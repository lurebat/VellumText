namespace Vellum.Tests.Interop;

public class ImportResultTests
{
    [Fact]
    public void Null_document_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ImportResult(null!));
    }

    [Fact]
    public void Diagnostics_default_to_none()
    {
        var result = new ImportResult(DocumentNode.FromParagraphs("hello"));

        Assert.Empty(result.Diagnostics);
        Assert.False(result.AnythingDropped);
    }

    [Fact]
    public void Diagnostics_keep_the_order_they_were_reported_in()
    {
        var result = new ImportResult(
            DocumentNode.Empty,
            [
                new ImportDiagnostic(DiagnosticSeverity.Dropped, "second"),
                new ImportDiagnostic(DiagnosticSeverity.Downgraded, "first"),
            ]);

        Assert.Equal(["second", "first"], result.Diagnostics.Select(d => d.Message));
    }

    [Fact]
    public void A_single_empty_paragraph_counts_as_empty()
    {
        Assert.True(new ImportResult(DocumentNode.Empty).IsEmpty);
        Assert.True(new ImportResult(DocumentNode.FromParagraphs("")).IsEmpty);
    }

    [Fact]
    public void Anything_at_all_counts_as_not_empty()
    {
        Assert.False(new ImportResult(DocumentNode.FromParagraphs("x")).IsEmpty);

        // Two empty paragraphs are two blank lines, which is content a user can see.
        Assert.False(new ImportResult(DocumentNode.FromParagraphs("", "")).IsEmpty);

        // A block that is not a paragraph is content even when it has no text.
        Assert.False(new ImportResult(new DocumentNode([RuleNode.Instance])).IsEmpty);
    }

    [Fact]
    public void Dropped_is_distinguished_from_merely_downgraded()
    {
        var downgraded = new ImportResult(
            DocumentNode.Empty,
            [new ImportDiagnostic(DiagnosticSeverity.Downgraded, "a")]);

        var malformed = new ImportResult(
            DocumentNode.Empty,
            [new ImportDiagnostic(DiagnosticSeverity.Malformed, "b")]);

        var dropped = new ImportResult(
            DocumentNode.Empty,
            [
                new ImportDiagnostic(DiagnosticSeverity.Downgraded, "a"),
                new ImportDiagnostic(DiagnosticSeverity.Dropped, "b"),
            ]);

        Assert.False(downgraded.AnythingDropped);
        Assert.False(malformed.AnythingDropped);
        Assert.True(dropped.AnythingDropped);
    }

    [Fact]
    public void The_slice_is_closed_at_both_ends_so_pasting_it_does_not_merge()
    {
        var result = new ImportResult(DocumentNode.FromParagraphs("one", "two"));
        var slice = result.ToSlice();

        Assert.Equal(0, slice.OpenStart);
        Assert.Equal(0, slice.OpenEnd);
        Assert.Equal(2, slice.Content.Length);
    }

    [Fact]
    public void The_slice_can_actually_be_pasted()
    {
        var doc = DocumentNode.FromParagraphs("before", "after");
        var result = new ImportResult(DocumentNode.FromParagraphs("inserted"));

        // Between the two existing blocks: "before" ends at 7, so the boundary after it is 8.
        var step = new ReplaceStep(8, 8, result.ToSlice());
        var applied = step.Apply(doc);

        Assert.True(applied.IsOk);
        Assert.Equal(
            ["before", "inserted", "after"],
            applied.Doc!.Blocks.Cast<ParagraphNode>().Select(p => p.Content.Text));
    }

    [Fact]
    public void A_diagnostic_reads_usefully()
    {
        Assert.Equal(
            "Dropped: script removed (script)",
            new ImportDiagnostic(DiagnosticSeverity.Dropped, "script removed", "script").ToString());

        Assert.Equal(
            "Downgraded: no equivalent",
            new ImportDiagnostic(DiagnosticSeverity.Downgraded, "no equivalent").ToString());
    }
}
