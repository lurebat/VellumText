using System.Collections.Immutable;

namespace VellumText.Avalonia;

/// <summary>
/// A column boundary a pointer is on, with what it would take to move it.
/// </summary>
/// <remarks>
/// <para>
/// The widths are the ones the view just laid out, in pixels, rather than the ratios the document
/// states. A drag is a pixel gesture, and the table may state no widths at all — an automatic
/// table has an empty vector and would otherwise have nothing to adjust. Working from the laid-out
/// edges means an automatic table and a stated one behave identically under the pointer, and the
/// conversion back to ratios happens once, on release.
/// </para>
/// <para>
/// <see cref="Boundary"/> indexes the edges, not the columns: boundary <c>b</c> divides column
/// <c>b - 1</c> from column <c>b</c>, and only the interior ones are ever reported.
/// </para>
/// </remarks>
/// <param name="TablePos">The position directly before the table.</param>
/// <param name="Boundary">Which edge, counted from the table's left edge as zero.</param>
/// <param name="Widths">The laid-out column widths, in pixels.</param>
/// <param name="Left">Where the table's left edge sits in the control's coordinates.</param>
internal readonly record struct ColumnBoundary(
    int TablePos, int Boundary, ImmutableArray<double> Widths, double Left);
