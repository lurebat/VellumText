using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>
/// The public rich text editor control, per architecture 4.6.
/// </summary>
/// <remarks>
/// <para>
/// A thin templated shell over <see cref="RichTextView"/>, which does the work. The split is
/// what lets an application restyle the chrome — the scroll viewer, the border, the padding —
/// without inheriting from or reaching into the part that lays text out.
/// </para>
/// <para>
/// The default template is built in code rather than in XAML. It is a border around a scroll
/// viewer around the view, with no visual states and nothing to theme, so markup would buy
/// nothing here — unlike <see cref="RichTextToolbar"/>, whose default template is a bar of
/// buttons and does live in <c>Themes/RichTextToolbar.axaml</c>. An application that wants
/// different chrome sets <see cref="TemplatedControl.Template"/>, which is the whole point of
/// the type.
/// </para>
/// <para>
/// <b>Scrolling is virtualized.</b> The view implements <c>ILogicalScrollable</c> over a block
/// height index, and the template makes it the <see cref="ScrollViewer"/>'s own content so that
/// the scroll presenter reaches it: only the blocks near the viewport are laid out. Anything
/// inserted between the scroll viewer and the view — a border for a page effect, say — silently
/// puts the whole document back on the ordinary path, because a scroll presenter only inspects
/// its immediate child.
/// </para>
/// <para>
/// <b>The selection toolbar</b> is a second <see cref="RichTextToolbar"/> in the template,
/// overlaid on the view and moved to sit beside whatever is selected. See
/// <see cref="IsSelectionToolbarEnabled"/>.
/// </para>
/// </remarks>
public sealed class RichTextEditor : TemplatedControl
{
    /// <summary>The name of the <see cref="RichTextView"/> in the template.</summary>
    public const string ViewPart = "PART_View";

    /// <summary>The name of the panel the selection toolbar is positioned within.</summary>
    public const string OverlayPart = "PART_Overlay";

    /// <summary>The name of the toolbar that floats beside the selection.</summary>
    public const string SelectionToolbarPart = "PART_SelectionToolbar";

    /// <summary>The gap between the selection and the toolbar floating beside it, in pixels.</summary>
    private const double SelectionToolbarGap = 8;

