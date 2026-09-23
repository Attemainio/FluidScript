using FluidScript.Core.Units;

namespace FluidScript.Core.Topology;

/// <summary>A <c>control</c> line's setpoint as the design solve sees it (<c>D-141</c>).</summary>
/// <param name="Controller">The controller the line names with <c>by=</c>.</param>
/// <param name="Measured">The component whose property the loop reads: <c>N2</c> for <c>N2.t</c>.</param>
/// <param name="Parameter">The measured property's parameter key on that component: <c>t</c>.</param>
/// <param name="ActuatorComponent">The component the loop drives: <c>3WV</c> for <c>3WV.position</c>.</param>
/// <param name="ActuatorParameter">The parameter it moves: <c>position</c>.</param>
/// <param name="Value">The setpoint, SI, in the measurement's dimension.</param>
/// <param name="Applied">
/// Whether lowering wrote the setpoint into the measured component's stated parameters, so that it is
/// a constraint of the design solve promoting the actuator. <see langword="false"/> when the actuator
/// is stated (the run starts off setpoint, <c>FS3210</c>), when the measured component states the
/// property itself, when a neighbouring terminal already fixes it, or when the measurement is not one
/// the design solve can hold (<c>FS3211</c>).
/// </param>
/// <param name="Reason">
/// Why it was not applied, in the words <c>FS3210</c> prints; <see langword="null"/> when it was, and
/// when the measurement is not holdable at all, which is <c>FS3211</c>'s sentence rather than a reason.
/// </param>
/// <remarks>
/// <para>
/// <strong>The design point of a controlled loop is its setpoint.</strong> The demand-step loop states
/// <c>HE1 out.t=50</c>, no <c>in.t</c> and no valve position; without this the valve defaults to fully
/// open, the exchanger's outlet promotion drives the pump to 62 m, and the t = 0 solve goes non-finite
/// (<c>S-75</c>). With it <c>N2.t = 20</c> is a node-temperature constraint answered by the valve the
/// line names, exactly as <c>N2 node t=20</c> would be, and the run starts at the controlled equilibrium
/// with the controller initialized bumplessly at the position the design solve chose.
/// </para>
/// <para>
/// The graph carries every binding with a setpoint, applied or not, because well-posedness runs on
/// the graph alone and is where the two informational codes are raised.
/// </para>
/// </remarks>
public sealed record Setpoint(
    string Controller,
    string Measured,
    string Parameter,
    string ActuatorComponent,
    string ActuatorParameter,
    Quantity Value,
    bool Applied,
    string? Reason = null);
