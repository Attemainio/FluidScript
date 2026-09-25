using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Tests.Language.Conversion;

/// <summary>A language 1 script converted to language 2, and what could not be said there.</summary>
/// <param name="Text">The language 2 text.</param>
/// <param name="Gaps">Each thing language 2 could not say, in words; empty when the conversion is whole.</param>
/// <param name="Original">The language 1 script's model, which the converted one is proven against.</param>
/// <param name="Review">Comments carried across that still describe language 1's syntax, for a person to reword.</param>
public sealed record Conversion(string Text, ImmutableArray<string> Gaps, SemanticModel Original, ImmutableArray<string> Review);

/// <summary>Converts a language 1 script to language 2 (<c>D-174</c>, <c>P6.11</c> package 5).</summary>
/// <remarks>
/// <para>
/// A development tool, never a product feature, and deleted at the switch with the rest of language 1. It reads
/// the language 1 tree for the text — expressions are copied as written, comments travel with the statement they
/// sat on — and the language 1 model for what the text leaves implicit: a circuit's role, a port chosen by order.
/// </para>
/// <para>
/// Ports are written only where language 2's inference would choose differently: the converted text is bound, the
/// ports each component ends up with are compared with language 1's, and a component that differs has its ports
/// written out and the text converted again. Anything language 2 cannot say is a gap, reported and never dropped
/// in silence.
/// </para>
/// </remarks>
public static partial class Language1Converter
{
    /// <summary>Converts one script.</summary>
    /// <param name="text">The language 1 text.</param>
    /// <param name="documentName">The name the binder gives an untitled circuit, as the original was bound with.</param>
    /// <returns>The language 2 text, its gaps, and the original's model.</returns>
    public static Conversion Convert(string text, string documentName = "script")
    {
        ArgumentNullException.ThrowIfNull(text);

        var forced = new HashSet<string>(StringComparer.Ordinal);
        Conversion result;

        for (var round = 0; ; round++)
        {
            result = new Run(text, documentName, forced).Execute();
            var differing = DifferingPorts(result, documentName).Where(name => !forced.Contains(name)).ToList();
            if (differing.Count == 0 || round == 4)
            {
                return result;
            }

            forced.UnionWith(differing);
        }
    }

    /// <summary>Binds language 2 text as the pipeline does.</summary>
    /// <param name="text">The text.</param>
    /// <param name="documentName">The document's name.</param>
    /// <returns>The binding, with the parse's diagnostics ahead of the binder's.</returns>
    public static BindResult BindLanguage2(string text, string documentName = "script")
    {
        var source = new SourceText(text);
        var major = ScriptCompatibility.Inspect(source).DetectedMajor;
        var parse = MajorParser.Parse(source, major, ComponentRegistry.Default);
        var bound = new Binder(ComponentRegistry.Default).Bind(parse, documentName);
        return bound with { Diagnostics = [.. parse.Diagnostics, .. bound.Diagnostics] };
    }

    /// <summary>Every port a component is connected at, and to what, as a set of lines.</summary>
    /// <param name="model">The model.</param>
    /// <param name="component">The component.</param>
    /// <returns>The component's connections, sorted.</returns>
    public static IEnumerable<string> PortsOf(SemanticModel model, string component) =>
        model.Connections
            .SelectMany(connection => new[]
            {
                connection.From.Component == component ? $"{connection.From.Port} -> {connection.To.Component}.{connection.To.Port}" : null,
                connection.To.Component == component ? $"{connection.To.Port} <- {connection.From.Component}.{connection.From.Port}" : null,
            })
            .OfType<string>()
            .Order(StringComparer.Ordinal);

    private static IEnumerable<string> DifferingPorts(Conversion conversion, string documentName)
    {
        var converted = BindLanguage2(conversion.Text, documentName).Model;

        foreach (var component in conversion.Original.Components)
        {
            if (component.Kind is not { } kind || kind.HasUnlimitedPorts || kind.Ports.IsEmpty)
            {
                continue;
            }

            if (!PortsOf(conversion.Original, component.Name).SequenceEqual(PortsOf(converted, component.Name)))
            {
                yield return component.Name;
            }
        }
    }

    /// <summary>A piece of output: the comments that sat above it, its lines, and the comment at the end of its first line.</summary>
    private sealed class Chunk
    {
        public List<string> Comments { get; } = [];

        public List<string> Lines { get; } = [];

        public string? Trailing { get; set; }
    }

    private sealed class CircuitOut
    {
        public required string Name { get; init; }

        public Chunk Head { get; } = new();

        public List<string> Settings { get; } = [];

        public List<string> Style { get; } = [];

        public List<Chunk> Body { get; } = [];

        public bool HasComponents { get; set; }
    }

    private sealed class ControllerOut
    {
        public required ComponentDeclarationSyntax Declaration { get; init; }

        public required Chunk Chunk { get; init; }

        public List<ControlBindingSyntax> Controls { get; } = [];

        public List<(ImmutableArray<string> Comments, string? Trailing)> ControlNotes { get; } = [];
    }

