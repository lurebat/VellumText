using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Presenters;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Scrolling, and the virtualization that hangs off it.
/// </summary>
/// <remarks>
/// <para>
/// The claim being tested is not "the view can be scrolled" — a plain scroll viewer did that
/// already — but "a document longer than the screen is not laid out". So the assertions are
/// about <see cref="RichTextView.RealizedBlocks"/> and about geometry after a scroll, not about
/// pixels: a view that quietly laid everything out would draw identically and answer every
/// geometry question identically, and would have missed the entire point.
/// </para>
/// <para>
/// Paragraphs here are deliberately of uneven length. With blocks of one uniform height the
/// estimate for an unmeasured block is exactly right, every correction is zero, and the anchoring
/// that stops the text sliding under the reader can be deleted without a test noticing.
/// </para>
/// </remarks>
public class RichTextViewScrollTests
{
    private const double Width = 300;
    private const double Height = 200;
    private const double Narrow = 120;

    // ---- that it virtualizes at all ---------------------------------------------------------

    [AvaloniaFact]
    public void A_document_taller_than_the_viewport_lays_out_only_what_fits()
    {
        var view = Laid(Paragraphs(400));

        var realized = view.RealizedBlocks.End.Value - view.RealizedBlocks.Start.Value;

        Assert.Equal(400, view.Slots.Length);
        Assert.InRange(realized, 1, 60);
    }

    [AvaloniaFact]
    public void Nothing_is_virtualized_when_nothing_is_scrolling_the_view()
    {
        // What a view hosted in a border or a stack panel gets: an unbounded height, and no
        // window to lay out a part of. The demo does exactly this, so it has to keep working.
        var view = new RichTextView { State = EditorState.Create(Paragraphs(40)) };

        view.Measure(new Size(Width, double.PositiveInfinity));

        Assert.Equal(new Range(0, 40), view.RealizedBlocks);
        Assert.Equal(view.Extent.Height, view.DesiredSize.Height, 0);
    }

    [AvaloniaFact]
    public void The_extent_is_the_whole_document_and_not_the_window()
    {
        var view = Laid(Paragraphs(400));

        Assert.True(
            view.Extent.Height > Height * 10,
            $"A 400-paragraph document reported an extent of {view.Extent.Height}.");
    }

    [AvaloniaFact]
    public void Scrolling_lays_out_the_blocks_that_came_into_view()
    {
        var view = Laid(Paragraphs(400));
        var before = view.RealizedBlocks;

        Scroll(view, view.Extent.Height / 2);

        var after = view.RealizedBlocks;

        Assert.True(
            after.Start.Value > before.End.Value,
            $"Scrolling half a document moved the window from {before} to {after}.");
    }

    [AvaloniaFact]
    public void A_block_scrolled_past_is_not_laid_out_again_to_answer_for_the_one_on_screen()
    {
        // The window is bounded at both ends, not just at the bottom. A window that only ever
        // grew downwards would virtualize the first screenful and nothing after it.
        var view = Laid(Paragraphs(400));

        Scroll(view, view.Extent.Height / 2);

        var realized = view.RealizedBlocks.End.Value - view.RealizedBlocks.Start.Value;

        Assert.InRange(realized, 1, 60);
    }

    // ---- geometry follows the scroll --------------------------------------------------------

    [AvaloniaFact]
    public void Block_origins_move_up_by_the_scroll_offset()
    {
        var view = Laid(Paragraphs(400));
        var index = view.RealizedBlocks.Start.Value + 2;
        var before = view.BlockOrigin(index).Y;

        Scroll(view, 40);

        Assert.Equal(before - 40, view.BlockOrigin(index).Y, 3);
    }

    [AvaloniaFact]
    public void A_click_lands_on_the_text_that_scrolled_under_it()
    {
        var view = Laid(Paragraphs(400));
        var before = view.PositionAt(new Point(4, Height / 2));

        Scroll(view, view.Extent.Height / 2);

        Assert.True(
            view.PositionAt(new Point(4, Height / 2)) > before,
            "A click in the middle of the viewport reported the same position after scrolling.");
    }

    [AvaloniaFact]
    public void The_pointer_is_over_text_at_the_top_of_a_scrolled_view()
    {
        // The regression this guards: with the offset left out of the hit test, the top of a
        // scrolled viewport reports whatever block sits at document y = 0, whose geometry is
        // hundreds of pixels away, and the I-beam disappears everywhere below the fold.
        var view = Laid(Paragraphs(400));

        Scroll(view, view.Extent.Height / 2);

        Assert.True(view.IsOverContent(new Point(4, 4)));
    }

