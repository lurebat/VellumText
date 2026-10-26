using System.Reflection;
using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// The Markdown package's public API says nothing about HTML.
/// </summary>
/// <remarks>
/// <para>
/// Markdown import runs through an HTML stage internally — Markdig renders HTML and the HTML
/// importer reads it — for the same reason RTF does: one place decides what an import is allowed to
/// contain. That is an implementation choice, and the RTF package shows what happens when such a
/// choice reaches the surface. <c>RtfImporter.Import(string, HtmlImportOptions?)</c> made every
/// consumer who wanted to configure RTF import take a dependency on the HTML package, and froze an
/// internal decision where a later rewrite of the reader could not have changed it.
/// <see cref="MarkdownImportOptions"/> exists so that does not happen twice.
/// </para>
/// <para>
/// Markdig is behind the same wall. A <c>MarkdownPipeline</c> on the surface would be worse than the
/// HTML leak, because it would make the parser itself part of the compatibility contract.
/// </para>
/// </remarks>
public class MarkdownPublicSurfaceTests
{
    [Fact]
    public void No_public_member_of_the_markdown_package_names_a_type_from_the_html_package() =>
        AssertNothingLeaksFrom(typeof(VellumText.Interop.Html.HtmlImportOptions).Assembly);

    [Fact]
    public void No_public_member_of_the_markdown_package_names_a_type_from_markdig() =>
        AssertNothingLeaksFrom(typeof(Markdig.MarkdownPipeline).Assembly);

    private static void AssertNothingLeaksFrom(Assembly hidden)
    {
        var leaked = new List<string>();
        var checkedMembers = 0;

        foreach (var type in typeof(MarkdownImporter).Assembly.GetExportedTypes())
        {
            foreach (var member in type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.DeclaredOnly))
            {
                checkedMembers++;

                foreach (var used in TypesIn(member))
                {
                    if (used.Assembly == hidden)
                    {
                        leaked.Add($"{type.Name}.{member.Name} -> {used.Name}");
                    }
                }
            }
        }

        // Guards the guard: a reflection walk that found nothing would pass silently.
        Assert.True(
            checkedMembers > 20,
            $"only {checkedMembers} members walked - has the package moved?");
        Assert.Equal([], leaked.Distinct().Order());
    }

    /// <summary>
    /// Every option this package offers reaches the HTML stage that acts on it, at its default
    /// value and at a value other than its default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hand-written mapping between two records is where an option gets silently forgotten, and
    /// the symptom is the worst kind: <c>AllowRemoteImages = true</c> that does nothing, or worse,
    /// an <c>AllowDataImages = false</c> a host set for safety that never arrives.
    /// </para>
    /// <para>
    /// The second set of values is generated rather than written out, so that an option added to
    /// the record later is changed here too without anyone remembering to. A hand-written set left
    /// a new option at its default, where an unforwarded one reads the same as a forwarded one
    /// whenever the two records' defaults agree. A property of a type <see cref="Another"/> does
    /// not know fails the test rather than being skipped.
    /// </para>
    /// <para>
    /// <see cref="MarkdownImportOptions.AllowRawHtml"/> is excluded by name because it has no HTML
    /// counterpart by design: it decides whether raw HTML survives the Markdown parse at all, which
    /// happens before the HTML stage exists. The exclusion is a list rather than a skip so that a
    /// second unmapped option cannot join it by accident.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_markdown_option_reaches_the_html_stage()
    {
        var properties = typeof(MarkdownImportOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var defaults = new MarkdownImportOptions();
        var changed = new MarkdownImportOptions();

        foreach (var property in properties)
        {
            property.SetValue(changed, Another(property, property.GetValue(defaults)));
        }

        var unmapped = new List<string>();
        var compared = 0;

        foreach (var property in properties)
        {
            if (property.Name == nameof(MarkdownImportOptions.AllowRawHtml))
            {
                continue;
            }

            var counterpart = typeof(VellumText.Interop.Html.HtmlImportOptions).GetProperty(property.Name);

            Assert.NotNull(counterpart);

            compared++;

            // What the HTML stage holds when the option is not forwarded at all. One of the two
            // values compared has to differ from it, or dropping the option would go unnoticed.
            var unforwarded = counterpart.GetValue(VellumText.Interop.Html.HtmlImportOptions.Default);

            Assert.False(
                Equals(property.GetValue(defaults), unforwarded) && Equals(property.GetValue(changed), unforwarded),
                $"{property.Name}: neither value compared differs from the HTML default");

            foreach (var (name, options) in new[] { ("default", defaults), ("changed", changed) })
            {
                if (!Equals(property.GetValue(options), counterpart.GetValue(options.ToHtmlOptions())))
                {
                    unmapped.Add($"{property.Name} ({name})");
                }
            }
        }

        Assert.True(compared >= 5, $"only {compared} options compared - has the record moved?");
        Assert.Equal([], unmapped.Order());
    }

    /// <summary>A value for <paramref name="property"/> other than <paramref name="current"/>.</summary>
    private static object? Another(PropertyInfo property, object? current) => current switch
    {
        bool value => !value,
        int value => value == int.MaxValue ? value - 7 : value + 7,
        long value => value == long.MaxValue ? value - 7 : value + 7,
        Uri => null,
        null when property.PropertyType == typeof(Uri) => new Uri("https://example.test/dir/page"),
        _ => throw new InvalidOperationException(
            $"{property.Name}: give this test a second value for {property.PropertyType.Name}."),
    };

    /// <summary>
    /// Every type a member's signature names, including the ones nested inside another type.
    /// </summary>
    /// <remarks>
    /// The recursion is the point. A guard that looked only at the outer type reported
    /// <c>IReadOnlyList&lt;HtmlImportOptions&gt;</c> as living in <c>System.Private.CoreLib</c> and
    /// let the leak through, while the same leak written as an array or a bare property was caught —
    /// so the hole was invisible to anyone who tested the guard the obvious way. Element types and
    /// interfaces are walked for the same reason, and <paramref name="seen"/> is what stops
    /// <c>string : IComparable&lt;string&gt;</c> recursing forever.
    /// </remarks>
    private static IEnumerable<Type> TypesIn(MemberInfo member) =>
        Expand(Signature(member), []);

    private static IEnumerable<Type> Signature(MemberInfo member) => member switch
    {
        MethodInfo method =>
            [method.ReturnType, .. method.GetParameters().Select(p => p.ParameterType)],
        ConstructorInfo ctor => ctor.GetParameters().Select(p => p.ParameterType),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        EventInfo e when e.EventHandlerType is { } handler => [handler],
        _ => [],
    };

    private static IEnumerable<Type> Expand(IEnumerable<Type> types, HashSet<Type> seen)
    {
        foreach (var type in types)
        {
            if (!seen.Add(type))
            {
                continue;
            }

            yield return type;

            foreach (var inner in Expand(
                [
                    .. type.GetGenericArguments(),
                    .. type.HasElementType ? new[] { type.GetElementType()! } : [],
                    .. type.GetInterfaces(),
                ],
                seen))
            {
                yield return inner;
            }
        }
    }
}
