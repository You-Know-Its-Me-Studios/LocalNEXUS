using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Editing;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Services.Agent;

/// <summary>
/// The tools an agent has of its own, and what they do.
/// </summary>
/// <remarks>
/// Everything here already existed as behaviour inside a node. Reading a file is what the coder
/// does before an edit, compiling is what the check node does, searching is what triage does, and
/// writing is what the output node does. What is new is that a model chooses between them a turn at
/// a time instead of a graph deciding the order in advance.
///
/// Nothing here is a private route to disk. A write goes through the same batch, the same expected
/// existence check, the same Unity rules where Unity is, and the same duplicate guard that the
/// output node uses, and a refusal is staged and handed back as a result the model reads. A tool
/// that skipped any of that would skip everything built since v1.12, and the point of giving the
/// model tools is not to give it a way round the rules.
///
/// Read before write is not relaxed because a loop is doing it. Every edit reads the file from disk
/// at the moment it is edited, which is stricter here than in the pipeline: the agent may have
/// written that file itself two turns ago.
/// </remarks>
public sealed class AgentToolbox
{
    /// <summary>What the tools this file provides are owned by, so a call routes back here.</summary>
    public const string OwnerId = "localnexus.agent";

    /// <summary>The largest file this will hand back or accept in one call.</summary>
    /// <remarks>
    /// The same budget the coder reads under, so an agent and a pipeline see a file the same way.
    /// </remarks>
    public const int FileBudget = SourceFileReader.WholeFileBudget;

    /// <summary>How many entries a folder listing returns before it says there are more.</summary>
    public const int ListingLimit = 200;

    private readonly NodeExecutionContext _ctx;
    private readonly Guid _nodeId;

    public AgentToolbox(NodeExecutionContext ctx, Guid nodeId)
    {
        _ctx = ctx;
        _nodeId = nodeId;
    }

    /// <summary>Files this agent has written or staged, in the order it did.</summary>
    public List<string> Written { get; } = new();

    /// <summary>
    /// The tools, as the model is offered them.
    /// </summary>
    /// <remarks>
    /// Deliberately six. Every one of them is something the pipeline already does, and each is the
    /// smallest thing a turn can usefully be. What is not here is anything that runs a command, and
    /// that is not an oversight: this application starts processes it owns and nothing else, and a
    /// tool that ran an arbitrary command line would be a hole through every one of those rules.
    /// </remarks>
    public static IReadOnlyList<ToolDefinition> Tools { get; } = new[]
    {
        new ToolDefinition(
            "read_file",
            "Read a file in the project, exactly as it is on disk right now. Do this before "
            + "changing a file, every time, including one you wrote earlier in this same task.",
            Schema(("path", "string", "Path relative to the project root.")),
            OwnerId),

        new ToolDefinition(
            "write_file",
            "Write a whole file into the project, creating it or replacing it. Give the complete "
            + "contents: a comment saying the rest is unchanged deletes everything it stands for. "
            + "The project's write rules run first and may refuse it, which you will be told about.",
            Schema(
                ("path", "string", "Path relative to the project root."),
                ("content", "string", "The complete file.")),
            OwnerId),

        new ToolDefinition(
            "edit_file",
            "Change one member of one type in a file, by naming it rather than by quoting the "
            + "lines around it. The member is found by parsing the file, so there is nothing to "
            + "get wrong about what it currently contains, and indentation is handled for you.",
            Schema(
                ("path", "string", "Path relative to the project root."),
                ("type", "string", "The type holding the member."),
                ("member", "string", "The member to change."),
                ("action", "string", "replace, add or remove."),
                ("code", "string", "The complete new declaration. Not needed for remove.")),
            OwnerId),

        new ToolDefinition(
            "list_folder",
            "List the files and folders in one folder of the project.",
            Schema(("path", "string", "Path relative to the project root. Empty for the root.")),
            OwnerId),

        new ToolDefinition(
            "search_project",
            "Find where a type is defined, from the index of the whole project. Use this before "
            + "writing a new type, so you change what is there rather than adding a second copy.",
            Schema(("name", "string", "The type name to look for.")),
            OwnerId),

        new ToolDefinition(
            "compile",
            "Compile a file against the project's own references and read back the errors. Use it "
            + "on anything you wrote before you say you are done.",
            Schema(("path", "string", "Path relative to the project root.")),
            OwnerId),
    };

