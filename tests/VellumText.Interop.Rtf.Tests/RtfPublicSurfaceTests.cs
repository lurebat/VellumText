using VellumText.Interop.Rtf;
using System.Reflection;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// The RTF package's public API says nothing about HTML.
/// </summary>
/// <remarks>
/// RTF import runs through an HTML stage internally — the reader emits HTML and the HTML importer
/// reads it — but that is an implementation choice. It leaked into the public surface as
/// <c>RtfImporter.Import(string, HtmlImportOptions?)</c>, which made every consumer who wanted to
/// configure RTF import take a dependency on the HTML package and froze an internal decision where
/// a later rewrite of the RTF reader could not have changed it.
/// </remarks>
public class RtfPublicSurfaceTests
{
    [Fact]
    public void NoPublicMemberOfTheRtfPackageNamesATypeFromTheHtmlPackage()
    {
        var html = typeof(VellumText.Interop.Html.HtmlImportOptions).Assembly;
        var leaked = new List<string>();
        var checkedMembers = 0;

        foreach (var type in typeof(RtfImporter).Assembly.GetExportedTypes())
        {
            foreach (var member in type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.DeclaredOnly))
            {
                checkedMembers++;

                foreach (var used in TypesIn(member))
                {
                    if (used.Assembly == html)
                    {
                        leaked.Add($"{type.Name}.{member.Name} -> {used.Name}");
                    }
                }
            }
        }

        // Guards the guard: a reflection walk that found nothing would pass silently.
        Assert.True(checkedMembers > 20, $"only {checkedMembers} members walked - has the package moved?");
        Assert.Equal([], leaked.Distinct().Order());
    }

    [Fact]
    public void RtfImportOptionsDefaultsAreTheParanoidOnes()
    {
        var options = RtfImportOptions.Default;

        Assert.False(options.AllowRemoteImages);
        Assert.True(options.AllowDataImages);
        Assert.Null(options.BaseUri);
        Assert.Equal(8 * 1024 * 1024, options.MaxDataImageBytes);
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
    /// </remarks>
    [Fact]
    public void EveryRtfOptionReachesTheHtmlStage()
    {
        var properties = typeof(RtfImportOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var defaults = new RtfImportOptions();
        var changed = new RtfImportOptions();

        foreach (var property in properties)
        {
            property.SetValue(changed, Another(property, property.GetValue(defaults)));
        }

        var unmapped = new List<string>();

        foreach (var property in properties)
        {
            var counterpart = typeof(VellumText.Interop.Html.HtmlImportOptions).GetProperty(property.Name);

            Assert.NotNull(counterpart);

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

        Assert.True(properties.Length >= 5, $"only {properties.Length} options compared - has the record moved?");
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

    private static IEnumerable<Type> TypesIn(MemberInfo member) => member switch
    {
        MethodInfo method =>
            [method.ReturnType, .. method.GetParameters().Select(p => p.ParameterType)],
        ConstructorInfo ctor => ctor.GetParameters().Select(p => p.ParameterType),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        EventInfo e when e.EventHandlerType is { } handler => [handler],
        _ => [],
    };
}
