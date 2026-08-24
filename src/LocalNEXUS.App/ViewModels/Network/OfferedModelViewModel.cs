using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// One local model, and whether this machine offers it to the mesh.
/// </summary>
/// <remarks>
/// Offering is opt in per model. Nothing is offered until it is ticked, because what this machine
/// serves to other people is a decision somebody should have made rather than one that happened
/// by default.
/// </remarks>
public sealed partial class OfferedModelViewModel : ObservableObject
{
    private readonly Action _changed;

    /// <summary>True when this model is offered to the mesh.</summary>
    [ObservableProperty]
    private bool _isOffered;

    public OfferedModelViewModel(LocalModelInfo model, bool isOffered, Action changed)
    {
        Model = model;

        // A model the mesh cannot serve is never offered, whatever a saved configuration says.
        // Older builds let one be ticked, so a file written by one of those can still name it.
        _isOffered = isOffered && CanBeOffered(model);
        _changed = changed;
    }

    /// <summary>The model on disk.</summary>
    public LocalModelInfo Model { get; }

    /// <summary>
    /// Whether the mesh can actually serve this model.
    /// </summary>
    /// <remarks>
    /// The mesh engine's inference path is GGUF only. The catalogue deliberately lists both
    /// formats, so without this the panel offered safetensors models with a tick box that
    /// saved, was sent to the engine, and failed there. Refusing at the tick box is the whole
    /// point: a setting that accepts an answer it cannot honour is worse than one that says no.
    ///
    /// A safetensors model too large for one machine is not stranded by this. It goes through
    /// the distributed runtime instead, which is a different path and has its own switch.
    /// </remarks>
    public bool IsOfferable => CanBeOffered(Model);

    /// <summary>Why this model cannot be offered, or empty when it can.</summary>
    public string OfferRefusal => IsOfferable
        ? string.Empty
        : $"The mesh serves GGUF only, and this is {Model.FormatLabel}. A safetensors model is "
          + "shared through distributed inference instead, in the settings below.";

    private static bool CanBeOffered(LocalModelInfo model)
        => model.Descriptor.Format == Services.Inference.ModelFormat.Gguf;

    /// <summary>Absolute path, which is what the engine is given.</summary>
    public string Path => Model.Path;

    /// <summary>The file or folder name.</summary>
    public string Name => Model.Name;

    /// <summary>The quantization, or a note that the name does not say.</summary>
    public string Quantisation => Model.Descriptor.Quantisation;

    /// <summary>Size on disk.</summary>
    public string SizeLabel => Model.Descriptor.SizeLabel;

    /// <summary>GGUF or safetensors.</summary>
    public string FormatLabel => Model.FormatLabel;

    partial void OnIsOfferedChanged(bool value)
    {
        // The view disables the box, and this is the same rule enforced where it cannot be
        // bypassed by a binding, a saved file, or a later change to the template.
        if (value && !IsOfferable)
        {
            IsOffered = false;
            return;
        }

        _changed();
    }
}
