// Chaos: random events that just happen as a race goes on (meteors, a dragon, a tornado, a bomber raid, a witch, a
// magical girl, a black hole). One scheduler picks an enabled event every settings.chaos.minSeconds..maxSeconds;
// POST /chaos/:name starts one now, /chaos/clear ends them all, GET /chaos lists them. Every event lives in its own
// Chaos.<Event>.cs as a coroutine on the chaos root, so destroying that root (race end, new lobby, clear) stops them
// all at once. Everything is built from Unity primitives and code (no asset bundles). Ranked blocks it all.
using System.Collections;
using System.Collections.Specialized;

namespace StreamRacerApi;

static partial class Chaos
{
    // name -> the coroutine that plays it. An event file adds itself here (see Register calls in each file).
    static readonly Dictionary<string, System.Func<IEnumerator>> Events = new()
    {
        ["meteors"] = Meteors,
        ["dragon"] = Dragon,
        ["tornado"] = Tornado,
        ["raid"] = Raid,
        ["witch"] = Witch,
        ["magical"] = MagicalGirl,
        ["blackhole"] = BlackHole,
        ["uma"] = UmaRace,   // a whole race, not a random event: rolled at the start (Chaos.Uma)
        ["umagym"] = UmaGymRace,   // the same, everyone in the gym uniform
    };

    public static IEnumerable<string> Names => Events.Keys;

    static GameObject _root;
    static ChaosHost _host;
    static readonly HashSet<string> _active = new();
    static float _nextEvent;
    static bool _warned;

    // Holds the events' coroutines; it dies with the root, which stops every event mid-flight.
    class ChaosHost : MonoBehaviour
    {
    }

    // The parent of everything an event spawns. Created on first use, destroyed by Clear.
    public static GameObject Root
    {
        get
        {
            if (_root == null)
            {
                _root = new GameObject("sr-chaos");
                _host = _root.AddComponent<ChaosHost>();
            }
            return _root;
        }
    }

    // ---- API ----

    // POST /chaos/:name, POST /chaos/clear.
    public static object Request(string target, NameValueCollection query, ref int status)
    {
        if (target == "clear")
        {
            Clear();
            return Dto();
        }
        string name = (target ?? "").ToLowerInvariant();
        if (!Events.ContainsKey(name))
        {
            status = 404;
            return new
            {
                error = "need /chaos/<name>|clear",
                events = Names,
            };
        }
        if (name.StartsWith("uma") && query["look"] != null)
        {
            UmaLook = query["look"];   // dev aid: ?look=vertexlit|toon (templates already built keep their look)
            foreach (var template in _umaTemplates.Values.Where(template => template.Model != null))
            {
                Object.Destroy(template.Model);
            }
            _umaTemplates.Clear();
        }
        string refusal = Start(name);
        if (refusal != null)
        {
            status = 409;
            return new { error = refusal };
        }
        return Dto();
    }

    public static object Dto() => new
    {
        active = _active.ToList(),
        events = Names,
        enabled = Settings.Current.chaos.enabled,
        minSeconds = Settings.Current.chaos.minSeconds,
        maxSeconds = Settings.Current.chaos.maxSeconds,
        nextInSeconds = _nextEvent > 0f ? Mathf.Max(0f, _nextEvent - Time.unscaledTime) : 0f,
    };

    // Null = started; otherwise why not.
    public static string Start(string name)
    {
        if (!Game.Running)
        {
            return "no race running";
        }
        if (_active.Contains(name))
        {
            return name + " is already happening";
        }
        Root.GetComponent<ChaosHost>().StartCoroutine(Run(name));
        return null;
    }

    static IEnumerator Run(string name)
    {
        _active.Add(name);
        Plugin.Emit("chaos", new
        {
            @event = name,
            phase = "start",
        });
        var inner = Events[name]();
        while (true)
        {
            bool more;
            try
            {
                more = inner.MoveNext();
            }
            catch (System.Exception error)
            {
                Warn(name, error);
                more = false;
            }
            if (!more)
            {
                break;
            }
            yield return inner.Current;
        }
        _active.Remove(name);
        Plugin.Emit("chaos", new
        {
            @event = name,
            phase = "end",
        });
    }

    // Race over, new lobby, Ranked switched on or POST /chaos/clear: every event and everything it left behind.
    public static void Clear()
    {
        foreach (var undo in _onClear.ToList())
        {
            Guard("clear", undo);
        }
        _onClear.Clear();
        Guard("reset", ResetEvents);
        if (_root != null)
        {
            Object.Destroy(_root);
        }
        _root = null;
        _host = null;
        _active.Clear();
        _nextEvent = 0f;
    }

    // Things an event must put back even when it is cut short (sky colours, hidden car renderers, ...).
    static readonly List<System.Action> _onClear = new();
    public static void OnClear(System.Action undo) => _onClear.Add(undo);
    public static void ForgetOnClear(System.Action undo) => _onClear.Remove(undo);

    // ---- scheduler ----

    public static void Tick()
    {
        if (!Game.Running)
        {
            _umaRolled = false;
            return;
        }
        if (!Settings.Current.sendResults)
        {
            Guard("uma", MaybeUmaRace);
        }
        Guard("tick", TickEvents);
        var options = Settings.Current.chaos;
        if (!options.enabled || Settings.Current.sendResults)   // Ranked keeps to the game's own racing
        {
            _nextEvent = 0f;
            return;
        }
        if (_nextEvent <= 0f)
        {
            _nextEvent = Time.unscaledTime + Random.Range(options.minSeconds, options.maxSeconds);
            return;
        }
        if (Time.unscaledTime < _nextEvent)
        {
            return;
        }
        _nextEvent = Time.unscaledTime + Random.Range(options.minSeconds, Mathf.Max(options.minSeconds, options.maxSeconds));
        var pool = Names.Where(name => !name.StartsWith("uma") && options.On(name) && !_active.Contains(name)).ToList();
        if (pool.Count > 0)
        {
            Start(pool[Random.Range(0, pool.Count)]);
        }
    }

    // Per-frame work of state that outlives an event's own coroutine (burning cars, holes, costumes, the black hole's
    // pull), and its reset. Each lives in its event's file; a missing one compiles away (partial methods).
    static partial void TickFire();
    static partial void TickHoles();
    static partial void TickCostumes();
    static partial void TickTornado();
    static partial void TickBlackHole();
    static partial void ResetFire();
    static partial void ResetHoles();
    static partial void ResetCostumes();
    static partial void ResetTornado();
    static partial void ResetBlackHole();

    static void TickEvents()
    {
        TickFire();
        TickHoles();
        TickCostumes();
        TickTornado();
        TickBlackHole();
    }

    static void ResetEvents()
    {
        ResetFire();
        ResetHoles();
        ResetCostumes();
        ResetTornado();
        ResetBlackHole();
    }

    // ---- fail soft ----

    public static void Guard(string what, System.Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception error)
        {
            Warn(what, error);
        }
    }

    static void Warn(string what, System.Exception error)
    {
        if (_warned)
        {
            return;
        }
        _warned = true;
        Plugin.Log.LogWarning("chaos " + what + ": " + error + " (further errors hidden)");
    }
}
