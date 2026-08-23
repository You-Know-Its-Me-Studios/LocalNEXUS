using System.IO;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// Every location the application reads from or writes to on disk.
/// </summary>
/// <remarks>
/// User data lives under <c>%LOCALAPPDATA%\LocalNEXUS</c> and never inside the repository, so a
/// clone stays clean and models are never at risk of being committed.
/// </remarks>
public static class AppPaths
{
    /// <summary>Name of the llama.cpp server executable that the app spawns for local models.</summary>
    public const string LlamaServerExecutableName = "llama-server.exe";

    /// <summary>
    /// Name of the Mesh LLM node executable, which is the process the distributed path runs on.
    /// </summary>
    public const string MeshExecutableName = "mesh-llm.exe";

    /// <summary>Name of the bundled uv executable, which builds the Python runtime environment.</summary>
    public const string UvExecutableName = "uv.exe";

    /// <summary>Root of the per user data folder.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocalNEXUS");

    /// <summary>Default folder scanned for GGUF model files.</summary>
    public static string Models { get; } = Path.Combine(Root, "models");

    /// <summary>Folder that application and llama-server logs are written to.</summary>
    public static string Logs { get; } = Path.Combine(Root, "logs");

    /// <summary>Folder holding state about the current run rather than the user's own data.</summary>
    public static string Runtime { get; } = Path.Combine(Root, "runtime");

    /// <summary>Path of the persisted application configuration.</summary>
    public static string ConfigFile { get; } = Path.Combine(Root, "config.json");

    /// <summary>
    /// Where the engine processes this application starts are recorded, so a later session can
    /// recognise anything a crash left behind.
    /// </summary>
    public static string ChildProcessFile { get; } = Path.Combine(Runtime, "children.json");

    /// <summary>
    /// Root of the supervised Python runtime: its interpreter, its environment and its download
    /// cache. Under the user data folder rather than the install directory, so an install can be
    /// replaced or run from a read only location without taking the environment with it.
    /// </summary>
    public static string PythonRoot { get; } = Path.Combine(Runtime, "python");

    /// <summary>The virtual environment the safetensors runtime is served from.</summary>
    public static string PythonVenv { get; } = Path.Combine(PythonRoot, ".venv");

    /// <summary>The interpreter inside that environment. This is the only Python the app runs.</summary>
    public static string PythonExecutable { get; } = Path.Combine(PythonVenv, "Scripts", "python.exe");

    /// <summary>Where uv keeps the standalone interpreters it downloads.</summary>
    public static string PythonInterpreters { get; } = Path.Combine(PythonRoot, "interpreters");

    /// <summary>Where uv keeps downloaded wheels, so a repair does not download them again.</summary>
    public static string PythonCache { get; } = Path.Combine(PythonRoot, "cache");

    /// <summary>What the environment was last provisioned from, so a finished install can be recognised.</summary>
    public static string PythonStateFile { get; } = Path.Combine(PythonRoot, "environment.json");

    /// <summary>
    /// The user editable list of extra folders scanned for models, in either format. A plain
    /// text file rather than a buried setting, because adding a drive full of models should be
    /// one line in one file.
    /// </summary>
    public static string ModelPathsFile { get; } = Path.Combine(Root, "model-paths.txt");

    /// <summary>Where GGUF files are suggested to go.</summary>
    public static string ModelsGguf { get; } = Path.Combine(Models, "gguf");

    /// <summary>Where safetensors model folders are suggested to go.</summary>
    public static string ModelsSafetensors { get; } = Path.Combine(Models, "safetensors");

    /// <summary>Reserved for embedding models. Empty and unused today.</summary>
    public static string ModelsEmbeddings { get; } = Path.Combine(Models, "embeddings");

    /// <summary>Creates the data folders on first run. Safe to call repeatedly.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Models);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Runtime);

