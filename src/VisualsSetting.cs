// Adds a "Twitch pictures" switch to the game's own Settings → Visuals list (a copy of its V-SYNC row): on = lobby and
// results rows show each racer's Twitch picture, the driver portrait only for racers without one (settings.twitchPictures).
// Re-injected whenever the menu reloads; follows changes made on the web page.
using Michsky.UI.Zone;
using TMPro;

namespace StreamRacerApi;

public static class VisualsSetting
{
    const string ListPath = "Canvas/Main Panels/Settings/Content/Box 1/Panels/Visuals/Content/List";
    static SwitchManager _switch;
    static float _next;

    public static void Tick()
    {
        if (Time.unscaledTime < _next)
        {
            return;
        }
        _next = Time.unscaledTime + 0.5f;
        if (_switch != null)
        {
            if (_switch.isOn != Settings.Current.twitchPictures)
            {
                _switch.isOn = Settings.Current.twitchPictures;
                _switch.Init();
            }
            return;
        }
        var list = GameObject.Find(ListPath)?.transform;
        var vsync = list?.Find("VSYNC");
        if (vsync == null)
        {
            return;
        }
        var row = Object.Instantiate(vsync.gameObject, list);
        row.name = "TwitchPictures";
        row.transform.SetSiblingIndex(vsync.GetSiblingIndex() + 1);
        var title = row.transform.Find("Title");
        foreach (var behaviour in title.GetComponents<MonoBehaviour>())
        {
            if (behaviour is not TMP_Text)
            {
                Object.Destroy(behaviour);   // the game's localiser would put "V-SYNC" back
            }
        }
        title.GetComponent<TMP_Text>().text = "TWITCH PICTURES";
        _switch = row.GetComponentInChildren<SwitchManager>();
        _switch.onEvent = new UnityEngine.Events.UnityEvent();   // no events: the game's settings code may poke
        _switch.offEvent = new UnityEngine.Events.UnityEvent();  // switches, and that must never save our setting
        _switch.isOn = Settings.Current.twitchPictures;
        // every button in the copy (the row and the switch's own on/off parts) still pointed at V-SYNC; only a
        // click on ours flips and saves it
        foreach (var button in row.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            button.onClick.AddListener(Toggle);
        }
    }

    static void Toggle()
    {
        _switch.AnimateSwitch();
        Save(_switch.isOn);
    }

    static void Save(bool on)
    {
        if (Settings.Current.twitchPictures == on)
        {
            return;
        }
        Settings.Current.twitchPictures = on;
        Settings.Persist();
        Plugin.Emit("settings", Settings.WithConfig());
    }
}
