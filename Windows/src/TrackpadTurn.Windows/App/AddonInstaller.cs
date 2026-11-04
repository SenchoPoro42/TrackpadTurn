using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TrackpadTurn.Windows.Diagnostics;

namespace TrackpadTurn.Windows.App;

/// <summary>A WoW game flavor folder such as `_retail_` or `_classic_era_`.</summary>
internal sealed record GameFlavor(string Folder, string? InstalledVersion)
{
    public string AddOnsFolder => Path.Combine(Folder, "Interface", "AddOns");
    public string AddonFolder => Path.Combine(AddOnsFolder, AddonInstaller.AddonName);

    public string DisplayName
    {
        get
        {
            var name = Path.GetFileName(Folder);
            return name switch
            {
                "_retail_" => "Retail",
                "_classic_" => "Classic",
                "_classic_era_" => "Classic Era",
                "_anniversary_" => "Anniversary",
                "_forever_" => "Forever",
                "_ptr_" => "Retail PTR",
                "_xptr_" => "Retail Beta",
                "_beta_" => "Beta",
                "_classic_ptr_" => "Classic PTR",
                "_classic_era_ptr_" => "Classic Era PTR",
                _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Trim('_').Replace('_', ' ')),
            };
        }
    }
}

/// <summary>WoW's AddOns folder can't be written by this user.</summary>
internal sealed class AddOnsFolderNotWritableException(string folder, Exception inner)
    : IOException($"{folder} is not writable without administrator rights", inner);

internal enum InstallOutcome
{
    Installed,
    /// <summary>WoW's folder is not writable for this user; retry as administrator.</summary>
    NeedsAdministrator,
    Failed,
}

/// <summary>
/// Finds WoW installs and keeps the TrackpadTurn addon that ships inside this exe installed in
/// each game flavor. Mirrors the Mac app's AddonInstaller.
/// </summary>
/// <remarks>
/// The addon is embedded in the exe, so helper and addon always speak the same signal protocol;
/// installs that already exist are updated on launch.
/// </remarks>
internal sealed partial class AddonInstaller
{
    public const string AddonName = "TrackpadTurn";
    private const string ResourcePrefix = "Addon/TrackpadTurn/";
    public const string InstallArgument = "--install-addon";

    public AddonInstaller()
    {
        BundledVersion = VersionIn(ReadBundledFile($"{AddonName}.toc"));
    }

    public string? BundledVersion { get; }
    public IReadOnlyList<GameFlavor> Flavors { get; private set; } = [];
    public string? LastError { get; private set; }

