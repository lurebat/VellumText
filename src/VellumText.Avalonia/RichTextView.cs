using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace VellumText.Avalonia;

/// <summary>
/// The control that presents a document and draws the caret and the selection, per
/// architecture 4.6.
/// </summary>
/// <remarks>
/// <para>
/// The division of labour: blocks own layout and answer for geometry in their own coordinates,
/// and this control owns the block list, the caret, the selection highlight and everything
/// that has to be true across blocks. The block list and the reconciliation that keeps it
/// cheap live in the Blocks part of this class.
/// </para>
/// <para>
/// The caret and the highlight are drawn here rather than by the block precisely because a
/// selection spans blocks and the caret is a single concern with one blink timer. A block that
/// drew its own caret would need to know whether it held the focused end of the selection,
/// which is knowledge it has no business having.
/// </para>
/// </remarks>
public sealed partial class RichTextView : DocumentPresenter
{
    /// <summary>Defines the <see cref="State"/> property.</summary>
    public static readonly DirectProperty<RichTextView, EditorState> StateProperty =
        AvaloniaProperty.RegisterDirect<RichTextView, EditorState>(
            nameof(State), o => o.State, (o, v) => o.State = v);

    /// <summary>Defines the <see cref="Document"/> property.</summary>
    /// <remarks>
    /// One-way to the source by default, because that is the only direction this property has:
    /// see <see cref="Document"/>.
    /// </remarks>
    public static readonly DirectProperty<RichTextView, DocumentNode> DocumentProperty =
        AvaloniaProperty.RegisterDirect<RichTextView, DocumentNode>(
            nameof(Document),
            o => o.Document,
            defaultBindingMode: global::Avalonia.Data.BindingMode.OneWayToSource);

    /// <summary>Defines the <see cref="Selection"/> property.</summary>
    /// <remarks>One-way to the source for the same reason <see cref="DocumentProperty"/> is.</remarks>
    public static readonly DirectProperty<RichTextView, Selection> SelectionProperty =
        AvaloniaProperty.RegisterDirect<RichTextView, Selection>(
            nameof(Selection),
            o => o.Selection,
            defaultBindingMode: global::Avalonia.Data.BindingMode.OneWayToSource);

