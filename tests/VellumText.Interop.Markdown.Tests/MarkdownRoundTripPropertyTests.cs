using System.Text;
using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// The exporter's contract with itself, checked over generated documents: when it reports no loss,
/// the text and every mark it claims to have written come back.
/// </summary>
/// <remarks>
/// <para>
/// This is the regression net for the inline serializer. The hand-written round-trip tests pin the
/// exact Markdown for shapes someone thought of; this one finds the shapes nobody did — a closing
/// delimiter merging with the next run's opener, a code fence touching a literal backtick, a styled
/// run that is one escaped character long.
/// </para>
/// <para>
/// A failure message carries the Markdown, both dumps, and the seed, which is all it takes to turn
/// the case into a fixed test.
/// </para>
/// </remarks>
public class MarkdownRoundTripPropertyTests
{
    private const string Alphabet = "ab *_`[]()!#>-+|\\<&~1.:$";

    [Fact]
    public async Task Generated_documents_survive_when_the_export_reports_no_loss()
    {
        var (failures, clean) = await Run(0, 1500, new RoundTripGenerator.Options());

        Assert.True(failures.Count == 0, string.Join("\n\n", failures.Take(6)));

        // A generator whose every document reports a loss would make this test pass vacuously.
        Assert.True(clean > 300, $"only {clean} of 1500 documents exported without a reported loss");
    }

    /// <summary>
    /// Single paragraphs over an alphabet of nothing but Markdown's own punctuation, with one run
    /// styled — the densest way to put a delimiter next to something it can merge with.
    /// </summary>
    [Fact]
    public async Task Punctuation_dense_paragraphs_survive_when_the_export_reports_no_loss()
    {
        var rng = new Random(12345);
        var styles = new[]
        {
            TextStyle.Bold, TextStyle.Italic, TextStyle.Code, TextStyle.Strikethrough,
            TextStyle.Bold | TextStyle.Italic, TextStyle.Bold | TextStyle.Strikethrough,
        };
        var failures = new List<string>();

        for (var n = 0; n < 6000 && failures.Count < 6; n++)
        {
            var text = RandomText(rng);
            var content = InlineContent.FromText(text);

            for (var marks = rng.Next(1, 3); marks > 0; marks--)
            {
                var start = rng.Next(text.Length);
                var length = rng.Next(1, text.Length - start + 1);
                var style = styles[rng.Next(styles.Length)];

                content = content.ApplyMarks(
                    start,
                    length,
                    MarkSet.Empty with { Style = style },
                    style.ToFields());
            }

            var kind = rng.Next(6) switch
            {
                0 => ParagraphKind.Heading2,
                1 => ParagraphKind.Quote,
                _ => ParagraphKind.Body,
            };

            var doc = rng.Next(4) == 0
                ? new DocumentNode(
                    [new ListNode([new ListItemNode([new ParagraphNode(content)])], ListKind.Ordered)])
                : new DocumentNode([new ParagraphNode(content, kind)]);

            if ((await Check(doc)).Failure is { } failure)
            {
                failures.Add($"case {n}: {failure}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    internal static async Task<(List<string> Failures, int Clean)> Run(
        int firstSeed, int count, RoundTripGenerator.Options options)
    {
        var failures = new List<string>();
        var clean = 0;

        for (var seed = firstSeed; seed < firstSeed + count && failures.Count < 6; seed++)
        {
            var doc = RoundTripGenerator.Document(new Random(seed), options);

            var (failure, lossless) = await Check(doc);

            if (failure is not null)
            {
                failures.Add($"seed {seed}: {failure}");
            }

            clean += lossless ? 1 : 0;
        }

        return (failures, clean);
    }

    /// <summary>Null when the round trip holds or the exporter owned up to a loss.</summary>
    private static async Task<(string? Failure, bool Lossless)> Check(DocumentNode doc)
    {
        MarkdownExportResult exported;

        try
        {
            exported = MarkdownExporter.Instance.ExportWithDiagnostics(doc);
        }
        catch (Exception ex)
        {
            return ($"export threw {ex.GetType().Name}: {ex.Message}\n  doc:\n{RoundTripGenerator.Dump(doc)}", false);
        }

        if (!exported.Diagnostics.IsEmpty)
        {
            return (null, false);
        }

        var before = RoundTripGenerator.Dump(doc);
        var after = RoundTripGenerator.Dump((await MarkdownImporter.ImportAsync(exported.Text)).Doc);

        return (before == after
            ? null
            : $"md=[{exported.Text.TrimEnd()}]\n  before:\n{before}  after:\n{after}", true);
    }

    private static string RandomText(Random rng)
    {
        var text = new StringBuilder();

        for (var i = rng.Next(1, 12); i > 0; i--)
        {
            text.Append(Alphabet[rng.Next(Alphabet.Length)]);
        }

        var s = text.ToString().Trim();

        while (s.Contains("  ", StringComparison.Ordinal))
        {
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        }

        return s.Length == 0 ? "a" : s;
    }
}
