using System.IO;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// That running the tests cannot touch the settings of whoever is running them.
/// </summary>
/// <remarks>
/// This is the guard on a real incident rather than a hypothetical one. The suite wrote to the
/// real config.json: view models save when a setting changes, the tests build view models, and
/// the data folder resolved to the running user's LocalAppData. Hashing that file either side of
/// a single run showed 31 keys become 30, and the key removed was the record of which crash had
/// already been reported, so the application announced a two day old crash on every launch and the
/// Python consent was lost the same way.
///
/// Two things stop it now and both are checked here, because either one alone leaves a way back
/// in: the data folder is redirected away from the real one, and a configuration object that was
/// not loaded from disk refuses to write over it.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ConfigSafetyTests
{
    /// <summary>Where a real install keeps its settings, worked out the way the application does.</summary>
    private static string RealHome => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocalNEXUS");

    [Fact]
    public void TheTestsDoNotPointAtTheRealDataFolder()
    {
        Assert.NotEqual(
            Path.GetFullPath(RealHome),
            Path.GetFullPath(AppPaths.Root));

        Assert.DoesNotContain(
            Path.GetFullPath(RealHome),
            Path.GetFullPath(AppPaths.ConfigFile),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A configuration nobody loaded cannot write itself over one somebody did.
    /// </summary>
    /// <remarks>
    /// This is what the tests were doing without meaning to. An object built with new is a set of
    /// defaults; writing it over the file removes every answer the file was holding, and does it
    /// silently, because a save that succeeds looks exactly like a save that was correct.
    /// </remarks>
    [Fact]
    public void AConfigurationNobodyLoadedDoesNotWriteItself()
    {
        AppPaths.EnsureCreated();

        var existing = new AppConfig { LastReportedCrash = "crash-first.log" };

        // Only the loader may claim the file, so this is how a real one is made.
        File.WriteAllText(AppPaths.ConfigFile, "{ \"LastReportedCrash\": \"crash-first.log\" }");

        var before = File.ReadAllText(AppPaths.ConfigFile);

        // Exactly what a test does: build one, change something, save.
        var scratch = new AppConfig { Theme = existing.Theme };
        scratch.Save();

        Assert.Equal(before, File.ReadAllText(AppPaths.ConfigFile));
    }

    /// <summary>The one that was loaded still writes, or none of this would be settings at all.</summary>
    [Fact]
    public void TheLoadedConfigurationStillSaves()
    {
        AppPaths.EnsureCreated();

        File.WriteAllText(AppPaths.ConfigFile, "{ \"LastReportedCrash\": \"crash-first.log\" }");

        var loaded = AppConfig.Load();
        Assert.Equal("crash-first.log", loaded.LastReportedCrash);

        loaded.LastReportedCrash = "crash-second.log";
        loaded.Save();

        Assert.Equal("crash-second.log", AppConfig.Load().LastReportedCrash);
    }
}
