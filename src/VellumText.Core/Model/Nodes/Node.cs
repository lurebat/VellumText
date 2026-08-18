using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace VellumText;

/// <summary>
/// A node in the block-level document tree.
/// </summary>
/// <remarks>
/// <para>
/// The block level is a typed tree because tables, list nesting and blockquotes are
/// genuinely structural. The inline level inside a paragraph is deliberately not a tree —
/// see <see cref="InlineContent"/>.
/// </para>
/// <para>
/// Nodes are immutable and compare structurally. Structural equality is what the undo
/// round-trip property test rests on, and it has to be written by hand: records compare
/// <see cref="ImmutableArray{T}"/> fields by reference, which would make two identical
/// documents compare unequal.
/// </para>
/// </remarks>
public abstract class Node : IEquatable<Node>
{
    /// <remarks>
    /// <c>private protected</c> closes the hierarchy. Every consumer that walks a document —
    /// the exporters, the JSON writer, the presenter's block factory — switches on the concrete
    /// node types and throws on anything else, so a node type written outside this assembly would
    /// compile and then fail the first time it was rendered or saved. That is worse than no
    /// extensibility, because it fails at run time in the host's application rather than at
    /// compile time in ours.
    /// </remarks>
    private protected Node()
    {
    }
    /// <summary>
    /// How many children a lookup has to walk past before this node earns a size index.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A floor below which an index cannot pay, not a tuned knob. One cold lookup at the last
    /// child of a node costs the same 276 ns at 32 children whether it scans or builds and
    /// searches an index; below that the <c>int[]</c> is pure loss, and most nodes are a table
    /// row of three cells or a list of five items, while a document of 50,000 paragraphs has
    /// exactly one node that is not.
    /// </para>
    /// <para>
    /// What the measurement actually settles is that the trigger must not be zero. Indexing on
    /// the first lookup instead made resolving at the <em>start</em> of a document linear in its
    /// size — 0.011 ms and 17 KB, flat across sizes, became 0.140 ms and 81 KB at 16,000 blocks —
    /// which is this defect again, moved to the cheap end. See <see cref="ChildIndexAt"/>.
    /// </para>
    /// </remarks>
    private const int IndexAfterScanning = 32;

    /// <summary>
    /// Where each child ends, cumulatively — or null until a lookup here has proven expensive.
    /// </summary>
    /// <remarks>
    /// Strictly increasing, because every node occupies at least one position, which is what
    /// makes a binary search over it unambiguous.
    /// </remarks>
    private int[]? _childEnds;

    /// <summary>
    /// The number of positions inside this node, excluding its own boundary tokens.
    /// </summary>
    /// <remarks>
    /// Each character counts 1, each leaf node counts 1, and each non-leaf child counts its
    /// content plus the 2 positions of its open and close boundaries.
    /// </remarks>
    public abstract int ContentSize { get; }

    /// <summary>Whether this node has no interior at all — a rule or a block image.</summary>
    public abstract bool IsLeaf { get; }

    /// <summary>
    /// The number of positions this node occupies in its parent: 1 for a leaf, otherwise its
    /// content plus its two boundary tokens.
    /// </summary>
    public int NodeSize => IsLeaf ? 1 : ContentSize + 2;

    /// <summary>The child nodes, empty for paragraphs and leaves.</summary>
    public abstract ImmutableArray<Node> Children { get; }

    /// <summary>A short name used in diagnostics and validation messages.</summary>
    public abstract string TypeName { get; }

    /// <summary>Returns this node with different children.</summary>
    /// <param name="children">The replacement children.</param>
    /// <exception cref="ArgumentException">A child has a type this node cannot hold.</exception>
    /// <exception cref="InvalidOperationException">This node cannot hold children at all.</exception>
    public abstract Node WithChildren(IReadOnlyList<Node> children);

