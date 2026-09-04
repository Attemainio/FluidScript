using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Solvers;

/// <summary>Where one component's residuals land in the system's residual vector.</summary>
/// <param name="Component">The component's index in <see cref="CircuitGraph.Components"/>.</param>
/// <param name="FirstRow">The row its first kept residual occupies.</param>
/// <param name="LocalCount">How many residuals it writes, which is its <c>EquationCount</c>.</param>
/// <param name="DroppedLocal">
/// The local residual index the assembler discards, or <c>-1</c> when it keeps all of them. At most one
/// per component, and always a mass balance.
/// </param>
/// <remarks>
/// <strong>The component still writes every residual it declares; the assembler decides what to keep.</strong>
/// Asking a component to write one fewer residual when it happens to be the redundant one would make its
/// <c>EquationCount</c> depend on the partition it sits in, which nothing in
/// <see cref="IFlowComponent"/>'s contract lets it see.
/// </remarks>
public readonly record struct ComponentRows(int Component, int FirstRow, int LocalCount, int DroppedLocal)
{
    /// <summary>Gets whether one of this component's residuals is dropped as redundant.</summary>
    public bool HasDrop => DroppedLocal >= 0;

    /// <summary>Gets how many rows this component actually occupies.</summary>
    public int RowCount => HasDrop ? LocalCount - 1 : LocalCount;

    /// <summary>Finds the system row one of this component's residuals writes to.</summary>
    /// <param name="local">The residual's index within the component, as it writes them.</param>
    /// <returns>The row, or <c>-1</c> for the residual the assembler discards.</returns>
    public int Row(int local) =>
        local == DroppedLocal ? -1
            : FirstRow + (HasDrop && local > DroppedLocal ? local - 1 : local);
}

/// <summary>One mass balance the assembler dropped because the rest of its component imply it.</summary>
/// <param name="Hydraulic">The hydraulic component whose balances are redundant.</param>
/// <param name="Component">The element whose balance was dropped.</param>
/// <param name="Equation">The name the dropped row would have carried.</param>
public sealed record DroppedBalance(int Hydraulic, string Component, string Equation);

/// <summary>Which equation each row of the residual vector is, and which component owns it.</summary>
/// <remarks>
/// <para>
/// <strong>This is the other half of <see cref="SystemLayout"/>, and it is built the same way</strong>: by
/// walking the graph, then holding the total against <see cref="CountingTable.Equations"/>. The table is
/// the same number computed a second way, and the agreement is only worth something because the two are
/// derived independently (<c>S-9</c>).
/// </para>
/// <para>
/// <strong>The row-to-component mapping exists only here</strong> (<c>31</c>, <c>S-7</c>). A residual is a
/// number until something can say which equation it is; <see cref="EquationDeclaration"/> carries the name
/// and the unit, and this assigns the index that connects it to a position in the vector. If this layer
/// does not carry it, no later one can reconstruct it.
/// </para>
/// <para>
/// <strong>Component rows come first, then the rows assembly owns.</strong> A stated pressure, a datum and
/// a promotion pairing are not any component's equation — they are the script's, and grouping them at the
/// end keeps the component block's structure the same whether or not the user stated a boundary.
/// </para>
/// </remarks>
public sealed class EquationLayout
{
    private EquationLayout(
        ImmutableArray<EquationDeclaration> rows,
        ImmutableArray<ComponentRows> components,
        ImmutableArray<DroppedBalance> dropped,
        int linkOffset,
        int boundaryOffset,
        int datumOffset,
        int constraintOffset)
    {
        Rows = rows;
        Components = components;
        Dropped = dropped;
        LinkOffset = linkOffset;
        BoundaryOffset = boundaryOffset;
        DatumOffset = datumOffset;
        ConstraintOffset = constraintOffset;
    }

    /// <summary>Gets every equation, in residual order, each carrying its own row index.</summary>
    public ImmutableArray<EquationDeclaration> Rows { get; }

    /// <summary>Gets where each component's residuals land, indexed as the graph's components are.</summary>
    public ImmutableArray<ComponentRows> Components { get; }

    /// <summary>Gets the mass balances dropped as redundant, one per closed hydraulic component.</summary>
    public ImmutableArray<DroppedBalance> Dropped { get; }

    /// <summary>Gets the index of the first ideal-link row.</summary>
    /// <value>
    /// <c>D-25</c>'s zero-drop connection, and the one row family no component declares: a bare
    /// <c>A - B</c> between two nodes puts nothing in the branch's path, so the assembler writes
    /// <c>p_A = p_B</c> itself from <see cref="CountingTable.IdealLinks"/>.
    /// </value>
    public int LinkOffset { get; }

    /// <summary>Gets the index of the first stated-pressure row.</summary>
    public int BoundaryOffset { get; }

    /// <summary>Gets the index of the first pressure-datum row.</summary>
    public int DatumOffset { get; }

    /// <summary>Gets the index of the first promotion-pairing row.</summary>
    public int ConstraintOffset { get; }

    /// <summary>Gets how many equations the system has.</summary>
    public int Count => Rows.Length;

