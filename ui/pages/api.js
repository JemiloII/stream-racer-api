import { useState, useRef, useEffect } from "react";
import { html, useCss, authHeaders, withToken, getToken } from "../store.js";

// ---------- reference ----------
const P = (name, type, desc, extra = {}) => ({ name, type, desc, ...extra });
const TARGET = P("x", "string", "Twitch user id (digits) or login name", { in: "path", example: "shibikox" });
const SECS = P("seconds", "number", "how long the shot lasts; 0 = stay until the next camera call", { in: "query", default: 8 });
const FOV = [P("fov", "number", "field of view at the start of the shot", { in: "query", default: 60 }), P("fovTo", "number", "field of view at the end (a zoom over the shot)", { in: "query" })];

const GROUPS = [
  ["Race state", [
    { m: "GET", p: "/race", s: "Snapshot of the current race or lobby", d: "`state` per car: driving · air · flipped · offroad · stunned (boomed) · stuck · finished.", params: [], res: { running: true, lobby: false, streamer: "shibikox", vehicles: [{ place: 1, id: "23256990", login: "shibikox", displayName: "ShibikoX", color: "#FF8A00", sub: true, type: "REGULAR", progress: 812.4, finishAt: 1616, pct: 50.3, finished: false, boosts: 2, state: "driving", image: null, avatar: "https://static-cdn.jtvnw.net/…/profile_image-300x300.png", title: "Subscriber", x: 394.4, z: -613.3 }] } },
    { m: "GET", p: "/screen", s: "Which screen the game is on", d: "Also emitted as the `screen` SSE event whenever it changes. Handy for bots that need to wait for a lobby.", params: [], res: { screen: "lobby", scene: "Play", running: false, lobby: true, vehicles: 3 }, note: "screen: home · play · settings · lobby · racing · postgame · trackbuilder" },
    { m: "GET", p: "/version", s: "Mod, game, Unity and BepInEx versions + update check", d: "`upToDate` is null until `api.UpdateUrl` (plugin config) points at a JSON `{version, url}`; then true/false against `latest`.", params: [], res: { api: "1.0.0", commit: "a1b2c3d", game: "EARLY ACCESS 2.4.0", unity: "2021.3.17f1", bepinex: "5.4.23", latest: "1.0.0", upToDate: true, updateUrl: null, developer: "Shibiko", twitch: "https://twitch.tv/ShibikoX" } },
    { m: "GET", p: "/me", s: "The logged-in streamer", params: [], res: { id: "23256990", login: "shibikox", inRace: true } },
    { m: "GET", p: "/inventory/:login", s: "What a racer has left: boosts and chat respawns", d: "`respawns` is -1 when `settings.respawnLimit` is 0 (unlimited). Chat respawns count against the limit; the streamer's POST /respawn does not. Bots answer `!race inv` with this.", params: [TARGET], res: { login: "shibikox", displayName: "ShibikoX", boosts: 1, respawns: 2, respawnLimit: 2, running: true }, err: "404 no vehicle" },
    { m: "GET", p: "/perks/:login", s: "Why a viewer did or didn't get extra boosts", d: "Settings → Perks decides `extraBoosts` (follower + subscriber + developer + host, stacking). `followerChecks`: `ok` = lookups run; `no token` = no Twitch token + client id pasted on the Settings page, so followers are never detected; `unknown` = a token is set but the last Helix lookup failed (`followerChecksError` says why, e.g. 401 = the token lacks moderator:read:followers for this channel). An unknown follower status starts a lookup: ask again a second later. `granted` = the boosts were already added to the car.", params: [P("login", "string", "Twitch login (any casing)", { in: "path", example: "shibikox" })], res: { login: "shibikox", inRace: true, follower: false, followerKnown: false, subscriber: true, developer: false, host: false, extraBoosts: 1, why: ["sub", "follower boost needs a Twitch token with moderator:read:followers + its client id (Settings -> Perks)"], followerChecks: "no token", followerChecksError: null, granted: true, boosts: 3, perks: { colorCommand: "follower", coloredNames: "everyone", boostFollower: 1, boostSubscriber: 1, boostDeveloper: 1, boostHost: 0 } }, err: "400 no login" },
    { m: "GET", p: "/track", s: "Route outline for maps", d: "Points are [x, z] in world units along the road. `raw=1` returns the bare waypoints instead of the densified line.", params: [P("raw", "0|1", "bare waypoint transforms", { in: "query", default: 0 })], res: { points: [[291.9, -738.4], [289.1, -742.0], ["…"]], bounds: { minX: -153.4, maxX: 291.9, minZ: -1080.6, maxZ: -738.4 } } },
    { m: "GET", p: "/track/zones", s: "Good straights to boost on, for the current map", d: "Computed from the route: stretches where it turns less than 12° and stays flat over the next 40 units, at least 55 long. Route distances match each car's `progress`. Bots with auto-boost use these.", params: [], res: { map: 845, mapName: "Locate Yourself", length: 2016.3, finishAt: 1616, zones: [{ start: 120, end: 310, length: 190 }, { start: 640, end: 760, length: 120 }] } },
    { m: "GET", p: "/maps", s: "Map list from the game's backend", d: "First call kicks off the fetch and answers 202 with `loading: true`; call again.", params: [], res: { loading: false, maps: [{ id: 845, name: "Gherkin Gauntlent", creator: "UnclePickle89", official: false, length: "NORMAL", avgTime: 228.2 }] } },
  ]],
  ["Lobby & race flow", [
    { m: "POST", p: "/lobby", s: "Create a lobby from any screen", d: "Fetches the map list, puts your pick first in the game's playlist and presses PLAY. Uses the Play tab's saved options (max cars, type, time). Answers 202 while loading; watch the `lobby` SSE event.", params: [P("map", "string", "map id or part of a name; default = first map", { in: "query", example: "locate" })], res: { ok: true, state: "opening" }, err: "409 race running / already in a lobby" },
    { m: "POST", p: "/join", s: "Add racers to the lobby", d: "Non-Twitch names work (bots). `image` (URL or local path) replaces the avatar in the lobby and results lists and is served back at `/image/:login`.", params: [P("body", "object | array", "{id, login, displayName, color, sub, image}", { in: "body" })], body: [{ id: "", login: "mybot", displayName: "My Bot", color: "#ff00aa", image: "C:/overlays/bot.png" }], res: { ok: true, affected: 1 }, err: "409 race running" },
    { m: "POST", p: "/join/me", s: "Add the streamer's own car", d: "Same as the lobby's JOIN GAME button. Color defaults to Settings → My car color.", params: [P("color", "hex", "car / name color", { in: "query", example: "#ff8a00" })], res: { ok: true, affected: 1 }, err: "409 no lobby, not logged in, already joined" },
    { m: "POST", p: "/autojoin/join", s: "Join the saved auto-join list now", params: [], res: { ok: true, affected: 3 }, err: "409 race running" },
    { m: "POST", p: "/kick/:x", s: "Remove a racer from the lobby", params: [TARGET], res: { ok: true, affected: 1 }, err: "404 no vehicle · 409 race running" },
    { m: "POST", p: "/lobby/exit", s: "Leave the lobby (the EXIT button)", d: "Back to the home screen; the cars in the lobby are dropped.", params: [], res: { ok: true, affected: 1 }, err: "409 not in a lobby" },
    { m: "POST", p: "/race/start", s: "Start the lobby countdown", d: "What the START button does. The race begins when the countdown (Settings → start countdown) hits zero.", params: [P("now", "0|1", "skip the countdown, start immediately", { in: "query", default: 0 })], res: { ok: true, affected: 1 }, err: "409 no lobby with cars" },
    { m: "POST", p: "/race/end", s: "Force-end the race", params: [], res: { ok: true, affected: 1 } },
    { m: "POST", p: "/race/next", s: "Next map from the queue", d: "Works from the post-game screen or anywhere idle. The queue is set by `/lobby` or the Play tab.", params: [], res: { ok: true, affected: 1 }, err: "409 race running / nothing queued" },
    { m: "POST", p: "/color/:x", s: "Set a racer's color", d: "Hex (#ff8800 / ff8800) or a name (red, cyan, orange, pink, gold, lime, …). Applied now (car, label, lobby row, leaderboard, overlays) and remembered for future joins: a saved color is put on the car before the game builds its lobby row, and the lobby/results name is painted in it (same `perks.coloredNames` tier and `colorLeaderboard` switch as the in-game leaderboard). Omit `color` to forget the saved one; `/color/reset` forgets all. Viewers can do the same from chat with any alias in `settings.colorCommand` (default `!race color|!color`); when `settings.chatReplies` is on the game's own chat connection answers `@name color set to #ff8800` or `@name color is for follower+`.", params: [TARGET, P("color", "string", "hex or name", { in: "query", example: "#ff8800" })], res: { ok: true, affected: 1 }, err: "400 bad color" },
    { m: "POST", p: "/finish/:x", s: "Mark a racer as finished", d: "Runs the car's own finish-line trigger. For the game bug where a car crosses the line without touching the trigger.", params: [TARGET], res: { ok: true, affected: 1 }, err: "404 no vehicle · 409 already finished / no race" },
  ]],
  ["Chaos", [
    { m: "POST", p: "/boom", s: "Boom one random car", d: "Skips cars already mid-boom. A boom is a stun: RIP banner, 5 s fuse, the car is launched, drives again after 4 s.", params: [], res: { ok: true, affected: 1 }, err: "409 no race running" },
    { m: "POST", p: "/boom/:n", s: "Boom n random cars", params: [P("n", "1–200", "how many; `affected` is the real hit count", { in: "path", example: 3 })], res: { ok: true, affected: 3 } },
    { m: "POST", p: "/boom/:x", s: "Boom a specific car", params: [TARGET], res: { ok: true, affected: 1 }, err: "404 · 409 finished or mid-boom" },
    { m: "POST", p: "/boom/all", s: "Boom everyone", params: [P("except", "string", "id or login to spare", { in: "query", example: "shibikox" })], res: { ok: true, affected: 6 } },
    { m: "POST", p: "/boost/me", s: "Spend one of the streamer's boosts", d: "Identical to typing !boost (same as the in-game hotkey).", params: [], res: { ok: true, affected: 1 }, err: "404 not in race · 409 no boosts left" },
    { m: "POST", p: "/boost/:x", s: "Fire a free boost on a car", d: "Does not touch their !boost pool. Omit params for the game's random strength. Negative force shoves backwards.", params: [P("x", "string | all", "id, login, or `all`", { in: "path", example: "all" }), P("force", "number", "velocity kick per quarter second", { in: "query", example: 10 }), P("seconds", "number", "duration", { in: "query", example: 3 })], res: { ok: true, affected: 1 } },
    { m: "POST", p: "/boost/:x/use", s: "Spend one of a car's own boosts", d: "Same as that racer typing !boost: takes one from their pool. This is how a third-party bot drives its own car (turn its auto-boost off on the Bots page first). Use GET /track/zones to know where the straights are.", params: [TARGET], res: { ok: true, affected: 1, boosts: 1 }, err: "404 no vehicle · 409 no boosts left / not driving" },
    { m: "POST", p: "/boost/:x/add", s: "Add boosts to a car's !boost pool", d: "Fires a `boosts` SSE event per car (`{login, boosts, delta}`). A single target answers with the new pool.", params: [P("x", "string | all", "id, login, or `all`", { in: "path", example: "all" }), P("n", "int", "how many to add", { in: "query", default: 1 })], res: { ok: true, affected: 1, boosts: 3 } },
    { m: "POST", p: "/speed/:x", s: "Top-speed multiplier for a while", d: "0.3 = crawl, 1.5 = faster than everyone. Re-applied every frame over the AI's own throttle logic.", params: [P("x", "string | all", "id, login, or `all`", { in: "path", example: "all" }), P("mult", "number", "multiplier", { in: "query", default: 0.5 }), P("seconds", "number", "duration", { in: "query", default: 5 })], res: { ok: true, affected: 7 } },
    { m: "POST", p: "/respawn/:x", s: "The game's stuck-car respawn", d: "Blink for 2 s, drop back onto the track a little ahead. Viewers respawn their own car from chat with any alias in `settings.respawnCommand` (default `!race respawn|!respawn`).", params: [P("x", "string | all", "id, login, or `all`", { in: "path", example: "all" })], res: { ok: true, affected: 7 } },
  ]],
  ["Camera", [
    { m: "GET", p: "/camera", s: "Current camera state", d: "`shots` = the director's per-shot toggles (`settings.camera.shots`, all on by default): a shot switched off is never picked by the director; manual `POST /camera/<shot>` still works. Also the `camera` SSE event.", params: [], res: { auto: true, mode: "pack", target: "4 cars", cars: ["filian", "faker", "snoopdogg", "elonmusk"], fov: 75, shots: { grid: true, high: true, side: true, sweep: true, pack: true, front: true, chase: true, orbit: true, overhead: true, prop: true, finish: true, duel: true, pileup: true, boom: true } }, note: "mode: follow · free · chase · front · pack · sweep · side · high · orbit · overhead · prop · finish · manual" },
    { m: "POST", p: "/camera/auto", s: "Toggle the auto director", d: "Wide shots first (pack, side, sweep, high), the leader every other cut, the game's fixed track cams when a group approaches one, cuts to booms, a finish camera when cars near the line. Any camera key or mouse look by the streamer pauses it for 5 s. Each kind of shot can be switched off in `settings.camera.shots` (grid, high, side, sweep, pack, front, chase, orbit, overhead, prop, finish, duel, pileup, boom).", params: [P("on", "0|1", "set instead of toggle", { in: "query" })], res: { auto: true, mode: "pack", target: "4 cars", cars: ["…"], fov: 75, shots: { grid: true, "…": true } } },
    { m: "POST", p: "/camera/focus/:x", s: "The game's follow cam on a racer", d: "What pressing 1–9 does.", params: [TARGET, SECS], res: { auto: true, mode: "follow", target: "shibikox", cars: ["shibikox"], fov: 60 } },
    { m: "POST", p: "/camera/chase/:x", s: "Behind the car, road ahead visible", params: [P("x", "string", "id or login; omit for the leader", { in: "path", example: "shibikox" }), SECS, ...FOV], res: { auto: false, mode: "chase", target: "shibikox", cars: ["shibikox"], fov: 75 } },
    { m: "POST", p: "/camera/front/:x", s: "Ahead of the car looking back", d: "Shows the chasers too. The director prefers this for leaders.", params: [P("x", "string", "id or login; omit for the leader", { in: "path", example: "shibikox" }), SECS, ...FOV], res: { auto: false, mode: "front", target: "shibikox", cars: ["shibikox"], fov: 75 } },
    { m: "POST", p: "/camera/orbit/:x", s: "Slow pan around a car", params: [P("x", "string", "id or login; omit for the leader", { in: "path", example: "shibikox" }), SECS, ...FOV], res: { auto: false, mode: "orbit", target: "shibikox", cars: ["shibikox"], fov: 60 } },
    { m: "POST", p: "/camera/pack", s: "Behind the densest group, wide", params: [SECS, ...FOV], res: { auto: false, mode: "pack", target: "5 cars", cars: ["…"], fov: 75 } },
    { m: "POST", p: "/camera/side", s: "Alongside the group, looking ahead", d: "Jumps, inclines and corners show before the cars reach them.", params: [SECS, ...FOV], res: { auto: false, mode: "side", target: "5 cars", cars: ["…"], fov: 75 } },
    { m: "POST", p: "/camera/grid", s: "Start shot: on the road ahead, looking back at the field", d: "The director opens every race with this for 6 s, then a high overview.", params: [SECS], res: { auto: false, mode: "grid", target: "7 cars", cars: ["…"], fov: 60 } },
    { m: "POST", p: "/camera/sweep", s: "Crane pan as the group goes by", d: "Camera parked beside the track ahead of the group; only turns.", params: [SECS, ...FOV], res: { auto: false, mode: "sweep", target: "5 cars", cars: ["…"], fov: 70 } },
    { m: "POST", p: "/camera/high", s: "High and behind, down the road", params: [SECS, ...FOV], res: { auto: false, mode: "high", target: "5 cars", cars: ["…"], fov: 75 } },
    { m: "POST", p: "/camera/overhead", s: "Top-down of the whole track", params: [SECS, ...FOV], res: { auto: false, mode: "overhead", target: null, cars: [], fov: 60 } },
    { m: "POST", p: "/camera/finish", s: "Finish-line camera", d: "High and to the side of the real finish line, angled at the line and the last stretch of road.", params: [SECS, ...FOV], res: { auto: false, mode: "finish", target: "finish", cars: ["faker"], fov: 70 } },
    { m: "POST", p: "/camera/prop", s: "One of the game's fixed track cameras", d: "Nearest intact prop cam ahead of the leader. Knocked-over cams are skipped.", params: [SECS], res: { auto: false, mode: "prop", target: "Inner", cars: ["…"], fov: 60 }, err: "409 none in range" },
    { m: "POST", p: "/camera/boom", s: "Orbit the most recently boomed car", params: [SECS, ...FOV], res: { auto: false, mode: "orbit", target: "snoopdogg", cars: ["snoopdogg"], fov: 60 }, err: "409 nothing boomed yet" },
    { m: "POST", p: "/camera/wide/:x", s: "Follow cam, zoomed out, pack in frame", d: "The game's number-key follow the way a streamer uses it: wheel zoomed out to 40, tilted 22° down, orbited so the rest of the field sits behind the target. Omit :x for the leader. The director's most-used shot.", params: [P("x", "string", "id or login; omit for the leader", { in: "path", example: "shibikox" }), SECS], res: { auto: false, mode: "followwide", target: "shibikox", cars: ["shibikox"], fov: 60 } },
    { m: "POST", p: "/camera/leader", s: "Game follow cam on 1st", params: [SECS], res: { auto: false, mode: "follow", target: "filian", cars: ["filian"], fov: 60 } },
    { m: "POST", p: "/camera/free", s: "Release to the game's free cam", params: [], res: { auto: false, mode: "free", target: null, cars: [], fov: 60 } },
    { m: "POST", p: "/minimap", s: "Toggle the in-game mini map", d: "Placement and look live in Settings → Mini map (or `settings.minimap`).", params: [P("on", "0|1", "set instead of toggle", { in: "query" })], res: { enabled: true, x: 0.02, y: 0.03, w: 0.18, h: 0, marker: 3, bg: "#000000", alpha: 0.55, track: "#ffffff", pad: 1.15, leaderBig: true, names: true, aspect: "16:9", live: true } },
  ]],
  ["Settings & Twitch", [
    { m: "GET", p: "/settings", s: "All persisted settings", d: "`followerChecks` (ok · no token · unknown) says whether follower perks can work; `twitchTokenSet` whether a token is stored (the token itself is never returned). `camera.shots` = director toggles, `chatReplies` = confirm chat commands in Twitch chat, `respawnCommand` / `colorCommand` = alias lists separated by `|` or `,`.", params: [], res: { autoJoinStreamer: true, streamerColor: "#ff8a00", autoJoin: [{ id: "", login: "mybot", displayName: "My Bot", color: "#ff00aa", sub: false, image: null }], respawnCommand: "!race respawn|!respawn", colorCommand: "!race color|!color", chatReplies: true, perks: { colorCommand: "follower", coloredNames: "everyone", boostFollower: 1, boostSubscriber: 1, boostDeveloper: 1, boostHost: 0 }, twitchClientId: "", twitchTokenSet: false, followerChecks: "no token", followerChecksError: null, camera: { shots: { grid: true, high: true, side: true, sweep: true, pack: true, front: true, chase: true, orbit: true, overhead: true, prop: true, finish: true, duel: true, pileup: true, boom: true } }, ui: { boomCount: 1 }, overlay: { size: 40, board: 10 }, minimap: { enabled: true, aspect: "16:9" }, bots: ["elonmusk", "mrbeast6000"], config: { port: 8793, tokenRequired: false, bindAll: false, hotkeyBoost: "R", camUp: "Space", camDown: "C" } } },
    { m: "PUT", p: "/settings", s: "Update settings (merge)", d: "Send only the keys you own; the rest is kept. Broadcast to every page as the `settings` SSE event. `autoJoin` = everyone who joins every lobby (viewers and ★ bots alike; no master switch), `bots` = Twitch logins that race as AI cars, `customBots` = your own, `botOptions[login].autoBoost=false` = you drive that car's boosts via POST /boost/:x/use, `webhooks` = [{event, url, method, header, body, enabled}] fired on events (empty body = event JSON, else `{json}`/`{event}` are filled in). `camera.shots` = {grid, high, side, sweep, pack, front, chase, orbit, overhead, prop, finish, duel, pileup, boom}: false = the director never picks that shot (shots you leave out stay on). `chatReplies` (default true) = answer the color command in Twitch chat. `respawnCommand` / `colorCommand` take several aliases: `!race respawn|!respawn`. `twitchToken` + `twitchClientId` (a token with moderator:read:followers) turn follower checks on; `oauth:` / `Bearer` prefixes are stripped.", params: [P("body", "object", "settings", { in: "body" })], body: { autoJoinStreamer: true, streamerColor: "#ff8a00", autoJoin: [{ login: "mybot", displayName: "My Bot" }], ui: {}, overlay: {}, minimap: { enabled: true }, bots: ["elonmusk", "snoopdogg"], camera: { shots: { grid: false } }, chatReplies: true, respawnCommand: "!race respawn|!respawn" }, res: { autoJoinStreamer: true, "…": "…" } },
    { m: "PUT", p: "/config", s: "Change plugin config live", d: "Writes the BepInEx config and applies it immediately. A `port` or `bindAll` change restarts the HTTP server about half a second after the reply; reconnect to `url`. Keys are Unity KeyCode names (R, Space, Alpha1, F5, …).", params: [P("body", "object", "any of: port, bindAll, token, hotkeyBoost, camUp, camDown, tickHz, posHz", { in: "body" })], body: { hotkeyBoost: "R", camUp: "Space", camDown: "C", posHz: 60 }, res: { ok: true, errors: [], restarting: false, url: "http://127.0.0.1:8793/", config: { port: 8793, tokenRequired: false, bindAll: false, tickHz: 4, posHz: 60, hotkeyBoost: "R", camUp: "Space", camDown: "C" } } },
    { m: "GET", p: "/twitch/users", s: "Resolve Twitch logins", d: "Uses the game's own Twitch token (Helix). Unknown logins are simply omitted.", params: [P("logins", "csv", "up to 100 login names", { in: "query", example: "mrbeast6000,snoopdogg" })], res: { users: [{ id: "38746172", login: "mrbeast6000", displayName: "MrBeast6000", image: "https://static-cdn.jtvnw.net/…png", description: "Go watch Beast Games now on Prime Video!!!" }] } },
    { m: "GET", p: "/chat", s: "Can the mod talk in Twitch chat?", d: "The mod sends through the game's own chat connection (logged in as you with the game's token). `connected` = that connection is up; `canSend` = the game token has the `chat:edit` scope (looked up once an hour at id.twitch.tv; null until known). Without `chat:edit` Twitch drops sent lines silently: then relay the `color` / `denied` SSE events from your own bot instead. `replies` mirrors `settings.chatReplies`.", params: [], res: { connected: true, channel: "shibikox", login: "shibikox", replies: true, canSend: true, scopes: ["chat:read", "chat:edit"], scopesError: null, note: null } },
    { m: "POST", p: "/chat/say", s: "Say something in Twitch chat", d: "For bots and for testing the chat reply path. Goes out as the streamer through the game's connection (see GET /chat). Also emitted as the `chat` SSE event.", params: [P("text", "string", "the message (or a `{text}` JSON body); cut at 480 chars", { in: "query", example: "hello from the API" })], res: { ok: true, affected: 1, channel: "shibikox", text: "hello from the API" }, err: "400 no text · 409 chat not connected" },
    { m: "PUT", p: "/image/:login", s: "Upload a picture for a login", d: "Saved under BepInEx/config/shibiko.streamracer.images and returned as a path you can put in a join / custom bot / auto-join entry. The Bots page uses this for its file picker.", params: [P("login", "string", "login name", { in: "path", example: "chanbot" }), P("body", "object", "{data: 'data:image/png;base64,…'}", { in: "body" })], body: { data: "data:image/png;base64,iVBORw0KGgo…" }, res: { ok: true, path: "C:/…/BepInEx/config/shibiko.streamracer.images/chanbot.png", url: "/image/chanbot" } },
    { m: "GET", p: "/image/:login", s: "A racer's custom join image", d: "Served by the plugin so local file paths work in browser overlays. `avatar` in snapshots points here when set.", params: [P("login", "string", "login name", { in: "path", example: "mybot" })], res: "image bytes" },
  ]],
];

