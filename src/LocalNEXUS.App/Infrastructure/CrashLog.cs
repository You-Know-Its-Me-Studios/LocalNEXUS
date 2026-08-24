using System.IO;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Infrastructure;

/// <summary>
/// Writes unhandled exceptions to the logs folder.
/// </summary>
/// <remarks>
/// A dialog is easy to dismiss and impossible to quote later. Writing the same detail to a file
/// means a failure can still be diagnosed after the fact, and the dialog only has to say where
/// to look.
/// </remarks>
public static class CrashLog
{
    /// <summary>
    /// Records a fault the application recovered from, and returns the file, or null when even
    /// logging failed, which must never itself become an error.
    /// </summary>
    /// <remarks>
    /// A separate name from a crash, and that distinction is the whole point of this method
    /// existing. Everything that goes wrong used to be written as a crash report, so a handled
    /// exception on the dispatcher, a faulted task nobody awaited and a background scan that gave
    /// up all left a file that told the next launch the application had crashed. It had not: it
    /// caught the fault, said so, and carried on, and being told it crashed on every launch after
    /// that is how a real crash report stops being read.
    ///
    /// Both kinds are kept, because the diagnosis is worth the same either way. Only a crash is
    /// worth interrupting somebody about.
    /// </remarks>
    public static string? WriteFault(string context, Exception exception)
        => Write("fault", "LocalNEXUS fault report", context, exception);

    /// <summary>
    /// Records an exception that ended the process, and returns the file it was written to.
    /// </summary>
    /// <remarks>
    /// This is the one the next launch asks about, so it is reserved for a fault the application
    /// could not carry on from.
    /// </remarks>
    public static string? Write(string context, Exception exception)
        => Write("crash", "LocalNEXUS crash report", context, exception);

    private static string? Write(string prefix, string heading, string context, Exception exception)
    {
        try
        {
            AppPaths.EnsureCreated();
            var path = AppPaths.CreateLogFilePath(prefix);

            var content =
                $"{heading}{Environment.NewLine}" +
                $"Time: {DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"Context: {context}{Environment.NewLine}{Environment.NewLine}" +
                exception;

            File.WriteAllText(path, content);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