    // ---- the offset itself ------------------------------------------------------------------

    [AvaloniaFact]
    public void The_offset_is_clamped_to_the_document()
    {
        var view = Laid(Paragraphs(400));

        view.Offset = new Vector(0, view.Extent.Height * 10);

        Assert.Equal(view.Extent.Height - view.Viewport.Height, view.Offset.Y, 3);
    }

    [AvaloniaFact]
    public void The_offset_never_goes_above_the_document()
    {
        var view = Laid(Paragraphs(400));

        view.Offset = new Vector(0, -500);

        Assert.Equal(0, view.Offset.Y);
    }

    [AvaloniaFact]
    public void A_document_shorter_than_the_viewport_does_not_scroll()
    {
        var view = Laid(Paragraphs(2));

        view.Offset = new Vector(0, 500);

        Assert.Equal(0, view.Offset.Y);
    }

    [AvaloniaFact]
    public void The_block_under_the_top_of_the_viewport_stays_put_while_estimates_are_replaced()
    {
        // The anchoring rule. Laying out a screenful replaces estimated heights with real ones,
        // which moves every block below the correction; without putting the anchor back the
        // words the reader is looking at jump on every scroll.
        var view = Laid(Paragraphs(400));

        Scroll(view, view.Extent.Height / 2);

        var anchor = view.BlockIndexAt(0);
        var before = view.BlockOrigin(anchor).Y;

        // A second pass with nothing changed: any movement here is the estimate settling, and
        // the anchor is what has to absorb it.
        view.InvalidateMeasure();
        Pump(view);

        Assert.Equal(anchor, view.BlockIndexAt(0));
        Assert.Equal(before, view.BlockOrigin(anchor).Y, 1);
    }

    // ---- the caret --------------------------------------------------------------------------

    [AvaloniaFact]
    public void Moving_the_caret_off_the_bottom_scrolls_it_into_view()
    {
        var view = Laid(Paragraphs(400));

        MoveToLastBlock(view);

        Assert.True(view.Offset.Y > 0, "The caret went to the end of the document and nothing scrolled.");
        Assert.InRange(view.CaretRect().Bottom, 0, Height);
    }

    [AvaloniaFact]
    public void Moving_the_caret_back_to_the_top_scrolls_back()
    {
        var view = Laid(Paragraphs(400));

        MoveToLastBlock(view);
        view.State = EditorState.Create(view.State.Doc, TextSelection.Cursor(1));
        Pump(view);

        Assert.Equal(0, view.Offset.Y);
    }

    [AvaloniaFact]
    public void Scrolling_lands_on_the_block_that_was_scrolled_to()
    {
        // What a scroll bar drag means: the offset is picked against the height map as it stands,
        // which for a document nobody has read is mostly estimates. Laying out the screenful that
        // lands there replaces those estimates and moves everything below them — so the offset
        // that was picked no longer points at the block it was picked to point at. Re-anchoring is
        // what keeps the drag honest.
        var view = Laid(Paragraphs(400));
        var target = view.Extent.Height / 2;
        var expected = view.BlockIndexAt(target);

        Scroll(view, target);

        Assert.Equal(expected, view.BlockIndexAt(0));
    }

    [AvaloniaFact]
    public void Resizing_gives_the_document_the_height_that_width_deserves()
    {
        // A block laid out at one width is not the height it is at another, so a resize makes
        // every height on record a lie — including the ones for blocks measured earlier and since
        // scrolled past, which nothing is going to re-measure on its own. Reading the document
        // through and then narrowing it is what leaves those behind.
        var view = Laid(Paragraphs(400));

        Scroll(view, view.Extent.Height);
        Scroll(view, 0);

        Pump(view, Narrow, Height);

        var born = new RichTextView { State = EditorState.Create(Paragraphs(400)) };
        var root = new Window { Width = Narrow, Height = Height, Content = born };

        root.Show();
        Pump(born, Narrow, Height);

        Assert.Equal(born.Extent.Height, view.Extent.Height, 1);
    }

