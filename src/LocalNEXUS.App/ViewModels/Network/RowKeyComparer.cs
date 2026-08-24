namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// Orders two sort keys from the network table without ever throwing.
/// </summary>
/// <remarks>
/// Rows of different kinds share one table, and each answers for a column out of its own data, so
/// the keys arrive as IComparable and nothing at compile time checks that two rows agree on what
/// a column holds. When they disagreed, the default comparison called Int32.CompareTo with a
/// string and threw inside the sort, which surfaced as a dialog and a crash report for something
/// that was only ever a table in the wrong order.
///
/// The rows are fixed so they agree, and this is here so that being wrong about it again is a
/// list in an odd order rather than a fault. Comparing the text of two keys of different types is
/// not meaningful and is not meant to be: it is defined, stable and total, which is all a sort
/// needs from a comparer.
/// </remarks>
public sealed class RowKeyComparer : IComparer<IComparable?>
{
    /// <summary>The one instance, because it holds nothing.</summary>
    public static RowKeyComparer Instance { get; } = new();

    private RowKeyComparer()
    {
    }

    /// <inheritdoc />
    public int Compare(IComparable? left, IComparable? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        // A row with nothing to say for this column sorts below one that answered.
        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (left.GetType() == right.GetType())
        {
            return left.CompareTo(right);
        }

        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
