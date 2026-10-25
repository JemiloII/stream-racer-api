// Mod: everything the setup window does to the disk and the network. Finding the game (Steam's registry entry, every
// Steam library, the usual folders), what version is installed (the plugin DLL's file version), the newest GitHub
// release, installing from a release zip and uninstalling.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StreamRacerSetup;

static class Mod
{
    const string GameFolderName = @"steamapps\common\Stream Racer";
    const string LatestRelease = "https://api.github.com/repos/JemiloII/stream-racer-api/releases/latest";
    public const string Page = "https://github.com/JemiloII/stream-racer-api";

    // Files the release zip puts next to StreamRacer.exe (the rest of it is a readme).
    static readonly string[] LoaderFiles = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" };

    public sealed class Release
    {
        public Version Version;
        public string ZipUrl;
    }

    public static string FindGame()
    {
        string steam = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string)
            ?.Replace('/', '\\');
        var roots = new[]
        {
            steam,
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            @"D:\Steam",
            @"D:\SteamLibrary",
            @"E:\SteamLibrary",
        }.Concat(SteamLibraries(steam));
        return roots.Where(root => !string.IsNullOrEmpty(root))
            .Select(root => Path.Combine(root, GameFolderName))
            .FirstOrDefault(IsGameFolder);
    }

    // libraryfolders.vdf lists every Steam library as "path"  "D:\\SteamLibrary".
    static string[] SteamLibraries(string steam)
    {
        string list = steam == null ? null : Path.Combine(steam, @"steamapps\libraryfolders.vdf");
        if (list == null || !File.Exists(list))
        {
            return new string[0];
        }
        return Regex.Matches(File.ReadAllText(list), "\"path\"\\s+\"([^\"]+)\"")
            .Cast<Match>()
            .Select(match => match.Groups[1].Value.Replace(@"\\", @"\"))
            .ToArray();
    }

    public static bool IsGameFolder(string folder) => folder != null && File.Exists(Path.Combine(folder, "StreamRacer.exe"));

    public static bool GameRunning() => Process.GetProcessesByName("StreamRacer").Length > 0;

    static string PluginDll(string game) => Path.Combine(game, @"BepInEx\plugins\StreamRacerApi.dll");

    public static Version Installed(string game)
    {
        string dll = PluginDll(game);
        if (!File.Exists(dll))
        {
            return null;
        }
        return Version.TryParse(FileVersionInfo.GetVersionInfo(dll).FileVersion, out var version) ? version : new Version(0, 0);
    }

    // The zip baked into this exe by scripts/release.sh (null in a dev build), and its version (= this exe's).
    public static Stream Bundled() => typeof(Mod).Assembly.GetManifestResourceStream("payload.zip");

    public static Version BundledVersion() =>
        Version.TryParse(FileVersionInfo.GetVersionInfo(typeof(Mod).Assembly.Location).FileVersion, out var version) ? version : null;

    public static Release Latest()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var web = new WebClient();
        web.Headers["User-Agent"] = "StreamRacerApiSetup";
        string json = web.DownloadString(LatestRelease);
        var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
        var zip = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.zip)\"");
        if (!tag.Success || !zip.Success || !Version.TryParse(tag.Groups[1].Value, out var version))
        {
            return null;
        }
        return new Release
        {
            Version = version,
            ZipUrl = zip.Groups[1].Value,
        };
    }

    public static Stream Download(string url)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var web = new WebClient();
        web.Headers["User-Agent"] = "StreamRacerApiSetup";
        return new MemoryStream(web.DownloadData(url));
    }

    // Copies BepInEx/ and the loader files out of a release zip into the game folder, overwriting what's there.
    public static void Install(Stream zip, string game)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0 && Belongs(entry.FullName)))
        {
            string target = Path.Combine(game, entry.FullName.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            entry.ExtractToFile(target, true);
        }
    }

    static bool Belongs(string path) => path.StartsWith("BepInEx/") || LoaderFiles.Contains(path);

    // The mod's own files, settings included; BepInEx and its loader go too when no other mod uses them.
    public static void Uninstall(string game)
    {
        string bepinex = Path.Combine(game, "BepInEx");
        DeleteFile(PluginDll(game));
        DeleteFolder(Path.Combine(bepinex, @"plugins\StreamRacerApi.uma"));
        DeleteFolder(Path.Combine(bepinex, @"config\shibiko.streamracer.images"));
        string config = Path.Combine(bepinex, "config");
        if (Directory.Exists(config))
        {
            foreach (string file in Directory.GetFiles(config, "shibiko.streamracer.*"))
            {
                DeleteFile(file);
            }
        }
        string plugins = Path.Combine(bepinex, "plugins");
        if (Directory.Exists(plugins) && Directory.EnumerateFileSystemEntries(plugins).Any())
        {
            return;   // another mod still needs BepInEx
        }
        DeleteFolder(bepinex);
        foreach (string file in LoaderFiles)
        {
            DeleteFile(Path.Combine(game, file));
        }
    }

    static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
