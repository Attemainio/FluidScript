using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Translation;

/// <content>A controller written as one declaration (<c>D-168</c>, <c>19</c> §Controllers).</content>
/// <remarks>
/// Language 1 says a controller in two statements — the declaration with its gains, and a <c>control</c> line
/// naming what it moves and reads — and the binder reads both. The declaration keeps what belongs to the
/// controller whatever it is wired to (<c>type</c>, <c>kp</c>, <c>ti</c>, <c>td</c>, <c>action</c>); the control
/// line takes what is read in the units of what it moves or measures (<c>setpoint</c>, <c>band</c>,
/// <c>differential</c>, <c>output</c>, <c>curve</c>).
/// </remarks>
internal sealed partial class TranslationRun
{
    /// <summary>Every setting a language 2 controller has, in <c>19</c>'s order.</summary>
    private static readonly ImmutableArray<string> ControllerSettings =
        ["type", "moves", "reads", "setpoint", "band", "kp", "ti", "td", "output", "action", "differential", "curve"];

    /// <summary>What the controller's declaration keeps; the rest go on its control line.</summary>
    private static readonly ImmutableHashSet<string> DeclaredSettings = ["type", "kp", "ti", "td", "action"];

    /// <summary>The settings every type takes.</summary>
    private static readonly ImmutableArray<string> CommonSettings = ["type", "moves", "reads", "setpoint", "output", "action"];

