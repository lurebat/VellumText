using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace VellumText.Avalonia;

/// <summary>
/// The view of one <see cref="ParagraphNode"/>, per architecture 4.6.
/// </summary>
/// <remarks>
/// <para>
/// <b>How the run cache is driven.</b> Architecture 4.6 originally sketched
/// <c>_runCache.InvalidateFrom(localOffset)</c> on an edit. Increment 0 measured that and it is
/// wrong: invalidation is entry-granular and a paragraph is a single entry keyed 0, so
/// <c>InvalidateFrom(k)</c> for any k greater than zero returns the <em>pre-edit</em> layout
/// without ever asking the source for a run. The measurement is in
/// <c>docs/increment-0-findings.md</c> §2 and architecture §2.3 records the reversal.
/// </para>
/// <para>
/// So the cache is driven by <em>lifetime, not offset</em>. Content change means the whole
/// cache goes; a width-only change keeps it, which is where the cache actually earns its place
/// — Increment 0 measured that case at roughly 3.5× and it is the case that fires on every
/// window resize, once per paragraph.
/// </para>
/// <para>
/// <b>Long paragraphs are laid out in chunks.</b> One layout costs Avalonia lines × runs, which
/// is quadratic in a long formatted paragraph, so past <see cref="ParagraphChunking.Threshold"/>
/// the paragraph is held as consecutive layouts that each start and end at a line boundary, and an
/// edit lays out again only the chunks it reached. Everything here therefore reads the paragraph
/// through its <see cref="Lines"/>, which both kinds of layout have; see
/// <see cref="ParagraphChunks"/> for why the seams are exact and what is never chunked.
/// </para>
/// </remarks>
internal sealed class ParagraphView : BlockView
{
    private readonly ITextStyleResolver _base;
    private readonly IParagraphStyleResolver _kinds;
    private readonly IEmbedRenderer _embeds;
    private readonly double _indentStep;
    private readonly ParagraphChunking _chunking;
    private ITextStyleResolver _styles;
    private ParagraphStyle _style;
    private TextRunCache _runCache = new();
    private ParagraphNode _paragraph;
    private Preedit? _preedit;
    private TextLayout? _layout;
    private ParagraphChunks? _chunks;
    private ParagraphChunks? _staleChunks;
    private PlacedLine[]? _placed;
    private TextLayout? _markerLayout;
    private ListMarker? _marker;
    private int _depth;
    private double _layoutWidth = double.NaN;

    /// <summary>Creates a view of a paragraph.</summary>
    /// <param name="paragraph">The paragraph to present.</param>
    /// <param name="styles">Resolves mark sets to run properties.</param>
    /// <param name="embeds">Measures and draws embeds, or null for the placeholder renderer.</param>
    /// <param name="kinds">Decides how the paragraph's kind is presented, or null for the default.</param>
    /// <param name="indentStep">Device pixels per level of <see cref="ParagraphNode.IndentLevel"/>.</param>
    /// <param name="chunking">
    /// When a long paragraph is laid out in chunks, or null for <see cref="ParagraphChunking.Default"/>.
    /// </param>
    public ParagraphView(
        ParagraphNode paragraph,
        ITextStyleResolver styles,
        IEmbedRenderer? embeds = null,
        IParagraphStyleResolver? kinds = null,
        double indentStep = DefaultIndentStep,
        ParagraphChunking? chunking = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(styles);

        if (indentStep < 0 || double.IsNaN(indentStep))
        {
            throw new ArgumentOutOfRangeException(
                nameof(indentStep), indentStep, "An indent step cannot be negative.");
        }

        _paragraph = paragraph;
        _base = styles;
        _kinds = kinds ?? new ParagraphStyleResolver();
        _embeds = embeds ?? new PlaceholderEmbedRenderer();
        _indentStep = indentStep;
        _chunking = chunking ?? ParagraphChunking.Default;
        _style = _kinds.Resolve(paragraph.Kind);
        _styles = Styled();
    }

    /// <summary>The default width of one indent step, in device pixels.</summary>
    public const double DefaultIndentStep = 24;

    /// <summary>
    /// How far a block fill's corners are rounded. Small on purpose: enough to stop a code
    /// block reading as a raw scanline, not enough to make it a card.
    /// </summary>
    private const double BackgroundCornerRadius = 4;

    /// <summary>The paragraph currently presented.</summary>
    public ParagraphNode Paragraph => _paragraph;

    /// <summary>
    /// How far the paragraph's whole text block is inset from the left, from its indent level.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>TextParagraphProperties.Indent</c>, which indents only the first
    /// line of a paragraph. An indent level insets the entire block, so it is applied as an
    /// offset of the layout: the layout wraps to a narrower width and every coordinate crossing
    /// this type's boundary is shifted by it.
    /// </remarks>
    public double Indent => ((_paragraph.IndentLevel + _depth) * _indentStep) + _style.LeftInset;

    /// <summary>How deep in a list the paragraph sits; zero when it is not in one.</summary>
    public int Depth => _depth;

    /// <summary>The bullet or number drawn in front of the paragraph, or null for none.</summary>
    public ListMarker? Marker => _marker;

