using Avalonia;
using Avalonia.Input;

namespace Vellum.Avalonia;

/// <summary>
/// Undo and redo.
/// </summary>
/// <remarks>
/// The history is recorded in <see cref="RichTextView.Apply"/>, so every edit that goes through
/// the control is undoable by construction and nothing has to remember to record itself. An
/// application that sets <see cref="RichTextView.State"/> directly is deliberately outside this:
/// that is how a host replaces the document wholesale, and folding a document swap into the undo
/// stack would let Ctrl+Z resurrect a document the host had discarded.
/// </remarks>
public partial class RichTextView
{
    /// <summary>Defines the <see cref="CanUndo"/> property.</summary>
    public static readonly DirectProperty<RichTextView, bool> CanUndoProperty =
        AvaloniaProperty.RegisterDirect<RichTextView, bool>(nameof(CanUndo), o => o.CanUndo);

    /// <summary>Defines the <see cref="CanRedo"/> property.</summary>
    public static readonly DirectProperty<RichTextView, bool> CanRedoProperty =
        AvaloniaProperty.RegisterDirect<RichTextView, bool>(nameof(CanRedo), o => o.CanRedo);

    private History _history = History.Empty;

    /// <summary>The undo history.</summary>
    /// <remarks>
    /// Settable so that a host can choose a grouping policy, or clear the history after loading
    /// a document. Replacing it discards whatever was on both stacks, which is the point.
    /// </remarks>
    public History History
    {
        get => _history;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var before = HistoryFlags;

            _history = value;

            RaiseHistoryFlags(before);
        }
    }

    /// <summary>Whether there is anything to undo.</summary>
    /// <remarks>
    /// Observable, so that a toolbar button can bind its enabled state to it. A command surface
    /// that had to poll this would be wrong for exactly as long as nobody looked.
    /// </remarks>
    public bool CanUndo => _history.CanUndo;

    /// <summary>Whether there is anything to redo.</summary>
    /// <remarks>See <see cref="CanUndo"/>.</remarks>
    public bool CanRedo => _history.CanRedo;

    /// <summary>Undoes the most recent edit.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Undo() => Replay(undo: true);

    /// <summary>Redoes the most recently undone edit.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Redo() => Replay(undo: false);

    private (bool Undo, bool Redo) HistoryFlags => (_history.CanUndo, _history.CanRedo);

    /// <remarks>
    /// Raised after the state has been installed, never before. A listener woken by
    /// <see cref="CanUndoProperty"/> asks what the document now says, and the answer has to be
    /// the document this history belongs to.
    /// </remarks>
    private void RaiseHistoryFlags((bool Undo, bool Redo) before)
    {
        if (before.Undo != _history.CanUndo)
        {
            RaisePropertyChanged(CanUndoProperty, before.Undo, _history.CanUndo);
        }

        if (before.Redo != _history.CanRedo)
        {
            RaisePropertyChanged(CanRedoProperty, before.Redo, _history.CanRedo);
        }
    }

    private bool Replay(bool undo)
    {
        if (undo ? !_history.CanUndo : !_history.CanRedo)
        {
            return false;
        }

        var before = HistoryFlags;
        var (state, history) = undo ? _history.Undo(_state) : _history.Redo(_state);

        _history = history;
        _goalX = null;
        State = state;

        RaiseHistoryFlags(before);

        return true;
    }
}
