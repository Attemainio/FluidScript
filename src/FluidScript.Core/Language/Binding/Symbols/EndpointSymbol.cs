namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One end of a connection: a component and the port it claimed.</summary>
/// <param name="Component">The component's name.</param>
/// <param name="Port">
/// The port's name, empty for a component whose ports are unlimited and unnamed — which is only
/// <c>node</c>.
/// </param>
/// <param name="PortStated">
/// Whether <paramref name="Port"/> is the name the script wrote or the reader settled from the plant (<c>D-175</c>,
/// <c>D-177</c>), rather than one <see cref="BindingRun"/> chose by position for an endpoint that reached it with no
/// port (<c>D-88</c>).
/// <para>
/// <strong>The two are indistinguishable downstream, and something depends on telling them
/// apart.</strong> A three-way valve's <c>a</c> is its control path and <c>b</c> its bypass, so a
/// port stated either way says which leg the valve modulates. Positional binding hands out the
/// same two letters in connection order, where they mean nothing: measured on
/// <c>m2-cooling-loop</c> wired that way, the positional <c>a</c> lands on the recirculation leg and
/// the positional <c>b</c> on the control leg — the opposite of the letters.
/// Sizing against the wrong leg is not a labelling error but a wrong Kv, because authority is
/// measured against the resistance behind the leg and the bypass has almost none.
/// </para>
/// </param>
public readonly record struct EndpointSymbol(string Component, string Port, bool PortStated = false);
