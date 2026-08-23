namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// The sortable columns of the model table, in the order they appear.
/// </summary>
/// <remarks>
/// Size, throughput and last verified are not here because sorting a column of "not reported" is
/// a control that does nothing, and a header that responds to a click by not changing anything is
/// worse than one that does not respond.
/// </remarks>
public enum ModelColumn
{
    /// <summary>Model name.</summary>
    Name,

    /// <summary>How well the network covers it, weakest first.</summary>
    Coverage,

    /// <summary>How many sources hold pieces of it.</summary>
    Sources,

    /// <summary>Spare sources behind the weakest section.</summary>
    Spare,

    /// <summary>Whether it can run right now.</summary>
    Status,

    /// <summary>Context window.</summary>
    Context,

    /// <summary>What the row is made of: models for a mesh, machines for a model.</summary>
    Contents
}
