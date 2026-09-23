using System.Collections.Immutable;
using FluidScript.Core.Language.Syntax.Ast.Statements;

namespace FluidScript.Core.Language.Binding;

/// <summary>Presentation values: the <c>style</c> directives read (<c>D-104</c>) and the <c>spacing</c>.</summary>
/// <param name="Tokens">The applied <c>style</c> directives' positional tokens, verbatim and in order.</param>
/// <param name="Spacing">
/// The <c>spacing</c> directive's value in world units, or <see langword="null"/> when the script
/// states none, in which case the layout's default margin applies (<c>D-103</c>).
/// </param>
/// <param name="Default">The project-level style: every <c>style</c> applied before the first circuit header, merged.</param>
/// <param name="Definitions">The named styles, <c>style name = ...</c>, by name.</param>
public sealed record StyleSettings(
    ImmutableArray<StyleTokenSyntax> Tokens,
    double? Spacing,
    StyleSpec Default,
    ImmutableDictionary<string, StyleSpec> Definitions);
