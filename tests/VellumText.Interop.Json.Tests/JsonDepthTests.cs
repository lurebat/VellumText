using VellumText;
using VellumText.Interop.Json;

namespace VellumText.Tests.Interop.Json;

/// <summary>
/// JSON is the lossless save format, so whatever nesting the writer emits the reader must take
/// back. Each level of list nesting costs four levels of JSON and each table level six, so the
/// reader's old default of 64 refused 19 nested lists and 11 nested tables that the writer had
/// happily saved, and the saved document came back empty.
/// </summary>
public class JsonDepthTests
{
    private static async Task<DocumentNode> RoundTrip(DocumentNode doc)
    {
        var result = await JsonFormat.Instance.ImportAsync(JsonFormat.Instance.Export(doc), TestContext.Current.CancellationToken);

        Assert.Empty(result.Diagnostics);

        return result.Doc;
    }

    private static DocumentNode NestedLists(int depth)
    {
        BlockNode block = ParagraphNode.FromText("deepest");

        for (var level = 0; level < depth; level++)
        {
            block = new ListNode([new ListItemNode([ParagraphNode.FromText($"level {level}"), block])]);
        }

        return new DocumentNode([block]);
    }

    private static DocumentNode NestedTables(int depth)
    {
        BlockNode block = ParagraphNode.FromText("deepest");

        for (var level = 0; level < depth; level++)
        {
            block = new TableNode([new TableRowNode([new TableCellNode([block])])]);
        }

        return new DocumentNode([block]);
    }

    [Fact]
    public async Task NineteenNestedListsRoundTrip()
    {
        var doc = NestedLists(19);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task ElevenNestedTablesRoundTrip()
    {
        var doc = NestedTables(11);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task TheDeepestListNestingTheWriterEmitsRoundTrips()
    {
        var depth = 1;

        while (CanExport(NestedLists(depth + 1)))
        {
            depth++;
        }

        // Guards the search itself: the writer must stop well past the old reader limit.
        Assert.True(depth > 100, $"The writer stopped at {depth} levels.");

        var doc = NestedLists(depth);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    [Fact]
    public async Task TheDeepestTableNestingTheWriterEmitsRoundTrips()
    {
        var depth = 1;

        while (CanExport(NestedTables(depth + 1)))
        {
            depth++;
        }

        Assert.True(depth > 100, $"The writer stopped at {depth} levels.");

        var doc = NestedTables(depth);

        Assert.Equal(doc, await RoundTrip(doc));
    }

    private static bool CanExport(DocumentNode doc)
    {
        try
        {
            JsonFormat.Instance.Export(doc);

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
