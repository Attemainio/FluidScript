using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Units;

namespace FluidScript.Core.Syntax;

/// <summary>Turns script text into tokens, keeping every character.</summary>
/// <remarks>
/// <para>
/// The lexer never throws on any input (principle P4). A character it does not recognise becomes an
/// <see cref="TokenKind.Unknown"/> token and an <c>FS1002</c>; an unterminated string becomes a
/// <see cref="TokenKind.StringLiteral"/> token ending at the line break and an <c>FS1001</c>. Both are
/// ordinary results, because a script under editing is malformed most of the time.
/// </para>
/// <para>
/// <strong>It is lossless.</strong> Every character of the input lands in exactly one token or one
/// trivium, so concatenating them in order reproduces the source byte for byte. That is asserted
/// directly, over the sample corpus and over every <c>fluidscript</c> block in <c>plan/</c> and
/// <c>/docs</c>.
/// </para>
/// <para>
/// <strong>The only lookahead is one token wide.</strong> A unit symbol is recognised solely by
/// following a number, and rejected when an <c>=</c> follows it — which is what keeps
/// <c>power=30 in=20</c> from reading as thirty inches. Nothing else in the lexer depends on context.
/// </para>
/// </remarks>
public static class Lexer
{
    /// <summary>Lexes a script.</summary>
    /// <param name="source">The text to lex. Any characters at all; may be empty.</param>
    /// <returns>
    /// The tokens, always ending in <see cref="TokenKind.EndOfFile"/>, and whatever diagnostics the
    /// text produced.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static LexResult Lex(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var scanner = new Scanner(source);
        var tokens = ImmutableArray.CreateBuilder<Token>();

        while (true)
        {
            var leading = scanner.ScanTrivia(stopAtLineBreak: false);
            var token = scanner.ScanToken();
            var trailing = scanner.ScanTrivia(stopAtLineBreak: true);

            tokens.Add(token with { LeadingTrivia = leading, TrailingTrivia = trailing });

            if (token.Kind == TokenKind.EndOfFile)
            {
                break;
            }
        }

        return new LexResult(source, tokens.ToImmutable(), scanner.Diagnostics);
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    // The grammar's letter is ASCII. A word may start with a digit -- 3WV is a legal name -- so the
    // start rule differs from the continuation rule only in excluding digits, and the digit case is
    // reached through the numeric scan instead.
    private static bool IsWordStart(char c) => IsLetter(c) || c == '_';

    private static bool IsWordChar(char c) => IsWordStart(c) || IsDigit(c);

    private sealed class Scanner(SourceText source)
    {
        private readonly ImmutableArray<Diagnostic>.Builder _diagnostics =
            ImmutableArray.CreateBuilder<Diagnostic>();

        private readonly List<Trivia> _trivia = [];
        private int _position;

        public ImmutableArray<Diagnostic> Diagnostics => _diagnostics.ToImmutable();

        private int Length => source.Length;

        public ImmutableArray<Trivia> ScanTrivia(bool stopAtLineBreak)
        {
            _trivia.Clear();

            while (_position < Length)
            {
                var start = _position;
                var c = source[_position];

                if (c is ' ' or '\t')
                {
                    while (_position < Length && source[_position] is ' ' or '\t')
                    {
                        _position++;
                    }

                    _trivia.Add(new Trivia(TriviaKind.Whitespace, TextSpan.FromBounds(start, _position)));
                }
                else if (c == '#')
                {
                    while (_position < Length && source[_position] is not ('\n' or '\r'))
                    {
                        _position++;
                    }

                    _trivia.Add(new Trivia(TriviaKind.Comment, TextSpan.FromBounds(start, _position)));
                }
                else if (c is '\n' or '\r')
                {
                    if (stopAtLineBreak)
                    {
                        break;
                    }

                    _position += c == '\r' && _position + 1 < Length && source[_position + 1] == '\n' ? 2 : 1;
                    _trivia.Add(new Trivia(TriviaKind.EndOfLine, TextSpan.FromBounds(start, _position)));
                }
                else
                {
                    break;
                }
            }

            return _trivia.Count == 0 ? [] : [.. _trivia];
        }

