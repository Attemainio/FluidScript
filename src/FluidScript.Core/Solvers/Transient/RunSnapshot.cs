using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Topology.Construction;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Transient;

/// <summary>Everything a run reads, frozen before its first step (<c>D-22</c>, <c>33</c>).</summary>
/// <remarks>
/// <para>
/// Built once from the design solve and never from a draft: the graph, the system, the design state,
/// the initial differential states, the schedule, the setpoints and the settings are all copies or
/// immutable, and no object reachable from here is one the editor can change. An edit during a run
/// produces a new snapshot for the next run, never a change to this one.
/// </para>
/// <para>
/// <strong>The initial state is the design state</strong> (<c>D-141</c>): every algebraic unknown,
/// every size and every promotion as the design solve left them. A pipe cell starts at the enthalpy
/// the steady solve gave its node; a tank layer starts at the profile the script states, <c>t</c> for
/// every layer or <c>t1</c>…<c>tN</c> per layer, else at the design solve's mixed enthalpy. An explicit
/// profile is a non-equilibrium initial condition and may evolve at t = 0 without a schedule
/// (<c>33</c> invariant 3).
/// </para>
/// </remarks>
public sealed record RunSnapshot
{
    /// <summary>The run's identity: the same script, versions and settings produce the same id.</summary>
    /// <value><c>sha256:</c> and 64 hex digits, over the source hash, every version and the settings.</value>
    public required string SnapshotId { get; init; }

    /// <summary>The hash of the source the snapshot was built from, as the caller computed it.</summary>
    public required string SourceHash { get; init; }

    /// <summary>The versions that shaped the model, by name: language, catalogue, property backend, contract.</summary>
    public required ImmutableSortedDictionary<string, string> Versions { get; init; }

    /// <summary>The lowered graph the run integrates, with its sizes applied.</summary>
    public required CircuitGraph Graph { get; init; }

    /// <summary>The assembled system, in its equilibrium form; the run pins it per step.</summary>
    public required EquationSystem System { get; init; }

    /// <summary>The counting table and the partition the system was assembled against.</summary>
    public required WellPosednessResult Posedness { get; init; }

    /// <summary>The design state: every unknown as the design solve left it.</summary>
    public required StateVector Initial { get; init; }

    /// <summary>The initial value of every differential state, in <see cref="SystemLayout.Differential"/> order.</summary>
    /// <value>J/kg.</value>
    public required ImmutableArray<double> DifferentialInitial { get; init; }

    /// <summary>The reference mass of every differential state, in <see cref="SystemLayout.Differential"/> order.</summary>
    /// <value>kg: the state's volume at the density of its initial state. Fixed for the run (<c>33</c> invariant 11), which is the incompressible approximation that makes mass conservation exact.</value>
    public required ImmutableArray<double> ReferenceMasses { get; init; }

    /// <summary>The design value of every promotion, in <c>CountingTable.Promotions</c> order.</summary>
    /// <value>Each in its parameter's own SI unit. What <see cref="EquationSystem.Freeze"/> holds at t &gt; 0 until a controller or a schedule moves it (<c>D-140</c>).</value>
    public required ImmutableArray<double> PromotionInitial { get; init; }

    /// <summary>The schedule, in start-time order.</summary>
    public required ImmutableArray<ScheduledChange> Schedule { get; init; }

    /// <summary>Every control line's setpoint, applied to the design solve or not.</summary>
    public required ImmutableArray<Setpoint> Setpoints { get; init; }

    /// <summary>How long, how often a frame, and the integrator's bounds.</summary>
    public required TransientSettings Settings { get; init; }

