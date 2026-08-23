namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The sections of the settings panel, in the order they are listed.
/// </summary>
public enum SettingsSection
{
    /// <summary>Theme, and the type it is rendered in.</summary>
    Appearance,

    /// <summary>Where models are looked for, and how cloud providers are reached.</summary>
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

    /// <summary>The Python environment and the mesh node.</summary>
    Runtime,

    /// <summary>Extensions registered against the open project.</summary>
    Extensions,

    /// <summary>What the record of past runs is keeping, and what it is costing.</summary>
    History,

    /// <summary>The values a newly added node starts from.</summary>
    Behaviour
}
