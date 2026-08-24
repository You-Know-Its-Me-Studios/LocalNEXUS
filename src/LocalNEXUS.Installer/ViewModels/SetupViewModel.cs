using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.Installer.Models;
using LocalNEXUS.Installer.Services;

namespace LocalNEXUS.Installer.ViewModels;

/// <summary>
/// The wizard: which step is showing, what has been chosen, and what happens when Install is
/// pressed.
/// </summary>
/// <remarks>
/// One view model for the whole wizard rather than one per step, because every step reads from
/// and writes to the same set of choices and splitting them would mean seven objects passing the
/// same state between each other. The steps are views over this.
/// </remarks>
public sealed partial class SetupViewModel : ObservableObject
{
    /// <summary>The version this installer carries, shown in the rail corner and recorded on install.</summary>
    /// <remarks>
    /// The one place the number is written. The welcome title and the rail corner reach it through
    /// x:Static rather than repeating it, so a version bump is this line and nothing else.
    /// </remarks>
    public const string Version = "1.6.0";

    private readonly GpuDetector _detector = new();
    private readonly AssetDownloader _downloader = new();
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Which step is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Progress))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(NextLabel))]
    [NotifyPropertyChangedFor(nameof(ShowNavigation))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private WizardStep _step = WizardStep.Welcome;

    /// <summary>Configuring, installing, finished or failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(ShowNavigation))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private SetupPhase _phase = SetupPhase.Configuring;

    /// <summary>Whether the licence was accepted, which is the gate on leaving step two.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _licenseAccepted;

    /// <summary>Whether the licence was explicitly declined, so the two options are exclusive.</summary>
    [ObservableProperty]
    private bool _licenseDeclined;

    /// <summary>Whether to put a shortcut on the desktop.</summary>
    [ObservableProperty]
    private bool _createDesktopShortcut;

    /// <summary>Whether to start the application when the wizard closes.</summary>
    [ObservableProperty]
    private bool _launchOnFinish = true;

    /// <summary>The sentence above the build options, saying what was detected.</summary>
    [ObservableProperty]
    private string _detectionSummary = string.Empty;

    /// <summary>The file being fetched right now.</summary>
    [ObservableProperty]
    private string _currentFile = string.Empty;

    /// <summary>How far the install has got, zero to one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private double _installFraction;

    /// <summary>Why it failed, when it did.</summary>
    [ObservableProperty]
    private string? _failure;

    public SetupViewModel()
    {
        Steps = new ObservableCollection<StepItemViewModel>
        {
            new(WizardStep.Welcome, 1, "Welcome"),
            new(WizardStep.License, 2, "License Agreement"),
            new(WizardStep.Components, 3, "Select Components"),
            new(WizardStep.Build, 4, "Choose a Build"),
            new(WizardStep.Ready, 5, "Ready to Install"),
            new(WizardStep.Installing, 6, "Installing"),
            new(WizardStep.Finish, 7, "Finish")
        };

        Components = new ObservableCollection<ComponentItemViewModel>
        {
            new(null, "LocalNEXUS", "The application itself", "181 MB", isRequired: true),
            new(EngineComponent.Llama, "llama.cpp", "Run GGUF models on your own hardware, which is what most people want", "varies"),
            new(EngineComponent.Mesh, "Mesh LLM", "Split a model across several machines from the Network tab", EngineCatalog.Mesh.SizeText),
            new(EngineComponent.Uv, "uv", "Serve safetensors models through a local Python runtime", EngineCatalog.Uv.SizeText)
        };

        foreach (var component in Components)
        {
            component.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ComponentItemViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedEngineCount));
                    OnPropertyChanged(nameof(EngineCountText));
                    RefreshFetchList();
                }
            };
        }

        BuildOptions = new ObservableCollection<BuildOptionViewModel>
        {
            new(LlamaFlavour.Cuda13, "CUDA 13", "For NVIDIA GPUs with driver 580 or newer", EngineCatalog.LlamaBytes(LlamaFlavour.Cuda13)),
            new(LlamaFlavour.Cuda12, "CUDA 12", "For NVIDIA GPUs with an older driver", EngineCatalog.LlamaBytes(LlamaFlavour.Cuda12)),
            new(LlamaFlavour.Vulkan, "Vulkan", "For AMD, Intel and NVIDIA, and the safe default", EngineCatalog.LlamaBytes(LlamaFlavour.Vulkan)),
            new(LlamaFlavour.Cpu, "Processor only", "No graphics card needed, and slow", EngineCatalog.LlamaBytes(LlamaFlavour.Cpu))
        };

        FetchList = new ObservableCollection<FetchItem>();
        Log = new ObservableCollection<string>();

        Detect();
        RefreshStepStates();
        RefreshFetchList();
    }

    /// <summary>The seven rows of the rail.</summary>
    public ObservableCollection<StepItemViewModel> Steps { get; }

    /// <summary>The component list.</summary>
    public ObservableCollection<ComponentItemViewModel> Components { get; }

    /// <summary>The four builds.</summary>
    public ObservableCollection<BuildOptionViewModel> BuildOptions { get; }

    /// <summary>What will be fetched, itemised, with the CUDA runtime as its own line.</summary>
    public ObservableCollection<FetchItem> FetchList { get; }

    /// <summary>The install log, one line per thing that happened.</summary>
    public ObservableCollection<string> Log { get; }

    /// <summary>How far along the wizard is, which fills the sliver across the top.</summary>
    public double Progress => (double)((int)Step) / (Steps.Count - 1);

    /// <summary>Where the application goes.</summary>
    public string Destination => @"%LocalAppData%\Programs\LocalNEXUS";

    /// <summary>The install percentage as the interface states it.</summary>
    public string PercentText => $"{(int)Math.Round(InstallFraction * 100d)}%";

    /// <summary>How many engines are ticked.</summary>
    public int SelectedEngineCount => Components.Count(c => c.Component is not null && c.IsSelected);

    /// <summary>The hint under the component list.</summary>
    public string EngineCountText => $"{SelectedEngineCount} of 3 engines selected";

    /// <summary>How many files the install will download.</summary>
    public string FetchCountText => FetchList.Count == 1 ? "1 file to download" : $"{FetchList.Count} files to download";

    /// <summary>Cancel is offered right up until files start being written, and not after.</summary>
    public bool CanCancel => Phase == SetupPhase.Configuring;

    /// <summary>Back is absent on the first step, and gone once installing.</summary>
    public bool CanGoBack => Phase == SetupPhase.Configuring && Step != WizardStep.Welcome;

    /// <summary>The navigation row is hidden entirely while installing.</summary>
    public bool ShowNavigation => Step != WizardStep.Installing || Phase == SetupPhase.Failed;

    /// <summary>Offered only after a failure, because only then is there something to try again.</summary>
    public bool ShowRetry => Phase == SetupPhase.Failed;

    /// <summary>The primary button's label, which becomes Install on the Ready step.</summary>
    public string NextLabel => Step switch
    {
        WizardStep.Ready => "Install",
        WizardStep.Finish => "Finish",
        _ => "Next"
    };

    /// <summary>True when a previous install is present, which turns this into a modify.</summary>
    public bool IsModify { get; } = InstallLocations.IsInstalled;

    /// <summary>The chosen build.</summary>
    public LlamaFlavour Flavour => BuildOptions.FirstOrDefault(b => b.IsSelected)?.Flavour ?? LlamaFlavour.Vulkan;

    /// <summary>True when llama.cpp is ticked, which is what decides whether step four is shown.</summary>
    public bool WantsLlama => Components.Any(c => c.Component == EngineComponent.Llama && c.IsSelected);

    /// <summary>Moves forward, skipping the build step when there is no build to choose.</summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (Step == WizardStep.Finish)
        {
            Close();
            return;
        }

        if (Step == WizardStep.Ready)
        {
            await InstallAsync().ConfigureAwait(true);
            return;
        }

        Step = NextStep(Step);
        RefreshStepStates();

        if (Step == WizardStep.Ready)
        {
            RefreshFetchList();
        }
    }

    private bool CanGoNext()
    {
        if (Phase is SetupPhase.Installing)
        {
            return false;
        }

        // The one hard gate in the wizard. Everything else can be left at its default.
        return Step != WizardStep.License || LicenseAccepted;
    }

    /// <summary>Moves back, skipping the build step the same way forward does.</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        Step = PreviousStep(Step);
        RefreshStepStates();
    }

    /// <summary>Abandons the install. Only offered while nothing has been written.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cancellation.Cancel();
        Close();
    }

    /// <summary>Ticks everything.</summary>
    [RelayCommand]
    private void PresetEverything()
    {
        foreach (var component in Components)
        {
            component.IsSelected = true;
        }
    }

    /// <summary>Ticks only what a machine running its own models needs.</summary>
    [RelayCommand]
    private void PresetLocalOnly()
    {
        foreach (var component in Components)
        {
            component.IsSelected = component.Component is null or EngineComponent.Llama;
        }
    }

    /// <summary>Selects one component, or the whole row being clicked.</summary>
    [RelayCommand]
    private void ToggleComponent(ComponentItemViewModel? component)
    {
        if (component is null || component.IsRequired)
        {
            return;
        }

        component.IsSelected = !component.IsSelected;
    }

    /// <summary>Chooses a build, which is exclusive.</summary>
    [RelayCommand]
    private void SelectBuild(BuildOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var candidate in BuildOptions)
        {
            candidate.IsSelected = ReferenceEquals(candidate, option);
        }

        RefreshFetchList();
    }

    /// <summary>Turns the desktop shortcut on or off. The whole row is the target, not the box.</summary>
    [RelayCommand]
    private void ToggleDesktopShortcut() => CreateDesktopShortcut = !CreateDesktopShortcut;

    /// <summary>Turns the launch on finish option on or off.</summary>
    [RelayCommand]
    private void ToggleLaunch() => LaunchOnFinish = !LaunchOnFinish;

    /// <summary>Accepts the licence, which is what enables Next.</summary>
    [RelayCommand]
    private void Accept()
    {
        LicenseAccepted = true;
        LicenseDeclined = false;
    }

    /// <summary>Declines it, which leaves Next disabled.</summary>
    [RelayCommand]
    private void Decline()
    {
        LicenseAccepted = false;
        LicenseDeclined = true;
    }

    /// <summary>Tries the install again after a failure.</summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        Failure = null;
        Phase = SetupPhase.Configuring;
        await InstallAsync().ConfigureAwait(true);
    }

    private async Task InstallAsync()
    {
        Step = WizardStep.Installing;
        Phase = SetupPhase.Installing;
        Failure = null;
        RefreshStepStates();

        Log.Clear();
        Append($"Preparing {FetchList.Count} download(s)...");

        var runner = new SetupRunner(
            _downloader,
            Append,
            progress =>
            {
                CurrentFile = progress.Label;
                InstallFraction = progress.Fraction;
            });

        try
        {
            await runner
                .RunAsync(PlannedAssets(), CreateDesktopShortcut, Version, _cancellation.Token)
                .ConfigureAwait(true);

            Phase = SetupPhase.Completed;
            Step = WizardStep.Finish;
            RefreshStepStates();
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (SetupException ex)
        {
            Phase = SetupPhase.Failed;
            Failure = ex.Message;
            Append("Failed. " + ex.Message);
        }
        catch (Exception ex)
        {
            Phase = SetupPhase.Failed;
            Failure = $"Something unexpected went wrong: {ex.Message}";
            Append("Failed. " + ex.Message);
        }
    }

    private IReadOnlyList<EngineAsset> PlannedAssets()
    {
        var assets = new List<EngineAsset>();

        // A modify does not fetch what is already on disk. Half a gigabyte is far too much to
        // download twice for somebody who only came back to add Mesh LLM.
        if (WantsLlama && !(IsModify && InstallLocations.HasLlama))
        {
            assets.AddRange(EngineCatalog.Llama(Flavour));
        }

        if (Selected(EngineComponent.Mesh) && !(IsModify && InstallLocations.HasMesh))
        {
            assets.Add(EngineCatalog.Mesh);
        }

        if (Selected(EngineComponent.Uv) && !(IsModify && InstallLocations.HasUv))
        {
            assets.Add(EngineCatalog.Uv);
        }

        return assets;
    }

    private bool Selected(EngineComponent component)
        => Components.Any(c => c.Component == component && c.IsSelected);

    private void RefreshFetchList()
    {
        FetchList.Clear();

        foreach (var asset in PlannedAssets())
        {
            FetchList.Add(new FetchItem(asset.Label, asset.SizeText));
        }

        OnPropertyChanged(nameof(FetchCountText));
        OnPropertyChanged(nameof(NothingSelected));
    }

    /// <summary>
    /// True when nothing optional is ticked, which is worth saying before Install rather than
    /// letting somebody find out afterwards.
    /// </summary>
    public bool NothingSelected => SelectedEngineCount == 0;

    private void Detect()
    {
        var report = _detector.Detect();
        DetectionSummary = report.Summary;

        foreach (var option in BuildOptions)
        {
            option.IsSelected = option.Flavour == report.Flavour;
        }
    }

    private void RefreshStepStates()
    {
        foreach (var item in Steps)
        {
            item.State = item.Step < Step ? StepState.Done
                : item.Step == Step ? StepState.Active
                : StepState.Upcoming;
        }

        // The build step is passed over rather than shown as pending when there is no build to
        // choose, so the rail does not point at something that will never happen.
        if (!WantsLlama && Step > WizardStep.Build && Steps.FirstOrDefault(s => s.Step == WizardStep.Build) is { } build)
        {
            build.State = StepState.Done;
        }

        OnPropertyChanged(nameof(Progress));
    }

    private WizardStep NextStep(WizardStep from)
    {
        var next = (WizardStep)((int)from + 1);

        if (next == WizardStep.Build && !WantsLlama)
        {
            next = WizardStep.Ready;
        }

        return next;
    }

    private WizardStep PreviousStep(WizardStep from)
    {
        var previous = (WizardStep)((int)from - 1);

        if (previous == WizardStep.Build && !WantsLlama)
        {
            previous = WizardStep.Components;
        }

        return previous;
    }

    private void Append(string line)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Log.Add(line));
            return;
        }

        Log.Add(line);
    }

    private void Close()
    {
        if (Phase == SetupPhase.Completed && LaunchOnFinish && File.Exists(InstallLocations.AppExecutable))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = InstallLocations.AppExecutable,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                // Installed either way. Failing to launch it is not worth a dialog on the way out.
            }
        }

        Application.Current?.Shutdown();
    }
}
