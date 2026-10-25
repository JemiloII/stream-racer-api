// UpdatePrompt: an in-game window when a newer release is out (Updates) or this build doesn't fit the game version
// (Plugin.Off). Update now = download the release zip, quit, a small batch file swaps the files once the game has
// closed and starts it again through Steam. Uninstall = the same batch trick, removing the mod's files instead.
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine.UI;

namespace StreamRacerApi;

static class UpdatePrompt
{
    const string SteamAppId = "1333410";

    static bool _dismissed, _confirmUninstall, _quit, _failed;
    static string _status;   // "Downloading…", or what went wrong
    static GUIStyle _text, _button;
    static GameObject _blocker;   // invisible full-screen UI panel: clicks on the window don't reach the game's menus

    static string GameDir => BepInEx.Paths.GameRootPath;

    public static void Draw()
    {
        if (_quit)
        {
            Application.Quit();
            return;
        }
        bool update = Updates.UpToDate == false && Updates.Url != null;
        bool show = !_dismissed && (Plugin.Off || update);
        Block(show);
        if (!show)
        {
            return;
        }
        _text ??= new GUIStyle(GUI.skin.label) { fontSize = 24, wordWrap = true };
        _button ??= new GUIStyle(GUI.skin.button) { fontSize = 22, fixedHeight = 44 };
        float scale = Screen.height / 1080f;   // laid out for 1080p, scaled to the real screen
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        var area = new Rect((Screen.width / scale - 760) / 2f, (1080 - 340) / 2f, 760, 340);
        GUI.Box(area, "");
        GUI.Box(area, "");   // twice: the default box is see-through
        GUILayout.BeginArea(new Rect(area.x + 24, area.y + 20, area.width - 48, area.height - 40));
        GUILayout.Label(Message(update), _text);
        GUILayout.FlexibleSpace();
        if (_status != null)
        {
            GUILayout.Label(_status, _text);
            if (_failed && GUILayout.Button("Close", _button))
            {
                _dismissed = true;
            }
        }
        else
        {
            Buttons(update);
        }
        GUILayout.EndArea();
    }

    static void Block(bool on)
    {
        if (_blocker == null && on)
        {
            _blocker = new GameObject("sr-update-blocker", typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            Object.DontDestroyOnLoad(_blocker);
            var canvas = _blocker.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            _blocker.GetComponent<Image>().color = Color.clear;   // still catches raycasts
        }
        if (_blocker != null && _blocker.activeSelf != on)
        {
            _blocker.SetActive(on);
        }
    }

    static string Message(bool update)
    {
        if (Plugin.Off && update)
        {
            return $"Stream Racer API doesn't work with this game version, so it's turned off. "
                + $"Version {Updates.Latest} is ready and may fix that.";
        }
        if (Plugin.Off)
        {
            return "Stream Racer API doesn't work with this game version yet, so it's turned off and your game runs "
                + "untouched. Check back later for an update.";
        }
        return $"Stream Racer API {Updates.Latest} is out (you have {Plugin.Version}).";
    }

    static void Buttons(bool update)
    {
        if (update && GUILayout.Button("Update now (the game restarts)", _button))
        {
            Download(Updates.Url);
        }
        if (GUILayout.Button(Plugin.Off ? "Play without the mod" : "Not now", _button))
        {
            _dismissed = true;
        }
        string uninstall = _confirmUninstall ? "Sure? Click again to uninstall (the game restarts)" : "Uninstall the mod";
        if (GUILayout.Button(uninstall, _button))
        {
            if (_confirmUninstall)
            {
                RunAfterQuit(UninstallScript());
            }
            _confirmUninstall = true;
        }
    }

    static void Download(string url)
    {
        _status = "Downloading the update…";
        string zip = Path.Combine(Path.GetTempPath(), "stream-racer-api-update.zip");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using var web = new WebClient();
                web.Headers["User-Agent"] = "StreamRacerApi/" + Plugin.Version;
                web.DownloadFile(url, zip);
                RunAfterQuit(UpdateScript(zip));
            }
            catch (System.Exception error)
            {
                Plugin.Log.LogWarning("update: " + error);
                _failed = true;
                _status = "The update didn't work (" + error.GetBaseException().Message + "). The mod stays as it is; "
                    + "you can update by hand from github.com/JemiloII/stream-racer-api.";
            }
        });
    }

    // Writes the batch file, starts it hidden and quits; it waits for the game to close before touching anything.
    static void RunAfterQuit(string body)
    {
        string script = Path.Combine(Path.GetTempPath(), "stream-racer-api-swap.bat");
        string wait = ":wait\r\n"
            + "tasklist /FI \"IMAGENAME eq StreamRacer.exe\" | find /I \"StreamRacer.exe\" >nul "
            + "&& (timeout /t 1 /nobreak >nul & goto wait)\r\n";
        string relaunch = $"start \"\" \"steam://rungameid/{SteamAppId}\"\r\ndel \"%~f0\"\r\n";
        File.WriteAllText(script, "@echo off\r\n" + wait + body + relaunch);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        _status = "Closing the game…";
        _quit = true;
    }

    // Unzips to a temp folder and copies only the loader and BepInEx (the zip also holds the installer and a readme).
    static string UpdateScript(string zip)
    {
        string unpacked = Path.Combine(Path.GetTempPath(), "stream-racer-api-update");
        return $"rmdir /S /Q \"{unpacked}\" 2>nul\r\n"
            + $"mkdir \"{unpacked}\"\r\n"
            + $"tar -xf \"{zip}\" -C \"{unpacked}\"\r\n"
            + $"xcopy /E /Y /Q /I \"{unpacked}\\BepInEx\" \"{GameDir}\\BepInEx\" >nul\r\n"
            + $"copy /Y \"{unpacked}\\winhttp.dll\" \"{GameDir}\" >nul\r\n"
            + $"copy /Y \"{unpacked}\\doorstop_config.ini\" \"{GameDir}\" >nul\r\n"
            + $"rmdir /S /Q \"{unpacked}\"\r\n"
            + $"del \"{zip}\"\r\n";
    }

    // The mod's own files; BepInEx itself only goes too when no other mod is left in the plugins folder.
    static string UninstallScript()
    {
        string bep = Path.Combine(GameDir, "BepInEx");
        return $"del /Q \"{bep}\\plugins\\StreamRacerApi.dll\"\r\n"
            + $"rmdir /S /Q \"{bep}\\plugins\\StreamRacerApi.uma\" 2>nul\r\n"
            + $"rmdir /S /Q \"{bep}\\config\\shibiko.streamracer.images\" 2>nul\r\n"
            + $"del /Q \"{bep}\\config\\shibiko.streamracer.*\" 2>nul\r\n"
            + $"dir /B /A \"{bep}\\plugins\" 2>nul | find /V \"\" >nul || (\r\n"
            + $"  rmdir /S /Q \"{bep}\"\r\n"
            + $"  del /Q \"{GameDir}\\winhttp.dll\" \"{GameDir}\\doorstop_config.ini\" \"{GameDir}\\.doorstop_version\" 2>nul\r\n"
            + ")\r\n";
    }
}
