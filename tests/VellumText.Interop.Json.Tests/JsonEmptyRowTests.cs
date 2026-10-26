using VellumText;
using VellumText.Interop.Json;

namespace VellumText.Tests.Interop.Json;

/// <summary>
/// A table whose merge leaves a row carrying no cells of its own.
/// </summary>
/// <remarks>
/// Merging a whole rectangle produces one, and until 2026-08-18 the schema refused it, so no
/// format had ever been asked to carry one. JSON claims to be lossless, and that claim is only
/// worth anything for shapes a document can actually reach.
/// </remarks>
public class JsonEmptyRowTests
{
    [Fact]
    public async Task ARowCoveredEntirelyByAMergeSurvivesTheRoundTrip()
    {
        var doc = Merged();
        var back = await JsonFormat.Instance.ImportAsync(JsonFormat.Instance.Export(doc));

        Assert.False(back.AnythingDropped, string.Join("; ", back.Diagnostics));
        Assert.Equal(doc, back.Doc);
        Assert.True(
            DocumentSchema.IsValid(back.Doc),
            string.Join("; ", DocumentSchema.Validate(back.Doc)));
    }

    internal static DocumentNode Merged() =>
        new([
            new TableNode([
                new TableRowNode([
                    new TableCellNode(
                        [ParagraphNode.FromText("merged")], rowSpan: 2, columnSpan: 2),
                ]),
                new TableRowNode([]),
            ]),
        ]);
}
