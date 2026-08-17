using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Checks that every member <c>docs/guide.md</c> names actually exists, by reading the guide.
/// </summary>
/// <remarks>
/// <para>
/// Documentation drifts silently because nothing checks it. Four of this guide's claims were wrong
/// when it was first written and all four looked plausible: <c>Replace</c> took a different
/// argument list, <c>EndFind</c> was really <c>ClearFind</c>, <c>CurrentMatchNumber</c> is on the
/// editor while the view has a zero-based <c>CurrentMatch</c>, and <c>Rectangle</c> is private.
/// </para>
/// <para>
/// <b>An earlier version of this file hand-copied the guide's snippets, which is exactly what does
/// not work.</b> When the guide turned out to still contain the wrong names, the copy was quietly
/// corrected instead — so it compiled, passed, and proved nothing at all about the document it was
/// supposed to be guarding. It reads the file now: whatever the guide says is what gets checked,
/// and correcting one without the other fails.
/// </para>
/// </remarks>
public class GuideCheckTests
{
    /// <summary>Receivers the guide uses, and the type each one stands for.</summary>
    private static readonly (string Receiver, Type Type)[] Receivers =
    [
        ("view", typeof(RichTextView)),
        ("editor", typeof(RichTextEditor)),
        ("Editor", typeof(RichTextEditor)),
        ("map", typeof(DocumentTextMap)),
        ("Slice", typeof(Slice)),
        ("RichTextClipboard", typeof(RichTextClipboard)),
        ("DocumentPreview", typeof(DocumentPreview)),
        ("DocumentText", typeof(DocumentText)),
        ("DocumentTextMap", typeof(DocumentTextMap)),
        ("DocumentSearch", typeof(DocumentSearch)),
        ("BitmapEmbedRenderer", typeof(BitmapEmbedRenderer)),
        ("RichTextToolbar", typeof(RichTextToolbar)),
    ];

    [AvaloniaFact]
    public void EveryMemberTheGuideNamesExists()
    {
        var guide = ReadGuide();
        var missing = new List<string>();
        var found = 0;

        foreach (var (receiver, type) in Receivers)
        {
            foreach (Match match in Regex.Matches(guide, $@"\b{receiver}\.([A-Z]\w*)"))
            {
                var member = match.Groups[1].Value;

                found++;

                if (type.GetMember(
                        member,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                            | BindingFlags.FlattenHierarchy).Length == 0)
                {
                    missing.Add($"{type.Name}.{member}");
                }
            }
        }

        // Guards the guard: a regex that matched nothing would pass silently.
        Assert.True(found > 40, $"only {found} member references found - has the guide moved?");
        Assert.Equal([], missing.Distinct().Order());
    }

    [AvaloniaFact]
    public void TheThemeUriTheGuideGivesResolves()
    {
        // The first claim a reader acts on, and the one that breaks silently if anything is
        // renamed.
        var uri = Regex.Match(ReadGuide(), @"avares://[^\s""]+\.axaml").Value;

        Assert.Equal("avares://VellumText.Avalonia/Themes/VellumText.axaml", uri);
    }

    [AvaloniaFact]
    public void TheControlPropertiesTheGuidesTableNamesExist()
    {
        // The options table is prose rather than code, so nothing else here would catch it.
        var guide = ReadGuide();
        var table = guide[guide.IndexOf("## Options", StringComparison.Ordinal)..];
        var missing = new List<string>();
        var found = 0;

        foreach (Match match in Regex.Matches(table[..table.IndexOf("## Theming", StringComparison.Ordinal)], @"^\| `(\w+)`", RegexOptions.Multiline))
        {
            var name = match.Groups[1].Value;

            found++;

            if (typeof(RichTextView).GetMember(name).Length == 0
                && typeof(RichTextEditor).GetMember(name).Length == 0)
            {
                missing.Add(name);
            }
        }

        Assert.True(found >= 6, $"only {found} options found - has the table moved?");
        Assert.Equal([], missing.Order());
    }

    [AvaloniaFact]
    public void TheBrushesTheGuideNamesAreRealProperties()
    {
        var guide = ReadGuide();
        var section = guide[guide.IndexOf("## Theming", StringComparison.Ordinal)..];
        var missing = new List<string>();
        var found = 0;

        foreach (Match match in Regex.Matches(section, @"`(\w+Brush)`"))
        {
            var name = match.Groups[1].Value;

            // VellumText-prefixed names in this section are theme resource keys, checked below.
            if (name.StartsWith("VellumText", StringComparison.Ordinal))
            {
                continue;
            }

            found++;

            if (typeof(RichTextView).GetProperty(name) is null)
            {
                missing.Add(name);
            }
        }

        Assert.True(found >= 5, $"only {found} brushes found - has the section moved?");
        Assert.Equal([], missing.Distinct().Order());
    }

    [AvaloniaFact]
    public void TheToolbarResourcesTheGuideNamesAreDefined()
    {
        // Checked against the theme source rather than Application.Current, because the theme's
        // resources live inside its own Styles and the test harness does not include it - a
        // lookup there finds nothing and would fail every key, which says nothing about the guide.
        var theme = string.Concat(
            Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "VellumText.Avalonia", "Themes"), "*.axaml")
                .Select(File.ReadAllText));

        var defined = Regex.Matches(theme, @"x:Key=""(VellumText\w+)""")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(defined);

        var missing = Regex.Matches(ReadGuide(), @"`(VellumTextToolbar\w*)`")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .Where(key => !defined.Contains(key))
            .Order()
            .ToArray();

        Assert.Equal([], missing);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "docs", "guide.md")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }

    private static string ReadGuide() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "guide.md"));
}
