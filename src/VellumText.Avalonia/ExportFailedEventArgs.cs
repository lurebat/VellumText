namespace VellumText.Avalonia;

/// <summary>A registered exporter that failed while the selection was being copied.</summary>
/// <param name="format">The exporter's format name.</param>
/// <param name="exception">What it threw.</param>
/// <remarks>
/// The copy itself went ahead without that exporter's flavour: plain text and every other format
/// still reached the clipboard.
/// </remarks>
public sealed class ExportFailedEventArgs(string format, Exception exception) : EventArgs
{
    /// <summary>The format whose flavour is missing from the clipboard.</summary>
    public string Format { get; } = format;

    /// <summary>The exception the exporter threw.</summary>
    public Exception Exception { get; } = exception;
}