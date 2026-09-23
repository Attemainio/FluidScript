namespace FluidScript.Api.Contracts;

/// <summary>How long each stage took, whole milliseconds (<c>42</c>).</summary>
/// <param name="ParseMs">Lexing and parsing.</param>
/// <param name="BindMs">Binding: symbols, kinds, parameters, inference.</param>
/// <param name="SizeMs">Lowering with sizing applied, before the solve; zero when nothing was lowered.</param>
/// <param name="SolveMs">The outer loop; zero when nothing was solved.</param>
/// <param name="TotalMs">Request receipt to the response being built, layout and serialization included.</param>
/// <remarks>
/// Shipped in the response rather than only logged, so a status line can say "sizing took 400 ms"
/// without a profiler. Each figure is wall time of that stage alone; the total is more than their sum
/// by the layout, the contract and whatever the host did between them.
/// </remarks>
public sealed record TimingsWire(int ParseMs, int BindMs, int SizeMs, int SolveMs, int TotalMs);
