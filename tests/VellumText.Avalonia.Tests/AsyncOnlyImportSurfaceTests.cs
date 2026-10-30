using System.Reflection;
using VellumText.Avalonia;
using VellumText.Interop.Html;
using VellumText.Interop.Json;
using VellumText.Interop.Rtf;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The library has one import shape, and it is asynchronous (owner decisions OD-2 and Q-1).
/// </summary>
/// <remarks>
/// Checked by reflection over every public method of every shipped assembly this project can see,
/// rather than by naming the members that used to be synchronous: a synchronous import added later
/// under a new name is the regression this exists to catch.
/// </remarks>
public class AsyncOnlyImportSurfaceTests
{
    internal static IEnumerable<string> SynchronousImports(Assembly assembly) =>
        assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.ReturnType == typeof(ImportResult))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}");

    [Theory]
    [InlineData(typeof(IDocumentImporter))]
    [InlineData(typeof(HtmlFormat))]
    [InlineData(typeof(RtfFormat))]
    [InlineData(typeof(JsonFormat))]
    [InlineData(typeof(RichTextClipboard))]
    public void No_public_member_imports_synchronously(Type inAssembly) =>
        Assert.Empty(SynchronousImports(inAssembly.Assembly));

    [Fact]
    public void The_importer_contract_is_the_two_async_members()
    {
        var members = typeof(IDocumentImporter)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) : {m.ReturnType.Name}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "ImportAsync(Stream, CancellationToken) : Task`1",
                "ImportAsync(String, CancellationToken) : Task`1",
            ],
            members);
    }
}