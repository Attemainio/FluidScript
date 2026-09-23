using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Model.Contract;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Model;

/// <summary>Projects a bound, lowered and possibly solved model into the wire contract (<c>26</c>).</summary>
/// <remarks>
/// <para>
/// <strong>Never throws on user input.</strong> A value that cannot be expressed goes out as
/// <see langword="null"/> with <c>FS2501</c>; a payload past the cap goes out without states and with
/// <c>FS2502</c>; a state that cannot be read is absent. The script under editing is malformed most of
/// the time, and this runs on every debounce.
/// </para>
/// <para>
/// <strong>Values are rounded to six significant digits on the wire.</strong> A Newton solution
/// carries fifteen digits of which the last nine are noise below <c>36</c>'s tolerances; writing them
/// makes two runs of one script differ in the golden file and tells a reader nothing. Six is beyond
/// any instrument on the plant and is this project's choice, not a convention found elsewhere.
/// </para>
/// </remarks>
public static partial class ModelContractBuilder
{
    /// <summary>The version this builder implements.</summary>
    public const string ContractVersion = "2.2";

    /// <summary>The fluid property package and its exact version, as the provenance names it.</summary>
    public static VersionedId PropertyBackend { get; } = new("sharp-prop", FluidScript.Core.Physics.Fluids.PropertyBackend.PackageVersion);

    private const int SignificantDigits = 6;

    /// <summary>Builds the contract.</summary>
    /// <param name="input">What the pipeline produced.</param>
    /// <param name="statesOmitted">
    /// Whether to leave every state out and mark each circuit so. The size cap that decides this is
    /// measured on the serialized form, which Core cannot produce (<c>D-47</c>): the Api serializes,
    /// measures, and asks again with this set when the payload is over the cap, adding <c>FS2502</c>
    /// to <see cref="ModelContractInput.Diagnostics"/>.
    /// </param>
    /// <returns>The contract.</returns>
    public static ModelContract Build(ModelContractInput input, bool statesOmitted = false)
    {
        ArgumentNullException.ThrowIfNull(input);

        var graph = input.Run?.Graph ?? input.Graph;
        var (hints, layoutDiagnostics) = LayoutHintsDerivation.Derive(graph, input.Model, BranchFlows(input));

        return Build(input, hints, [.. input.Diagnostics, .. layoutDiagnostics], statesOmitted);
    }

    private static ModelContract Build(
        ModelContractInput input, LayoutHints hints, ImmutableArray<Diagnostic> diagnostics, bool statesOmitted)
    {
        var raised = ImmutableArray.CreateBuilder<Diagnostic>();
        var graph = input.Run?.Graph ?? input.Graph;
        var model = input.Model;
        var run = input.Run;
        var solved = run is not null && run.Solve.Converged;
        var layout = run is null ? null : SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);

        // A converged run has assembled this system once already; with it a discharging port reads
        // the component's own outlet rather than the mixed node it discharges into (C-103).
        var system = solved ? EquationSystem.Build(run!.Graph, WellPosedness.Check(run.Graph), run.Solve.Solution) : null;
        ImmutableArray<ImmutableArray<SolvedPort?>>? ports = run is null || layout is null || statesOmitted
            ? null
            : SolvedStates.Ports(run.Graph, layout, run.Solve.Solution, system);
        var registry = ComponentRegistry.Default;
        var symbols = model.Components.ToDictionary(static component => component.Name, StringComparer.Ordinal);
        var expansions = graph.Groups
            .SelectMany(group => group.Members.Select(member => (Member: member, Parent: group.Source)))
            .ToDictionary(static pair => pair.Member, static pair => pair.Parent, StringComparer.Ordinal);

        var components = ImmutableArray.CreateBuilder<ComponentWire>();

