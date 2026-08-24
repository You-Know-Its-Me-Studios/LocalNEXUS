using System.IO;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Every node this build offers is named in the documentation that lists nodes.
/// </summary>
/// <remarks>
/// The node table in the readme was written when there were six node types and was never revisited
/// while six more were added, so the two that arrived most recently were the two nobody could read
/// about. Nothing failed when that happened, which is why it went unnoticed for so long.
///
/// This is the cheapest guard that would have caught it: the picker's own list against the table,
/// so adding a node and not documenting it fails here rather than quietly shipping.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class DocumentedNodeTests
{
    /// <summary>
    /// The repository root, found by walking up from the test binary to the solution file.
    /// </summary>
    /// <remarks>
    /// The solution rather than the readme, because there is a readme under tests as well and
    /// walking up to the first one found stops one directory short. The test runs from bin, and
    /// how deep that is has changed with target frameworks before, so it is searched for rather
    /// than counted back.
    /// </remarks>
    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LocalNEXUS.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!;
    }

    [Fact]
    public void TheReadmeNodeTableNamesEveryNodeThePickerOffers()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot().FullName, "README.md"));

        var table = readme[readme.IndexOf("## Nodes", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("## ", 3, StringComparison.Ordinal)];

        var missing = NodeFactory.Descriptors
            .Select(d => d.DisplayName)
            .Where(name => !table.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"The readme's node table does not mention: {string.Join(", ", missing)}. "
            + "A node somebody cannot read about is a node they will not find.");
    }

    /// <summary>The architecture note lists the node classes, and it drifted the same way.</summary>
    [Fact]
    public void TheArchitectureNoteNamesEveryNodeClass()
    {
        var architecture = File.ReadAllText(
            Path.Combine(RepositoryRoot().FullName, "docs", "architecture.md"));

        var missing = NodeFactory.Descriptors
            .Select(d => d.TypeKey + "Node")
            .Where(name => !architecture.Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"docs/architecture.md does not list: {string.Join(", ", missing)}.");
    }
}
