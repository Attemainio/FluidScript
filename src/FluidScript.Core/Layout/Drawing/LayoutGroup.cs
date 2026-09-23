using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Drawing;

/// <summary>A group the solver laid out as one object (<c>28</c> §24–25): its members were placed together and the parent only moved them.</summary>
/// <param name="Id">A stable name: <c>loop-1</c>, <c>header-1</c>, <c>chain-3</c>.</param>
/// <param name="Kind"><c>loop</c>, <c>header</c>, <c>chain</c> or <c>fragment</c>.</param>
/// <param name="Orientation"><c>cw</c> or <c>ccw</c> for a loop; the flow direction for a chain; empty otherwise.</param>
/// <param name="Members">The component ids, in the group's own order.</param>
/// <param name="Bounds">The union of the members' inner boxes and the group's own routes.</param>
public sealed record LayoutGroup(string Id, string Kind, string Orientation, ImmutableArray<string> Members, Box Bounds);
