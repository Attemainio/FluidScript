using System.Collections.Immutable;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Core.Model;

public static partial class ModelContractBuilder
{
    /// <summary>Renders diagnostics in <c>44</c>'s wire shape: both position forms from one line index, ordered by severity, then offset, then code.</summary>
    /// <param name="source">The text the spans index.</param>
    /// <param name="diagnostics">The diagnostics, in production order.</param>
    /// <param name="components">The components on the wire, so a diagnostic that names none but sits on a declaration is attributed to it; empty when there is no model.</param>
    /// <returns>The wire records, a diagnostic with no span last within its severity.</returns>
    /// <remarks>
    /// Few producers set <see cref="Diagnostic.ComponentName"/>, yet nearly every diagnostic about a
    /// component is raised on its declaration's span. The log, the canvas badge and the hover card key on
    /// the wire's <c>component</c> (<c>44</c>, <c>54</c>, <c>56</c>), so the declared component whose span
    /// holds the diagnostic's start stands in where the producer said nothing. An inferred component has
    /// no span and is never chosen; a diagnostic on its declaring line is attributed to the declared one.
    /// </remarks>
    public static ImmutableArray<DiagnosticWire> Diagnostics(SourceText source, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<ComponentWire> components = default) =>
    [
        .. diagnostics
            .OrderBy(static diagnostic => diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => 0,
                DiagnosticSeverity.Warning => 1,
                _ => 2,
            })
            .ThenBy(static diagnostic => diagnostic.Span?.Start ?? int.MaxValue)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .Select(diagnostic => new DiagnosticWire
            {
                Code = diagnostic.Code,
                Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                Message = diagnostic.Message,
                Range = diagnostic.Span is { } span ? Range(source, span) : null,
                Component = diagnostic.ComponentName ?? Owner(components, diagnostic.Span),
                Suggestion = diagnostic.Suggestion is { } fix
                    ? new SuggestionWire(fix.Title, Range(source, fix.Span), fix.Replacement)
                    : null,
                Related = [.. diagnostic.Related.Select(related => new RelatedWire(related.Message, Range(source, related.Span)))],
            }),
    ];

    /// <summary>The declared component whose source span holds the start of <paramref name="span"/>, or <see langword="null"/>.</summary>
    private static string? Owner(ImmutableArray<ComponentWire> components, TextSpan? span)
    {
        if (span is not { } at || components.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var component in components)
        {
            // A declared component only: an implicit pipe carries its connection line (C-97), and a
            // diagnostic on that line is about whatever it names, not about the pipe.
            if (component.Origin == "declared" && component.SourceSpan is { } declared && declared.Start <= at.Start && at.Start < declared.Start + declared.Length)
            {
                return component.Id;
            }
        }

        return null;
    }

    /// <summary>Both position forms from the one line index (<c>44</c>).</summary>
    private static RangeWire Range(SourceText source, TextSpan span)
    {
        var start = Math.Clamp(span.Start, 0, source.Length);
        var end = Math.Clamp(span.End, start, source.Length);
        var first = source.GetLinePosition(start);
        var last = source.GetLinePosition(end);

        return new RangeWire
        {
            Start = new PositionWire(first.Line, first.Character),
            End = new PositionWire(last.Line, last.Character),
            Offset = start,
            Length = end - start,
        };
    }
}
