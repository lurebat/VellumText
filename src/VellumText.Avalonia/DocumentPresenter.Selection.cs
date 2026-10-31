using Avalonia;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>How a selected range is filled.</summary>
public abstract partial class DocumentPresenter
{
    /// <summary>Defines the <see cref="SelectionCornerRadius"/> property.</summary>
    public static readonly StyledProperty<double> SelectionCornerRadiusProperty =
        AvaloniaProperty.Register<DocumentPresenter, double>(
            nameof(SelectionCornerRadius), coerce: (_, value) => double.IsFinite(value) && value > 0 ? value : 0);

    /// <summary>How far the outer corners of a selection highlight are rounded, in pixels.</summary>
    /// <remarks>
    /// <para>
    /// Zero, the default, fills each line's rectangle square, as Word and most platform text boxes
    /// do. A few pixels gives VS Code's <c>roundedSelection</c>: the lines of a selection are drawn
    /// as one shape, rounded only where the outline turns a corner, so two lines that meet join
    /// without a notch. Lines separated by paragraph spacing are rounded apart.
    /// </para>
    /// <para>
    /// A rectangle of table cells is always square: it is a grid, not a run of text. A negative or
    /// non-finite value is taken as zero rather than throwing, because it arrives through styles
    /// and bindings as often as through code.
    /// </para>
    /// </remarks>
    public double SelectionCornerRadius
    {
        get => GetValue(SelectionCornerRadiusProperty);
        set => SetValue(SelectionCornerRadiusProperty, value);
    }

    /// <summary>The selected range, or null when nothing is selected or the selection is not a range.</summary>
    private protected abstract (int From, int To)? HighlightedRange { get; }

    /// <summary>The rectangles the selected range fills, one per line, for tests.</summary>
    internal List<Rect> SelectionRects()
    {
        var rects = new List<Rect>();

        if (HighlightedRange is { } range)
        {
            EnumerateRange(range.From, range.To, rects.Add);
        }

        return rects;
    }

    /// <summary>Fills a range's rectangles, square or rounded as <see cref="SelectionCornerRadius"/> says.</summary>
    /// <param name="context">The surface to draw on.</param>
    /// <param name="brush">The fill.</param>
    /// <param name="from">The start of the range.</param>
    /// <param name="to">The end of the range.</param>
    private protected void FillRange(DrawingContext context, IBrush brush, int from, int to)
    {
        var radius = SelectionCornerRadius;

        if (radius <= 0)
        {
            EnumerateRange(from, to, rect => context.FillRectangle(brush, rect));

            return;
        }

        var rects = new List<Rect>();

        EnumerateRange(from, to, rects.Add);

        if (RoundedOutline(rects, radius) is { } outline)
        {
            context.DrawGeometry(brush, null, outline);
        }
    }

    /// <summary>
    /// One figure per rectangle, each rounded only at the corners no neighbouring line covers.
    /// </summary>
    /// <remarks>
    /// The rectangles come one per line, top to bottom. A corner is left square where the line
    /// above (for a top corner) or below (for a bottom one) touches this line and reaches past
    /// that corner, which is what joins the lines of a selection into one shape. Non-zero filling
    /// keeps the overlap of two figures from cancelling out.
    /// </remarks>
    internal static Geometry? RoundedOutline(IReadOnlyList<Rect> rects, double radius)
    {
        if (rects.Count == 0)
        {
            return null;
        }

        var geometry = new StreamGeometry();

        using (var g = geometry.Open())
        {
            g.SetFillRule(FillRule.NonZero);

            for (var i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                var above = Touching(rects, i, -1);
                var below = Touching(rects, i, +1);
                var max = Math.Min(r.Width, r.Height) / 2;

                var topLeft = above is { } a1 && a1.Left <= r.Left + 0.5 ? 0 : Math.Min(radius, max);
                var topRight = above is { } a2 && a2.Right >= r.Right - 0.5 ? 0 : Math.Min(radius, max);
                var bottomLeft = below is { } b1 && b1.Left <= r.Left + 0.5 ? 0 : Math.Min(radius, max);
                var bottomRight = below is { } b2 && b2.Right >= r.Right - 0.5 ? 0 : Math.Min(radius, max);

                g.BeginFigure(new Point(r.Left + topLeft, r.Top), true);
                g.LineTo(new Point(r.Right - topRight, r.Top));
                Corner(g, topRight, new Point(r.Right, r.Top + topRight));
                g.LineTo(new Point(r.Right, r.Bottom - bottomRight));
                Corner(g, bottomRight, new Point(r.Right - bottomRight, r.Bottom));
                g.LineTo(new Point(r.Left + bottomLeft, r.Bottom));
                Corner(g, bottomLeft, new Point(r.Left, r.Bottom - bottomLeft));
                g.LineTo(new Point(r.Left, r.Top + topLeft));
                Corner(g, topLeft, new Point(r.Left + topLeft, r.Top));
                g.EndFigure(true);
            }
        }

        return geometry;

        static void Corner(StreamGeometryContext g, double radius, Point to)
        {
            if (radius > 0)
            {
                g.ArcTo(to, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            }
        }

        static Rect? Touching(IReadOnlyList<Rect> rects, int i, int step)
        {
            var j = i + step;

            if (j < 0 || j >= rects.Count)
            {
                return null;
            }

            var r = rects[i];
            var other = rects[j];
            var touches = step < 0
                ? Math.Abs(other.Bottom - r.Top) < 0.75
                : Math.Abs(r.Bottom - other.Top) < 0.75;

            return touches && other.Right > r.Left && other.Left < r.Right ? other : null;
        }
    }
}