    [AvaloniaFact]
    public void Wildly_uneven_blocks_still_leave_the_view_consistent_and_settled()
    {
        // The mean estimate is at its worst when most blocks are tiny and a few are enormous, so
        // this is where Settle's bounded loop is most likely to give up early. What has to hold is
        // not that a scroll lands on the block the old estimates pointed at — correcting an
        // estimate is what makes that number mean something else, and every virtualizing control
        // has that property. What has to hold is that the view is never left describing itself
        // wrongly, and never left drifting.
        var blocks = Enumerable.Range(0, 500)
            .Select(i => ParagraphNode.FromText(i % 17 == 0 ? new string('y', 3000) : "short"))
            .Cast<BlockNode>();

        var view = Laid(new DocumentNode([.. blocks]));

        for (var step = 1; step < 60; step++)
        {
            Scroll(view, step / 60.0 * (view.Extent.Height - Height));

            var top = view.BlockIndexAt(0);

            Assert.True(
                view.BlockOrigin(top).Y <= 0.5,
                $"Block {top} is named as the top one but is drawn at {view.BlockOrigin(top).Y}.");

            var settled = view.Offset.Y;

            Pump(view);

            Assert.Equal(settled, view.Offset.Y, 1);
        }
    }

    [AvaloniaFact]
    public void Arrowing_past_a_tall_image_reaches_the_paragraph_beyond_it()
    {
        // Vertical movement skips whatever is not a paragraph, so the block it lands on can be
        // arbitrarily far from the one it left — further than the realized window reaches, if
        // what sits between them is tall enough. It has never been laid out, and asking an
        // unmeasured paragraph where its lines are is an exception.
        var doc = new DocumentNode(
        [
            ParagraphNode.FromText("above the image"),
            new BlockImageNode(new ImageEmbed("http://example.invalid/a.png", 40, 4000)),
            ParagraphNode.FromText("below the image"),
            .. Enumerable.Range(0, 100).Select(i => ParagraphNode.FromText($"tail {i}")),
        ]);

        var view = Laid(doc);

        view.Focus();
        view.State = EditorState.Create(doc, TextSelection.Cursor(1));
        Pump(view);

        Press(view, Key.Down);

        Assert.Equal(2, view.CaretBlockIndex);
    }

    [AvaloniaFact]
    public void A_document_that_opens_mid_way_scrolls_no_further_than_it_has_to()
    {
        // Bringing a caret into view means the smallest scroll that does it, so the caret lands
        // against the edge it came in from and the text around it stays where the reader expects.
        // Jumping it to the top of the viewport instead is a different, and wrong, answer.
        var doc = Paragraphs(400);
        var view = new RichTextView
        {
            State = EditorState.Create(doc, TextSelection.Cursor(doc.ContentSize / 2)),
        };

        var root = new Window { Width = Width, Height = Height, Content = view };

        root.Show();
        Pump(view);

        var caret = view.CaretRect();

        Assert.InRange(caret.Bottom, Height - 40, Height + 1);
    }

    [AvaloniaFact]
    public void A_document_that_opens_with_its_caret_at_the_end_shows_the_end()
    {
        // The state can already carry a selection before the control has ever been laid out — a
        // restored document is exactly that. The first measure runs before any arrange, so the
        // viewport is not known yet, and a request to show the caret made then must survive
        // until there is a viewport to show it in rather than being spent on nothing.
        var doc = Paragraphs(400);
        var view = new RichTextView
        {
            State = EditorState.Create(doc, TextSelection.Cursor(doc.ContentSize - 1)),
        };

        var root = new Window { Width = Width, Height = Height, Content = view };

        root.Show();
        Pump(view);

        Assert.True(view.Offset.Y > 0, "The document opened at the top with the caret far below.");

        var caret = view.CaretRect();

        Assert.InRange(caret.Bottom, 0, Height);
    }

    [AvaloniaFact]
    public void A_settled_view_stays_put_when_it_is_laid_out_again()
    {
        // A layout pass that changes nothing must scroll nothing. The caret landing a hair's
        // breadth outside the viewport is arithmetic, not a caret off the screen, and acting on it
        // would creep the view on every keystroke.
        var view = Laid(Paragraphs(400));

        MoveToLastBlock(view);

        var offset = view.Offset.Y;

        for (var i = 0; i < 5; i++)
        {
            Pump(view);
            Assert.Equal(offset, view.Offset.Y);
        }
    }

    [AvaloniaFact]
    public void A_caret_already_on_screen_does_not_move_the_text()
    {
        // Asserted on where the text is, not on the offset. Laying out a screenful replaces
        // estimated heights with real ones, so the offset that keeps the same words on screen is
        // a different number afterwards — and the number is not what the reader can see.
        var view = Laid(Paragraphs(400));

        Scroll(view, view.Extent.Height / 2);

        var top = view.BlockIndexAt(0);
        var before = view.BlockOrigin(top).Y;
        var head = view.PositionAt(new Point(4, Height / 2));

        view.State = EditorState.Create(view.State.Doc, TextSelection.Cursor(head));
        Pump(view);

        Assert.Equal(top, view.BlockIndexAt(0));
        Assert.Equal(before, view.BlockOrigin(top).Y, 1);
    }