    /// <summary>The usual install places, plus the one Battle.net records in the registry.</summary>
    public static IEnumerable<string> StandardWowRoots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "World of Warcraft");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "World of Warcraft");
        foreach (var keyPath in new[] { @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft", @"SOFTWARE\Blizzard Entertainment\World of Warcraft" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("InstallPath") is string installPath && installPath.Length > 0)
            {
                yield return RootOf(installPath);
            }
        }
    }

    /// <summary>Accepts the WoW folder or any flavor folder inside it.</summary>
    public static string RootOf(string folder)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(folder);
        return IsFlavorName(Path.GetFileName(trimmed)) ? Path.GetDirectoryName(trimmed) ?? trimmed : trimmed;
    }

    public void Refresh(string? customRoot)
    {
        var roots = StandardWowRoots().Concat(customRoot is null ? [] : [RootOf(customRoot)]);
        var found = new List<GameFlavor>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var folder in FlavorFolders(root))
            {
                if (!found.Any(flavor => string.Equals(flavor.Folder, folder, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(new GameFlavor(folder, InstalledVersion(folder)));
                }
            }
        }
        Flavors = found;
    }

    public bool NeedsInstall(GameFlavor flavor) => flavor.InstalledVersion is null || flavor.InstalledVersion != BundledVersion;

    public InstallOutcome Install(GameFlavor flavor)
    {
        try
        {
            InstallFiles(flavor.Folder);
            LastError = null;
            Log.Info($"addon {BundledVersion} installed in {flavor.Folder}");
            RefreshVersion(flavor);
            return InstallOutcome.Installed;
        }
        catch (AddOnsFolderNotWritableException)
        {
            return InstallOutcome.NeedsAdministrator;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = $"Could not install into {flavor.DisplayName}: {exception.Message}";
            Log.Error(LastError);
            return InstallOutcome.Failed;
        }
    }

    /// <summary>Runs this exe again as administrator to install into a folder this user can't write.</summary>
    public InstallOutcome InstallAsAdministrator(GameFlavor flavor)
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                ArgumentList = { InstallArgument, flavor.Folder },
            };
            using var process = Process.Start(start);
            process?.WaitForExit();
            RefreshVersion(flavor);
            if (process?.ExitCode == 0)
            {
                LastError = null;
                return InstallOutcome.Installed;
            }
            LastError = $"Could not install into {flavor.DisplayName}. The log has details.";
        }
        catch (Win32Exception)
        {
            LastError = "Install cancelled: Windows needs administrator approval to write to WoW's folder.";
        }
        return InstallOutcome.Failed;
    }

    /// <summary>Keeps existing installs in step with this helper. Never installs where the user has not.</summary>
    public IReadOnlyList<GameFlavor> UpdateExistingInstalls()
    {
        var needAdministrator = new List<GameFlavor>();
        foreach (var flavor in Flavors.Where(flavor => flavor.InstalledVersion is not null && NeedsInstall(flavor)).ToArray())
        {
            if (Install(flavor) == InstallOutcome.NeedsAdministrator)
            {
                needAdministrator.Add(flavor);
            }
        }
        return needAdministrator;
    }

    /// <summary>Entry point of `TrackpadTurn.exe --install-addon &lt;flavor folder&gt;`, run elevated.</summary>
    public static int InstallFromCommandLine(string flavorFolder)
    {
        Log.Start();
        try
        {
            if (!IsFlavorName(Path.GetFileName(Path.TrimEndingDirectorySeparator(flavorFolder))) || !Directory.Exists(flavorFolder))
            {
                Log.Error($"not a WoW flavor folder: {flavorFolder}");
                return 2;
            }
            InstallFiles(flavorFolder);
            Log.Info($"addon installed as administrator in {flavorFolder}");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"administrator install into {flavorFolder} failed", exception);
            return 1;
        }
        finally
        {
            Log.Flush();
        }
    }

    // Files

    private static void InstallFiles(string flavorFolder)
    {
        var addOns = Path.Combine(flavorFolder, "Interface", "AddOns");
        var target = Path.Combine(addOns, AddonName);
        var staging = Path.Combine(addOns, $".{AddonName}-installing");
        var previous = Path.Combine(addOns, $".{AddonName}-previous");

        try
        {
            Directory.CreateDirectory(addOns);
            DeleteFolder(staging);
            Directory.CreateDirectory(staging);
            foreach (var (relativePath, contents) in BundledFiles())
            {
                var file = Path.Combine(staging, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, contents);
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            // Only here does "access denied" mean the folder needs administrator rights. Later,
            // during the swap, it usually means a file is briefly open (an antivirus scan).
            throw new AddOnsFolderNotWritableException(addOns, exception);
        }

        try
        {
            // Swap the new copy in with renames, so WoW never loads a half-copied addon.
            if (Directory.Exists(target))
            {
                DeleteFolder(previous);
                MoveWithRetries(target, previous);
                try
                {
                    MoveWithRetries(staging, target);
                }
                catch
                {
                    // Never leave the addon renamed away: WoW would lose it, and updates only
                    // touch flavors where it is installed.
                    if (!Directory.Exists(target) && Directory.Exists(previous))
                    {
                        MoveWithRetries(previous, target);
                    }
                    throw;
                }
                TryDeleteFolder(previous);
            }
            else
            {
                MoveWithRetries(staging, target);
            }
        }
        finally
        {
            TryDeleteFolder(staging);
        }
    }

    /// <summary>A folder rename fails while any file in it is open, e.g. during an antivirus scan of new files.</summary>
    private static void MoveWithRetries(string source, string destination)
    {
        const int attempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception exception) when (attempt < attempts && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            DeleteFolder(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Info($"could not remove {folder}: {exception.Message}");
        }
    }

    private static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The addon's files, from the resources embedded at build time.</summary>
    public static IEnumerable<(string RelativePath, byte[] Contents)> BundledFiles()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            // MSBuild's %(RecursiveDir) uses the build machine's separator.
            var normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            yield return (normalized[ResourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar), buffer.ToArray());
        }
    }

    private static string? ReadBundledFile(string relativePath)
    {
        var match = BundledFiles().FirstOrDefault(file => file.RelativePath == relativePath);
        return match.Contents is null ? null : System.Text.Encoding.UTF8.GetString(match.Contents);
    }

    // Discovery

    private void RefreshVersion(GameFlavor changed) =>
        Flavors = [.. Flavors.Select(flavor => flavor.Folder == changed.Folder ? flavor with { InstalledVersion = InstalledVersion(flavor.Folder) } : flavor)];

    /// <summary>Game flavors are `_name_` folders, e.g. `_retail_`, `_classic_era_`.</summary>
    private static string[] FlavorFolders(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }
        try
        {
            return Directory.EnumerateDirectories(root)
                .Where(folder => IsFlavorName(Path.GetFileName(folder)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsFlavorName(string name) => name.Length > 2 && name.StartsWith('_') && name.EndsWith('_');

    private static string? InstalledVersion(string flavorFolder)
    {
        var toc = Path.Combine(flavorFolder, "Interface", "AddOns", AddonName, $"{AddonName}.toc");
        try
        {
            return File.Exists(toc) ? VersionIn(File.ReadAllText(toc)) ?? "unknown" : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    private static string? VersionIn(string? toc) =>
        toc is not null && TocVersion().Match(toc) is { Success: true } match ? match.Groups["version"].Value : null;

    [GeneratedRegex(@"^## Version:\s*(?<version>\S+)", RegexOptions.Multiline)]
    private static partial Regex TocVersion();
}