    /// <summary>Defines the <see cref="State"/> property.</summary>
    public static readonly StyledProperty<EditorState> StateProperty =
        AvaloniaProperty.Register<RichTextEditor, EditorState>(
            nameof(State),
            EditorState.Create(DocumentNode.Empty),
            defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Defines the <see cref="Document"/> property.</summary>
    /// <remarks>
    /// One-way to the source by default, because that is the only direction it has. See
    /// <see cref="RichTextView.DocumentProperty"/>.
    /// </remarks>
    public static readonly DirectProperty<RichTextEditor, DocumentNode> DocumentProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, DocumentNode>(
            nameof(Document),
            o => o.Document,
            defaultBindingMode: global::Avalonia.Data.BindingMode.OneWayToSource);

    /// <summary>Defines the <see cref="Selection"/> property.</summary>
    /// <remarks>One-way to the source for the same reason <see cref="DocumentProperty"/> is.</remarks>
    public static readonly DirectProperty<RichTextEditor, Selection> SelectionProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, Selection>(
            nameof(Selection),
            o => o.Selection,
            defaultBindingMode: global::Avalonia.Data.BindingMode.OneWayToSource);

    /// <summary>Defines the <see cref="IsSelectionToolbarEnabled"/> property.</summary>
    public static readonly StyledProperty<bool> IsSelectionToolbarEnabledProperty =
        AvaloniaProperty.Register<RichTextEditor, bool>(
            nameof(IsSelectionToolbarEnabled), defaultValue: true);

    /// <summary>Defines the <see cref="IsReadOnly"/> property.</summary>
    /// <remarks>
    /// These four are added owners of the view's properties rather than new ones, so the default
    /// and the coercion are defined once. The shell still has to push each into the view, because
    /// an added owner shares the definition and not the value.
    /// </remarks>
    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        RichTextView.IsReadOnlyProperty.AddOwner<RichTextEditor>();

    /// <summary>Defines the <see cref="AcceptsReturn"/> property.</summary>
    public static readonly StyledProperty<bool> AcceptsReturnProperty =
        RichTextView.AcceptsReturnProperty.AddOwner<RichTextEditor>();

    /// <summary>Defines the <see cref="AcceptsTab"/> property.</summary>
    public static readonly StyledProperty<bool> AcceptsTabProperty =
        RichTextView.AcceptsTabProperty.AddOwner<RichTextEditor>();

    /// <summary>Defines the <see cref="UndoLimit"/> property.</summary>
    public static readonly StyledProperty<int> UndoLimitProperty =
        RichTextView.UndoLimitProperty.AddOwner<RichTextEditor>();

    /// <summary>Defines the <see cref="CanUndo"/> property.</summary>
    public static readonly DirectProperty<RichTextEditor, bool> CanUndoProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, bool>(nameof(CanUndo), o => o.CanUndo);

    /// <summary>Defines the <see cref="CanRedo"/> property.</summary>
    public static readonly DirectProperty<RichTextEditor, bool> CanRedoProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, bool>(nameof(CanRedo), o => o.CanRedo);

    /// <summary>Defines the <see cref="MatchCount"/> property.</summary>
    public static readonly DirectProperty<RichTextEditor, int> MatchCountProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, int>(nameof(MatchCount), o => o.MatchCount);

    /// <summary>Defines the <see cref="CurrentMatchNumber"/> property.</summary>
    public static readonly DirectProperty<RichTextEditor, int> CurrentMatchNumberProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, int>(
            nameof(CurrentMatchNumber), o => o.CurrentMatchNumber);

    /// <summary>Defines the <see cref="View"/> property.</summary>
    /// <remarks>
    /// Observable rather than a plain property because the view does not exist until the
    /// template is applied, and anything attaching to the editor — a toolbar, most obviously —
    /// is built before that. Reading it once at construction would read null forever.
    /// </remarks>
    public static readonly DirectProperty<RichTextEditor, RichTextView?> ViewProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, RichTextView?>(nameof(View), o => o.View);

    static RichTextEditor()
    {
        TemplateProperty.OverrideDefaultValue<RichTextEditor>(
            new FuncControlTemplate<RichTextEditor>((editor, scope) =>
            {
                var view = new RichTextView { Name = ViewPart };

                view.RegisterInNameScope(scope);

                var scroller = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = view,
                };

                // Padding belongs to the scroll viewer's content, not to the border around it,
                // so the text scrolls out from under the top margin instead of the margin
                // staying pinned over it while the words slide past underneath.
                scroller.Bind(
                    Decorator.PaddingProperty,
                    editor.GetObservable(PaddingProperty));

                var border = new Border { Child = scroller };

                border.Bind(Border.BackgroundProperty, editor.GetObservable(BackgroundProperty));
                border.Bind(Border.BorderBrushProperty, editor.GetObservable(BorderBrushProperty));
                border.Bind(
                    Border.BorderThicknessProperty, editor.GetObservable(BorderThicknessProperty));
                border.Bind(
                    Border.CornerRadiusProperty, editor.GetObservable(CornerRadiusProperty));

                // The floating toolbar is a sibling of the scroll viewer rather than a popup. A
                // popup would be free of the editor's bounds, which sounds like an advantage and
                // is not: a formatting bar for a selection belongs over the text it formats, and
                // an overlay cannot outlive its editor, drift onto another monitor, or need an
                // overlay layer the host may not have templated.
                var floating = new RichTextToolbar
                {
                    Name = SelectionToolbarPart,
                    Editor = editor,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,

                    // Genuinely invisible, not merely transparent. Opacity does not affect
                    // IsEffectivelyVisible, which is what Avalonia's automation peer reports as
                    // IsOffscreen -- a transparent bar announces nine phantom buttons to a screen
                    // reader. The cost is that a hidden control is never measured, so the code that
                    // shows it measures it by hand before reading DesiredSize.
                    IsVisible = false,
                    IsHitTestVisible = false,

                    // Placed by transform rather than by margin. Measured, and it is not a
                    // preference: Avalonia's DesiredSize includes the margin, so a bar positioned
                    // by margin grows its own desired size, which moves it, which grows it --
                    // "Infinite layout loop detected", every frame. A render transform does not
                    // participate in layout at all, so the position cannot feed back into the
                    // measurement it was computed from.
                    RenderTransform = new TranslateTransform(),
                    RenderTransformOrigin = RelativePoint.TopLeft,
                };

                floating.Classes.Add("VellumText-selection");
                floating.RegisterInNameScope(scope);

                var overlay = new Panel { Name = OverlayPart };

                overlay.Children.Add(border);
                overlay.Children.Add(floating);
                overlay.RegisterInNameScope(scope);

                return overlay;
            }));

        // Horizontal scrolling is off because the view wraps to the width it is given; leaving
        // it automatic lets a wide embed widen the content, which widens the wrap width, which
        // widens the content -- a layout cycle the user sees as a flickering scrollbar.

        // Room for the text to breathe. A document that starts at the frame edge reads as a
        // text box; the default should look like a page without the host having to say so.
        PaddingProperty.OverrideDefaultValue<RichTextEditor>(new Thickness(28, 24));

        // The shell is chrome; the view is what takes the keyboard. Leaving both focusable puts
        // a tab stop in front of the editor that does nothing when you reach it.
        FocusableProperty.OverrideDefaultValue<RichTextEditor>(false);
    }

