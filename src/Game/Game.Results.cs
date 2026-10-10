// Game.Results: race results kept on this PC. Every race that ends is appended as one JSON line to
// BepInEx/config/shibiko.streamracer.results.jsonl; GET /standings totals them per day / week / month / all time
// (Pure.Standings). And settings.sendResults = false keeps results off the game's backend (Patches.KeepResultsLocal).
using System.IO;
using Newtonsoft.Json;

namespace StreamRacerApi;

static partial class Game
{
    // Ranked = results go to the game's servers, so only the game's own racing may decide them (Pure.RankedBlocks).
    public static bool RankedMode => Settings.Current.sendResults;

    // Ranked switched on: anything of ours still running stops now.
    public static void EnterRanked()
    {
        Weather.EndOfRace();   // weather + every laid surface
        StopDrifts();
        Slows.Clear();
    }

    static readonly string ResultsPath = Path.Combine(BepInEx.Paths.ConfigPath, "shibiko.streamracer.results.jsonl");
    static readonly object ResultsLock = new();
    static readonly JsonSerializerSettings ResultsJson = new() { DateParseHandling = DateParseHandling.DateTimeOffset };

    // Runs at race end: finishers in the order they crossed, then everyone who did not finish.
    public static void RecordRace()
    {
        var ranked = Ranked();
        if (ranked.Count == 0) return;
        var game = Instances.GameController?.CurrentGame();
        var record = new
        {
            at = System.DateTime.Now.ToString("o"),
            map = game == null ? null : new { id = game.MapId(), name = game.MapName() },
            racers = ranked.Select((vehicle, index) => new
            {
                login = Login(vehicle),
                name = vehicle.Profile().DisplayName(),
                color = "#" + ColorUtility.ToHtmlStringRGB(vehicle.Profile().Color()),
                place = vehicle.HasFinished() ? index + 1 : 0,
                finished = vehicle.HasFinished(),
            }).ToList(),
        };
        string line = JsonConvert.SerializeObject(record) + "\n";
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (ResultsLock) File.AppendAllText(ResultsPath, line);
            }
            catch (System.Exception error)
            {
                Plugin.Log.LogWarning("could not save the race result: " + error.Message);
            }
        });
    }

    // GET /standings?period=day|week|month|all&limit=N. Reads the whole file per request, off the game thread.
    // ponytail: one line per race, re-read every time; fine for years of streams, index by month if it ever gets slow.
    public static object StandingsDto(string period, int limit)
    {
        period = Pure.Periods.Contains(period) ? period : "week";
        var races = new List<Pure.RaceRecord>();
        lock (ResultsLock)
        {
            if (File.Exists(ResultsPath))
                foreach (var line in File.ReadAllLines(ResultsPath))
                    if (TryParseRace(line, out var race)) races.Add(race);
        }
        var now = System.DateTime.Now;
        var standings = Pure.Standings(races, period, now);
        var since = Pure.PeriodStart(period, now);
        return new
        {
            period,
            since = since == System.DateTime.MinValue ? null : since.ToString("o"),
            races = races.Count(race => race.At >= since),
            standings = standings.Take(limit > 0 ? limit : int.MaxValue).Select((standing, index) => new
            {
                rank = index + 1,
                login = standing.Login,
                name = standing.Name,
                color = standing.Color,
                points = standing.Points,
                wins = standing.Wins,
                podiums = standing.Podiums,
                finishes = standing.Finishes,
                races = standing.Races,
                best = standing.Best,
            }).ToList(),
        };
    }

    static bool TryParseRace(string line, out Pure.RaceRecord race)
    {
        race = null;
        try
        {
            // keep the offset: a plain parse turns "02:54-05:00" into an offset-less 02:54 that later reads as UTC
            var json = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(line, ResultsJson);
            race = new Pure.RaceRecord
            {
                At = json["at"].ToObject<System.DateTimeOffset>().LocalDateTime,
                Racers = json["racers"].Select(racer => new Pure.RaceEntry
                {
                    Login = (string)racer["login"],
                    Name = (string)racer["name"],
                    Color = (string)racer["color"],
                    Place = (int?)racer["place"] ?? 0,
                    Finished = (bool?)racer["finished"] ?? false,
                }).ToList(),
            };
            return true;
        }
        catch
        {
            return false;   // a half-written or hand-edited line: skip it
        }
    }

    // What the backend would have answered, built here (places only, no points): the game's own reply for local test maps.
    public static RaceResultResponse LocalRaceResult() => new()
    {
        RaceResults = Ranked().Where(vehicle => vehicle.HasFinished())
            .Select((vehicle, index) => new RaceResultResponseItem { UserID = vehicle.Profile().BackendId(), Pos = index + 1 })
            .ToArray(),
    };
}
