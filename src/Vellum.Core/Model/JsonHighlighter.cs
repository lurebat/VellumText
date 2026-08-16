using System.Collections.Immutable;

namespace Vellum;

/// <summary>
/// Highlights JSON.
/// </summary>
/// <remarks>
/// <para>
/// JSON first because it is the one language this can be <em>exact</em> about rather than
/// approximate: no token may span a line, so tokenizing a paragraph in isolation is the whole
/// answer rather than a guess with no state. It is also what people paste.
/// </para>
/// <para>
/// A property name is reported as <see cref="TokenKind.Identifier"/> rather than
/// <see cref="TokenKind.String"/>, since telling keys from values is most of what makes JSON
/// readable. The distinction is made by looking ahead for the colon, which is why it is done here
/// and not by the theme.
/// </para>
/// <para>
/// Never throws, and never refuses. Anything that is not valid JSON is reported as
/// <see cref="TokenKind.Invalid"/> and tokenizing continues, because a clipboard holds fragments
/// far more often than it holds whole documents — a single object out of the middle of a file has
/// unbalanced braces and is still worth reading.
/// </para>
/// </remarks>
public sealed class JsonHighlighter : ISyntaxHighlighter
{
    /// <summary>The shared instance.</summary>
    public static readonly JsonHighlighter Instance = new();

    /// <inheritdoc/>
    public string Language => "json";

    /// <inheritdoc/>
    public ImmutableArray<ValueSpan<TokenKind>> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var tokens = ImmutableArray.CreateBuilder<ValueSpan<TokenKind>>();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;

                continue;
            }

            switch (c)
            {
                case '"':
                    var start = i;
                    var closed = ReadString(text, ref i);

                    tokens.Add(new ValueSpan<TokenKind>(
                        start,
                        i - start,
                        closed
                            ? IsPropertyName(text, i) ? TokenKind.Identifier : TokenKind.String
                            : TokenKind.Invalid));

                    break;

                case '{' or '}' or '[' or ']' or ',' or ':':
                    tokens.Add(new ValueSpan<TokenKind>(i, 1, TokenKind.Operator));
                    i++;

                    break;

                default:
                    var wordStart = i;

                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && !IsStructural(text[i]))
                    {
                        i++;
                    }

                    var word = text.AsSpan(wordStart, i - wordStart);

                    tokens.Add(new ValueSpan<TokenKind>(wordStart, word.Length, Classify(word)));

                    break;
            }
        }

        return tokens.ToImmutable();
    }

    private static bool IsStructural(char c) => c is '{' or '}' or '[' or ']' or ',' or ':' or '"';

    /// <summary>Reads a quoted string, returning whether it was closed.</summary>
    private static bool ReadString(string text, ref int i)
    {
        // Past the opening quote.
        i++;

        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                // A backslash at the very end escapes nothing; the string is unterminated either
                // way, and stepping two would step past the end.
                i += i + 1 < text.Length ? 2 : 1;

                continue;
            }

            if (text[i] == '"')
            {
                i++;

                return true;
            }

            i++;
        }

        return false;
    }

    /// <summary>Whether the string that just ended is a property name.</summary>
    /// <remarks>
    /// Decided by the next non-space character being a colon. That is the only way to tell, and it
    /// is why a key and a value are distinguishable at all.
    /// </remarks>
    private static bool IsPropertyName(string text, int after)
    {
        while (after < text.Length && char.IsWhiteSpace(text[after]))
        {
            after++;
        }

        return after < text.Length && text[after] == ':';
    }

    private static TokenKind Classify(ReadOnlySpan<char> word) => word switch
    {
        "true" or "false" or "null" => TokenKind.Keyword,
        _ => IsNumber(word) ? TokenKind.Number : TokenKind.Invalid,
    };

    /// <summary>Whether a word is a JSON number, by the grammar rather than by guessing.</summary>
    private static bool IsNumber(ReadOnlySpan<char> word)
    {
        if (word.IsEmpty)
        {
            return false;
        }

        var i = 0;

        if (word[i] == '-')
        {
            i++;
        }

        var digits = 0;

        // JSON's integer grammar is zero, or a non-zero digit followed by any digits - so "0" is a
        // number and "01" is not. Accepting it would colour invalid JSON as though it parsed.
        if (i < word.Length && word[i] == '0')
        {
            i++;
            digits = 1;
        }
        else
        {
            while (i < word.Length && char.IsAsciiDigit(word[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (i < word.Length && word[i] == '.')
        {
            i++;
            digits = 0;

            while (i < word.Length && char.IsAsciiDigit(word[i]))
            {
                i++;
                digits++;
            }

            if (digits == 0)
            {
                return false;
            }
        }

        if (i < word.Length && (word[i] == 'e' || word[i] == 'E'))
        {
            i++;

            if (i < word.Length && (word[i] == '+' || word[i] == '-'))
            {
                i++;
            }

            digits = 0;

            while (i < word.Length && char.IsAsciiDigit(word[i]))
            {
                i++;
                digits++;
            }

            if (digits == 0)
            {
                return false;
            }
        }

        return i == word.Length;
    }
}