    /// <summary>True when this call belongs to the toolbox rather than to an extension.</summary>
    public static bool Owns(string ownerId) => string.Equals(ownerId, OwnerId, StringComparison.Ordinal);

    /// <summary>
    /// Runs one tool and returns what to tell the model, and whether it failed.
    /// </summary>
    /// <remarks>
    /// Every path returns rather than throws. A tool that threw would end the run, and the whole
    /// point of a failure here is that the model reads it and does something else.
    /// </remarks>
    public async Task<(string Text, bool IsError)> RunAsync(ToolCall call, CancellationToken ct)
    {
        JsonObject arguments;

        try
        {
            arguments = JsonNode.Parse(
                string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson) as JsonObject
                ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            return ($"Those arguments are not valid JSON: {ex.Message}", true);
        }

        try
        {
            return call.Name switch
            {
                "read_file" => ReadFile(Text(arguments, "path")),
                "write_file" => await WriteFileAsync(Text(arguments, "path"), Text(arguments, "content"), ct)
                    .ConfigureAwait(false),
                "edit_file" => await EditFileAsync(arguments, ct).ConfigureAwait(false),
                "list_folder" => ListFolder(Text(arguments, "path")),
                "search_project" => SearchProject(Text(arguments, "name")),
                "compile" => await CompileAsync(Text(arguments, "path"), ct).ConfigureAwait(false),
                _ => ($"There is no tool called '{call.Name}'.", true)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ($"That tool could not be run: {ex.Message}", true);
        }
    }

    private (string Text, bool IsError) ReadFile(string path)
    {
        if (Project() is not { } project)
        {
            return (NoProject, true);
        }

        var reading = SourceFileReader.Read(project.ProjectPath, path, string.Empty);

        if (!reading.IsUsable)
        {
            return (reading.Message, true);
        }

        var note = reading.Note.Length == 0 ? string.Empty : reading.Note + Environment.NewLine + Environment.NewLine;

        return (note + reading.Content, false);
    }

    /// <summary>
    /// Writes one file through the same path the output node writes through.
    /// </summary>
    /// <remarks>
    /// The order is the order that matters: resolve inside the project, check the file is or is not
    /// there as expected, run the Unity rules where Unity is, refuse a type the project already
    /// declares, then stage and commit as one batch so a failure part way puts it back. A refusal
    /// is staged and reported, exactly as a refused plan row is, and comes back here as a result.
    /// </remarks>
    private async Task<(string Text, bool IsError)> WriteFileAsync(string path, string content, CancellationToken ct)
    {
        if (Project() is not { } project)
        {
            return (NoProject, true);
        }

        if (path.Length == 0)
        {
            return ("A write needs a path.", true);
        }

        if (content.Trim().Length == 0)
        {
            return ("A write needs the complete contents of the file.", true);
        }

        if (CodeEditApplier.LooksElided(content))
        {
            return ("That stops part way and says the rest is unchanged. Writing it would delete "
                    + "everything it stands for. Send the complete file.", true);
        }

        string absolute;

        try
        {
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
            absolute = project.ResolveTargetPath(folder, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return ($"That path is not inside the project: {ex.Message}", true);
        }

        var existed = File.Exists(absolute);
        var declared = DeclaredTypes(content, path, ct);
        var batch = new ProjectWriteBatch(_ctx.Services.FileWriter);

        try
        {
            batch.EnforceExpectedExistence(absolute, existed);

            if (project.IsUnity)
            {
                UnityScriptRules.Enforce(path, content, _ctx.Services.ProjectIndex.FindFile(path), declared);
            }

            EnforceNothingDeclaredTwice(path, declared);
        }
        catch (UnityScriptRuleException ex)
        {
            Stage(path, content, StagedReason.RefusedByProjectRules, ex.Message);

            return ($"The project's write rules refused it: {ex.Message}", true);
        }
        catch (InvalidOperationException ex)
        {
            Stage(path, content, StagedReason.RefusedByProjectRules, ex.Message);

            return (ex.Message, true);
        }

        Snapshot(absolute);
        batch.Stage(absolute, content);

        try
        {
            var written = await batch.CommitAsync(ct).ConfigureAwait(false);

            Written.Add(path);
            Record(path, Services.History.FileOutcome.Written, null);

            var lines = content.ReplaceLineEndings("\n").Split('\n').Length;

            return ($"{(existed ? "Replaced" : "Created")} {path}, {lines} line(s). {written.Count} file written.", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Stage(path, content, StagedReason.WriteFailed, ex.Message);

            return ($"It could not be written: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Changes one member, by finding it in the tree rather than by matching text.
    /// </summary>
    /// <remarks>
    /// The file is read here and now, not remembered from an earlier turn, because the agent may
    /// have written it itself since. What comes back goes out through the ordinary write, so it
    /// meets every rule a whole file meets.
    /// </remarks>
    private async Task<(string Text, bool IsError)> EditFileAsync(JsonObject arguments, CancellationToken ct)
    {
        if (Project() is not { } project)
        {
            return (NoProject, true);
        }

        var path = Text(arguments, "path");
        var action = Text(arguments, "action").ToLowerInvariant();

        var kind = action switch
        {
            "replace" or "" => StructuredEditKind.ReplaceMember,
            "add" => StructuredEditKind.AddMember,
            "remove" => StructuredEditKind.RemoveMember,
            _ => (StructuredEditKind?)null
        };

        if (kind is not { } editKind)
        {
            return ($"'{action}' is not an action. Use replace, add or remove.", true);
        }

        var reading = SourceFileReader.Read(project.ProjectPath, path, Text(arguments, "type"));

        if (!reading.IsUsable)
        {
            return (reading.Message, true);
        }

        if (reading.State == FileReadState.Excerpted)
        {
            return ($"{path} is too large to edit safely this way. Read it and write it whole instead.", true);
        }

        var edit = new StructuredEdit(editKind, Text(arguments, "type"), Text(arguments, "member"), Text(arguments, "code"));
        var result = await RoslynEditApplier.ApplyAsync(reading.Content, new[] { edit }, ct).ConfigureAwait(false);

        if (!result.IsApplied)
        {
            return (result.Message, true);
        }

        var written = await WriteFileAsync(path, result.Content, ct).ConfigureAwait(false);

        return written.IsError ? written : ($"{edit}. {written.Text}", false);
    }

    private (string Text, bool IsError) ListFolder(string path)
    {
        if (Project() is not { ProjectPath: { Length: > 0 } root })
        {
            return (NoProject, true);
        }

        string absolute;

        try
        {
            absolute = Path.GetFullPath(Path.Combine(root, path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ($"'{path}' is not a usable path.", true);
        }

        if (!absolute.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            return ("That is outside the project.", true);
        }

        if (!Directory.Exists(absolute))
        {
            return ($"There is no folder at '{path}'.", true);
        }

        var text = new StringBuilder();
        var shown = 0;

        foreach (var entry in Directory.EnumerateFileSystemEntries(absolute).OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
        {
            if (shown++ >= ListingLimit)
            {
                text.AppendLine($"... and more, {ListingLimit} shown");
                break;
            }

            text.AppendLine(Directory.Exists(entry) ? Path.GetFileName(entry) + "/" : Path.GetFileName(entry));
        }

        return (shown == 0 ? "That folder is empty." : text.ToString().TrimEnd(), false);
    }

    private (string Text, bool IsError) SearchProject(string name)
    {
        if (name.Length == 0)
        {
            return ("A search needs a type name.", true);
        }

        var found = _ctx.Services.ProjectIndex.FindType(name);

        if (found.Count == 0)
        {
            return ($"Nothing in this project declares a type called {name}.", false);
        }

        var text = new StringBuilder($"{found.Count} match(es) for {name}:");

        foreach (var type in found)
        {
            text.AppendLine();
            text.Append($"  {type.FullName} in {_ctx.Services.ProjectIndex.FileOf(type)?.RelativePath ?? "an unknown file"} at line {type.Line}");
        }

        return (text.ToString(), false);
    }

    private async Task<(string Text, bool IsError)> CompileAsync(string path, CancellationToken ct)
    {
        if (Project() is not { } project)
        {
            return (NoProject, true);
        }

        var reading = SourceFileReader.Read(project.ProjectPath, path, string.Empty);

        if (!reading.IsUsable)
        {
            return (reading.Message, true);
        }

        var result = await _ctx.Services.Compiler
            .CompileAsync(reading.Content, Path.GetFileName(path), project.ProjectPath, ct)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            return ($"{path} compiles. {result.ReferenceSummary}.", false);
        }

        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        var text = new StringBuilder($"{path} does not compile. {result.ReferenceSummary}.");

        foreach (var diagnostic in errors.Take(20))
        {
            text.AppendLine();
            text.Append("  ").Append(diagnostic);
        }

        if (errors.Count > 20)
        {
            text.AppendLine();
            text.Append($"  ... and {errors.Count - 20} more");
        }

        // Not an error result. The compile ran and answered; what it said is the answer, and
        // telling the model its tool failed would send it looking in the wrong place.
        return (text.ToString(), false);
    }

    private const string NoProject = "No project is open, so there is nothing to read or write.";

    private Services.Files.ProjectService? Project()
        => _ctx.Services.Project is { HasProject: true } project ? project : null;

    private void EnforceNothingDeclaredTwice(string path, IReadOnlyList<IndexedType> declared)
    {
        foreach (var type in declared.Where(t => !t.IsPartial))
        {
            var existing = _ctx.Services.ProjectIndex.FindType(type.Name)
                .Where(t => !t.IsPartial)
                .Select(t => _ctx.Services.ProjectIndex.FileOf(t)?.RelativePath)
                .FirstOrDefault(f => f is not null
                                     && !string.Equals(f, path, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                throw new InvalidOperationException(
                    $"{type.Name} is already declared in {existing}. Change that one rather than "
                    + "adding a second copy, or give this a different name.");
            }
        }
    }

    private IReadOnlyList<IndexedType> DeclaredTypes(string content, string relativePath, CancellationToken ct)
    {
        var temporary = Path.GetTempFileName();

        try
        {
            File.WriteAllText(temporary, content);
            return SourceFileParser.Parse(temporary, relativePath, ct)?.Types ?? Array.Empty<IndexedType>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<IndexedType>();
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // A scratch file that will not delete is not worth failing a write over.
            }
        }
    }

    private void Snapshot(string absolute)
    {
        if (_ctx.RunId is { } runId)
        {
            _ctx.Services.History.Snapshot(runId, absolute);
        }
    }

    private void Stage(string path, string content, StagedReason reason, string detail)
    {
        _ctx.Services.Staging.Stage(new StagedFile(
            path,
            Path.GetFileNameWithoutExtension(path),
            !File.Exists(path),
            "asked for by the agent",
            content,
            reason,
            detail,
            DateTimeOffset.Now));

        Record(path, Services.History.FileOutcome.Staged, detail);
    }

    private void Record(string path, Services.History.FileOutcome outcome, string? detail)
    {
        if (_ctx.RunId is { } runId)
        {
            _ctx.Services.History.RecordFile(runId, path, outcome, detail);
        }
    }

    private static string Text(JsonObject arguments, string name)
        => arguments[name]?.GetValueKind() == JsonValueKind.String
            ? arguments[name]!.GetValue<string>()
            : string.Empty;

    private static JsonObject Schema(params (string Name, string Type, string Description)[] properties)
    {
        var fields = new JsonObject();

        foreach (var (name, type, description) in properties)
        {
            fields[name] = new JsonObject { ["type"] = type, ["description"] = description };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = fields,
            ["required"] = new JsonArray(properties.Take(1).Select(p => (JsonNode?)JsonValue.Create(p.Name)).ToArray())
        };
    }
}
