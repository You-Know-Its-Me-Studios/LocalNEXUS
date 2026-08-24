using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.ViewModels.Network;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Ordering the network table when it holds more than one kind of row.
/// </summary>
/// <remarks>
/// One table shows models served by the mesh and meshes found in the directory, and each row
/// answers for a column out of its own data. The key comes back as IComparable, so nothing at
/// compile time checks that two kinds of row agree on what a column holds, and when they did not
/// the sort compared an int with a string and threw.
///
/// It threw where nobody was looking for it. The table is sorted by name almost all of the time,
/// and the two columns that disagreed were coverage and context, so it only happened when a mesh
/// was discovered while the table happened to be sorted one of those two ways. What reached the
/// user was a dialog about an unexpected error and a crash report on the next launch, for a table
/// that was only in the wrong order.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class NetworkSortTests
{
    private static DiscoveredMeshRow Mesh() => new(new DiscoveredMesh(
        Name: "a mesh",
        NodeCount: 3,
        CapacityGb: 24d,
        Serving: new[] { "qwen" },
        Wanted: Array.Empty<string>(),
        OnDisk: Array.Empty<string>(),
        Score: 1,
        Freshness: "now",
        ClientCount: 0));

    private static NetworkModelRow Model() => new(new NetworkServedModel("owner/model.gguf"), () => false);

    /// <summary>
    /// Every column answers with one type, whichever kind of row is asked.
    /// </summary>
    /// <remarks>
    /// This is the invariant that was broken, stated directly. A row with nothing to say for a
    /// column still has to say it in the column's type, because the alternative is a key that
    /// sorts against its neighbours by luck.
    /// </remarks>
    [Fact]
    public void EveryColumnKeepsOneKeyTypeAcrossRowKinds()
    {
        var mesh = Mesh();
        var model = Model();

        foreach (var column in Enum.GetValues<ModelColumn>())
        {
            var fromMesh = mesh.SortKey(column);
            var fromModel = model.SortKey(column);

            Assert.NotNull(fromMesh);
            Assert.NotNull(fromModel);

            Assert.True(
                fromMesh.GetType() == fromModel.GetType(),
                $"{column} is {fromMesh.GetType().Name} on a mesh row and "
                + $"{fromModel.GetType().Name} on a model row, so sorting a table holding both "
                + "compares two types that cannot be compared");
        }
    }

    /// <summary>
    /// Sorting a table holding both kinds of row does not throw, on any column.
    /// </summary>
    /// <remarks>
    /// The test above says the rows agree. This one says the sort survives them not agreeing,
    /// because the first is a rule somebody has to keep and the second is what happens when they
    /// do not. A table in a strange order is a nuisance; a dialog and a crash report is not.
    /// </remarks>
    [Fact]
    public void SortingAMixedTableNeverThrows()
    {
        var rows = new INetworkRow[] { Mesh(), Model(), Mesh() };

        foreach (var column in Enum.GetValues<ModelColumn>())
        {
            var ascending = rows.OrderBy(r => r.SortKey(column), RowKeyComparer.Instance).ToList();
            var descending = rows.OrderByDescending(r => r.SortKey(column), RowKeyComparer.Instance).ToList();

            Assert.Equal(rows.Length, ascending.Count);
            Assert.Equal(rows.Length, descending.Count);
        }
    }

    /// <summary>Keys of different types are ordered rather than thrown over.</summary>
    [Fact]
    public void TheComparerOrdersKeysItCannotReallyCompare()
    {
        var comparer = RowKeyComparer.Instance;

        // The exact pairing that threw: a count against a name.
        var mixed = comparer.Compare(3, "a mesh");

        Assert.Equal(-mixed, comparer.Compare("a mesh", 3));
        Assert.Equal(0, comparer.Compare(null, null));
        Assert.True(comparer.Compare(null, 1) < 0);
        Assert.True(comparer.Compare(1, null) > 0);
        Assert.True(comparer.Compare(1, 2) < 0);
        Assert.True(comparer.Compare("a", "b") < 0);
    }
}