const EVENTS = [
  ["lobby", "a lobby opened (full snapshot)"], ["joined", "a racer joined (one vehicle)"], ["race_start", "countdown started (snapshot)"],
  ["positions", "full snapshot, TickHz/s (default 4)"], ["pos", "light frame at PosHz/s (default 60): {t, v:[[login, x, z, pct, place, finished, state], …]}"],
  ["finisher", "a car finished (one vehicle, with place)"], ["race_end", "race over (snapshot)"],
  ["boom", "a car was boomed (one vehicle) – bits, API, anything"], ["boost", "a car spent a pool boost (one vehicle, `boosts` = what is left): !boost in chat, the hotkey, /boost/me, /boost/:x/use, bot auto-boost"],
  ["boosts", "{login, displayName, boosts, delta} — a pool changed without a boost firing: /boost/:x/add, a perk grant, and once per car at race_start (delta 0)"],
  ["respawn", "a car was respawned (API or the chat command; one vehicle)"],
  ["developer", "a joined racer's backend title says Developer (one vehicle)"], ["camera", "{auto, mode, target, cars, fov, shots} on every cut"],
  ["settings", "full settings after any PUT"], ["color", "{login, displayName, color} when a racer's color is set (API or chat)"],
  ["perk", "{login, displayName, extraBoosts, reasons:[follower|sub|dev|host], boosts, followerChecks, follower} — the extra-boost decision for a joined car (Settings → Perks); fired even for 0 so the reason is visible; followerChecks = ok · no token · unknown"],
  ["denied", "{login, displayName, command, tier, followerChecks} — a chat command was refused by the tier rule"],
  ["chat", "{channel, text} — a line the mod sent to Twitch chat (a color reply or POST /chat/say)"],
  ["crash", "{login, displayName, state, place, pileup} — a car has been flipped / off the road / stuck for over 1 s (booms and jumps don't count); pileup = cars crashed in the last 8 s"],
  ["recovered", "{login, displayName, place} — a crashed car is driving again"], ["screen", "{screen, scene, running, lobby, vehicles} when the game changes screen"],
];

