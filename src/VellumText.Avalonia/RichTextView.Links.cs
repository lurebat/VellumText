using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace VellumText.Avalonia;

/// <summary>
/// The hyperlink workflow: making one, editing it, removing it and following it.
/// </summary>
/// <remarks>
/// <para>
/// The model has carried <see cref="LinkMark"/> and both importers have produced links since well
/// before this file; what was missing was every way a user reaches one.
/// <see cref="RichTextView.SetLink"/> already applies and removes the mark, so nothing here
/// re-implements that — this is the gestures, the range a command should act on, and the
/// notification.
/// </para>
/// <para>
/// <b>Activation raises an event and does nothing else.</b> It deliberately does not launch a
/// browser. A control library that opens a URL because a document said so hands every document
/// the application ever renders — a pasted clip, an email, an untrusted file — the ability to
/// start a process on the user's machine. Whether a URL may be opened, and which schemes are
/// acceptable, is the host application's decision and only the host has the context to make it.
/// The event follows <see cref="ContentImported"/>: a notification, not a veto.
/// </para>
/// </remarks>
public partial class RichTextView
{
    /// <summary>Raised when the user activates a link, so the host can decide what to do.</summary>
    /// <remarks>
    /// The library never follows the link itself; see the note on this class. A host that wants
    /// the ordinary behaviour opens <see cref="LinkActivatedEventArgs.Link"/>'s href after
    /// checking the scheme against whatever policy it has.
    /// </remarks>
    public event EventHandler<LinkActivatedEventArgs>? LinkActivated;

    /// <summary>A link the user asked to follow.</summary>
    /// <param name="link">The link that was activated.</param>
    /// <param name="position">The document position it was activated at.</param>
    public sealed class LinkActivatedEventArgs(LinkMark link, int position) : EventArgs
    {
        /// <summary>The link that was activated.</summary>
        public LinkMark Link { get; } = link;

        /// <summary>The document position the link was activated at.</summary>
        public int Position { get; } = position;
    }

    /// <summary>Raised when the user asks to make or edit a link, so the host can prompt.</summary>
    /// <remarks>
    /// Ctrl+K. A notification like <see cref="LinkActivated"/>, and for the same reason: this
    /// control has no dialog of its own and should not grow one. The host reads
    /// <see cref="SelectionLink"/> to fill the prompt in and calls <see cref="ApplyLink"/> with
    /// the answer.
    /// </remarks>
    public event EventHandler<LinkRequestedEventArgs>? LinkRequested;

    /// <summary>A request to make or edit a link over the current selection.</summary>
    /// <param name="existing">The link already there, or null when a new one is being made.</param>
    public sealed class LinkRequestedEventArgs(LinkMark? existing) : EventArgs
    {
        /// <summary>The link already on the selection, or null when there is none.</summary>
        public LinkMark? Existing { get; } = existing;
    }

    /// <remarks>
    /// Silent on a read-only view. The event asks the host to put a link dialog on screen, so
    /// raising it would prompt the user for a URL that <see cref="ApplyLink"/> then refuses --
    /// the same "advertising an edit that will not happen" failure as showing a Bold button, but
    /// worse, because it costs the user a dialog before the refusal.
    /// </remarks>
    private void RaiseLinkRequested()
    {
        if (IsReadOnly)
        {
            return;
        }

        LinkRequested?.Invoke(this, new LinkRequestedEventArgs(SelectionLink));
    }

    /// <summary>
    /// Puts a link on the selection, or on the whole link the caret is inside.
    /// </summary>
    /// <remarks>
    /// The caret case is what makes editing a link's href possible without selecting its text
    /// exactly first, which is the same asymmetry <see cref="RemoveLink"/> exists for.
    /// </remarks>
    /// <param name="link">The link to apply, or null to remove one.</param>
    /// <returns>Whether anything changed.</returns>
    public bool ApplyLink(LinkMark? link)
    {
        if (link is null)
        {
            return RemoveLink();
        }

        if (!_state.Selection.IsEmpty)
        {
            return SetLink(link);
        }

        if (LinkRangeAt(_state.Selection.From) is not var (from, to) || from == to)
        {
            // A caret that is not in a link has no text to make into one, so this only arms the
            // mark — the same thing SetLink documents, and the caller's business to follow with
            // the text it wants linked.
            return SetLink(link);
        }

        var transaction = _state.Transaction().As(TransactionKind.Format);

        transaction
            .Step(new AddMarkStep(from, to, new MarkSet(link: link), MarkFields.Link))
            .SetSelection(_state.Selection)
            .SetStoredMarks(null);

        return Apply(transaction);
    }

    /// <summary>The link at a document position, or null if there is no link there.</summary>
    /// <remarks>
    /// The mark <em>at</em> the position rather than the one a character typed there would get:
    /// an insertion mark is inherited from the character before, so asking that way would report
    /// a link one position past its end and make the space after a link clickable.
    /// </remarks>
    /// <param name="position">The document position.</param>
    public LinkMark? LinkAt(int position)
    {
        var resolved = _state.Doc.Resolve(position);

        if (resolved.Paragraph is not { } paragraph
            || resolved.ParentOffset < 0
            || resolved.ParentOffset >= paragraph.Content.Length)
        {
            return null;
        }

        return paragraph.Content.MarkAt(resolved.ParentOffset).Link;
    }

    /// <summary>The link the selection is on, or null if it is not wholly on one link.</summary>
    /// <remarks>
    /// What a link dialog opens with. A caret reports the link it sits inside, so Ctrl+K in the
    /// middle of a link edits that link rather than offering to make a new one.
    /// </remarks>
    public LinkMark? SelectionLink =>
        _state.Selection.IsEmpty
            ? LinkAt(LinkedCharacterAt(_state.Selection.From))
            : SelectionMarks.Link;

