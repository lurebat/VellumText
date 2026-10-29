using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace VellumText.Avalonia;

/// <summary>
/// A formatting toolbar for a <see cref="RichTextEditor"/>: what the selection is currently
/// formatted as, and the commands that change it.
/// </summary>
/// <remarks>
/// <para>
/// The control holds no buttons. It exposes the state a toolbar needs to show — which styles the
/// selection has, which heading it is, which way it is aligned — and the commands that change
/// them, and leaves the arrangement of those into controls entirely to the template. That is
/// what makes it retemplatable: an application that wants a different toolbar writes different
/// markup rather than a different toolbar.
/// </para>
/// <para>
/// The default arrangement lives in <c>Themes/RichTextToolbar.axaml</c> and is reached by adding
/// <c>avares://VellumText.Avalonia/Themes/VellumText.axaml</c> to the application's styles. Without it
/// the control is present and working but draws nothing, which is the ordinary bargain of a
/// templated control rather than a fault.
/// </para>
/// <para>
/// Every state property is driven from the document rather than from what was last clicked, so
/// that moving the caret into bold text lights the bold button, and undo puts the whole bar back.
/// A property is only true when <em>every</em> character of the selection agrees; a half-bold
/// selection reports not bold rather than lying about it.
/// </para>
/// <para>
/// <b>Commands never throw, and are enabled whenever the editor accepts edits.</b> A formatting
/// command that cannot apply — no editor attached, no template yet, a selection holding nothing
/// it can change — does nothing. Disabling half the bar every time the caret lands somewhere
/// awkward reads as broken, and the two commands where "nothing to do" is worth showing, undo and
/// redo, say so through <see cref="CanUndo"/> and <see cref="CanRedo"/> instead. A read-only
/// editor is different: every command is refused, so every command reports itself disabled until
/// <see cref="RichTextEditor.IsReadOnly"/> is turned off again.
/// </para>
/// </remarks>
public class RichTextToolbar : TemplatedControl
{
    /// <summary>The colours the default template's two colour buttons offer.</summary>
    /// <remarks>
    /// A palette rather than a colour wheel: two rows of eight is the whole decision for almost
    /// every document, and a picker that opens on a hue slider makes the common case the slow one.
    /// They are strings because markup cannot construct an <see cref="Rgba"/>, and because a
    /// replacement template is then free to offer its own list without referencing this one.
    /// </remarks>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#000000", "#434343", "#787878", "#B7B7B7", "#D9D9D9", "#EFEFEF", "#FFFFFF", "#980000",
        "#C82020", "#E06810", "#F0D020", "#189040", "#109098", "#2060D8", "#182870", "#8030B0",
    ];

    /// <summary>The font sizes, in points, the default template's size box offers.</summary>
    /// <remarks>
    /// A list to pick from, not a limit: the box stays editable, so an application that needs
    /// nine and a half can still type it. These are only the sizes worth one click.
    /// </remarks>
    public static IReadOnlyList<double> FontSizes { get; } =
        [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 72];

    /// <summary>The font families installed on this machine, in alphabetical order.</summary>
    /// <remarks>
    /// <para>
    /// Enumerated once and cached, because asking the platform is not free and the answer does
    /// not change while the application runs.
    /// </para>
    /// <para>
    /// Strings rather than <see cref="FontFamily"/> so that what the editable box commits is what
    /// the list shows, and so a template can bind the same list to something else entirely. A
    /// headless or otherwise font-less platform yields an empty list rather than throwing: a
    /// toolbar with no font list is usable, a toolbar that throws while templating is not.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> FontFamilies { get; } = InstalledFonts();

    private static IReadOnlyList<string> InstalledFonts()
    {
        try
        {
            return [.. FontManager.Current.SystemFonts
                .Select(family => family.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Defines the <see cref="Editor"/> property.</summary>
    public static readonly StyledProperty<RichTextEditor?> EditorProperty =
        AvaloniaProperty.Register<RichTextToolbar, RichTextEditor?>(nameof(Editor));

    /// <summary>Defines the <see cref="IsBold"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, bool> IsBoldProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, bool>(nameof(IsBold), o => o.IsBold);

    /// <summary>Defines the <see cref="IsItalic"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, bool> IsItalicProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, bool>(nameof(IsItalic), o => o.IsItalic);

    /// <summary>Defines the <see cref="IsUnderline"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, bool> IsUnderlineProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, bool>(nameof(IsUnderline), o => o.IsUnderline);

    /// <summary>Defines the <see cref="IsStrikethrough"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, bool> IsStrikethroughProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, bool>(
            nameof(IsStrikethrough), o => o.IsStrikethrough);

    /// <summary>Defines the <see cref="IsSuperscript"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, bool> IsSuperscriptProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, bool>(
            nameof(IsSuperscript), o => o.IsSuperscript);

    /// <summary>Defines the <see cref="IsSubscript"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, bool> IsSubscriptProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, bool>(
            nameof(IsSubscript), o => o.IsSubscript);

    /// <summary>Defines the <see cref="IsLink"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, bool> IsLinkProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, bool>(nameof(IsLink), o => o.IsLink);

    /// <summary>Defines the <see cref="ParagraphKind"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, ParagraphKind?> ParagraphKindProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, ParagraphKind?>(
            nameof(ParagraphKind), o => o.ParagraphKind);

    /// <summary>Defines the <see cref="Align"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, TextAlign?> AlignProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, TextAlign?>(nameof(Align), o => o.Align);

    /// <summary>Defines the <see cref="ListKind"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, ListKind?> ListKindProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, ListKind?>(nameof(ListKind), o => o.ListKind);

    /// <summary>Defines the <see cref="SelectionFontFamily"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, string?> SelectionFontFamilyProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, string?>(
            nameof(SelectionFontFamily), o => o.SelectionFontFamily);

    /// <summary>Defines the <see cref="SelectionFontSize"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, double?> SelectionFontSizeProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, double?>(
            nameof(SelectionFontSize), o => o.SelectionFontSize);

    /// <summary>Defines the <see cref="SelectionForeground"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, Rgba?> SelectionForegroundProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, Rgba?>(
            nameof(SelectionForeground), o => o.SelectionForeground);

    /// <summary>Defines the <see cref="SelectionHighlight"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, Rgba?> SelectionHighlightProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, Rgba?>(
            nameof(SelectionHighlight), o => o.SelectionHighlight);

    /// <summary>Defines the <see cref="CanUndo"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, bool> CanUndoProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, bool>(nameof(CanUndo), o => o.CanUndo);

    /// <summary>Defines the <see cref="CanRedo"/> property.</summary>
    public static readonly DirectProperty<RichTextToolbar, bool> CanRedoProperty =
        AvaloniaProperty.RegisterDirect<RichTextToolbar, bool>(nameof(CanRedo), o => o.CanRedo);

    private RichTextEditor? _attached;
    private RichTextView? _view;
    private ComboBox? _fontBox;
    private ComboBox? _sizeBox;
    private readonly List<ToolbarCommand> _commands = [];

    private bool _isBold;
    private bool _isItalic;
    private bool _isUnderline;
    private bool _isStrikethrough;
    private bool _isSuperscript;
    private bool _isSubscript;
    private bool _isLink;
    private ParagraphKind? _kind;
    private TextAlign? _align;
    private ListKind? _list;
    private string? _family;
    private double? _size;
    private Rgba? _foreground;
    private Rgba? _highlight;
    private bool _canUndo;
    private bool _canRedo;

    /// <summary>Creates a toolbar with no editor attached.</summary>
    public RichTextToolbar()
    {
        ToggleStyleCommand = Command<TextStyle>(style => _view?.ToggleStyle(style));
        SetParagraphKindCommand = Command<ParagraphKind>(kind => _view?.SetParagraphKind(kind));
        ToggleParagraphKindCommand = Command<ParagraphKind>(kind => _view?.ToggleParagraphKind(kind));
        SetAlignCommand = Command<TextAlign>(align => _view?.SetAlign(align));
        ToggleListCommand = Command<ListKind>(kind => _view?.ToggleList(kind));
        SetForegroundCommand = Command<object?>(colour => _view?.SetForeground(Colour(colour)));
        SetHighlightCommand = Command<object?>(colour => _view?.SetHighlight(Colour(colour)));
        SetFontFamilyCommand = Command<string?>(family => _view?.SetFontFamily(family));
        SetFontSizeCommand = Command<object?>(size => _view?.SetFontSize(Points(size) * PixelsPerPoint));
        IndentCommand = Command(() => _view?.Indent());
        OutdentCommand = Command(() => _view?.Outdent());
        ClearFormattingCommand = Command(() => _view?.ClearFormatting());
        UndoCommand = Command(() => _view?.Undo());
        RedoCommand = Command(() => _view?.Redo());

        // Toggling rather than setting, because the button that makes a link is the button that
        // removes one -- a toolbar with a permanently lit "link" button and no way back is the
        // shape this avoids.
        SetLinkCommand = Command<object?>(target => _view?.SetLink(Link(target)));

        // The event rather than an OnApplyTemplate override: the hook is an implementation
        // detail, and an override would be public API surface on an unsealed control.
        TemplateApplied += (_, e) => HookBoxes(e.NameScope);
    }

    /// <summary>The editor this toolbar formats.</summary>
    /// <remarks>
    /// Null is allowed and means the toolbar is inert rather than broken, which is what a bar
    /// built in markup before its editor has been bound genuinely is.
    /// </remarks>
    public RichTextEditor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>Whether every character of the selection is bold.</summary>
    public bool IsBold => _isBold;

    /// <summary>Whether every character of the selection is italic.</summary>
    public bool IsItalic => _isItalic;

    /// <summary>Whether every character of the selection is underlined.</summary>
    public bool IsUnderline => _isUnderline;

    /// <summary>Whether every character of the selection is struck through.</summary>
    public bool IsStrikethrough => _isStrikethrough;

    /// <summary>Whether every character of the selection is superscript.</summary>
    public bool IsSuperscript => _isSuperscript;

    /// <summary>Whether every character of the selection is subscript.</summary>
    public bool IsSubscript => _isSubscript;

    /// <summary>Whether the whole selection is one link.</summary>
    public bool IsLink => _isLink;

    /// <summary>The kind every selected paragraph shares, or null if they differ.</summary>
    public ParagraphKind? ParagraphKind => _kind;

    /// <summary>The alignment every selected paragraph shares, or null if they differ.</summary>
    public TextAlign? Align => _align;

    /// <summary>The kind of list the caret is in, or null if it is not in one.</summary>
    public ListKind? ListKind => _list;

    /// <summary>The font family the whole selection shares, or null for the document default.</summary>
    public string? SelectionFontFamily => _family;

    /// <summary>
    /// The font size the whole selection shares, in points, or null for the document default.
    /// </summary>
    /// <remarks>
    /// Points, like the size box and <see cref="SetFontSizeCommand"/>, and like every word
    /// processor: a 20-point heading pasted from Word reads 20 here. The model stores
    /// device-independent pixels, 96 to the inch against 72 points, so the heading is 26⅔ there.
    /// </remarks>
    public double? SelectionFontSize => _size;

    /// <summary>Device-independent pixels per typographic point: 96 per inch against 72.</summary>
    private const double PixelsPerPoint = 96.0 / 72.0;

    /// <summary>The colour the whole selection shares, or null for the document default.</summary>
    public Rgba? SelectionForeground => _foreground;

    /// <summary>The highlight the whole selection shares, or null for none.</summary>
    public Rgba? SelectionHighlight => _highlight;

    /// <summary>Whether there is anything to undo.</summary>
    public bool CanUndo => _canUndo;

    /// <summary>Whether there is anything to redo.</summary>
    public bool CanRedo => _canRedo;

    /// <summary>Turns a <see cref="TextStyle"/> on or off across the selection.</summary>
    public ICommand ToggleStyleCommand { get; }

    /// <summary>Sets the <see cref="global::VellumText.ParagraphKind"/> of every selected paragraph.</summary>
    public ICommand SetParagraphKindCommand { get; }

    /// <summary>
    /// Turns a <see cref="global::VellumText.ParagraphKind"/> on across the selection, or back off to
    /// <see cref="ParagraphKind.Body"/> when it is already on.
    /// </summary>
    /// <remarks>
    /// This rather than <see cref="SetParagraphKindCommand"/> is what a <c>ToggleButton</c> wants:
    /// its lit state has to be reversible by pressing it again, which a command that only ever
    /// sets cannot do.
    /// </remarks>
    public ICommand ToggleParagraphKindCommand { get; }

    /// <summary>Sets the <see cref="TextAlign"/> of every selected paragraph.</summary>
    public ICommand SetAlignCommand { get; }

    /// <summary>Turns the selection into a list of the given <see cref="global::VellumText.ListKind"/>, or out of one.</summary>
    public ICommand ToggleListCommand { get; }

    /// <summary>Sets the selection's colour; a null parameter clears it.</summary>
    public ICommand SetForegroundCommand { get; }

    /// <summary>Sets the selection's highlight; a null parameter clears it.</summary>
    public ICommand SetHighlightCommand { get; }

    /// <summary>Sets the selection's font family; a null or blank parameter clears it.</summary>
    public ICommand SetFontFamilyCommand { get; }

    /// <summary>Sets the selection's font size in points; a null or unparseable parameter clears it.</summary>
    /// <remarks>
    /// Converted to the model's device-independent pixels, so picking 12 gives 12-point text — 16
    /// pixels — as it does in a word processor.
    /// </remarks>
    public ICommand SetFontSizeCommand { get; }

    /// <summary>Indents every selected paragraph one level.</summary>
    public ICommand IndentCommand { get; }

    /// <summary>Outdents every selected paragraph one level.</summary>
    public ICommand OutdentCommand { get; }

    /// <summary>Strips character formatting from the selection.</summary>
    public ICommand ClearFormattingCommand { get; }

    /// <summary>Makes the selection a link to the parameter, or removes the link it already has.</summary>
    public ICommand SetLinkCommand { get; }

    /// <summary>Undoes the most recent edit.</summary>
    public ICommand UndoCommand { get; }

    /// <summary>Redoes the most recently undone edit.</summary>
    public ICommand RedoCommand { get; }

    /// <summary>Watches the template's font and size boxes for a pick from their lists.</summary>
    private void HookBoxes(INameScope scope)
    {
        if (_fontBox is not null)
        {
            _fontBox.SelectionChanged -= OnFontPicked;
        }

        if (_sizeBox is not null)
        {
            _sizeBox.SelectionChanged -= OnSizePicked;
        }

        _fontBox = scope.Find<ComboBox>("PART_FontBox");
        _sizeBox = scope.Find<ComboBox>("PART_SizeBox");

        if (_fontBox is not null)
        {
            _fontBox.SelectionChanged += OnFontPicked;
        }

        if (_sizeBox is not null)
        {
            _sizeBox.SelectionChanged += OnSizePicked;
        }
    }

    private void OnFontPicked(object? sender, SelectionChangedEventArgs e) => Picked(_fontBox, SetFontFamilyCommand);

    private void OnSizePicked(object? sender, SelectionChangedEventArgs e) => Picked(_sizeBox, SetFontSizeCommand);

    /// <summary>Commits an item the user picked from a box's open list.</summary>
    /// <remarks>
    /// Only while the list is open. The box's text follows the selection, and an editable box may
    /// select the matching item to go with it; reading that as a pick would re-apply the caret's
    /// own size to whatever is selected every time the caret moved. Typing still commits on Enter.
    /// </remarks>
    private static void Picked(ComboBox? box, ICommand command)
    {
        if (box is { IsDropDownOpen: true, SelectedItem: { } item } && command.CanExecute(item))
        {
            command.Execute(item);
        }
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == EditorProperty)
        {
            Attach(change.GetNewValue<RichTextEditor?>());
        }
    }

    /// <summary>Reads a size from whatever a combo box or a button parameter happened to hold.</summary>
    /// <remarks>
    /// Markup supplies sizes as strings and view models as numbers, and a combo box that has just
    /// been cleared supplies null. All three mean the same thing to the command, so all three are
    /// accepted rather than making the caller convert. Anything unparseable clears the size, which
    /// is the same thing an empty box means.
    /// </remarks>
    private static double? Points(object? size) => size switch
    {
        double value => value,
        int value => value,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
        _ => null,
    };

    /// <summary>Reads a colour from whatever a swatch button's parameter happened to hold.</summary>
    /// <remarks>
    /// Markup cannot construct an <see cref="Rgba"/>, so a palette written in XAML has no way to
    /// say what colour a swatch is except as a string. Null means no colour, which is not black:
    /// it removes the mark and lets the text take the colour it inherits.
    /// </remarks>
    private static Rgba? Colour(object? colour) => colour switch
    {
        Rgba value => value,
        global::Avalonia.Media.Color value => value.ToVellumText(),
        string text when global::Avalonia.Media.Color.TryParse(text, out var value) => value.ToVellumText(),
        _ => null,
    };

    private LinkMark? Link(object? target)
    {
        if (_view?.SelectionMarks.Link is not null)
        {
            return null;
        }

        return target switch
        {
            LinkMark link => link,
            string href when !string.IsNullOrWhiteSpace(href) => new LinkMark(href),
            _ => null,
        };
    }

    private ICommand Command(Func<bool?> run) => Track(new Action(run, IsEditable));

    private ICommand Command<T>(Func<T, bool?> run) => Track(new Action<T>(run, IsEditable));

    private ToolbarCommand Track(ToolbarCommand command)
    {
        _commands.Add(command);

        return command;
    }

    /// <summary>Whether the bound editor accepts edits. True with no editor, as before.</summary>
    private bool IsEditable() => _attached?.IsReadOnly != true;

    /// <summary>Tells every command's subscribers that whether it can run may have changed.</summary>
    private void RaiseCanExecuteChanged()
    {
        foreach (var command in _commands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private void Attach(RichTextEditor? editor)
    {
        if (_attached is not null)
        {
            _attached.PropertyChanged -= OnEditorPropertyChanged;
        }

        _attached = editor;

        if (_attached is not null)
        {
            _attached.PropertyChanged += OnEditorPropertyChanged;
        }

        _view = _attached?.View;
        Sync();
        RaiseCanExecuteChanged();
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == RichTextEditor.IsReadOnlyProperty)
        {
            RaiseCanExecuteChanged();
            return;
        }

        if (e.Property == RichTextEditor.ViewProperty)
        {
            // The view arrives when the editor is templated, which is after the toolbar was
            // built and bound. Without this the bar would be attached to an editor whose view
            // it never noticed and every command would silently do nothing.
            //
            // Read from the attachment rather than from the event, so that an editor this bar
            // has since been unbound from cannot hand it a view by retemplating itself.
            _view = _attached?.View;
        }
        else if (e.Property != RichTextEditor.StateProperty
            && e.Property != RichTextEditor.CanUndoProperty
            && e.Property != RichTextEditor.CanRedoProperty)
        {
            return;
        }

        Sync();
    }

    /// <summary>Reads the whole bar's state out of the document.</summary>
    private void Sync()
    {
        var marks = _view?.SelectionMarks ?? MarkSet.Empty;

        Set(IsBoldProperty, ref _isBold, marks.Has(TextStyle.Bold));
        Set(IsItalicProperty, ref _isItalic, marks.Has(TextStyle.Italic));
        Set(IsUnderlineProperty, ref _isUnderline, marks.Has(TextStyle.Underline));
        Set(IsStrikethroughProperty, ref _isStrikethrough, marks.Has(TextStyle.Strikethrough));
        Set(IsSuperscriptProperty, ref _isSuperscript, marks.Has(TextStyle.Super));
        Set(IsSubscriptProperty, ref _isSubscript, marks.Has(TextStyle.Sub));
        Set(IsLinkProperty, ref _isLink, marks.Link is not null);

        Set(SelectionFontFamilyProperty, ref _family, marks.FontFamily);
        Set(SelectionFontSizeProperty, ref _size, marks.FontSize is { } pixels ? Math.Round(pixels / PixelsPerPoint, 2) : null);
        Set(SelectionForegroundProperty, ref _foreground, marks.Foreground);
        Set(SelectionHighlightProperty, ref _highlight, marks.Highlight);

        Set(ParagraphKindProperty, ref _kind, _view?.ParagraphKindAt);
        Set(AlignProperty, ref _align, _view?.AlignAt);
        Set(ListKindProperty, ref _list, _view?.ListKindAt);

        Set(CanUndoProperty, ref _canUndo, _view?.CanUndo ?? false);
        Set(CanRedoProperty, ref _canRedo, _view?.CanRedo ?? false);
    }

    private void Set<T>(DirectPropertyBase<T> property, ref T field, T value) =>
        SetAndRaise(property, ref field, value);

    /// <summary>A command that is enabled exactly while the bound editor accepts edits.</summary>
    /// <remarks>
    /// The formatting commands return whether they changed anything, and the toolbar deliberately
    /// discards it: a button that reported failure by doing nothing visible is the same button
    /// either way, and the state properties already say what the document looks like afterwards.
    /// </remarks>
    private abstract class ToolbarCommand(Func<bool> enabled) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => enabled();

        public void Execute(object? parameter)
        {
            if (enabled())
            {
                Run(parameter);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        protected abstract void Run(object? parameter);
    }

    /// <summary>A command with no parameter.</summary>
    private sealed class Action(Func<bool?> run, Func<bool> enabled) : ToolbarCommand(enabled)
    {
        protected override void Run(object? parameter) => run();
    }

    /// <summary>A command taking one typed parameter, tolerant of markup handing it the wrong thing.</summary>
    private sealed class Action<T>(Func<T, bool?> run, Func<bool> enabled) : ToolbarCommand(enabled)
    {
        protected override void Run(object? parameter)
        {
            // A parameter of the wrong type is a markup mistake, and throwing from a click
            // handler tears down the application. Nothing happening is the recoverable failure.
            if (parameter is T value)
            {
                run(value);
            }
            else if (parameter is null && default(T) is null)
            {
                run(default!);
            }
        }
    }
}