    /// <summary>
    /// Finds the child covering an offset into this node's content.
    /// </summary>
    /// <param name="offset">An offset from the start of this node's content.</param>
    /// <param name="start">Set to the offset at which the returned child starts.</param>
    /// <returns>
    /// The child whose half-open extent <c>[start, start + NodeSize)</c> contains
    /// <paramref name="offset"/>, or <see cref="ImmutableArray{T}.Length"/> of
    /// <see cref="Children"/> when the offset is at or past the end of the content — the same
    /// answer a position landing exactly on a boundary has always given.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Resolving a position is this lookup once per level, so before there was an index the cost
    /// of finding a position depended on where in the document it was: typing at the end of
    /// 50,000 paragraphs walked all 50,000, and typing at the start walked one. Typing at the end
    /// is what writing is, so that is the case the budget has to hold for.
    /// </para>
    /// <para>
    /// <b>A plain prefix-sum array, not a Fenwick tree.</b> <c>BlockHeightIndex</c> in the
    /// view layer needs a Fenwick tree because a block's height changes under it and the index
    /// has to absorb that in logarithmic time. Children never change: a node with different
    /// children is a different node, with its own index. So the structure that fits is the
    /// simplest one that answers the question — cumulative ends, binary searched — and the
    /// update path a Fenwick tree buys would never be taken.
    /// </para>
    /// <para>
    /// <b>Built on demand, after a scan has proven expensive, rather than in the constructor.</b>
    /// Building it eagerly would put an <c>int[]</c> per node on the edit path, where
    /// <see cref="WithChildren"/> rebuilds the root on every keystroke — and it would charge that
    /// to documents that never resolve anything near their end. Building it on the first lookup
    /// instead would charge a full pass to <em>cheap</em> lookups, which is a straight loss for a
    /// caret at the start of a document. So the trigger is the work actually done: a scan that
    /// walked past <see cref="IndexAfterScanning"/> children says this node is one where finding
    /// a child is expensive, and the next lookup in it will not be.
    /// </para>
    /// <para>
    /// The index cannot go stale, because there is nothing for it to go stale against: it is
    /// derived from children that are fixed at construction. Two threads racing here compute the
    /// same array from the same children, so the loser's work is wasted and nothing else. The
    /// volatile write is what makes the array's contents visible to a reader that sees the
    /// reference.
    /// </para>
    /// </remarks>
    internal int ChildIndexAt(int offset, out int start)
    {
        if (Volatile.Read(ref _childEnds) is { } ends)
        {
            return Search(ends, offset, out start);
        }

        var children = Children;
        var consumed = 0;
        var index = 0;

        while (index < children.Length)
        {
            if (index == IndexAfterScanning)
            {
                // The walk has gone far enough to prove this is a node where finding a child is
                // expensive. Building the index costs one pass over the children, which is no
                // more than finishing the walk would have, and it is the last such pass this node
                // will ever need — so stop walking here rather than paying for both.
                return Search(BuildChildEnds(children), offset, out start);
            }

            var size = children[index].NodeSize;

            if (consumed + size > offset)
            {
                break;
            }

            consumed += size;
            index++;
        }

        start = consumed;

        return index;
    }

    /// <inheritdoc/>
    public bool Equals(Node? other) =>
        ReferenceEquals(this, other) || (other is not null && GetType() == other.GetType() && EqualsCore(other));

    /// <inheritdoc/>
    public sealed override bool Equals(object? obj) => Equals(obj as Node);

    /// <inheritdoc/>
    public abstract override int GetHashCode();

    /// <summary>
    /// Compares against a node already known to be of the same runtime type.
    /// </summary>
    /// <param name="other">The node to compare against.</param>
    protected abstract bool EqualsCore(Node other);

    /// <summary>Records where each child ends, for <see cref="ChildIndexAt"/> to search.</summary>
    /// <param name="children">This node's children, already read once by the caller.</param>
    /// <returns>The cumulative ends, so the caller can search them without re-reading the field.</returns>
    private int[] BuildChildEnds(ImmutableArray<Node> children)
    {
        var ends = new int[children.Length];
        var total = 0;

        for (var i = 0; i < children.Length; i++)
        {
            total += children[i].NodeSize;
            ends[i] = total;
        }

        Volatile.Write(ref _childEnds, ends);

        return ends;
    }

