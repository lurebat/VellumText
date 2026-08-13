using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;

namespace Vellum.Avalonia;

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
/// viewer around the view, with no visual states, so a markup file would buy little and would
/// drag a resource-loading step into a library that otherwise has none. An application that
/// wants different chrome sets <see cref="TemplatedControl.Template"/>, which is the whole
/// point of the type.
/// </para>
/// <para>
/// <b>Scrolling is virtualized.</b> The view implements <c>ILogicalScrollable</c> over a block
/// height index, and the template makes it the <see cref="ScrollViewer"/>'s own content so that
/// the scroll presenter reaches it: only the blocks near the viewport are laid out. Anything
/// inserted between the scroll viewer and the view — a border for a page effect, say — silently
/// puts the whole document back on the ordinary path, because a scroll presenter only inspects
/// its immediate child.
/// </para>
/// </remarks>
public class RichTextEditor : TemplatedControl
{
    /// <summary>The name of the <see cref="RichTextView"/> in the template.</summary>
    public const string ViewPart = "PART_View";

    /// <summary>Defines the <see cref="State"/> property.</summary>
    public static readonly StyledProperty<EditorState> StateProperty =
        AvaloniaProperty.Register<RichTextEditor, EditorState>(
            nameof(State),
            EditorState.Create(DocumentNode.Empty),
            defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Defines the <see cref="CanUndo"/> property.</summary>
    public static readonly DirectProperty<RichTextEditor, bool> CanUndoProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, bool>(nameof(CanUndo), o => o.CanUndo);

    /// <summary>Defines the <see cref="CanRedo"/> property.</summary>
    public static readonly DirectProperty<RichTextEditor, bool> CanRedoProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, bool>(nameof(CanRedo), o => o.CanRedo);

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

                return border;
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
    private History? _pendingHistory;

    /// <summary>The document and selection being edited.</summary>
    public EditorState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
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
        }
    }

    /// <summary>Whether there is anything to undo.</summary>
    public bool CanUndo => _view?.CanUndo ?? false;

    /// <summary>Whether there is anything to redo.</summary>
    public bool CanRedo => _view?.CanRedo ?? false;

    /// <summary>The view doing the work, or null before the template has been applied.</summary>
    public RichTextView? View => _view;

    /// <summary>Undoes the most recent edit.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Undo() => _view?.Undo() ?? false;

    /// <summary>Redoes the most recently undone edit.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Redo() => _view?.Redo() ?? false;

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

        if (_pendingHistory is not null)
        {
            _view.History = _pendingHistory;
            _pendingHistory = null;
        }

        if (old is not null)
        {
            // Retemplating leaves the old view detached but alive; without this its events would
            // keep arriving and the editor would report the state of a view nobody can see.
            old.PropertyChanged -= OnViewPropertyChanged;
        }

        _view.PropertyChanged += OnViewPropertyChanged;

        RaisePropertyChanged(ViewProperty, old, _view);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == StateProperty && _view is not null)
        {
            _view.State = State;
        }
    }

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
        }
        else if (e.Property == RichTextView.CanUndoProperty)
        {
            RaisePropertyChanged(CanUndoProperty, (bool)e.OldValue!, (bool)e.NewValue!);
        }
        else if (e.Property == RichTextView.CanRedoProperty)
        {
            RaisePropertyChanged(CanRedoProperty, (bool)e.OldValue!, (bool)e.NewValue!);
        }
    }
}
