using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What a host can see, and refuse, before content from outside reaches the document.
/// </summary>
/// <remarks>
/// <c>ContentImported</c> says what an import cost, after the fact. There was no way to inspect
/// content before it landed, so an application with a content policy had two choices: accept
/// everything, or accept it and edit it back out — which is on the undo stack, and which the user
/// watches happen. These say a refusal is a paste that never happened.
/// </remarks>
public class RichTextViewContentPolicyTests
{
    private static readonly ImportDiagnostic[] Compromises =
    [
        new(DiagnosticSeverity.Dropped, "Script was removed.", "script"),
    ];

    /// <summary>An importer that always has something to complain about.</summary>
    private sealed class Chatty : IDocumentImporter
    {
        public string Format => "chatty";

        public string MediaType => "text/chatty";

        public Encoding? TextEncoding => Encoding.UTF8;

        public ImportResult Import(string text) =>
            new(DocumentNode.FromParagraphs(text.Split('\n')), Compromises);

        public ImportResult Import(Stream source) =>
            Import(DocumentPayload.ReadAllText(source, Encoding.UTF8));
    }

    private static RichTextView Shown(params string[] paragraphs)
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs(paragraphs)) };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        return view;
    }

    private static DataTransfer Transfer(string format, string payload)
    {
        var item = new DataTransferItem();

        item.Set(RichTextClipboard.FlavorFor(format).DataFormat, Encoding.UTF8.GetBytes(payload));

        var data = new DataTransfer();

        data.Add(item);

        return data;
    }

    private static DataTransfer TextTransfer(string text)
    {
        var item = new DataTransferItem();

        item.SetText(text);

        var data = new DataTransfer();

        data.Add(item);

        return data;
    }

    [AvaloniaFact]
    public async Task A_refused_paste_leaves_the_document_exactly_as_it_was()
    {
        var view = Shown("hello");
        var imported = 0;

        view.Formats.Add(new Chatty());
        view.ContentImporting += (_, e) => e.Cancel = true;
        view.ContentImported += (_, _) => imported++;
        view.MoveTo(1, extend: false);

        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(Transfer("chatty", "pasted"));

        Assert.False(await view.PasteAsync());

        Assert.Equal("hello", DocumentText.Of(view.State.Doc).Trim());

        // A refusal is a paste that did nothing, all the way down: nothing to undo, and nothing
        // reported as having arrived. The host is the one refusing, so it already knows.
        Assert.False(view.CanUndo);
        Assert.Equal(0, imported);
    }

    [AvaloniaFact]
    public async Task A_refused_plain_text_paste_leaves_the_document_alone()
    {
        // The second seam. Plain text does not go through an importer at all, so a gate on the
        // rich path only would let every terminal paste past a policy that had refused it.
        var view = Shown("hello");
        var imported = 0;

        view.ContentImporting += (_, e) => e.Cancel = true;
        view.ContentImported += (_, _) => imported++;
        view.MoveTo(1, extend: false);

        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(TextTransfer("pasted"));

        Assert.False(await view.PasteAsync());

        Assert.Equal("hello", DocumentText.Of(view.State.Doc).Trim());
        Assert.False(view.CanUndo);
        Assert.Equal(0, imported);
    }

    [AvaloniaFact]
    public async Task Ctrl_shift_V_is_gated_too()
    {
        // Paste-as-plain-text is its own entry point and reads the clipboard itself.
        var view = Shown("hello");

        view.Formats.Add(new Chatty());
        view.ContentImporting += (_, e) => e.Cancel = true;
        view.MoveTo(1, extend: false);

        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(TextTransfer("pasted"));

        Assert.False(await view.PasteTextAsync());
        Assert.Equal("hello", DocumentText.Of(view.State.Doc).Trim());
    }

    [AvaloniaFact]
    public void A_refused_drop_leaves_the_document_alone()
    {
        var view = Shown("hello");

        view.Formats.Add(new Chatty());
        view.ContentImporting += (_, e) => e.Cancel = true;

        view.RaiseEvent(new DragEventArgs(
            DragDrop.DropEvent,
            Transfer("chatty", "dropped"),
            view,
            new Point(4, 4),
            KeyModifiers.None));

        Assert.Equal("hello", DocumentText.Of(view.State.Doc).Trim());
        Assert.False(view.CanUndo);
    }

    [AvaloniaFact]
    public void The_host_sees_the_content_and_what_it_cost_before_it_lands()
    {
        var view = Shown("hello");
        var seen = new List<RichTextView.ContentImportingEventArgs>();

        view.ContentImporting += (_, e) =>
        {
            seen.Add(e);

            // Asserted inside the handler, which is the only moment the claim means anything.
            Assert.Equal("hello", DocumentText.Of(view.State.Doc).Trim());
        };

        view.MoveTo(1, extend: false);

        Assert.True(view.PasteImport(new ImportResult(DocumentNode.FromParagraphs("imported"), Compromises)));

        var offered = Assert.Single(seen);

        Assert.Equal("imported", DocumentText.Of(offered.Content).Trim());
        Assert.Equal(Compromises, offered.Diagnostics.AsEnumerable());
        Assert.True(offered.AnythingDropped);
        Assert.False(offered.Cancel);
    }

    [AvaloniaFact]
    public async Task Plain_text_is_offered_as_the_paragraphs_it_will_become()
    {
        // Not as the string that arrived. The line breaks are normalized and the reserved embed
        // placeholder taken out before the paste, so a policy shown the raw text would be vetting
        // something other than what lands - and would see a U+FFFC that never reaches the
        // document.
        var view = Shown("hello");
        var seen = new List<DocumentNode>();

        view.ContentImporting += (_, e) => seen.Add(e.Content);
        view.MoveTo(1, extend: false);

        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(TextTransfer("one\r\ntw\ufffco"));

        Assert.True(await view.PasteAsync());

        var offered = Assert.Single(seen);

        Assert.Equal(2, offered.Blocks.Length);
        Assert.Equal("one", DocumentText.Of(offered).Split('\n')[0]);
        Assert.DoesNotContain('\ufffc', DocumentText.Of(offered));

        // And it really is what landed.
        Assert.DoesNotContain('\ufffc', DocumentText.Of(view.State.Doc));
        Assert.Contains("two", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void A_policy_that_allows_changes_nothing()
    {
        // The positive control for every refusal above: the same fixture, the same handler
        // attached, without the Cancel.
        var view = Shown("hello");
        var asked = 0;

        view.ContentImporting += (_, _) => asked++;
        view.MoveTo(1, extend: false);

        Assert.True(view.PasteImport(new ImportResult(DocumentNode.FromParagraphs("imported"))));

        Assert.Equal(1, asked);
        Assert.Contains("imported", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void A_host_that_throws_from_its_policy_sees_its_own_exception()
    {
        // Nothing has been applied when the gate runs, so a throwing policy cannot leave half a
        // paste behind. A host calling PasteImport itself gets the exception, because swallowing
        // it here would hide the host's bug from the host.
        var view = Shown("hello");

        view.ContentImporting += (_, _) => throw new InvalidOperationException("policy is broken");

        view.MoveTo(1, extend: false);

        Assert.Throws<InvalidOperationException>(
            () => view.PasteImport(new ImportResult(DocumentNode.FromParagraphs("imported"))));

        Assert.Equal("hello", DocumentText.Of(view.State.Doc).Trim());
        Assert.False(view.CanUndo);
    }

    [AvaloniaFact]
    public async Task A_throwing_policy_loses_the_paste_and_not_the_application()
    {
        // The keyboard path already treats a clipboard it cannot read as a paste that did not
        // happen. A policy that throws meets the same handler, so a bug in a host's policy costs
        // the user one paste rather than the window.
        var view = Shown("hello");

        view.ContentImporting += (_, _) => throw new InvalidOperationException("policy is broken");

        view.MoveTo(1, extend: false);

        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(TextTransfer("pasted"));

        Assert.False(await view.PasteAsync());
        Assert.Equal("hello", DocumentText.Of(view.State.Doc).Trim());

        // And the control still works afterwards.
        Assert.True(view.InsertText("x"));
    }

    [AvaloniaFact]
    public void A_read_only_control_never_asks()
    {
        // A policy may be slow - it may hash the content, or ask a server. Asking it about a paste
        // that is refused anyway spends that for nothing, and teaches a host that being asked
        // means something is about to happen when it does not.
        var view = Shown("hello");
        var asked = 0;

        view.IsReadOnly = true;
        view.ContentImporting += (_, _) => asked++;
        view.MoveTo(1, extend: false);

        Assert.False(view.PasteImport(new ImportResult(DocumentNode.FromParagraphs("imported"))));
        Assert.Equal(0, asked);

        // The positive control: the same fixture asks once it can accept an edit.
        view.IsReadOnly = false;

        Assert.True(view.PasteImport(new ImportResult(DocumentNode.FromParagraphs("imported"))));
        Assert.Equal(1, asked);
    }

    [AvaloniaFact]
    public void Nothing_is_asked_about_content_that_would_not_land_anyway()
    {
        var view = Shown("hello");
        var asked = 0;

        view.ContentImporting += (_, _) => asked++;
        view.MoveTo(1, extend: false);

        Assert.False(view.PasteImport(new ImportResult(new DocumentNode([]))));
        Assert.Equal(0, asked);

        // The positive control, again: content that would land is asked about.
        Assert.True(view.PasteImport(new ImportResult(DocumentNode.FromParagraphs("imported"))));
        Assert.Equal(1, asked);
    }

    [AvaloniaFact]
    public void A_policy_subscribed_on_the_editor_before_templating_still_runs()
    {
        // The forwarding case, and the one that matters most: a content policy attached to a view
        // that does not exist yet is a policy that never runs, and the first sign of it is content
        // the host meant to refuse sitting in the document.
        var editor = new RichTextEditor();

        editor.ContentImporting += (_, e) => e.Cancel = true;

        var window = new Window { Width = 400, Height = 200, Content = editor };

        window.Show();

        Assert.False(editor.EnsureView().PasteImport(
            new ImportResult(DocumentNode.FromParagraphs("pasted"))));

        Assert.DoesNotContain("pasted", DocumentText.Of(editor.Document));
    }
}
