using System.Collections.Immutable;

namespace VellumText.Interop.Markdown;

/// <summary>
/// Markdown text, together with everything about the document that Markdown could not say.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IDocumentExporter"/> has no diagnostics channel, deliberately: its contract is that
/// every construct the model can hold is one the exporter is required to write, so a diagnostic
/// could only ever carry an exporter bug. Markdown is the first supported format smaller than the
/// model — it has no colour, no highlight, no font, no sub- or superscript, no alignment outside a
/// table column, no indent and no merged cells — so for Markdown that premise is simply false.
/// </para>
/// <para>
/// Rather than widen the shipped interface for one format, the loss is reported through a method of
/// its own. <see cref="MarkdownExporter.Export(DocumentNode)"/> still satisfies the interface and
/// still writes the best Markdown it can; a caller who needs to tell the user why the highlighting
/// vanished calls <see cref="MarkdownExporter.ExportWithDiagnostics"/> instead.
/// </para>
/// <para>
/// <see cref="ImportDiagnostic"/> is reused rather than a parallel export type being invented. It
/// carries a severity, a message and a context, which is exactly what is needed, and a second type
/// with the same three fields would only mean hosts writing two display paths for one idea.
/// </para>
/// </remarks>
/// <param name="Text">The Markdown.</param>
/// <param name="Diagnostics">What could not be written faithfully, in the order it happened.</param>
public readonly record struct MarkdownExportResult(
    string Text,
    ImmutableArray<ImportDiagnostic> Diagnostics)
{
    /// <summary>Whether anything was removed outright rather than merely downgraded.</summary>
    /// <remarks>
    /// The distinction matters to a host deciding whether to warn before overwriting a file. A
    /// downgrade — bold surviving but its colour not — leaves the words intact. A drop does not: a
    /// table nested inside a table cell has nowhere to go in a pipe table.
    /// </remarks>
    public bool AnythingDropped =>
        Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Dropped);
}
