using System;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike.App;

/// <summary>
/// Renders a TextLayout and lets the mouse drive real hit testing and selection, so the
/// probe's headless numbers can be confirmed against something a human can actually see.
/// </summary>
public sealed class LiveTextControl : Control
{
    private readonly Func<double, TextLayout> _build;
    private readonly string _text;
    private TextLayout? _layout;
    private double _builtWidth = -1;
    private int _anchor;
    private int _caret;
    private bool _dragging;
    private bool _extend;

    public LiveTextControl(string text, Func<double, TextLayout> build)
    {
        _text = text;
        _build = build;
        Focusable = true;
        Margin = new Thickness(8);
    }

    public event Action<string>? Diagnostics;

    private TextLayout Layout(double width)
    {
        if (_layout is null || Math.Abs(_builtWidth - width) > 0.01)
        {
            _layout?.Dispose();
            _layout = _build(width);
            _builtWidth = width;
        }
        return _layout;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        var layout = Layout(width);
        return new Size(width, layout.Height + 4);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        var hit = Layout(Bounds.Width).HitTestPoint(p);
        _anchor = _caret = hit.TextPosition;
        _dragging = true;
        Focus();
        e.Pointer.Capture(this);
        Report(p, hit);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var hit = Layout(Bounds.Width).HitTestPoint(p);
        if (_dragging)
        {
            _caret = hit.TextPosition;
            InvalidateVisual();
        }
        Report(p, hit);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var layout = Layout(Bounds.Width);
        var line = layout.TextLines[0];

        // 'E' toggles extend mode. Shift state does not survive background-posted key events,
        // so this gives an equivalent way to drive selection from the keyboard.
        if (e.Key == Key.E)
        {
            _extend = !_extend;
            if (!_extend) _anchor = _caret;
            e.Handled = true;
            Report(null, null);
            return;
        }

        // Caret movement over the live layout, exercising the same APIs the probe measured.
        if (e.Key == Key.Right)
            _caret = layout.TextLines
                .Select(l => l.GetNextCaretCharacterHit(new CharacterHit(_caret)))
                .Select(h => h.FirstCharacterIndex + h.TrailingLength)
                .Where(x => x > _caret)
                .DefaultIfEmpty(_caret)
                .Min();
        else if (e.Key == Key.Left)
            _caret = layout.TextLines
                .Select(l => l.GetPreviousCaretCharacterHit(new CharacterHit(_caret)))
                .Select(h => h.FirstCharacterIndex + h.TrailingLength)
                .Where(x => x < _caret)
                .DefaultIfEmpty(_caret)
                .Max();
        else if (e.Key == Key.Back)
        {
            var h = line.GetBackspaceCaretCharacterHit(new CharacterHit(_caret));
            _caret = h.FirstCharacterIndex + h.TrailingLength;
        }
        else return;

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !_extend) _anchor = _caret;
        e.Handled = true;
        Report(null, null);
        InvalidateVisual();
    }

    private void Report(Point? p, TextHitTestResult? hit)
    {
        var sb = new StringBuilder();
        if (p is { } pt && hit is { } h)
            sb.Append($"point ({pt.X:0.#}, {pt.Y:0.#}) -> TextPosition {h.TextPosition}, "
                      + $"IsInside {h.IsInside}, IsTrailing {h.IsTrailing}    ");

        var (start, len) = (Math.Min(_anchor, _caret), Math.Abs(_caret - _anchor));
        sb.Append($"caret {_caret}, selection [{start}, {start + len})");
        if (_extend) sb.Append(" [extend]");

        if (len > 0)
        {
            var rects = Layout(Bounds.Width).TextLines
                .SelectMany(l => l.GetTextBounds(start, len))
                .ToList();
            sb.Append($" -> {rects.Count} rect(s): ");
            sb.Append(string.Join("  ", rects.Select(r =>
                $"[{r.Rectangle.X:0.#},{r.Rectangle.Width:0.#} {r.FlowDirection}]")));
        }

        Diagnostics?.Invoke(sb.ToString());
    }

    public override void Render(DrawingContext context)
    {
        var layout = Layout(Bounds.Width);

        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));

        // Selection behind the text, straight from GetTextBounds.
        var (start, len) = (Math.Min(_anchor, _caret), Math.Abs(_caret - _anchor));
        if (len > 0)
        {
            var y = 0.0;
            foreach (var line in layout.TextLines)
            {
                foreach (var b in line.GetTextBounds(start, len))
                    context.FillRectangle(
                        new SolidColorBrush(Color.FromArgb(70, 30, 110, 230)),
                        b.Rectangle.WithY(b.Rectangle.Y + y));
                y += line.Height;
            }
        }

        layout.Draw(context, new Point(0, 0));

        // Baseline guides, so alignment claims are visually checkable.
        var guide = new Pen(new SolidColorBrush(Color.FromArgb(60, 200, 0, 0)), 1);
        var yy = 0.0;
        foreach (var line in layout.TextLines)
        {
            context.DrawLine(guide, new Point(0, yy + line.Baseline), new Point(Bounds.Width, yy + line.Baseline));
            yy += line.Height;
        }

        // Caret from HitTestTextPosition.
        var caretRect = layout.HitTestTextPosition(_caret);
        context.DrawLine(new Pen(Brushes.Black, 1.5),
            caretRect.TopLeft, new Point(caretRect.X, caretRect.Bottom));
    }
}
