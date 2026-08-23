namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// A mesh somebody else is running, as the public directory describes it.
/// </summary>
/// <remarks>
/// Not a mesh this install is in, and not one it can see anything inside. Everything here is what
/// the listing says about itself, which is enough to decide whether it is worth joining and not
/// enough to pretend the models are usable. A model listed by a mesh you have not joined is a
/// model you could reach, not one you can reach.
///
/// The invite token is deliberately not part of this. The directory prints it truncated for
/// display, so what is listed cannot be joined with, and the full one is fetched at the moment
/// somebody asks to join. Carrying a token that does not work would be worse than carrying none.
/// </remarks>
/// <param name="Name">What the mesh calls itself, or empty when it has not named itself.</param>
/// <param name="NodeCount">How many machines it reports.</param>
/// <param name="CapacityGb">Total memory it reports across those machines.</param>
/// <param name="Serving">Models it says it is serving right now.</param>
/// <param name="Wanted">Models it says it wants, which is what somebody joining could contribute.</param>
/// <param name="OnDisk">Models its machines hold but are not serving.</param>
/// <param name="Score">The directory's own ranking, higher being better placed.</param>
/// <param name="Freshness">How recently it was heard from, in the directory's own words.</param>
/// <param name="ClientCount">How many consumers are already using it.</param>
public sealed record DiscoveredMesh(
    string Name,
    int NodeCount,
    double CapacityGb,
    IReadOnlyList<string> Serving,
    IReadOnlyList<string> Wanted,
    IReadOnlyList<string> OnDisk,
    int Score,
    string Freshness,
    int ClientCount)
{
    /// <summary>What to show when the mesh did not name itself.</summary>
    public const string Unnamed = "unnamed mesh";

    /// <summary>True when the mesh named itself, which is what makes it addressable by name.</summary>
    public bool HasName => Name.Length > 0;

    /// <summary>What to call it on screen.</summary>
    public string DisplayName => HasName ? Name : Unnamed;

    /// <summary>One line naming what it holds, for a row that has one line to say it in.</summary>
    public string ServingText => Serving.Count == 0
        ? "no models loaded"
        : string.Join(", ", Serving.Select(ShortModelName));

    /// <summary>How big it is, in one line.</summary>
    public string SizeSummary
        => $"{NodeCount} {(NodeCount == 1 ? "machine" : "machines")}, {CapacityText} between them, "
           + (ClientCount == 0 ? "nobody using it yet." : $"{ClientCount} already using it.");

    /// <summary>Its size as a person reads it.</summary>
    public string CapacityText => CapacityGb > 0 ? $"{CapacityGb:0.#} GB" : "not reported";

    /// <summary>
    /// The tail of a model reference, which is the part that identifies it.
    /// </summary>
    /// <remarks>
    /// A directory listing is full of things like unsloth/Qwen3-8B-GGUF@main:Q4_K_M and absolute
    /// paths from somebody else's machine. The publisher and the branch are noise in a table.
    /// </remarks>
    public static string ShortModelName(string reference)
    {
        var text = reference.Replace('\\', '/');
        var cut = text.LastIndexOf('/');

        return cut >= 0 && cut < text.Length - 1 ? text[(cut + 1)..] : text;
    }
}
