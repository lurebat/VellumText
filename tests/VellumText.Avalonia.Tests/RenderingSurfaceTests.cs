using System.Reflection;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What the rendering layer does and does not put in the public API.
/// </summary>
/// <remarks>
/// <c>architecture.md</c> §4.6 and §4.7 declare <c>BlockView</c>, <c>ParagraphView</c> and
/// <c>InlineTextSource</c> internal, and they were public anyway. They are layout objects holding
/// a <c>TextLayout</c> and a run cache, with lifetimes the presenter owns and invalidation rules
/// nothing outside it can honour, so shipping them at 1.0 would have frozen the rendering
/// machinery as a contract by accident. The list is asserted rather than trusted because the fix
/// is one keyword and so is the regression.
/// </remarks>
public class RenderingSurfaceTests
{
    private static readonly string[] Machinery =
    [
        "BlockView",
        "ParagraphView",
        "RuleView",
        "BlockImageView",
        "TableBlockView",
        "InlineTextSource",
        "TextAt",
    ];

    [Theory]
    [InlineData("BlockView")]
    [InlineData("ParagraphView")]
    [InlineData("RuleView")]
    [InlineData("BlockImageView")]
    [InlineData("TableBlockView")]
    [InlineData("InlineTextSource")]
    [InlineData("TextAt")]
    public void The_layout_objects_are_not_public(string name)
    {
        var type = typeof(RichTextView).Assembly.GetType($"VellumText.Avalonia.{name}", throwOnError: true)!;

        Assert.False(type.IsPublic, $"{name} is public");
    }

    /// <summary>
    /// The stronger claim: nothing public exposes one of them, which is what a compile would catch
    /// today but not once someone adds a member typed as <c>object</c> or a generic.
    /// </summary>
    [Fact]
    public void No_public_member_hands_one_of_them_out()
    {
        var assembly = typeof(RichTextView).Assembly;
        var leaks = new List<string>();
        var checkedMembers = 0;

        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var member in type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                checkedMembers++;

                foreach (var used in Referenced(member))
                {
                    if (Machinery.Contains(used.Name, StringComparer.Ordinal))
                    {
                        leaks.Add($"{type.Name}.{member.Name} -> {used.Name}");
                    }
                }
            }
        }

        // Guards the guard: a walk that found nothing would pass while proving nothing.
        Assert.True(checkedMembers > 100, $"only {checkedMembers} members walked");
        Assert.Equal([], leaks.Distinct().Order());
    }

    /// <summary>
    /// The count survived internalizing the views, because a host counting blocks needs the range
    /// its block indices live in and never needed the layout objects.
    /// </summary>
    [AvaloniaFact]
    public void A_host_can_still_count_blocks()
    {
        var view = new RichTextView
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("one", "two", "three")),
        };

        var window = new global::Avalonia.Controls.Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new global::Avalonia.Size(400, 300));
        window.Arrange(new global::Avalonia.Rect(0, 0, 400, 300));

        Assert.Equal(3, view.BlockCount);
    }

    private static IEnumerable<Type> Referenced(MemberInfo member) => member switch
    {
        PropertyInfo p => [p.PropertyType, .. p.GetIndexParameters().Select(i => i.ParameterType)],
        MethodInfo m => [m.ReturnType, .. m.GetParameters().Select(p => p.ParameterType)],
        FieldInfo f => [f.FieldType],
        ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
        EventInfo e when e.EventHandlerType is { } handler => [handler],
        _ => [],
    };
}
