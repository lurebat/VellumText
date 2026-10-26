using VellumText.Interop.Rtf;
using System.Reflection;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// The RTF package's public API says nothing about HTML.
/// </summary>
/// <remarks>
/// RTF import runs through an HTML stage internally — RtfPipe emits HTML and the HTML importer
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
    /// Every option this package offers reaches the HTML stage that acts on it.
    /// </summary>
    /// <remarks>
    /// A hand-written mapping between two records is where an option gets silently forgotten, and
    /// the symptom is the worst kind: <c>AllowRemoteImages = true</c> that does nothing, or worse,
    /// an <c>AllowDataImages = false</c> a host set for safety that never arrives. Reflection
    /// rather than four assertions so that adding a fifth option and not mapping it fails here.
    /// </remarks>
    [Fact]
    public void EveryRtfOptionReachesTheHtmlStage()
    {
        var options = new RtfImportOptions
        {
            AllowRemoteImages = true,
            AllowDataImages = false,
            MaxDataImageBytes = 4321,
            BaseUri = new Uri("https://example.test/dir/page"),
            MaxTableSlots = 77,
        };

        var mapped = options.ToHtmlOptions();
        var unmapped = new List<string>();
        var compared = 0;

        foreach (var property in typeof(RtfImportOptions).GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            var counterpart = typeof(VellumText.Interop.Html.HtmlImportOptions)
                .GetProperty(property.Name);

            Assert.NotNull(counterpart);

            compared++;

            if (!Equals(property.GetValue(options), counterpart.GetValue(mapped)))
            {
                unmapped.Add(property.Name);
            }
        }

        Assert.True(compared >= 5, $"only {compared} options compared - has the record moved?");
        Assert.Equal([], unmapped.Order());
    }

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
