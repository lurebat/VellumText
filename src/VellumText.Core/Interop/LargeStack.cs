using System.Runtime.ExceptionServices;

namespace VellumText;

/// <summary>
/// Runs import work on a thread with a stack large enough for the parsers it calls.
/// </summary>
/// <remarks>
/// <para>
/// The importers hand untrusted input to parsers this library does not own — AngleSharp,
/// AngleSharp.Css, Markdig, RtfPipe — and several of them recurse once per level of nesting in the
/// input. On an ordinary 1 MiB stack a few kilobytes are enough to overflow it: 3.3 KB of nested
/// <c>calc(</c> in a style attribute, 50 KB of nested <c>template</c>. A stack overflow cannot be
/// caught; it ends the process, so for a clipboard manager every copy is a chance to die.
/// </para>
/// <para>
/// The guard lives here, inside the importers, rather than in the editor, because the importers
/// are public and a host that calls one directly from a thread-pool thread deserves the same
/// protection as a paste. Pre-parse limits on nesting (see <c>HtmlImportOptions</c>) bound the
/// common shapes; this is what stands behind them for every shape nobody has thought of yet.
/// </para>
/// <para>
/// One thread per call, not a reused worker: a single worker would serialise concurrent imports
/// and would deadlock the moment one import calls another (Markdown and RTF both finish in the
/// HTML importer). The stack is reserved address space, not committed memory, so the cost of a
/// call is a thread start — tens of microseconds against an import that parses HTML. A call made
/// on a thread this class started runs inline.
/// </para>
/// </remarks>
internal static class LargeStack
{
    /// <summary>The stack reserved for import work.</summary>
    /// <remarks>
    /// 256 MiB is about 250 times what overflowed in the cases above. A 32-bit process has 2 GiB
    /// of address space in total, so it gets less.
    /// </remarks>
    internal static readonly int StackBytes = Environment.Is64BitProcess ? 256 * 1024 * 1024 : 16 * 1024 * 1024;

    [ThreadStatic]
    private static bool _onLargeStack;

    /// <summary>Whether the calling thread is one this class started.</summary>
    internal static bool IsCurrentThreadLarge => _onLargeStack;

    /// <summary>Runs <paramref name="work"/> on a large stack and returns its result.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="work">The work. Exceptions it throws are rethrown here with their stack.</param>
    /// <returns>What <paramref name="work"/> returned.</returns>
    internal static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (_onLargeStack)
        {
            return work();
        }

        T result = default!;
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(
            () =>
            {
                _onLargeStack = true;

                try
                {
                    result = work();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            },
            StackBytes)
        {
            IsBackground = true,
            Name = "VellumText import",
        };

        thread.Start();
        thread.Join();

        failure?.Throw();

        return result;
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a large stack without blocking the caller, and completes
    /// with its result.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="work">The work. An exception it throws faults the task.</param>
    /// <param name="cancellationToken">
    /// Cancels the wait, not the work: a parser has no safe point to stop at, so a cancelled
    /// import finishes on its own thread and its result is discarded.
    /// </param>
    /// <returns>A task that completes with what <paramref name="work"/> returned.</returns>
    /// <remarks>
    /// The asynchronous half of <see cref="Run{T}"/>, which every public importer is built on: the
    /// thread is started and the caller gets a task back at once, rather than parking a thread of
    /// its own on <see cref="Thread.Join()"/> for the length of the parse. A call made on a thread
    /// this class started runs inline, as it does in <see cref="Run{T}"/>.
    /// </remarks>
    internal static Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        if (_onLargeStack)
        {
            try
            {
                return Task.FromResult(work());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        var thread = new Thread(
            () =>
            {
                _onLargeStack = true;

                try
                {
                    completion.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    registration.Dispose();
                }
            },
            StackBytes)
        {
            IsBackground = true,
            Name = "VellumText import",
        };

        thread.Start();

        return completion.Task;
    }
}
