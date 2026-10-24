namespace ATProtoNet.Repo;

// Walks a tree in key order, one item at a time: a key and its value, or a subtree the caller may
// descend into or step over. Stepping over subtrees whose CIDs match is what keeps a diff
// proportional to the change rather than to the tree.
internal sealed class MstWalker
{
    private readonly List<(MstNode Node, int Position)> _path = [];

    public MstWalker(MstNode root) => Tree = root;

    public bool Done { get; private set; }

    // The current subtree, or null when the current item is a key.
    public MstNode? Tree { get; private set; }

    public byte[] Key { get; private set; } = [];

    public Cid Value { get; private set; } = null!;

    // The key right after the current subtree in its node, when it has one: every key below sorts before it.
    public byte[]? Bound { get; private set; }

    public void Descend()
    {
        _path.Add((Tree!, 0));
        Settle();
    }

    public void Advance()
    {
        if (_path.Count == 0)
        {
            Done = true;
            return;
        }
        var (node, position) = _path[^1];
        _path[^1] = (node, position + 1);
        Settle();
    }

    // Positions alternate: 2i is the subtree before key i, 2i + 1 is key i.
    private void Settle()
    {
        while (_path.Count > 0)
        {
            var (node, position) = _path[^1];
            var (keys, values, subtrees) = node.Content;
            if (position > 2 * keys.Length)
            {
                _path.RemoveAt(_path.Count - 1);
                if (_path.Count > 0) _path[^1] = (_path[^1].Node, _path[^1].Position + 1);
                continue;
            }
            var i = position / 2;
            if (position % 2 == 1)
            {
                (Tree, Key, Value) = (null, keys[i], values[i]);
                return;
            }
            if (subtrees[i] is { } subtree)
            {
                (Tree, Bound) = (subtree, i < keys.Length ? keys[i] : null);
                return;
            }
            _path[^1] = (node, position + 1);
        }
        Done = true;
    }

    // The keys whose values differ between two trees, with the value before and after (null when absent).
    public static IEnumerable<(byte[] Key, Cid? Before, Cid? After)> Diff(MstNode before, MstNode after)
    {
        MstWalker b = new(before), a = new(after);
        while (!a.Done || !b.Done)
        {
            if (a.Done || b.Done || a.Tree is not null || b.Tree is not null)
            {
                if (!a.Done && !b.Done && a.Tree is { } at && b.Tree is { } bt)
                {
                    if (at.Cid == bt.Cid)
                    {
                        a.Advance();
                        b.Advance();
                    }
                    else
                    {
                        if (at.Layer >= bt.Layer) a.Descend();
                        if (bt.Layer >= at.Layer) b.Descend();
                    }
                }
                else if (!a.Done && a.Tree is not null) a.Descend();
                else if (!b.Done && b.Tree is not null) b.Descend();
                else if (a.Done)
                {
                    yield return (b.Key, b.Value, null);
                    b.Advance();
                }
                else
                {
                    yield return (a.Key, null, a.Value);
                    a.Advance();
                }
                continue;
            }
            var order = a.Key.AsSpan().SequenceCompareTo(b.Key);
            if (order == 0)
            {
                if (a.Value != b.Value) yield return (a.Key, b.Value, a.Value);
                a.Advance();
                b.Advance();
            }
            else if (order < 0)
            {
                yield return (a.Key, null, a.Value);
                a.Advance();
            }
            else
            {
                yield return (b.Key, b.Value, null);
                b.Advance();
            }
        }
    }
}
