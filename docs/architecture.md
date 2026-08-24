# Architecture


**Pin types.** Connections require the source and target pin types to match, with one
deliberate exception: a `Code` output may feed a `Text` input. Without it a Model node,
which takes `Text` and emits `Code`, could only ever be fed by a Prompt node, so chaining
a planning model into a coding model would be impossible. The exception is one
directional: a `Text` output still cannot reach a `Code` input, so prose cannot be piped
straight into a file writer. The rule lives in one place,
`Models/PinTypeCompatibility.cs`.

**Reshape script mode** evaluates a single C# expression through Roslyn with the
incoming value bound to `input`. `System`, `System.Linq`, `System.Text` and
`System.Text.RegularExpressions` are imported. Compilation is cached per expression, and
compile errors surface in the activity feed when the node runs.

**One run at a time.** The slice permits a single active run. Run, Pause and Stop are
driven by the `RunState` enum rather than by scattered flags. Pausing takes effect
between nodes, so it never interrupts a model mid stream.

## Project layout

```
src/LocalNEXUS.App/
  Models/          NodeBase, Pin, Connection, GraphModel, pin typing and validation
  Nodes/           PromptNode, AgentNode, TriageNode, ModelNode, DebateNode, JudgeNode,
                   LoopNode, ReshapeNode, CompilerCheckNode, TextOutputNode, OutputNode,
                   ExtensionNode, NodeFactory
  Services/
    Execution/     GraphExecutor, RunContext, RunState, topological sort
    Inference/     IModelClient, OpenAiCompatibleClient, and the local runtimes behind
                   IModelRuntime: LlamaServerManager, PythonRuntimeManager,
                   RuntimeResolver, ModelFormatDetector, ModelDescriptor
    Python/        the supervised Python environment: PythonProvisioner,
                   AcceleratorProbe, PythonEnvironmentState
    Compilation/   ICodeCompiler, RoslynUnityCompiler, UnityReferenceResolver,
                   UnityInstallLocator, CompileDiagnostic, CompileResult
    ProjectIndex/  ProjectIndexService, SourceFileParser, RelevanceRanker,
                   ProjectDigest, ContextBudget, ProjectIndexCache
    Planning/      FilePlan, CodeTask, PlanParser, PlanPrompt, DuplicateTypeGuard
    Editing/       EditFormat, LineTaggedDiff, CodeEditApplier
    Distributed/   the mesh and what it reports: MeshManager, MeshStatusReader,
                   InferenceSource, ModelSection, CoveragePlan, NetworkServedModel
    Processes/     ChildProcessGroup, JobObject, and the registry of what we started
    Persistence/   AppPaths, AppConfig, ModelCatalog, ModelPathsFile, GraphSerializer
    Files/         UnityProjectService, FileWriter, ProjectWriteBatch, UnityScriptRules
    Dialogs/       IDialogService and its Windows implementation
  ViewModels/      MainViewModel, ActivityFeedViewModel, NetworkViewModel, and friends
    Theming/       AppTheme, ThemeDefinition, ThemeService, SemanticBrushes, ThemePalette
  Views/           XAML only. Theme.xaml and ShellStyles.xaml are the controls, Themes/ is
                   the five palettes, Shell/ is the activity bar, side bars, editor area,
                   bottom panel, inspector and status bar
  Assets/Fonts/    JetBrains Mono, bundled as a compiled resource, with its OFL licence
  Infrastructure/  ActivityFeed, converters, behaviours
vendor/llama/      llama.cpp binaries, fetched not committed
vendor/mesh/       Mesh LLM binaries, fetched not committed
vendor/uv/         uv, fetched not committed
vendor/python/     the Python dependency lockfiles, committed
publish.ps1        self contained single file publish into dist/
```

