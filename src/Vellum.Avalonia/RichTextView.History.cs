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

            _history = value;
        }
    }

    /// <summary>Whether there is anything to undo.</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>Whether there is anything to redo.</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>Undoes the most recent edit.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Undo() => Replay(undo: true);

    /// <summary>Redoes the most recently undone edit.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Redo() => Replay(undo: false);

    private bool Replay(bool undo)
    {
        if (undo ? !_history.CanUndo : !_history.CanRedo)
        {
            return false;
        }

        var (state, history) = undo ? _history.Undo(_state) : _history.Redo(_state);

        _history = history;
        _goalX = null;
        State = state;

        return true;
    }
}
