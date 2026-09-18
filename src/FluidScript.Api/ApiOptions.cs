using FluidScript.Core.Language;

namespace FluidScript.Api;

/// <summary>What the host lets an operator set: the input limits, the session lifetime and where the docs index is.</summary>
/// <remarks>
/// Bound from the <c>FluidScript</c> configuration section. Everything here has a default a fresh
/// checkout runs with; nothing here changes a number a solve produces (<c>41</c>'s invariant 6).
/// </remarks>
public sealed class ApiOptions
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string Section = "FluidScript";

    /// <summary>The ceilings one request may reach; <c>07</c>'s defaults, and what <c>metadata</c> reports.</summary>
    public InputLimits Limits { get; set; } = InputLimits.Default;

    /// <summary>How long a session may go untouched before it is dropped. A dropped session costs one cold solve (<c>41</c>).</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How often idle sessions are looked for.</summary>
    public TimeSpan SessionEvictionInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The URI <c>metadata.docsIndex</c> carries: the generated function index the language's pages hang off (<c>61</c>).</summary>
    public string DocsIndex { get; set; } = "docs/functions/index.md";
}
