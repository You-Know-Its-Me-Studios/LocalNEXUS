namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The tabs of the bottom panel, in the order they are shown.
/// </summary>
/// <remarks>
/// Two tabs answering two different questions: what is wrong with the code, and what happened
/// during the run. There was a third, Output, and it was the second one over again with every
/// body already open. That is a setting on a view rather than a view of its own, and having it as
/// a tab made somebody decide which of the two held the thing they wanted before they could go
/// and look for it.
/// </remarks>
public enum BottomPanelTab
{
    /// <summary>Compiler diagnostics from the compile check nodes in the graph.</summary>
    Problems,

    /// <summary>The streaming run transcript.</summary>
    Activity
}
