namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One end of a connection: a component and the port it claimed.</summary>
/// <param name="Component">The component's name.</param>
/// <param name="Port">
/// The port's name, empty for a component whose ports are unlimited and unnamed — which is only
/// <c>node</c>.
/// </param>
/// <param name="PortStated">
/// Whether <paramref name="Port"/> is the name the <em>script</em> wrote, rather than one
/// <see cref="BindingRun"/> chose for an unqualified endpoint (<c>D-88</c>).
/// <para>
/// <strong>The two are indistinguishable downstream, and something depends on telling them
/// apart.</strong> A three-way valve's <c>a</c> is its control path and <c>b</c> its bypass, so a
/// script that names them has said which leg the valve modulates. Positional binding hands out the
/// same two letters in connection order, where they mean nothing: measured on
/// <c>m2-cooling-loop</c>, whose valve is wired without ports, the inferred <c>a</c> lands on the
/// recirculation leg and the inferred <c>b</c> on the control leg — the opposite of the letters.
/// Sizing against the wrong leg is not a labelling error but a wrong Kv, because authority is
/// measured against the resistance behind the leg and the bypass has almost none.
/// </para>
/// </param>
public readonly record struct EndpointSymbol(string Component, string Port, bool PortStated = false);
