using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>Square or rounded selection highlights, in the editor and the viewer.</summary>
public class SelectionCornerTests
{
    private const int Width = 300;
    private const int Height = 160;

    [AvaloniaFact]
    public void TheDefaultIsSquare()
    {
        Assert.Equal(0, new RichTextView().SelectionCornerRadius);
        Assert.Equal(0, new RichTextViewer().SelectionCornerRadius);
        Assert.Equal(0, new RichTextEditor().SelectionCornerRadius);
    }

    [AvaloniaFact]
    public void ASquareSelectionFillsItsCorners()
    {
        var (window, view) = Selected(radius: 0);
        var box = Line(view, 0);

        Assert.True(IsSelected(window, box.Left + 1.5, box.Top + 1.5));
    }

    [AvaloniaFact]
    public void ARoundedSelectionLeavesItsOuterCornersOpen()
    {
        var (window, view) = Selected(radius: 6);
        var first = Line(view, 0);
        var last = Line(view, LineCount(view) - 1);

        Assert.False(IsSelected(window, first.Left + 1.5, first.Top + 1.5), "top-left corner");
        Assert.False(IsSelected(window, last.Right - 1.5, last.Bottom - 1.5), "bottom-right corner");

        // The middle of a line is still selected, rounded or not.
        Assert.True(IsSelected(window, first.Center.X, first.Center.Y));
    }

    [AvaloniaFact]
    public void LinesThatMeetAreJoinedWithoutANotch()
    {
        // Two full lines share their left edge: rounding the corner where they meet would cut a
        // notch into the middle of the selection.
        var (window, view) = Selected(radius: 6);
        var first = Line(view, 0);
        var second = Line(view, 1);

        Assert.Equal(first.Bottom, second.Top, 0.75);
        Assert.True(IsSelected(window, first.Left + 1.5, first.Bottom - 1.5), "bottom-left of the first line");
        Assert.True(IsSelected(window, second.Left + 1.5, second.Top + 1.5), "top-left of the second line");
    }

    [AvaloniaFact]
    public void TheViewerRoundsItsSelectionToo()
    {
        var viewer = new RichTextViewer
        {
            Document = Doc(),
            SelectionBrush = Brushes.Lime,
            SelectionCornerRadius = 6,
        };
        var window = Window(viewer);

        viewer.SelectAll();
        window.UpdateLayout();

        var box = viewer.SelectionRects().First();

        Assert.False(IsSelected(window, box.Left + 1.5, box.Top + 1.5));
        Assert.True(IsSelected(window, box.Center.X, box.Center.Y));
    }

    [AvaloniaTheory]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonsenseRadiusIsSquare(double radius)
    {
        var view = new RichTextView { SelectionCornerRadius = radius };

        Assert.Equal(0, view.SelectionCornerRadius);
    }

    [AvaloniaFact]
    public void ChangingTheRadiusRedraws()
    {
        var (window, view) = Selected(radius: 0);
        var box = Line(view, 0);

        Assert.True(IsSelected(window, box.Left + 1.5, box.Top + 1.5));

        view.SelectionCornerRadius = 6;

        Assert.False(IsSelected(window, box.Left + 1.5, box.Top + 1.5));
    }

    private static DocumentNode Doc() =>
        DocumentNode.FromParagraphs(string.Join(" ", Enumerable.Repeat("selected", 20)));

    private static Window Window(Control content)
    {
        var window = new Window { Width = Width, Height = Height, Content = content, Background = Brushes.White };

        TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);
        window.Show();
        window.UpdateLayout();

        return window;
    }

    private static (Window Window, RichTextView View) Selected(double radius)
    {
        var view = new RichTextView
        {
            State = EditorState.Create(Doc()),
            SelectionBrush = Brushes.Lime,
            SelectionCornerRadius = radius,
        };
        var window = Window(view);

        view.State = EditorState.Create(
            view.State.Doc, TextSelection.Create(view.State.Doc, 1, view.State.Doc.ContentSize - 1));
        window.UpdateLayout();

        return (window, view);
    }

    private static int LineCount(RichTextView view) => view.SelectionRects().Count;

    private static Rect Line(RichTextView view, int index) => view.SelectionRects()[index];

    private static bool IsSelected(Window window, double x, double y)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing was rendered");

        var pixels = RenderedPixels.Rgba(frame);
        var i = (((int)y * frame.PixelSize.Width) + (int)x) * 4;

        // Lime under the text, or the grey where antialiased glyphs sit on lime: green clearly
        // above red and blue either way. White and black glyphs on white are not.
        return pixels[i + 1] > pixels[i] + 60 && pixels[i + 1] > pixels[i + 2] + 60;
    }
}