    /// <summary>The settings each type adds to the common ones, by the type's normalised spelling (<c>19</c>'s table).</summary>
    private static readonly ImmutableDictionary<string, ImmutableArray<string>> TypeSettings =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            ["p"] = ["band", "kp"],
            ["pi"] = ["band", "kp", "ti"],
            ["pid"] = ["band", "kp", "ti", "td"],
            ["onoff"] = ["differential"],
            ["curve"] = ["curve"],
        }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>A declaration, and for a controller the control line it implies.</summary>
    private IEnumerable<StatementSyntax> Declare(ComponentDeclarationSyntax declaration, ImmutableArray<ParameterSyntax> body)
    {
        if (_kinds.GetValueOrDefault(declaration.Name.Text)?.Keyword != "controller")
        {
            yield return Declaration(declaration, body);
            yield break;
        }

        var settings = Controller(declaration, [.. declaration.Parameters, .. body]);

        yield return Declaration(
            declaration with { Parameters = [] },
            [.. settings.Where(static setting => DeclaredSettings.Contains(NameResolution.Normalize(setting.Name.Text)))]);

        if (ControlLine(declaration, settings) is { } control)
        {
            yield return control;
        }
    }

    /// <summary>Checks a controller's settings against its type, and keeps the ones that belong.</summary>
    /// <returns>The settings, less those reported: an unknown one (<c>FS1503</c>) and one its type does not have (<c>FS1808</c>).</returns>
    private List<ParameterSyntax> Controller(ComponentDeclarationSyntax declaration, ImmutableArray<ParameterSyntax> written)
    {
        var name = declaration.Name.Text;
        var kept = new List<ParameterSyntax>();

        foreach (var setting in written)
        {
            if (!ControllerSettings.Contains(Key(setting)))
            {
                Unknown("controller", setting, string.Join(", ", ControllerSettings));
                continue;
            }

            kept.Add(setting);
        }

        var typeSetting = kept.FirstOrDefault(static setting => Key(setting) == "type");
        var typeWritten = typeSetting?.Value is ReferenceSyntax { Parts.IsDefaultOrEmpty: true } word ? word.Head.Token.Text : "PI";

        // An unknown type is the binder's `FS1514`, against the registry's list; nothing here can say which
        // settings it would have had.
        if (!TypeSettings.TryGetValue(NameResolution.Normalize(typeWritten), out var own))
        {
            return kept;
        }

        var allowed = CommonSettings.Concat(own).Where(setting => !(own.Contains("curve") && setting == "setpoint")).ToList();

        foreach (var setting in kept.ToArray())
        {
            if (!allowed.Contains(Key(setting)))
            {
                Report(
                    Language2Diagnostics.ControllerSettingNotOfType,
                    setting.Name.Span,
                    ("controller", name),
                    ("type", typeWritten),
                    ("parameter", setting.Name.Text),
                    ("available", string.Join(", ", allowed)));
                kept.Remove(setting);
            }
        }

        if (kept.FirstOrDefault(static setting => Key(setting) == "band") is not null
            && kept.FirstOrDefault(static setting => Key(setting) == "kp") is { } kp)
        {
            Report(Language2Diagnostics.BandAndGain, kp.Name.Span, ("controller", name));
            kept.Remove(kp);
        }

        if (NameResolution.Normalize(typeWritten) != "pi")
        {
            Report(
                Language2Diagnostics.ControllerTypeNotRun,
                typeSetting?.Value.Span ?? declaration.Kind.Span,
                ("controller", name),
                ("type", typeWritten));
        }

        return kept;
    }

    /// <summary>The control line a controller's <c>moves</c> and <c>reads</c> imply, in language 1's short form (<c>D-61</c>).</summary>
    /// <returns>
    /// The line, or <see langword="null"/> when <c>moves</c> or <c>reads</c> is missing (<c>FS1521</c>) or unreadable, and
    /// for a <c>curve</c> controller reading a driver or the clock, which language 1's line cannot name: that
    /// controller binds as a declaration, and <c>FS1810</c> already says it is not run.
    /// </returns>
    private ControlBindingSyntax? ControlLine(ComponentDeclarationSyntax declaration, List<ParameterSyntax> settings)
    {
        var moves = settings.FirstOrDefault(static setting => Key(setting) == "moves");
        var reads = settings.FirstOrDefault(static setting => Key(setting) == "reads");

        if (moves is null || reads is null)
        {
            Report(
                BinderDiagnostics.ControlMissingArgument,
                declaration.Span,
                ("list", "moves, reads"),
                ("missing", string.Join(", ", new[] { moves is null ? "moves" : null, reads is null ? "reads" : null }.OfType<string>())));
            return null;
        }

        if (reads.Value is ReferenceSyntax { Parts.IsDefaultOrEmpty: true } bare
            && (bare.Head.Token.Text == "time" || IsLet(bare.Head.Token.Text)))
        {
            return null;
        }

        if (Endpoint(moves, "a component, such as TV1, or a parameter, such as BLR.power") is not { } actuator
            || Endpoint(reads, "a sensor, such as TE1, or a property, such as N2.t") is not { } sensor)
        {
            return null;
        }

        var at = declaration.Span.Start;

        return new ControlBindingSyntax(
            Keyword(ReservedWord.Control, "control", at),
            [.. settings.Where(static setting => !DeclaredSettings.Contains(Key(setting)) && Key(setting) is not ("moves" or "reads")).Select(Parameter)])
        {
            Actuator = actuator,
            WithKeyword = Made(TokenKind.Identifier, "with", new TextSpan(actuator.Span.End, 0)),
            Sensor = sensor,
            ByKeyword = Made(TokenKind.Identifier, "by", new TextSpan(sensor.Span.End, 0)),
            Controller = Identifier(declaration.Name.Text, new TextSpan(sensor.Span.End, 0)),
        };
    }

    /// <summary>A <c>moves</c> or <c>reads</c> value as an endpoint: a name, or a name and a property.</summary>
    private EndpointSyntax? Endpoint(ParameterSyntax setting, string available)
    {
        if (setting.Value is not ReferenceSyntax reference)
        {
            return Rejected<EndpointSyntax>(setting, available);
        }

        return reference.Parts.IsDefaultOrEmpty
            ? new EndpointSyntax(reference.Head, null, null)
            : new EndpointSyntax(reference.Head, reference.Parts[0].Dot, new QualifiedNameSyntax(reference.Parts[0].Name, reference.Parts[1..]));
    }

    private static string Key(ParameterSyntax setting) => NameResolution.Normalize(setting.Name.Text);

    private bool IsLet(string name) =>
        _lets.OfType<LetBindingSyntax>().Any(let => string.Equals(let.Name.Text, name, StringComparison.Ordinal));
}
