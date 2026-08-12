using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace Vellum.Avalonia;

/// <summary>
/// Selecting everything, and moving text through the system clipboard.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="DataFormat.Text"/> is read and written here. The richer formats architecture
/// section 4.9 describes — a lossless native slice, HTML and RTF — need exporters that do not exist
/// yet, and reading them needs the interop packages, which this assembly deliberately does not
/// reference. A paste that arrives as HTML or RTF still carries a plain-text flavour, so this
/// degrades to unformatted text rather than to nothing.
/// </para>
/// <para>
/// The clipboard is asynchronous on every platform Avalonia supports, so these are tasks. The
/// keyboard and menu paths deliberately do not await them: a paste that has to wait on another
/// process must not freeze the caret.
/// </para>
/// </remarks>
public partial class RichTextView
{
    /// <summary>Selects the whole document.</summary>
    /// <returns><see langword="true"/> if the selection changed.</returns>
    public bool SelectAll()
    {
        var from = NearestTextPosition(0, forward: true);
        var to = NearestTextPosition(_state.Doc.ContentSize, forward: false);

        if (_state.Selection is TextSelection current && current.From == from && current.To == to)
        {
            return false;
        }

        _goalX = null;
        State = EditorState.Create(_state.Doc, TextSelection.Create(_state.Doc, from, to));
        ResetCaretBlink();

        return true;
    }

    /// <summary>The plain text covered by the selection, empty when the selection is a caret.</summary>
    public string SelectedText()
    {
        var selection = _state.Selection;

        if (selection.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var any = false;

        Append(builder, Slice.Cut(_state.Doc, selection.From, selection.To).Content, ref any);

        return builder.ToString();
    }

    /// <summary>Puts the selection on the clipboard.</summary>
    /// <returns><see langword="true"/> if anything was written.</returns>
    public async Task<bool> CopyAsync()
    {
        var text = SelectedText();

        if (text.Length == 0)
        {
            return false;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return false;
        }

        await clipboard.SetTextAsync(text).ConfigureAwait(true);

        return true;
    }

    /// <summary>Puts the selection on the clipboard and removes it from the document.</summary>
    /// <returns><see langword="true"/> if anything was cut.</returns>
    public async Task<bool> CutAsync()
    {
        // The range is settled before the await, and the delete refuses if the selection moved
        // while the clipboard was busy. Awaiting first and deleting "the selection" afterwards
        // cuts whatever the user happened to select in the meantime.
        var before = _state.Selection;

        if (before.IsEmpty || !await CopyAsync().ConfigureAwait(true))
        {
            return false;
        }

        return DeleteCutRange(before);
    }

    /// <summary>
    /// Removes the range a cut was started on, refusing if the selection moved meanwhile.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CutAsync"/> because it is the only part of a cut that can be
    /// tested: the headless clipboard completes synchronously, so no test can drive the await
    /// this exists to defend.
    /// </remarks>
    internal bool DeleteCutRange(Selection before)
    {
        var after = _state.Selection;

        if (after.From != before.From || after.To != before.To)
        {
            return false;
        }

        return DeleteSelection();
    }

    /// <summary>Replaces the selection with the clipboard's text.</summary>
    /// <returns><see langword="true"/> if the document changed.</returns>
    public async Task<bool> PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return false;
        }

        string? text;

        try
        {
            using var data = await clipboard.TryGetDataAsync().ConfigureAwait(true);

            text = data is null ? null : await data.TryGetTextAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // A clipboard owned by another process can fail for reasons none of which are this
            // control's business. Losing a paste is bad; taking the application down is worse.
            return false;
        }

        if (text is not { Length: > 0 })
        {
            return false;
        }

        return PasteText(text);
    }

    /// <summary>Replaces the selection with <paramref name="text"/>, honouring line breaks.</summary>
    /// <param name="text">The text to insert.</param>
    /// <returns><see langword="true"/> if the document changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public bool PasteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return false;
        }

        // Normalized first, so that a Windows clipboard's CRLF does not paste two breaks and a
        // classic-Mac CR does not paste none.
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var selection = _state.Selection;
        var at = selection.From;

        // One transaction, so that one undo takes the paste back. Splitting and inserting line by
        // line reads more simply and is wrong: it leaves the user pressing Ctrl+Z once per line.
        // Open on both ends is what makes the first and last pasted paragraphs merge into the
        // paragraph the caret was sitting in, rather than pushing it apart.
        var content = lines.Select(line => (Node)new ParagraphNode(InlineContent.FromText(line))).ToArray();
        var slice = new Slice(content, 1, 1);
        var caret = at + slice.Size;

        var transaction = _state.Transaction().As(TransactionKind.Paste);

        transaction
            .Replace(selection.From, selection.To, slice)
            .SetSelection(TextSelection.Cursor(caret))
            .SetStoredMarks(null);

        return Apply(transaction);
    }
    /// <summary>Removes the selection, leaving a caret where it was.</summary>
    /// <returns><see langword="true"/> if the document changed.</returns>
    public bool DeleteSelection()
    {
        var selection = _state.Selection;

        if (selection.IsEmpty)
        {
            return false;
        }

        var transaction = _state.Transaction().As(TransactionKind.Structure);

        transaction
            .Delete(selection.From, selection.To)
            .SetSelection(TextSelection.Cursor(selection.From))
            .SetStoredMarks(null);

        return Apply(transaction);
    }

    /// <summary>Appends the text of every paragraph reachable from <paramref name="nodes"/>.</summary>
    /// <remarks>
    /// The separator is counted rather than inferred from whether anything has been written yet.
    /// A document whose first paragraph is empty would otherwise lose its leading blank line.
    /// </remarks>
    private static void Append(StringBuilder builder, IEnumerable<Node> nodes, ref bool any)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ParagraphNode paragraph:
                    if (any)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(paragraph.Content.Text);
                    any = true;
                    break;

                default:
                    Append(builder, node.Children, ref any);
                    break;
            }
        }
    }
}