    [AvaloniaFact]
    public void A_block_nowhere_near_the_window_is_laid_out_before_it_is_asked_for_geometry()
    {
        // Ctrl+End in a long document: the caret's block has never been measured, and a caret
        // rectangle taken from an unmeasured block is a zero-height rectangle at the origin.
        var view = Laid(Paragraphs(400));

        MoveToLastBlock(view);

        Assert.True(view.CaretRect().Height > 1, "The caret at the end of the document had no height.");
    }

    // ---- talking to the scroll viewer -------------------------------------------------------

    [AvaloniaFact]
    public void The_scroll_viewer_is_told_when_the_document_changes_size()
    {
        var view = Laid(Paragraphs(400));
        var raised = 0;

        ((ILogicalScrollable)view).ScrollInvalidated += (_, _) => raised++;

        view.State = EditorState.Create(Paragraphs(800));
        Pump(view);

        Assert.True(raised > 0, "The extent changed and the scroll viewer was never told.");
    }

    [AvaloniaFact]
    public void Logical_scrolling_is_offered_to_whatever_hosts_the_view()
    {
        Assert.True(((ILogicalScrollable)new RichTextView()).IsLogicalScrollEnabled);
    }

    [AvaloniaFact]
    public void A_scroll_presenter_is_told_the_size_of_the_document()
    {
        var presenter = Hosted(Paragraphs(400), out var view);

        Assert.Same(view, presenter.Child);
        Assert.Equal(view.Extent.Height, presenter.Extent.Height, 1);
        Assert.Equal(new Size(Width, Height), presenter.Viewport);
    }

    [AvaloniaFact]
    public void A_scroll_presenter_moves_the_view_when_it_is_scrolled()
    {
        // The other half of the contract, and the half that only the real presenter can prove:
        // it pushes its own offset into whatever it is scrolling.
        var presenter = Hosted(Paragraphs(400), out var view);

        presenter.Offset = new Vector(0, 900);
        Pump(presenter);

        Assert.True(
            view.Offset.Y > Height,
            $"The presenter scrolled to 900 and the view was left at {view.Offset.Y}.");
        Assert.True(
            view.RealizedBlocks.Start.Value > 5,
            $"The presenter scrolled to 900 and the window was still {view.RealizedBlocks}.");
    }

