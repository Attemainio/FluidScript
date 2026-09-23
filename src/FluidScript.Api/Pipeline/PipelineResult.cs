using System.Collections.Immutable;

using FluidScript.Api.Contracts;
using FluidScript.Core.Model.Contract;
using FluidScript.Core.Solvers.Passes;

namespace FluidScript.Api.Pipeline;

/// <summary>What the pipeline produced.</summary>
/// <param name="Model">The contract, or <see langword="null"/> when the language gate refused the script or the mode was <see cref="PipelineMode.Validate"/>.</param>
/// <param name="Diagnostics">Every diagnostic in <c>44</c>'s order, rendered against the source; the model carries the same list when there is one.</param>
/// <param name="Timings">Stage timings.</param>
/// <param name="Run">The outer-loop result when something was solved, for the session to keep its solution.</param>
/// <param name="LanguageMajor">The major the script declared, or <see langword="null"/>.</param>
public sealed record PipelineResult(
    ModelContract? Model,
    ImmutableArray<DiagnosticWire> Diagnostics,
    TimingsWire Timings,
    OuterLoopResult? Run,
    int? LanguageMajor);