const SOURCES = [
  ["/overlay", "horizontal bar: avatars along a track line stacked by place, RIP / boost / finish banner", "?token=&size=40&names=0&accent=%23ff8a00"],
  ["/leaderboard", "vertical top-N list: place, avatar, name, progress", "?token=&rows=10&side=left|right&scale=1&accent=%23ff8a00"],
  ["/minimap", "route outline + car dots, same look as the in-game map", "?token=&aspect=16:9&names=1&leaderBig=1&track=%23fff&bg=%23000&alpha=0.55&marker=3"],
];

// ---------- helpers ----------
const fmt = (o) => (typeof o === "string" ? o : JSON.stringify(o, null, 2));
function hl(code, lang = "json") {
  try { return window.hljs ? window.hljs.highlight(code, { language: lang }).value : esc(code); } catch { return esc(code); }
}
const esc = (s) => String(s).replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]));

function Code({ code, lang = "json", label }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => { try { await navigator.clipboard.writeText(code); setCopied(true); setTimeout(() => setCopied(false), 1200); } catch {} };
  return html`
    <div class="code">
      <div class="code-bar"><span>${label || lang}</span><button class="copy" onClick=${copy}>${copied ? "copied" : "copy"}</button></div>
      <pre><code dangerouslySetInnerHTML=${{ __html: hl(code, lang) }}></code></pre>
    </div>`;
}

