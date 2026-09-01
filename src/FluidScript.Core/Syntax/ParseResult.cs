using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax.Ast;

namespace FluidScript.Core.Syntax;

/// <summary>Everything one run of the parser produced.</summary>
/// <param name="Source">
/// The text that was parsed. Carried here because a trivium addresses its characters rather than
/// holding them, so printing the tree back needs the text it came from, and a result that carried
/// only the tree could be printed against the wrong one.
/// </param>
/// <param name="Root">
/// The tree, always non-null. A tree containing <see cref="MalformedStatementSyntax"/> nodes is a
/// normal result, not a failure.
/// </param>
/// <param name="Diagnostics">
/// Every diagnostic the lexer and the parser produced, in source order.
/// </param>
public sealed record ParseResult(
    SourceText Source,
    ScriptSyntax Root,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>Which of a circuit's three sections a statement sits in.</summary>
/// <remarks>
/// Scoped to a circuit rather than to the file (<c>D-52</c>): a <c>circuit</c> header ends whatever
/// section the previous circuit was in and opens the new circuit's declaration section.
/// </remarks>
public enum ScriptSection
{
    /// <summary>Before any section marker: directives, bindings and declarations.</summary>
    Declaration = 1,

    /// <summary>After a <c>connections</c> line.</summary>
    Connections,

    /// <summary>After a <c>schedule</c> line.</summary>
    Schedule,
}

/// <summary>What a line was classified as, before it was parsed.</summary>
/// <remarks>
/// <para>
/// Produced by <c>FluidScriptParser.Classify</c> from a line's first token, at most one token
/// of lookahead, and the section it sits in — which is
/// <c>plan/10-language/11-language-overview.md</c>'s invariant 7, expressed as a signature so that
/// breaking it does not compile.
/// </para>
/// <para>
/// Classification is not validation. <c>in N3</c> and <c>in pump power=30</c> are both
/// <see cref="Declaration"/> here, and telling them apart is the declaration parser's job — the first
/// is <c>FS1109</c> and the second is an ordinary component called <c>in</c>. That refinement needs
/// the line's length, not a third token, so the invariant holds.
/// </para>
/// </remarks>
public enum StatementKind
{
    /// <summary>The <c>fluidscript</c> version line.</summary>
    Version = 1,

    /// <summary>A <c>project</c> directive.</summary>
    Project,

    /// <summary>A <c>spacing</c> directive.</summary>
    Spacing,

    /// <summary>A <c>circuit</c> header.</summary>
    Circuit,

    /// <summary>A <c>fluid</c> directive.</summary>
    Fluid,

    /// <summary>A <c>catalog</c> directive.</summary>
    Catalog,

    /// <summary>A <c>style</c> directive.</summary>
    Style,

    /// <summary>A <c>show</c> directive.</summary>
    Show,

    /// <summary>A <c>let</c> binding.</summary>
    Let,

    /// <summary>The <c>connections</c> section marker.</summary>
    ConnectionsHeader,

    /// <summary>The <c>schedule</c> section marker.</summary>
    ScheduleHeader,

    /// <summary>A <c>supply</c> or <c>return</c> attachment.</summary>
    Attachment,

    /// <summary>A <c>control</c> binding.</summary>
    Control,

    /// <summary>A line of topology.</summary>
    Connection,

    /// <summary>A scheduled disturbance.</summary>
    Disturbance,

    /// <summary>A component declaration.</summary>
    Declaration,

    /// <summary>A line that starts with a word that cannot start a statement.</summary>
    Unclassifiable,
}
