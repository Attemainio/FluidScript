using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Binding;

/// <summary>Reads a syntax tree into the records the binder binds (<c>D-177</c>, <c>D-178</c>). Not reusable, and not shared between threads.</summary>
/// <remarks>
/// <para>
/// The binder's front end (<c>19</c> §Reading the tree into the binder). It settles what a script leaves to the
/// plant -- every unnamed port, by the direction of flow; a sensor written in a chain, on a node of its own; a
/// controller's line from its <c>moves</c> and <c>reads</c> -- and hands the binder circuits, links, controls, curves,
/// cases and the project as records, with the declarations, <c>let</c>s and runs as the tree has them and their values
/// in the form the evaluator reads (a list's unit on each item, <c>L-75</c>). Names are passed on as written: a
/// <c>K</c> is a difference in the unit table, and an exchanger's <c>secondary.in</c> is the registry's spelling
/// (<c>D-179</c>).
/// </para>
/// <para>
/// The frozen corpus holds its decisions to the models it gave when it was proven (<c>D-178</c>). A token made
/// here has a span inside the text it stands for --
/// a zero-length one where it stands for nothing written -- so every diagnostic lands on what the user wrote.
/// </para>
/// <para><strong>Never throws on user input</strong> (principle P4): what it cannot place is reported and left out.</para>
/// </remarks>
internal sealed partial class ScriptReader(ParseResult source, IComponentRegistry registry)
{
    private readonly ScriptReading _reading = new();

    private readonly List<LetBindingSyntax> _lets = [];

    /// <summary>Every declared component's kind, by name, for what a kind decides here: whether it is a sensor.</summary>
    private readonly Dictionary<string, ComponentKindInfo?> _kinds = new(StringComparer.Ordinal);

    /// <summary>Every name the file uses for a component or a node, so a node made here cannot take one.</summary>
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>The node each sensor written in a chain observes, by the sensor's name (<c>D-166</c>).</summary>
    private readonly Dictionary<string, string> _sensorNodes = new(StringComparer.Ordinal);

    private int _untitled;

    /// <summary>Reads the tree.</summary>
    /// <returns>What the binder binds, and what the reading itself had to say.</returns>
    public ScriptReading Execute()
    {
        var circuits = new List<BlockSyntax>();

        foreach (var statement in source.Root.Statements)
        {
            switch (statement)
            {
                case BlockSyntax { Head: ProjectHeadSyntax } project:
                    ReadProject(project);
                    break;

                case BlockSyntax { Head: DriverCurveHeadSyntax } curve:
                    ReadCurve(curve);
                    break;

                case BlockSyntax { Head: CircuitHeadSyntax } circuit:
                    circuits.Add(circuit);
                    break;

                case LetBindingSyntax let:
                    _lets.Add(let with { Value = Value(let.Value) });
                    break;

                case BlockSyntax { Head: RunHeadSyntax } run:
                    _reading.Runs.Add(ReadRun(run));
                    break;

                // The version line, and anything else at the top level: a statement the parser already reported as
                // out of its block (FS1802) or could not read.
                default:
                    break;
            }
        }

        CollectNames(circuits);
        PlaceSensorsInChains(circuits);
        InferPorts(circuits);

        for (var index = 0; index < circuits.Count; index++)
        {
            _reading.Circuits.Add(ReadCircuit(circuits[index], withLets: index == 0));
        }

        // A file with no circuit keeps its `let`s in the one circuit the binder gives every file.
        if (circuits.Count == 0)
        {
            _reading.Circuits.Add(new BindingRun.CircuitBlock(null, [.. _lets]));
        }

        return _reading;
    }

    // ---- tokens made here ----------------------------------------------------------------------

    /// <summary>A token standing for something the binder reads that the script writes differently or not at all.</summary>
    private static Token Made(TokenKind kind, string text, TextSpan span) =>
        new() { Kind = kind, Text = text, Span = span };

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
        _reading.Diagnostics.Add(Diagnostic.Create(
            descriptor,
            span,
            [.. arguments.Select(static argument => new DiagnosticArgument(argument.Name, argument.Value))]));

    private string Text(SyntaxNode node) => source.Source.ToString(node.Span);
}

/// <summary>What <see cref="ScriptReader"/> hands the binder, in the order the binder applies it.</summary>
internal sealed class ScriptReading
{
    /// <summary>Gets what the reading itself reported: ports it could not settle, settings a block does not take.</summary>
    public List<Diagnostic> Diagnostics { get; } = [];

    /// <summary>Gets each project block's title, or <see langword="null"/> where it has none (<c>L-85</c>).</summary>
    public List<string?> Projects { get; } = [];

    /// <summary>Gets or sets the drawing's spacing, the last stated.</summary>
    public double? Spacing { get; set; }

    /// <summary>Gets the project's <c>style:</c> blocks, merged per project block into one list of style tokens.</summary>
    public List<ImmutableArray<StyleTokenSyntax>> ProjectStyles { get; } = [];

    /// <summary>Gets the <c>show</c> settings, in the order written.</summary>
    public List<VisualizationSymbol> Visualizations { get; } = [];

    /// <summary>Gets the curves, the cases and the design case, in the order written.</summary>
    public List<BindingRun.FileLine> FileLines { get; } = [];

    /// <summary>Gets the circuits, in the order written; never empty.</summary>
    public List<BindingRun.CircuitBlock> Circuits { get; } = [];

    /// <summary>Gets the runs, their values in the form the evaluator reads.</summary>
    public List<BlockSyntax> Runs { get; } = [];
}