    private RichTextView? _view;
    private Panel? _overlay;
    private RichTextToolbar? _floating;
    private History? _pendingHistory;

    /// <summary>The document and selection being edited.</summary>
    public EditorState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The document being edited.</summary>
    /// <remarks>
    /// <para>
    /// Read-only and one-way <em>out</em>: bind it with <c>Mode=OneWayToSource</c> — its default —
    /// or subscribe with <c>GetObservable(DocumentProperty)</c>, and a view model hears every edit
    /// without knowing anything about transactions. Load a document by assigning
    /// <see cref="State"/>; <see cref="RichTextView.Document"/> explains why that direction is not
    /// this property's.
    /// </para>
    /// <para>
    /// Read from <see cref="State"/> rather than relayed from the view, so it answers before the
    /// template has been applied. A view model bound in a constructor would otherwise see an empty
    /// document until the control was first laid out.
    /// </para>
    /// </remarks>
    public DocumentNode Document => State.Doc;

    /// <summary>What is selected: a range of text, or a rectangle of table cells.</summary>
    /// <remarks>Read-only and one-way out, exactly as <see cref="Document"/> is.</remarks>
    public Selection Selection => State.Selection;

    /// <summary>Whether a small formatting bar floats beside a non-empty selection.</summary>
    /// <remarks>
    /// <para>
    /// On by default, because the alternative for a bare <c>&lt;RichTextEditor/&gt;</c> is a
    /// surface with no visible way to format anything. An application that supplies its own
    /// toolbar and wants nothing floating over the text turns it off.
    /// </para>
    /// <para>
    /// It appears only once a pointer drag has finished, never during one: a bar that follows the
    /// pointer across the paragraph being selected would land under it and swallow the release.
    /// </para>
    /// <para>
    /// Nothing appears if <c>avares://VellumText.Avalonia/Themes/VellumText.axaml</c> is not among the
    /// application's styles, since the bar has no template then. That is deliberate — an empty
    /// box over the selection would be worse than no bar at all.
    /// </para>
    /// </remarks>
    public bool IsSelectionToolbarEnabled
    {
        get => GetValue(IsSelectionToolbarEnabledProperty);
        set => SetValue(IsSelectionToolbarEnabledProperty, value);
    }

    /// <summary>Whether the document may be changed by the user.</summary>
    /// <remarks>Forwarded to <see cref="RichTextView.IsReadOnly"/>, which documents it.</remarks>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Whether Enter splits the paragraph.</summary>
    /// <remarks>Forwarded to <see cref="RichTextView.AcceptsReturn"/>, which documents it.</remarks>
    public bool AcceptsReturn
    {
        get => GetValue(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>Whether Tab indents, and nests a list item, rather than moving focus.</summary>
    /// <remarks>Forwarded to <see cref="RichTextView.AcceptsTab"/>, which documents it.</remarks>
    public bool AcceptsTab
    {
        get => GetValue(AcceptsTabProperty);
        set => SetValue(AcceptsTabProperty, value);
    }

    /// <summary>The most edits the undo stack will keep, or -1 for no limit.</summary>
    /// <remarks>Forwarded to <see cref="RichTextView.UndoLimit"/>, which documents it.</remarks>
    public int UndoLimit
    {
        get => GetValue(UndoLimitProperty);
        set => SetValue(UndoLimitProperty, value);
    }

    /// <summary>The undo history, or null before one has been set or a template applied.</summary>
    /// <remarks>
    /// <para>
    /// Nullable because there is genuinely no history until there is a view to hold one. Setting
    /// null is a different thing entirely and is refused, exactly as
    /// <see cref="RichTextView.History"/> refuses it — silently ignoring it would turn a
    /// programming error into a control that quietly stops being undoable.
    /// </para>
    /// <para>
    /// A history set before the template has been applied is held and pushed into the view when
    /// it arrives. An application that configures the control in a constructor or from a view
    /// model binding has no way to know when templating happens, so dropping the value there
    /// would silently restore the default grouping policy.
    /// </para>
    /// <para>
    /// Assigning one adopts the limit its policy carries into <see cref="UndoLimit"/>, the same
    /// last-one-written-wins rule the view applies. Without it the shell would keep pushing the
    /// older <see cref="UndoLimit"/> back over the assigned policy at the next unrelated property
    /// change.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><c>value</c> is null.</exception>
    public History? History
    {
        get => _view?.History ?? _pendingHistory;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_view is not null)
            {
                _view.History = value;
            }
            else
            {
                _pendingHistory = value;
            }

            SetCurrentValue(UndoLimitProperty, value.Policy.Limit);
        }
    }

