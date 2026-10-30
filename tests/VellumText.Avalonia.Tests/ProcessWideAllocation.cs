namespace VellumText.Tests.Avalonia;

/// <summary>
/// Tests that measure allocation across the whole process, which other tests running at the same
/// time would add to.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideAllocation
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Process-wide allocation";
}