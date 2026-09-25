using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Translation;

/// <summary>One translation of one language 2 parse. Not reusable, and not shared between threads.</summary>
/// <remarks>
/// <para>
/// The binder reads statements in file order and partitions them by <see cref="CircuitHeaderSyntax"/>, so the
/// order written here is part of the meaning: the file-wide statements first (the version, the project and its
/// presentation, the cases, the curves), then each circuit's header, fluid and style ahead of its body. The
/// <c>let</c>s go after the first circuit's header, because the binder files a statement before any circuit into
/// an implicit circuit of its own.
/// </para>
/// <para>
/// A token made here has a span inside the language 2 text it stands for — a zero-length one where it stands for
/// nothing written, such as the <c>=</c> of <c>length=</c> — and never one that runs backwards: a node's span
/// runs from its first token to its last, so tokens keep source order within every node built here.
/// </para>
/// </remarks>
internal sealed partial class TranslationRun(ParseResult source, IComponentRegistry registry)
{
    private readonly ImmutableArray<Diagnostic>.Builder _diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

    /// <summary>The file-wide statements, in the order the binder reads them.</summary>
    private readonly List<StatementSyntax> _fileWide = [];

    private readonly List<StatementSyntax> _lets = [];
    private readonly List<StatementSyntax> _circuits = [];
    private readonly List<StatementSyntax> _runs = [];

    /// <summary>Every declared component's kind, by name, for what a kind decides here: whether it is a sensor.</summary>
    private readonly Dictionary<string, ComponentKindInfo?> _kinds = new(StringComparer.Ordinal);

    /// <summary>Every name the file uses for a component or a node, so a node made here cannot take one.</summary>
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>The node each sensor written in a chain observes, by the sensor's name (<c>D-166</c>).</summary>
    private readonly Dictionary<string, string> _sensorNodes = new(StringComparer.Ordinal);

    private int _untitled;

    public ParseResult Execute()
    {
        _diagnostics.AddRange(source.Diagnostics);

        var circuits = new List<BlockSyntax>();

        foreach (var statement in source.Root.Statements)
        {
            switch (statement)
            {
                case VersionDirectiveSyntax version:
                    _fileWide.Add(version);
                    break;

                case BlockSyntax { Head: ProjectHeadSyntax } project:
                    TranslateProject(project);
                    break;

                case BlockSyntax { Head: DriverCurveHeadSyntax } curve:
                    TranslateCurve(curve);
                    break;

                case BlockSyntax { Head: CircuitHeadSyntax } circuit:
                    circuits.Add(circuit);
                    break;

                case LetBindingSyntax let:
                    _lets.Add(let with { Value = Value(let.Value) });
                    break;

                case BlockSyntax { Head: RunHeadSyntax } run:
                    _runs.Add(TranslateRun(run));
                    break;

                // Anything else at the top level is a statement the parser already reported as out of its
                // block (FS1802) or could not read.
                default:
                    break;
            }
        }

        CollectNames(circuits);
        PlaceSensorsInChains(circuits);
        InferPorts(circuits);

        for (var index = 0; index < circuits.Count; index++)
        {
            TranslateCircuit(circuits[index], withLets: index == 0);
        }

        if (circuits.Count == 0)
        {
            _circuits.AddRange(_lets);
        }

        // Last, so every name a run targets is declared above it; the binder steps over a run in its
        // circuit partition and binds it once the model is complete.
        var root = new ScriptSyntax([.. _fileWide, .. _circuits, .. _runs], source.Root.EndOfFile);
        return new ParseResult(source.Source, root, _diagnostics.ToImmutable()) { Language = 2 };
    }

    // ---- tokens made here ----------------------------------------------------------------------

    /// <summary>A token standing for something language 2 writes differently or not at all.</summary>
    private static Token Made(TokenKind kind, string text, TextSpan span) =>
        new() { Kind = kind, Text = text, Span = span };

    /// <summary>A language 1 keyword that language 2 does not write, placed where its statement begins.</summary>
    private static Token Keyword(ReservedWord word, string text, int at) =>
        new() { Kind = TokenKind.Keyword, Keyword = word, Text = text, Span = new TextSpan(at, 0) };

    private static Token EqualsAt(int at) => Made(TokenKind.Equals, "=", new TextSpan(at, 0));

    private static IdentifierSyntax Identifier(string text, TextSpan span) =>
        new(Made(TokenKind.Identifier, text, span));

    /// <summary>A one-word parameter name made here, <c>length</c> or <c>dn</c>, placed at the start of what it names.</summary>
    private static QualifiedNameSyntax Named(string text, int at) =>
        new(new IndexedNameSyntax(Identifier(text, new TextSpan(at, 0)), null), []);

    /// <summary>Whether a setting's name is the given word, by <c>D-15</c>'s first stage (case and underscores), as <c>D-170</c> keeps it.</summary>
    private static bool Is(ParameterSyntax setting, string word) =>
        setting.Name.Parts.IsDefaultOrEmpty
        && string.Equals(NameResolution.Normalize(setting.Name.Head.Name.Text), NameResolution.Normalize(word), StringComparison.Ordinal);

    // ---- diagnostics ---------------------------------------------------------------------------

    private void Report(DiagnosticDescriptor descriptor, TextSpan span, params (string Name, string Value)[] arguments) =>
        _diagnostics.Add(Diagnostic.Create(
            descriptor,
            span,
            [.. arguments.Select(static argument => new DiagnosticArgument(argument.Name, argument.Value))]));

    private string Text(SyntaxNode node) => source.Source.ToString(node.Span);
}
