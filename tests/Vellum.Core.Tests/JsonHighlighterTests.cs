using Vellum;

namespace Vellum.Tests.Core;

/// <summary>
/// Tokenizing JSON, including the things a clipboard actually holds.
/// </summary>
public class JsonHighlighterTests
{
    private static TokenKind[] Kinds(string text) =>
        [.. JsonHighlighter.Instance.Tokenize(text).Select(t => t.Value)];

    private static string Span(string text, int index)
    {
        var token = JsonHighlighter.Instance.Tokenize(text)[index];

        return text.Substring(token.Start, token.Length);
    }

    [Fact]
    public void AKeyIsToldApartFromAValue()
    {
        // Most of what makes JSON readable. The only way to tell is the colon after it, which is
        // why the tokenizer does it rather than the theme.
        Assert.Equal(
            [TokenKind.Operator, TokenKind.Identifier, TokenKind.Operator, TokenKind.String, TokenKind.Operator],
            Kinds("""{"name": "value"}"""));
    }

    [Fact]
    public void AStringHoldingAColonIsStillAValue()
    {
        // The look-ahead has to skip the string's own contents, not scan the raw text.
        Assert.Equal(TokenKind.String, Kinds("""{"k": "a: b"}""")[3]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("-0.5")]
    [InlineData("1e10")]
    [InlineData("1E+10")]
    [InlineData("-2.5e-3")]
    public void NumbersAreRecognisedByTheGrammar(string number)
    {
        Assert.Equal([TokenKind.Number], Kinds(number));
    }

    [Theory]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("--1")]
    [InlineData("NaN")]
    [InlineData("0x10")]
    public void WhatIsNotANumberIsNotCalledOne(string word)
    {
        // "01" is not valid JSON, and a tokenizer that shrugged would colour it as though it were.
        Assert.Equal([TokenKind.Invalid], Kinds(word));
    }

    [Fact]
    public void TheLiteralsAreKeywords()
    {
        Assert.Equal(
            [TokenKind.Operator, TokenKind.Keyword, TokenKind.Operator, TokenKind.Keyword,
             TokenKind.Operator, TokenKind.Keyword, TokenKind.Operator],
            Kinds("[true, false, null]"));
    }

    [Fact]
    public void AnUnterminatedStringIsReportedRatherThanSwallowed()
    {
        // A clipboard holds fragments. A broken string coloured as ordinary text renders as though
        // nothing were wrong.
        Assert.Equal([TokenKind.Invalid], Kinds("\"never closed"));
        Assert.Equal("\"never closed", Span("\"never closed", 0));
    }

    [Fact]
    public void EscapesInsideAStringDoNotEndIt()
    {
        Assert.Equal([TokenKind.String], Kinds("""  "a \" b"  """.Trim()));
    }

    [Fact]
    public void ATrailingBackslashDoesNotRunPastTheEnd()
    {
        // The escape would step two characters and the second is not there.
        Assert.Equal([TokenKind.Invalid], Kinds("\"abc\\"));
    }

    [Fact]
    public void AFragmentWithUnbalancedBracesStillTokenizes()
    {
        // Copying one object out of the middle of a file is the common case, not the exception.
        Assert.Equal(
            [TokenKind.Identifier, TokenKind.Operator, TokenKind.Number, TokenKind.Operator],
            Kinds("\"id\": 42,"));
    }

    [Fact]
    public void EverySpanIsInsideTheTextAndInOrder()
    {
        const string json = """{"a": [1, true, "x"], "b": {"c": null}, "bad": 0x1}""";

        var tokens = JsonHighlighter.Instance.Tokenize(json);
        var previousEnd = 0;

        Assert.NotEmpty(tokens);

        foreach (var token in tokens)
        {
            Assert.InRange(token.Start, previousEnd, json.Length);
            Assert.InRange(token.End, token.Start + 1, json.Length);
            previousEnd = token.End;
        }
    }

    [Fact]
    public void WhitespaceAndEmptinessProduceNothing()
    {
        Assert.Empty(JsonHighlighter.Instance.Tokenize(""));
        Assert.Empty(JsonHighlighter.Instance.Tokenize("   \t  "));
        Assert.Empty(JsonHighlighter.Instance.Tokenize(null!));
    }

    [Fact]
    public void ItNeverThrowsOnAnythingAtAll()
    {
        // Contractual: this is handed arbitrary clipboard text that may not be JSON in any sense.
        foreach (var text in new[]
        {
            "not json at all", "<html><body>x</body></html>", "\\", "\"", "{{{{{{", "]]]]",
            "\u0000\u0001", "\U0001F468\u200D\U0001F469 emoji", "e\u0301", ":::", "1e999999",
        })
        {
            var tokens = JsonHighlighter.Instance.Tokenize(text);

            foreach (var token in tokens)
            {
                Assert.InRange(token.End, 0, text.Length);
            }
        }
    }

    [Fact]
    public void ItIdentifiesItsLanguage()
    {
        Assert.Equal("json", JsonHighlighter.Instance.Language);
    }
}
