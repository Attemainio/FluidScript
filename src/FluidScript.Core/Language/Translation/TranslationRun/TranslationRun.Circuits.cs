using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Translation;

internal sealed partial class TranslationRun
{
    private const string CircuitSettings = "fluid, number, role, style";

    /// <summary>Records every declared component's kind and every name a chain uses, across all circuits.</summary>
    /// <remarks>Components are named globally (<c>D-41</c>), so a chain in one circuit may name a sensor declared in another.</remarks>
    private void CollectNames(List<BlockSyntax> circuits)
    {
        foreach (var (statement, circuit) in circuits.SelectMany(static (block, index) => block.Body.Select(statement => (statement, index))))
        {
            if (Declared(statement) is { } declaration)
            {
                var name = declaration.Name.Text;
                _names.Add(name);
                _declaredIn.TryAdd(name, circuit);
                _writtenKinds.TryAdd(name, declaration.Kind.Text);
                _kinds.TryAdd(
                    name,
                    registry.Resolve(declaration.Kind.Text) is KindResolution.Exact exact ? exact.Kind : null);
            }
            else if (Chain(statement) is { } chain)
            {
                foreach (var endpoint in chain.Endpoints)
                {
                    _names.Add(endpoint.Component.Text);
                }
            }
        }
    }

    /// <summary>Gives each sensor written in a chain the node it observes (<c>19</c> §Connections, <c>D-166</c>).</summary>
    /// <remarks>
    /// <c>TV1 - SP - TE1 - RAD</c> is <c>SP - N - RAD</c> with <c>TE1</c> placed <c>at</c> <c>N</c>. A sensor written in
    /// several chains is one node, so <c>SP - TE1</c> and <c>TE1 - RAD</c> on two lines say the same as one chain.
    /// </remarks>
    private void PlaceSensorsInChains(List<BlockSyntax> circuits)
    {
        foreach (var chain in circuits.SelectMany(static circuit => circuit.Body).Select(Chain).OfType<ConnectionSyntax>())
        {
            foreach (var endpoint in chain.Endpoints)
            {
                var name = endpoint.Component.Text;
                if (endpoint.Port is null && IsSensor(name) && !_sensorNodes.ContainsKey(name))
                {
                    _sensorNodes[name] = Fresh(name + "_node");
                }
            }
        }
    }

    private bool IsSensor(string name) =>
        _kinds.TryGetValue(name, out var kind) && kind?.MeasuredProperty is not null;

