using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Distributed;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// The mesh this machine hosts, as a line in a table of its own.
/// </summary>
/// <remarks>
/// One row, because an install hosts one mesh. It is a table anyway, for the same reason the joined
/// meshes are: the questions asked of a mesh you host are the same shape as the questions asked of
/// one you joined, and answering them in a different shape means reading two layouts to learn one
/// thing.
///
/// Everything here is about the mesh rather than about a model in it. Who can see it, who is in it,
/// what this machine is putting in, and whether the node is actually up to serve any of it.
/// </remarks>
public sealed partial class HostedMeshRow : ObservableObject
{
    private readonly MeshManager _mesh;
    private readonly Func<int> _sharedModels;
    private readonly Func<int> _members;
    private readonly Func<string> _name;
    private readonly Func<bool> _contributing;

    private readonly Func<bool> _publishWanted;

    public HostedMeshRow(
        MeshManager mesh,
        Func<string> name,
        Func<int> members,
        Func<int> sharedModels,
        Func<bool> contributing,
        Func<bool> publishWanted)
    {
        _mesh = mesh;
        _name = name;
        _members = members;
        _sharedModels = sharedModels;
        _contributing = contributing;
        _publishWanted = publishWanted;
    }

    /// <summary>What the mesh is called: what the node reports, or what it is configured as.</summary>
    /// <remarks>
    /// The node's own answer wins once it has one, because that is what anybody joining will see.
    /// Before it is up, the configured name is the truthful preview of what it will be called.
    /// </remarks>
    public string DisplayName
    {
        get
        {
            if (_mesh.IsRunning && !string.IsNullOrWhiteSpace(_mesh.MeshName))
            {
                return _mesh.MeshName;
            }

            var configured = _name();

            return string.IsNullOrWhiteSpace(configured) ? "unnamed mesh" : configured;
        }
    }

    /// <summary>The mesh's identity, which only exists once the node has created it.</summary>
    public string ShortId => _mesh.HasInviteToken
        ? new JoinedMesh(string.Empty, _mesh.InviteToken, DateTimeOffset.Now).ShortId
        : "not created yet";

    /// <summary>
    /// Who can find it, which is the one setting that leaves the local network.
    /// </summary>
    /// <remarks>
    /// The node's answer and the setting are two different things and the gap between them is
    /// exactly where somebody gets confused. Publishing is a launch argument, so ticking it and
    /// saving changes nothing until the node restarts, and the row said this network only while
    /// the switch above it said public.
    /// </remarks>
    public string VisibilityText
    {
        get
        {
            if (_mesh.PublishFailed)
            {
                return "could not be published";
            }

            if (_mesh.IsPublic)
            {
                return "anybody, listed publicly";
            }

            if (_publishWanted())
            {
                return _mesh.IsRunning
                    ? "publishing, not listed yet"
                    : "public once the node starts";
            }

            return "this network only";
        }
    }

    /// <summary>True when it is listed publicly, so the row can colour that differently.</summary>
    public bool IsPublic => _mesh.IsPublic;

    /// <summary>How many machines are in it, this one included.</summary>
    public string MembersText => _mesh.IsRunning
        ? $"{_members()} {(_members() == 1 ? "machine" : "machines")}"
        : "none until it is up";

    /// <summary>What this machine is putting into it.</summary>
    /// <remarks>
    /// Nothing shared is a coherent thing to be doing rather than a fault. A node can host a mesh
    /// purely to route for others, and saying so plainly is better than a zero somebody reads as
    /// broken.
    /// </remarks>
    public string SharingText
    {
        get
        {
            if (!_contributing())
            {
                return "not offering this machine";
            }

            var count = _sharedModels();

            return count == 0
                ? "offering the machine, no models ticked"
                : $"{count} {(count == 1 ? "model" : "models")}";
        }
    }

    /// <summary>Whether the mesh is actually up.</summary>
    public string StateText => _mesh.State switch
    {
        MeshNodeState.Serving => "hosting, serving",
        MeshNodeState.Client => "hosting, routing only",
        MeshNodeState.Starting => "starting",
        MeshNodeState.Failed => "failed",
        _ => "node stopped"
    };

    /// <summary>What that means, for the panel on the right.</summary>
    public string StateDetail => _mesh.State switch
    {
        MeshNodeState.Serving => "The mesh is up and this machine is serving models into it.",
        MeshNodeState.Client => "The mesh is up. This machine routes for it and is not serving any models of its own.",
        MeshNodeState.Starting => "The node is coming up. Its invite appears once it has created the mesh.",
        MeshNodeState.Failed => "The node stopped with an error. What it said is under This machine.",
        _ => "Nothing is hosted while the node is stopped. Start it and the mesh comes back with the same identity."
    };

    /// <summary>The colour key for the state, which is the node's own.</summary>
    public MeshNodeState State => _mesh.State;

    /// <summary>The invite, so somebody can be let in.</summary>
    public string InviteToken => _mesh.InviteToken;

    /// <summary>True once there is an invite to pass on.</summary>
    public bool HasInvite => _mesh.HasInviteToken;

    /// <summary>Re-reads everything, since all of it is derived from the node and the settings.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