    private sealed partial class Run(string text, string documentName, HashSet<string> forced)
    {
        private readonly SourceText _source = new(text);
        private readonly ComponentRegistry _registry = ComponentRegistry.Default;
        private readonly List<string> _gaps = [];

        private readonly Chunk _projectHead = new();
        private readonly List<Chunk> _projectSettings = [];
        private readonly List<string> _projectStyle = [];
        private readonly List<Chunk> _lets = [];
        private readonly List<Chunk> _curves = [];
        private readonly List<CircuitOut> _circuits = [];
        private readonly List<Chunk> _events = [];
        private readonly Dictionary<string, ControllerOut> _controllers = new(StringComparer.Ordinal);
        private readonly List<string> _pending = [];

        private SemanticModel _model = null!;
        private ImmutableArray<StatementSyntax> _statements;
        private (ImmutableArray<string> Leading, string? Trailing)[] _notes = [];
        private Dictionary<(int Statement, int Endpoint, bool Inflow), string> _resolved = [];
        private string? _projectName;
        private string? _start;
        private bool _hasShow;
        private CircuitOut? _circuit;
        private Chunk? _curve;
        private string? _timeFormat;

        public Conversion Execute()
        {
            var parse = FluidScriptParser.Parse(_source);
            _model = new Binder(_registry).Bind(parse, documentName).Model;
            _statements = parse.Root.Statements;
            _notes = Notes();
            _resolved = ResolvedPorts();

            for (var i = 0; i < _statements.Length; i++)
            {
                Statement(i, _statements[i]);
            }

            var converted = Emit();
            var review = converted.Split('\n')
                .Where(static line => line.TrimStart().StartsWith('#') && Language1Words().IsMatch(line))
                .Select(static line => line.Trim());
            return new Conversion(converted, [.. _gaps.Distinct(StringComparer.Ordinal)], _model, [.. review]);
        }

        // ---- comments --------------------------------------------------------------------------------

        private (ImmutableArray<string> Leading, string? Trailing)[] Notes()
        {
            var notes = new (ImmutableArray<string>, string?)[_statements.Length];
            var previousEnd = -1;

            for (var i = 0; i < _statements.Length; i++)
            {
                var span = _statements[i].Span;
                var first = _source.GetLinePosition(span.Start).Line;
                var last = _source.GetLinePosition(Math.Max(span.Start, span.End - 1)).Line;
                var leading = ImmutableArray.CreateBuilder<string>();

                for (var line = previousEnd + 1; line < first; line++)
                {
                    var content = Line(line).Trim();
                    if (content.Length == 0)
                    {
                        // A blank line is kept once, and never at the very top of the file.
                        var afterText = leading.Count > 0 ? leading[^1].Length != 0 : previousEnd >= 0;
                        if (afterText)
                        {
                            leading.Add(string.Empty);
                        }
                    }
                    else
                    {
                        leading.Add(content);
                    }
                }

                var rest = Line(last)[Math.Min(Line(last).Length, _source.GetLinePosition(span.End).Line == last ? _source.GetLinePosition(span.End).Character : Line(last).Length)..];
                var hash = rest.IndexOf('#', StringComparison.Ordinal);
                notes[i] = (leading.ToImmutable(), hash >= 0 ? rest[hash..].TrimEnd() : null);
                previousEnd = last;
            }

            return notes;
        }

        private string Line(int line)
        {
            var start = _source.GetLineStart(line);
            var end = line + 1 < _source.LineCount ? _source.GetLineStart(line + 1) : _source.Length;
            return _source.Text[start..end].TrimEnd('\r', '\n');
        }

        private string Epilogue()
        {
            if (_statements.IsEmpty)
            {
                return string.Empty;
            }

            var last = _source.GetLinePosition(Math.Max(0, _statements[^1].Span.End - 1)).Line;
            var lines = new List<string>();
            for (var line = last + 1; line < _source.LineCount; line++)
            {
                var content = Line(line).Trim();
                if (content.Length > 0)
                {
                    lines.Add(content);
                }
            }

            return lines.Count == 0 ? string.Empty : "\n" + string.Join('\n', lines) + "\n";
        }

        private Chunk Chunk(int index)
        {
            var chunk = new Chunk { Trailing = _notes[index].Trailing };
            chunk.Comments.AddRange(_pending);
            _pending.Clear();
            chunk.Comments.AddRange(_notes[index].Leading);
            return chunk;
        }

        private void Carry(int index)
        {
            _pending.AddRange(_notes[index].Leading);
            if (_notes[index].Trailing is { } trailing)
            {
                _pending.Add(trailing);
            }
        }

        private void Gap(string what) => _gaps.Add(what);

        // ---- statements ------------------------------------------------------------------------------