    /// <summary>Defines the <see cref="SelectionBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<RichTextView, IBrush?>(
            nameof(SelectionBrush), new SolidColorBrush(Color.FromArgb(80, 0, 120, 215)));

    /// <summary>Defines the <see cref="CaretBrush"/> property.</summary>
    /// <remarks>
    /// Null means "whatever the text is painted in", which is the default because a fixed colour
    /// is wrong under half the themes there are. Measured: with a hard-coded black default the
    /// caret was invisible in the dark-themed demo, which reads as the control ignoring the
    /// keyboard entirely. Set it to paint the caret in something other than the text colour.
    /// </remarks>
    public static readonly StyledProperty<IBrush?> CaretBrushProperty =
        AvaloniaProperty.Register<RichTextView, IBrush?>(nameof(CaretBrush));

    /// <summary>The interval at which the caret changes state.</summary>
    public static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _blink;
    private EditorState _state = EditorState.Create(DocumentNode.Empty);

    /// <summary>The state whose document the block views were last reconciled with.</summary>
    private EditorState? _reconciledState;
    private bool _caretVisible;
    private int _caretBlock;

    /// <summary>Creates the view.</summary>
    public RichTextView()
    {
        Focusable = true;

        // Set per pointer move from IsOverContent, not once here: an I-beam pinned to the whole
        // control claims the empty space below the document is text.
        Cursor = ArrowCursor;

        ContextFlyout = BuildContextFlyout();

        // A drop is a transfer from another application, exactly like a paste, so it is handled
        // here rather than left to the host to wire up.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _inputMethod = new RichTextInputMethodClient(this);

        // How a control offers itself to the platform input method: the request bubbles from
        // the focused element, and answering it is what makes composition arrive here at all.
        AddHandler(
            global::Avalonia.Input.InputElement.TextInputMethodClientRequestedEvent,
            (_, e) => e.Client = _inputMethod);

        _blink = new DispatcherTimer { Interval = BlinkInterval };
        _blink.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };
    }

    /// <summary>The document and selection being presented.</summary>
    public EditorState State
    {
        get => _state;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_state, value))
            {
                return;
            }

            SetState(value, derived: false);
        }
    }

    /// <summary>Installs a new state, and decides what becomes of the history.</summary>
    /// <param name="value">The state to install.</param>
    /// <param name="derived">
    /// Whether the state came from this control editing the one before it, as opposed to a host
    /// loading a different document.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Loading a different document discards the history</b>, because the history describes
    /// edits to a document that is no longer here. Undoing it against the new one at best restores
    /// text from somewhere else and at worst throws, since the positions it recorded need not
    /// exist — measured, before this existed: typing into a four-character clip, loading a
    /// one-character clip and pressing Ctrl+Z threw <c>Range [3, 4) is not inside the document</c>.
    /// A clipboard history or a file list hits that by arrowing down and pressing a key.
    /// </para>
    /// <para>
    /// Only a change of <em>document</em> counts. Assigning a state that carries the same document
    /// with a different selection is how selection is set from outside — <see cref="SelectAll"/>
    /// does it — and must not cost the user their undo stack.
    /// </para>
    /// <para>
    /// The limit survives, since it is the host's configuration rather than the document's
    /// history. A host that genuinely wants the old stack can put it back through
    /// <see cref="History"/> afterwards.
    /// </para>
    /// </remarks>
    private void SetState(EditorState value, bool derived)
    {
        // The view reconciles before the change is announced, not after. A consumer handling
        // StateProperty asks the view questions -- which block the caret is in, what the caret
        // block's style is -- and those are answered from state that OnStateChanged rebuilds.
        // Raising first hands every listener the previous document's answers.
        var previous = _state;
        _state = value;

        if (!derived && !ReferenceEquals(previous.Doc, value.Doc))
        {
            History = History.With(_history.Policy);

            // The goal column is an x position measured in the previous document's layout; kept,
            // it sent the next Up or Down to a column from the clip the host just replaced. Only a
            // different document clears it: caret moves arrive through the same setter and a
            // vertical run depends on the column surviving them.
            _goalX = null;

            // An import still running belongs to the document that is no longer here. Its result
            // would be refused by StillAt anyway; cancelling stops waiting for it.
            CancelImport();
        }

        OnStateChanged();
        RaisePropertyChanged(StateProperty, previous, value);
        RaiseStateParts(previous, value);
    }

    /// <summary>Announces the two halves of the state a view model binds to on their own.</summary>
    /// <remarks>
    /// <para>
    /// Raised from here rather than from a setter because <see cref="State"/> is the only thing
    /// that installs either of them: a document and a selection cannot be assigned separately, and
    /// anything that announced them separately from somewhere else would be a second place to
    /// forget.
    /// </para>
    /// <para>
    /// Compared by reference for the document and by value for the selection, and deliberately not
    /// the other way round. A selection is four small integers. A document is a tree, and
    /// <see cref="Node.Equals(Node)"/> walks it — so a value comparison would run a deep compare of
    /// the whole document on every keystroke, to answer a question identity already answers: the
    /// model is immutable, so a different object is a different document.
    /// </para>
    /// </remarks>
    private void RaiseStateParts(EditorState previous, EditorState value)
    {
        if (!ReferenceEquals(previous.Doc, value.Doc))
        {
            RaisePropertyChanged(DocumentProperty, previous.Doc, value.Doc);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        if (previous.Selection != value.Selection)
        {
            RaisePropertyChanged(SelectionProperty, previous.Selection, value.Selection);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised after <see cref="Document"/> has become a different document.</summary>
    /// <remarks>
    /// <para>
    /// The plain-event half of the same notification <see cref="DocumentProperty"/> gives, and it
    /// exists because the property alone is not a usable story for an application that is not
    /// written in XAML. Measured, against Avalonia 12.1.1: a <c>OneWayToSource</c> binding delivers
    /// <em>changes</em> and never the value that was already there, so a view model bound before
    /// the first edit holds null until the user types; and <c>GetObservable</c> does prime, but
    /// only through <c>AnonymousObserver&lt;T&gt;</c> or a reference to Rx, which is a great deal
    /// of ceremony to hear that a document changed.
    /// </para>
    /// <para>
    /// Read <see cref="Document"/> in the handler. There is no payload deliberately: a document is
    /// the whole tree, and an argument carrying the previous one would keep it alive for as long as
    /// the handler ran.
    /// </para>
    /// </remarks>
    public event EventHandler? DocumentChanged;

    /// <summary>Raised after <see cref="Selection"/> has moved or changed shape.</summary>
    /// <remarks>
    /// Not raised for an edit that leaves the selection where it was, and not raised twice for an
    /// edit that moves it: the two are announced independently because a view model usually wants
    /// only one of them. A word count wants <see cref="DocumentChanged"/>; a "3 characters
    /// selected" label wants this.
    /// </remarks>
    public event EventHandler? SelectionChanged;

    /// <summary>The document being presented.</summary>
    /// <remarks>
    /// <para>
    /// Read-only, and one-way <em>out</em> of the control: bind it with
    /// <c>Mode=OneWayToSource</c> — which is its default — or subscribe with
    /// <c>GetObservable(DocumentProperty)</c>, and a view model hears every edit the user makes.
    /// </para>
    /// <para>
    /// <b>Assigning a document is deliberately not this property's job.</b> Load one through
    /// <see cref="State"/>, which is where the rule about what becomes of the undo history lives —
    /// see <see cref="SetState"/>. Reconciling a document assigned from outside against edits
    /// already in flight, and against a history recorded over a document that is no longer there,
    /// is a design with real hazards and nobody has asked for it.
    /// </para>
    /// </remarks>
    public DocumentNode Document => _state.Doc;

    /// <summary>What is selected: a range of text, or a rectangle of table cells.</summary>
    /// <remarks>
    /// Read-only and one-way out, exactly as <see cref="Document"/> is. Move it with
    /// <see cref="SelectAll"/>, <c>MoveTo</c>, or by assigning a <see cref="State"/> carrying the
    /// same document and a different selection — which costs no undo history.
    /// </remarks>
    public Selection Selection => _state.Selection;

    /// <summary>The fill behind selected text.</summary>
    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>The caret's colour.</summary>
    public IBrush? CaretBrush
    {
        get => GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    /// <inheritdoc/>
    protected internal override DocumentNode PresentedDocument => _state.Doc;

    /// <inheritdoc/>
    protected internal override bool SupportsTextInput => !IsReadOnly;

    /// <summary>Replaces the whole document with plain text, on behalf of an automation client.</summary>
    /// <param name="text">The replacement text.</param>
    /// <remarks>
    /// <para>
    /// One transaction replacing the whole document with the text's paragraphs, so one undo takes
    /// it back. Empty text leaves one empty paragraph, since a document needs somewhere for the
    /// caret.
    /// </para>
    /// <para>
    /// Not select-all and paste, which it used to be: select-all spans the first to the last
    /// text position, so on a document that is one table it is a rectangle of cells — which paste
    /// refuses, and the call did nothing and said nothing — and a block image at either edge was
    /// outside it and survived a "replace everything".
    /// </para>
    /// </remarks>
    internal void SetTextFromAutomation(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Checked here and not only in the edit. SelectAll runs first, so gating the paste alone
        // left an automation client unable to change a locked document but still able to move the
        // user's selection to the whole of it - the same shape as a read-only Cut writing the
        // clipboard before its delete declines.
        //
        // The peer refuses before it gets here, so this is unreachable through the only caller
        // in this assembly. It stays because a second internal caller would not be refused by
        // the peer. Tested directly rather than through the peer, because a guard nothing can
        // turn red is a guard nobody can rely on.
        if (IsReadOnly)
        {
            return;
        }

        var paragraphs = text.Length == 0 ? [ParagraphNode.Empty] : TextParagraphs(text);
        var size = paragraphs.Sum(p => p.NodeSize);
        var transaction = _state.Transaction().As(TransactionKind.Structure);

        transaction
            .Replace(0, _state.Doc.ContentSize, Slice.OfBlocks([.. paragraphs]))
            .SetSelection(TextSelection.Cursor(size - 1))
            .SetStoredMarks(null);

        if (transaction.Failures.IsEmpty)
        {
            Apply(transaction);
        }
    }

    /// <summary>The index of the block the caret is in.</summary>
    public int CaretBlockIndex => _caretBlock;

    /// <summary>
    /// The paragraph the caret is in, where it starts and where it is drawn, or null.
    /// </summary>
    /// <remarks>
    /// Recomputed rather than cached because the caret moves far more often than the layout
    /// changes and a stale copy would edit the wrong paragraph. It is a search through one
    /// block, not the document.
    /// </remarks>
    private TextAt? CaretText
    {
        get
        {
            if (_views.Count == 0)
            {
                return null;
            }

            // Laid out first, outside a frame, so that asking a table for the paragraph does not
            // lay out a far row behind the presenter's back; painting reads it ready-made.
            if (!LayoutGuard.IsPainting)
            {
                PreparePosition(_state.Selection.Head);
            }

            return _views[_caretBlock].ParagraphAt(
                Math.Clamp(
                    _state.Selection.Head - _slots[_caretBlock].Start,
                    0,
                    _views[_caretBlock].ContentSize));
        }
    }

    /// <summary>
    /// The view of the paragraph the caret is in, or null before the control has been measured.
    /// </summary>
    /// <remarks>
    /// Null when the caret is somewhere no text lives — a rule, an image — since
    /// <see cref="Selection.Near"/> puts a text caret wherever one can go.
    /// </remarks>
    /// <remarks>
    /// Not the same thing as the block at <see cref="CaretBlockIndex"/>: a table is one block
    /// holding many paragraphs, and every editing command wants the paragraph.
    /// </remarks>
    internal ParagraphView? Block => CaretText?.View;

    /// <summary>
    /// The document position at which the caret's paragraph starts.
    /// </summary>
    /// <remarks>
    /// The one place a paragraph-local position becomes a document position. Paired with
    /// <see cref="Block"/> — the two are always read together and must come from the same
    /// paragraph, which is why both go through <see cref="CaretText"/>.
    /// </remarks>
    public int BlockStart =>
        _slots.IsEmpty ? 0 : _slots[_caretBlock].Start + (CaretText?.Start ?? 0);

    /// <summary>Where the caret's paragraph is drawn, in this control's coordinates.</summary>
    private Point BlockOffset
    {
        get
        {
            var origin = BlockOrigin(_caretBlock);
            var inner = CaretText?.Origin ?? default;

            return new Point(origin.X + inner.X, origin.Y + inner.Y);
        }
    }

    /// <summary>Whether the caret is currently painted, for tests and for the blink timer.</summary>
    internal bool IsCaretVisible => _caretVisible;

    /// <inheritdoc/>
    /// <remarks>
    /// Order matters and was settled by looking at the result. Found-but-not-current matches go
    /// under the selection, because a match the user has also selected should read as selected.
    /// The current match goes over it, because it is always selected and would otherwise be a
    /// muddy mixture of the two. Text is above all of them, the image handles, the column
    /// resize guide and the drop caret above the text they overlap, and the caret above
    /// everything.
    /// </remarks>
    private protected override void RenderContent(DrawingContext context)
    {
        RenderFindHighlights(context);
        RenderSelection(context);
        RenderCurrentFindHighlight(context);
        RenderBlocks(context);
        RenderImageHandles(context);
        RenderColumnGuide(context);
        RenderDropCaret(context);
        RenderTouchHandles(context);
        RenderCaret(context);
    }
    /// <summary>Restarts the blink so the caret is solid immediately after it moves.</summary>
    /// <remarks>
    /// Without this a caret moved just before the timer ticks vanishes on arrival, which reads
    /// as a dropped keystroke.
    /// </remarks>
    internal void ResetCaretBlink()
    {
        _caretVisible = IsFocused;
        _blink.Stop();

        if (IsFocused)
        {
            _blink.Start();
        }

        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnGotFocus(global::Avalonia.Input.FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        ResetCaretBlink();
    }

    /// <inheritdoc/>
    protected override void OnLostFocus(global::Avalonia.Input.FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        // Touch chrome is an affordance for adjusting a selection the user is looking at. A view
        // that is not focused has no such selection on offer.
        HideTouchHandles();
        ResetCaretBlink();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // A dispatcher timer holds the control alive through its Tick handler, so a view that
        // leaves the tree without stopping it keeps repainting nothing forever.
        _blink.Stop();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // A derived resolver is only as current as the properties it was derived from, so a
        // font change has to invalidate it. An explicitly set one is the caller's to manage;
        // the base class owns that decision, and this only adds the brushes it knows nothing of.
        if (change.Property == SelectionBrushProperty || change.Property == CaretBrushProperty)
        {
            InvalidateVisual();
        }

        if (change.Property == UndoLimitProperty)
        {
            OnUndoLimitChanged(change.GetNewValue<int>());
        }

        // Read-only changes what Undo and Redo will do, so a bound button has to hear about it.
        // The caret stays: a read-only TextBox shows one too, and it is what tells a reader
        // where a selection they are about to extend actually starts.
        if (change.Property == IsReadOnlyProperty)
        {
            RaiseHistoryFlags((
                !change.GetOldValue<bool>() && _history.CanUndo,
                !change.GetOldValue<bool>() && _history.CanRedo));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The paragraph being composed in, so that <see cref="OnReconciled"/> can tell whether the
    /// selection has left it while a composition was in progress.
    /// </remarks>
    private protected override BlockView? Anchor => _composing;

    /// <inheritdoc/>
    private protected override void OnReconciled(BlockView? before)
    {
        _reconciledState = _state;
        _caretBlock = Math.Min(BlockIndexOf(_state.Selection.Head), Math.Max(0, _views.Count - 1));

        // A composition belongs to the paragraph the commit will replace the selection in. If
        // the selection has left it, the composition has to go: leaving it behind would draw
        // composed text in a paragraph the input method is no longer talking about, and it would
        // never be committed there. The head's paragraph is not that one when the selection
        // crosses paragraphs, so it is the composing paragraph itself that is checked.
        if (before is ParagraphView composing
            && (composing.Preedit is null || !ReferenceEquals(composing, CompositionText?.View)))
        {
            ClearComposition();
        }
    }

    /// <inheritdoc/>
    /// <remarks>The composition goes with the views: none of the new ones is drawing it.</remarks>
    private protected override void OnLayoutDropped()
    {
        _caretBlock = 0;
        _composing = null;
        _reconciledState = null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The caret and the composition, both ends of the selection for the touch handles, the
    /// selected picture for its handles, and the drop target of a move being dragged.
    /// </remarks>
    private protected override void PreparePaintDependencies()
    {
        var selection = _state.Selection;

        PreparePosition(selection.Head);
        PreparePosition(selection.From);
        PreparePosition(selection.To);

        if (SelectedImage is { } image)
        {
            PreparePosition(image);
        }

        if (_moving)
        {
            PreparePosition(_moveTarget);
        }
    }

    /// <summary>Lays out what painting will ask about, then asks for a frame.</summary>
    /// <remarks>
    /// For a change something painted depends on that comes without a layout pass — a drop
    /// target moving under the pointer, handles appearing — so the frame finds it laid out.
    /// </remarks>
    private void InvalidatePaint()
    {
        PreparePaintDependencies();
        InvalidateVisual();
    }

    /// <summary>
    /// Whether every position a piece of painting asks about was laid out before the frame.
    /// </summary>
    /// <remarks>
    /// Painting lays nothing out (<see cref="LayoutGuard"/>). A position nothing prepared is a
    /// missed <see cref="InvalidatePaint"/>: a debug build throws, so the test that paints it says
    /// where; a release build leaves that piece out of this frame and asks for another once the
    /// position is laid out, rather than laying it out mid-frame or drawing it in the wrong place.
    /// </remarks>
    /// <param name="positions">The positions.</param>
    private bool PaintReady(params ReadOnlySpan<int> positions)
    {
        foreach (var position in positions)
        {
            if (!IsPositionPrepared(position))
            {
#if DEBUG
                throw new InvalidOperationException(
                    $"Painting asked about position {position}, which nothing laid out before the frame.");
#else
                Dispatcher.UIThread.Post(InvalidatePaint);

                return false;
#endif
            }
        }

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Known when the state now presented was applied directly from the one last laid out, which
    /// is every edit, undo and redo, since the view lays out after each.
    /// </remarks>
    private protected override Mapping? MappingFrom(DocumentNode basis) =>
        _reconciledState is { } laidOut && ReferenceEquals(laidOut.Doc, basis)
            ? StateLineage.MappingFrom(laidOut, _state)
            : null;

    /// <inheritdoc/>
    /// <summary>
    /// Scrolls until the caret is inside the viewport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wanted whenever the selection moves, which is the only time a caret can leave the screen
    /// on its own. Typing at the bottom of a screenful and watching the line you are writing
    /// disappear is the defect this exists to prevent.
    /// </para>
    /// <para>
    /// Called from <c>Realize</c> rather than straight from the state change, because where the
    /// caret <em>is</em> depends on the heights of every block above it, and those are not
    /// settled until the window has been laid out.
    /// </para>
    /// <para>
    /// The caret's block may be nowhere near the realized window — Ctrl+End is exactly that — so
    /// it is laid out on demand before it is asked where its caret is.
    /// </para>
    /// </remarks>
    /// <returns>Whether the view moved.</returns>
    /// <inheritdoc/>
    /// <remarks>The caret at the last place in the document one can sit: where Ctrl+End puts it.</remarks>
    private protected override bool IsAnchorAtDocumentEnd =>
        _state.Selection.Head == Selection.AtEnd(_state.Doc).Head;

    private protected override bool ScrollAnchorIntoView()
    {
        if (!IsVirtualizing || _views.Count == 0)
        {
            return false;
        }

        EnsureMeasured(_caretBlock);

        // A block with no caret in it — a rule, an image — has no caret rectangle to aim at,
        // so the whole block is brought into view instead.
        var caret = Block is null
            ? new Rect(0, BlockOrigin(_caretBlock).Y, 0, _views[_caretBlock].Size.Height)
            : CaretRect();

        // Measured against the constraint, not against the arranged viewport: a document can
        // arrive with its caret already at the end — a restored one does — and the first measure
        // runs before any arrange, so the arranged viewport is still nothing. The constraint is
        // known by then, and is the same height.
        var delta = 0d;

        if (caret.Bottom > ViewportHeight)
        {
            delta = caret.Bottom - ViewportHeight;
        }

        // Checked after the downward correction and allowed to override it: a caret taller than
        // the viewport cannot be shown whole, and showing its top is more use than its bottom.
        if (caret.Top - delta < 0)
        {
            delta = caret.Top;
        }

        // No dead zone around the delta. A caret already on screen produces exactly zero, and
        // the comparison below is what reports whether anything moved; a sub-pixel threshold on
        // top of that was tried and could not be shown to prevent anything — swept over 200
        // scroll positions, no repeated layout ever moved the view by a fraction of a pixel.
        var before = _offset;

        ApplyOffset(new Vector(_offset.X, _offset.Y + delta), relayout: false);

        return _offset != before;
    }

    private void OnStateChanged()
    {
        // Consumed by Realize, not acted on here: where the caret is depends on the heights of
        // every block above it, and those are not settled until the window has been laid out.
        _pendingAnchor = true;

        // Before the layout work, so a research that changes nothing does not also cost a frame.
        Research();

        if (_views.Count > 0)
        {
            // Realised eagerly rather than waiting for the layout pass: see Realize.
            Realize();
        }

        InvalidateMeasure();
        ResetCaretBlink();
        NotifyInputMethod();
    }

    /// <summary>
    /// The rectangle the selection occupies on screen, in view coordinates, or null when nothing
    /// is selected or no part of the selection has been realized.
    /// </summary>
    /// <remarks>
    /// The union of the rectangles the selection paints, and so bounded by the realized window
    /// rather than by the document: a selection running off the bottom of the viewport reports
    /// only the part that is on screen. That is what a caller putting something beside the
    /// selection actually wants, since there is nowhere off screen to put it.
    /// </remarks>
    public Rect? SelectionBounds()
    {
        Rect? bounds = null;

        EnumerateSelection(rect => bounds = bounds is { } seen ? seen.Union(rect) : rect);

        return bounds;
    }

    private void RenderSelection(DrawingContext context)
    {
        if (SelectionBrush is not { } brush)
        {
            return;
        }

        EnumerateSelection(rect => context.FillRectangle(brush, rect));
    }

    /// <summary>Walks the rectangles the current selection covers, in view coordinates.</summary>
    /// <remarks>
    /// Shared by painting and by <see cref="SelectionBounds"/> so that the two can never disagree
    /// about where the selection is — a toolbar floating beside a highlight that is somewhere else
    /// is worse than no toolbar.
    /// </remarks>
    private void EnumerateSelection(Action<Rect> onRect)
    {
        if (_state.Selection.IsEmpty)
        {
            return;
        }

        if (_state.Selection is CellSelection rectangle)
        {
            EnumerateCells(rectangle, onRect);

            return;
        }

        EnumerateRange(_state.Selection.From, _state.Selection.To, onRect);
    }

    /// <summary>Walks the boxes of the cells a rectangular selection covers.</summary>
    /// <remarks>
    /// A rectangle is not a range: selecting the left column of a two-column table skips the
    /// right one in every row, so walking <c>From</c> to <c>To</c> would highlight cells the
    /// reader did not select. The cells are asked for by position instead.
    /// </remarks>
    private void EnumerateCells(CellSelection selection, Action<Rect> onRect)
    {
        var cells = selection.Cells(_state.Doc);

        if (cells.IsEmpty)
        {
            return;
        }

        var wanted = cells.ToHashSet();

        for (var i = _first; i <= _last; i++)
        {
            if (_views[i] is not TableBlockView table)
            {
                continue;
            }

            var origin = BlockOrigin(i);

            foreach (var rect in table.GetCellRects(wanted, _slots[i].Start, RealizedWindowOf(origin)))
            {
                onRect(rect.Translate(origin));
            }
        }
    }

    /// <summary>Walks the rectangles a document range covers, in view coordinates.</summary>
    /// <remarks>
    /// The implementation moved to <see cref="DocumentPresenter.EnumerateRange"/> when the viewer
    /// gained a selection, since both controls highlight a range with the same geometry.
    /// </remarks>
    private void RenderCaret(DrawingContext context)
    {
        if (!_caretVisible || _views.Count == 0 || !PaintReady(_state.Selection.Head, _state.Selection.From))
        {
            return;
        }

        // A non-empty selection hides the caret, except while composing: then the caret belongs
        // to the input method and sits inside text the selection knows nothing about.
        if (Preedit is null && !_state.Selection.IsEmpty)
        {
            return;
        }

        // Falls back to the colour the text itself is painted in, so the caret follows the
        // theme rather than a colour chosen when the control was written. See CaretBrushProperty.
        if ((CaretBrush ?? TextStyles.Resolve(MarkSet.Empty).ForegroundBrush) is not { } brush)
        {
            return;
        }

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

        context.FillRectangle(brush, SnapCaret(CaretRect(), scale));
    }

    /// <summary>
    /// Snaps a caret rectangle to the device pixel grid and gives it a width of exactly one
    /// device pixel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Architecture 4.6 says to follow <c>TextPresenter</c> with
    /// <c>PixelRect.FromRect(rect, scale).ToRect(scale)</c>. That does not do what is wanted
    /// here, and it was measured: <c>PixelRect.FromRect</c> is a <em>covering</em> conversion,
    /// flooring the left edge and ceiling the right, so a one-wide rectangle at a fractional x
    /// comes back two pixels wide — precisely the blurred double-width caret the snapping was
    /// meant to prevent. <c>FromRect(new Rect(10.7, 4.2, 1, 18.6), 1)</c> returns
    /// <c>10, 4, 2, 19</c>.
    /// </para>
    /// <para>
    /// So the origin is rounded to the grid and the width is set to one device pixel outright.
    /// The height is snapped by rounding both edges, which keeps the caret the same height as
    /// the line rather than letting rounding shave it.
    /// </para>
    /// </remarks>
    /// <param name="caret">The caret rectangle in device-independent pixels.</param>
    /// <param name="scale">The render scaling of the surface being drawn on.</param>
    internal static Rect SnapCaret(Rect caret, double scale)
    {
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "Render scaling must be a positive, finite number.");
        }

        var x = Math.Round(caret.X * scale) / scale;
        var top = Math.Round(caret.Y * scale) / scale;
        var bottom = Math.Round((caret.Y + caret.Height) * scale) / scale;

        return new Rect(x, top, CaretWidth / scale, bottom - top);
    }
}