    /// <summary>
    /// Tells the view where in a list its paragraph sits and what to draw in front of it.
    /// </summary>
    /// <remarks>
    /// Not part of the paragraph, which is why it is set rather than read. Nothing in a
    /// paragraph node says what list encloses it or which number it takes, and both can change
    /// without the paragraph changing at all: deleting the first item of a list renumbers every
    /// item after it while leaving their paragraphs untouched, references and all.
    /// </remarks>
    /// <param name="depth">How many list levels enclose the paragraph.</param>
    /// <param name="marker">The bullet or number to draw, or null for none.</param>
    /// <returns>True if anything changed and the caller must redraw.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is negative.</exception>
    public bool SetLead(int depth, ListMarker? marker)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);

        if (depth == _depth && marker.Equals(_marker))
        {
            return false;
        }

        _depth = depth;
        _marker = marker;
        _markerLayout = null;

        // The indent is part of the width the text wraps to, so the layout is stale.
        Invalidate();

        return true;
    }

    /// <summary>
    /// Tells the view what fill its paragraph sits on inside a table cell, for contrast adaptation.
    /// </summary>
    /// <remarks>
    /// Like the lead, not part of the paragraph: the fill belongs to the cell. A paragraph that is
    /// not adapting colours ignores it.
    /// </remarks>
    /// <param name="fill">The cell's fill, or null for none.</param>
    internal void SetBackdrop(Color? fill)
    {
        if (fill == _cellFill)
        {
            return;
        }

        _cellFill = fill;

        if (_base is not ContrastStyles)
        {
            return;
        }

        // Every run's colour may have changed, so neither the layout nor the shaped runs survive.
        _styles = Styled();
        _runCache = new TextRunCache();
        _markerLayout = null;
        Invalidate();
        _staleChunks = null;
    }

    /// <summary>The resolver this paragraph draws with: its kind's, read against its own fill.</summary>
    private ITextStyleResolver Styled()
    {
        var styles = _base;

        if (styles is ContrastStyles contrast)
        {
            if (_cellFill is { } cell)
            {
                contrast = contrast.Over(cell);
            }

            if (_style.Background is ISolidColorBrush { Color: var colour } fill)
            {
                contrast = contrast.Over(
                    Color.FromArgb((byte)Math.Round(colour.A * fill.Opacity), colour.R, colour.G, colour.B));
            }

            styles = contrast;
        }

        return KindStyles.Wrap(styles, _style);
    }

    private Color? _cellFill;

    /// <summary>Where the text sits inside the block, past the indent and the space above.</summary>
    private Point Origin => new(Indent, _style.SpaceBefore + _style.Padding.Top);

    /// <inheritdoc/>
    public override int ContentSize => _paragraph.ContentSize;

    /// <inheritdoc/>
    public override Size Size =>
        !IsMeasured ? default : new Size(
            Indent + TextWidth + _style.Padding.Right,
            _style.SpaceBefore + _style.Padding.Top
                + TextHeight
                + _style.Padding.Bottom + _style.SpaceAfter);

    /// <summary>Whether there is a current layout, in one piece or in chunks.</summary>
    private bool IsMeasured => _layout is not null || _chunks is not null;

    private double TextWidth => _chunks?.Width ?? _layout!.Width;

    private double TextHeight => _chunks?.Height ?? _layout!.Height;

    /// <summary>
    /// The current layout of a paragraph laid out in one piece.
    /// </summary>
    /// <remarks>
    /// A paragraph longer than its <see cref="ParagraphChunking.Threshold"/> is laid out in chunks
    /// instead and has no single layout; everything this type answers goes through its lines,
    /// which both kinds have.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The view has never been measured, or its paragraph is laid out in chunks.
    /// </exception>
    public TextLayout Layout => _layout ?? throw new InvalidOperationException(
        _chunks is null
            ? "The paragraph has not been measured yet; call Measure before asking for geometry."
            : "The paragraph is laid out in chunks and has no single layout.");

    /// <summary>The chunks a long paragraph is laid out in, or null if it is one layout.</summary>
    internal ParagraphChunks? Chunks => _chunks;

    /// <summary>Every line, in order, with where it sits.</summary>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    private PlacedLine[] Lines
    {
        get
        {
            if (_chunks is { } chunks)
            {
                return chunks.Lines;
            }

            if (_placed is { } placed)
            {
                return placed;
            }

            var lines = Layout.TextLines;
            var top = 0.0;

            placed = new PlacedLine[lines.Count];

            for (var i = 0; i < lines.Count; i++)
            {
                placed[i] = new PlacedLine(lines[i], 0, top);
                top += lines[i].Height;
            }

            return _placed = placed;
        }
    }

    /// <summary>Drops the current layout, keeping any chunks to lay out again from.</summary>
    private void Invalidate()
    {
        _layout = null;
        _placed = null;
        _staleChunks = _chunks ?? _staleChunks;
        _chunks = null;
    }

    /// <summary>
    /// Points the view at a new paragraph, discarding the layout if the content differs.
    /// </summary>
    /// <remarks>
    /// Attributes that only affect how existing text is arranged — alignment, indent — still
    /// force a relayout, but they keep the run cache, because the shaped runs themselves are
    /// unaffected. That is the same reasoning as a width change.
    /// </remarks>
    /// <param name="paragraph">The new paragraph.</param>
    /// <returns>True if anything changed and the caller must redraw.</returns>
    public bool Update(ParagraphNode paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        if (ReferenceEquals(paragraph, _paragraph) || paragraph.Equals(_paragraph))
        {
            return false;
        }

        var textChanged = !paragraph.Content.Equals(_paragraph.Content);
        var kindChanged = paragraph.Kind != _paragraph.Kind;

        _paragraph = paragraph;
        Invalidate();

        if (kindChanged)
        {
            // A kind change is a font change — a different size, weight or family for every run
            // in the paragraph — so the shaped runs are worthless and the cache goes with them.
            _style = _kinds.Resolve(paragraph.Kind);
            _styles = Styled();
            _runCache = new TextRunCache();

            // The marker is drawn in the paragraph's own font, which the kind just changed.
            _markerLayout = null;
        }

        if (textChanged)
        {
            // Entry-granular invalidation cannot express a partial edit, so the cache is
            // replaced outright. See the class remarks.
            _runCache = new TextRunCache();

            // A composition is anchored at an offset into text that no longer exists, so it
            // cannot survive the edit that replaced that text. The view commits or abandons a
            // composition before editing; this is the backstop that keeps the anchor from
            // pointing past the end of the paragraph if it ever does not.
            _preedit = null;
        }

        return true;
    }

    /// <summary>
    /// Text an input method is composing here, or null for none.
    /// </summary>
    /// <remarks>
    /// The composition is spliced into the layout and is therefore visible to every geometry
    /// query, but it is not part of the paragraph: <see cref="BlockView.ContentSize"/> does not
    /// count it and no position this type hands out or accepts includes it. See
    /// <see cref="ToLayout"/>.
    /// </remarks>
    public Preedit? Preedit => _preedit;

    /// <summary>Sets or clears the composition being drawn in this paragraph.</summary>
    /// <param name="preedit">The composition, or null to clear it.</param>
    /// <returns>True if anything changed and the caller must redraw.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The composition is anchored outside the paragraph.
    /// </exception>
    public bool SetPreedit(Preedit? preedit)
    {
        if (preedit is not null && preedit.Offset > ContentSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preedit),
                preedit.Offset,
                $"A preedit must be anchored within [0, {ContentSize}] of its paragraph.");
        }

        if (preedit == _preedit)
        {
            return false;
        }

        _preedit = preedit;
        Invalidate();

        // Composing changes the text being shaped, so the same reasoning as an edit applies:
        // the cache cannot express a partial change and is replaced outright.
        _runCache = new TextRunCache();

        return true;
    }

    /// <summary>The length of what is laid out, which counts any composition.</summary>
    private int LayoutSize => ContentSize + (_preedit?.Length ?? 0);

    /// <summary>
    /// A paragraph-local position in the layout's coordinates.
    /// </summary>
    /// <remarks>
    /// The composition occupies positions the document does not have, so everything after its
    /// anchor is displaced by its length. The anchor itself maps to the <em>start</em> of the
    /// composition, which is what puts a caret at the anchor before the composed text rather
    /// than after it.
    /// </remarks>
    private int ToLayout(int localPosition) =>
        _preedit is { } preedit && localPosition > preedit.Offset
            ? localPosition + preedit.Length
            : localPosition;

    /// <summary>A layout position back in the paragraph's coordinates.</summary>
    /// <remarks>
    /// A position <em>inside</em> the composition has no document counterpart, so it collapses
    /// to the anchor. That is what stops a click on composing text from moving the model's
    /// caret into text the model does not have.
    /// </remarks>
    private int FromLayout(int layoutPosition)
    {
        if (_preedit is not { } preedit)
        {
            return layoutPosition;
        }

        return layoutPosition <= preedit.Offset
            ? layoutPosition
            : Math.Max(preedit.Offset, layoutPosition - preedit.Length);
    }

    /// <inheritdoc/>
    public override Size Measure(double availableWidth)
    {
        if (availableWidth <= 0 || double.IsNaN(availableWidth))
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableWidth),
                availableWidth,
                "A paragraph must be given a positive width to wrap to.");
        }

        if (IsMeasured && _layoutWidth.Equals(availableWidth))
        {
            return Size;
        }

        LayoutGuard.Changing("a paragraph laid out");

        // The indent and the padding both eat into the width the text may wrap to, so a padded
        // or indented paragraph wraps where it visually runs out of room rather than where a
        // plain one would. A width smaller than that still has to leave somewhere for the text.
        var textWidth = Math.Max(1, availableWidth - Indent - _style.Padding.Right);
        var properties = ParagraphProperties();
        var source = new InlineTextSource(_paragraph.Content, _styles, _embeds, _preedit);

        _placed = null;

        if (LayoutSize > _chunking.Threshold
            && properties.TextAlignment != TextAlignment.Justify
            && ParagraphChunks.CanChunk(_paragraph.Content.Text, _preedit))
        {
            // Long enough that one layout costs lines × runs: laid out in chunks, from the
            // first one the change since the last layout reached. See ParagraphChunks.
            _chunks = ParagraphChunks.Build(
                source,
                new ParagraphChunks.Key(
                    _paragraph.Content,
                    _preedit,
                    textWidth,
                    properties.TextAlignment,
                    properties.LineHeight,
                    _styles),
                properties,
                _chunking.Window,
                _chunks ?? _staleChunks);
            _layout = null;
        }
        else
        {
            var fetched = WorkCounters.RunsFetched;

            _layout = new TextLayout(
                source,
                properties,
                maxWidth: textWidth,
                textRunCache: _runCache);
            _chunks = null;

            WorkCounters.CharactersLaidOut += LayoutSize;
            ParagraphChunks.CountWrapping(_layout, fetched);
        }

        _staleChunks = null;
        _layoutWidth = availableWidth;

        return Size;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The horizontal extent comes from <c>GetTextBounds</c>, which is right across a bidi
    /// boundary, and the vertical extent from the run itself, because the text bounds are as tall
    /// as the line. The run's top is the line's baseline less the run's own, which is how the
    /// formatter placed it — see <see cref="EmbedRun"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public override Rect? GetImageRect(int localPosition)
    {
        // Deliberately kept although no test can distinguish it: the line search below happens to
        // reject an out-of-range offset, and Avalonia happens to tolerate a negative index in
        // GetTextBounds, but neither is a documented guarantee. Measured — removing this passes
        // the whole suite, which is why the mutation list carries a note rather than a mutant.
        if (localPosition < 0 || localPosition >= ContentSize)
        {
            return null;
        }

        var target = ToLayout(localPosition);

        foreach (var placed in Lines)
        {
            if (target < placed.End)
            {
                var line = placed.Line;

                foreach (var bounds in line.GetTextBounds(target - placed.Offset, 1))
                {
                    foreach (var run in bounds.TextRunBounds)
                    {
                        if (run.TextRun is EmbedRun { Embed: ImageEmbed } embed)
                        {
                            return new Rect(
                                Origin.X + run.Rectangle.X,
                                Origin.Y + placed.Top + line.Baseline - embed.Baseline,
                                embed.Size.Width,
                                embed.Size.Height);
                        }
                    }
                }

                return null;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context, Point origin)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_style.Background is { } background)
        {
            // The block's content box: across the full column, not just as wide as the longest
            // line, and inside the outer spacing rather than over it. A fill that hugged the
            // glyphs would read as a highlight on the text, and one that covered SpaceBefore
            // would close the gap to the paragraph above and merge two code blocks into one.
            var width = double.IsNaN(_layoutWidth) ? Size.Width : _layoutWidth;
            var height = Size.Height - _style.SpaceBefore - _style.SpaceAfter;

            context.DrawRectangle(
                background,
                pen: null,
                new RoundedRect(
                    new Rect(origin.X, origin.Y + _style.SpaceBefore, width, height),
                    BackgroundCornerRadius));
        }

        if (_style.Bar is { } bar)
        {
            // Down the whole block including its spacing, so a run of quoted paragraphs reads as
            // one quotation rather than a column of dashes.
            context.FillRectangle(
                bar, new Rect(origin.X, origin.Y, _style.BarWidth, Size.Height));
        }

        if (_layout is { } layout)
        {
            layout.Draw(context, origin + Origin);
        }
        else
        {
            // What TextLayout.Draw does, line by line, across the chunks.
            var (x, y) = origin + Origin;

            foreach (var placed in Lines)
            {
                placed.Line.Draw(context, new Point(x, y));
                y += placed.Line.Height;
            }
        }

        if (MarkerLayout is { } marker)
        {
            // Right-aligned into the gutter the depth opened up, and on the paragraph's own
            // first baseline so a bullet lines up with the text it belongs to rather than with
            // the top of the block.
            var baseline = Lines[0].Line.Baseline - marker.TextLines[0].Baseline;

            marker.Draw(
                context,
                origin + new Point(Indent - marker.Width - MarkerGap, Origin.Y + baseline));
        }
    }

    /// <summary>The gap between a list marker and the text it introduces, in device pixels.</summary>
    public const double MarkerGap = 6;

    /// <summary>What is drawn in front of the paragraph, or null when nothing is.</summary>
    public string? MarkerText => _marker is { } marker
        ? marker.Kind == ListKind.Ordered ? $"{marker.Ordinal}." : "\u2022"
        : null;

    /// <summary>The laid-out bullet or number, or null when there is none to draw.</summary>
    private TextLayout? MarkerLayout
    {
        get
        {
            if (MarkerText is not { } text)
            {
                return null;
            }

            return _markerLayout ??= new TextLayout(
                text,
                _styles.Default.Typeface,
                _styles.Default.FontRenderingEmSize,
                _styles.Default.ForegroundBrush);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public override int HitTest(Point local)
    {
        var point = local - Origin;
        var position = _layout is { } layout
            ? layout.HitTestPoint(point).TextPosition
            : HitTestLines(point);

        // TextPosition already carries the trailing adjustment: the trailing half of a
        // character reports the position after it, and it does so in whole clusters, so a
        // surrogate pair or a combining sequence is never split by a click.
        return FromLayout(Math.Clamp(position, 0, LayoutSize));
    }

    /// <summary>
    /// <c>TextLayout.HitTestPoint(point).TextPosition</c>, over the lines of a chunked paragraph.
    /// </summary>
    /// <remarks>
    /// The same steps in the same order as Avalonia 12.1.1's, so that a chunked paragraph and an
    /// unchunked one answer alike: the first line whose bottom is below the point, or the last
    /// line; the hit on it; and the trailing newline taken back off a hit at its very end.
    /// </remarks>
    private int HitTestLines(Point point)
    {
        var lines = Lines;
        var placed = lines[^1];

        foreach (var candidate in lines)
        {
            if (candidate.Top + candidate.Line.Height > point.Y)
            {
                placed = candidate;
                break;
            }
        }

        var line = placed.Line;
        var hit = line.GetCharacterHitFromDistance(point.X);
        var position = hit.FirstCharacterIndex + hit.TrailingLength;

        if (position == line.FirstTextSourceIndex + line.Length)
        {
            position -= line.NewLineLength;
        }

        return placed.Offset + position;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="localPosition"/> is outside <c>[0, ContentSize]</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public override Rect GetCaretRect(int localPosition)
    {
        CheckPosition(localPosition);

        return CaretRectInLayout(ToLayout(localPosition));
    }

    /// <summary>The caret rectangle for a composition's own cursor.</summary>
    /// <remarks>
    /// Separate from <see cref="GetCaretRect"/> because a composition cursor sits at a layout
    /// position the document has no counterpart for, which is exactly what that method refuses
    /// to accept.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Nothing is being composed here, or the paragraph has not been measured.
    /// </exception>
    public Rect GetPreeditCaretRect()
    {
        if (_preedit is not { } preedit)
        {
            throw new InvalidOperationException("Nothing is being composed in this paragraph.");
        }

        return CaretRectInLayout(Math.Clamp(preedit.CaretPosition, 0, LayoutSize));
    }

    private Rect CaretRectInLayout(int layoutPosition)
    {
        var rect = _layout is { } layout
            ? layout.HitTestTextPosition(layoutPosition)
            : HitTestPosition(layoutPosition);

        return new Rect(Origin.X + rect.X, Origin.Y + rect.Y, 0, rect.Height);
    }

    /// <summary>
    /// <c>TextLayout.HitTestTextPosition</c>, over the lines of a chunked paragraph.
    /// </summary>
    /// <remarks>
    /// As Avalonia 12.1.1 does it: the first line the position is before the end of, or the last
    /// line, asked for the distance to the position. A position at the end of a line therefore
    /// belongs to the next one, which is also what makes a chunk's end belong to the chunk after.
    /// </remarks>
    private Rect HitTestPosition(int layoutPosition)
    {
        var lines = Lines;

        for (var i = 0; i < lines.Length; i++)
        {
            var placed = lines[i];

            if (placed.End <= layoutPosition && i + 1 < lines.Length)
            {
                continue;
            }

            var line = placed.Line;
            var hit = new CharacterHit(layoutPosition - placed.Offset);
            var start = line.GetDistanceFromCharacterHit(hit);
            var end = line.GetDistanceFromCharacterHit(line.GetNextCaretCharacterHit(hit));

            return new Rect(start, placed.Top, end - start, line.Height);
        }

        return default;
    }

    /// <inheritdoc/>
    /// <remarks>A paragraph is the paragraph, wherever inside it the position falls.</remarks>
    public override TextAt? ParagraphAt(int localPosition) => new(this, 0, default);

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="from"/> or <paramref name="to"/> is outside <c>[0, ContentSize]</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The paragraph has not been measured, and the range is not empty. An empty range answers
    /// with no rectangles before any geometry is needed, so it is not a question the layout has to
    /// exist to answer.
    /// </exception>
    public override IReadOnlyList<Rect> GetSelectionRects(int from, int to)
    {
        CheckPosition(from);
        CheckPosition(to);
        var start = ToLayout(Math.Min(from, to));
        var length = ToLayout(Math.Max(from, to)) - start;

        if (length == 0)
        {
            return [];
        }

        var rects = new List<Rect>();

        foreach (var placed in Lines)
        {
            // A line's range is clipped against the selection rather than tested for
            // containment, because a selection routinely covers part of a line at each end.
            // Avalonia clamps an overrunning length itself — there is a test pinning that — so
            // the upper clip is defensive; the lower one is not.
            var lineStart = Math.Max(start, placed.Start);
            var lineEnd = Math.Min(start + length, placed.End);

            if (lineEnd > lineStart)
            {
                foreach (var bounds in placed.Line.GetTextBounds(lineStart - placed.Offset, lineEnd - lineStart))
                {
                    // One TextBounds per direction run, so a selection crossing a bidi boundary
                    // arrives here as several disjoint rectangles. Increment 0 measured this.
                    //
                    // The rectangle is measured from the top of its own line, not from the top
                    // of the layout, so the line's offset has to be added or every line of a
                    // wrapped selection lands on the first one. Measured before this was here:
                    // a ten-line selection produced ten rectangles all at y=0.
                    rects.Add(bounds.Rectangle.Translate(Origin + new Vector(0, placed.Top)));
                }
            }
        }

        return rects;
    }

    /// <summary>
    /// The next caret position after <paramref name="localPosition"/>, never landing inside a
    /// grapheme cluster.
    /// </summary>
    /// <param name="localPosition">The position to move from.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="localPosition"/> is outside <c>[0, ContentSize]</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public int NextCaretPosition(int localPosition) =>
        Move(localPosition, forward: true, static (line, hit) => line.GetNextCaretCharacterHit(hit));

    /// <summary>
    /// The previous caret position before <paramref name="localPosition"/>, never landing
    /// inside a grapheme cluster.
    /// </summary>
    /// <param name="localPosition">The position to move from.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="localPosition"/> is outside <c>[0, ContentSize]</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public int PreviousCaretPosition(int localPosition) =>
        Move(localPosition, forward: false, static (line, hit) => line.GetPreviousCaretCharacterHit(hit));

    /// <summary>
    /// Where a Backspace from <paramref name="localPosition"/> should delete back to.
    /// </summary>
    /// <remarks>
    /// Not the same as <see cref="PreviousCaretPosition"/>: Backspace peels one combining mark
    /// off a cluster where the arrow key steps over the whole cluster. Increment 0 measured the
    /// difference and the user chose to keep the component-peel behaviour.
    /// </remarks>
    /// <param name="localPosition">The caret position to delete back from.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="localPosition"/> is outside <c>[0, ContentSize]</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public int BackspacePosition(int localPosition) =>
        Move(localPosition, forward: false, static (line, hit) => line.GetBackspaceCaretCharacterHit(hit));

    /// <summary>The number of visual lines the paragraph wrapped onto.</summary>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public int LineCount => Lines.Length;

    /// <inheritdoc/>
    /// <remarks>Whether the paragraph has a layout; the host knows whether it is for the current width.</remarks>
    internal override bool IsPreparedAt(int localPosition) => IsMeasured;

    /// <inheritdoc/>
    /// <remarks>
    /// The first position of the visual line at <paramref name="y"/> and how far below that
    /// line's top the offset is. A paragraph whose layout is gone — a composition dropped it — is
    /// named by its box instead, since there are no lines to read.
    /// </remarks>
    internal override LocalAnchor CaptureAnchor(double y)
    {
        if (!IsMeasured)
        {
            return new LocalAnchor(0, y, AnchorKind.Box);
        }

        var lines = Lines;
        var local = y - Origin.Y;
        var low = 0;
        var high = lines.Length - 1;

        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);

            if (lines[middle].Top <= local)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        var line = lines[low];

        // A line that starts inside composed text and goes on into the paragraph's own text after
        // it is held by that text, which is what the reader sees resume there, so the composition
        // grows upward from it.
        if (_preedit is { Length: > 0 } mixed
            && line.Start > mixed.Offset
            && line.Start < mixed.Offset + mixed.Length
            && mixed.Offset < ContentSize
            && mixed.Offset + mixed.Length < line.End)
        {
            return new LocalAnchor(mixed.Offset, y - (Origin.Y + line.Top), AnchorKind.Line, AfterComposition: true);
        }

        // A line that starts inside composed text and holds nothing else has no position of its
        // own: the composition is not in the document. It is held from the line the composition
        // starts on, by however far below that line the offset is, so the composed text stays put
        // as it grows.
        if (_preedit is { } preedit && line.Start > preedit.Offset && line.Start < preedit.Offset + preedit.Length)
        {
            var first = Lines[LineIndexAt(preedit.Offset, forward: true)];

            return new LocalAnchor(preedit.Offset, y - (Origin.Y + first.Top), AnchorKind.Line, Unclamped: true);
        }

        // The line the paragraph's own text resumes on after a composition ending at a line break
        // starts at the composition's position too; read back as a position it would name the
        // composition's first line, so it says which side of the composition it is on.
        if (_preedit is { } before && before.Length > 0 && line.Start == before.Offset + before.Length)
        {
            return new LocalAnchor(before.Offset, y - (Origin.Y + line.Top), AnchorKind.Line, AfterComposition: true);
        }

        return new LocalAnchor(
            FromLayout(Math.Clamp(line.Start, 0, LayoutSize)),
            y - (Origin.Y + line.Top),
            AnchorKind.Line);
    }

    /// <inheritdoc/>
    internal override double? AnchorPoint(LocalAnchor anchor)
    {
        if (anchor.Kind != AnchorKind.Line)
        {
            return base.AnchorPoint(anchor);
        }

        if (!IsMeasured)
        {
            return null;
        }

        var lines = Lines;
        var index = anchor.AfterComposition && _preedit is { } composing && anchor.Position == composing.Offset
            ? LayoutLineAt(composing.Offset + composing.Length)
            : LineIndexAt(Math.Clamp(anchor.Position, 0, ContentSize), forward: true);
        var line = lines[index];

        // Held within the line's slot — down to the next line, the last one to the paragraph's
        // bottom, and the first one up to the paragraph's top — so an offset in the spacing around
        // a line is kept as it was, and one a reflow left past the line is pulled back to it.
        var above = index == 0 ? -(Origin.Y + line.Top) : 0;
        var slot = index + 1 < lines.Length
            ? lines[index + 1].Top - line.Top
            : Size.Height - Origin.Y - line.Top;
        var dy = anchor.Unclamped ? anchor.Dy : Math.Clamp(anchor.Dy, Math.Min(0, above), Math.Max(line.Line.Height, slot));

        return Origin.Y + line.Top + dy;
    }

    /// <inheritdoc/>
    internal override double TextBottom =>
        IsMeasured && Lines is { Length: > 0 } lines ? Origin.Y + lines[^1].Top + lines[^1].Line.Height : Size.Height;

    /// <summary>The index of the visual line a position sits on.</summary>
    /// <remarks>
    /// A wrap boundary belongs to two lines, so the caller says which way it is travelling for
    /// the same reason <see cref="Move"/> does.
    /// </remarks>
    /// <param name="localPosition">The position to locate.</param>
    /// <param name="forward">Whether the caret is moving forward.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="localPosition"/> is outside <c>[0, ContentSize]</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public int LineIndexAt(int localPosition, bool forward)
    {
        CheckPosition(localPosition);

        var layoutPosition = ToLayout(localPosition);
        var lines = Lines;

        for (var i = 0; i < lines.Length; i++)
        {
            var end = lines[i].End;

            var owns = forward
                ? layoutPosition < end
                : layoutPosition <= end && layoutPosition > lines[i].Start;

            if (owns)
            {
                return i;
            }
        }

        return forward ? lines.Length - 1 : 0;
    }

    /// <summary>The index of the visual line a layout position starts or sits on.</summary>
    private int LayoutLineAt(int layoutPosition)
    {
        var lines = Lines;

        for (var i = 0; i < lines.Length; i++)
        {
            if (layoutPosition < lines[i].End)
            {
                return i;
            }
        }

        return lines.Length - 1;
    }

    /// <summary>The first position on a visual line.</summary>
    /// <param name="lineIndex">The line.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="lineIndex"/> is outside <c>[0, LineCount)</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public int LineStart(int lineIndex) =>
        FromLayout(Math.Clamp(Line(lineIndex).Start, 0, LayoutSize));

    /// <summary>The last position on a visual line.</summary>
    /// <remarks>
    /// Clamped to <see cref="BlockView.ContentSize"/>, because the last line's text source
    /// length counts the paragraph terminator, which is not a position the caret can occupy.
    /// Trailing whitespace that wrapped is excluded for the same reason a caret at End should
    /// not sit past the visible edge of the line.
    /// </remarks>
    /// <param name="lineIndex">The line.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="lineIndex"/> is outside <c>[0, LineCount)</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public int LineEnd(int lineIndex)
    {
        var placed = Line(lineIndex);
        var line = placed.Line;
        var end = placed.End - line.NewLineLength - line.TrailingWhitespaceLength;

        return Math.Max(LineStart(lineIndex), FromLayout(Math.Clamp(end, 0, LayoutSize)));
    }

    /// <summary>The position on a visual line nearest a horizontal offset.</summary>
    /// <remarks>
    /// This is what carries a caret's goal column across a vertical move. The offset is in this
    /// block's coordinates, so it includes <see cref="Indent"/>.
    /// </remarks>
    /// <param name="lineIndex">The line to land on.</param>
    /// <param name="x">The horizontal offset to land nearest.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="lineIndex"/> is outside <c>[0, LineCount)</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The paragraph has not been measured.</exception>
    public int PositionAtLineX(int lineIndex, double x)
    {
        var placed = Line(lineIndex);
        var hit = placed.Line.GetCharacterHitFromDistance(x - Origin.X);

        return FromLayout(Math.Clamp(placed.Offset + hit.FirstCharacterIndex + hit.TrailingLength, 0, LayoutSize));
    }

    private PlacedLine Line(int lineIndex)
    {
        var lines = Lines;

        if (lineIndex < 0 || lineIndex >= lines.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineIndex), lineIndex, $"The paragraph has {lines.Length} lines.");
        }

        return lines[lineIndex];
    }

    private int Move(int localPosition, bool forward, Func<TextLine, CharacterHit, CharacterHit> step)
    {
        CheckPosition(localPosition);

        var placed = LineAt(localPosition, forward);
        var hit = step(placed.Line, new CharacterHit(ToLayout(localPosition) - placed.Offset));

        return FromLayout(Math.Clamp(placed.Offset + hit.FirstCharacterIndex + hit.TrailingLength, 0, LayoutSize));
    }

    /// <summary>
    /// The line to ask about a move from <paramref name="localPosition"/>.
    /// </summary>
    /// <remarks>
    /// A wrap boundary is one position belonging to two lines, and <see cref="TextLine"/>'s
    /// caret methods refuse to leave the line they are called on. So the line is chosen by the
    /// direction of travel: moving left from a wrap boundary asks the line that <em>ends</em>
    /// there, which is the only one that can answer. Getting this wrong does not throw — it
    /// silently pins the caret at the start of every wrapped line.
    /// </remarks>
    private PlacedLine LineAt(int localPosition, bool forward) =>
        Lines[LineIndexAt(localPosition, forward)];

    /// <summary>
    /// The paragraph-level properties handed to the formatter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base direction is fixed left-to-right, which is a deliberate deferral rather than an
    /// oversight. Bidirectional text <em>within</em> a line is handled by the formatter and was
    /// measured in Increment 0; what is missing is a paragraph whose base direction is
    /// right-to-left, so that alignment resolves to the right edge and neutrals at the ends of
    /// the line settle the other way. That is a document-model question — the model has no
    /// direction attribute on a paragraph and inferring one from content is a decision with
    /// visible consequences — so it waits for the model to carry the answer.
    /// </para>
    /// <para>
    /// <c>indent</c> is zero here on purpose: Avalonia's paragraph indent applies to the first
    /// line only, whereas an indent level insets the whole block. See <see cref="Indent"/>.
    /// </para>
    /// <para>
    /// The line height is resolved against the block's own already-scaled default size rather
    /// than being carried as pixels, so a heading and a body paragraph get the leading each
    /// wants from one number in the style. Zero leaves the spacing to the font.
    /// </para>
    /// <para>
    /// A forced line height is <em>absolute</em> in Avalonia, not a minimum: a line holding a
    /// drawable run taller than it keeps the forced height, and the drawable spills out of the
    /// paragraph and over whatever is drawn below. Measured on 12.1.1 — a 40pt image on a 19.2pt
    /// line reported <c>Height</c> 19.2 and <c>Baseline</c> 40, which cannot describe a real box.
    /// So a paragraph whose tallest embed does not fit gives the leading up and lets the font
    /// decide, which grows only the lines that need it rather than every line in the paragraph.
    /// Text is held to the same rule: the forced height is worked out from the paragraph's own
    /// size, and text set larger than that — a size picked from the toolbar, a pasted heading
    /// with a size of its own — was drawn over the line below it.
    /// </para>
    /// </remarks>
    private TextParagraphProperties ParagraphProperties()
    {
        var scaled = _style.LineHeightScale > 0
            ? _style.LineHeightScale * _styles.Default.FontRenderingEmSize
            : 0;

        return new GenericTextParagraphProperties(
            FlowDirection.LeftToRight,
            Alignment(_paragraph.Align),
            firstLineInParagraph: true,
            alwaysCollapsible: false,
            _styles.Default,
            TextWrapping.Wrap,
            lineHeight: scaled > 0 && scaled >= TallestEmbed() && scaled >= TallestText() ? scaled : 0,
            indent: 0,
            letterSpacing: 0);
    }

    /// <summary>
    /// The height a line needs for the largest text in the paragraph: its font's ascent, descent
    /// and line gap at its size.
    /// </summary>
    /// <remarks>
    /// Each distinct set of marks is resolved once, so the cost is the number of differently
    /// formatted stretches, not the length of the paragraph; text with no marks of its own is
    /// the paragraph's own size, which the forced height was worked out for.
    /// </remarks>
    private double TallestText()
    {
        var tallest = 0.0;
        HashSet<MarkSet>? seen = null;

        foreach (var span in _paragraph.Content.Marks)
        {
            if (!(seen ??= []).Add(span.Value))
            {
                continue;
            }

            var properties = _styles.Resolve(span.Value);

            if (properties.FontRenderingEmSize <= _styles.Default.FontRenderingEmSize)
            {
                continue;
            }

            var metrics = properties.Typeface.GlyphTypeface.Metrics;
            var lineSpacing = (metrics.Descent - metrics.Ascent + metrics.LineGap)
                / (double)metrics.DesignEmHeight;

            tallest = Math.Max(tallest, properties.FontRenderingEmSize * lineSpacing);
        }

        return tallest;
    }

    /// <summary>The height of the tallest embed in the paragraph, or zero if it has none.</summary>
    /// <remarks>
    /// Measured through the same path <see cref="EmbedRun"/> takes, including its fallback for an
    /// embed that does not say how big it is, so the answer cannot disagree with the run that is
    /// actually laid out. The common case — a paragraph with no embeds — allocates nothing.
    /// </remarks>
    private double TallestEmbed()
    {
        var tallest = 0.0;

        foreach (var embed in _paragraph.Content.Embeds)
        {
            tallest = Math.Max(
                tallest, new EmbedRun(embed, _styles.Default, _embeds).Size.Height);
        }

        return tallest;
    }

    private static TextAlignment Alignment(TextAlign align) => align switch
    {
        TextAlign.Left => TextAlignment.Left,
        TextAlign.Center => TextAlignment.Center,
        TextAlign.Right => TextAlignment.Right,
        TextAlign.Justify => TextAlignment.Justify,
        _ => TextAlignment.Start,
    };

    private void CheckPosition(int localPosition)
    {
        if (localPosition < 0 || localPosition > ContentSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(localPosition),
                localPosition,
                $"Position must be within [0, {ContentSize}] for this paragraph.");
        }
    }
    /// <summary>
    /// An <see cref="ITextStyleResolver"/> with a paragraph kind's presentation folded in.
    /// </summary>
    /// <remarks>
    /// A decorator rather than a parameter on the resolver interface, because the kind is fixed
    /// for the whole paragraph: the text source asks per run, and the run has no idea what block
    /// it is in. Wrapping means the source stays ignorant and hosts keep the simpler interface.
    /// </remarks>
    private sealed class KindStyles : ITextStyleResolver
    {
        private readonly Dictionary<MarkSet, TextRunProperties> _cache = [];
        private readonly ITextStyleResolver _inner;
        private readonly ParagraphStyle _style;

        private KindStyles(ITextStyleResolver inner, ParagraphStyle style)
        {
            _inner = inner;
            _style = style;
            Default = Apply(MarkSet.Empty, inner.Default);
        }

        public TextRunProperties Default { get; }

        /// <summary>The resolver to use for a kind, which is the original one where it changes nothing.</summary>
        public static ITextStyleResolver Wrap(ITextStyleResolver inner, ParagraphStyle style) =>
            style.FontScale == 1 && style.Weight is null && style.Family is null
                ? inner
                : new KindStyles(inner, style);

        public TextRunProperties Resolve(MarkSet marks)
        {
            if (_cache.TryGetValue(marks, out var cached))
            {
                return cached;
            }

            var built = Apply(marks, _inner.Resolve(marks));

            _cache[marks] = built;

            return built;
        }

        private TextRunProperties Apply(MarkSet marks, TextRunProperties properties)
        {
            var typeface = properties.Typeface;

            // The marks win where they are explicit. A word deliberately set to a family or to
            // bold keeps that inside a heading or a code block; the kind only supplies what was
            // not asked for.
            var family = marks.FontFamily is null && _style.Family is { } kindFamily
                ? kindFamily
                : typeface.FontFamily;

            var weight = !marks.Has(TextStyle.Bold) && _style.Weight is { } kindWeight
                ? kindWeight
                : typeface.Weight;

            return new GenericTextRunProperties(
                new Typeface(family, typeface.Style, weight, typeface.Stretch),
                fontRenderingEmSize: properties.FontRenderingEmSize * _style.FontScale,
                textDecorations: properties.TextDecorations,
                foregroundBrush: properties.ForegroundBrush,
                backgroundBrush: properties.BackgroundBrush,
                baselineAlignment: properties.BaselineAlignment,
                cultureInfo: properties.CultureInfo);
        }
    }
}