    /// <summary>Whether there is anything to undo.</summary>
    public bool CanUndo => _view?.CanUndo ?? false;

    /// <summary>Whether there is anything to redo.</summary>
    public bool CanRedo => _view?.CanRedo ?? false;

    /// <summary>How many matches the current search has.</summary>
    public int MatchCount => _view?.MatchCount ?? 0;

    /// <summary>Which match the user is on, counting from one, or 0 for none.</summary>
    /// <remarks>
    /// One-based because its only consumer is a "3 of 7" label, and a find bar that reported
    /// "2 of 7" for the third match would be a defect a reader could see.
    /// </remarks>
    public int CurrentMatchNumber => (_view?.CurrentMatch ?? -1) + 1;

    /// <summary>The view doing the work, or null before the template has been applied.</summary>
    /// <remarks>
    /// <para>
    /// Null is the honest answer and not a defensive one: until the template is applied there is
    /// no view, and every editing command lives on it. Use this to <i>ask</i> whether the control
    /// has been templated; use <see cref="EnsureView"/> to get one.
    /// </para>
    /// <para>
    /// The setup surfaces a host reaches before the control is on screen — <see cref="Formats"/>,
    /// <see cref="ImageStore"/>, <see cref="Embeds"/>, the style resolvers, the syntax properties
    /// and the two reporting events — are forwarded from here, so none of them requires a view
    /// that may not exist yet. Those are the ones where a value set too early would be silently
    /// lost rather than loudly refused; the editing commands are called in response to a user
    /// action, by which point the control has long been templated.
    /// </para>
    /// </remarks>
    public RichTextView? View => _view;

    /// <summary>The view doing the work, applying the template first if that has not happened.</summary>
    /// <returns>The view. Never null.</returns>
    /// <exception cref="InvalidOperationException">
    /// The template contains no <see cref="RichTextView"/> named <see cref="ViewPart"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The escape hatch, for the editing commands this shell does not forward. It is a method
    /// rather than a non-null property because it can do work and can fail, and because
    /// <c>Editor.View!</c> — which is what a host writes otherwise — is a null-forgiving operator
    /// on something that genuinely is null for part of the control's life, and so is an exception
    /// waiting for whichever host calls it one layout pass too early.
    /// </para>
    /// <para>
    /// Applying the template early is safe and idempotent: the default template is registered as
    /// the property's default value rather than coming from a theme dictionary, so it resolves
    /// with no application styles loaded at all, and Avalonia applies a template once per
    /// <see cref="TemplatedControl.Template"/> value.
    /// </para>
    /// </remarks>
    public RichTextView EnsureView()
    {
        if (_view is null)
        {
            ApplyTemplate();
        }

        return _view
            ?? throw new InvalidOperationException(
                $"The {nameof(RichTextEditor)} template produced no {nameof(RichTextView)}.");
    }

    /// <summary>The interchange formats copy and paste carry, beyond plain text.</summary>
    /// <remarks>
    /// Forwarded rather than left to <see cref="View"/> because registration happens at setup,
    /// which is exactly when there is no view: a host that writes
    /// <c>Editor.View!.Formats.Add(…)</c> in a constructor throws, and one that writes it in a
    /// property setter fired by a binding throws intermittently.
    /// </remarks>
    public ICollection<IDocumentFormat> Formats => EnsureView().Formats;

