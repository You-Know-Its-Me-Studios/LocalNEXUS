using System.IO;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>One graph somebody can start from.</summary>
/// <param name="Id">What it is called in the config and on the File menu, stable across renames.</param>
/// <param name="Name">What it is called on screen.</param>
/// <param name="Description">What shape of work it is for.</param>
/// <param name="Path">The file it comes from, or null when it is one of the built in ones.</param>
public sealed record GraphTemplate(string Id, string Name, string Description, string? Path)
{
    /// <summary>True when this is one somebody saved rather than one that ships.</summary>
    public bool IsOwn => Path is not null;
}

/// <summary>
/// The graphs the application ships with, and the ones somebody saved.
/// </summary>
/// <remarks>
/// The built in ones are built rather than shipped as files, and that is the whole design decision
/// here. A shipped file names node types and pin identifiers, and both have been renamed twice; a
/// template file would have to be migrated exactly as a saved graph is, except that nobody would
/// notice it had rotted until the day somebody opened it. Building through the factory means a
/// template cannot name a node type that does not exist, because the build would fail, and a wire
/// it draws goes through the same validator every other wire does.
///
/// The bar is that a template opens and runs without editing, given a configured model. So none of
/// them set a model, a file name or a project path: those belong to the machine rather than to the
/// shape, and a template that pre-filled them would be a template that had to be fixed first. What
/// they do carry is the wiring, which is the part that is tedious and easy to get wrong.
///
/// Nothing here assumes Unity. The output node's folder default is the one setting a graph carries
/// that mentions Assets, and it is left as the application default rather than written here, so
/// changing that default changes what a template produces.
/// </remarks>
public sealed class GraphTemplates
{
    private readonly NodeFactory _factory;
    private readonly GraphSerializer _serializer;

    public GraphTemplates(NodeFactory factory, GraphSerializer serializer)
    {
        _factory = factory;
        _serializer = serializer;
    }

    /// <summary>Where a template somebody saved is kept.</summary>
    public static string Folder { get; } = Path.Combine(AppPaths.Root, "templates");

    /// <summary>The built in shapes, in the order they are worth learning.</summary>
    private static readonly IReadOnlyList<(string Id, string Name, string Description, Action<Builder> Build)> BuiltIn = new[]
    {
        ("minimal", "One model, one file",
            "The smallest graph that writes a file. Type a request, a model answers, the answer is written.",
            new Action<Builder>(Minimal)),

        ("ask", "Ask a question",
            "Type a question, a model answers, and the answer is shown so you can read and copy it. Nothing is written to disk.",
            new Action<Builder>(Ask)),

        ("multi-file", "Plan several files",
            "Reads your project first and works out which files to edit and which to write new, then writes them in dependency order.",
            new Action<Builder>(MultiFile)),

        ("checked", "Check it compiles, and repair it",
            "Compiles every file before anything is written, and hands failures back to the model to fix. Nothing lands unless it builds.",
            new Action<Builder>(Checked)),

        ("debate", "Two models argue it out first",
            "Two models disagree about the approach over several rounds, a third decides, and the winner is what gets built.",
            new Action<Builder>(Debate))
    };

    /// <summary>Everything on offer: what ships, then what somebody saved.</summary>
    public IReadOnlyList<GraphTemplate> All()
    {
        var all = BuiltIn
            .Select(t => new GraphTemplate(t.Id, t.Name, t.Description, null))
            .ToList();

        all.AddRange(Own());
        return all;
    }

    /// <summary>The templates in the folder, newest name order, or none when there is no folder.</summary>
    public IReadOnlyList<GraphTemplate> Own()
    {
        if (!Directory.Exists(Folder))
        {
            return Array.Empty<GraphTemplate>();
        }

        try
        {
            return Directory
                .EnumerateFiles(Folder, "*" + GraphSerializer.FileExtension)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(path => new GraphTemplate(
                    Path.GetFileName(path),
                    NameOf(path),
                    "Saved from a graph on this machine.",
                    path))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<GraphTemplate>();
        }
    }