        public Token ScanToken()
        {
            if (_position >= Length)
            {
                return new Token
                {
                    Kind = TokenKind.EndOfFile,
                    Span = new TextSpan(Length, 0),
                    Text = string.Empty,
                };
            }

            var c = source[_position];

            if (c == '"')
            {
                return ScanString();
            }

            if (IsDigit(c))
            {
                return ScanNumeric();
            }

            if (IsWordStart(c))
            {
                return ScanWord();
            }

            return ScanPunctuation(c);
        }

        private Token ScanPunctuation(char c)
        {
            var start = _position;

            // A '.' is two tokens' worth of decision and no more: '..' is the range and the dotted
            // pattern, a lone '.' qualifies a port. Neither needs to look past the second character.
            if (c == '.' && _position + 1 < Length && source[_position + 1] == '.')
            {
                _position += 2;
                return Make(TokenKind.DotDot, start);
            }

            var kind = c switch
            {
                '=' => TokenKind.Equals,
                '-' => TokenKind.Minus,
                '+' => TokenKind.Plus,
                '*' => TokenKind.Star,
                '/' => TokenKind.Slash,
                '.' => TokenKind.Dot,
                ',' => TokenKind.Comma,
                '(' => TokenKind.OpenParenthesis,
                ')' => TokenKind.CloseParenthesis,
                '@' => TokenKind.At,
                ':' => TokenKind.Colon,
                _ => TokenKind.Unknown,
            };

            _position++;
            var token = Make(kind, start);

            if (kind == TokenKind.Unknown)
            {
                Report(LexerDiagnostics.UnrecognisedCharacter, token.Span, new DiagnosticArgument("ch", token.Text));
            }

            return token;
        }

        private Token ScanWord()
        {
            var start = _position;
            while (_position < Length && IsWordChar(source[_position]))
            {
                _position++;
            }

            var text = source.ToString(TextSpan.FromBounds(start, _position));

            return ReservedWords.TryMatch(text, out var word)
                ? new Token
                {
                    Kind = TokenKind.Keyword,
                    Span = TextSpan.FromBounds(start, _position),
                    Text = text,
                    Keyword = word,
                }
                : new Token
                {
                    Kind = TokenKind.Identifier,
                    Span = TextSpan.FromBounds(start, _position),
                    Text = text,
                };
        }

        private Token ScanString()
        {
            var start = _position;
            _position++;

            while (_position < Length && source[_position] is not ('"' or '\n' or '\r'))
            {
                _position++;
            }

            var terminated = _position < Length && source[_position] == '"';
            var contentEnd = _position;
            if (terminated)
            {
                _position++;
            }

            var span = TextSpan.FromBounds(start, _position);
            var token = new Token
            {
                Kind = TokenKind.StringLiteral,
                Span = span,
                Text = source.ToString(span),
                StringValue = source.ToString(TextSpan.FromBounds(start + 1, contentEnd)),
            };

            if (!terminated)
            {
                // The token stops at the line break rather than running on: invariant 6 says no token
                // spans a newline, and a string that swallowed the rest of the file would take every
                // later line's diagnostics with it -- the opposite of what an editor needs.
                Report(LexerDiagnostics.UnterminatedString, span);
            }

            return token;
        }

        private Token ScanNumeric()
        {
            var start = _position;
            ScanNumberBody();
            var numberEnd = _position;
            var numberText = source.ToString(TextSpan.FromBounds(start, numberEnd));

            // Rule 3: the unit is attached to the number.
            var attached = MatchUnit(numberEnd, rejectBeforeEquals: false);
            if (attached > 0)
            {
                return MakeQuantity(start, numberText, numberEnd, numberEnd + attached);
            }

            // Rule 4: what follows is word characters that are not a unit, so the whole run is one
            // name. 3WV gets here: 'W' is a unit but 'WV' is not, and a name is matched whole.
            //
            // The number must itself be spellable inside a name, which excludes a decimal point and an
            // exponent's sign. '1.5x' is a number and a stray name, because no name can contain a '.'.
            if (numberEnd < Length && IsWordChar(source[numberEnd]) && IsNameSpellable(numberText))
            {
                while (_position < Length && IsWordChar(source[_position]))
                {
                    _position++;
                }

                var span = TextSpan.FromBounds(start, _position);
                return new Token
                {
                    Kind = TokenKind.Identifier,
                    Span = span,
                    Text = source.ToString(span),
                };
            }

            // Rule 5: the unit is separated from its number by horizontal whitespace. Never by a line
            // break -- no token spans one -- so this cannot reach across a line to steal a word.
            var unitStart = numberEnd;
            while (unitStart < Length && source[unitStart] is ' ' or '\t')
            {
                unitStart++;
            }

            if (unitStart > numberEnd)
            {
                var spaced = MatchUnit(unitStart, rejectBeforeEquals: true);
                if (spaced > 0)
                {
                    return MakeQuantity(start, numberText, unitStart, unitStart + spaced);
                }
            }

            _position = numberEnd;
            return new Token
            {
                Kind = TokenKind.NumberLiteral,
                Span = TextSpan.FromBounds(start, numberEnd),
                Text = numberText,
                NumberText = numberText,
                Value = ParseValue(numberText),
            };
        }

