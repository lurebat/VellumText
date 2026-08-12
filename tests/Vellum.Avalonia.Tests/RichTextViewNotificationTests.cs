using Avalonia;
using Avalonia.Headless.XUnit;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// What a listener sees at the moment <see cref="RichTextView.StateProperty"/> is raised.
/// </summary>
/// <remarks>
/// The whole reason to observe the property is to ask the view a follow-up question — which block
/// the caret is in, what to show as active on a toolbar. Announcing the change before the view has
/// reconciled means every one of those questions is answered about the previous document.
/// </remarks>
public class RichTextViewNotificationTests
{
    private static DocumentNode ThreeBlocks() =>
        DocumentNode.FromParagraphs("first", "second", "third");

    /// <summary>
    /// Gives the view a layout pass, which is when it first builds its block views. Before that it
    /// deliberately answers as empty, so a test that skipped this would be asking about a state the
    /// bug never applied to.
    /// </summary>
    private static RichTextView Realized(EditorState state)
    {
        var view = new RichTextView { State = state };

        view.Measure(new Size(400, 2000));
        view.Arrange(new Rect(0, 0, 400, 2000));

        return view;
    }

    [AvaloniaFact]
    public void The_caret_block_is_current_when_the_state_change_is_announced()
    {
        var view = Realized(EditorState.Create(ThreeBlocks()));

        Assert.Equal(3, view.Blocks.Count);

        int? seen = null;
        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichTextView.StateProperty)
            {
                seen = view.CaretBlockIndex;
            }
        };

        // Put the caret inside the third block.
        var doc = view.State.Doc;
        view.State = EditorState.Create(doc, Selection.Near(doc, doc.ContentSize - 2));

        Assert.Equal(view.CaretBlockIndex, seen);
        Assert.Equal(2, seen);
    }

    [AvaloniaFact]
    public void The_block_views_are_current_when_the_state_change_is_announced()
    {
        var view = Realized(EditorState.Create(DocumentNode.FromParagraphs("only")));

        int? seen = null;
        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichTextView.StateProperty)
            {
                seen = view.Blocks.Count;
            }
        };

        view.State = EditorState.Create(ThreeBlocks());

        Assert.Equal(3, seen);
    }

    [AvaloniaFact]
    public void The_new_state_is_readable_from_the_view_when_the_change_is_announced()
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs("before")) };
        var replacement = EditorState.Create(DocumentNode.FromParagraphs("after"));

        EditorState? seen = null;
        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichTextView.StateProperty)
            {
                seen = view.State;
            }
        };

        view.State = replacement;

        Assert.Same(replacement, seen);
    }

    [AvaloniaFact]
    public void The_announcement_still_carries_the_old_and_new_values()
    {
        var first = EditorState.Create(DocumentNode.FromParagraphs("first"));
        var second = EditorState.Create(DocumentNode.FromParagraphs("second"));
        var view = new RichTextView { State = first };

        object? oldValue = null;
        object? newValue = null;
        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichTextView.StateProperty)
            {
                oldValue = e.OldValue;
                newValue = e.NewValue;
            }
        };

        view.State = second;

        Assert.Same(first, oldValue);
        Assert.Same(second, newValue);
    }
}
