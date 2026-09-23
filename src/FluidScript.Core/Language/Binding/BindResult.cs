using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Binding;

/// <summary>Everything one run of the binder produced.</summary>
/// <param name="Model">The bound model, always present. A model with errors is still a model.</param>
/// <param name="Diagnostics">Everything the binder reported, in source order.</param>
public sealed record BindResult(SemanticModel Model, ImmutableArray<Diagnostic> Diagnostics);
