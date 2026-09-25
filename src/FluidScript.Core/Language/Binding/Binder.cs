using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;

namespace FluidScript.Core.Language.Binding;

/// <summary>
/// Turns a syntax tree into a semantic model: resolved symbols, known kinds, typed values, and the
/// distinction between absent and given that <c>D-02</c> depends on.
/// </summary>
/// <remarks>
/// <para>
/// This runs <c>15</c>'s binding steps 0 through 5 — partition into circuits, collect declarations,
/// resolve kinds, bind parameters, build the dependency graph, evaluate. Steps 6 through 11 — ports,
/// connections, inference, attachments, control bindings, the schedule, validation and tags — are in
/// <c>BindingRun.Topology.cs</c> and have no notion of expressions, exactly as steps 0–5 have no
/// notion of topology. The split is what keeps each half testable alone.
/// </para>
/// <para>
/// Like every stage, it never throws on user input. A script under editing is malformed most of the
/// time, and a malformed script still binds: an unresolved kind produces a component with no kind, a
/// failed expression produces a parameter with no value, and the rest of the file binds around it.
/// </para>
/// </remarks>
public sealed class Binder
{
    private readonly IComponentRegistry _registry;

    /// <summary>Creates a binder over a component registry.</summary>
    /// <param name="registry">Where kinds, parameters and properties are looked up.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    public Binder(IComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
    }

    /// <summary>Binds a parsed script.</summary>
    /// <param name="parse">The parse to bind.</param>
    /// <param name="documentName">
    /// What to call the file when the script declares no circuit, used for the implicit circuit's name.
    /// </param>
    /// <returns>The model and every diagnostic binding produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parse"/> is <see langword="null"/>.</exception>
    public BindResult Bind(ParseResult parse, string documentName = "script")
    {
        ArgumentNullException.ThrowIfNull(parse);

        var bound = new BindingRun(_registry, parse, documentName).Execute();

        // The binder's messages are language 1's; a language 2 file reads them in its own words (19 §Diagnostics).
        return parse.Language == 2
            ? bound with { Diagnostics = Translation.Language2Wording.Apply(bound.Diagnostics) }
            : bound;
    }
}
