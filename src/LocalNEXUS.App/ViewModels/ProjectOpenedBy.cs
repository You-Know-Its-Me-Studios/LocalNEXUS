namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// Who opened a project, which decides whether anything is put in front of anybody.
/// </summary>
/// <remarks>
/// Everything opening a project does is the same either way but one step. A modal window appearing
/// because something on the machine made a tool call is worse than the gap it would close: nobody
/// asked for it, nobody is necessarily at the screen, and it lands on top of whatever they were
/// doing. So a tool opens a project with its defaults and the questions wait for a person.
/// </remarks>
public enum ProjectOpenedBy
{
    /// <summary>Somebody at the window, through the front door or the File menu.</summary>
    Person,

    /// <summary>A tool call, over MCP, with nobody necessarily watching.</summary>
    Tool
}
