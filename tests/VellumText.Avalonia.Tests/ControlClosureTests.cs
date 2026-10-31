using System.Reflection;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The controls are closed to subclassing, so their overrides are not a contract with hosts.
/// </summary>
/// <remarks>
/// A compiler-generated constructor on an abstract class is <c>protected</c>, so deleting
/// <see cref="DocumentPresenter"/>'s explicit one, or dropping a <c>sealed</c>, reopens the
/// hierarchy without any error. These tests are what notice.
/// </remarks>
public class ControlClosureTests
{
    [Theory]
    [InlineData(typeof(RichTextView))]
    [InlineData(typeof(RichTextEditor))]
    [InlineData(typeof(RichTextViewer))]
    public void TheControlsAreSealed(Type control)
    {
        Assert.True(control.IsPublic);
        Assert.True(control.IsSealed, $"{control.Name} is not sealed");
    }

    [Fact]
    public void AHostCannotDeriveAPresenter()
    {
        var presenter = typeof(DocumentPresenter);
        var constructors = presenter.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.True(presenter.IsPublic && presenter.IsAbstract);
        Assert.NotEmpty(constructors);
        Assert.All(constructors, c =>
            Assert.False(c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly, $"{c} is reachable from a host"));
    }

    [Fact]
    public void OnlyTheTwoControlsArePresenters()
    {
        var presenters = typeof(DocumentPresenter).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(DocumentPresenter)))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

        Assert.Equal([typeof(RichTextView), typeof(RichTextViewer)], presenters);
    }
}
