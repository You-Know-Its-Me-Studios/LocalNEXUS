using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The first run checklist.
/// </summary>
/// <remarks>
/// Two things are worth holding it to and both are easy to get wrong. Every step has to answer from
/// what is true now rather than from having been clicked, or it starts lying the moment somebody
/// closes their project. And it has to be a suggestion: nothing may wait on it, and somebody who
/// dismissed it has to be able to get it back.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class WalkthroughTests
{
    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Services = TestServices.Create();
            Graph = new GraphModel();
            Config = new AppConfig();

            var templates = new GraphTemplates(Services.Factory, new GraphSerializer(Services.Factory)).All();

            Feed = new ActivityFeedViewModel(
                new GraphExecutor(Services.Services),
                Graph,
                Services.Feed,
                System.Windows.Threading.Dispatcher.CurrentDispatcher);

            Walkthrough = new WalkthroughViewModel(
                Config,
                Services.Project,
                Models,
                Graph,
                Feed,
                new RelayCommand(() => ProjectOpened++),
                new RelayCommand(() => SettingsOpened++),
                new RelayCommand<GraphTemplate>(t => Applied.Add(t!)),
                templates);
        }

        public TestServices Services { get; }

        /// <summary>Where a finished run is announced from, which is the last step's only signal.</summary>
        public ActivityFeedViewModel Feed { get; }

        /// <summary>Stands in for the model catalogue, which is not part of the test services.</summary>
        public System.Collections.ObjectModel.ObservableCollection<App.Services.Persistence.LocalModelInfo> Models { get; } = new();

        public GraphModel Graph { get; }

        public AppConfig Config { get; }

        public WalkthroughViewModel Walkthrough { get; }

        public int ProjectOpened { get; private set; }

        public int SettingsOpened { get; private set; }

        public List<GraphTemplate> Applied { get; } = new();

        public void Dispose() => Services.Dispose();
    }

    /// <summary>It ends in one run, and every step before it is something concrete.</summary>
    [Fact]
    public void ItIsAPathToOneRun()
    {
        using var harness = new Harness();

        var steps = harness.Walkthrough.Steps;

        Assert.Equal(5, steps.Count);
        Assert.All(steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Title)));
        Assert.All(steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Detail)));

        Assert.Contains("run it", steps[^1].Title, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>On a first launch nothing is done, and the count says so.</summary>
    [Fact]
    public void ItStartsWithNothingDone()
    {
        using var harness = new Harness();

        Assert.All(harness.Walkthrough.Steps, s => Assert.False(s.IsDone));
        Assert.Equal("0 of 5 done", harness.Walkthrough.Progress);
        Assert.False(harness.Walkthrough.IsFinished);
        Assert.True(harness.Walkthrough.IsOpen);
    }

    /// <summary>
    /// A step ticks itself when the thing it describes becomes true, without being clicked.
    /// </summary>
    /// <remarks>
    /// The whole reason nothing is remembered. Somebody who opened their project before reading
    /// this should find that step already done rather than being told to do it again.
    /// </remarks>
    [Fact]
    public void OpeningAProjectTicksItsStepWithoutTheButton()
    {
        using var harness = new Harness();
        using var project = SampleProject.Create();

        Assert.False(harness.Walkthrough.Steps[0].IsDone);

        harness.Services.Project.Open(project.Root);

        Assert.True(harness.Walkthrough.Steps[0].IsDone);
        Assert.Equal(0, harness.ProjectOpened);
    }

    /// <summary>And it unticks when the thing stops being true.</summary>
    [Fact]
    public void AStepUnticksWhenItStopsBeingTrue()
    {
        using var harness = new Harness();
        using var project = SampleProject.Create();

        harness.Services.Project.Open(project.Root);
        Assert.True(harness.Walkthrough.Steps[0].IsDone);

        harness.Services.Project.Close();
        Assert.False(harness.Walkthrough.Steps[0].IsDone);
    }

    /// <summary>Putting a node on the canvas ticks the template step.</summary>
    [Fact]
    public void PuttingSomethingOnTheCanvasTicksItsStep()
    {
        using var harness = new Harness();

        Assert.False(harness.Walkthrough.Steps[2].IsDone);

        harness.Graph.AddNode(harness.Services.Factory.Create("Prompt"));

        Assert.True(harness.Walkthrough.Steps[2].IsDone);
    }

    /// <summary>Choosing a model on a node ticks the step that asks for one.</summary>
    [Fact]
    public void ChoosingAModelTicksItsStep()
    {
        using var harness = new Harness();

        var model = (ModelNode)harness.Services.Factory.Create("Model");
        harness.Graph.AddNode(model);

        Assert.False(harness.Walkthrough.Steps[3].IsDone);

        model.Provider = ModelProvider.SelfHosted;
        model.SelfHostedModelId = "something";

        harness.Walkthrough.Refresh();

        Assert.True(harness.Walkthrough.Steps[3].IsDone);
    }

    /// <summary>The template step opens a template rather than describing one.</summary>
    [Fact]
    public void TheTemplateStepOpensATemplate()
    {
        using var harness = new Harness();

        var step = harness.Walkthrough.Steps[2];

        Assert.True(step.HasAction);
        step.Action!.Execute(null);

        Assert.Single(harness.Applied);
    }

    /// <summary>A run that finished is the last step, and it is remembered.</summary>
    /// <remarks>
    /// The only one that cannot be recomputed: a completed run leaves nothing behind that is still
    /// true a minute later.
    /// </remarks>
    [Fact]
    public void ARunThatFinishedTicksTheLastStep()
    {
        using var harness = new Harness();

        Assert.False(harness.Walkthrough.Steps[^1].IsDone);

        harness.Walkthrough.RecordSuccessfulRun();

        Assert.True(harness.Walkthrough.Steps[^1].IsDone);
        Assert.True(harness.Config.HasCompletedAWalkthroughRun);
    }

    /// <summary>Dismissing hides it and remembers, and it can be brought back.</summary>
    [Fact]
    public void ItIsSkippableAndReopenable()
    {
        using var harness = new Harness();

        harness.Walkthrough.DismissCommand.Execute(null);

        Assert.False(harness.Walkthrough.IsOpen);
        Assert.True(harness.Config.WalkthroughDismissed);

        harness.Walkthrough.ShowCommand.Execute(null);

        Assert.True(harness.Walkthrough.IsOpen);

        // And it stays dismissed for the next launch, which is what stops it reappearing at every
        // start for somebody who has done this before.
        Assert.True(harness.Config.WalkthroughDismissed);
    }

    /// <summary>Somebody who dismissed it once is not shown it again.</summary>
    [Fact]
    public void ADismissedWalkthroughDoesNotOpenItself()
    {
        using var services = TestServices.Create();

        var config = new AppConfig { WalkthroughDismissed = true };
        var graph = new GraphModel();

        var walkthrough = new WalkthroughViewModel(
            config,
            services.Project,
            new System.Collections.ObjectModel.ObservableCollection<App.Services.Persistence.LocalModelInfo>(),
            graph,
            new ActivityFeedViewModel(
                new GraphExecutor(services.Services),
                graph,
                services.Feed,
                System.Windows.Threading.Dispatcher.CurrentDispatcher),
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new RelayCommand<GraphTemplate>(_ => { }),
            new GraphTemplates(services.Factory, new GraphSerializer(services.Factory)).All());

        Assert.False(walkthrough.IsOpen);
    }

    /// <summary>Nothing in it assumes Unity.</summary>
    /// <remarks>
    /// A walkthrough that says open your Unity project would undo the visible half of v1.37, and
    /// it is the first thing a first time user reads.
    /// </remarks>
    [Fact]
    public void NothingInItAssumesUnity()
    {
        using var harness = new Harness();

        foreach (var step in harness.Walkthrough.Steps)
        {
            Assert.DoesNotContain("MonoBehaviour", step.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("Assets", step.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("Unity", step.Title, StringComparison.Ordinal);
        }

        // The first step may name Unity, because saying that a Unity project is recognised is the
        // opposite of assuming one. What it must not do is require one.
        Assert.Contains("any", harness.Walkthrough.Steps[0].Detail, StringComparison.OrdinalIgnoreCase);
    }
}
