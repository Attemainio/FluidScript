namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>
/// A second ring that shares one element with a fragment's body (<c>28</c> E2, <c>D-157</c>): a loop leaving that
/// element by one port and returning by another -- a buffer tank's discharging loop, which the tank's charging loop
/// does not reach. It is read as a ring cut at the shared element, as a sourced ring is cut at its source, and laid
/// from that element once the body has placed it.
/// </summary>
/// <param name="At">The shared element.</param>
/// <param name="OutPort">The port the ring leaves it by.</param>
/// <param name="InPort">The port the ring returns to it by.</param>
/// <param name="Body">The ring between the two ports, its terminals the plan's virtual vertices.</param>
internal sealed record AttachedRing(int At, int OutPort, int InPort, Structure Body);
