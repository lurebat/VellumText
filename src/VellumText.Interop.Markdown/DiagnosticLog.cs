using System.Collections.Immutable;

namespace VellumText.Interop.Markdown;

/// <summary>
/// Collects diagnostics, reporting each distinct thing once.
/// </summary>
/// <remarks>
/// A document's worth of Markdown can contain thousands of removals of the same kind. Reporting
/// each one turns a useful list into a wall the caller cannot read, and gives the source control
/// over how much memory the diagnostics take. This is the same rule the HTML sanitizer applies,
/// deliberately: a host that shows the two lists side by side should not see them behave
/// differently.
/// </remarks>
internal sealed class DiagnosticLog
{
    private readonly List<ImportDiagnostic> _items = [];
    private readonly HashSet<string> _reported = [];

    /// <summary>Whether anything has been reported.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>What has been reported, in the order it happened.</summary>
    public ImmutableArray<ImportDiagnostic> Items => [.. _items];

    /// <summary>Records a diagnostic the first time each distinct thing is reported.</summary>
    /// <param name="severity">How badly the conversion had to compromise.</param>
    /// <param name="message">What happened, in terms a user of the host could act on.</param>
    /// <param name="context">The construct it happened to.</param>
    public void ReportOnce(DiagnosticSeverity severity, string message, string context)
    {
        if (_reported.Add($"{severity}|{context}"))
        {
            _items.Add(new ImportDiagnostic(severity, message, context));
        }
    }
}
