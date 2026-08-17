using System.Collections.Immutable;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>
/// The colours a <see cref="TokenKind"/> is drawn in.
/// </summary>
/// <remarks>
/// Separate from the highlighter because a kind is a fact about the text and a colour is a decision
/// about a theme. One tokenizer serves a light theme and a dark one; swapping this swaps the
/// palette without re-tokenizing anything.
/// </remarks>
public interface ISyntaxTheme
{
    /// <summary>The marks to apply to a token, or null to leave it as it is.</summary>
    /// <param name="kind">What the token is.</param>
    MarkSet? For(TokenKind kind);
}

/// <summary>
/// A syntax palette, with light and dark defaults.
/// </summary>
/// <remarks>
/// The colours are close to what most editors use, because syntax colour is a convention people
/// already know and an editor that invents its own makes everyone re-learn it. Only the foreground
/// is set: bolding or italicising tokens fights with a code block's own formatting, and a comment
/// rendered in italic inside a paragraph that is already italic says nothing.
/// </remarks>
public sealed class SyntaxTheme : ISyntaxTheme
{
    private readonly Dictionary<TokenKind, MarkSet> _marks;

    /// <summary>Creates a theme from a map of kinds to colours.</summary>
    /// <param name="colours">The colours. Kinds not present are left unstyled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="colours"/> is <see langword="null"/>.</exception>
    public SyntaxTheme(IReadOnlyDictionary<TokenKind, Rgba> colours)
    {
        ArgumentNullException.ThrowIfNull(colours);

        _marks = colours.ToDictionary(
            entry => entry.Key,
            entry => new MarkSet { Foreground = entry.Value });
    }

    /// <summary>The palette for a light background.</summary>
    public static SyntaxTheme Light { get; } = new(new Dictionary<TokenKind, Rgba>
    {
        [TokenKind.Keyword] = new(0, 0, 192, 255),
        [TokenKind.String] = new(163, 21, 21, 255),
        [TokenKind.Number] = new(9, 134, 88, 255),
        [TokenKind.Comment] = new(0, 128, 0, 255),
        [TokenKind.Identifier] = new(4, 81, 165, 255),
        [TokenKind.Operator] = new(80, 80, 80, 255),
        [TokenKind.Invalid] = new(205, 49, 49, 255),
    });

    /// <summary>The palette for a dark background.</summary>
    public static SyntaxTheme Dark { get; } = new(new Dictionary<TokenKind, Rgba>
    {
        [TokenKind.Keyword] = new(86, 156, 214, 255),
        [TokenKind.String] = new(206, 145, 120, 255),
        [TokenKind.Number] = new(181, 206, 168, 255),
        [TokenKind.Comment] = new(106, 153, 85, 255),
        [TokenKind.Identifier] = new(156, 220, 254, 255),
        [TokenKind.Operator] = new(180, 180, 180, 255),
        [TokenKind.Invalid] = new(244, 135, 113, 255),
    });

    /// <inheritdoc/>
    public MarkSet? For(TokenKind kind) => _marks.TryGetValue(kind, out var mark) ? mark : null;
}

/// <summary>
/// Applies a highlighter's tokens to a paragraph, for display only.
/// </summary>
internal static class SyntaxHighlighting
{
    /// <summary>Returns the paragraph with highlighting marks laid over its own.</summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="highlighter">The highlighter, or null to leave it alone.</param>
    /// <param name="theme">The palette.</param>
    /// <returns>
    /// A paragraph to draw. The original is returned unchanged when there is nothing to do, which
    /// is every paragraph that is not code.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The result is never stored.</b> It is built during layout and discarded with it, so the
    /// document keeps its own marks and knows nothing about themes. Storing it would put a palette
    /// in every export, put highlighting in the undo stack, and make changing theme an edit.
    /// </para>
    /// <para>
    /// Only the foreground is applied, and only where the paragraph does not already set one — a
    /// colour a user chose deliberately outranks one a tokenizer guessed.
    /// </para>
    /// </remarks>
    public static ParagraphNode Apply(
        ParagraphNode paragraph, ISyntaxHighlighter? highlighter, ISyntaxTheme theme)
    {
        if (highlighter is null || paragraph.Kind != ParagraphKind.Code || paragraph.Content.IsEmpty)
        {
            return paragraph;
        }

        ImmutableArray<ValueSpan<TokenKind>> tokens;

        try
        {
            tokens = highlighter.Tokenize(paragraph.Content.Text);
        }
        catch (Exception)
        {
            // The contract says a highlighter does not throw, so reaching this is a bug in one.
            // Losing the colour is the right price; losing the document is not.
            return paragraph;
        }

        if (tokens.IsDefaultOrEmpty)
        {
            return paragraph;
        }

        var content = paragraph.Content;
        var length = content.Length;

        foreach (var token in tokens)
        {
            // A highlighter that reported a span running past the text it was given. ValueSpan
            // already forbids a negative start, so this is the only way one can be out of range -
            // and it must not throw out of a layout pass.
            if (token.End > length)
            {
                continue;
            }

            if (token.Length == 0 || theme.For(token.Value) is not { } mark)
            {
                continue;
            }

            if (content.MarkAt(token.Start).Foreground is not null)
            {
                continue;
            }

            content = content.ApplyMarks(token.Start, token.Length, mark, MarkFields.Foreground);
        }

        return ReferenceEquals(content, paragraph.Content) ? paragraph : paragraph.WithContent(content);
    }
}
