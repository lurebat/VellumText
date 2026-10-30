using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Two small hand-off defects: the vertical-move goal column outliving the document it was
/// measured in, and the viewer not repainting when its selection brush changes.
/// </summary>
public class GoalColumnAndRepaintTests
{
    private static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));
        view.Focus();

        return (window, view);
    }

    /// <summary>
    /// The goal column is an x position in the document it was measured in; a host loading a
    /// different document must not leave the next Up or Down aiming for it.
    /// </summary>
    [AvaloniaFact]
    public void The_goal_column_is_forgotten_when_the_host_replaces_the_document()
    {
        var (window, view) = Shown(DocumentNode.FromParagraphs("a long first line of text", "second line of text"));

        view.MoveTo(view.BlockStart + 20, extend: false);
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.NotNull(view.GoalX);

        var fresh = DocumentNode.FromParagraphs("xy", "zw long enough line here");

        view.State = EditorState.Create(fresh, TextSelection.Cursor(2));

        Assert.Null(view.GoalX);
    }

    /// <summary>
    /// The control: moving vertically through the same document keeps the column, which is the
    /// point of having one.
    /// </summary>
    [AvaloniaFact]
    public void The_goal_column_survives_moving_through_the_same_document()
    {
        var (window, view) = Shown(DocumentNode.FromParagraphs("a long first line of text", "x", "third line of text here"));

        view.MoveTo(view.BlockStart + 20, extend: false);
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        var goal = view.GoalX;

        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);

        Assert.NotNull(goal);
        Assert.Equal(goal, view.GoalX);
    }

    [AvaloniaFact]
    public void The_viewer_repaints_when_the_selection_brush_changes()
    {
        var viewer = new RichTextViewer { Document = DocumentNode.FromParagraphs("hello world") };
        var window = new Window { Content = viewer, Width = 300, Height = 100 };

        window.Show();
        viewer.SelectAll();
        Dispatcher.UIThread.RunJobs();

        var before = Frame(window);

        viewer.SelectionBrush = global::Avalonia.Media.Brushes.Red;
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(before, Frame(window));
    }

    private static string Frame(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();

        frame.Save(stream, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return Convert.ToBase64String(stream.ToArray());
    }
}
