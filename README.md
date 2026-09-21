# stream-racer-api

Stream Racer API: a BepInEx plugin that exposes [Stream Racer](https://store.steampowered.com/app/1333410/Stream_Racer/) over HTTP + SSE, with a built-in control page. Nothing in the game files is modified; delete the `BepInEx` folder and `winhttp.dll` to uninstall.

## Install

**Release zip:** unzip over the game folder (next to `StreamRacer.exe`), launch the game, open `http://localhost:8793`.

**From source** (Git Bash, .NET 8 SDK):

```
./install.sh        # builds, installs BepInEx if missing, copies the plugin
```

The control page is plain ES modules embedded in the DLL, no bundler. Edit `ui/`, rebuild, restart the game.

## Antivirus heads-up

Windows Defender or another scanner may flag the download. Nothing here hides what it does, so here is exactly what triggers it and what the pieces are:

- **`winhttp.dll` next to the game exe** is BepInEx's loader. Windows looks for that DLL in the game folder before the system one, so the game loads BepInEx, which loads plugins from `BepInEx/plugins`. "A DLL that replaces a Windows DLL and injects code into another process" is the pattern scanners look for, even though it is the standard, open-source way Unity games get modded (BepInEx: https://github.com/BepInEx/BepInEx).
- **`StreamRacerApi.dll`** is this plugin. It patches a few game methods in memory with Harmony (race start/end, chat, camera, car AI slow-down) so it can raise events and take commands. It never writes to the game's files.
- **It opens a local web server** on port 8793 (`127.0.0.1` only unless you turn on `api.BindAll`) for the control page, overlays and the API. Software that opens a listening port from inside a game process can look like a backdoor to a heuristic scanner. The only outbound requests are to Twitch (profile pictures and, if you paste a token, follower checks), your optional update URL, and whatever webhooks you configure.
- **It reads Twitch chat** through the game's own Twitch connection, only to answer the color and respawn commands.

If your scanner quarantines it: the whole thing is source, this repo is all of it. Read `src/`, build it yourself with `./install.sh` (needs the .NET 8 SDK and Git Bash), and add the game folder to your scanner's exclusions. The release zip is that same build, nothing more. No telemetry, no accounts, no launcher.

## Config

`BepInEx/config/shibiko.streamracer.api.cfg` (created on first launch). All of it is editable live from Settings or `PUT /config`; a port change restarts the API server in place.

| key | default | |
|---|---|---|
| api.Port | 8793 | HTTP + SSE port |
| api.TickHz | 4 | full `positions` snapshot rate while racing |
| api.PosHz | 60 | light `pos` event rate (per-car login, x, z, pct, place) for smooth maps/overlays |
| api.Token | (empty) | if set, every API call needs `Authorization: Bearer <token>` (or `?token=` for SSE). The page still loads; paste the token on its Settings tab. |
| api.BindAll | false | listen on all interfaces so a bot on another machine can call it. Requires a Token. Windows needs once, as admin: `netsh http add urlacl url=http://+:8793/ user=Everyone` |
| hotkeys.Boost | R | spend one of your own boosts in-game, same as typing `!boost` |
| camera.Up / Down | Space / C | extra free-cam keys (Q/E still work) |

Page settings (auto-join toggle + list, action defaults) persist server-side in `shibiko.streamracer.settings.json` next to the config, so every browser/OBS dock sees the same thing.

Log: `BepInEx/LogOutput.log`.

## Control page  `http://localhost:8793`

- **Controls**: camera director (auto / leader / overhead / boom cam / free, plus 🎥 per racer), boost-me button, boom / boost / add-boost / slow / respawn for the field, live timing board with per-racer actions and search.
- **Bots**: custom bots (your own login, name, color, picture upload; no Twitch account) plus a saved list of well-known Twitch accounts (resolved live, real avatars). Add/remove, drop them into the lobby or the auto-join list. Their cars fire their own boosts: first 0-4 s after the start, then every 6-18 s, preferring straights.
- **Settings**: perks (chat color command tier, colored-name tier, stacking extra boosts for follower / subscriber / developer / host, optional Twitch token for follower checks), your own auto-join + car color, auto-join list, in-game mini map (toggle, position, size), overlay look & feel (applies live to open browser sources), API token for this browser, read-only plugin config.
- **API**: route reference with a request tester and a live `/events` viewer.

Works as an OBS custom browser dock. Pages and the overlay reconnect on their own when the game restarts (the browser retries the event stream every few seconds and the page resyncs on reconnect). The one thing that can't self-heal: if OBS loads the page while the game is closed there is no page at all, so add the source with the game running, or tick "Refresh browser when scene becomes active".

## API

`:x` is a Twitch user id (digits) or login name. `all` = every car. `me` = the streamer.

| route | |
|---|---|
| `GET /race` | `{running, streamer, vehicles:[{place,id,login,displayName,color,sub,type,progress,finishAt,pct,finished,boosts,image,avatar,title}]}` |
| `GET /events` | SSE: `lobby`, `joined`, `race_start`, `positions` (TickHz full snapshot), `pos` (PosHz, light `{t, v:[[login,x,z,pct,place,finished]…]}`), `finisher`, `race_end` (snapshot); `boom`, `boost`, `respawn`, `developer`, `crash`, `recovered` (one vehicle; booms/boosts include chat-triggered ones; `crash` = flipped / off road / stuck for over 1 s, with a `pileup` count) |
| `GET /overlay` | transparent OBS browser source: avatars along a track line stacked by place, RIP/boost effects. `?size=56&board=5&names=0&token=` |
| `GET /me` | `{id, login, inRace}` |
| `POST /boom` | one random car (skips cars already mid-boom) |
| `POST /boom/:n` | n random booms (n ≤ 200), `affected` = real hits |
| `POST /boom/:x` | targeted |
| `POST /boom/all?except=:x` | everyone but one |
| `POST /boost/me` | spend one of the streamer's boosts (= `!boost`) |
| `POST /boost/:x\|all?force=&seconds=` | fire a boost now, free. Defaults = game's random range. Negative force = shove backwards. |
| `POST /boost/:x\|all/add?n=1` | add to the `!boost` pool |
| `POST /speed/:x\|all?mult=0.5&seconds=5` | top-speed multiplier for a while |
| `POST /respawn/:x\|all` | the game's stuck-car respawn |
| `POST /join` | body `{id,login,displayName,color,sub,image}` or array. Lobby only. Non-Twitch names work (bots). `image` (URL or local path) replaces the avatar in the lobby/results lists. |
| `POST /join/me?color=` | add the streamer's own car (= JOIN GAME button); color defaults to the saved "my car color"; lobby only |
| `POST /color/:x?color=` | set a racer's color (hex or name), remembered for future joins; viewers can use the chat command (default `!color`) |
| `POST /finish/:x` | mark a racer finished (runs the car's finish-line trigger; for the game bug where a car crosses without triggering it) |
| `POST /kick/:x` | lobby only |
| `PUT /config` | live plugin config: `{port, bindAll, token, hotkeyBoost, camUp, camDown, tickHz, posHz}`; port/bind changes restart the server |
| `GET\|PUT /settings` | PUT merges: `autoJoin` (everyone on it joins every lobby), `bots`, `customBots`, `botOptions[login].autoBoost`, `webhooks` (call anything on `race_end` etc.), `perks`… + read-only `config` |
| `POST /autojoin/join` | join the list now (lobby only) |
| `GET /twitch/users?logins=a,b` | resolve logins via Helix with the game's token: `{users:[{id,login,displayName,image,description}]}` |
| `GET /image/:login` | a racer's custom join image, served by the plugin (so local paths work in browser overlays); `avatar` points here when set |
| `GET /screen` | `{screen, scene, running, lobby, vehicles}`; a `screen` SSE event fires on every change |
| `GET /camera` | `{auto, mode, target}` |
| `POST /camera/auto?on=` | toggle/set the director: pack / chase / orbit / game follow / overhead with random FOVs and zooms, cuts to booms |
| `POST /camera/pack`, `/sweep`, `/side`, `/high`, `/finish`, `/front/:x`, `/chase/:x`, `/orbit/:x`, `/overhead`, `/boom`, `/prop` | custom shots; `?seconds=` (0 = stay) `&fov=` `&fovTo=` (zoom over the shot) |
| `POST /camera/focus/:x`, `/leader`, `/free` | the game's own follow / free cam |
| `POST /minimap?on=` | toggle the in-game picture-in-picture course map; placement/look in `settings.minimap` |
| `GET /minimap` | browser-source mini map page (route + car dots), same look settings |
| `GET /track/zones` | straights worth boosting on for the current map (route distances, match `progress`) |
| `POST /boost/:x/use` | spend one of a car's own boosts (third-party bot control) |
| `GET /track` | route outline `{points:[[x,z]…], bounds}`; snapshots carry each car's `x`,`z` |
| `GET /maps` | map list (first call fetches, returns 202) |
| `POST /lobby?map=` | create a lobby from any screen; `map` = id or part of a name, default first. Uses the Play tab's saved options. 202 while loading |
| `POST /race/start` | begins the lobby countdown like the START button; `?now=1` starts immediately |
| `POST /race/end` | |
| `POST /race/next` | next map from the queue (set by `/lobby` or the Play tab); 409 if nothing queued |

Errors: 401 token required · 404 no such car · 409 not applicable (no race / finished / lobby closed) · 501 a game update renamed an obfuscated member (fix the name in `src/Game.cs`).

Notes: a boom is a stun, not a kill (RIP banner, 5 s fuse, car launched, drives again after 4 s). `pct` = progress ÷ finish line. `curl -X POST` needs `-d ''`. CORS is open.

```js
const H = { Authorization: "Bearer <token>" }; // only if api.Token is set
const es = new EventSource("http://127.0.0.1:8793/events?token=<token>");
es.addEventListener("positions", e => render(JSON.parse(e.data).vehicles));
es.addEventListener("finisher",  e => tts(JSON.parse(e.data).displayName + " finished!"));
await fetch("http://127.0.0.1:8793/boom/all?except=me", { method: "POST", headers: H });
```

## Layout

```
src/Plugin.cs    HttpListener, SSE, auth, static ui/ serving, hotkey
src/Routes.cs    URL → action
src/Game.cs      the only file that touches obfuscated game members
src/Patches.cs   Harmony: race lifecycle events, free-cam keys, slow re-apply
src/Settings.cs  persisted page settings + auto-join
ui/              control page (React + zustand + htm from esm.sh, Pico CSS); one .js + .css per page/component
```
