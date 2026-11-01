using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;
using VellumText.Interop.Html;
using VellumText.Interop.Json;
using VellumText.Interop.Rtf;

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
        ("viewer", typeof(RichTextViewer)),
        ("editor", typeof(RichTextEditor)),
        ("Editor", typeof(RichTextEditor)),
        ("map", typeof(DocumentTextMap)),
        ("Slice", typeof(Slice)),
        ("RichTextClipboard", typeof(RichTextClipboard)),
        ("DocumentPreview", typeof(DocumentPreview)),
        ("DocumentText", typeof(DocumentText)),
        ("DocumentTextMap", typeof(DocumentTextMap)),
        ("DocumentSearch", typeof(DocumentSearch)),
        ("DocumentPayload", typeof(DocumentPayload)),
        ("BitmapEmbedRenderer", typeof(BitmapEmbedRenderer)),
        ("RichTextToolbar", typeof(RichTextToolbar)),
        ("HtmlFormat", typeof(HtmlFormat)),
        ("RtfFormat", typeof(RtfFormat)),
        ("JsonFormat", typeof(JsonFormat)),

        // Not a typo: the receiver in the guide really is `HtmlFormat.Instance`, and without this
        // entry `With` — the whole point of the configuration seam — would go unchecked.
        ("HtmlFormat.Instance", typeof(HtmlFormat)),
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

    [AvaloniaFact]
    public void TheImportOptionsTheGuideSetsAreRealProperties()
    {
        // These arrive in an object initializer, so the receiver regex above cannot see them —
        // and they are the security controls, where a guide naming a property that no longer
        // exists teaches a host to write code that does not compile, or worse, silently omits a
        // setting they believed they had made.
        var guide = ReadGuide();
        var missing = new List<string>();
        var found = 0;

        foreach (Match block in Regex.Matches(
            guide,
            @"new (Html|Rtf)ImportOptions\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline))
        {
            var type = block.Groups[1].Value == "Html"
                ? typeof(HtmlImportOptions)
                : typeof(RtfImportOptions);

            foreach (Match property in Regex.Matches(block.Groups["body"].Value, @"(\w+)\s*="))
            {
                var name = property.Groups[1].Value;

                found++;

                if (type.GetProperty(name) is null)
                {
                    missing.Add($"{type.Name}.{name}");
                }
            }
        }

        Assert.True(found >= 2, $"only {found} import options found - has the section moved?");
        Assert.Equal([], missing.Distinct().Order());
    }

    [AvaloniaFact]
    public void TheMvvmSurfaceTheGuideDescribesExistsOnBothControls()
    {
        // The section a developer reads in their first ten minutes, and none of it is reachable by
        // the receiver regex above: the table is prose, and the XAML snippet is the exact thing
        // that gets copied. A binding to a property that is not there fails at runtime with a
        // warning in a log nobody is reading.
        var guide = ReadGuide();
        var section = Section(guide, "## Binding to a view model", "## Which control for a preview pane");
        var missing = new List<string>();
        var rows = 0;

        foreach (Match row in Regex.Matches(section, @"^\| `(\w+)` \| ([^|]+)\|", RegexOptions.Multiline))
        {
            var member = row.Groups[1].Value;

            foreach (var control in row.Groups[2].Value.Split(','))
            {
                var type = control.Trim() switch
                {
                    "editor" => typeof(RichTextEditor),
                    "view" => typeof(RichTextView),
                    _ => null,
                };

                if (type is null)
                {
                    continue;
                }

                rows++;

                if (type.GetMember(member).Length == 0)
                {
                    missing.Add($"{type.Name}.{member}");
                }
            }
        }

        // Bindable in XAML means the AvaloniaProperty exists too, not just the CLR property.
        var attributes = 0;

        foreach (Match attribute in Regex.Matches(section, @"(\w+)=""\{Binding"))
        {
            var name = attribute.Groups[1].Value;

            attributes++;

            if (typeof(RichTextEditor).GetField($"{name}Property", BindingFlags.Public | BindingFlags.Static) is null)
            {
                missing.Add($"RichTextEditor.{name}Property");
            }
        }

        var commands = 0;

        foreach (Match command in Regex.Matches(section, @"`(\w+Command)`"))
        {
            // The interface itself, named in the prose that introduces the list.
            if (command.Groups[1].Value == "ICommand")
            {
                continue;
            }

            commands++;

            if (typeof(RichTextToolbar).GetProperty(command.Groups[1].Value) is null)
            {
                missing.Add($"RichTextToolbar.{command.Groups[1].Value}");
            }
        }

        // Guards the guard three times over: each regex would pass silently against a section that
        // had been renamed out from under it.
        Assert.True(rows >= 8, $"only {rows} table entries found - has the section moved?");
        Assert.True(attributes >= 2, $"only {attributes} bound attributes found - has the section moved?");
        Assert.True(commands >= 10, $"only {commands} commands found - has the section moved?");
        Assert.Equal([], missing.Distinct().Order());
    }

    private static string Section(string guide, string from, string to)
    {
        var start = guide.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"'{from}' is no longer in the guide");

        var end = guide.IndexOf(to, start, StringComparison.Ordinal);

        Assert.True(end > start, $"'{to}' no longer follows '{from}' in the guide");

        return guide[start..end];
    }

    private static string RepositoryRoot()
    {
        // Package qualification runs tested binaries from an out-of-tree artifacts root.
        var sourceRoot = typeof(GuideCheckTests).Assembly
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepositoryRoot").Value!;

        foreach (var start in new[] { AppContext.BaseDirectory, sourceRoot })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "guide.md")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        Assert.Fail("Cannot locate docs/guide.md from the test output or working directory.");

        throw new InvalidOperationException("The repository guide is required by these tests.");
    }

    private static string ReadGuide() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "guide.md"));
}