        for (var i = 0; i < graph.Components.Length; i++)
        {
            var component = graph.Components[i];
            symbols.TryGetValue(component.Name, out var symbol);
            var kind = symbol?.Kind ?? (registry.Resolve(component.Kind) as KindResolution.Exact)?.Kind;

            components.Add(new ComponentWire
            {
                Id = component.Name,
                Kind = component.Kind,
                Mode = component.Mode,
                SymbolId = SymbolCatalog.IdFor(component.Kind),
                Origin = OriginOf(symbol, expansions.ContainsKey(component.Name)),
                // A declared component's declaration, or an implicit pipe's connection line (C-97); null for what nobody wrote.
                SourceSpan = symbol?.DeclarationSpan is { } span ? new SpanWire(span.Start, span.Length) : null,
                Circuit = graph.CircuitOf.GetValueOrDefault(component.Name)
                    ?? symbol?.CircuitName
                    ?? (expansions.TryGetValue(component.Name, out var parent) && symbols.TryGetValue(parent, out var owner) ? owner.CircuitName : model.Circuits[0].Name),
                Tag = symbol?.Tag,
                Parameters = Parameters(component, kind, run, layout, raised),
                State = ports is null || layout is null || run is null
                    ? null
                    : State(graph, i, component, kind, ports.Value[i], layout, run.Solve.Solution, raised),
                Ports = Ports(graph, i, component),
            });
        }

        // Instruments and controllers are drawn but not lowered: they appear here so the canvas can
        // key them by id, and in `layout.nonFlowElements` rather than `layout.order`.
        foreach (var symbol in model.Components.Where(static component => component.Kind is { IsObserver: true } || component.Kind?.Keyword == "controller"))
        {
            components.Add(new ComponentWire
            {
                Id = symbol.Name,
                Kind = symbol.Kind!.Keyword,
                SymbolId = SymbolCatalog.IdFor(symbol.Kind.Keyword),
                Origin = OriginOf(symbol, expanded: false),
                SourceSpan = symbol.DeclarationSpan is { } span ? new SpanWire(span.Start, span.Length) : null,
                Circuit = symbol.CircuitName,
                Tag = symbol.Tag,
                Parameters = StatedOnly(symbol, raised),
                State = null,
                Ports = [],
            });
        }

        var connections = Connections(model, graph, hints, ports, raised);
        var circuits = Circuits(model, graph, hints, solved, statesOmitted);
        var scene = LayoutSolver.Solve(graph, model, hints, LayoutSolver.MarginOf(model));

        // The audit runs on every layout, not only on the ladder's fixtures (C-101): a hard breach of 28 B
        // is reported, one line per breach, so the picture never arrives as if it met the standard.
        foreach (var breach in SceneAudit.Findings(scene, model).Where(static finding => finding.Hard))
        {
            raised.Add(Diagnostic.Create(
                LayoutDiagnostics.LayoutBreach,
                span: null,
                new DiagnosticArgument("rule", breach.Kind),
                new DiagnosticArgument("first", breach.First),
                new DiagnosticArgument("second", breach.Second),
                new DiagnosticArgument("detail", breach.Detail)));
        }
        var styles = new Styles(model, graph);
        var scales = ColourScales.Resolve(input.Root, graph, ports, raised);
        var states = components.ToDictionary(static c => c.Id, static c => c.State, StringComparer.Ordinal);
        var all = Diagnostics(input.Source, [.. diagnostics, .. raised], [.. components]);

