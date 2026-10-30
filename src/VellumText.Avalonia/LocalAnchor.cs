namespace VellumText.Avalonia;

/// <summary>How a <see cref="LocalAnchor"/> names its point.</summary>
internal enum AnchorKind
{
    /// <summary>
    /// <see cref="LocalAnchor.Dy"/> below the top of the visual line holding
    /// <see cref="LocalAnchor.Position"/>: the text line stays where it is however the lines
    /// around it reflow.
    /// </summary>
    Line,

    /// <summary><see cref="LocalAnchor.Dy"/> below the top of the block at the position.</summary>
    Box,

    /// <summary><see cref="LocalAnchor.Dy"/> below the bottom of the block at the position.</summary>
    BoxBottom,

    /// <summary>
    /// <see cref="LocalAnchor.Dy"/> as a share of the height of the table row starting at the
    /// position, for a row nobody has laid out, whose height is a guess.
    /// </summary>
    RowShare,
}

/// <summary>
/// A point inside a block named by what is there rather than by where it is: a local position,
/// and an offset from the line or the box holding it.
/// </summary>
/// <remarks>
/// <para>
/// For keeping what is at the top of the viewport at the top of the viewport. A distance from the
/// top of a block is not that: anything above the point inside the block — a row of a nested table
/// measured at last, an edit three paragraphs up, composed text growing — moves the content under
/// a fixed distance. A position is moved by an edit exactly as the content is (the transaction's
/// mapping says how), and the line holding it is wherever the layout put that content. So the
/// line at the top is held, not the pixel (owner decision Q3, the browser's scroll anchoring).
/// </para>
/// <para>
/// Read from the last layout without laying anything out, because it is taken before an edit or a
/// composition is reconciled, when the views still show what the reader saw.
/// </para>
/// </remarks>
/// <param name="Position">A local position inside the block.</param>
/// <param name="Dy">The offset from the line or box the position is in; see <see cref="Kind"/>.</param>
/// <param name="Kind">How the point is named.</param>
/// <param name="Unclamped">
/// Whether <paramref name="Dy"/> may reach past the line: the top of the viewport was on a line
/// made wholly of composed text, which has no position of its own, so the point is held from the
/// line the composition starts on.
/// </param>
/// <param name="AfterComposition">
/// Whether <paramref name="Position"/> names the text right after a composition at it rather than
/// the composition's start, which is the same position: the line held is the one the paragraph's
/// own text resumes on.
/// </param>
internal readonly record struct LocalAnchor(
    int Position, double Dy, AnchorKind Kind, bool Unclamped = false, bool AfterComposition = false)
{
    /// <summary>The same point, moved by <paramref name="delta"/> positions.</summary>
    /// <param name="delta">How far to move it.</param>
    public LocalAnchor Shift(int delta) => this with { Position = Position + delta };
}
