# stream-racer-api

Stream Racer API: a BepInEx plugin that exposes [Stream Racer](https://store.steampowered.com/app/1333410/Stream_Racer/) over HTTP + SSE, with a built-in control page. Nothing in the game files is modified; delete the `BepInEx` folder and `winhttp.dll` to uninstall.

## Install

**Release zip:** contains everything, BepInEx 5.4.23.3 (`winhttp.dll`, `doorstop_config.ini`, `BepInEx/core`) plus the plugin at `BepInEx/plugins/StreamRacerApi.dll`. Close the game, unzip over the game folder (next to `StreamRacer.exe`), launch the game, open `http://localhost:8793`. See `INSTALL.txt` inside the zip.

**From source** (Git Bash, .NET 8 SDK):

```
./install.sh          # builds, installs BepInEx if missing, copies the plugin into the game folder
scripts/release.sh    # build a release: dist/stream-racer-api-<version>.zip (BepInEx + plugin + INSTALL.txt)
dotnet test tests/unit
```

The control page is plain ES modules embedded in the DLL, no bundler. Edit `ui/`, rebuild, restart the game.

## Antivirus heads-up

Windows Defender or another scanner may flag the download. Nothing here hides what it does, so here is exactly what triggers it and what the pieces are:

- **`winhttp.dll` next to the game exe** is BepInEx's loader. Windows looks for that DLL in the game folder before the system one, so the game loads BepInEx, which loads plugins from `BepInEx/plugins`. "A DLL that replaces a Windows DLL and injects code into another process" is the pattern scanners look for, even though it is the standard, open-source way Unity games get modded (BepInEx: https://github.com/BepInEx/BepInEx).
- **`StreamRacerApi.dll`** is this plugin. It patches a few game methods in memory with Harmony (race start/end, chat, camera, car AI slow-down) so it can raise events and take commands. It never writes to the game's files.
- **It opens a local web server** on port 8793 (`127.0.0.1` only unless you turn on `api.BindAll`) for the control page, overlays and the API. Software that opens a listening port from inside a game process can look like a backdoor to a heuristic scanner. The only outbound requests are to Twitch (profile pictures and, if you paste a token, follower checks), your optional update URL, and whatever webhooks you configure.
- **It reads Twitch chat** through the game's own Twitch connection, only to answer the color and respawn commands, and (Settings → `chatReplies`, on by default) writes one line back to confirm a color command. If you paste a token for follower checks it is only ever sent to Twitch's own API.

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
- **Camera**: every shot as a button, plus "Director uses" switches (`settings.camera.shots`) to keep the auto director off shots you don't want.
- **Settings**: perks (chat color command tier, colored-name tier, stacking extra boosts for follower / subscriber / developer / host, optional Twitch token for follower checks), your own auto-join + car color, auto-join list, in-game mini map (toggle, position, size), overlay look & feel split into the horizontal bar and the leaderboard (rows, size, side, link builder; applies live to open browser sources), API token for this browser, read-only plugin config.
- **API**: route reference with a request tester and a live `/events` viewer.

### Browser sources (OBS)

Transparent pages served by the plugin, one source each, all following Settings live. They draw nothing outside a race: after `race_end` the cars fade out over a second (the finish banner stays a few seconds), then nothing until the next `race_start`; `settings.overlay.showInLobby` (Settings → Overlay look, off by default) shows the field in the lobby too.

| source | what | query params (override the saved look for that source) |
|---|---|---|
| `/overlay` | horizontal bar: avatars along a track line stacked by place, RIP / boost / finish banner | `?size=40&names=0&accent=%23ff8a00&token=` |
| `/leaderboard` | vertical top-N list: place, avatar, name, progress. Make it smaller (`scale`) so the mini map fits above it | `?rows=10&side=left\|right&scale=1&accent=%23ff8a00&token=` |
| `/minimap` | route outline + car dots, same look as the in-game map | `?aspect=16:9&names=1&leaderBig=1&track=%23fff&bg=%23000&alpha=0.55&marker=3&token=` |

Works as an OBS custom browser dock. Pages and the overlays reconnect on their own when the game restarts (the browser retries the event stream every few seconds and the page resyncs on reconnect). The one thing that can't self-heal: if OBS loads the page while the game is closed there is no page at all, so add the source with the game running, or tick "Refresh browser when scene becomes active".

## API

`:x` is a Twitch user id (digits) or login name. `all` = every car. `me` = the streamer.

| route | |
|---|---|
| `GET /race` | `{running, streamer, vehicles:[{place,id,login,displayName,color,sub,type,progress,finishAt,pct,finished,boosts,image,avatar,title}]}` |
| `GET /events` | SSE: `lobby`, `joined`, `race_start`, `positions` (TickHz full snapshot), `pos` (PosHz, light `{t, v:[[login,x,z,pct,place,finished]…]}`), `finisher`, `race_end` (snapshot); `boom`, `boost` (a pool spend, `boosts` = what is left), `respawn`, `developer`, `crash`, `recovered` (one vehicle; booms/boosts include chat-triggered ones; `crash` = flipped / off road / stuck for over 1 s, with a `pileup` count); `boosts` `{login, boosts, delta}` when a pool changes without a boost firing (add, perk, race start); `perk` `{login, extraBoosts, reasons, boosts, followerChecks}` per joined car; `color`, `denied`, `chat` (chat commands and replies); `camera`, `settings`, `screen` |
| `GET /overlay` | browser source, the horizontal bar: avatars along a track line stacked by place, RIP/boost effects. `?size=56&names=0&token=` |
| `GET /leaderboard` | browser source, the vertical top-N list. `?rows=10&side=left&scale=1&token=` |
| `GET /me` | `{id, login, inRace}` |
| `POST /boom` | one random car (skips cars already mid-boom) |
| `POST /boom/:n` | n random booms (n ≤ 200), `affected` = real hits |
| `POST /boom/:x` | targeted |
| `POST /boom/all?except=:x` | everyone but one |
| `POST /boost/me` | spend one of the streamer's boosts (= `!boost`) |
| `POST /boost/:x\|all?force=&seconds=` | fire a boost now, free. Defaults = game's random range. Negative force = shove backwards. |
| `POST /boost/:x\|all/add?n=1` | add to the `!boost` pool; a single target answers `{boosts}`; fires `boosts` |
| `POST /speed/:x\|all?mult=0.5&seconds=5` | top-speed multiplier for a while |
| `POST /respawn/:x\|all` | the game's stuck-car respawn |
| `POST /join` | body `{id,login,displayName,color,sub,image}` or array. Lobby only. Non-Twitch names work (bots). `image` (URL or local path) replaces the avatar in the lobby/results lists. |
| `POST /join/me?color=` | add the streamer's own car (= JOIN GAME button); color defaults to the saved "my car color"; lobby only |
| `POST /color/:x?color=` | set a racer's color (hex or name), remembered for future joins and shown on the car, the lobby row and the leaderboard; viewers can use any alias in `settings.colorCommand` (default `!race color\|!color`), confirmed in chat when `settings.chatReplies` is on |
| `GET /inventory/:login` | `{boosts, respawns, respawnLimit}` for a racer (`!race inv` in chat via the bot) |
| `GET /perks/:login` | `{login, follower, subscriber, developer, host, extraBoosts, why[], followerChecks: ok\|no token\|unknown, followerChecksError, granted, boosts}`: why someone did or didn't get extra boosts. `no token` = paste a Twitch token with `moderator:read:followers` + its client id on Settings → Perks |
| `GET /chat` | `{connected, channel, replies, canSend, scopes}`: whether the mod can talk in chat through the game's connection (`canSend` = the game token has `chat:edit`; null until looked up) |
| `POST /chat/say?text=` | say a line in Twitch chat as the streamer (409 when chat is not connected); emits `chat` |
| `POST /finish/:x` | mark a racer finished (runs the car's finish-line trigger; for the game bug where a car crosses without triggering it) |
| `POST /kick/:x` | lobby only |
| `PUT /config` | live plugin config: `{port, bindAll, token, hotkeyBoost, camUp, camDown, tickHz, posHz}`; port/bind changes restart the server |
| `GET\|PUT /settings` | PUT merges: `autoJoin` (everyone on it joins every lobby), `bots`, `customBots`, `botOptions[login].autoBoost`, `webhooks` (call anything on `race_end` etc.), `perks`, `camera.shots` (director toggles: grid, high, side, sweep, pack, front, chase, orbit, overhead, prop, finish, duel, pileup, boom; omitted keys stay on), `chatReplies`, `respawnCommand` / `colorCommand` (alias lists, `a\|b`), `twitchToken` + `twitchClientId` (follower checks)… + read-only `config`, `followerChecks` |
| `POST /autojoin/join` | join the list now (lobby only) |
| `GET /twitch/auth` · `GET\|POST\|DELETE /twitch/token` | log the mod into Twitch with your own app (follower checks + chat replies); Settings → Perks → Connect Twitch |
| `GET /twitch/users?logins=a,b` | resolve logins via Helix with the game's token: `{users:[{id,login,displayName,image,description}]}` |
| `GET /image/:login` | a racer's custom join image, served by the plugin (so local paths work in browser overlays); `avatar` points here when set |
| `GET /screen` | `{screen, scene, running, lobby, vehicles}`; a `screen` SSE event fires on every change |
| `GET /camera` | `{auto, mode, target, cars, fov, shots}` (`shots` = the director toggles from `settings.camera.shots`; manual shots ignore them) |
| `POST /camera/auto?on=` | toggle/set the director: pack / chase / orbit / game follow / overhead with random FOVs and zooms, cuts to booms |
| `POST /camera/pack`, `/sweep`, `/side`, `/high`, `/finish`, `/front/:x`, `/chase/:x`, `/orbit/:x`, `/overhead`, `/boom`, `/prop` | custom shots; `?seconds=` (0 = stay) `&fov=` `&fovTo=` (zoom over the shot) |
| `POST /camera/focus/:x`, `/leader`, `/free` | the game's own follow / free cam |
| `POST /minimap?on=` | toggle the in-game picture-in-picture course map; placement/look in `settings.minimap` |
| `GET /minimap` | browser-source mini map page (route + car dots), same look settings |
| `POST /lobby/exit` | leave the lobby (EXIT button), back to the home screen |
| `GET /track/zones` | straights worth boosting on for the current map (route distances, match `progress`) |
| `POST /boost/:x/use` | spend one of a car's own boosts (third-party bot control) |
| `GET /track` | route outline `{points:[[x,z]…], bounds}`; snapshots carry each car's `x`,`z` |
| `GET /maps` | map list (first call fetches, returns 202) |
| `POST /lobby?map=` | create a lobby from any screen; `map` = id or part of a name, default first. Uses the Play tab's saved options. 202 while loading |
| `POST /race/start` | begins the lobby countdown like the START button; `?now=1` starts immediately |
| `POST /race/end` | |
| `POST /race/next` | next map from the queue (set by `/lobby` or the Play tab); 409 if nothing queued |

Errors: 401 token required · 404 no such car · 409 not applicable (no race / finished / lobby closed) · 501 a game update renamed an obfuscated member (fix the name in `src/GameNames.cs`).

Notes: a boom is a stun, not a kill (RIP banner, 5 s fuse, car launched, drives again after 4 s). `pct` = progress ÷ finish line. `curl -X POST` needs `-d ''`. CORS is open.

```js
const H = { Authorization: "Bearer <token>" }; // only if api.Token is set
const es = new EventSource("http://127.0.0.1:8793/events?token=<token>");
es.addEventListener("positions", e => render(JSON.parse(e.data).vehicles));
es.addEventListener("finisher",  e => tts(JSON.parse(e.data).displayName + " finished!"));
await fetch("http://127.0.0.1:8793/boom/all?except=me", { method: "POST", headers: H });
```

## Tests

Four suites, `pnpm` only (`pnpm install` once). `pnpm test` = unit + api; `pnpm test:all` = unit + api + ui + race.

| command | what | needs |
|---|---|---|
| `pnpm test:unit` | xunit over the pure C# logic (`src/Pure.cs`) | .NET 8 SDK; no game |
| `pnpm test:api` | Vitest + TypeScript, one file per route family in `tests/api/`: version, screen, race snapshot, settings (PUT merges, restored), config (unchanged values only: never the port or token), maps, twitch users, track, camera state, minimap (toggled twice), static pages, events, auth, webhooks (a local HTTP server on a random port receives the `settings` event). Read-only: everything it touches is put back. | the game running on any screen; skips with a printed reason when it is not |
| `pnpm test:ui` | Playwright over the control page (below) | the game running + cached Chromium |
| `pnpm test:race` | **takes over the game for ~2 minutes**: `tests/race/` runs `lobby` → `racing` → `camera` → `end` in that order (`/lobby`, `/join`, `/race/start?now=1`, 60 Hz `pos` frames, zones, boosts, booms, respawn, speed, `/finish`, every camera shot, `/race/end`, postgame lock-out, `/race/next`). Auto-join is switched off for the run and restored afterwards. | the game **idle on the home screen** with nobody watching; refuses otherwise. It ends in a fresh empty lobby: back out to the menu before running it again |

`SR_API` overrides the base URL (default `http://127.0.0.1:8793`), `SR_TOKEN` supplies the bearer when `api.Token` is set. `pnpm typecheck` runs `tsc --noEmit` over every TypeScript test. Shared helpers live in `tests/support/` (`apiClient`, `gameScreen`, `eventStream`, `sourceVersion`, `settingsStore`); `tests/api/version.test.ts` fails when the installed DLL is not the build from `src/Plugin.cs`.

UI tests: `pnpm test:ui` (Playwright + TypeScript; `tests/ui/` mirrors `ui/`: `pages/`, `components/`, `app`, `overlay`, `leaderboard`, `minimap`, shared fixtures in `tests/ui/fixtures/`). They open every control page plus `/overlay`, `/leaderboard` and `/minimap` in headless Chromium against the running game and compare the DOM with `/settings`, `/version` and `/race`, failing on any console error or uncaught exception. The pages, `.js` and `.css` are served from the working tree (`fixtures/working-tree.ts` routes them; API calls still hit the game), so a UI change is testable without rebuilding the DLL; `SR_UI_LIVE=1` tests the embedded copy instead. Overlay behaviour (fade after `race_end`, lobby visibility, live boost counters) is driven through a fake `EventSource` (`fixtures/fake-events.ts`) so the live race cannot interfere; the Bots page also gets a layout check at 1400×1100 and 1000×800 (nothing outside its card, badges clear of tools and picture, one baseline per form row). Read-only: nothing that changes game state is clicked. If the game is down every test skips with a printed reason. `SR_API` overrides the base URL (default `http://127.0.0.1:8793`); screenshots land in `test-results/` on failure only. `pnpm typecheck` type-checks the specs.

## Layout

```
src/Plugin.cs        HttpListener, SSE, auth, static ui/ serving, hotkey
src/Routes.cs        URL → action
src/GameNames.cs     the obfuscation boundary: string constants for Harmony/reflection targets, singletons, readable extension methods
src/GlobalUsings.cs  readable aliases for the game's obfuscated types (Vehicle, RacerProfile, ...); no other file spells an obfuscated name
src/States.cs        CameraMode / VehicleState / GameScreen enums, converted to the API strings in one place each
src/Game/            static partial class Game, one file per concern: Vehicles, Actions, Lobby, Perks, Colors, Chat, Track, Images
src/Camera/          static partial class Cam: Camera.cs (state + helpers), Camera.Shots.cs (shots + Tick), Camera.Director.cs (auto director)
src/Patches.cs       Harmony: race lifecycle events, booms/boosts, joins, free-cam keys, chat, slow re-apply, row images
src/Settings.cs      persisted page settings + auto-join
src/Pure.cs          engine-free logic (boost zones, versions, mini map math, colors, chat parsing); what the unit tests cover
src/Minimap.cs       in-game mini map · src/Credits.cs in-game credits block · src/TwitchAuth.cs mod login · src/Updates.cs · src/Webhooks.cs
ui/              control page (React + zustand + htm from esm.sh, Pico CSS); one .js + .css per page/component
ui/app.js        header + page router; ui/store.js = the zustand store (typed in JSDoc) fed by /events
ui/pages/        <page>.js composes <page>/<Card>.js: controls/ (Driver, Field, Race), camera/ (CameraControls, DirectorShots),
                 bots/ (AddCustomBotForm, BotCard), settings/ (one card per section + fields.js, TwitchConnect), api/ (Code, RouteCard, EventLog)
ui/components/   racers.js (timing board), roster.js (TwitchLookup, Card, Grid, Empty, useTwitchUsers)
ui/lib/          shared helpers, no duplicates elsewhere: html (htm + useCss), api (fetch + token), toast, text (escapeHtml, ordinal, initials),
                 files, query (browser-source ?token=), defaults (overlay / mini map look defaults)
                 overlay.* (bar) + leaderboard.* (list) share overlay-shared.js (event stream, snapshot, race phase); minimap.* stands alone
tests/unit/      xunit tests, mirroring src/: tests/unit/Pure/<Concern>Tests.cs, one file per section of Pure.cs
tests/ui/        Playwright UI tests mirroring ui/ (pages/, components/, app, overlay, leaderboard, minimap); fixtures/ = server truth, console guard, working-tree router, fake EventSource
tests/*.mjs      end-to-end tests against a running game
scripts/         release.sh (release zip), bepinex.sh (BepInEx version/URL + download cache), post-commit (semver bump hook, amends the commit)
```
