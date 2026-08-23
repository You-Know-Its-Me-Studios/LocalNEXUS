using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// How far a mesh this machine has joined has got.
/// </summary>
public enum JoinState
{
    /// <summary>The invite is held and the node is not running, so nothing is happening.</summary>
    NodeStopped,

    /// <summary>The node process has been started and has not answered its own console yet.</summary>
    StartingNode,

    /// <summary>The node is up and has not attached to the mesh yet.</summary>
    ReachingMesh,

    /// <summary>Attached, and the runtime is bringing models up.</summary>
    LoadingModels,

    /// <summary>Attached, with models ready to answer.</summary>
    Ready,

    /// <summary>The node tried and failed.</summary>
    Failed
}

/// <summary>
/// One mesh this machine has an invite to.
/// </summary>
/// <remarks>
/// A list rather than a single membership, because the engine takes a repeated join argument and a
/// machine can be in several meshes at once. Modelling it as one token meant joining a second mesh
/// silently replaced the first.
///
/// The name is the one recorded when it was joined. Most meshes in the directory have no name of
/// their own, so nothing else will ever supply one, and the alternative is a row that says nothing
/// about which mesh it is.
/// </remarks>
public sealed partial class JoinedMesh : ObservableObject
{
    public JoinedMesh(string name, string token, DateTimeOffset joinedAt)
    {
        Name = name;
        Token = token;
        JoinedAt = joinedAt;
    }

    /// <summary>What it was called when it was joined.</summary>
    public string Name { get; }

    /// <summary>The invite, which is what actually joins it and is never shown in full.</summary>
    public string Token { get; }

    /// <summary>When this machine joined it.</summary>
    public DateTimeOffset JoinedAt { get; }

    /// <summary>How far it has got.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateDetail))]
    private JoinState _state = JoinState.NodeStopped;

    /// <summary>
    /// That state in words, naming the part of joining that is happening.
    /// </summary>
    /// <remarks>
    /// Every one of these is read from something the engine reports rather than from a timer, so
    /// the row moves when the node moves. It said "connecting" for the whole of it, which covers
    /// starting a process, finding a mesh over the network and loading models off disk, and those
    /// take wildly different amounts of time and fail for entirely different reasons.
    /// </remarks>
    public string StateText => State switch
    {
        JoinState.Ready => "in it, models ready",
        JoinState.LoadingModels => "loading models",
        JoinState.ReachingMesh => "reaching the mesh",
        JoinState.StartingNode => "starting the node",
        JoinState.Failed => "failed",
        _ => "node stopped"
    };

    /// <summary>What that state means, for anybody wondering whether to keep waiting.</summary>
    public string StateDetail => State switch
    {
        JoinState.Ready => "The mesh is attached and its models can answer.",
        JoinState.LoadingModels => "Attached. The runtime is bringing models up, which is the slow part and is disk bound.",
        JoinState.ReachingMesh => "The node is up and looking for this mesh over the network. It is not attached yet.",
        JoinState.StartingNode => "The node process has started and has not answered its own console yet. This takes a second or two.",
        JoinState.Failed => "The node stopped with an error. What it said is under This machine.",
        _ => "The invite is saved and the node is not running, so nothing is connected. Start the node."
    };

    /// <summary>What to show for a mesh that never named itself.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "unnamed mesh" : Name;

    /// <summary>
    /// The mesh's own identifier, read out of the invite.
    /// </summary>
    /// <remarks>
    /// The one thing that tells two unnamed meshes apart, and it is already in the token: an invite
    /// is base64 json carrying the mesh id and the addresses to reach it. Only the id is read, and
    /// only the front of it is shown, because the rest is a fingerprint nobody reads across.
    /// </remarks>
    public string ShortId
    {
        get
        {
            if (ReadId() is not { Length: > 0 } id)
            {
                return "not readable";
            }

            return id.Length <= 12 ? id : id[..12];
        }
    }

    /// <summary>When it was joined, as a person reads it.</summary>
    public string JoinedText => JoinedAt.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    private string? ReadId()
    {
        try
        {
            // Base64 without padding is what the engine emits, so it is put back before decoding.
            var padded = Token.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            return JsonDocument.Parse(json).RootElement.TryGetProperty("id", out var id)
                   && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        {
            // A token this cannot read still joins perfectly well, because the engine reads it and
            // this does not. Only the row's subtitle is lost.
            return null;
        }
    }
}
