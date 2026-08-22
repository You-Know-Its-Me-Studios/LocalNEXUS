using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// The work a run left unfinished, kept with the project it belongs to.
/// </summary>
/// <remarks>
/// A run no longer ends in success or failure. It ends in a state somebody can pick back up, and
/// this is where that state lives. Files that passed are already on disk; files that did not are
/// here, with what they were for and what was wrong, ready to be worked on from the chat box
/// rather than by starting the whole request again.
///
/// It is stored with the project rather than globally, and that is deliberate. The most common way
/// a resumable session goes wrong is being scoped to the directory it was created in, so that
/// opening a different project offers you somebody else's unfinished work, or worse, hides yours.
/// A file under the project answers that by construction: open that project and it is there, open
/// another and it is not.
///
/// What is recorded is what was intended, never a snapshot of the project. By the time anyone
/// comes back the project may have changed underneath, and an intention still reads correctly
/// against a changed project where a snapshot would be quietly wrong.
///
/// Mutations arrive from the run's thread and the list is bound to, so they go through the
/// dispatcher for the same reason and in the same shape as the activity feed.
/// </remarks>
public sealed partial class StagingStore : ObservableObject
{
    /// <summary>
    /// The folder inside a project where this application keeps its own state.
    /// </summary>
    /// <remarks>
    /// Defined by <see cref="Persistence.ProjectPaths"/>, which is where every project scoped path
    /// is decided. This was the first thing written into that folder and so used to be where the
    /// name lived, which meant the history store asking the staging file where a project keeps its
    /// database.
    /// </remarks>
    public const string FolderName = Persistence.ProjectPaths.FolderName;

    /// <summary>The file staged work is written to.</summary>
    public const string FileName = "staging.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dispatcher _dispatcher;

    private string? _projectPath;

    public StagingStore()
        : this(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
    {
    }

    public StagingStore(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Everything waiting to be resolved, oldest first.</summary>
    public ObservableCollection<StagedFile> Pending { get; } = new();

    /// <summary>True when the last run left something unfinished.</summary>
    public bool HasPending => Pending.Count > 0;

    /// <summary>How many files are waiting.</summary>
    public int Count => Pending.Count;

    /// <summary>One line for the status bar and the feed.</summary>
    public string Summary => Pending.Count switch
    {
        0 => string.Empty,
        1 => "1 file waiting to be resolved",
        _ => $"{Pending.Count} files waiting to be resolved"
    };

    /// <summary>
    /// Points the store at a project and reads whatever that project left behind.
    /// </summary>
    /// <remarks>
    /// Called when a project is opened and when one is closed, with null. Closing empties the list
    /// rather than keeping it, because work belonging to a project nobody has open is not work
    /// anyone can act on and showing it would invite acting on the wrong project.
    /// </remarks>
    public void OpenProject(string? projectPath)
    {
        _projectPath = string.IsNullOrWhiteSpace(projectPath) ? null : projectPath;

        Replace(_projectPath is null ? Array.Empty<StagedFile>() : Read(_projectPath));
    }

    /// <summary>
    /// Records a file the run could not finish, replacing any earlier entry for the same path.
    /// </summary>
    public void Stage(StagedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        Invoke(() =>
        {
            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Pending[i].RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    Pending.RemoveAt(i);
                }
            }

            Pending.Add(file);
            Changed();
        });
    }

    /// <summary>Forgets one file, because it was written or because nobody wants it any more.</summary>
    public void Resolve(string relativePath)
    {
        Invoke(() =>
        {
            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Pending[i].RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    Pending.RemoveAt(i);
                }
            }

            Changed();
        });
    }

    /// <summary>Forgets everything waiting.</summary>
    public void Clear() => Invoke(() =>
    {
        Pending.Clear();
        Changed();
    });

    /// <summary>
    /// The staged work as text, for handing to a run as context.
    /// </summary>
    /// <remarks>
    /// The intent and the errors rather than the code. What a run needs to know is what this file
    /// was meant to do and what stopped it, and the file's own content is already available to
    /// anything that goes looking for it.
    /// </remarks>
    public string Describe()
    {
        if (Pending.Count == 0)
        {
            return string.Empty;
        }

        var lines = Pending.Select(f =>
            $"- {f.RelativePath} ({(f.IsNewFile ? "new file" : "edit")}), intended to {f.Intent}. "
            + $"{f.ReasonText}{Environment.NewLine}  {Shorten(f.Detail)}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string Shorten(string detail)
    {
        const int limit = 600;

        var flat = detail.Replace(Environment.NewLine, Environment.NewLine + "  ", StringComparison.Ordinal);
        return flat.Length <= limit ? flat : flat[..limit] + "...";
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Summary));
        Write();
    }

    private void Replace(IReadOnlyList<StagedFile> files) => Invoke(() =>
    {
        Pending.Clear();

        foreach (var file in files)
        {
            Pending.Add(file);
        }

        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Summary));
    });

    /// <summary>Where this project's staging file lives.</summary>
    private static string PathFor(string projectPath)
        => Path.Combine(projectPath, FolderName, FileName);

    private static IReadOnlyList<StagedFile> Read(string projectPath)
    {
        var path = PathFor(projectPath);

        try
        {
            if (!File.Exists(path))
            {
                return Array.Empty<StagedFile>();
            }

            return JsonSerializer.Deserialize<List<StagedFile>>(File.ReadAllText(path), SerializerOptions)
                   ?? (IReadOnlyList<StagedFile>)Array.Empty<StagedFile>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable staged work is not worth taking the project down for, and there is
            // nothing useful to say about it beyond that it is gone.
            return Array.Empty<StagedFile>();
        }
    }

    private void Write()
    {
        if (_projectPath is null)
        {
            return;
        }

        var path = PathFor(_projectPath);

        try
        {
            var folder = Path.GetDirectoryName(path);

            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
            }

            if (Pending.Count == 0)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            File.WriteAllText(path, JsonSerializer.Serialize(Pending.ToList(), SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A project folder that cannot be written to is a real problem, and it is one the file
            // writer will report the moment a run tries to write a script into it. Saying it twice
            // from here would not add anything.
        }
    }

    private void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }
}
