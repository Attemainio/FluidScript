using FluidScript.Core.Solvers.Passes;

namespace FluidScript.Core.Sizing.Scenarios;

/// <summary>One scenario's solve, with what it cost.</summary>
/// <param name="Name">The scenario's name.</param>
/// <param name="Index">Its position in the declaration, which is what an array element binds to.</param>
/// <param name="Result">The solve.</param>
/// <param name="Elapsed">Wall time, for the report's parallel-efficiency line.</param>
public readonly record struct ScenarioSolve(string Name, int Index, OuterLoopResult Result, TimeSpan Elapsed);