    /// <summary>Lays out the equation rows of a graph.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="posedness">
    /// The well-posedness result, which supplies both the counting table and the hydraulic partition. They
    /// are taken together rather than separately because a table from one graph and a partition from
    /// another would produce a layout that reconciles against nothing.
    /// </param>
    /// <returns>The layout.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Exactly one mass balance is dropped per closed hydraulic component, and which one is a
    /// stability decision rather than a numerical one.</strong> With every external flux known, summing a
    /// component's balances gives an identity, so one of them is implied by the rest and any one may go.
    /// The first element in graph order is chosen because graph order is declaration order: appending a
    /// component to a script leaves the choice where it was, while dropping the last would move it and
    /// change the Jacobian's structure for an edit that changed nothing.
    /// </para>
    /// <para>
    /// <strong>The redundancy is found from the declarations, not recomputed from the rule.</strong> A
    /// component is a candidate here because it declared a mass row, which is the same fact
    /// <c>WellPosedness</c>'s own count is built on. Re-deriving "which elements carry a balance" would be
    /// a second implementation of a rule that has already caught one defect by being stated once.
    /// </para>
    /// </remarks>
    public static EquationLayout Build(CircuitGraph graph, WellPosednessResult posedness)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(posedness);

        var counting = posedness.Counting;

        // Keyed by object with a reference comparer: a hydraulic component's elements are the same
        // instances the graph holds, and a value-equal pair of identical components must not collide.
        var indices = new Dictionary<object, int>(graph.Components.Length, ReferenceEqualityComparer.Instance);

        for (var index = 0; index < graph.Components.Length; index++)
        {
            indices[graph.Components[index]] = index;
        }

        var drops = Redundancies(posedness, indices, out var dropped);

        var rows = ImmutableArray.CreateBuilder<EquationDeclaration>(counting.Equations);
        var components = ImmutableArray.CreateBuilder<ComponentRows>(graph.Components.Length);

        for (var index = 0; index < graph.Components.Length; index++)
        {
            var component = graph.Components[index];
            var declarations = component.DeclareEquations();
            var drop = drops.TryGetValue(index, out var local) ? local : -1;

            components.Add(new ComponentRows(index, rows.Count, declarations.Length, drop));

            for (var position = 0; position < declarations.Length; position++)
            {
                if (position != drop)
                {
                    rows.Add(declarations[position] with { Index = rows.Count });
                }
            }
        }

        var links = rows.Count;

        foreach (var link in counting.IdealLinks)
        {
            rows.Add(new EquationDeclaration(
                rows.Count,
                EquationKind.Pressure,
                link.From.Name,
                $"{link.From.Name} = {link.To.Name}, ideal link",
                "Pa"));
        }

        var boundaries = rows.Count;

        foreach (var node in counting.PressureNodes)
        {
            rows.Add(new EquationDeclaration(
                rows.Count, EquationKind.Boundary, node.Name, $"{node.Name} stated pressure", "Pa"));
        }

        var datums = rows.Count;

        foreach (var hydraulic in counting.DatumComponents)
        {
            rows.Add(new EquationDeclaration(
                rows.Count, EquationKind.Boundary, hydraulic.Datum, $"{hydraulic.Datum} pressure datum", "Pa"));
        }

        var constraints = rows.Count;

        foreach (var constraint in counting.Constraints)
        {
            rows.Add(new EquationDeclaration(
                rows.Count,
                EquationKind.ComponentConstraint,
                constraint.Component,
                $"{constraint.Label} stated",
                Residual(constraint.Kind)));
        }

        return new EquationLayout(
            rows.ToImmutable(), components.ToImmutable(), dropped, links, boundaries, datums, constraints);
    }

    /// <summary>Finds the state-vector row a component's residual writes to.</summary>
    /// <param name="component">The component's index in the graph.</param>
    /// <param name="local">The residual's index within that component.</param>
    /// <returns>The row, or <c>-1</c> when the assembler discards that residual.</returns>
    public int Row(int component, int local) => Components[component].Row(local);

    /// <summary>The SI unit a stated constraint's residual is measured in.</summary>
    /// <param name="kind">What the constraint asks the circuit to do.</param>
    /// <returns>The unit, for a message that names a miss in the reader's own terms.</returns>
    private static string Residual(ConstraintKind kind) => kind switch
    {
        ConstraintKind.FixedFlow => "kg/s",
        _ => "K",
    };

    /// <summary>Chooses the mass balance to drop in each closed hydraulic component.</summary>
    /// <param name="posedness">The partition and its counting table.</param>
    /// <param name="indices">Each component's position in the graph, by reference.</param>
    /// <param name="dropped">Receives one record per drop, for reporting.</param>
    /// <returns>Component index to the local residual index dropped there.</returns>
    private static Dictionary<int, int> Redundancies(
        WellPosednessResult posedness,
        Dictionary<object, int> indices,
        out ImmutableArray<DroppedBalance> dropped)
    {
        var drops = new Dictionary<int, int>();
        var records = ImmutableArray.CreateBuilder<DroppedBalance>();

        foreach (var hydraulic in posedness.Hydraulics)
        {
            if (hydraulic.HasUnknownFlux)
            {
                continue;
            }

            foreach (var element in hydraulic.Elements)
            {
                if (!indices.TryGetValue(element, out var index) || drops.ContainsKey(index))
                {
                    continue;
                }

                var declarations = element.DeclareEquations();
                var local = -1;

                for (var position = 0; position < declarations.Length; position++)
                {
                    if (declarations[position].Kind == EquationKind.Mass)
                    {
                        local = position;
                        break;
                    }
                }

                if (local < 0)
                {
                    continue;
                }

                drops[index] = local;
                records.Add(new DroppedBalance(
                    hydraulic.Index, element.Name, declarations[local].Name));

                break;
            }
        }

        dropped = records.ToImmutable();

        return drops;
    }
}
