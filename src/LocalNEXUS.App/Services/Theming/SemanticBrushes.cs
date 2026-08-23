namespace LocalNEXUS.App.Services.Theming;

/// <summary>
/// Every brush the application binds to, and which colour of the theme it takes.
/// </summary>
/// <remarks>
/// This is the semantic layer. A theme supplies about thirty colours and nothing else; this table
/// turns them into the brushes everything actually uses, so a theme never has to know what a
/// coverage level or a node state is, and adding a state is one line here rather than five lines
/// across five themes.
///
/// It is a table in code rather than a resource dictionary of brushes whose colours are dynamic
/// references, which is the obvious XAML way to express it and does not survive a theme change. A
/// dynamic reference inside a brush is resolved once, when that brush is first created, and never
/// revisited, because a brush is not an element and has nothing to be notified through; and the
/// dictionary that would hold those brushes is cached by uri, so loading a second copy to read new
/// colours from hands back the live one. Both problems disappear once the mapping is data that can
/// simply be read.
///
/// Three rules the state brushes follow, and they are the reason there is a rule at all:
///
/// In progress is never a failure. Starting, Provisioning, Loading and Checking take the info
/// colour, never the danger one. Something still coming up must never look like something broke.
///
/// Pending is quiet, not a warning. It takes the neutral colour, which is grey in every theme. A
/// node that has not run yet is not asking for attention.
///
/// Thin is a real warning and Uncovered is a real failure. One spare source left is worth saying
/// out loud; none at all is a different thing again.
/// </remarks>
public static class SemanticBrushes
{
    /// <summary>Brush key to theme colour key, in the order the file reads best.</summary>
    public static IReadOnlyList<(string Brush, string Colour)> Map { get; } = new[]
    {
        // Surfaces
        ("Surface.Window.Brush", "Surface.WindowColor"),
        ("Surface.Panel.Brush", "Surface.PanelColor"),
        ("Surface.Card.Brush", "Surface.CardColor"),
        ("Surface.Input.Brush", "Surface.InputColor"),
        ("Surface.Border.Brush", "Surface.BorderColor"),
        ("Surface.Chrome.Brush", "Surface.ChromeColor"),
        ("Surface.Hover.Brush", "Surface.HoverColor"),
        ("Surface.Selected.Brush", "Surface.SelectedColor"),
        ("Surface.Track.Brush", "Surface.TrackColor"),
        ("Surface.Scrim.Brush", "Surface.ScrimColor"),

        // Text
        ("Text.Primary.Brush", "Text.PrimaryColor"),
        ("Text.Secondary.Brush", "Text.SecondaryColor"),
        ("Text.Muted.Brush", "Text.MutedColor"),
        ("Text.Inverse.Brush", "Text.InverseColor"),

        // Accent
        ("Accent.Primary.Brush", "Accent.PrimaryColor"),
        ("Accent.Neutral.Brush", "Accent.NeutralColor"),

        // Status. The four words everything else is expressed in.
        ("Status.Success.Brush", "Status.SuccessColor"),
        ("Status.Info.Brush", "Status.InfoColor"),
        ("Status.Danger.Brush", "Status.DangerColor"),
        ("Status.Warning.Brush", "Status.WarningColor"),

        // A wire the run stops on. Red because that is what a breakpoint is everywhere else and
        // somebody arriving here already knows what it means, and it is the one red in the
        // application that does not mean something broke.
        ("Breakpoint.Marker.Brush", "Status.DangerColor"),

        // How much a file changed, in the feed.
        ("Diff.Added.Brush", "Diff.AddedColor"),
        ("Diff.Removed.Brush", "Diff.RemovedColor"),

        // Pin types.
        ("Pin.Text.Brush", "Pin.TextColor"),
        ("Pin.Code.Brush", "Pin.CodeColor"),

        // A model pin wears the model node's own colour, so the wire and the node it comes
        // from read as the same thing. No theme is edited for this: that is what this table is.
        ("Pin.Model.Brush", "NodeType.ModelColor"),

        // Node type accents. The only colour a node carries.
        ("NodeType.Prompt.Brush", "NodeType.PromptColor"),
        ("NodeType.Triage.Brush", "NodeType.TriageColor"),
        ("NodeType.Model.Brush", "NodeType.ModelColor"),
        ("NodeType.Debate.Brush", "NodeType.DebateColor"),
        ("NodeType.Judge.Brush", "NodeType.JudgeColor"),
        ("NodeType.Reshape.Brush", "NodeType.ReshapeColor"),
        ("NodeType.CompilerCheck.Brush", "NodeType.CompilerCheckColor"),
        ("NodeType.Output.Brush", "NodeType.OutputColor"),
        ("NodeType.TextOutput.Brush", "NodeType.TextOutputColor"),
        ("NodeType.Agent.Brush", "NodeType.AgentColor"),
        ("NodeType.Loop.Brush", "NodeType.LoopColor"),

        // Node execution state, as the model records it.
        ("NodeState.Pending.Brush", "Accent.NeutralColor"),
        ("NodeState.Running.Brush", "Status.InfoColor"),
        ("NodeState.Completed.Brush", "Status.SuccessColor"),
        ("NodeState.Faulted.Brush", "Status.DangerColor"),

        // Node state as it is drawn, which has one value more than the model does. A node still
        // pending when the run faulted never ran and never will, and the same quiet grey as a node
        // waiting its turn is the honest answer: it did not fail, it was not reached.
        // An artifact of a change. Blocked takes the neutral colour rather than the danger one,
        // because blocked is waiting its turn and painting it red would blame it for the artifact
        // in front of it. Same three state discipline as a node that was never reached.
        ("SpecArtifactState.Unknown.Brush", "Accent.NeutralColor"),
        ("SpecArtifactState.Done.Brush", "Status.SuccessColor"),
        ("SpecArtifactState.Ready.Brush", "Status.InfoColor"),
        ("SpecArtifactState.Blocked.Brush", "Accent.NeutralColor"),

        ("NodeDisplayState.Pending.Brush", "Accent.NeutralColor"),
        ("NodeDisplayState.Running.Brush", "Status.InfoColor"),
        ("NodeDisplayState.Completed.Brush", "Status.SuccessColor"),
        ("NodeDisplayState.Faulted.Brush", "Status.DangerColor"),
        ("NodeDisplayState.Skipped.Brush", "Accent.NeutralColor"),

        // Coverage of one section, or of a whole model.
        ("SectionCoverage.Starting.Brush", "Status.InfoColor"),
        ("SectionCoverage.Healthy.Brush", "Status.SuccessColor"),
        ("SectionCoverage.Thin.Brush", "Status.WarningColor"),
        ("SectionCoverage.Uncovered.Brush", "Status.DangerColor"),

        // What the mesh reports about a peer.
        ("SourceState.Unknown.Brush", "Accent.NeutralColor"),
        ("SourceState.Available.Brush", "Status.InfoColor"),
        ("SourceState.Serving.Brush", "Status.SuccessColor"),
        ("SourceState.Unreachable.Brush", "Status.DangerColor"),

        // This install owns exactly one mesh node, and this is its condition.
        ("MeshNodeState.Stopped.Brush", "Accent.NeutralColor"),
        ("MeshNodeState.Starting.Brush", "Status.InfoColor"),
        ("MeshNodeState.Client.Brush", "Status.InfoColor"),
        ("MeshNodeState.Serving.Brush", "Status.SuccessColor"),
        ("MeshNodeState.Failed.Brush", "Status.DangerColor"),

        // Availability of a model the mesh knows about.
        ("ModelAvailability.Starting.Brush", "Status.InfoColor"),
        ("ModelAvailability.Complete.Brush", "Status.SuccessColor"),
        ("ModelAvailability.Blocked.Brush", "Status.DangerColor"),

        // Not joined is not a problem and not progress. Neutral, like anything else that is simply
        // sitting there waiting to be asked for.
        ("ModelAvailability.NotJoined.Brush", "Accent.NeutralColor"),

        // How far a mesh this machine joined has got. Connecting is work in progress rather than
        // trouble, and a node that is simply stopped is neither.
        ("JoinState.NotConnected.Brush", "Accent.NeutralColor"),
        ("JoinState.Joining.Brush", "Status.InfoColor"),
        ("JoinState.Joined.Brush", "Status.SuccessColor"),

        // A local model's own server. Starting and restarting are work in progress rather than
        // trouble: one is a model loading and the other is a load setting that changed, and neither
        // is a failure. Not loaded is the ordinary state before a first run, so it is neutral.
        ("LocalModelState.NotLoaded.Brush", "Accent.NeutralColor"),
        ("LocalModelState.Starting.Brush", "Status.InfoColor"),
        ("LocalModelState.Restarting.Brush", "Status.InfoColor"),
        ("LocalModelState.Running.Brush", "Status.SuccessColor"),

        // The Python runtime. Provisioning is a download, not a fault.
        ("PythonEnvironmentState.Unknown.Brush", "Accent.NeutralColor"),
        ("PythonEnvironmentState.Missing.Brush", "Accent.NeutralColor"),
        ("PythonEnvironmentState.Provisioning.Brush", "Status.InfoColor"),
        ("PythonEnvironmentState.Ready.Brush", "Status.SuccessColor"),
        ("PythonEnvironmentState.Failed.Brush", "Status.DangerColor"),

        // The project index.
        ("ProjectIndexState.Unknown.Brush", "Accent.NeutralColor"),
        ("ProjectIndexState.Indexing.Brush", "Status.InfoColor"),
        ("ProjectIndexState.Ready.Brush", "Status.SuccessColor"),
        ("ProjectIndexState.Empty.Brush", "Accent.NeutralColor"),
        ("ProjectIndexState.Unavailable.Brush", "Status.WarningColor"),

        // How the last compile check ended.
        ("CompileOutcome.NotRun.Brush", "Accent.NeutralColor"),
        ("CompileOutcome.Checking.Brush", "Status.InfoColor"),
        ("CompileOutcome.Compiled.Brush", "Status.SuccessColor"),
        ("CompileOutcome.Repaired.Brush", "Status.SuccessColor"),
        ("CompileOutcome.Failed.Brush", "Status.DangerColor"),
        ("CompileOutcome.Inconclusive.Brush", "Status.WarningColor"),
        ("CompileOutcome.Unavailable.Brush", "Status.WarningColor"),

        // Severity of one diagnostic in the Problems list.
        ("CompileSeverity.Info.Brush", "Status.InfoColor"),
        ("CompileSeverity.Warning.Brush", "Status.WarningColor"),
        ("CompileSeverity.Error.Brush", "Status.DangerColor"),

        // Lifecycle of a run, for the status bar.
        ("RunState.Idle.Brush", "Accent.NeutralColor"),
        ("RunState.Running.Brush", "Status.InfoColor"),
        ("RunState.Paused.Brush", "Status.WarningColor"),
        ("RunState.Completed.Brush", "Status.SuccessColor"),
        ("RunState.Unresolved.Brush", "Status.WarningColor"),
        ("RunState.Faulted.Brush", "Status.DangerColor"),

        // Kinds of activity feed entry.
        ("Activity.Info.Brush", "Accent.NeutralColor"),
        ("Activity.Request.Brush", "Accent.PrimaryColor"),
        ("Activity.RunStarted.Brush", "Status.InfoColor"),
        ("Activity.RunCompleted.Brush", "Status.SuccessColor"),
        ("Activity.RunFaulted.Brush", "Status.DangerColor"),
        ("Activity.NodeStarted.Brush", "Status.InfoColor"),
        ("Activity.NodeCompleted.Brush", "Status.SuccessColor"),
        ("Activity.NodeFaulted.Brush", "Status.DangerColor"),
        ("Activity.ModelStream.Brush", "NodeType.ModelColor"),
        ("Activity.FileWritten.Brush", "Status.SuccessColor"),
        ("Activity.Confirmation.Brush", "Status.WarningColor"),
        ("Activity.Error.Brush", "Status.DangerColor"),

        // A field level problem in a settings panel, for example a file that is no longer there.
        ("Field.Error.Brush", "Status.DangerColor"),

        // Pending wire feedback: accepted while the drop would be allowed, danger while it not.
        ("Wire.True.Brush", "Status.SuccessColor"),
        ("Wire.False.Brush", "Status.DangerColor")
    };

    /// <summary>Gradient brush key to the theme colours of its stops, running start to end.</summary>
    /// <remarks>
    /// A second table rather than an entry in the first, because a gradient takes several colours
    /// and a solid brush takes one, and folding them together would mean every row carrying a list
    /// so that one of them could. Painting works the same way for both.
    ///
    /// Only one gradient exists and one entry is the right size for it. Every theme fills all
    /// three stops, a flat one repeating its window colour, so there is one painting path and no
    /// theme is a special case.
    /// </remarks>
    public static IReadOnlyList<(string Brush, IReadOnlyList<string> Colours)> Gradients { get; } = new (string, IReadOnlyList<string>)[]
    {
        ("Surface.Gradient.Brush", new[]
        {
            "Surface.GradientStartColor",
            "Surface.GradientMidColor",
            "Surface.GradientEndColor"
        })
    };
}