    /// <inheritdoc cref="RichTextView.ImageStore"/>
    public IImageStore ImageStore
    {
        get => EnsureView().ImageStore;
        set => EnsureView().ImageStore = value;
    }

    /// <inheritdoc cref="DocumentPresenter.Embeds"/>
    public IEmbedRenderer Embeds
    {
        get => EnsureView().Embeds;
        set => EnsureView().Embeds = value;
    }

    /// <inheritdoc cref="DocumentPresenter.TextStyles"/>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public ITextStyleResolver TextStyles
    {
        get => EnsureView().TextStyles;
        set => EnsureView().TextStyles = value;
    }

    /// <inheritdoc cref="DocumentPresenter.ParagraphStyles"/>
    public IParagraphStyleResolver ParagraphStyles
    {
        get => EnsureView().ParagraphStyles;
        set => EnsureView().ParagraphStyles = value;
    }

    /// <inheritdoc cref="DocumentPresenter.SyntaxHighlighter"/>
    public ISyntaxHighlighter? SyntaxHighlighter
    {
        get => EnsureView().SyntaxHighlighter;
        set => EnsureView().SyntaxHighlighter = value;
    }

    /// <inheritdoc cref="DocumentPresenter.SyntaxTheme"/>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public ISyntaxTheme SyntaxTheme
    {
        get => EnsureView().SyntaxTheme;
        set => EnsureView().SyntaxTheme = value;
    }

    /// <inheritdoc cref="RichTextView.ContentImporting"/>
    /// <remarks>
    /// Forwarded for the reason <see cref="ContentImported"/> is, and it matters more here: a
    /// content policy subscribed to a view that does not exist yet is a policy that never runs,
    /// and the first sign of it is content the host meant to refuse sitting in the document.
    /// </remarks>
    public event EventHandler<RichTextView.ContentImportingEventArgs>? ContentImporting
    {
        add
        {
            var view = EnsureView();

            _contentImporting += value;
            view.ContentImporting += value;
        }

        remove
        {
            _contentImporting -= value;
            EnsureView().ContentImporting -= value;
        }
    }

    /// <inheritdoc cref="RichTextView.ContentImported"/>
    /// <remarks>
    /// Forwarded because a subscription is made at setup. Missing one costs no exception and no
    /// warning — the host simply never hears that a paste dropped half a document.
    /// </remarks>
    public event EventHandler<RichTextView.ContentImportedEventArgs>? ContentImported
    {
        add
        {
            var view = EnsureView();

            _contentImported += value;
            view.ContentImported += value;
        }

        remove
        {
            _contentImported -= value;
            EnsureView().ContentImported -= value;
        }
    }

    /// <inheritdoc cref="RichTextView.ImageRefused"/>
    /// <remarks>
    /// Forwarded for the same reason as <see cref="ContentImported"/>: a missed subscription is
    /// silent, and this one reports a security decision.
    /// </remarks>
    public event EventHandler<RichTextView.ImageRefusedEventArgs>? ImageRefused
    {
        add
        {
            var view = EnsureView();

            _imageRefused += value;
            view.ImageRefused += value;
        }

        remove
        {
            _imageRefused -= value;
            EnsureView().ImageRefused -= value;
        }
    }

    /// <inheritdoc cref="RichTextView.ExportFailed"/>
    /// <remarks>
    /// Forwarded, and carried across a retemplate, for the same reason as <see cref="ImageRefused"/>:
    /// a missed subscription is silent, and this one is how a failing exporter gets noticed.
    /// </remarks>
    public event EventHandler<ExportFailedEventArgs>? ExportFailed
    {
        add
        {
            var view = EnsureView();

            _exportFailed += value;
            view.ExportFailed += value;
        }

        remove
        {
            _exportFailed -= value;
            EnsureView().ExportFailed -= value;
        }
    }

    // The handlers subscribed through this shell, kept here as well as on the view so that a
    // retemplate can move them to the new view. A subscription that lived only on the old view
    // went with it: a content policy stopped vetoing, with no sign that it had.
    private EventHandler<RichTextView.ContentImportingEventArgs>? _contentImporting;
    private EventHandler<RichTextView.ContentImportedEventArgs>? _contentImported;
    private EventHandler<RichTextView.ImageRefusedEventArgs>? _imageRefused;
    private EventHandler<ExportFailedEventArgs>? _exportFailed;


    /// <summary>
    /// The toolbar that floats beside the selection, or null when the template has none.
    /// </summary>
    public RichTextToolbar? SelectionToolbar => _floating;