    /// <summary>Finds the child covering an offset in an already-built index.</summary>
    /// <param name="ends">Cumulative child ends, strictly increasing.</param>
    /// <param name="offset">An offset from the start of this node's content.</param>
    /// <param name="start">Set to the offset at which the returned child starts.</param>
    private static int Search(int[] ends, int offset, out int start)
    {
        // An exact hit is the boundary *after* that child, so the position belongs to the next
        // one. A miss gives the first child ending past the offset, which is the one containing
        // it. Both cases fall out of the same expression, which is why the bitwise complement is
        // not unwrapped into a branch.
        var found = Array.BinarySearch(ends, offset);
        var at = found >= 0 ? found + 1 : ~found;

        start = at == 0 ? 0 : ends[at - 1];

        return at;
    }

    /// <summary>Compares two child lists element by element.</summary>
    /// <param name="left">The first list.</param>
    /// <param name="right">The second list.</param>
    protected static bool ChildrenEqual<T>(ImmutableArray<T> left, ImmutableArray<T> right)
        where T : Node
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Folds a child list into a hash.</summary>
    /// <param name="hash">The hash to add to.</param>
    /// <param name="children">The children to fold in.</param>
    protected static void HashChildren<T>(ref HashCode hash, ImmutableArray<T> children)
        where T : Node
    {
        hash.Add(children.Length);

        foreach (var child in children)
        {
            hash.Add(child);
        }
    }

    /// <summary>Sums the sizes the children occupy in this node.</summary>
    /// <param name="children">The children to measure.</param>
    protected static int SumOfNodeSizes<T>(ImmutableArray<T> children)
        where T : Node
    {
        var total = 0;

        foreach (var child in children)
        {
            total += child.NodeSize;
        }

        return total;
    }

    /// <summary>
    /// Copies <paramref name="children"/> into a typed array, rejecting anything of the
    /// wrong type.
    /// </summary>
    /// <typeparam name="T">The child type this node accepts.</typeparam>
    /// <param name="children">The children to check.</param>
    /// <param name="parameterName">The parameter to blame in the exception.</param>
    /// <exception cref="ArgumentException">A child is null or of the wrong type.</exception>
    /// <remarks>
    /// <c>MoveToImmutable</c> rather than <c>ToImmutable</c>: the builder is created at exactly
    /// the final count and filled to it, so moving hands over the array it already has where
    /// <c>ToImmutable</c> copies it into a second one. Every <see cref="WithChildren"/> goes
    /// through here, and at 50,000 blocks that copy is a 400 KB large-object allocation per
    /// rebuild, several of which happen on every keystroke.
    /// </remarks>
    protected static ImmutableArray<T> RequireAll<T>(
        IReadOnlyList<Node> children, string parameterName)
        where T : Node
    {
        ArgumentNullException.ThrowIfNull(children);

        var builder = ImmutableArray.CreateBuilder<T>(children.Count);

        foreach (var child in children)
        {
            if (child is not T typed)
            {
                throw new ArgumentException(
                    $"Expected {typeof(T).Name} but found {child?.TypeName ?? "null"}.",
                    parameterName);
            }

            builder.Add(typed);
        }

        return builder.MoveToImmutable();
    }
}

/// <summary>A node that can appear where block content is expected.</summary>
/// <remarks>
/// Needs its own <c>private protected</c> constructor: inheriting one does not make it callable,
/// but the implicit public parameterless constructor the compiler would otherwise supply here
/// would reopen what <see cref="Node"/> closed.
/// </remarks>
public abstract class BlockNode : Node
{
    private protected BlockNode()
    {
    }
}

/// <summary>Guards for the enum-valued attributes nodes carry.</summary>
/// <remarks>
/// A cast is not a check. <c>(TextAlign)999</c> compiles, so an attribute set by a caller,
/// deserialized from a file, or read back from an older version can be a value no renderer has
/// a case for. Node constructors enforce their own attributes for the same reason they enforce
/// child types: it is the only place that cannot be bypassed (architecture §7).
/// </remarks>
internal static class NodeAttr
{
    /// <summary>Throws unless <paramref name="value"/> is a named member of its enum.</summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="name">The parameter being checked, supplied by the compiler.</param>
    internal static void Require<T>(
        T value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                name, value, $"{typeof(T).Name} has no member with this value.");
        }
    }
}
