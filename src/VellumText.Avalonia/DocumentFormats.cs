namespace VellumText.Avalonia;

/// <summary>
/// Refuses a format object that can neither be read nor written, at the point of registration
/// rather than silently doing nothing at the point of use.
/// </summary>
/// <remarks>
/// Lifted out of <see cref="RichTextView"/> when the viewer gained copy: both controls hand the
/// same collection to the host, so the same guard has to apply to both, and a private nested copy
/// per control would have been the second one.
/// </remarks>
internal sealed class DocumentFormats : System.Collections.ObjectModel.Collection<IDocumentFormat>
{
    protected override void InsertItem(int index, IDocumentFormat item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item is not (IDocumentImporter or IDocumentExporter))
        {
            throw new ArgumentException(
                $"'{item.Format}' can neither be read nor written: it implements neither " +
                $"{nameof(IDocumentImporter)} nor {nameof(IDocumentExporter)}.",
                nameof(item));
        }

        base.InsertItem(index, item);
    }
}
