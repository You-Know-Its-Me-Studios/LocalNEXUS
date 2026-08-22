using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Nodes;

/// <summary>Whether a node's tools have been listed yet, and how that went.</summary>
public enum ToolListingState
{
    /// <summary>Nobody has asked. An extension is not started to fill in a panel nobody opened.</summary>
    NotListed,

    /// <summary>The extension is being started and asked what it has.</summary>
    Listing,

    /// <summary>It answered, and its tools are on the choice.</summary>
    Listed,

    /// <summary>It could not be reached, and the reason is on the choice.</summary>
    Unavailable
}

/// <summary>One tool of one extension, and whether this node may call it.</summary>
/// <remarks>
/// Narrowing is a convenience rather than a requirement. AnkleBreaker alone exposes between two
/// hundred and sixty eight and three hundred and thirty tools depending on version, and every one
/// of their names, descriptions and schemas goes into the prompt on every turn. That is nothing on
/// a large context and several thousand tokens worth trimming on a small one.
/// </remarks>
public sealed partial class ToolChoice : ObservableObject
{
    private readonly Action<ToolChoice> _changed;

    public ToolChoice(ToolDefinition definition, bool isSelected, Action<ToolChoice> changed)
    {
        Definition = definition;
        _changed = changed;
        _isSelected = isSelected;
    }

    /// <summary>The tool as the model would be offered it.</summary>
    public ToolDefinition Definition { get; }

    /// <summary>What it is called, which is what the model asks for by name.</summary>
    public string Name => Definition.Name;

    /// <summary>What it says it does, which is all a model reads before choosing it.</summary>
    public string Description => Definition.Description;

    /// <summary>Roughly what offering this one costs, every turn.</summary>
    public int TokenEstimate => ToolTokens.Estimate(Definition);

    /// <summary>True when this node may call it.</summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _changed(this);
}

/// <summary>One installed extension, and whether this node may use its tools.</summary>
public sealed partial class ExtensionChoice : ObservableObject
{
    private readonly Action<ExtensionChoice> _changed;

    public ExtensionChoice(string id, string name, string stateText, bool isUsable, bool isSelected, Action<ExtensionChoice> changed)
    {
        Id = id;
        Name = name;
        StateText = stateText;
        IsUsable = isUsable;
        _changed = changed;
        _isSelected = isSelected;
    }

    /// <summary>Which extension, as the registry knows it.</summary>
    public string Id { get; }

    /// <summary>What it is called.</summary>
    public string Name { get; }

    /// <summary>What state the registry has it in, for a row that cannot be used.</summary>
    public string StateText { get; }

    /// <summary>True when it could actually be started and asked.</summary>
    public bool IsUsable { get; }

    /// <summary>Its tools, once somebody has asked for them.</summary>
    public ObservableCollection<ToolChoice> Tools { get; } = new();

    /// <summary>Whether the tools have been listed, and how that went.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing))]
    [NotifyPropertyChangedFor(nameof(HasTools))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private ToolListingState _listing = ToolListingState.NotListed;

    /// <summary>Why the tools could not be listed, when they could not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string? _problem;

    /// <summary>True while this node may use it.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True while it is being started and asked.</summary>
    public bool IsListing => Listing == ToolListingState.Listing;

    /// <summary>True when there are tools to narrow.</summary>
    public bool HasTools => Tools.Count > 0;

    /// <summary>
    /// What this row says about itself, which is a count once there is one to give.
    /// </summary>
    /// <remarks>
    /// Not listed is not a failure and does not read as one. An extension is a process, and one is
    /// not started to fill in a panel somebody opened to change the temperature.
    /// </remarks>
    public string Summary => Listing switch
    {
        ToolListingState.Listing => "starting it and asking",
        ToolListingState.Unavailable => Problem ?? "it could not be reached",
        ToolListingState.Listed when Tools.Count == 0 => "it has no tools",
        ToolListingState.Listed => $"{Tools.Count(t => t.IsSelected)} of {Tools.Count} tools, about {SelectedTokens} tokens",
        _ => IsUsable ? "tools not listed yet" : StateText
    };

    /// <summary>Roughly what the selected tools of this extension cost every turn.</summary>
    public int SelectedTokens => Tools.Where(t => t.IsSelected).Sum(t => t.TokenEstimate);

    /// <summary>Tells the panel the count and the cost moved.</summary>
    public void RefreshSummary()
    {
        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(SelectedTokens));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnIsSelectedChanged(bool value) => _changed(this);
}

/// <summary>
/// Roughly what a tool's schema costs in a prompt.
/// </summary>
/// <remarks>
/// Characters over four, which is the usual rule of thumb for English and JSON alike and is close
/// enough for a number whose only job is to make a choice informed rather than blind. Counting
/// properly would mean running the model's own tokenizer over every schema on every keystroke, to
/// answer a question nobody needs to four significant figures.
/// </remarks>
public static class ToolTokens
{
    /// <summary>Characters per token, as a rule of thumb.</summary>
    public const int CharactersPerToken = 4;

    /// <summary>What one tool adds to a request.</summary>
    public static int Estimate(ToolDefinition tool)
    {
        var characters = tool.Name.Length
                         + tool.Description.Length
                         + (tool.ParametersSchema?.ToJsonString().Length ?? 0);

        return characters / CharactersPerToken;
    }
}
