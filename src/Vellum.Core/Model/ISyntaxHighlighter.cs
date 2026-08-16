using System.Collections.Immutable;

namespace Vellum;

/// <summary>
/// What a tokenizer decided a run of code is.
/// </summary>
/// <remarks>
/// Kinds rather than colours, because a colour is a decision about a theme and a kind is a fact
/// about the text. A tokenizer that emitted colours would have to be rewritten for a dark theme,
/// and every document highlighted under one theme would carry that theme's palette into its
/// exports for ever.
/// </remarks>
public enum TokenKind
{
    /// <summary>Anything the tokenizer has no opinion about.</summary>
    Text = 0,

    /// <summary>A reserved word.</summary>
    Keyword,

    /// <summary>A quoted string, including its quotes.</summary>
    String,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>A comment, of any syntax.</summary>
    Comment,

    /// <summary>An operator or a separator.</summary>
    Operator,

    /// <summary>A name: a property, a variable, a type.</summary>
    Identifier,

    /// <summary>Text that is not valid in this language.</summary>
    /// <remarks>
    /// Reported rather than swallowed. Code on a clipboard is often a fragment, and a tokenizer
    /// that silently called a broken string "text" would render it as though it were fine.
    /// </remarks>
    Invalid,
}

/// <summary>
/// Turns the text of a code block into tokens.
/// </summary>
/// <remarks>
/// <para>
/// <b>Highlighting is presentation, not content.</b> A highlighter is asked at layout time and its
/// answer is never stored: putting colours in the document would put a theme in every export, make
/// a theme change rewrite every document, and put highlighting in the undo stack.
/// </para>
/// <para>
/// One paragraph at a time, which is exact for languages whose tokens cannot span a line and an
/// approximation for those where they can — a C block comment opened on one line and closed on the
/// next will not be understood. Carrying state between lines is a larger design and is deliberately
/// not attempted here; a language that needs it should have a highlighter that says so rather than
/// this interface growing a state parameter nobody can supply correctly.
/// </para>
/// </remarks>
public interface ISyntaxHighlighter
{
    /// <summary>What this highlights, for a host choosing between several.</summary>
    string Language { get; }

    /// <summary>Splits a line of code into tokens.</summary>
    /// <param name="text">The line.</param>
    /// <returns>
    /// The tokens, in order and non-overlapping. Runs the tokenizer has no opinion about may be
    /// omitted rather than returned as <see cref="TokenKind.Text"/>.
    /// </returns>
    /// <remarks>
    /// Must not throw. Code arriving from a clipboard is arbitrary text that may not be in this
    /// language at all, and a highlighter that threw would take down the render of a document
    /// whose only fault was containing something unexpected.
    /// </remarks>
    ImmutableArray<ValueSpan<TokenKind>> Tokenize(string text);
}
