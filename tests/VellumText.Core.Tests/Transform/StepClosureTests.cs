namespace VellumText.Tests.Transform;

/// <summary>
/// Closing the step hierarchy (see <c>ClosedHierarchyTests</c>) closed it to hosts only: every
/// built-in step is still a public, sealed type a host can construct.
/// </summary>
public class StepClosureTests
{
    private static readonly Type[] BuiltInSteps =
        [.. typeof(Step).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Step)))];

    [Fact]
    public void EveryBuiltInStepIsPublicAndSealed()
    {
        Assert.Contains(typeof(ReplaceTextStep), BuiltInSteps);
        Assert.Contains(typeof(ReplaceStep), BuiltInSteps);

        Assert.All(BuiltInSteps, t =>
        {
            Assert.True(t.IsPublic, $"{t.Name} is not public");
            Assert.True(t.IsSealed, $"{t.Name} is not sealed");
        });
    }

    [Fact]
    public void TextReplacementIsPublic() => Assert.True(typeof(TextReplacement).IsPublic);
}
