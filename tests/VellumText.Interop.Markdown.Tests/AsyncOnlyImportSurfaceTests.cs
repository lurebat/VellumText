using System.Reflection;
using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>The Markdown package has no synchronous import either (owner decisions OD-2, Q-1).</summary>
public class AsyncOnlyImportSurfaceTests
{
    [Fact]
    public void No_public_member_imports_synchronously() =>
        Assert.Empty(
            typeof(MarkdownFormat).Assembly.GetExportedTypes()
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(method => method.ReturnType == typeof(ImportResult))
                .Select(method => $"{method.DeclaringType!.Name}.{method.Name}"));
}