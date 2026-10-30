using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A host replacing the document, or the editor being retemplated, between any two operations —
/// including while an import is still running — leaves the editor coherent (RP-7).
/// </summary>
/// <remarks>
/// <para>
/// The codebase audit found four bugs of one shape (F-08, F-19, F-39, F-42): view state that
/// outlived the document or the view it belonged to. Each was fixed where it was found, and each
/// fix had its own test; this fixture is the guard for the shape, so that the next piece of state a
/// view keeps is exercised against a replacement without anyone having to think of it.
/// </para>
/// <para>
/// Every ordered pair of operations runs with each interruption between them. What has to hold
/// afterwards: nothing throws; the editor and its current view agree on the state; the document is
/// schema-valid; after a swap, nothing of the replaced document comes back and there is nothing to
/// undo; after a retemplate, the document, the undo history and the registered formats carried
/// over. The in-flight variants start a paste whose import the test holds open, interrupt it, then
/// release it: the paste must land nothing.
/// </para>
/// </remarks>
public class HostStateReplacementTests
{
    private const string Original = "ORIGINAL";

    private static readonly string[] Operations =
        ["type", "enter", "backspace", "down", "bold", "select-all", "undo", "redo", "paste", "cut", "drop"];

    /// <summary>Every ordered pair of operations with a swap or a retemplate between them.</summary>
    public static TheoryData<string, string, string> Pairs()
    {
        var data = new TheoryData<string, string, string>();

        foreach (var first in Operations)
        {
            foreach (var interruption in new[] { "swap", "retemplate" })
            {
                foreach (var second in Operations)
                {
                    data.Add(first, interruption, second);
                }
            }
        }

        return data;
    }

    /// <summary>Every operation after an interruption that lands while a paste is still importing.</summary>
    public static TheoryData<string, string> InFlight()
    {
        var data = new TheoryData<string, string>();

        foreach (var interruption in new[] { "swap", "retemplate" })
        {
            foreach (var next in Operations)
            {
                data.Add(interruption, next);
            }
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Pairs))]
    public async Task An_interruption_between_two_operations_leaves_the_editor_coherent(
        string first, string interruption, string second)
    {
        var (editor, _) = Shown();

        await Run(editor, first);
        await Interrupt(editor, interruption);
        await Run(editor, second);

        AssertCoherent(editor, swapped: interruption == "swap");
    }

    [AvaloniaTheory]
    [MemberData(nameof(InFlight))]
    public async Task An_interruption_during_an_import_lands_nothing_and_leaves_the_editor_coherent(
        string interruption, string next)
    {
        var (editor, gated) = Shown();
        var view = editor.EnsureView();

        await TopLevel.GetTopLevel(editor)!.Clipboard!.SetDataAsync(Transfer(gated.Format, "GATED"));

        var paste = view.PasteAsync();

        await gated.Started.Task;
        await Interrupt(editor, interruption);
        gated.Release.SetResult();

        Assert.False(await paste);
        Assert.DoesNotContain("GATED", Text(editor), StringComparison.Ordinal);

        await Run(editor, next);

        AssertCoherent(editor, swapped: interruption == "swap");
        Assert.DoesNotContain("GATED", Text(editor), StringComparison.Ordinal);
    }

    private static (RichTextEditor Editor, Gated Importer) Shown()
    {
        var gated = new Gated();
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs(Original + " first line", Original + " second line")),
        };
        var window = new Window { Content = editor, Width = 400, Height = 300 };

        editor.Formats.Add(HtmlFormat.Instance);
        editor.Formats.Add(gated);
        window.Show();
        Layout(editor);
        editor.EnsureView().MoveTo(3, extend: false);

        return (editor, gated);
    }

    private static void Layout(RichTextEditor editor)
    {
        var window = (Window)TopLevel.GetTopLevel(editor)!;

        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));
    }

    private static async Task Run(RichTextEditor editor, string operation)
    {
        var view = editor.EnsureView();

        switch (operation)
        {
            case "type":
                view.InsertText("t");
                break;
            case "enter":
                Key(view, global::Avalonia.Input.Key.Enter);
                break;
            case "backspace":
                Key(view, global::Avalonia.Input.Key.Back);
                break;
            case "down":
                Key(view, global::Avalonia.Input.Key.Down);
                break;
            case "bold":
                Key(view, global::Avalonia.Input.Key.B, KeyModifiers.Control);
                break;
            case "select-all":
                view.SelectAll();
                break;
            case "undo":
                editor.Undo();
                break;
            case "redo":
                editor.Redo();
                break;
            case "paste":
                await TopLevel.GetTopLevel(editor)!.Clipboard!.SetDataAsync(TextTransfer("CLIP"));
                await view.PasteAsync();
                break;
            case "cut":
                await view.CutAsync();
                break;
            case "drop":
                var data = TextTransfer("DROP");

                view.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, view, new Point(4, 4), KeyModifiers.None));
                await view.PendingDrop;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    /// <summary>The swap marker, so a later assertion can tell the replacement from the original.</summary>
    private const string Replacement = "REPLACED";

    private static Task Interrupt(RichTextEditor editor, string interruption)
    {
        switch (interruption)
        {
            case "swap":
                editor.State = EditorState.Create(DocumentNode.FromParagraphs(Replacement + " one", Replacement + " two"));

                // A host load is a new document: what was typed into the old one is not undoable here.
                Assert.False(editor.CanUndo);
                Assert.StartsWith(Replacement, Text(editor), StringComparison.Ordinal);
                break;
            case "retemplate":
                var oldView = editor.EnsureView();
                var doc = editor.State.Doc;
                var canUndo = editor.CanUndo;
                var formats = oldView.Formats.Count;

                editor.Template = RetemplateTests.Chrome();
                editor.ApplyTemplate();
                Layout(editor);

                var newView = editor.EnsureView();

                Assert.NotSame(oldView, newView);
                Assert.Same(doc, editor.State.Doc);
                Assert.Same(doc, newView.State.Doc);
                Assert.Equal(canUndo, editor.CanUndo);
                Assert.Equal(formats, newView.Formats.Count);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(interruption), interruption, null);
        }

        return Task.CompletedTask;
    }

    private static void AssertCoherent(RichTextEditor editor, bool swapped)
    {
        var view = editor.EnsureView();

        Assert.Same(editor.State.Doc, view.State.Doc);
        Assert.Equal(editor.State.Selection, view.State.Selection);
        Assert.True(DocumentSchema.IsValid(editor.State.Doc), string.Join("; ", DocumentSchema.Validate(editor.State.Doc)));

        if (swapped)
        {
            Assert.DoesNotContain(Original, Text(editor), StringComparison.Ordinal);
        }
    }

    private static string Text(RichTextEditor editor) => DocumentText.Of(editor.State.Doc);

    private static void Key(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = view,
        });

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

    /// <summary>An importer that waits for the test to release it, ignoring cancellation.</summary>
    private sealed class Gated : IDocumentImporter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Format => "gated";

        public string MediaType => "text/x-gated";

        public Encoding? TextEncoding => Encoding.UTF8;

        public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
            var text = await reader.ReadToEndAsync(CancellationToken.None);

            Started.TrySetResult();
            await Release.Task;

            return new ImportResult(DocumentNode.FromParagraphs(text));
        }
    }
}