    /// <summary>
    /// Where the floating toolbar has been put, relative to the editor's top-left corner.
    /// </summary>
    /// <remarks>
    /// A translation rather than a position in the layout: the bar is arranged at the origin and
    /// moved from there, so its <see cref="Visual.Bounds"/> say nothing about where it appears.
    /// Placing it by margin instead was measured and is a trap — Avalonia's <c>DesiredSize</c>
    /// includes the margin, so the bar's own position grows its size, which moves it, which grows
    /// it again: "Infinite layout loop detected", once per frame.
    /// </remarks>
    public Point SelectionToolbarPlacement =>
        _floating?.RenderTransform is TranslateTransform move ? new Point(move.X, move.Y) : default;

    /// <summary>Undoes the most recent edit.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Undo() => _view?.Undo() ?? false;

    /// <summary>Redoes the most recently undone edit.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Redo() => _view?.Redo() ?? false;

    /// <summary>Runs a search, replacing any previous one.</summary>
    /// <param name="query">The text to find. Empty ends the search.</param>
    /// <param name="options">How to compare, or null for <see cref="SearchOptions.Default"/>.</param>
    /// <returns>The number of matches.</returns>
    /// <remarks>
    /// Does not move the selection, so a find bar can search on every keystroke. Use
    /// <see cref="FindNext"/> to step onto a match.
    /// </remarks>
    public int Find(string query, SearchOptions? options = null) =>
        _view?.Find(query, options) ?? 0;

    /// <summary>Ends the search and takes the highlights away.</summary>
    public void ClearFind() => _view?.ClearFind();

    /// <summary>Selects the next match after the caret, wrapping at the end.</summary>
    /// <returns>Whether there was one.</returns>
    public bool FindNext() => _view?.FindNext() ?? false;

    /// <summary>Selects the previous match before the caret, wrapping at the start.</summary>
    /// <returns>Whether there was one.</returns>
    public bool FindPrevious() => _view?.FindPrevious() ?? false;

    /// <summary>Replaces the match the user is on, then moves to the next.</summary>
    /// <param name="replacement">The text to put there.</param>
    /// <returns>Whether anything was replaced.</returns>
    public bool ReplaceCurrent(string replacement) => _view?.ReplaceCurrent(replacement) ?? false;

    /// <summary>Replaces every match, as one undoable step.</summary>
    /// <param name="replacement">The text to put in each one.</param>
    /// <returns>How many were replaced.</returns>
    public int ReplaceAll(string replacement) => _view?.ReplaceAll(replacement) ?? 0;

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnApplyTemplate(e);

        var found = e.NameScope.Find<RichTextView>(ViewPart)
            ?? throw new InvalidOperationException(
                $"A {nameof(RichTextEditor)} template must contain a {nameof(RichTextView)} named {ViewPart}.");

        var old = _view;

        _view = found;

        // Pushed into the view first, because the view's own default would otherwise overwrite
        // whatever the application set before the template was applied.
        _view.State = State;

        PushOptions();

        if (old is not null)
        {
            // Retemplating leaves the old view detached but alive; without this its events would
            // keep arriving and the editor would report the state of a view nobody can see.
            old.PropertyChanged -= OnViewPropertyChanged;
            old.LayoutUpdated -= OnViewLayoutUpdated;

            MoveSetup(old, _view);
        }

        if (_pendingHistory is not null)
        {
            _view.History = _pendingHistory;
            _pendingHistory = null;
        }

        _view.PropertyChanged += OnViewPropertyChanged;

        // Where the selection is on screen depends on the heights of every block above it and on
        // how far the view has been scrolled, neither of which is settled until the pass is over.
        // Scrolling goes through the view's Offset setter, which invalidates measure, so this
        // catches a wheel notch as well as a resize.
        _view.LayoutUpdated += OnViewLayoutUpdated;

        // Optional, unlike the view: a replacement template that wants no floating bar simply
        // leaves it out, and the feature turns itself off rather than refusing to template.
        _overlay = e.NameScope.Find<Panel>(OverlayPart);

        var oldBar = _floating;

        _floating = e.NameScope.Find<RichTextToolbar>(SelectionToolbarPart);

        // The discarded template's bar is still bound to this editor, and so still subscribed to
        // it: it would go on syncing to every state change from outside the visual tree.
        if (oldBar is not null && !ReferenceEquals(oldBar, _floating) && ReferenceEquals(oldBar.Editor, this))
        {
            oldBar.Editor = null;
        }

