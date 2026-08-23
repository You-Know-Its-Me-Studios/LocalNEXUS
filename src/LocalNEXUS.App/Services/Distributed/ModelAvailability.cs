namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Whether the mesh can run a model right now: the single verdict the model list, the coverage
/// chain and a model node's refusal all read.
/// </summary>
/// <remarks>
/// <see cref="Blocked"/> means the mesh is known to be unable to assemble the model, which is a
/// failure worth showing as one. A model still coming up is <see cref="Starting"/> instead,
/// because reporting a working system as failed for the seconds it takes to load is a worse lie
/// than saying nothing. Starting is first so that a value nobody has set yet reads as unknown
/// rather than as either verdict.
/// </remarks>
public enum ModelAvailability
{
    /// <summary>Coming up, or not yet reported on. No verdict either way.</summary>
    Starting,

    /// <summary>Every section is held and serving. The model can be run.</summary>
    Complete,

    /// <summary>Known to be unrunnable, with the section at fault named.</summary>
    Blocked,

    /// <summary>
    /// Somebody else's mesh, found in the directory and not joined.
    /// </summary>
    /// <remarks>
    /// Its own state rather than one of the three above, because it is not a verdict about a model
    /// at all. Reported as starting, seven meshes found in a directory made the status filter say
    /// seven were starting, which is a sentence about this machine doing work it is not doing.
    /// Appended, so nothing that reads this enum by position changes meaning.
    /// </remarks>
    NotJoined
}