    /// <summary>Builds the snapshot of a design solve.</summary>
    /// <param name="graph">The lowered graph the design solve converged on, sizes applied.</param>
    /// <param name="posedness">Its counting table and partition.</param>
    /// <param name="design">The converged design state.</param>
    /// <param name="settings">The run's settings.</param>
    /// <param name="sourceHash">The source's hash, as the caller computes it.</param>
    /// <param name="versions">The versions that shaped the model, by name.</param>
    /// <returns>The snapshot, or the reason a tank's stated profile could not be evaluated (<c>FS3108</c>'s case).</returns>
    public static Result<RunSnapshot> Create(
        CircuitGraph graph,
        WellPosednessResult posedness,
        StateVector design,
        TransientSettings settings,
        string sourceHash,
        IReadOnlyDictionary<string, string> versions)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(posedness);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sourceHash);
        ArgumentNullException.ThrowIfNull(versions);

        var system = EquationSystem.Build(graph, posedness, design);
        var layout = system.Unknowns;
        var differential = ImmutableArray.CreateBuilder<double>(layout.Differential.Length);
        var masses = ImmutableArray.CreateBuilder<double>(layout.Differential.Length);

        // One evaluation at the design state, so each cell's node carries the density its reference
        // mass is fixed from.
        system.TryEvaluateResiduals(design.Values.AsSpan(), new double[system.Rows]);

        foreach (var state in layout.Differential)
        {
            if (state.Column >= 0)
            {
                differential.Add(design.Values[state.Column]);
                masses.Add(state.Volume * system.NodeDensity(state.Column - layout.NodeEnthalpyOffset));
                continue;
            }

            var initial = LayerInitial(graph, layout, design, state);

            if (!initial.IsSuccess)
            {
                return Result.Failure<RunSnapshot>(initial.Error);
            }

            if (!double.IsFinite(initial.Value))
            {
                return Result.Failure<RunSnapshot>(CannotInitialize(state, Enthalpy(initial.Value)));
            }

            var layer = graph.Substance.FromPressureEnthalpy(
                Quantity.FromSi(Math.Max(PortPressure(graph, layout, design, state.Element), 0), Dimension.Pressure),
                Quantity.FromSi(initial.Value, Dimension.Enthalpy));

            if (!layer.IsSuccess)
            {
                return Result.Failure<RunSnapshot>(CannotInitialize(state, Enthalpy(initial.Value)));
            }

            differential.Add(initial.Value);
            masses.Add(state.Volume * layer.Value.Density.SiValue);
        }

        var promotions = ImmutableArray.CreateBuilder<double>(posedness.Counting.Promotions.Length);

        for (var index = 0; index < posedness.Counting.Promotions.Length; index++)
        {
            promotions.Add(design.Values[layout.PromotionOffset + index]);
        }

        var sorted = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, versions);

        return Result.Success(new RunSnapshot
        {
            SnapshotId = Identity(sourceHash, sorted, settings),
            SourceHash = sourceHash,
            Versions = sorted,
            Graph = graph,
            System = system,
            Posedness = posedness,
            Initial = new StateVector([.. design.Values]),
            DifferentialInitial = differential.ToImmutable(),
            ReferenceMasses = masses.ToImmutable(),
            PromotionInitial = promotions.ToImmutable(),
            Schedule = [.. graph.Schedule.OrderBy(static change => change.From).ThenBy(static change => change.To)],
            Setpoints = graph.Setpoints,
            Settings = settings,
        });
    }

    /// <summary>A tank layer's initial enthalpy: the stated profile, else the design solve's mixed value.</summary>
    private static Result<double> LayerInitial(CircuitGraph graph, SystemLayout layout, StateVector design, DifferentialState state)
    {
        if (graph.Components[state.Element] is not TankComponent tank)
        {
            return Result.Success(0.0);
        }

        Quantity? stated = tank.StatedParameters.TryGetValue("t" + state.Layer.ToString(CultureInfo.InvariantCulture), out var layer)
            ? layer
            : tank.StatedParameters.TryGetValue("t", out var uniform) ? uniform : null;

        if (stated is null)
        {
            // The mixed unknown is the tank's first own unknown; its column is where the layout put it.
            var mixed = layout.Unknowns.FirstOrDefault(unknown =>
                unknown.Kind == UnknownKind.NodeEnthalpy
                && string.Equals(unknown.OwnerComponentId, tank.Name, StringComparison.Ordinal)
                && unknown.Index >= layout.ComponentUnknownOffset);

            return Result.Success(mixed is null ? 0.0 : design.Values[mixed.Index]);
        }

        var fluid = graph.Substance.FromPressureTemperature(
            Quantity.FromSi(Math.Max(PortPressure(graph, layout, design, state.Element), 0), Dimension.Pressure),
            Quantity.FromSi(stated.Value.SiValue, Dimension.Temperature));

        // `FS3108` rather than the backend's own range message: the user wrote a temperature on a
        // layer, so the answer names that layer and that temperature.
        return fluid.IsSuccess
            ? Result.Success(fluid.Value.Enthalpy.SiValue)
            : Result.Failure<double>(CannotInitialize(
                state,
                (stated.Value.SiValue - 273.15).ToString("0.#", CultureInfo.InvariantCulture) + " °C"));
    }

    /// <summary><c>FS3108</c>: a tank's stated layer cannot be evaluated inside the supported property domain.</summary>
    /// <param name="state">The layer.</param>
    /// <param name="described">What it was asked for, written the way the script wrote it.</param>
    /// <returns>The error, naming the tank, the layer and the state it was asked for.</returns>
    private static ResultError CannotInitialize(DifferentialState state, string described) => new(
        TransientDiagnostics.CannotInitializeLayer,
        [
            new DiagnosticArgument("tank", state.Owner),
            new DiagnosticArgument("layer", state.Layer.ToString(CultureInfo.InvariantCulture)),
            new DiagnosticArgument("state", described),
        ]);

    /// <summary>An enthalpy as a message writes it.</summary>
    private static string Enthalpy(double value) =>
        (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " kJ/kg";

    /// <summary>The pressure of the node on a tank's first port: what every layer shares to within the tank's head, which water's properties barely notice.</summary>
    private static double PortPressure(CircuitGraph graph, SystemLayout layout, StateVector design, int element)
    {
        var peer = graph.Adjacency.Peer(element, 0);

        if (!peer.Exists)
        {
            return 0;
        }

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            if (ReferenceEquals(graph.Components[peer.Component], graph.Nodes[node].Component))
            {
                return design.Values[layout.NodePressure(node)];
            }
        }

        return 0;
    }

    /// <summary>The snapshot id: one hash over what the run reads and nothing the editor can change.</summary>
    private static string Identity(string sourceHash, ImmutableSortedDictionary<string, string> versions, TransientSettings settings)
    {
        var text = new StringBuilder(sourceHash);

        foreach (var (name, version) in versions)
        {
            text.Append('\n').Append(name).Append('=').Append(version);
        }

        text.Append(CultureInfo.InvariantCulture, $"\nhorizon={settings.Horizon:R}\ninterval={settings.FrameInterval:R}")
            .Append(CultureInfo.InvariantCulture, $"\nmax_step={settings.MaxStep:R}\nmin_step={settings.MinStep:R}")
            .Append(CultureInfo.InvariantCulture, $"\ncfl={settings.CflSafety:R}\nlocal_error={settings.LocalErrorTolerance:R}");

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
