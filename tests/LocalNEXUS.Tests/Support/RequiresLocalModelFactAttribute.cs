using System.IO;
using LocalNEXUS.App.Services.Persistence;
using Xunit;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A test that needs a real engine and a real model, and is skipped when either is absent.
/// </summary>
/// <remarks>
/// These used to fail on a machine without a model, which meant a fresh clone's first
/// <c>dotnet test</c> went red for a reason that was nobody's fault and had nothing to do with
/// the code. A red suite that is red by design teaches people to ignore a red suite, and the
/// next failure is the one that mattered.
///
/// Skipping says the same thing without the false alarm: the reason names what is missing and
/// where to put it, so somebody who wants to run this layer can, and somebody who does not is
/// not told their checkout is broken.
///
/// The check runs when the attribute is constructed, which xUnit does during discovery. That is
/// early enough to skip and late enough to see the file system, which a compile time constant
/// could not. This is the standard way to do it on xUnit 2, which has no runtime skip of its own.
/// </remarks>
public sealed class RequiresLocalModelFactAttribute : FactAttribute
{
    public RequiresLocalModelFactAttribute()
    {
        if (Skip is not null)
        {
            return;
        }

        Skip = DescribeWhatIsMissing();
    }

    /// <summary>What is missing, or null when this layer can run.</summary>
    internal static string? DescribeWhatIsMissing()
    {
        if (AppPaths.FindLlamaServerExecutable() is null)
        {
            return "llama-server was not found. Put a llama.cpp Windows build in vendor/llama "
                + "beside the repository, or beside the published exe. Nothing here downloads it.";
        }

        if (!Directory.Exists(AppPaths.ModelsGguf)
            || !Directory.EnumerateFiles(AppPaths.ModelsGguf, "*.gguf", SearchOption.AllDirectories).Any())
        {
            return $"No GGUF model was found under {AppPaths.ModelsGguf}. Put one there to run "
                + "this layer. Nothing here downloads a model.";
        }

        return null;
    }
}
