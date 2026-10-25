// Stream Racer API Setup: one window to install, update or uninstall the mod. Run it again any time: it shows what is
// installed, checks GitHub for a newer release and offers the right button.
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StreamRacerSetup;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm(args.Length > 0 ? args[0] : null));   // a game folder argument skips the search
    }
}

sealed class SetupForm : Form
{
    readonly Label _folder = Line(), _installed = Line(), _latest = Line(), _status = Line();
    readonly Button _main = Action(), _uninstall = Action("Uninstall"), _change = Action("Change folder…");
    string _game;
    Mod.Release _online;
    bool _checked, _busy;

    public SetupForm(string game)
    {
        _game = game ?? Mod.FindGame();
        Text = "Stream Racer API Setup";
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var title = Line();
        title.Text = "Stream Racer API";
        title.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _main, _uninstall, _change });
        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Padding = new Padding(20),
        };
        layout.Controls.AddRange(new Control[] { title, _folder, _installed, _latest, buttons, _status });
        Controls.Add(layout);

        _main.Click += async (_, _) => await Run(Install);
        _uninstall.Click += async (_, _) => await Run(Uninstall);
        _change.Click += (_, _) => ChooseFolder();
        Shown += async (_, _) => await CheckOnline();
        Refresh();
    }

    static Label Line() => new() { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 4, 0, 4) };

    static Button Action(string text = "") => new() { Text = text, AutoSize = true, Padding = new Padding(10, 4, 10, 4) };

    async Task CheckOnline()
    {
        try
        {
            _online = await Task.Run(Mod.Latest);
        }
        catch
        {
            _online = null;   // offline: the bundled version still installs
        }
        _checked = true;
        Refresh();
    }

    // The newest version on hand: this exe's own copy or the GitHub release, whichever is newer.
    Version Best()
    {
        var bundled = Mod.Bundled() != null ? Mod.BundledVersion() : null;
        if (_online != null && (bundled == null || _online.Version > bundled))
        {
            return _online.Version;
        }
        return bundled;
    }

    new void Refresh()
    {
        bool found = Mod.IsGameFolder(_game);
        var installed = found ? Mod.Installed(_game) : null;
        var best = Best();
        _folder.Text = found ? "Game folder: " + _game : "Couldn't find Stream Racer. Click Change folder… and pick the "
            + "folder with StreamRacer.exe (in Steam: right-click the game › Manage › Browse local files).";
        _installed.Text = installed == null ? "Installed: not yet" : "Installed: " + Show(installed);
        _latest.Text = !_checked ? "Newest: checking GitHub…"
            : _online == null ? "Newest: couldn't check online, this setup has " + Show(Best())
            : "Newest: " + Show(_online.Version);
        _main.Text = installed == null ? "Install " + Show(best)
            : best != null && installed < best ? "Update to " + Show(best)
            : "Reinstall";
        _main.Enabled = found && best != null && !_busy;
        _uninstall.Enabled = installed != null && !_busy;
        _change.Enabled = !_busy;
    }

    static string Show(Version version) => version?.ToString() ?? "";

    void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Pick the Stream Racer folder (the one with StreamRacer.exe)" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        if (!Mod.IsGameFolder(dialog.SelectedPath))
        {
            MessageBox.Show(this, "That folder has no StreamRacer.exe in it.", Text);
            return;
        }
        _game = dialog.SelectedPath;
        Refresh();
    }

    // Runs one job off the UI thread with the buttons locked, turning failures into a plain sentence.
    async Task Run(Func<string> job)
    {
        if (Mod.GameRunning())
        {
            _status.Text = "Stream Racer is running. Close the game first, then click again.";
            return;
        }
        _busy = true;
        Refresh();
        try
        {
            _status.Text = await Task.Run(job);
        }
        catch (UnauthorizedAccessException)
        {
            _status.Text = "Windows wouldn't let me change the game folder. Close this, right-click "
                + "\"Stream Racer API Setup\" › Run as administrator, and try again.";
        }
        catch (Exception error)
        {
            _status.Text = "That didn't work: " + error.Message + " Check your internet connection and try again.";
        }
        _busy = false;
        Refresh();
    }

    string Install()
    {
        var bundled = Mod.Bundled();
        bool online = _online != null && (bundled == null || _online.Version > Mod.BundledVersion());
        using (var zip = online ? Mod.Download(_online.ZipUrl) : bundled)
        {
            Mod.Install(zip, _game);
        }
        return "Done! Start Stream Racer, then open http://localhost:8793 in your browser for the control panel.";
    }

    string Uninstall()
    {
        var answer = (DialogResult)Invoke(new Func<DialogResult>(() => MessageBox.Show(this,
            "Remove Stream Racer API, with its settings, colours and race history?", Text, MessageBoxButtons.YesNo)));
        if (answer != DialogResult.Yes)
        {
            return "";
        }
        Mod.Uninstall(_game);
        return "Removed. Stream Racer runs without the mod now. Run this again any time to put it back.";
    }
}