        private void Statement(int index, StatementSyntax statement)
        {
            if (statement is not CurveRowSyntax)
            {
                _curve = null;
            }

            switch (statement)
            {
                case VersionDirectiveSyntax:
                    break;

                case ProjectDirectiveSyntax project:
                    Project(index, project);
                    break;

                case ScenariosDirectiveSyntax scenarios:
                    Setting(index, $"cases = [{string.Join(", ", scenarios.Names.Select(static name => name.Text))}]");
                    break;

                case DesignDirectiveSyntax design:
                    Design(index, design);
                    break;

                case CatalogDirectiveSyntax catalog:
                    Setting(index, $"catalog = {Text(catalog.CatalogId)}{(catalog.Version is { } version ? "@" + version.Text : string.Empty)}");
                    break;

                case ShowDirectiveSyntax show:
                    Show(index, show);
                    break;

                case SpacingDirectiveSyntax spacing:
                    Setting(index, $"spacing = {Text(spacing.Value)}");
                    break;

                case StyleDirectiveSyntax style:
                    Style(index, style);
                    break;

                case LetBindingSyntax let:
                    var letChunk = Chunk(index);
                    letChunk.Lines.Add($"let {let.Name.Text} = {Value(let.Value)}");
                    _lets.Add(letChunk);
                    break;

                case CurveHeaderSyntax header:
                    Curve(index, header);
                    break;

                case CurveRowSyntax row:
                    Row(index, row);
                    break;

                case CircuitHeaderSyntax header:
                    OpenCircuit(index, header);
                    break;

                case FluidDirectiveSyntax fluid:
                    Fluid(index, fluid);
                    break;

                case ConnectionsHeaderSyntax:
                    Carry(index);
                    break;

                case ScheduleHeaderSyntax:
                    Carry(index);
                    break;

                case ComponentDeclarationSyntax declaration:
                    Declaration(index, declaration);
                    break;

                case ConnectionSyntax connection:
                    Connection(index, connection);
                    break;

                case ControlBindingSyntax control:
                    Control(index, control);
                    break;

                case DisturbanceSyntax disturbance:
                    Event(index, disturbance);
                    break;

                case AttachmentSyntax attachment:
                    Gap($"attachment '{Text(attachment)}': language 2 joins circuits through a component both name (D-166)");
                    break;

                case MalformedStatementSyntax malformed:
                    Gap($"a line language 1 cannot read: '{Text(malformed)}'");
                    break;

                default:
                    Gap($"no conversion for {statement.GetType().Name}: '{Text(statement)}'");
                    break;
            }
        }

        private void Project(int index, ProjectDirectiveSyntax project)
        {
            _projectName = project.Name.Text;
            _projectHead.Comments.AddRange(_notes[index].Leading);
            _projectHead.Trailing = _notes[index].Trailing;

            foreach (var argument in project.Arguments)
            {
                if (NameResolution.Normalize(argument.Name.Text) == "start" && Start(argument.Value) is { } start)
                {
                    _start = start;
                    continue;
                }

                Gap($"project argument '{Text(argument)}'");
            }
        }