        EnsureModelFolders();
    }

    /// <summary>
    /// The last line of every note this application writes into a model folder.
    /// </summary>
    /// <remarks>
    /// A marker, so these can be kept current without overwriting somebody's own words. A note
    /// carrying this line is one this application wrote and is replaced when the text changes; a
    /// note without it has been edited and is left exactly as it is. Deleting the line is how
    /// somebody claims the file.
    /// </remarks>
    private const string NoteMarker =
        "This file is written by LocalNEXUS and replaced when it changes. Delete this line to keep your own version.";

    /// <summary>
    /// Creates the typed model folders, each with a note saying what belongs in it.
    /// </summary>
    /// <remarks>
    /// Organisation for people, not something anything here depends on. Format is decided by
    /// reading the file, so a GGUF sitting in the safetensors folder loads exactly as it would
    /// anywhere else, and nothing refuses a file for being in the wrong place. What a flat folder
    /// costs is the signal: somebody arriving from another tool has no idea where anything goes,
    /// and an empty directory does not tell them.
    ///
    /// Nothing is moved. An install that already has a flat folder full of models keeps working
    /// exactly as it did, and gains three empty folders it is free to ignore.
    /// </remarks>
    private static void EnsureModelFolders()
    {
        Create(ModelsGguf, GgufNote, "GGUF models go here.");
        Create(ModelsSafetensors, SafetensorsNote, "Safetensors models go here.");
        Create(ModelsEmbeddings, EmbeddingsNote, "Embedding models will go here.");
    }

    private const string GgufNote = """
        # GGUF models

        Put `.gguf` files in this folder. One file is one model.

        ## Adding one

        Copy or move the file in. LocalNEXUS finds it the next time it scans, which happens at
        startup and whenever you rescan from the Models section of Settings. Nothing has to be
        registered and nothing has to be renamed.

        Subfolders work and are searched, so this is fine:

        ```
        gguf\
          qwen2.5-coder-7b-instruct-q4_k_m.gguf
          llama\
            llama-3.1-8b-instruct-q5_k_m.gguf
        ```

        ## Getting one

        Hugging Face is where most of them are. Search for the model you want with "GGUF" in the
        name, open the Files tab, and download a single `.gguf`. You do not need the rest of the
        repository.

        A file name usually ends with its quantization, such as `q4_k_m` or `q8_0`. Lower numbers
        are smaller and faster and lose more quality. `q4_k_m` is the usual starting point, and
        `q5_k_m` or `q6_k` are worth it if the model fits in your card with room to spare.

        A 7B model at `q4_k_m` is roughly 4.5 GB and wants about 6 GB of VRAM with a useful context
        window. Below 7B you will mostly watch the compiler check catch things.

        ## Using one

        Add a Model node to the canvas, click it, choose Local, and pick the model from the list.

        ## Keeping models somewhere else

        You do not have to keep them here. Either point a Model node straight at a file anywhere on
        disk, or list the folders you keep models in, one per line, in `model-paths.txt` beside this
        application's config. Both are scanned exactly like this folder.

        ## This folder is a suggestion

        A model is recognised by reading the file, never by where it sits, so one in the wrong
        folder still loads and nothing is refused for being misfiled. The folders exist so you can
        tell at a glance where things are.

        """;

    private const string SafetensorsNote = """
        # Safetensors models

        Put one folder per model in here. A model is a folder, not a file.

        ## What a valid one looks like

        ```
        safetensors\
          Qwen2.5-Coder-7B-Instruct\
            config.json
            model-00001-of-00004.safetensors
            model-00002-of-00004.safetensors
            model.safetensors.index.json
            tokenizer.json
            tokenizer_config.json
        ```

        `config.json` has to be there, beside the weights. That file is what says this is a model
        rather than a folder with some tensors in it.

        A lone `.safetensors` file with no `config.json`, or a folder of weights without one, is
        reported as exactly that and is not offered as a model. It is not an error and nothing is
        wrong with the file; there is just not enough there to serve.

        ## Getting one

        Download the whole repository from Hugging Face rather than picking files out of it. The
        Hugging Face CLI is the least error prone way:

        ```
        huggingface-cli download Qwen/Qwen2.5-Coder-7B-Instruct --local-dir Qwen2.5-Coder-7B-Instruct
        ```

        Run that from inside this folder and it lands correctly. Cloning the repository with git
        also works if you have git lfs set up.

        ## The Python runtime

        These are served through Python, which LocalNEXUS builds for itself in the background the
        first time it starts. That download is roughly 3 GB on an NVIDIA card because it pulls a
        CUDA build of torch, and a few hundred megabytes otherwise. Nothing waits on it and you can
        keep working while it runs.

        GGUF models never touch any of that. If you only ever use GGUF, the Python runtime is
        wasted effort and you can ignore it.

        Safetensors are unquantized, so they want considerably more memory than the same model as
        GGUF. A 7B model is around 15 GB in fp16. Use GGUF unless you have a specific reason not to.

        ## Using one

        Add a Model node to the canvas, click it, choose Local, and pick the model from the list.
        Which runtime serves it is worked out from what the folder contains; you are not asked.

        ## This folder is a suggestion

        A model is recognised by reading what is there, never by where it sits, so one in the wrong
        folder still loads and nothing is refused for being misfiled.

        """;

    private const string EmbeddingsNote = """
        # Embedding models

        Nothing uses this folder yet. There is nothing for you to do with it.

        It is reserved for semantic search over the project index. Searching your run history today
        is keyword matching: it finds the words that were actually written, which costs nothing and
        needs no model, and it does not find a different word meaning the same thing. Adding
        embeddings is how that would be fixed, and it has deliberately not been built.

        Putting a model in here will not make anything happen. It will not break anything either,
        and a model that happens to be servable will simply appear in the model list like any other,
        because models are found by reading files rather than by which folder they are in.

        If you are looking for somewhere to put a model you intend to use, it is `..\gguf` or
        `..\safetensors`.

        """;

    /// <summary>
    /// Creates one model folder and its note, without overwriting one somebody has edited.
    /// </summary>
    /// <param name="folder">The folder to create.</param>
    /// <param name="note">What to write.</param>
    /// <param name="firstShipped">
    /// The opening line of the first note shipped for this folder, which carried no marker.
    /// </param>
    /// <remarks>
    /// The first version of these notes went out before there was a way to tell an application
    /// written file from an edited one, so an install that already has one would keep it forever
    /// and never see a correction. Recognising that opening line is the one time transition out of
    /// that, and it is narrow enough not to catch anything somebody wrote themselves.
    /// </remarks>
    private static void Create(string folder, string note, string firstShipped)
    {
        try
        {
            Directory.CreateDirectory(folder);

            var readme = Path.Combine(folder, "README.md");
            var text = note.ReplaceLineEndings().TrimEnd() + Environment.NewLine + Environment.NewLine
                       + NoteMarker + Environment.NewLine;

            if (File.Exists(readme))
            {
                var existing = File.ReadAllText(readme);

                var ours = existing.Contains(NoteMarker, StringComparison.Ordinal)
                           || existing.TrimStart().StartsWith(firstShipped, StringComparison.Ordinal);

                if (!ours)
                {
                    return;
                }
            }

            File.WriteAllText(readme, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that will not be created is a tidiness problem, not a working one. Nothing
            // depends on these existing, so failing a launch over one would be the wrong trade.
        }
    }

    /// <summary>Returns a timestamped log file path inside <see cref="Logs"/>.</summary>
    public static string CreateLogFilePath(string prefix)
    {
        Directory.CreateDirectory(Logs);
        var safePrefix = string.Concat(prefix.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Logs, $"{safePrefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
    }

    /// <summary>
    /// Locates the bundled llama-server executable.
    /// </summary>
    /// <remarks>
    /// The binaries are fetched by the user rather than committed, so they can sit either next
    /// to the built application or in the repository's <c>vendor\llama</c> folder while working
    /// from a development build. Both are searched, followed by the user data folder.
    /// </remarks>
    public static string? FindLlamaServerExecutable() => FindLlamaExecutable(LlamaServerExecutableName);

    /// <summary>
    /// Locates the bundled Mesh LLM executable. Its release bundle carries a native runtime
    /// tree beside the executable, so the whole bundle is placed under <c>vendor\mesh</c>
    /// rather than the executable alone.
    /// </summary>
    public static string? FindMeshExecutable()
    {
        foreach (var candidate in EnumerateMeshSearchDirectories())
        {
            var executable = Path.Combine(candidate, MeshExecutableName);
            if (File.Exists(executable))
            {
                return executable;
            }
        }

        return null;
    }

    /// <summary>Every directory searched for the Mesh LLM executable, in priority order.</summary>
    public static IEnumerable<string> EnumerateMeshSearchDirectories()
    {
        foreach (var directory in EnumerateVendorDirectories("mesh"))
        {
            yield return directory;

            // The published release bundle keeps the executable one level down beside its
            // native runtimes, so both shapes resolve without the user rearranging anything.
            yield return Path.Combine(directory, "mesh-bundle");
        }
    }

    private static string? FindLlamaExecutable(string executableName)
    {
        foreach (var candidate in EnumerateLlamaSearchDirectories())
        {
            var executable = Path.Combine(candidate, executableName);
            if (File.Exists(executable))
            {
                return executable;
            }
        }

        return null;
    }

    /// <summary>Every directory searched for the llama.cpp executables, in priority order.</summary>
    public static IEnumerable<string> EnumerateLlamaSearchDirectories() => EnumerateVendorDirectories("llama");

    /// <summary>Locates the bundled uv executable, or null when it was not shipped with this build.</summary>
    public static string? FindUvExecutable()
    {
        foreach (var candidate in EnumerateUvSearchDirectories())
        {
            var executable = Path.Combine(candidate, UvExecutableName);
            if (File.Exists(executable))
            {
                return executable;
            }
        }

        return null;
    }

    /// <summary>Every directory searched for uv, in priority order.</summary>
    public static IEnumerable<string> EnumerateUvSearchDirectories() => EnumerateVendorDirectories("uv");

    /// <summary>
    /// Locates one of the committed dependency lockfiles. These are resolved once and committed
    /// rather than resolved on the user's machine, so two installs of the same build get the
    /// same packages whatever the index happens to be serving that day.
    /// </summary>
    public static string? FindPythonLockfile(string fileName)
    {
        foreach (var candidate in EnumeratePythonSearchDirectories())
        {
            var lockfile = Path.Combine(candidate, fileName);
            if (File.Exists(lockfile))
            {
                return lockfile;
            }
        }

        return null;
    }

    /// <summary>Every directory searched for the Python lockfiles, in priority order.</summary>
    public static IEnumerable<string> EnumeratePythonSearchDirectories() => EnumerateVendorDirectories("python");

    /// <summary>
    /// Every place a bundled vendor folder may live, in priority order. Resolution has to give
    /// the same answer from a development run and from the published single file executable,
    /// which is why the process path is yielded alongside the base directory rather than one
    /// being assumed to equal the other.
    /// </summary>
    private static IEnumerable<string> EnumerateVendorDirectories(string vendorName)
    {
        var baseDirectory = AppContext.BaseDirectory;

        yield return Path.Combine(baseDirectory, "vendor", vendorName);

        if (Environment.ProcessPath is { } processPath
            && Path.GetDirectoryName(processPath) is { } processDirectory
            && !string.Equals(processDirectory, baseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(processDirectory, "vendor", vendorName);
        }

        // Walk up from the build output towards the repository root so that a development run
        // finds the vendor folder without a build step that copies the binaries around.
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            yield return Path.Combine(directory.FullName, "vendor", vendorName);
            directory = directory.Parent;
        }

        yield return Path.Combine(Root, "vendor", vendorName);
    }
}