    /// <summary>The character whose link a caret counts as being on.</summary>
    /// <remarks>
    /// The one before it if that is linked — a caret at the end of a link is still inside it as far
    /// as the user is concerned — and otherwise the one after. One rule for what the dialog shows
    /// and what <see cref="ApplyLink"/> and <see cref="RemoveLink"/> edit: with the two links of
    /// <c>&lt;a&gt;x&lt;/a&gt;&lt;a&gt;y&lt;/a&gt;</c> meeting at the caret, they used to pick
    /// opposite sides, and the dialog opened on one link while the other was rewritten.
    /// </remarks>
    private int LinkedCharacterAt(int caret) =>
        caret > 0 && LinkAt(caret - 1) is not null ? caret - 1 : caret;

    /// <summary>Removes the link from the selection, or from the link the caret is inside.</summary>
    /// <remarks>
    /// The caret case is the point of this rather than <see cref="SetLink"/> with null. Removing
    /// a link is something a user does with the caret somewhere in the middle of one — there is
    /// no reason to expect them to select the link text exactly first — and <c>SetLink(null)</c>
    /// on a caret only disarms the stored marks and leaves the document untouched.
    /// </remarks>
    /// <returns>Whether anything changed.</returns>
    public bool RemoveLink()
    {
        if (!_state.Selection.IsEmpty)
        {
            return SetLink(null);
        }

        if (LinkRangeAt(_state.Selection.From) is not var (from, to) || from == to)
        {
            return false;
        }

        var transaction = _state.Transaction().As(TransactionKind.Format);

        transaction
            .Step(new AddMarkStep(from, to, MarkSet.Empty, MarkFields.Link))
            .SetSelection(_state.Selection)
            .SetStoredMarks(null);

        return Apply(transaction);
    }

    /// <summary>
    /// The run of text carrying the same link as <paramref name="position"/>, or null if there
    /// is no link there.
    /// </summary>
    /// <remarks>
    /// Walked over the paragraph's own text, so it stops at the paragraph edge: two paragraphs
    /// that happen to link to the same URL are two links, and joining them would let removing one
    /// silently remove the other.
    /// </remarks>
    /// <param name="position">A document position inside the link.</param>
    private (int From, int To)? LinkRangeAt(int position)
    {
        var inside = LinkedCharacterAt(position);

        if (LinkAt(inside) is not { } link || ParagraphAt(inside) is not { } at)
        {
            return null;
        }

        var from = inside;
        var to = inside + 1;

        while (from > at.Start && LinkAt(from - 1) == link)
        {
            from--;
        }

        while (to < at.Start + at.View.ContentSize && LinkAt(to) == link)
        {
            to++;
        }

        return (from, to);
    }

    /// <summary>Raises <see cref="LinkActivated"/> if there is a link at a position.</summary>
    /// <param name="position">The document position that was activated.</param>
    /// <returns>Whether a link was there and the event was raised.</returns>
    private bool TryActivateLink(int position)
    {
        // The character before as well as the one after, for the same reason LinkRangeAt looks
        // both ways: PositionAt rounds to the nearest caret position, so a click on the right
        // half of a link's last character resolves to the position just past it.
        var link = LinkAt(position) ?? (position > 0 ? LinkAt(position - 1) : null);

        if (link is null)
        {
            return false;
        }

        LinkActivated?.Invoke(this, new LinkActivatedEventArgs(link, position));

        return true;
    }

    /// <summary>
    /// The pointer and tooltip to show over a point, or null to leave both to the caller.
    /// </summary>
    /// <remarks>
    /// A link is invisible to the pointer without this: it is styled text, so nothing about
    /// moving over it says it can be followed or where it goes. The hand only appears with Ctrl
    /// held, because without Ctrl a click there selects text like anywhere else and a hand would
    /// promise a navigation that will not happen.
    /// </remarks>
    /// <param name="point">The point, in this control's coordinates.</param>
    /// <param name="control">Whether Ctrl is held.</param>
    private void UpdateLinkAffordance(Point point, bool control)
    {
        var link = IsOverContent(point) ? LinkAtPoint(point) : null;
        var tip = link is null ? null : LinkTip(link);

        // Only the tip is set; showing it is left to Avalonia's tooltip service, which already
        // owns the hover delay and the popup. Forcing it open here threw outright in a headless
        // window with no overlay layer, and would have fought the service everywhere else.
        if (!string.Equals(ToolTip.GetTip(this) as string, tip, StringComparison.Ordinal))
        {
            ToolTip.SetTip(this, tip);
        }

        _linkCursor = link is not null && control;
    }

    /// <summary>The link under a point, looking both ways for the reason activation does.</summary>
    private LinkMark? LinkAtPoint(Point point)
    {
        var position = PositionAt(point);

        return LinkAt(position) ?? (position > 0 ? LinkAt(position - 1) : null);
    }

    /// <summary>What the tooltip over a link says.</summary>
    /// <remarks>
    /// The href always, and the title as well when there is one. Showing the title alone would
    /// hide where the link actually goes, which is the one thing a reader needs before following
    /// it — a link titled "your invoice" pointing somewhere else is the whole of phishing.
    /// </remarks>
    private static string LinkTip(LinkMark link) =>
        string.IsNullOrEmpty(link.Title) ? link.Href : $"{link.Title}\n{link.Href}";

    /// <summary>Whether the pointer is over a link that Ctrl+click would follow.</summary>
    private bool _linkCursor;
}