        return new ModelContract
        {
            ContractVersion = ContractVersion,
            Provenance = new Provenance
            {
                SourceHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.Source.Text))),
                LanguageMajor = input.Root.Version is { } version && !double.IsNaN(version.Major.Value) ? (int)version.Major.Value : 1,
                Catalog = new VersionedId(input.Catalog.Name, input.Catalog.Version),
                PropertyBackend = PropertyBackend,
                AtmosphereKPaAbsolute = UnitTable.StandardAtmosphere / 1000,
            },
            Project = model.Project.Name is null && model.Project.DefaultMode is null
                ? null
                : new ProjectWire(model.Project.Name, ModeName(model.Project.DefaultMode)),
            Style = new StyleWire(
                [.. model.Style.Tokens.Select(static token => token.Text)],
                model.Style.Spacing,
                Styles.Resolve(model.Style.Default),
                model.Style.Definitions.OrderBy(static d => d.Key, StringComparer.Ordinal).ToDictionary(static d => d.Key, static d => Styles.Resolve(d.Value), StringComparer.Ordinal)),
            Circuits = circuits,
            PressureDatums = [.. HydraulicPartition.Of(graph).Select(static part => part.Datum).Where(static datum => datum.Length > 0)],
            Components = components.ToImmutable(),
            Symbols = SymbolCatalog.All,
            Connections = connections,
            Layout = Layout(hints, scene, styles, scales),
            Visualization = scales.Wire,
            Bindings = [.. model.Bindings.Select(binding => BindingOf(binding, raised))],
            Diagnostics = all,
            Solve = run is null
                ? null
                : new SolveWire
                {
                    Converged = run.Solve.Converged,
                    Iterations = run.Iterations,
                    ResidualNorm = Round(run.Solve.ResidualNorm, significant: 3),
                    ElapsedMs = input.ElapsedMs,
                    SizingPasses = run.Passes,
                },
        };
    }

    // ---- values ---------------------------------------------------------------------------------------

    /// <summary>A value in its canonical unit, rounded, or <see langword="null"/> with <c>FS2501</c>.</summary>
    private static (double? Value, string? Unit) Canonical(
        double si, Dimension dimension, string component, string field, ImmutableArray<Diagnostic>.Builder raised)
    {
        // A dimension with no canonical spelling -- head, Kv, a designation -- goes out in its SI unit,
        // which is what the script's bare number meant for it.
        var unit = UnitTable.CanonicalUnitFor(dimension);
        var text = unit?.Text ?? (dimension.SiUnit.Length > 0 ? dimension.SiUnit : null);
        var value = unit is null ? si : Quantity.FromSi(si, dimension).ValueIn(unit);

        if (!double.IsFinite(value))
        {
            raised.Add(Diagnostic.Create(
                ContractDiagnostics.NotFinite,
                span: null,
                new DiagnosticArgument("component", component),
                new DiagnosticArgument("field", field),
                new DiagnosticArgument("value", value.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticArgument("unit", text ?? "a bare number")));

            return (null, text);
        }

        // Below a nanounit the value is the solver's noise around zero -- a gauge pressure of -1.7e-21
        // kPa at the datum -- and zero is what it means.
        return (Math.Abs(value) < 1e-9 ? 0 : Round(value), text);
    }

    private static QuantityWire Wire(double si, Dimension dimension, string component, string field, ImmutableArray<Diagnostic>.Builder raised)
    {
        var (value, unit) = Canonical(si, dimension, component, field, raised);
        return new QuantityWire(value, unit ?? string.Empty);
    }

    /// <summary>Six significant digits, and a clean zero.</summary>
    private static double Round(double value, int significant = SignificantDigits)
    {
        if (value == 0 || !double.IsFinite(value))
        {
            return value == 0 ? 0 : value;
        }

        // Through the decimal string and back: the double nearest "0.239213" is what the writer prints
        // as 0.239213 again, where a scaled Math.Round can land one ulp off it and print seventeen
        // digits. Math.Round's fifteen-decimal limit would not reach a residual norm of 1e-11 either.
        return double.Parse(value.ToString("G" + significant.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    // ---- components -----------------------------------------------------------------------------------

    private static string OriginOf(ComponentSymbol? symbol, bool expanded) => symbol?.Origin switch
    {
        FluidScript.Core.Language.Binding.Origin.Declared => "declared",
        FluidScript.Core.Language.Binding.Origin.Inferred inferred => "inferred:" + inferred.Rule,
        _ => expanded ? "expanded" : "inferred",
    };

    private static string? ModeName(FluidMode? mode) => mode switch
    {
        FluidMode.Static => "steady",
        FluidMode.Dynamic => "transient",
        _ => null,
    };

    private static Dictionary<string, TOut> Ordered<TIn, TOut>(IEnumerable<KeyValuePair<string, TIn>> pairs, Func<TIn, TOut> map) =>
        pairs.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, pair => map(pair.Value), StringComparer.Ordinal);


    private static ImmutableArray<double>? BranchFlows(ModelContractInput input)
    {
        if (input.Run is not { } run || !run.Solve.Converged)
        {
            return null;
        }

        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        var values = run.Solve.Solution.Values;

        return [.. Enumerable.Range(0, run.Graph.Branches.Length).Select(i => layout.BranchFlow(i) < values.Length ? values[layout.BranchFlow(i)] : 0)];
    }
}