        private void ScanNumberBody()
        {
            while (_position < Length && IsDigit(source[_position]))
            {
                _position++;
            }

            // A '.' joins the number only when a digit follows it (D-51). Without that, maximal munch
            // takes the first dot of '30..60' and the range production never sees its second one.
            if (_position + 1 < Length && source[_position] == '.' && IsDigit(source[_position + 1]))
            {
                _position++;
                while (_position < Length && IsDigit(source[_position]))
                {
                    _position++;
                }
            }

            if (_position < Length && source[_position] is 'e' or 'E')
            {
                var afterSign = source[_position + 1 < Length ? _position + 1 : _position] is '+' or '-'
                    ? _position + 2
                    : _position + 1;

                // Only an exponent that actually has digits is one. '1exchanger' is a name, and
                // consuming its 'e' would leave 'xchanger' to lex on its own.
                if (afterSign < Length && IsDigit(source[afterSign]))
                {
                    _position = afterSign;
                    while (_position < Length && IsDigit(source[_position]))
                    {
                        _position++;
                    }
                }
            }
        }

        /// <summary>Finds the longest unit symbol at a position that ends where a symbol may end.</summary>
        private int MatchUnit(int start, bool rejectBeforeEquals)
        {
            var available = Math.Min(UnitTable.LongestSymbolLength, Length - start);

            for (var length = available; length >= 1; length--)
            {
                var end = start + length;

                // A symbol ends at a non-word character. Without this, '30kWx' would lex as thirty
                // kilowatts followed by 'x' instead of as the single name it is.
                if (end < Length && IsWordChar(source[end]))
                {
                    continue;
                }

                // Rule 5's '=' clause, and the whole safety of the whitespace-separated form: 'in' is
                // the inch symbol and a parameter name, and only the '=' tells them apart.
                if (rejectBeforeEquals && end < Length && source[end] == '=')
                {
                    continue;
                }

                if (UnitTable.IsSymbol(source.Slice(TextSpan.FromBounds(start, end))))
                {
                    return length;
                }
            }

            return 0;
        }

        private Token MakeQuantity(int start, string numberText, int unitStart, int unitEnd)
        {
            _position = unitEnd;
            var span = TextSpan.FromBounds(start, unitEnd);

            return new Token
            {
                Kind = TokenKind.QuantityLiteral,
                Span = span,
                Text = source.ToString(span),
                NumberText = numberText,
                Unit = source.ToString(TextSpan.FromBounds(unitStart, unitEnd)),
                Value = ParseValue(numberText),
            };
        }

        private Token Make(TokenKind kind, int start)
        {
            var span = TextSpan.FromBounds(start, _position);
            return new Token { Kind = kind, Span = span, Text = source.ToString(span) };
        }

        private void Report(
            DiagnosticDescriptor descriptor, TextSpan span, params ReadOnlySpan<DiagnosticArgument> arguments) =>
            _diagnostics.Add(Diagnostic.Create(descriptor, span, arguments));

        private static bool IsNameSpellable(string text) => text.All(IsWordChar);

        private static double ParseValue(string text) =>
            // The grammar's number is a subset of what the invariant-culture parser accepts, and the
            // scan above already bounded it, so this cannot fail on anything the scan produced. An
            // overflowing literal parses to an infinity rather than throwing, which the binder reports
            // -- the lexer's job is to say where the number is, not whether it is usable.
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.NaN;
    }
}
