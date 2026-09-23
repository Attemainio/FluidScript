using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Components;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Sizing.Sizers;

/// <summary>Chooses a pipe's nominal diameter from a catalogue (<c>24</c>).</summary>
/// <param name="catalog">The series to select from.</param>
/// <param name="gradientTarget">Pa/m the selection aims at. Defaults to <see cref="SizingDefaults.PipeGradientTarget"/>.</param>
/// <param name="available">Every shipped catalogue by id, for a pipe whose own <c>material</c> names one (<c>C-36</c>); <see langword="null"/> sizes every pipe from <paramref name="catalog"/>.</param>
/// <remarks>
/// <para>
/// <strong>The gradient is computed by the same code the residual uses, never transcribed.</strong>
/// Each candidate is a one-metre <see cref="Pipe"/> of that bore with no minor losses, and its
/// <see cref="Pipe.PressureDrop"/> is the gradient — so the sizer and the solver cannot drift apart
/// about friction, which is what <c>24</c>'s acceptance criterion asks for. It also means Serghide's
/// approximation, the laminar blend and the roughness all arrive automatically rather than being
/// re-derived here.
/// </para>
/// <para>
/// <strong>The three bounds have a precedence, because two of them disagree in the common case</strong>
/// (<c>C-48</c>). The velocity ceiling is hard and steps up; the gradient is a target and yields; the
/// velocity floor is soft, steps down, and never breaches the ceiling. Without that ordering the rule
/// selects sizes it knows will make validation complain.
/// </para>
/// <para>
/// <c>dn</c> is a designation and not a diameter, so this rule reports the designation and sizes on
/// <see cref="PipeSpec.InsideDiameter"/>. DN25 steel has a 27.3 mm bore; an area computed from 25 mm
/// is 16 % small and the gradient about a factor of two out.
/// </para>
/// </remarks>
public sealed class PipeSizer(
    ICatalog<PipeSpec> catalog,
    double gradientTarget = SizingDefaults.PipeGradientTarget,
    IReadOnlyDictionary<string, ICatalog<PipeSpec>>? available = null) : ISizer
{
    /// <inheritdoc/>
    public ImmutableArray<string> Parameters { get; } = ["dn"];

    /// <inheritdoc/>
    /// <remarks>
    /// The smallest size in the series. A pipe is the one kind lowering cannot build without a chosen
    /// value, so this is what makes a first graph exist; it is replaced before anything is solved. An
    /// empty catalogue -- refused by the catalogue gate before a sizer is ever built, and guarded here
    /// as <see cref="ValveSizer"/> guards its own row -- yields DN 0, which no rule accepts.
    /// </remarks>
    public ImmutableDictionary<string, Quantity> Provisional { get; } =
        ImmutableDictionary<string, Quantity>.Empty.Add(
            "dn",
            Quantity.FromSi(
                catalog is { Entries.Count: > 0 } ? catalog.Entries[0].Spec.NominalDiameter : 0,
                Dimension.NominalDiameter));

    /// <inheritdoc/>
    public bool CanSize(IFlowComponent component) => component is Pipe;

    /// <inheritdoc/>
    public Result<SizingResult> Size(IFlowComponent component, in SizingContext context)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component is not Pipe pipe)
        {
            return Result.Failure<SizingResult>(ResultError.From(
                FluidScript.Core.Diagnostics.Descriptors.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a diameter"),
                ("name", component.Name),
                ("state", "a component that is not a pipe")));
        }

        var density = context.State.Density.SiValue;
        var viscosity = context.State.DynamicViscosity.SiValue;

        // A flow of zero has no gradient to select against, and a rule that answered anyway would be
        // inventing a size from nothing. `24`'s `FS2304` is the eventual home for this.
        if (!double.IsFinite(density) || !double.IsFinite(viscosity) || density <= 0
            || Math.Abs(context.MassFlow) <= 0)
        {
            return Result.Failure<SizingResult>(ResultError.From(
                FluidScript.Core.Diagnostics.Descriptors.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a diameter"),
                ("name", pipe.Name),
                ("state", "no flow is determined in this branch")));
        }

        var volumeFlow = context.VolumeFlow(density);

        // A pipe's own `material` selects its series (C-36); the factory refused to build one whose
        // catalogue fails provenance, so a pipe that reaches here names a usable one or none.
        var series = pipe.Material is { } material
            && available is not null
            && available.TryGetValue(material, out var own)
                ? own
                : catalog;
        var entries = series.Entries;
        var notes = ImmutableArray.CreateBuilder<string>();
        var raised = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();

        // A note and its code say the same thing (C-74): the note for the explanation, in order; the
        // code for the wire, anchored to the pipe.
        void Say(Diagnostics.DiagnosticDescriptor descriptor, params Diagnostics.DiagnosticArgument[] arguments)
        {
            var diagnostic = Diagnostics.Diagnostic.Create(descriptor, span: null, arguments) with { ComponentName = pipe.Name };
            raised.Add(diagnostic);
            notes.Add(diagnostic.Message);
        }

        // Step 1: the smallest size meeting the gradient target, or the largest there is.
        var index = 0;

        while (index < entries.Count - 1 && Gradient(entries[index], volumeFlow, density, viscosity) > gradientTarget)
        {
            index++;
        }

        if (Gradient(entries[index], volumeFlow, density, viscosity) > gradientTarget)
        {
            Say(
                FluidScript.Core.Diagnostics.Descriptors.SizingDiagnostics.OutsideCatalogue,
                new Diagnostics.DiagnosticArgument("name", pipe.Name),
                new Diagnostics.DiagnosticArgument("max", entries[index].Spec.NominalDiameter.ToString(CultureInfo.InvariantCulture)),
                new Diagnostics.DiagnosticArgument("catalog", series.Name));
        }

        // Step 2: the velocity ceiling, which is hard.
        while (index < entries.Count - 1 && Velocity(entries[index], volumeFlow) > Ceiling(entries[index]))
        {
            index++;
            Say(
                FluidScript.Core.Diagnostics.Descriptors.SizingDiagnostics.SteppedUpForVelocity,
                new Diagnostics.DiagnosticArgument("name", pipe.Name),
                new Diagnostics.DiagnosticArgument("n", entries[index].Spec.NominalDiameter.ToString(CultureInfo.InvariantCulture)));
        }

        // Step 3: the velocity floor, which is soft and may not breach the ceiling.
        while (index > 0
            && Velocity(entries[index], volumeFlow) < SizingDefaults.VelocityMinimum
            && Velocity(entries[index - 1], volumeFlow) <= Ceiling(entries[index - 1]))
        {
            index--;
            notes.Add($"{pipe.Name} stepped down to DN{entries[index].Spec.NominalDiameter} for velocity.");
        }

        var chosen = entries[index];
        var velocity = Velocity(chosen, volumeFlow);
        var gradient = Gradient(chosen, volumeFlow, density, viscosity);

        // Step 2 can only step up while there is somewhere to step, so the top of the series is where a
        // hard bound stops being enforceable. Reporting the gradient miss and not this one would hand
        // back a size that is over the noise limit with nothing said about it -- and velocity is the
        // bound a user actually hears.
        if (velocity > Ceiling(chosen))
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{pipe.Name} runs at {velocity:0.##} m/s in DN{chosen.Spec.NominalDiameter}, above the "
                + $"{Ceiling(chosen):0.#} m/s limit, and there is no larger size in {series.Name}."));
        }

        return Result.Success(new SizingResult
        {
            Values = ImmutableDictionary<string, SizedValue>.Empty.Add(
                "dn",
                new SizedValue(
                    Quantity.FromSi(chosen.Spec.NominalDiameter, Dimension.NominalDiameter),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"DN{chosen.Spec.NominalDiameter} ({chosen.Spec.Series}) — {gradient:0.#} Pa/m, {velocity:0.##} m/s"),
                    FromDefault: false)),
            Notes = notes.ToImmutable(),
            Diagnostics = raised.ToImmutable(),
        });
    }

    private static double Area(CatalogEntry<PipeSpec> entry) =>
        Math.PI * entry.Spec.InsideDiameter * entry.Spec.InsideDiameter / 4;

    private static double Velocity(CatalogEntry<PipeSpec> entry, double volumeFlow) =>
        volumeFlow / Area(entry);

    private static double Ceiling(CatalogEntry<PipeSpec> entry) =>
        SizingDefaults.VelocityMaximum(entry.Spec.NominalDiameter);

    /// <summary>The pressure gradient a candidate would run at.</summary>
    /// <param name="entry">The candidate size.</param>
    /// <param name="volumeFlow">m³/s.</param>
    /// <param name="density">kg/m³.</param>
    /// <param name="viscosity">Pa·s.</param>
    /// <returns>Pa/m, from the component's own friction model over a one-metre length.</returns>
    private static double Gradient(
        CatalogEntry<PipeSpec> entry, double volumeFlow, double density, double viscosity) =>
        new Pipe("probe", 1, entry.Spec.InsideDiameter, entry.Spec.Roughness)
            .PressureDrop(Velocity(entry, volumeFlow), density, viscosity);
}
