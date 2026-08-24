using System.IO;
using System.Runtime.CompilerServices;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// Points the data folder at a scratch directory before anything in the tests can look at it.
/// </summary>
/// <remarks>
/// The tests used to write to the real one. They construct configuration objects for the view
/// models they exercise, those view models save whenever a setting is touched, and AppPaths
/// resolved to the LocalAppData folder of whoever was running them, so a test run wrote defaults
/// over a real config.json.
///
/// It was found by hashing that file either side of one run: 953 bytes and 31 keys became 896 and
/// 30, and the key that went was the record of which crash had already been reported. The next
/// launch of the application therefore announced a crash from two days earlier, every time,
/// because every test run undid the answer. The Python consent went the same way, which is why
/// the application kept asking permission to build a runtime that was already on the disk.
///
/// A module initialiser rather than a fixture, because it has to happen before the first static
/// read of AppPaths.Root, and a static is read the first time anything touches the class. A
/// fixture runs too late: whichever test collection loaded first would already have resolved the
/// real path.
///
/// The directory is left behind rather than deleted. It is under the temporary folder, it is
/// small, and a test that fails is easier to understand when what it wrote is still there.
/// </remarks>
internal static class ScratchHome
{
    /// <summary>The variable AppPaths reads, and the folder it is pointed at.</summary>
    private const string Variable = "LOCALNEXUS_HOME";

    [ModuleInitializer]
    internal static void Redirect()
    {
        // A run of the suite gets its own, so two running at once cannot fight over one folder.
        var scratch = Path.Combine(
            Path.GetTempPath(),
            "LocalNEXUS.Tests",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Directory.CreateDirectory(scratch);

        Environment.SetEnvironmentVariable(Variable, scratch);
    }
}