    /// <summary>A name no component or node in the file has, starting from the one wanted.</summary>
    private string Fresh(string wanted)
    {
        var name = wanted;
        for (var suffix = 2; !_names.Add(name); suffix++)
        {
            name = wanted + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }

    private static ComponentDeclarationSyntax? Declared(StatementSyntax statement) => statement switch
    {
        ComponentDeclarationSyntax declaration => declaration,
        BlockSyntax { Head: ComponentDeclarationSyntax declaration } => declaration,
        _ => null,
    };

    private static ConnectionSyntax? Chain(StatementSyntax statement) => statement switch
    {
        ConnectionSyntax chain => chain,
        PipedConnectionSyntax piped => piped.Connection,
        _ => null,
    };

    /// <summary>Translates one circuit block: its header, settings, style and body, in the order the binder reads them.</summary>
    private void TranslateCircuit(BlockSyntax block, bool withLets)
    {
        var head = (CircuitHeadSyntax)block.Head;

        NumberLiteralSyntax? number = null;
        IdentifierSyntax? role = null;
        FluidDirectiveSyntax? fluid = null;
        var styles = new List<StyleDirectiveSyntax>();

        foreach (var line in block.Body)
        {
            switch (line)
            {
                case SettingLineSyntax settings:
                    foreach (var setting in settings.Assignments)
                    {
                        if (Is(setting, "fluid"))
                        {
                            fluid = Fluid(setting) ?? fluid;
                        }
                        else if (Is(setting, "number"))
                        {
                            number = setting.Value as NumberLiteralSyntax
                                ?? Rejected<NumberLiteralSyntax>(setting, "a whole number, such as 200");
                        }
                        else if (Is(setting, "role"))
                        {
                            role = setting.Value is ReferenceSyntax { Parts.IsDefaultOrEmpty: true } named
                                ? named.Head
                                : Rejected<IdentifierSyntax>(setting, "a circuit role, such as heating");
                        }
                        else
                        {
                            Unknown("circuit", setting, CircuitSettings);
                        }
                    }

                    break;

                case BlockSyntax { Head: StyleHeadSyntax } style:
                    styles.Add(Style(style));
                    break;

                default:
                    break;
            }
        }

        _circuits.Add(new CircuitHeaderSyntax(head.Keyword, Title(head.Title, head.Keyword, "circuit"), number) { Role = role });

        if (fluid is not null)
        {
            _circuits.Add(fluid);
        }

        if (withLets)
        {
            _circuits.AddRange(_lets);
        }

        _circuits.AddRange(styles);

        foreach (var line in block.Body)
        {
            switch (line)
            {
                case ComponentDeclarationSyntax declaration:
                    _circuits.AddRange(Declare(declaration, []));
                    break;

                case BlockSyntax { Head: ComponentDeclarationSyntax declaration } component:
                    _circuits.AddRange(Declare(
                        declaration,
                        [.. component.Body.OfType<SettingLineSyntax>().SelectMany(static settings => settings.Assignments)]));
                    break;

                case ConnectionSyntax chain:
                    _circuits.AddRange(TranslateChain(chain, []));
                    break;

                case PipedConnectionSyntax piped:
                    // More than one link is FS1803 from the parser, and the chain binds without the pipe rather
                    // than giving every link the same one, which is what language 1 would do (`D-166`).
                    _circuits.AddRange(TranslateChain(
                        piped.Connection,
                        piped.Connection.Links.Length == 1 ? Pipe(piped.Properties) : []));
                    break;

                default:
                    break;
            }
        }
    }

    private FluidDirectiveSyntax? Fluid(ParameterSyntax setting)
    {
        var keyword = Keyword(ReservedWord.Fluid, "fluid", setting.Span.Start);

        return setting.Value switch
        {
            ReferenceSyntax { Parts.IsDefaultOrEmpty: true } named =>
                new FluidDirectiveSyntax(keyword, null, named.Head, []),
            CallSyntax call =>
                new FluidDirectiveSyntax(keyword, null, call.Name, [.. call.Arguments.Select(argument => Value(argument.Value))]),
            _ => Rejected<FluidDirectiveSyntax>(setting, "a fluid, such as water"),
        };
    }

    private T? Rejected<T>(ParameterSyntax setting, string available)
        where T : class
    {
        Report(
            BinderDiagnostics.UnacceptedSymbol,
            setting.Value.Span,
            ("parameter", setting.Name.Text),
            ("available", available),
            ("written", Text(setting.Value)));
        return null;
    }

    /// <summary>Translates a declaration, gathering a block's parameter lines onto it.</summary>
    /// <param name="declaration">The declaration line.</param>
    /// <param name="body">The <c>name = value</c> pairs indented under it, when it heads a block.</param>
    private ComponentDeclarationSyntax Declaration(
        ComponentDeclarationSyntax declaration,
        ImmutableArray<ParameterSyntax> body)
    {
        var atKeyword = declaration.AtKeyword;
        var attachedTo = declaration.AttachedTo;

        if (_sensorNodes.TryGetValue(declaration.Name.Text, out var node))
        {
            if (attachedTo is not null)
            {
                Report(
                    Language2Diagnostics.SensorPlacedTwice,
                    TextSpan.FromBounds(atKeyword!.Span.Start, attachedTo.Span.End),
                    ("sensor", declaration.Name.Text),
                    ("node", attachedTo.Text));
            }

            var at = new TextSpan(declaration.Kind.Span.End, 0);
            atKeyword = Made(TokenKind.Identifier, "at", at);
            attachedTo = Identifier(node, at);
        }

        return declaration with
        {
            AtKeyword = atKeyword,
            AttachedTo = attachedTo,
            Parameters = [.. declaration.Parameters.Concat(body).Select(Parameter)],
        };
    }

    private ParameterSyntax Parameter(ParameterSyntax parameter) =>
        parameter with { Name = PortName(parameter.Name), Value = Value(parameter.Value) };

    /// <summary>Translates a chain into one connection per link, each end with its port written out.</summary>
    /// <remarks>
    /// Language 1 reads <c>A - B - C</c> as two connections (rule I6) and gives an unnamed end a port by preference
    /// order. Language 2's ports come from the flow direction (<see cref="InferPorts"/>), and a component in the middle
    /// of a chain takes a different port on each side of it, which one language 1 endpoint cannot say — so the chain
    /// is written link by link. The pipe of a one-link line stays on that line.
    /// </remarks>
    private IEnumerable<ConnectionSyntax> TranslateChain(ConnectionSyntax chain, ImmutableArray<ParameterSyntax> pipe)
    {
        var endpoints = chain.Endpoints;

        for (var i = 0; i < chain.Links.Length; i++)
        {
            yield return new ConnectionSyntax(
                Endpoint(endpoints[i], inflow: false),
                [chain.Links[i] with { Endpoint = Endpoint(endpoints[i + 1], inflow: true) }],
                pipe);
        }
    }

    /// <summary>An endpoint with its port in language 1's spelling, or the node a sensor in the chain stands for.</summary>
    /// <param name="endpoint">The endpoint as written.</param>
    /// <param name="inflow">Whether the stream enters the component at this end.</param>
    private EndpointSyntax Endpoint(EndpointSyntax endpoint, bool inflow)
    {
        if (endpoint.Port is null && _sensorNodes.TryGetValue(endpoint.Component.Text, out var node))
        {
            return new EndpointSyntax(Identifier(node, endpoint.Component.Span), null, null);
        }

        return endpoint.Port is { } port ? endpoint with { Port = PortName(port) } : WithInferredPort(endpoint, inflow);
    }

    /// <summary>Translates a pipe's description after its link, <c>12 m  DN25  roughness = 0.05 mm</c>, into language 1's parameters (<c>D-166</c>, <c>D-110</c>).</summary>
    private ImmutableArray<ParameterSyntax> Pipe(ImmutableArray<SyntaxNode> properties)
    {
        var parameters = ImmutableArray.CreateBuilder<ParameterSyntax>();

        foreach (var property in properties)
        {
            switch (property)
            {
                case QuantityLiteralSyntax length:
                    parameters.Add(new ParameterSyntax(
                        Named("length", length.Span.Start),
                        EqualsAt(length.Span.Start),
                        Value(length)));
                    break;

                case IdentifierSyntax size when Designation(size) is { } dn:
                    parameters.Add(new ParameterSyntax(Named("dn", size.Span.Start), EqualsAt(size.Span.Start), dn));
                    break;

                case IdentifierSyntax other:
                    Report(Language2Diagnostics.NotAPipeSize, other.Span, ("text", other.Text));
                    break;

                case ParameterSyntax named:
                    parameters.Add(Parameter(named));
                    break;

                default:
                    break;
            }
        }

        return parameters.ToImmutable();
    }

    /// <summary>Reads <c>DN25</c> as the number 25, on the digits' own span.</summary>
    /// <returns>The number, or <see langword="null"/> when the word is not <c>DN</c> and digits.</returns>
    private static NumberLiteralSyntax? Designation(IdentifierSyntax size)
    {
        var text = size.Text;
        if (text.Length < 3
            || !text.StartsWith("DN", StringComparison.OrdinalIgnoreCase)
            || text.AsSpan(2).ContainsAnyExceptInRange('0', '9')
            || !int.TryParse(text.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var nominal))
        {
            return null;
        }

        var digits = text[2..];
        return new NumberLiteralSyntax(new Token
        {
            Kind = TokenKind.NumberLiteral,
            Text = digits,
            NumberText = digits,
            Value = nominal,
            Span = TextSpan.FromBounds(size.Span.Start + 2, size.Span.End),
        });
    }
}
