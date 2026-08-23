namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The sections of the settings panel, in the order they are listed.
/// </summary>
public enum SettingsSection
{
    /// <summary>Theme, and the type it is rendered in.</summary>
    Appearance,

    /// <summary>Where models are looked for on this machine.</summary>
    /// <remarks>
    /// Finding models, and nothing else. How a hosted provider is reached used to be here too and
    /// went to <see cref="ApiKeys"/> with the keys, because a provider without its key is not a
    /// setting anybody can act on.
    /// </remarks>
    Models,

    /// <summary>Every credential this installation holds, grouped by what it is for.</summary>
    /// <remarks>
    /// Its own section rather than a heading under Models, because a search key is not a model
    /// setting and the next kind of key will not be either. Grouping them by what they are is what
    /// makes the second one findable.
    /// </remarks>
    ApiKeys,

    /// <summary>
    /// The open project: what it is, where its generated code goes, and what is known about it.
    /// </summary>
    /// <remarks>
    /// Called Unity until v1.45, which stopped being accurate in v1.37 when a project stopped
    /// having to be one. It is where the per project settings belong rather than a second section
    /// beside it saying almost the same thing: somebody looking for what this application thinks
    /// about their project looks in one place.
    /// </remarks>
    Project,

    /// <summary>What this installation runs: the Python environment, the mesh node and the tool call server.</summary>
    /// <remarks>
    /// Grouped by whose they are rather than by what they do. Each is a thing this install turns on
    /// and off for itself, which is what makes them one section and what kept the tool call switch
    /// out of <see cref="Project"/>, where it was an install wide setting under a project heading.
    /// </remarks>
    Runtime,

    /// <summary>Extensions registered against the open project.</summary>
    Extensions,

    /// <summary>What the record of past runs is keeping, and what it is costing.</summary>
    History,

    /// <summary>The values a newly added node starts from.</summary>
    Behaviour
}
