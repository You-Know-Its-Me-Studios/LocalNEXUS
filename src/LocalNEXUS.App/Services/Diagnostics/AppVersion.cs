namespace LocalNEXUS.App.Services.Diagnostics;

/// <summary>
/// Which build of the application this is.
/// </summary>
/// <remarks>
/// Read off the assembly rather than written down, because a number written down is a number that
/// goes stale silently: nothing fails, nothing warns, and the status bar goes on reporting a
/// version that shipped months ago. The assembly carries what Directory.Build.props declared, so
/// this cannot disagree with the build it is running in.
///
/// Three parts rather than four. The fourth is always zero here, and the informational version
/// carries a commit hash appended by source link, which is right for a crash report and wrong for
/// a corner of the status bar.
/// </remarks>
public static class AppVersion
{
    /// <summary>The number on its own, as in 0.2.0.</summary>
    public static string Number { get; } =
        typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>The number as the status bar shows it, as in v0.2.0.</summary>
    public static string Display { get; } = "v" + Number;
}
