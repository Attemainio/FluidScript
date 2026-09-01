using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace FluidScript.Core.Diagnostics;

/// <summary>
/// The permanent definition of one diagnostic code: its severity, the stage that owns it, and the
/// message it renders.
/// </summary>
/// <remarks>
/// <para>
/// A descriptor is the only place a code's severity and wording are stated, which is what makes the
/// registry's both-directions invariant checkable: a code invented at an ad-hoc emit site has no
/// descriptor, and therefore no message to emit.
/// </para>
/// <para>
/// Codes are permanent. A code is never reused for a different meaning and never renumbered, because
/// scripts, tests, <c>/docs</c> pages and agent prompts all reference them. Retiring one is
/// <see cref="RetiredDiagnostic"/>'s job rather than a state of this type — a retired code has no
/// severity and no message, and modelling it here with both left blank would put an unemittable
/// value into every consumer's path.
/// </para>
/// </remarks>
/// <seealso cref="DiagnosticRegistry"/>
public sealed class DiagnosticDescriptor
{
    private const int CodeLength = 6;
    private const string CodePrefix = "FS";

    private readonly ImmutableArray<TemplateSegment> _segments;

    /// <summary>Initializes a descriptor for one diagnostic code.</summary>
    /// <param name="code">The code: <c>FS</c> followed by exactly four digits.</param>
    /// <param name="severity">How much a diagnostic carrying this code affects the element it is about.</param>
    /// <param name="messageTemplate">
    /// The message, with named placeholders in braces — <c>'{ch}' is not valid here.</c> — written to
    /// the style rules in <c>plan/10-language/16-diagnostics.md</c>. Write <c>{{</c> and <c>}}</c> for
    /// a literal brace.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The code is not <c>FS</c> plus four digits, its first two digits are not an allocated
    /// <see cref="DiagnosticStage"/>, the template is blank, or the template's braces are unbalanced
    /// or name an empty placeholder. Each of these is a mistake in the descriptor's own source rather
    /// than in a script, so it fails at construction — which happens once, as the registry is built.
    /// </exception>
    public DiagnosticDescriptor(string code, DiagnosticSeverity severity, string messageTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTemplate);

        Stage = StageOf(code);
        Code = code;
        Severity = severity;
        MessageTemplate = messageTemplate;
        _segments = ParseTemplate(messageTemplate);
        ArgumentNames =
        [
            .. _segments.Where(static segment => segment.IsPlaceholder)
                .Select(static segment => segment.Text)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>Gets the stable code this descriptor defines.</summary>
    /// <value><c>FS</c> followed by four digits, for example <c>FS1302</c>.</value>
    public string Code { get; }

    /// <summary>Gets the severity every diagnostic carrying this code is emitted with.</summary>
    /// <value>Fixed per code: one code is never emitted with two different severities.</value>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Gets the message with its placeholders still in place.</summary>
    /// <value>The template as written, which is the form the generated <c>/docs</c> page shows.</value>
    public string MessageTemplate { get; }

    /// <summary>Gets the pipeline stage that owns this code.</summary>
    /// <value>
    /// Derived from <see cref="Code"/> rather than supplied, so a code and its stage cannot disagree.
    /// </value>
    public DiagnosticStage Stage { get; }

    /// <summary>Gets the placeholder names the template expects, in order of first appearance.</summary>
    /// <value>
    /// Empty for a message with no substitutions. Names are distinct even where one repeats in the
    /// template.
    /// </value>
    public ImmutableArray<string> ArgumentNames { get; }

    /// <summary>Renders the message with its arguments substituted.</summary>
    /// <param name="arguments">
    /// Named values for the template's placeholders, in any order. Extra arguments are ignored, so a
    /// shared helper supplying more than one code needs does not become a special case.
    /// </param>
    /// <returns>
    /// The finished message. A placeholder with no matching argument is left in the output verbatim,
    /// braces and all, rather than throwing: a stage emitting a diagnostic while a user types must
    /// not be the thing that stops the pipeline, and a visible <c>{name}</c> is caught by the
    /// registry's own coverage test long before a user sees it.
    /// </returns>
    public string Render(params ReadOnlySpan<DiagnosticArgument> arguments)
    {
        // A template with no placeholders parses to exactly one literal segment, whose text already
        // has any doubled braces collapsed -- which the raw template does not.
        if (_segments.Length == 1 && !_segments[0].IsPlaceholder)
        {
            return _segments[0].Text;
        }

        var builder = new StringBuilder(MessageTemplate.Length);
        foreach (var segment in _segments)
        {
            builder.Append(segment.IsPlaceholder ? Substitute(segment.Text, arguments) : segment.Text);
        }

        return builder.ToString();
    }

    private static string Substitute(string name, ReadOnlySpan<DiagnosticArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            if (string.Equals(argument.Name, name, StringComparison.Ordinal))
            {
                return argument.Value;
            }
        }

        return $"{{{name}}}";
    }

    private static DiagnosticStage StageOf(string code)
    {
        if (code.Length != CodeLength
            || !code.StartsWith(CodePrefix, StringComparison.Ordinal)
            || !int.TryParse(
                code.AsSpan(CodePrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var digits))
        {
            throw new ArgumentException(
                $"'{code}' is not a diagnostic code. A code is 'FS' followed by exactly four digits.",
                nameof(code));
        }

        var stage = (DiagnosticStage)(digits / 100);
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentException(
                $"'{code}' falls in the unallocated range FS{digits / 100:D2}xx. Allocate the range as "
                + $"a {nameof(DiagnosticStage)} member before taking a code from it.",
                nameof(code));
        }

        return stage;
    }

    private static ImmutableArray<TemplateSegment> ParseTemplate(string template)
    {
        var segments = ImmutableArray.CreateBuilder<TemplateSegment>();
        var literal = new StringBuilder();

        for (var index = 0; index < template.Length; index++)
        {
            var current = template[index];

            // A doubled brace escapes one literal brace, as in composite formatting, so a message
            // that has to show a brace stays writable.
            if (current is '{' or '}' && index + 1 < template.Length && template[index + 1] == current)
            {
                literal.Append(current);
                index++;
                continue;
            }

            if (current == '}')
            {
                throw new ArgumentException(
                    $"Message template '{template}' closes a placeholder that was never opened.",
                    nameof(template));
            }

            if (current != '{')
            {
                literal.Append(current);
                continue;
            }

            var close = template.IndexOf('}', index + 1);
            var name = close < 0 ? string.Empty : template[(index + 1)..close];
            if (name.Length == 0)
            {
                throw new ArgumentException(
                    $"Message template '{template}' has an unterminated or empty placeholder.",
                    nameof(template));
            }

            if (literal.Length > 0)
            {
                segments.Add(new TemplateSegment(literal.ToString(), IsPlaceholder: false));
                literal.Clear();
            }

            segments.Add(new TemplateSegment(name, IsPlaceholder: true));
            index = close;
        }

        if (literal.Length > 0)
        {
            segments.Add(new TemplateSegment(literal.ToString(), IsPlaceholder: false));
        }

        return segments.ToImmutable();
    }

    private readonly record struct TemplateSegment(string Text, bool IsPlaceholder);
}
