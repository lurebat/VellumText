using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// A table whose merge leaves a row carrying no cells of its own.
/// </summary>
/// <remarks>
/// RTF is interchange and is explicitly expected-import rather than guaranteed-fidelity, so this
/// does not pin the grid. It does insist that a table comes back as a table: the import path runs
/// through the HTML importer, which used to drop an empty row and so turned the whole table into
/// plain paragraphs. Asserting only schema validity would have passed all the way through that,
/// because a document of paragraphs is perfectly valid.
/// </remarks>
public class RtfEmptyRowTests
{
    [Fact]
    public async Task ARowCoveredEntirelyByAMergeStillComesBackAsATable()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    new TableCellNode(
                        [ParagraphNode.FromText("merged")], rowSpan: 2, columnSpan: 2),
                ]),
                new TableRowNode([]),
            ]),
        ]);

        var back = await RtfFormat.Instance.ImportAsync(RtfFormat.Instance.Export(doc), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(back.Doc.Blocks, block => block is TableNode);
        Assert.Contains("merged", DocumentText.Of(back.Doc), StringComparison.Ordinal);
        Assert.True(
            DocumentSchema.IsValid(back.Doc),
            string.Join("; ", DocumentSchema.Validate(back.Doc)));
    }
}