function buildUrl(route, vals) {
  let path = route.p.replace(/:(\w+)/g, (_, k) => encodeURIComponent(vals[k] ?? ""));
  path = path.replace(/\/+$/, "").replace(/\/\//g, "/") || "/";
  const qs = route.params.filter((p) => p.in === "query" && vals[p.name] !== undefined && vals[p.name] !== "").map((p) => `${p.name}=${encodeURIComponent(vals[p.name])}`);
  return path + (qs.length ? "?" + qs.join("&") : "");
}
function curlFor(route, url, body) {
  const tok = getToken() ? ` -H "Authorization: Bearer ${getToken()}"` : "";
  if (route.m === "GET") return `curl${tok} "${location.origin}${url}"`;
  const b = body ? ` -H "Content-Type: application/json" -d '${body.replace(/'/g, "'\\''")}'` : " -d ''";
  return `curl -X ${route.m}${tok}${b} "${location.origin}${url}"`;
}

// ---------- one route card ----------
function Route({ r }) {
  const [open, setOpen] = useState(false);
  const init = {}; for (const p of r.params) if (p.in !== "body") init[p.name] = p.example ?? p.default ?? "";
  const [vals, setVals] = useState(init);
  const [body, setBody] = useState(r.body ? fmt(r.body) : "");
  const [res, setRes] = useState(null);
  const [busy, setBusy] = useState(false);
  const url = buildUrl(r, vals);
  const send = async () => {
    setBusy(true); const t0 = performance.now();
    try {
      const resp = await fetch(url, { method: r.m, headers: { "Content-Type": "application/json", ...authHeaders() }, body: r.m === "GET" ? undefined : (body || "") });
      const ct = resp.headers.get("content-type") || "";
      const text = ct.startsWith("image/") ? `(${ct}, ${resp.headers.get("content-length") || "?"} bytes)` : await resp.text();
      let pretty = text; try { pretty = JSON.stringify(JSON.parse(text), null, 2); } catch {}
      setRes({ status: resp.status, ms: Math.round(performance.now() - t0), body: pretty });
    } catch (e) { setRes({ status: 0, ms: 0, body: String(e) }); }
    setBusy(false);
  };
  return html`
    <div class=${"route " + r.m.toLowerCase() + (open ? " open" : "")}>
      <button class="route-head" onClick=${() => setOpen(!open)}>
        <span class="m">${r.m}</span><code class="path">${r.p}</code><span class="sum">${r.s}</span><span class="chev">${open ? "▾" : "▸"}</span>
      </button>
      ${open ? html`
        <div class="route-body">
          ${r.d ? html`<p class="desc" dangerouslySetInnerHTML=${{ __html: esc(r.d).replace(/`([^`]+)`/g, "<code>$1</code>") }}></p>` : null}
          ${r.note ? html`<p class="desc note">${r.note}</p>` : null}
          <div class="cols">
            <div>
              <h4>Parameters</h4>
              ${r.params.length ? html`
                <table class="params"><thead><tr><th>name</th><th>in</th><th>type</th><th>description</th><th>value</th></tr></thead><tbody>
                  ${r.params.map((p) => html`
                    <tr key=${p.name}>
                      <td><code>${p.name}</code></td><td>${p.in}</td><td>${p.type}</td>
                      <td>${p.desc}${p.default !== undefined ? html` <small>default ${String(p.default)}</small>` : null}</td>
                      <td>${p.in === "body" ? html`<textarea rows="4" spellCheck="false" value=${body} onInput=${(e) => setBody(e.target.value)}></textarea>`
                                            : html`<input value=${vals[p.name] ?? ""} placeholder=${p.example ?? ""} onInput=${(e) => setVals({ ...vals, [p.name]: e.target.value })} />`}</td>
                    </tr>`)}
                </tbody></table>` : html`<p class="desc">none</p>`}
              ${r.err ? html`<h4>Errors</h4><p class="desc">${r.err}</p>` : null}
            </div>
            <div>
              <h4>Try it</h4>
              <div class="try">
                <code class="url">${r.m} ${url}</code>
                <button onClick=${send} disabled=${busy} aria-busy=${busy}>Execute</button>
              </div>
              ${res ? html`
                <div class="res-head"><span class=${"code-status " + (res.status < 300 ? "ok" : "bad")}>${res.status || "ERR"}</span><span>${res.ms} ms</span></div>
                <${Code} code=${res.body} label="response" />` : html`
                <h5>Example response</h5>
                <${Code} code=${fmt(r.res)} label="200" />`}
              <h5>curl</h5>
              <${Code} code=${curlFor(r, url, r.m === "GET" ? "" : body)} lang="bash" label="bash" />
            </div>
          </div>
        </div>` : null}
    </div>`;
}

// ---------- events ----------
function EventLog() {
  const [log, setLog] = useState([]);
  const [on, setOn] = useState(false);
  const [filter, setFilter] = useState("");
  const esRef = useRef(null);
  useEffect(() => () => esRef.current?.close(), []);
  const toggle = () => {
    if (on) { esRef.current?.close(); esRef.current = null; setOn(false); return; }
    const es = new EventSource(withToken("/events"));
    for (const [ev] of EVENTS) es.addEventListener(ev, (e) => setLog((l) => [{ t: new Date().toLocaleTimeString(), ev, data: e.data }, ...l].slice(0, 60)));
    es.onerror = () => setLog((l) => [{ t: new Date().toLocaleTimeString(), ev: "error", data: "connection lost (token? game closed?)" }, ...l].slice(0, 60));
    esRef.current = es; setOn(true);
  };
  const shown = log.filter((e) => !filter || e.ev === filter);
  return html`
    <div class="evlog">
      <div class="res-head">
        <button class=${on ? "" : "secondary"} onClick=${toggle}>${on ? "Disconnect" : "Listen to /events"}</button>
        <select value=${filter} onChange=${(e) => setFilter(e.target.value)}><option value="">all events</option>${EVENTS.map(([n]) => html`<option key=${n} value=${n}>${n}</option>`)}</select>
        ${log.length ? html`<button class="secondary outline" onClick=${() => setLog([])}>Clear</button>` : null}
      </div>
      ${shown.slice(0, 12).map((e, i) => html`<div class="ev" key=${i}><span class="t">${e.t}</span><span class="name">${e.ev}</span><span class="d">${e.data.length > 300 ? e.data.slice(0, 300) + " …" : e.data}</span></div>`)}
    </div>`;
}

const CLIENT = `const BASE = "http://127.0.0.1:8793";
const H = { Authorization: "Bearer <token>" };        // only if api.Token is set in the BepInEx config

// live positions at 60 fps + every event
const es = new EventSource(BASE + "/events?token=<token>");
es.addEventListener("pos",      e => moveDots(JSON.parse(e.data).v));      // [[login, x, z, pct, place, finished], …]
es.addEventListener("finisher", e => tts(JSON.parse(e.data).displayName + " finished!"));
es.addEventListener("boom",     e => sfx("boom"));

// actions
await fetch(BASE + "/boom/all?except=shibikox", { method: "POST", headers: H });
await fetch(BASE + "/camera/focus/mybot?seconds=6", { method: "POST", headers: H });
await fetch(BASE + "/join", { method: "POST", headers: { ...H, "Content-Type": "application/json" },
  body: JSON.stringify([{ login: "mybot", displayName: "My Bot", color: "#ff00aa", image: "C:/overlays/bot.png" }]) });`;

export default function ApiDocs() {
  useCss("pages/api.css");
  const [q, setQ] = useState("");
  const [tab, setTab] = useState("routes");
  const match = (r) => !q || (r.p + " " + r.s + " " + (r.d || "")).toLowerCase().includes(q.toLowerCase());
  return html`
    <div class="api">
      <article class="api-head">
        <header>API <span class="base">${location.origin}</span></header>
        <div class="api-nav">
          <div class="tabs2">
            ${[["routes", "Routes"], ["events", "Events (SSE)"], ["client", "Client sample"]].map(([k, l]) => html`<button key=${k} class=${tab === k ? "active" : ""} onClick=${() => setTab(k)}>${l}</button>`)}
          </div>
          ${tab === "routes" ? html`<input type="search" placeholder="filter routes" value=${q} onInput=${(e) => setQ(e.target.value)} />` : null}
        </div>
        <p class="desc">Everything is plain HTTP + Server-Sent Events, CORS open. <code>:x</code> = Twitch user id (digits) or login name. Errors come back as <code>{"error": "…"}</code>:
          401 token required · 404 no such car · 409 not applicable (no race, finished, lobby closed) · 501 a game update renamed something.
          ${getToken() ? html` Token from Settings is attached to every call here.` : null}</p>
      </article>

      ${tab === "routes" ? GROUPS.map(([name, routes]) => {
        const rs = routes.filter(match); if (!rs.length) return null;
        return html`<section class="group" key=${name}><h3>${name}</h3>${rs.map((r) => html`<${Route} key=${r.m + r.p} r=${r} />`)}</section>`;
      }) : null}

      ${tab === "events" ? html`
        <article>
          <header>GET /events</header>
          <p class="desc">One long-lived request; the browser's <code>EventSource</code> handles it (Node 22+ too). Each line below is an event name; the payload is JSON.</p>
          <table class="params"><tbody>${EVENTS.map(([n, d]) => html`<tr key=${n}><td><code>${n}</code></td><td>${d}</td></tr>`)}</tbody></table>
          <h4>Live</h4>
          <${EventLog} />
        </article>` : null}

      ${tab === "client" ? html`
        <article>
          <header>Client sample</header>
          <${Code} code=${CLIENT} lang="javascript" label="javascript" />
          <h4>Browser sources</h4>
          <p class="desc">Transparent pages served by the plugin for OBS, one source each; they reconnect on their own, follow Settings → Overlay look / Mini map live, and draw nothing outside a race (the bar and the leaderboard fade out after <code>race_end</code>; <code>settings.overlay.showInLobby</code> shows the field in the lobby too).</p>
          <table class="params sources"><thead><tr><th>page</th><th>what</th><th>query params (override the saved look for that source)</th></tr></thead><tbody>
            ${SOURCES.map(([path, what, params]) => html`<tr key=${path}><td><code>${path}</code></td><td>${what}</td><td><code>${params}</code></td></tr>`)}
          </tbody></table>
        </article>` : null}
    </div>`;
}