        /// <summary>A language 1 start, <c>"2026-01-15T06:00:00"</c>, as language 2's unquoted date.</summary>
        private string? Start(ExpressionSyntax value)
        {
            var written = Text(value).Trim('"');
            return DateTime.TryParse(written, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? time.ToString(time.Second == 0 ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : null;
        }

        private void Setting(int index, string line)
        {
            var chunk = Chunk(index);
            chunk.Lines.Add(line);
            _projectSettings.Add(chunk);
        }

        private void Design(int index, DesignDirectiveSyntax design)
        {
            if (design.Scenario is { } named)
            {
                if (_model.Project.Scenarios.IsEmpty || _model.Project.Scenarios[0] != named.Text)
                {
                    Gap($"'design {named.Text}' is not the first case; language 2's design case is the first");
                }

                Carry(index);
                return;
            }

            var first = true;
            foreach (var argument in design.Arguments)
            {
                var chunk = first ? Chunk(index) : new Chunk();
                first = false;

                var key = argument.Name.Text;
                var value = Value(argument.Value);
                if (argument.Value is NumberLiteralSyntax or UnaryExpressionSyntax { Operand: NumberLiteralSyntax }
                    && _model.Project.Design.TryGetValue(key, out var entry)
                    && entry.Role?.Dimension is { } dimension
                    && UnitTable.CanonicalUnitFor(dimension) is { } unit)
                {
                    value = $"{value} {Language2Unit(unit.Text)}";
                }

                chunk.Lines.Add($"let {key} = {value}");
                _lets.Add(chunk);
            }
        }

        private void Show(int index, ShowDirectiveSyntax show)
        {
            if (_hasShow)
            {
                Gap($"a second show line, '{Text(show)}': language 2 shows one list for the project");
                Carry(index);
                return;
            }

            _hasShow = true;
            var names = show.Properties.Select(static property => property.Text).ToArray();
            var chunk = Chunk(index);
            chunk.Lines.Add(names.Length == 1 ? $"show = {names[0]}" : $"show = [{string.Join(", ", names)}]");
            if (show.Scale is { } scale)
            {
                chunk.Lines.Add($"scale = {Text(scale)}");
            }

            _projectSettings.Add(chunk);
        }

        private void Style(int index, StyleDirectiveSyntax style)
        {
            if (style.IsDefinition)
            {
                Gap($"named style '{Text(style)}': language 2 has no named styles");
                Carry(index);
                return;
            }

            var keys = new List<string>();
            foreach (var part in style.Parts)
            {
                var value = part.Text;
                keys.Add(part.Kind switch
                {
                    StyleTokenKind.Quoted => $"colour = {value}",
                    StyleTokenKind.Number => $"width = {value}",
                    StyleTokenKind.Quantity => $"width = {Regex.Replace(value, @"\s*px$", string.Empty)}",
                    StyleTokenKind.Pattern => $"line = {Pattern(value)}",
                    StyleTokenKind.Word when value is "sharp" or "fillet" or "round" => $"corner = {(value == "round" ? "fillet" : value)}",
                    StyleTokenKind.Word => $"colour = {value}",
                    _ => $"# {value}",
                });
            }

            if (_circuit is null)
            {
                _projectStyle.AddRange(keys);
                Carry(index);
            }
            else if (!_circuit.HasComponents)
            {
                _circuit.Style.AddRange(keys);
                Carry(index);
            }
            else
            {
                Gap($"style for the components that follow it, '{Text(style)}': language 2 styles a circuit or the project");
                Carry(index);
            }
        }

        /// <summary>Words and spellings only language 1 has, which a comment carried across may still use.</summary>
        [GeneratedRegex(@"\b(connections|schedule|scenarios|design \w+=|control \w+ with|dK|\w+\[2\]|fluid dynamic|project dynamic|short control form)\b|\w=\w")]
        private static partial Regex Language1Words();

        private static string Pattern(string pattern) => pattern switch
        {
            "-" => "solid",
            "--" => "dashed",
            ".." => "dotted",
            "-." => "dashdot",
            _ => pattern,
        };

        private void Curve(int index, CurveHeaderSyntax header)
        {
            var chunk = Chunk(index);
            var curve = _model.Curves.FirstOrDefault(c => c.Name == header.Name.Text);
            _timeFormat = curve?.TimeFormat;

            if (header.Driver is null)
            {
                Gap($"curve '{header.Name.Text}' names no driver");
            }
            else if (curve?.DriverRole is { } unstated
                && !_model.Project.Design.Values.Any(value => value.Role?.CanonicalName == unstated.CanonicalName)
                && curve.DriverKind != CurveDriverKind.Time)
            {
                Gap($"curve '{header.Name.Text}' is driven by '{header.Driver.Text}', which nothing states: language 2 drives a curve by a let, which needs a value");
            }
            else if (curve?.DriverKind == CurveDriverKind.Curve)
            {
                Gap($"curve '{header.Name.Text}' is driven by another curve, '{header.Driver.Text}': language 2 drives a curve by a let or time");
            }

            var modifiers = string.Concat(header.Modifiers.Select(static modifier => " " + modifier.Text));
            foreach (var argument in header.Arguments)
            {
                if (argument.Name.Text != "format")
                {
                    Gap($"curve argument '{Text(argument)}'");
                }
            }

            // `curve heating outdoor` reads `design tout=-26` through the schedule role both name; in language 2 the
            // driver is the let the design value became, by its own name.
            var driver = header.Driver?.Text ?? "?";
            if (curve?.DriverRole is { } role
                && _model.Project.Design.Values.FirstOrDefault(value => value.Role?.CanonicalName == role.CanonicalName) is { } design)
            {
                driver = design.WrittenName;
            }

            chunk.Lines.Add($"curve {header.Name.Text}: {driver}{modifiers}");
            _curves.Add(chunk);
            _curve = chunk;
        }

        private void Row(int index, CurveRowSyntax row)
        {
            if (_curve is null)
            {
                Gap($"a curve row outside a curve: '{Text(row)}'");
                return;
            }

            foreach (var comment in _notes[index].Leading.Where(static line => line.Length > 0))
            {
                _curve.Lines.Add("  " + comment);
            }

            var written = Text(row);
            if (_timeFormat is { } format)
            {
                written = TimeRow(written, format);
            }

            _curve.Lines.Add("  " + written + (_notes[index].Trailing is { } trailing ? "  " + trailing : string.Empty));
        }

        private string TimeRow(string row, string format)
        {
            var match = Regex.Match(row, @"^(?<time>.+?)\s+(?<y>\S+)$");
            if (match.Success
                && DateTime.TryParseExact(match.Groups["time"].Value.Trim('"'), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                var iso = time.Second == 0 ? time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                return $"{iso}   {match.Groups["y"].Value}";
            }

            Gap($"a curve row whose time is not '{format}': '{row}'");
            return row;
        }

        private void OpenCircuit(int index, CircuitHeaderSyntax header)
        {
            _circuit = new CircuitOut { Name = header.Name.Text };
            _circuit.Head.Comments.AddRange(_pending);
            _pending.Clear();
            _circuit.Head.Comments.AddRange(_notes[index].Leading);
            _circuit.Head.Trailing = _notes[index].Trailing;

            if (header.Number is { } number)
            {
                _circuit.Settings.Add($"number = {Text(number)}");
            }

            if (_model.Circuits.FirstOrDefault(c => c.Name == header.Name.Text) is { Role.CanonicalName: var role } && role != "neutral")
            {
                _circuit.Settings.Add($"role = {role}");
            }

            _circuits.Add(_circuit);
        }

        private CircuitOut Circuit()
        {
            if (_circuit is null)
            {
                var name = _model.Circuits.IsEmpty ? documentName : _model.Circuits[0].Name;
                _circuit = new CircuitOut { Name = name };
                if (_model.Circuits.FirstOrDefault() is { Role.CanonicalName: var role } && role != "neutral")
                {
                    _circuit.Settings.Add($"role = {role}");
                }

                _circuits.Add(_circuit);
            }

            return _circuit;
        }

        private void Fluid(int index, FluidDirectiveSyntax fluid)
        {
            var circuit = Circuit();
            circuit.Settings.Insert(0, $"fluid = {fluid.Substance.Text}");
            Carry(index);

            foreach (var argument in fluid.Arguments)
            {
                Gap($"fluid argument '{Text(argument)}' in '{circuit.Name}'");
            }
        }

        private void Declaration(int index, ComponentDeclarationSyntax declaration)
        {
            var circuit = Circuit();
            circuit.HasComponents = true;
            var chunk = Chunk(index);
            var symbol = _model.Components.FirstOrDefault(c => c.Name == declaration.Name.Text);

            if (symbol?.Kind?.Keyword == "controller")
            {
                _controllers[declaration.Name.Text] = new ControllerOut { Declaration = declaration, Chunk = chunk };
                circuit.Body.Add(chunk);
                return;
            }

            var line = new StringBuilder($"{declaration.Name.Text}  {declaration.Kind.Text}");
            if (declaration.AttachedTo is { } node)
            {
                line.Append($" at {node.Text}");
            }

            foreach (var parameter in declaration.Parameters)
            {
                line.Append("  ").Append(Parameter(parameter, declaration.Name.Text));
            }

            // `sized_at tout=-5` is a setting per driver in language 2, `sized_at.tout = -5 C` (D-175), named as the
            // design value's let is, since that is the driver a curve reads there.
            foreach (var argument in declaration.SizingPoint)
            {
                var written = argument.Name.Text;
                var role = ScheduleRoleRegistry.Resolve(written);
                var driver = role is not null
                    && _model.Project.Design.Values.FirstOrDefault(v => v.Role?.CanonicalName == role.CanonicalName) is { } design
                        ? design.WrittenName
                        : written;

                var value = Value(argument.Value);
                if (argument.Value is NumberLiteralSyntax or UnaryExpressionSyntax { Operand: NumberLiteralSyntax }
                    && role?.Dimension is { } dimension
                    && UnitTable.CanonicalUnitFor(dimension) is { } unit)
                {
                    value = $"{value} {Language2Unit(unit.Text)}";
                }

                line.Append($"  sized_at.{driver} = {value}");
            }

            chunk.Lines.Add(line.ToString());
            circuit.Body.Add(chunk);
        }

        private string Parameter(ParameterSyntax parameter, string component) =>
            $"{ParameterName(parameter.Name.Text, component)} = {Value(parameter.Value)}";

        // ---- connections -----------------------------------------------------------------------------

        private Dictionary<(int, int, bool), string> ResolvedPorts()
        {
            var resolved = new Dictionary<(int, int, bool), string>();

            for (var s = 0; s < _statements.Length; s++)
            {
                if (_statements[s] is not ConnectionSyntax connection)
                {
                    continue;
                }

                var symbols = _model.Connections.Where(c => c.SourceSpan == connection.Span).ToList();
                var used = new bool[symbols.Count];
                var endpoints = connection.Endpoints;

                for (var i = 0; i + 1 < endpoints.Length; i++)
                {
                    Take(endpoints[i].Component.Text, from: true, i, inflow: false);
                    Take(endpoints[i + 1].Component.Text, from: false, i + 1, inflow: true);
                }

                void Take(string component, bool from, int endpoint, bool inflow)
                {
                    for (var k = 0; k < symbols.Count; k++)
                    {
                        var end = from ? symbols[k].From : symbols[k].To;
                        if (!used[k] && end.Component == component)
                        {
                            used[k] = true;
                            resolved[(s, endpoint, inflow)] = end.Port;
                            return;
                        }
                    }
                }
            }

            return resolved;
        }

        private void Connection(int index, ConnectionSyntax connection)
        {
            var circuit = Circuit();
            var chunk = Chunk(index);
            var endpoints = connection.Endpoints;

            // A component whose ports are written for it and that sits inside the chain takes two ports, which one
            // endpoint cannot name: the chain is cut there.
            var pieces = new List<List<string>> { new() };
            for (var i = 0; i < endpoints.Length; i++)
            {
                // A three-way valve's written port is dropped for language 2's rule to settle (D-175), and written back
                // only where the rule would label the leg otherwise: what is left written is what the plant cannot say.
                var endpoint = endpoints[i];
                var name = endpoint.Component.Text;
                if (endpoint.Port is not null && ThreeWay(name) && !forced.Contains(name))
                {
                    endpoint = endpoint with { Dot = null, Port = null };
                }

                var middle = i > 0 && i + 1 < endpoints.Length;

                if (endpoint.Port is null && forced.Contains(name) && middle)
                {
                    pieces[^1].Add(Endpoint(endpoint, WrittenPort(index, i, inflow: true)));
                    pieces.Add([Endpoint(endpoint, WrittenPort(index, i, inflow: false))]);
                    continue;
                }

                var port = endpoint.Port is null && forced.Contains(name)
                    ? WrittenPort(index, i, inflow: i > 0)
                    : null;
                pieces[^1].Add(Endpoint(endpoint, port));
            }

            var pipe = Pipe(connection.Parameters);
            var links = pieces.Sum(static piece => piece.Count - 1);

            foreach (var piece in pieces)
            {
                if (pipe.Length == 0 || links == 1)
                {
                    chunk.Lines.Add(string.Join(" - ", piece) + (pipe.Length == 0 ? string.Empty : "   " + pipe));
                    continue;
                }

                // Language 1 gives each link of a chain its own pipe; language 2 describes one link per line (FS1803).
                for (var i = 0; i + 1 < piece.Count; i++)
                {
                    chunk.Lines.Add($"{piece[i]} - {piece[i + 1]}   {pipe}");
                }
            }

            circuit.Body.Add(chunk);
        }

        /// <summary>A three-way valve with all three legs connected: one the rule can count, which a fragment's is not.</summary>
        private bool ThreeWay(string name) =>
            _model.Components.FirstOrDefault(c => c.Name == name)?.Kind?.Keyword == "three_way_valve"
            && _model.Connections.Count(c => c.From.Component == name || c.To.Component == name) == 3;

        private string? WrittenPort(int statement, int endpoint, bool inflow)
        {
            if (!_resolved.TryGetValue((statement, endpoint, inflow), out var key))
            {
                return null;
            }

            var component = _model.Components.FirstOrDefault(c => c.Name == ((ConnectionSyntax)_statements[statement]).Endpoints[endpoint].Component.Text);
            var port = component?.Kind?.Ports.FirstOrDefault(p => p.Key == key)?.Name ?? key;
            return port;
        }

        private string Endpoint(EndpointSyntax endpoint, string? forcedPort)
        {
            var name = endpoint.Component.Text;
            var port = endpoint.Port?.Text ?? forcedPort;
            return port is null || port.Length == 0 ? name : $"{name}.{PortName(port, name)}";
        }

        private string Pipe(ImmutableArray<ParameterSyntax> parameters)
        {
            var parts = new List<string>();

            foreach (var parameter in parameters)
            {
                var key = NameResolution.Normalize(parameter.Name.Text);
                if (key == "length" && parameter.Value is NumberLiteralSyntax length)
                {
                    parts.Add($"{Text(length)} m");
                }
                else if (key == "length" && parameter.Value is QuantityLiteralSyntax written)
                {
                    parts.Add(Value(written));
                }
                else if (key == "dn" && parameter.Value is NumberLiteralSyntax dn && dn.Token.Value is { } size && size == Math.Floor(size))
                {
                    parts.Add($"DN{Text(dn)}");
                }
                else
                {
                    parts.Add($"{parameter.Name.Text} = {Value(parameter.Value)}");
                }
            }

            return string.Join("  ", parts);
        }

        // ---- controllers and events ------------------------------------------------------------------

        private void Control(int index, ControlBindingSyntax control)
        {
            var controller = control.Controller?.Text
                ?? control.Arguments.FirstOrDefault(static a => a.Name.Text == "by")?.Value.Tokens.FirstOrDefault()?.Text;

            if (controller is null || !_controllers.TryGetValue(controller, out var target))
            {
                Gap($"a control line whose controller is not declared: '{Text(control)}'");
                Carry(index);
                return;
            }

            target.Controls.Add(control);
            target.ControlNotes.Add((_notes[index].Leading, _notes[index].Trailing));
        }

        private void Event(int index, DisturbanceSyntax disturbance)
        {
            var chunk = Chunk(index);
            var target = Endpoint(disturbance.Target, null);

            if (disturbance.Keyword.Text == "over" && disturbance.When is RangeSyntax span && disturbance.Value is PointSyntax)
            {
                // Language 1 reads one value after `over` as a step at the span's end (`19`, FS1807).
                chunk.Lines.Add($"at {Value(span.To)}  {target} = {Value(((PointSyntax)disturbance.Value).Value)}");
            }
            else
            {
                chunk.Lines.Add($"{disturbance.Keyword.Text} {RangeOrPoint(disturbance.When)}  {target} = {RangeOrPoint(disturbance.Value)}");
            }

            _events.Add(chunk);
        }

        private string RangeOrPoint(RangeOrPointSyntax value) => value switch
        {
            PointSyntax point => Value(point.Value),
            RangeSyntax range => $"{Value(range.From)}..{Value(range.To)}",
            _ => Text(value),
        };

        private void FinishControllers()
        {
            foreach (var (name, controller) in _controllers)
            {
                var lines = controller.Chunk.Lines;
                lines.Add($"{name} controller:");

                // `34`'s parallel form, Δu = Kp·Δe + Ki·e·dt + Kd·Δ(de/dt): Ti = Kp/Ki and Td = Kd/Kp.
                var gains = controller.Declaration.Parameters.ToDictionary(p => NameResolution.Normalize(p.Name.Text), p => p.Value.Tokens is [{ Kind: TokenKind.NumberLiteral, Value: { } v }] ? v : (double?)null);
                var kp = gains.GetValueOrDefault("kp");
                foreach (var parameter in controller.Declaration.Parameters)
                {
                    var key = NameResolution.Normalize(parameter.Name.Text);
                    if (key is "ki" or "kd")
                    {
                        var gain = gains.GetValueOrDefault(key);
                        if (kp is { } p && p != 0 && gain is { } g && g != 0)
                        {
                            var time = key == "ki" ? p / g : g / p;
                            lines.Add($"  {(key == "ki" ? "ti" : "td")} = {time.ToString("0.###", CultureInfo.InvariantCulture)} s");
                        }
                        else
                        {
                            Gap($"'{name}' states {parameter.Name.Text} = {Text(parameter.Value)}: language 2 writes it as a time, and this needs a number for kp and for it");
                        }

                        continue;
                    }

                    lines.Add("  " + Parameter(parameter, name));
                }

                if (controller.Controls.Count == 0)
                {
                    Gap($"controller '{name}' controls nothing: language 2 needs its moves and reads");
                    continue;
                }

                if (controller.Controls.Count > 1)
                {
                    Gap($"controller '{name}' has {controller.Controls.Count} control lines; language 2 gives a controller one");
                }

                var control = controller.Controls[0];
                var (comments, trailing) = controller.ControlNotes[0];
                foreach (var comment in comments.Where(static c => c.Length > 0))
                {
                    lines.Add("  " + comment);
                }

                var arguments = control.Arguments.ToList();
                string? moves = control.Actuator is { } actuator ? Endpoint(actuator, null) : null;
                string? reads = control.Sensor is { } sensor ? Endpoint(sensor, null) : null;

                foreach (var argument in arguments.ToArray())
                {
                    switch (NameResolution.Normalize(argument.Name.Text))
                    {
                        case "actuate":
                            moves = Value(argument.Value);
                            arguments.Remove(argument);
                            break;
                        case "measure":
                            reads = Value(argument.Value);
                            arguments.Remove(argument);
                            break;
                        case "by":
                            arguments.Remove(argument);
                            break;
                    }
                }

                lines.Add($"  moves = {moves ?? "?"}" + (trailing is null ? string.Empty : "  " + trailing));
                lines.Add($"  reads = {reads ?? "?"}");
                foreach (var argument in arguments)
                {
                    lines.Add("  " + $"{argument.Name.Text} = {Value(argument.Value)}");
                }
            }
        }

        // ---- names and values ------------------------------------------------------------------------

        private bool TwoSided(string component)
        {
            var symbol = _model.Components.FirstOrDefault(c => c.Name == component);
            if (symbol?.Kind is not { } kind || !kind.Ports.Any(static port => port.Key == "in2"))
            {
                return false;
            }

            return symbol.Parameters.Keys.Any(static key => key.Contains('2', StringComparison.Ordinal))
                || _model.Connections.Any(c => c.From.Component == component && c.From.Port.EndsWith('2')
                    || c.To.Component == component && c.To.Port.EndsWith('2'));
        }

        private string ParameterName(string written, string component) =>
            TwoSided(component) ? Sides(written) : written;

        private string PortName(string written, string component) =>
            TwoSided(component) ? Sides(written) : written;

        private static string Sides(string written)
        {
            var match = Regex.Match(written, @"^(in|out)(\[2\])?(?<rest>(\..*)?)$");
            return match.Success
                ? $"{(match.Groups[2].Success ? "secondary" : "primary")}.{match.Groups[1].Value}{match.Groups["rest"].Value}"
                : written;
        }

        private string Value(ExpressionSyntax expression)
        {
            var tokens = expression.Tokens;
            var text = new StringBuilder();

            for (var i = 0; i < tokens.Length; i++)
            {
                if (i > 0 && tokens[i].Span.Start > tokens[i - 1].Span.End)
                {
                    text.Append(_source.Text, tokens[i - 1].Span.End, tokens[i].Span.Start - tokens[i - 1].Span.End);
                }

                text.Append(Token(tokens[i]));
            }

            // A reference to an exchanger's second side: `HX1.out[2].t` is `HX1.secondary.out.t`.
            return Regex.Replace(
                text.ToString(),
                @"\b(?<component>[A-Za-z0-9_]+)\.(?<port>(in|out)(\[2\])?)(?=\.)",
                match => TwoSided(match.Groups["component"].Value)
                    ? $"{match.Groups["component"].Value}.{Sides(match.Groups["port"].Value)}"
                    : match.Value);
        }

        private string Token(Token token)
        {
            if (token.Kind != TokenKind.QuantityLiteral || token.Unit is not { } unit)
            {
                return token.Text;
            }

            var converted = Language2Unit(unit);
            if (unit == "K")
            {
                Gap($"'{token.Text}' is an absolute temperature in kelvin, which language 2 cannot write");
            }
            else if (unit is "in" or "t")
            {
                Gap($"'{token.Text}' uses '{unit}', which language 2 does not read as a unit");
            }

            return converted == unit || !token.Text.EndsWith(unit, StringComparison.Ordinal)
                ? token.Text
                : token.Text[..^unit.Length] + converted;
        }

        private static string Language2Unit(string unit) => unit switch
        {
            "dK" => "K",
            _ => unit,
        };

        private string Text(SyntaxNode node) => _source.ToString(node.Span).Trim();

        // ---- output ----------------------------------------------------------------------------------

        private string Emit()
        {
            FinishControllers();

            var output = new List<string>();
            var prelude = _notes.Length > 0 && _statements[0] is VersionDirectiveSyntax ? _notes[0].Leading : [];
            output.AddRange(prelude);
            output.Add("fluidscript 2" + (_notes.Length > 0 && _statements[0] is VersionDirectiveSyntax && _notes[0].Trailing is { } version ? "  " + version : string.Empty));

            if (_projectName is not null || _projectSettings.Count > 0 || _projectStyle.Count > 0)
            {
                output.Add(string.Empty);
                output.AddRange(_projectHead.Comments);
                output.Add((_projectName is null ? "project:" : $"project \"{_projectName}\":") + Suffix(_projectHead.Trailing));
                foreach (var setting in _projectSettings)
                {
                    Add(output, setting, "  ");
                }

                if (_projectStyle.Count > 0)
                {
                    output.Add("  style:");
                    output.AddRange(_projectStyle.Select(static key => "    " + key));
                }
            }

            if (_lets.Count > 0)
            {
                output.Add(string.Empty);
                foreach (var let in _lets)
                {
                    Add(output, let, string.Empty);
                }
            }

            foreach (var curve in _curves)
            {
                output.Add(string.Empty);
                Add(output, curve, string.Empty);
            }

            foreach (var circuit in _circuits)
            {
                output.Add(string.Empty);
                output.AddRange(circuit.Head.Comments);
                output.Add($"circuit \"{circuit.Name}\":" + Suffix(circuit.Head.Trailing));
                output.AddRange(circuit.Settings.Select(static setting => "  " + setting));
                if (circuit.Style.Count > 0)
                {
                    output.Add("  style:");
                    output.AddRange(circuit.Style.Select(static key => "    " + key));
                }

                output.Add(string.Empty);
                foreach (var chunk in circuit.Body)
                {
                    Add(output, chunk, "  ");
                }
            }

            var dynamic = _model.Circuits.Where(static c => c.Mode == FluidMode.Dynamic).ToList();
            if (dynamic.Count > 0 || _events.Count > 0)
            {
                output.Add(string.Empty);
                output.Add("run \"Transient\":");
                var steady = _model.Circuits.Where(static c => c.Mode != FluidMode.Dynamic).Select(static c => $"\"{c.Name}\"").ToList();
                if (steady.Count > 0)
                {
                    output.Add($"  steady = [{string.Join(", ", steady)}]");
                }

                if (_start is not null)
                {
                    output.Add($"  start = {_start}");
                }

                foreach (var chunk in _events)
                {
                    Add(output, chunk, "  ");
                }
            }

            else if (_start is not null)
            {
                Gap($"start = {_start} in a file with nothing in time: language 2 states a start on a run, and this file has none");
            }

            output.AddRange(_pending.Where(static line => line.Length > 0));

            return Tidy(output) + Epilogue();
        }

        private static string Suffix(string? trailing) => trailing is null ? string.Empty : "  " + trailing;

        private static void Add(List<string> output, Chunk chunk, string indent)
        {
            foreach (var comment in chunk.Comments)
            {
                output.Add(comment.Length == 0 ? string.Empty : indent + comment);
            }

            for (var i = 0; i < chunk.Lines.Count; i++)
            {
                output.Add(indent + chunk.Lines[i] + (i == 0 ? Suffix(chunk.Trailing) : string.Empty));
            }
        }

        private static string Tidy(List<string> lines)
        {
            var text = new StringBuilder();
            var blank = true;
            var previous = string.Empty;

            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();
                if (trimmed.Length == 0)
                {
                    if (!blank && !previous.EndsWith(':'))
                    {
                        text.Append('\n');
                    }

                    blank = true;
                    continue;
                }

                text.Append(trimmed).Append('\n');
                blank = false;
                previous = trimmed.StartsWith('#') ? string.Empty : trimmed;
            }

            return text.ToString().TrimEnd('\n') + "\n";
        }
    }
}
