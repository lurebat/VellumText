namespace VellumText.Tests.Interop.Html;

/// <summary>
/// The tests that measure <see cref="GC.GetTotalAllocatedBytes(bool)"/>, run with nothing alongside.
/// </summary>
/// <remarks>
/// They need the process-wide count because the work they measure continues on other threads,
/// and that count also sees every test xUnit is running in parallel with them: a refusal that
/// allocates a few megabytes was reported as 22 MB while other classes were busy. xUnit runs a
/// collection with parallelization disabled on its own, after the parallel ones have finished.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideAllocation
{
    /// <summary>The collection's name, for <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "Process-wide allocation";
}