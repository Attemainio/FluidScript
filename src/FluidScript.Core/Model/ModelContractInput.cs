using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Catalogs;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Solvers;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Model;

/// <summary>Everything the pipeline hands the contract builder.</summary>
/// <remarks>
/// A compile-only request carries no <see cref="Run"/> and serializes with every state
/// <see langword="null"/>; that is the debounce path's normal case, not a degraded one (<c>26</c>).
/// </remarks>
public sealed record ModelContractInput
{
    /// <summary>The script text, for the hash and for line positions.</summary>
    public required SourceText Source { get; init; }

    /// <summary>The parsed script, for the version line and the <c>show</c> directive.</summary>
    public required ScriptSyntax Root { get; init; }

    /// <summary>The bound model.</summary>
    public required SemanticModel Model { get; init; }

    /// <summary>The lowered graph, sized or bootstrap.</summary>
    public required CircuitGraph Graph { get; init; }

    /// <summary>The outer-loop result, or <see langword="null"/> when nothing was solved.</summary>
    public OuterLoopResult? Run { get; init; }

    /// <summary>Every diagnostic the pipeline produced before serialization.</summary>
    public required ImmutableArray<Diagnostic> Diagnostics { get; init; }

    /// <summary>The pipe catalogue sizes were drawn from, for provenance.</summary>
    public required ICatalog<PipeSpec> Catalog { get; init; }

    /// <summary>Wall time the caller measured, ms, or <see langword="null"/>.</summary>
    public int? ElapsedMs { get; init; }
}
