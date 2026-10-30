using System.Text;

using VellumText;
using VellumText.ImportProbe;
using VellumText.Interop.Json;

namespace VellumText.Tests.Interop.Json;

/// <summary>
/// Documents nested far past any depth the format writes, imported in a child process so that a
/// stack overflow fails the test by exit code instead of killing the test host.
/// </summary>
/// <remarks>
/// The payloads are well-formed documents in the shape the writer emits — a versioned root, a
/// block list, lists or tables nested inside one another — so they pass every check that comes
/// before the block reader and the reader's depth limit is the only thing that refuses them (see
/// <c>JsonDepthTests</c> for the nesting it must still accept). With that limit lifted they are
/// parsed in full and the block reader builds the whole tree, recursing once per level: ten
/// thousand levels then import as a document that overflows the stack of anything that walks it
/// recursively afterwards, and a hundred thousand do not finish within the probe's timeout. A
/// child process makes sure either is a red test rather than a crashed test run that can still
/// report "Passed!".
/// </remarks>
public sealed class HostileNestingTests
{
    private const string DocumentOpen = "{\"VellumText\":1,\"blocks\":[";
    private const string DocumentClose = "]}";
    private const string ListOpen = "{\"type\":\"list\",\"items\":[{\"blocks\":[";
    private const string ListClose = "]}]}";
    private const string TableOpen = "{\"type\":\"table\",\"rows\":[{\"cells\":[{\"blocks\":[";
    private const string TableClose = "]}]}]}";

    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void Deeply_nested_lists_are_refused_without_crashing_the_host(int depth)
    {
        var result = ProbeRunner.Run("json", Payload(ListOpen, ListClose, depth));

        AssertRefusedForDepth(result);
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void Deeply_nested_tables_are_refused_without_crashing_the_host(int depth)
    {
        var result = ProbeRunner.Run("json", Payload(TableOpen, TableClose, depth));

        AssertRefusedForDepth(result);
    }

    /// <summary>
    /// Guards the payloads themselves: at a shallow depth they must be exactly what the writer
    /// emits and must import cleanly, or the tests above could pass on an early rejection that has
    /// nothing to do with depth.
    /// </summary>
    [Fact]
    public async Task The_hostile_payloads_are_documents_the_reader_accepts_when_shallow()
    {
        var lists = Nest(inner => new ListNode([new ListItemNode(inner)]));
        var tables = Nest(inner => new TableNode([new TableRowNode([new TableCellNode(inner)])]));

        Assert.Equal(JsonFormat.Instance.Export(lists), Payload(ListOpen, ListClose, 3));
        Assert.Equal(JsonFormat.Instance.Export(tables), Payload(TableOpen, TableClose, 3));

        foreach (var (doc, payload) in new[]
                 {
                     (lists, Payload(ListOpen, ListClose, 3)),
                     (tables, Payload(TableOpen, TableClose, 3)),
                 })
        {
            var result = await JsonFormat.Instance.ImportAsync(payload);

            Assert.Empty(result.Diagnostics);
            Assert.Equal(doc, result.Doc);
        }
    }

    private static DocumentNode Nest(Func<IEnumerable<BlockNode>, BlockNode> wrap)
    {
        IEnumerable<BlockNode> blocks = [];

        for (var level = 0; level < 3; level++)
        {
            blocks = [wrap(blocks)];
        }

        return new DocumentNode(blocks);
    }

    private static string Payload(string open, string close, int depth)
    {
        var payload = new StringBuilder(
            DocumentOpen.Length + ((open.Length + close.Length) * depth) + DocumentClose.Length);

        payload.Append(DocumentOpen);
        payload.Insert(payload.Length, open, depth);
        payload.Insert(payload.Length, close, depth);
        payload.Append(DocumentClose);

        return payload.ToString();
    }

    private static void AssertRefusedForDepth(ProbeResult result)
    {
        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("0", result.Value("blocks"));

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.StartsWith("Malformed|", diagnostic, StringComparison.Ordinal);
        Assert.Contains("maximum configured depth", diagnostic, StringComparison.Ordinal);
    }
}