        RaisePropertyChanged(ViewProperty, old, _view);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == StateProperty)
        {
            // The view first, then the announcement. A handler is entitled to read the view, and
            // one that touched it - select-all on load - used to find the old state there and
            // write it back over the state the host had just set.
            if (_view is not null)
            {
                _view.State = State;
            }

            // Announced here rather than beside the view's own pair, because this shell holds the
            // state itself: the editor answers Document before it has ever been templated, and a
            // relay from the view would say "empty" until the first layout pass.
            RaiseStateParts((EditorState)change.OldValue!, (EditorState)change.NewValue!);
        }
        else if (change.Property == IsSelectionToolbarEnabledProperty)
        {
            UpdateSelectionToolbar();
        }
        else if (change.Property == IsReadOnlyProperty
            || change.Property == AcceptsReturnProperty
            || change.Property == AcceptsTabProperty
            || change.Property == UndoLimitProperty)
        {
            PushOptions();

            if (change.Property == IsReadOnlyProperty)
            {
                // The bar is gated on IsReadOnly, so pushing the option is not enough: a bar
                // already floating over a selection would stay on screen offering edits the
                // control had just stopped accepting.
                UpdateSelectionToolbar();
            }
        }
    }

    /// <summary>Announces the two halves of the state a view model binds to on their own.</summary>
    /// <remarks>
    /// The same reference-for-the-document, value-for-the-selection rule
    /// <see cref="RichTextView"/> uses, and for the same reason: comparing documents by value
    /// would walk the whole tree on every keystroke to answer a question identity already answers.
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

    /// <inheritdoc cref="RichTextView.DocumentChanged"/>
    /// <remarks>
    /// Raised from the shell's own state rather than forwarded from the view, so a view model may
    /// subscribe in a constructor — before the template exists — and still hear the first edit.
    /// </remarks>
    public event EventHandler? DocumentChanged;

    /// <inheritdoc cref="RichTextView.SelectionChanged"/>
    /// <remarks>Raised from the shell's own state, for the reason <see cref="DocumentChanged"/> is.</remarks>
    public event EventHandler? SelectionChanged;

    /// <summary>Carries a host's setup from a discarded view to the one replacing it.</summary>
    /// <remarks>
    /// <para>
    /// Everything the shell forwards: registered formats, the image store, the renderer and style
    /// resolvers, the syntax properties, the handlers subscribed through the shell — and the undo
    /// history, because a retemplate is chrome changing, not the document's past being forgotten.
    /// </para>
    /// <para>
    /// The values are read from the old view rather than kept in the shell, so that a host which
    /// configured the view directly, through <see cref="EnsureView"/>, keeps that configuration too.
    /// The handlers cannot be read back from a view, which is why the shell keeps those itself.
    /// </para>
    /// </remarks>
    private void MoveSetup(RichTextView old, RichTextView view)
    {
        // An import still running in the old view belongs to a view that is about to stop being
        // this editor's; landing it there would edit a document nobody is looking at.
        old.CancelImport();

        view.Formats.Clear();

        foreach (var format in old.Formats)
        {
            view.Formats.Add(format);
        }

        view.ImageStore = old.ImageStore;
        view.Embeds = old.Embeds;
        view.TextStyles = old.TextStyles;
        view.ParagraphStyles = old.ParagraphStyles;
        view.SyntaxHighlighter = old.SyntaxHighlighter;
        view.SyntaxTheme = old.SyntaxTheme;
        view.History = old.History;

        // One at a time: removing the combined delegate only works while the view's list holds
        // these handlers contiguously, which a host subscribing on the view directly would break.
        foreach (var handler in Handlers(_contentImporting))
        {
            old.ContentImporting -= handler;
            view.ContentImporting += handler;
        }

        foreach (var handler in Handlers(_contentImported))
        {
            old.ContentImported -= handler;
            view.ContentImported += handler;
        }

        foreach (var handler in Handlers(_imageRefused))
        {
            old.ImageRefused -= handler;
            view.ImageRefused += handler;
        }

        foreach (var handler in Handlers(_exportFailed))
        {
            old.ExportFailed -= handler;
            view.ExportFailed += handler;
        }

        static IEnumerable<T> Handlers<T>(T? handlers)
            where T : Delegate =>
            handlers?.GetInvocationList().Cast<T>() ?? [];
    }

    /// <summary>Copies the four behaviour properties into the templated view.</summary>
    /// <remarks>
    /// Assignment rather than binding. A binding would have to be torn down when the control is
    /// retemplated, and a subscription left behind on a discarded view is the kind of leak that
    /// only shows up in an application that retemplates in a loop.
    /// </remarks>
    private void PushOptions()
    {
        if (_view is not { } view)
        {
            return;
        }

        view.IsReadOnly = IsReadOnly;
        view.AcceptsReturn = AcceptsReturn;
        view.AcceptsTab = AcceptsTab;
        view.UndoLimit = UndoLimit;
    }

    /// <summary>Puts the floating toolbar beside the selection, or takes it away.</summary>
    /// <remarks>
    /// Position is clamped into <see cref="OverlayPart"/>, so the bar can never be drawn outside
    /// the editor. It prefers to sit above the selection and drops below it when there is no room,
    /// which is the one case where covering the text being formatted is unavoidable.
    /// </remarks>
    private void UpdateSelectionToolbar()
    {
        if (_floating is not { } bar || _overlay is not { } overlay)
        {
            return;
        }

        if (!IsSelectionToolbarEnabled
            || IsReadOnly
            || bar.Template is null
            || _view is not { } view
            || view.IsDragging
            || view.SelectionBounds() is not { } local
            || view.TranslatePoint(local.TopLeft, overlay) is not { } topLeft
            || view.TranslatePoint(local.BottomRight, overlay) is not { } bottomRight)
        {
            bar.IsVisible = false;
            bar.IsHitTestVisible = false;
            return;
        }

        var area = new Rect(topLeft, bottomRight);
        var room = overlay.Bounds.Size;

        // Measured by hand, because a hidden control has no DesiredSize and the frame it becomes
        // visible on is the frame we need to place it. Layout will measure it again with the same
        // constraint immediately after; that pass agrees and so changes nothing.
        bar.IsVisible = true;
        bar.Measure(room);
        var size = bar.DesiredSize;

        var above = area.Top - size.Height - SelectionToolbarGap;
        var y = above >= 0 ? above : area.Bottom + SelectionToolbarGap;

        Place(
            bar,
            Math.Clamp(area.Center.X - (size.Width / 2), 0, Math.Max(0, room.Width - size.Width)),
            Math.Clamp(y, 0, Math.Max(0, room.Height - size.Height)));

        bar.IsHitTestVisible = true;
    }

    /// <summary>Reads back where <see cref="UpdateSelectionToolbar"/> put the bar.</summary>
    /// <remarks>
    /// The transform, not the bounds: the bar is arranged at the overlay's origin and moved from
    /// there, so its <see cref="Visual.Bounds"/> say nothing about where it appears.
    /// </remarks>
    private static void Place(Control bar, double x, double y)
    {
        if (bar.RenderTransform is not TranslateTransform move)
        {
            return;
        }

        move.X = x;
        move.Y = y;
    }

    private void OnViewLayoutUpdated(object? sender, EventArgs e) => UpdateSelectionToolbar();

    /// <remarks>
    /// The two properties are kept in step by hand rather than by a two-way binding, so that
    /// there is one place to look when they disagree. Neither setter raises for an identical
    /// reference, which is what stops the pair from echoing.
    /// </remarks>
    private void OnViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_view is null)
        {
            return;
        }

        if (e.Property == RichTextView.StateProperty)
        {
            State = _view.State;
            UpdateSelectionToolbar();
        }
        else if (e.Property == RichTextView.IsDraggingProperty)
        {
            UpdateSelectionToolbar();
        }
        else if (e.Property == RichTextView.CanUndoProperty)
        {
            RaisePropertyChanged(CanUndoProperty, (bool)e.OldValue!, (bool)e.NewValue!);
        }
        else if (e.Property == RichTextView.CanRedoProperty)
        {
            RaisePropertyChanged(CanRedoProperty, (bool)e.OldValue!, (bool)e.NewValue!);
        }
        else if (e.Property == RichTextView.MatchCountProperty)
        {
            RaisePropertyChanged(MatchCountProperty, (int)e.OldValue!, (int)e.NewValue!);
        }
        else if (e.Property == RichTextView.CurrentMatchProperty)
        {
            RaisePropertyChanged(
                CurrentMatchNumberProperty, (int)e.OldValue! + 1, (int)e.NewValue! + 1);
        }
    }
}
