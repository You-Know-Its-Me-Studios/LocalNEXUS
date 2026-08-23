using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Shows what reached it, and writes nothing anywhere.
/// </summary>
/// <remarks>
/// Every chain had to end in the Output node, which writes files, so the only way to read a reply
/// was to dig it out of the transcript or put it on disk to look at it. Not everything is a file:
/// what does this class do, how should I approach this, what does this error mean are all
/// reasonable things to ask a model on a canvas, and none of them wants a file written.
///
/// Nothing leaves the application here, so none of the machinery that exists to make a write safe
/// is involved: no project boundary, no write rules, no duplicate guard, no staging. There is
/// nothing to guard, because there is nothing being written.
///
/// What it holds is a run result rather than a setting, so it is deliberately not saved with the
/// graph. A graph is the arrangement; reopening one and finding last week's answer sitting in it,
/// looking exactly like this run's answer, would be worse than an empty node. The run history keeps
/// what was said.
/// </remarks>
public sealed partial class TextOutputNode : NodeBase
{
    /// <summary>
    /// How much of the answer the node itself shows.
    /// </summary>
    /// <remarks>
    /// The canvas is a diagram of the work and a node that grows to a thousand lines stops being
    /// one. Enough to recognise which answer arrived, and the rest in the inspector where there is
    /// room to read it.
    /// </remarks>
    public const int PreviewLength = 120;

    private readonly IDialogService? _dialogs;

    public TextOutputNode(IDialogService? dialogs = null)
        : base("Text output")
    {
        _dialogs = dialogs;
        Input = AddInput("Text", PinType.Text);
    }

    /// <inheritdoc />
    public override string TypeKey => "TextOutput";

    /// <summary>What to show.</summary>
    public Pin Input { get; }

    /// <summary>
    /// The whole of what arrived, for the inspector to show and for somebody to copy.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    private string _text = string.Empty;

    /// <summary>True when there is an answer to show.</summary>
    public bool HasText => Text.Trim().Length > 0;

    /// <summary>The first line or so, which is what the node draws.</summary>
    public string Preview
    {
        get
        {
            var flat = Text.ReplaceLineEndings(" ").Trim();

            if (flat.Length == 0)
            {
                return "nothing yet";
            }

            return flat.Length <= PreviewLength ? flat : flat[..PreviewLength] + "...";
        }
    }

    /// <summary>Puts the whole answer on the clipboard.</summary>
    /// <remarks>
    /// The point of asking a question is reading the answer and usually pasting it somewhere, so
    /// this is a button rather than a drag across a box that scrolls.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasText))]
    private void Copy() => _dialogs?.CopyToClipboard(Text);

    /// <inheritdoc />
    public override Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        Text = ctx.GetText(Input);

        var lines = Text.Length == 0 ? 0 : Text.ReplaceLineEndings("\n").Split('\n').Length;

        StatusMessage = HasText ? Preview : "nothing arrived";

        // The answer folds away in the transcript like anything else a step produced, and sits open
        // in the inspector, which is where somebody who wanted to read it is looking.
        ctx.Feed.Add(
            ActivityKind.Info,
            HasText ? $"{Title}: {lines} line(s)" : $"{Title} received nothing",
            Text,
            Id);

        return Task.FromResult(NodeResult.Empty);
    }

    /// <inheritdoc />
    /// <remarks>Nothing to save. What it holds is this run's answer, not how it is configured.</remarks>
    public override JsonObject SaveSettings() => new();

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
    }
}