    /// <summary>
    /// Replaces the contents of a graph with a template.
    /// </summary>
    /// <returns>Anything that could not be restored, which is only ever possible for a saved one.</returns>
    /// <exception cref="InvalidDataException">A saved template is not a graph this build can read.</exception>
    public IReadOnlyList<string> Apply(GraphTemplate template, GraphModel graph)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(graph);

        if (template.Path is { } path)
        {
            var warnings = _serializer.LoadInto(graph, path);
            graph.Name = template.Name;
            return warnings;
        }

        var built = BuiltIn.FirstOrDefault(t => t.Id == template.Id);

        if (built.Build is null)
        {
            throw new InvalidDataException($"There is no template called {template.Id}.");
        }

        graph.Clear();

        built.Build(new Builder(_factory, graph));
        graph.Name = template.Name;

        return Array.Empty<string>();
    }

    /// <summary>Saves a graph as a template of the given name, and returns where it went.</summary>
    /// <exception cref="ArgumentException">The name is blank or has nothing usable in it.</exception>
    public string Save(GraphModel graph, string name)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var safe = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();

        if (safe.Length == 0)
        {
            throw new ArgumentException("A template name needs at least one character a file name can hold.", nameof(name));
        }

        Directory.CreateDirectory(Folder);

        var path = Path.Combine(Folder, safe + GraphSerializer.FileExtension);
        _serializer.Save(graph, path);

        return path;
    }

    private static string NameOf(string path)
    {
        var name = Path.GetFileName(path);
        var cut = name.IndexOf(GraphSerializer.FileExtension, StringComparison.OrdinalIgnoreCase);

        return cut > 0 ? name[..cut] : Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>
    /// The three node graph from the readme: type something, a model answers, it is written.
    /// </summary>
    private static void Minimal(Builder b)
    {
        var prompt = b.Add("Prompt", 40, 140);
        var model = b.Add("Model", 300, 140);
        var output = b.Add("Output", 560, 140);

        b.Wire(prompt, "Text", model, "Text");
        b.Wire(model, "Code", output, "Code");
    }

    /// <summary>
    /// The smallest graph there is: ask, read.
    /// </summary>
    /// <remarks>
    /// First in the list after the one that writes a file, because it is the thing most people
    /// would try first and until now there was no graph that did it. Three nodes and two wires, and
    /// nothing it does can touch the project.
    /// </remarks>
    private static void Ask(Builder b)
    {
        var prompt = b.Add("Prompt", 40, 140);
        var model = b.Add("Model", 300, 140);
        var answer = b.Add("TextOutput", 560, 140);

        // The one setting this template does change, and it has to. A Model node starts configured
        // to write files: output raw code only, no commentary, no explanation. Asked a question in
        // that voice it answers with a class, which is the right answer for the graph it was built
        // for and the wrong one here. The prompt is part of the shape rather than of the machine,
        // which is why setting it is not the thing templates deliberately avoid.
        if (model is Nodes.ModelNode coder)
        {
            coder.SystemPrompt = Nodes.TextOutputNode.AskingPrompt;
            coder.StripCodeFences = false;
        }

        b.Wire(prompt, "Text", model, "Text");
        b.Wire(model, "Code", answer, "Text");
    }

    /// <summary>
    /// Triage in front, so one request becomes several files in dependency order.
    /// </summary>
    /// <remarks>
    /// The model wire into Triage is the one worth noticing. Triage borrows the model that is going
    /// to do the writing rather than carrying a second copy of every model setting, so the same
    /// node is wired twice: once as configuration and once as the thing downstream.
    /// </remarks>
    private static void MultiFile(Builder b)
    {
        var prompt = b.Add("Prompt", 40, 160);
        var triage = b.Add("Triage", 280, 160);
        var model = b.Add("Model", 540, 160);
        var output = b.Add("Output", 800, 160);

        b.Wire(prompt, "Text", triage, "Text");
        b.Wire(model, "Model", triage, "Model");
        b.Wire(triage, "Text", model, "Text");
        b.Wire(model, "Code", output, "Code");
    }

    /// <summary>
    /// The same, with the compiler in the way, which is the graph the application is really for.
    /// </summary>
    /// <remarks>
    /// The repair loop is not drawn and must not be. The check follows its own incoming wire and
    /// asks whatever it finds there for another attempt, so the loop is a setting on the node
    /// rather than a wire looped back, and a graph with a cycle in it is refused before it runs.
    /// </remarks>
    private static void Checked(Builder b)
    {
        var prompt = b.Add("Prompt", 40, 160);
        var triage = b.Add("Triage", 280, 160);
        var model = b.Add("Model", 540, 160);
        var check = b.Add("CompilerCheck", 800, 160);
        var output = b.Add("Output", 1060, 160);

        b.Wire(prompt, "Text", triage, "Text");
        b.Wire(model, "Model", triage, "Model");
        b.Wire(triage, "Text", model, "Text");
        b.Wire(model, "Code", check, "Code");
        b.Wire(check, "Code", output, "Code");
    }

    /// <summary>
    /// Two models argue, a third decides, and what it decided is what gets built.
    /// </summary>
    /// <remarks>
    /// Three model nodes because there are three roles, and the two arguing have to be able to
    /// differ. They are wired as configuration into the debate, which is why none of them runs on
    /// its own: a model reached only through a model pin is read rather than executed.
    /// </remarks>
    private static void Debate(Builder b)
    {
        var prompt = b.Add("Prompt", 40, 240);

        var first = b.Add("Model", 280, 40);
        var second = b.Add("Model", 280, 170);
        var debate = b.Add("Debate", 540, 240);

        var judgeModel = b.Add("Model", 540, 430);
        var judge = b.Add("Judge", 800, 240);

        var writer = b.Add("Model", 1060, 240);
        var output = b.Add("Output", 1320, 240);

        b.Wire(prompt, "Text", debate, "Text");
        b.Wire(first, "Model", debate, "Model A");
        b.Wire(second, "Model", debate, "Model B");

        b.Wire(debate, "Text", judge, "Text");
        b.Wire(judgeModel, "Model", judge, "Model");

        b.Wire(judge, "Text", writer, "Text");
        b.Wire(writer, "Code", output, "Code");
    }

    /// <summary>
    /// Builds a graph through the factory and the graph's own validator.
    /// </summary>
    /// <remarks>
    /// Pins are found by name because that is what a person reading the template sees on the
    /// canvas, and a wire that names a pin no longer there fails loudly here rather than producing
    /// a template with a wire quietly missing.
    /// </remarks>
    private sealed class Builder
    {
        private readonly NodeFactory _factory;
        private readonly GraphModel _graph;

        public Builder(NodeFactory factory, GraphModel graph)
        {
            _factory = factory;
            _graph = graph;
        }

        public NodeBase Add(string typeKey, double x, double y)
        {
            var node = _factory.Create(typeKey, x, y);
            _graph.AddNode(node);
            return node;
        }

        public void Wire(NodeBase from, string outputName, NodeBase to, string inputName)
        {
            var output = from.Outputs.FirstOrDefault(p => p.Name == outputName)
                ?? throw new InvalidOperationException($"{from.Title} has no output called {outputName}.");

            var input = to.Inputs.FirstOrDefault(p => p.Name == inputName)
                ?? throw new InvalidOperationException($"{to.Title} has no input called {inputName}.");

            if (!_graph.TryConnect(output, input, out var reason))
            {
                throw new InvalidOperationException(
                    $"The template cannot wire {from.Title}.{outputName} to {to.Title}.{inputName}: {reason}.");
            }
        }
    }
}