    /// <summary>
    /// The scroll bar has to follow the view when the view moves itself. Ctrl+End scrolls the
    /// caret into view from a key press, not from a layout pass, and the offset and extent it
    /// changed were only ever reported from arrange — which nothing had invalidated. Found live:
    /// the view showed the end of the document while the scroll bar's thumb sat at the top.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_End_moves_the_scroll_bar_with_the_view()
    {
        var presenter = Hosted(Paragraphs(400), out var view);

        view.Focus();
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.End,
            KeyModifiers = KeyModifiers.Control,
            Source = view,
        });
        presenter.UpdateLayout();

        Assert.True(view.Offset.Y > Height, $"Ctrl+End left the view at {view.Offset.Y}.");
        Assert.Equal(view.Offset.Y, presenter.Offset.Y, 1);
        Assert.Equal(view.Extent.Height, presenter.Extent.Height, 1);
    }

    /// <summary>
    /// A size the view learns outside a layout pass still reaches the scroll bar. A geometry
    /// question about a block far from the window measures it there and then, which changes the
    /// document's height — and that change was only ever reported from arrange, which nothing had
    /// asked for. The scroll bar kept the old range, so its thumb and the text drifted apart and
    /// an offset the bar thought valid was one the view clamped away (found live).
    /// </summary>
    [AvaloniaFact]
    public void A_height_learned_outside_layout_reaches_the_scroll_bar()
    {
        var presenter = Hosted(Paragraphs(400), out var view);
        var before = view.Extent.Height;

        _ = view.ImageRect(view.Slots[^1].Start);
        _ = view.ImageRect(view.Slots[^2].Start);

        Assert.NotEqual(before, view.Extent.Height, 1);

        presenter.UpdateLayout();

        Assert.Equal(view.Extent.Height, presenter.Extent.Height, 1);
    }
    [AvaloniaFact]
    public void The_editor_template_makes_the_view_the_scroll_viewer_s_own_content()
    {
        // ScrollContentPresenter looks at its immediate child and nothing deeper, so a view
        // wrapped one layer further in — for a page effect, say — is scrolled the ordinary way
        // and lays the whole document out. This is the test that the template's nesting is the
        // nesting virtualization needs.
        var editor = new RichTextEditor { State = EditorState.Create(Paragraphs(4)) };
        var root = new Window { Width = Width, Height = Height, Content = editor };

        root.Show();
        Pump(editor);

        var scroller = editor.GetVisualDescendants().OfType<ScrollViewer>().Single();

        Assert.NotNull(editor.View);
        Assert.Same(editor.View, scroller.Content);
    }

    // ---- width ------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_narrower_view_reports_a_taller_document()
    {
        // Every height on record was measured at the old width and is now a guess again. A view
        // that kept them would report a document that cannot wrap the way it is drawn.
        var view = Laid(Paragraphs(400));
        var before = view.Extent.Height;

        view.Measure(new Size(Width / 2, Height));
        view.Arrange(new Rect(0, 0, Width / 2, Height));

        Assert.True(
            view.Extent.Height > before,
            $"Halving the width left the document {view.Extent.Height} tall, down from {before}.");
    }

    // ---- helpers ----------------------------------------------------------------------------

    /// <summary>A document of paragraphs of uneven length; see the note on the class.</summary>
    private static DocumentNode Paragraphs(int count) =>
        new(Enumerable
            .Range(0, count)
            .Select(i => (BlockNode)ParagraphNode.FromText(
                string.Join(' ', Enumerable.Repeat($"block {i}", 1 + (i % 7)))))
            .ToArray());

    private static RichTextView Laid(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var root = new Window { Width = Width, Height = Height, Content = view };

        root.Show();
        Pump(view);

        return view;
    }

    // ---- geometry about blocks the window is nowhere near ------------------------------------

    [AvaloniaFact]
    public void A_block_edited_outside_the_window_can_still_answer_a_geometry_question()
    {
        // Two facts had drifted apart: whether the height index holds a measurement for a block,
        // and whether the block's view still has a layout. Reconciling an edit throws the layout
        // away and leaves the index untouched, and only the realized window is repaired -- so a
        // block scrolled far out of sight ended up recorded as measured with nothing to measure
        // against, and the next question about it threw out of MeasureOverride.
        var view = Focused(Paragraphs(200));

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.InsertText("Z");

        Scroll(view, view.Extent.Height - Height);

        Assert.True(
            view.RealizedBlocks.Start.Value > 0,
            $"The edited block is still realized: {view.RealizedBlocks}.");

        Assert.Null(Record.Exception(() => view.Undo()));
        Assert.StartsWith("block 0", view.State.Doc.Resolve(view.Slots[0].Start).Paragraph!.Content.Text);
    }

    [AvaloniaFact]
    public void The_caret_can_be_scrolled_back_to_a_block_that_was_edited_out_of_the_window()
    {
        var view = Focused(Paragraphs(200));

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.InsertText("Z");

        Scroll(view, view.Extent.Height - Height);
        view.Undo();

        Pump(view);

        Assert.Equal(0, view.Offset.Y, 0);
    }

    private static RichTextView Focused(DocumentNode doc)
    {
        Hosted(doc, out var view);
        view.Focus();

        return view;
    }
    /// <summary>Puts a view inside a real scroll presenter and lays the pair out.</summary>
    /// <remarks>
    /// The presenter rather than a <see cref="ScrollViewer"/>, because the headless test
    /// application loads no theme: a scroll viewer with no template never builds the presenter,
    /// and the presenter is the whole of the contract being tested. Constructing it directly
    /// tests the same class the real control template would have produced.
    /// </remarks>
    private static ScrollContentPresenter Hosted(DocumentNode doc, out RichTextView view)
    {
        view = new RichTextView { State = EditorState.Create(doc) };

        var presenter = new ScrollContentPresenter { CanVerticallyScroll = true, Content = view };
        var root = new Window { Width = Width, Height = Height, Content = presenter };

        root.Show();
        Pump(presenter);

        return presenter;
    }

    private static void Pump(Layoutable target, double width = Width, double height = Height)
    {
        // Twice, because the first pass is what discovers the viewport: measure works from the
        // constraint and arrange is what tells the view how big it actually is.
        for (var i = 0; i < 2; i++)
        {
            target.Measure(new Size(width, height));
            target.Arrange(new Rect(0, 0, width, height));
        }
    }

    private static void Press(RichTextView view, Key key) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            Source = view,
        });

    private static void Scroll(RichTextView view, double y)
    {
        view.Offset = new Vector(0, y);
        Pump(view);
    }

    private static void MoveToLastBlock(RichTextView view)
    {
        var doc = view.State.Doc;
        var last = view.Slots[^1];

        view.State = EditorState.Create(doc, TextSelection.Cursor(last.Start));
        Pump(view);
    }
}
