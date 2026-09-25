using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
    // ---- steps 1-3: declarations, kinds, parameters ---------------------------------------------

    private void CollectDeclarations(List<CircuitBlock> blocks)
    {
        // I7 (D-110): the implicit pipe each connection on a line carrying properties lowers to, declared with
        // the components so its parameters are evaluated with everyone else's. Named after its two ends as an
        // I2 node is, with an ordinal when the pair recurs; the line's properties are its stated parameters,
        // bound against the pipe's registry entry like a declaration's. A length it does not state is zero,
        // the factory's decided default for an implicit pipe. BindConnections wires it in by its key: the
        // line's position and the pair's index, which is what makes the name stable for the same script.
        void DeclareImplicitPipes(ConnectionSyntax connection, string circuit)
        {
            var endpoints = connection.Endpoints;

            for (var i = 0; i + 1 < endpoints.Length; i++)
            {
                var stem = $"{endpoints[i].Component.Token.Text}__{endpoints[i + 1].Component.Token.Text}";
                var name = stem;

                for (var ordinal = 2; _componentsByName.ContainsKey(name); ordinal++)
                {
                    name = $"{stem}_{ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                }

                var kind = registry.Resolve("pipe") is KindResolution.Exact exact ? exact.Kind : null;
                var key = $"{connection.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                var pipe = new ComponentSymbol
                {
                    Name = name,
                    Origin = new Origin.Inferred("I7", key),
                    Kind = kind,
                    WrittenKind = "pipe",
                    Parameters = BindParameters(connection.Parameters, kind, name),
                    // The connection line is the pipe's declaration (C-97): a click on the drawn pipe lands there.
                    DeclarationSpan = connection.Span,
                    CircuitName = circuit,
                    Ports = [.. (kind?.Ports ?? []).Select(static port => port.Key)],
                };

                Register(pipe, null);
                Report(BinderDiagnostics.ComponentInferred, connection.Span, ("kind", "pipe"), ("name", name), ("rule", "I7"));
            }
        }

        foreach (var block in blocks)
        {
            foreach (var statement in block.Statements)
            {
                switch (statement)
                {
                    case LetBindingSyntax let:
                        DeclareBinding(let, block.Circuit!.Name);
                        break;

                    case ComponentDeclarationSyntax declaration:
                        DeclareComponent(declaration, block.Circuit!.Name);
                        break;

                    case ConnectionSyntax { Parameters.Length: > 0 } connection:
                        DeclareImplicitPipes(connection, block.Circuit!.Name);
                        break;

                    default:
                        break;
                }
            }
        }

        // I1, brought forward (L-63): an endpoint naming nothing declared is a node, and it was one
        // only once the topology pass ran -- after every expression had been evaluated, so `N3.t` on a
        // node the script never declared was "nothing named". The node is declared here, with every
        // `let` and component already known, and the topology pass finds it as it would a declared one.
        // A name a `let` holds is left for the topology pass to refuse (FS1523), not absorbed.
        foreach (var block in blocks)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is not ConnectionSyntax connection)
                {
                    continue;
                }

                foreach (var endpoint in connection.Endpoints)
                {
                    var name = endpoint.Component.Token.Text;

                    if (!_componentsByName.ContainsKey(name) && !_bindingsByName.ContainsKey(name))
                    {
                        Infer(name, "I1", block.Circuit!.Name, endpoint.Span);
                    }
                }
            }
        }
    }

    private void DeclareBinding(LetBindingSyntax let, string circuitName)
    {
        var name = let.Name.Text;

        if (Constants.TryGet(name, out _))
        {
            Report(
                BinderDiagnostics.BuiltInConstantRedefined,
                let.Span,
                ("name", name),
                ("what", Constants.Describe(name)),
                ("value", Constants.Spell(name)));
            return;
        }

        if (_bindingsByName.TryGetValue(name, out var existing))
        {
            Report(
                BinderDiagnostics.DuplicateBinding,
                let.Span,
                ("name", name),
                ("line", LineOf(existing.Declaration.Span)));
            return;
        }

        _bindingCircuits[name] = circuitName;

        var id = new ValueId.Let(name);
        _bindingsByName[name] = new BindingSlot(let, id);
        _graph.Add(id);
        _pending[id] = new PendingValue(let.Value, id, let.Span, null);
    }

    private void DeclareComponent(ComponentDeclarationSyntax declaration, string circuitName)
    {
        var name = declaration.Name.Text;

        if (_componentsByName.TryGetValue(name, out var existing))
        {
            Report(
                BinderDiagnostics.DuplicateComponent,
                declaration.Span,
                ("name", name),
                ("line", LineOf(_components[existing.Index].DeclarationSpan ?? declaration.Span)));
            return;
        }

        var kind = ResolveKind(declaration);

        // `D-61`: `at` places an observer on a node, and only an observer. A component that carried
        // flow and claimed to observe a node at the same time is a shape no later stage represents,
        // and an instrument nothing placed observes nothing at all.
        if (declaration.AttachedTo is not null && kind is { IsObserver: false })
        {
            Report(
                BinderDiagnostics.NotAnObserver,
                declaration.Span,
                ("name", name),
                ("kind", kind.Keyword));
        }
        else if (declaration.AttachedTo is null && kind is { IsObserver: true })
        {
            Report(BinderDiagnostics.ObserverNotPlaced, declaration.Span, ("name", name));
        }

        var parameters = BindParameters(declaration.Parameters, kind, name);
        DeclareSizingPoint(declaration, name, parameters);

        var symbol = new ComponentSymbol
        {
            Name = name,
            Origin = new Origin.Declared(),
            Kind = kind,
            WrittenKind = declaration.Kind.Token.Text,
            Parameters = parameters,
            DeclarationSpan = declaration.Span,
            CircuitName = circuitName,
            AttachedTo = declaration.AttachedTo?.Text,
            Style = StyleOf(declaration),
        };

        Register(symbol, declaration);
    }

    /// <summary>Adds a component to the symbol table: its slot is its index in the component list, and the name map claims it in the same step, so the two can never disagree.</summary>
    /// <param name="symbol">The component.</param>
    /// <param name="declaration">Its declaration, or <see langword="null"/> when a rule inferred it.</param>
    /// <returns>The slot.</returns>
    private ComponentSlot Register(ComponentSymbol symbol, ComponentDeclarationSyntax? declaration)
    {
        _components.Add(symbol);
        var slot = new ComponentSlot(_components.Count - 1, declaration);
        _componentsByName[symbol.Name] = slot;
        return slot;
    }

    private ComponentKindInfo? ResolveKind(ComponentDeclarationSyntax declaration)
    {
        var written = declaration.Kind.Token.Text;
        var span = declaration.Kind.Span;

        switch (registry.Resolve(written))
        {
            case KindResolution.Exact exact:
                return exact.Kind;

            // `D-170`: language 2 binds a kind only by its spelling or a curated alias; the near miss is the fix.
            case KindResolution.Similar similar when parse.Language == 2:
                Report(
                    BinderDiagnostics.UnknownKind,
                    span,
                    new Suggestion($"Change it to '{similar.Kind.Keyword}'", span, similar.Kind.Keyword),
                    ("kind", written));
                return null;

            case KindResolution.Similar similar:
                Report(
                    BinderDiagnostics.ResolvedBySimilarity,
                    span,
                    ("written", written),
                    ("canonical", similar.Kind.Keyword));
                return similar.Kind;

            case KindResolution.Ambiguous ambiguous:
                Report(
                    BinderDiagnostics.AmbiguousKind,
                    span,
                    ("written", written),
                    ("first", ambiguous.Candidates[0].Keyword),
                    ("second", ambiguous.Candidates[1].Keyword));
                return null;

            case KindResolution.Unknown { SuggestedKeyword: { } suggestion }:
                Report(
                    BinderDiagnostics.UnknownKind,
                    span,
                    new Suggestion($"Change it to '{suggestion}'", span, suggestion),
                    ("kind", written));
                return null;

            default:
                Report(BinderDiagnostics.UnknownKind, span, ("kind", written));
                return null;
        }
    }

    private ImmutableDictionary<string, ParameterValue> BindParameters(
        ImmutableArray<ParameterSyntax> parameters,
        ComponentKindInfo? kind,
        string componentName)
    {
        var bound = ImmutableDictionary.CreateBuilder<string, ParameterValue>(StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            var written = parameter.Name.Text;

            // `style=name` is presentation every kind accepts (D-104); it is read by DeclareComponent
            // and is not a registry parameter, so it is neither bound nor reported here.
            if (string.Equals(written, "style", StringComparison.Ordinal))
            {
                continue;
            }

            // With no kind there is nothing to check a parameter against, so it is kept as written and
            // nothing is reported: the user already has one error on this line about the kind, and a
            // second one per parameter would bury it.
            if (kind is null)
            {
                bound[written] = new ParameterValue
                {
                    WrittenName = written,
                    Expression = parameter.Value,
                    Span = parameter.Span,
                };
                continue;
            }

            if (ResolveParameter(kind, written, parameter) is not { } info)
            {
                continue;
            }

            var value = BindParameterValue(kind, info, parameter, componentName, written);
            if (value is not null)
            {
                // Stored under the key, which is what every reader of a component's stated
                // parameters has always used; the name is the script's and the docs' (`D-120`).
                bound[info.Key] = value;
            }
        }

        return bound.ToImmutable();
    }

    /// <summary>The style a declaration carries: the one in force where it was written, then its own <c>style=</c>.</summary>
    private StyleSpec? StyleOf(ComponentDeclarationSyntax declaration)
    {
        var style = _styleAt.GetValueOrDefault(declaration);

        foreach (var parameter in declaration.Parameters)
        {
            if (!string.Equals(parameter.Name.Text, "style", StringComparison.Ordinal))
            {
                continue;
            }

            var name = (parameter.Value as ReferenceSyntax)?.Head.Token.Text ?? parse.Source.ToString(parameter.Value.Span).Trim();

            if (_styleDefinitions.TryGetValue(name, out var defined))
            {
                style = (style ?? StyleSpec.Empty).Merge(defined);
            }
            else
            {
                Report(StyleDiagnostics.UndefinedStyle, parameter.Span, ("name", name));
            }
        }

        return style;
    }

    private void ReadStyle(StyleDirectiveSyntax style)
    {
        var reported = (DiagnosticDescriptor descriptor, TextSpan span, (string Name, string Value)[] arguments) => Report(descriptor, span, arguments);

        if (style.Name is { } name)
        {
            if (_styleDefinitions.ContainsKey(name.Text))
            {
                Report(StyleDiagnostics.RedefinedStyle, name.Span, ("name", name.Text));
            }

            _styleDefinitions[name.Text] = StyleTokens.Classify(style.Parts, reported);
            return;
        }

        // A single bare word that names a defined style applies it; any other token list is an
        // anonymous style read for what its tokens are.
        if (style.Parts is [{ Kind: StyleTokenKind.Word } word] && !NamedColours.TryGet(word.Text, out _)
            && word.Text is not ("fillet" or "round" or "sharp"))
        {
            if (_styleDefinitions.TryGetValue(word.Text, out var defined))
            {
                _currentStyle = _currentStyle.Merge(defined);
            }
            else
            {
                Report(StyleDiagnostics.UndefinedStyle, word.Span, ("name", word.Text));
            }

            return;
        }

        _currentStyle = _currentStyle.Merge(StyleTokens.Classify(style.Parts, reported));
    }
}
