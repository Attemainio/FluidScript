namespace FluidScript.Core.Components.Valves;

/// <summary>Which service a three-way valve's script declares it built for, if it says (<c>D-136</c>).</summary>
/// <remarks>
/// The arrangement a valve <em>runs</em> in comes from the solved flow directions -- both switched legs
/// entering is mixing, both leaving is diverting -- and a bare <c>three_way_valve</c> claims nothing
/// about it. Written as <c>mixing_valve</c> or <c>diverting_valve</c> the script names the body it
/// intends to buy, and a seat valve is built for one service: Siemens' VXG44 is "to be used only as a
/// mixing valve", and the plug geometry of a body used the wrong way round defeats its fail-safe
/// position. <c>FS4012</c> is raised after the solve when the two disagree (<c>C-65</c>).
/// </remarks>
public enum ValveArrangement
{
    /// <summary>Written as <c>three_way_valve</c> or one of its neutral spellings: no claim.</summary>
    Unspecified,

    /// <summary>Written as <c>mixing_valve</c>: two streams in at <c>a</c> and <c>b</c>, one out at <c>ab</c>.</summary>
    Mixing,

    /// <summary>Written as <c>diverting_valve</c>: one stream in at <c>ab</c>, split between <c>a</c> and <c>b</c>.</summary>
    Diverting,
}
