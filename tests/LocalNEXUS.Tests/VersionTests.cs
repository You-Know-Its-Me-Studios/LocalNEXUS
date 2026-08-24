using System.Text.RegularExpressions;
using LocalNEXUS.App.Services.Diagnostics;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The version the status bar shows, against the version the build actually is.
/// </summary>
/// <remarks>
/// The number used to be written down in four places and kept in step by hand, which is a thing
/// that works until the once it does not, and fails silently when it does: a stale number renders
/// perfectly and is wrong. It is declared once in Directory.Build.props now and read back off the
/// assembly, and this is what holds that. The status bar binds to <see cref="AppVersion.Display"/>
/// through x:Static, so asserting on it is asserting on what is drawn.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class VersionTests
{
    [Fact]
    public void TheStatusBarShowsTheAssemblyVersion()
    {
        var assembly = typeof(AppVersion).Assembly.GetName().Version;

        Assert.NotNull(assembly);
        Assert.Equal(assembly.ToString(3), AppVersion.Number);
        Assert.Equal("v" + assembly.ToString(3), AppVersion.Display);
    }

    /// <summary>
    /// Three numbers, and not the fallback.
    /// </summary>
    /// <remarks>
    /// Dropping the Version element from Directory.Build.props does not break anything loudly. The
    /// assembly falls back to 1.0.0.0 and the corner of the status bar goes on looking right while
    /// reporting a version nobody released. This catches the shape rather than the value, because
    /// the value is meant to change and the shape is not.
    /// </remarks>
    [Fact]
    public void TheVersionReadsAsThreeNumbers()
    {
        Assert.Matches(new Regex(@"^v\d+\.\d+\.\d+$"), AppVersion.Display);
        Assert.NotEqual("0.0.0", AppVersion.Number);
    }
}
