using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace VellumText.Interop.Html;

/// <summary>
/// AngleSharp's attribute observer without the image request that setting an image's source
/// starts.
/// </summary>
/// <remarks>
/// <para>
/// Setting <c>src</c>, <c>srcset</c>, <c>sizes</c> or <c>crossorigin</c> on an <c>img</c> makes the
/// default observer choose the image's source again and resolve it as a URL for a request. Nothing
/// here can make that request: the importer's context has no resource loader. For a pasted
/// picture the source is its whole data URL, and resolving it cost six copies of it — 64 MiB for a
/// 4 MiB picture — when the sanitizer set the approved URL back on the element.
/// </para>
/// <para>
/// Every other change is passed on as before. The observer also keeps an element's class list in
/// step with its <c>class</c> attribute, a <c>base</c> element's address in step with the
/// document's, and more, so it is narrowed rather than removed.
/// </para>
/// </remarks>
/// <param name="inner">The observer to pass every other change to.</param>
internal sealed class WithoutImageRequests(IAttributeObserver inner) : IAttributeObserver
{
    /// <summary>The configuration the importer parses with.</summary>
    internal static IConfiguration Configuration { get; } = Narrowed(AngleSharp.Configuration.Default);

    /// <inheritdoc/>
    public void NotifyChange(IElement host, string name, string? value)
    {
        if (host is IHtmlImageElement && name is "src" or "srcset" or "sizes" or "crossorigin")
        {
            return;
        }

        inner.NotifyChange(host, name, value);
    }

    private static IConfiguration Narrowed(IConfiguration configuration)
    {
        var observer = configuration.Services.OfType<IAttributeObserver>().Single();

        return configuration.Without<IAttributeObserver>().With(new WithoutImageRequests(observer));
    }
}
