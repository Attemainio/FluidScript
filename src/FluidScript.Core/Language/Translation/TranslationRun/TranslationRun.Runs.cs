using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;

namespace FluidScript.Core.Language.Translation;

/// <content>A run (<c>D-169</c>, <c>19</c> §Runs).</content>
/// <remarks>
/// Language 1 has no statement for a run — its schedule, its <c>start=</c> and its dynamic circuits are the
/// file's, not a run's — so the block reaches the binder as it was written, its values in language 1's
/// spelling, and the binder binds it to a <see cref="Binding.Symbols.RunSymbol"/> of its own.
/// </remarks>
internal sealed partial class TranslationRun
{
    private BlockSyntax TranslateRun(BlockSyntax run) =>
        run with
        {
            Body =
            [
                .. run.Body.Select(line => line switch
                {
                    SettingLineSyntax settings => settings with { Assignments = [.. settings.Assignments.Select(Parameter)] },
                    DisturbanceSyntax change => change with
                    {
                        When = Span(change.When),
                        Target = change.Target.Port is { } port ? change.Target with { Port = PortName(port) } : change.Target,
                        Value = Span(change.Value),
                    },
                    _ => line,
                }),
            ],
        };

    /// <summary>An event's time or value, a ramp's upper unit on its bare lower end (<c>30..40 min</c>, <c>85..75 C</c>).</summary>
    private RangeOrPointSyntax Span(RangeOrPointSyntax written) => written switch
    {
        PointSyntax point => point with { Value = Value(point.Value) },
        RangeSyntax range => Ends(new RangeExpressionSyntax(range.From, range.DotDot, range.To)) is var (from, to)
            ? range with { From = from, To = to }
            : range,
        _ => written,
    };
}
