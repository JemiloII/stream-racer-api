// API page: the route reference (GROUPS), the event catalogue (EVENTS), the browser sources (SOURCES) and a client
// sample, rendered as searchable route cards with a request tester (pages/api/RouteCard.js) and a live /events
// viewer (pages/api/EventLog.js). tests/api/events.test.ts and tests/ui/pages/api.spec.ts parse GROUPS and EVENTS
// straight from this file, so the tables stay here.
import { useState } from "react";
import { html, useCss } from "../lib/html.js";
import { getToken } from "../lib/api.js";
import Code from "./api/Code.js";
import RouteCard from "./api/RouteCard.js";
import EventLog from "./api/EventLog.js";

// ---------- reference ----------
// A route: {method, path, summary, description?, params: [param…], body?, response, errors?, note?}.
// A param: {name, type, description, in: "path"|"query"|"body", example?, default?}.
const param = (name, type, description, extra = {}) => ({ name, type, description, ...extra });
const TARGET = param("x", "string", "Twitch user id (digits) or login name", { in: "path", example: "shibikox" });
const SECONDS = param("seconds", "number", "how long the shot lasts; 0 = stay until the next camera call", { in: "query", default: 8 });
const FOV_PARAMS = [param("fov", "number", "field of view at the start of the shot", { in: "query", default: 60 }), param("fovTo", "number", "field of view at the end (a zoom over the shot)", { in: "query" })];

const GROUPS = [
  ["Race state", [
    { method: "GET", path: "/race", summary: "Snapshot of the current race or lobby", description: "`state` per car: driving · air · flipped · offroad · stunned (boomed) · stuck · finished.", params: [], response: { running: true, lobby: false, streamer: "shibikox", vehicles: [{ place: 1, id: "23256990", login: "shibikox", displayName: "ShibikoX", color: "#FF8A00", sub: true, type: "REGULAR", progress: 812.4, finishAt: 1616, pct: 50.3, finished: false, boosts: 2, state: "driving", image: null, avatar: "https://static-cdn.jtvnw.net/…/profile_image-300x300.png", title: "Subscriber", x: 394.4, z: -613.3 }] } },
    { method: "GET", path: "/screen", summary: "Which screen the game is on", description: "Also emitted as the `screen` SSE event whenever it changes. Handy for bots that need to wait for a lobby.", params: [], response: { screen: "lobby", scene: "Play", running: false, lobby: true, vehicles: 3 }, note: "screen: home · play · settings · lobby · racing · postgame · trackbuilder" },
    { method: "GET", path: "/version", summary: "Mod, game, Unity and BepInEx versions + update check", description: "`upToDate` is null until `api.UpdateUrl` (plugin config) points at a JSON `{version, url}`; then true/false against `latest`.", params: [], response: { api: "1.0.0", commit: "a1b2c3d", game: "EARLY ACCESS 2.4.0", unity: "2021.3.17f1", bepinex: "5.4.23", latest: "1.0.0", upToDate: true, updateUrl: null, developer: "Shibiko", twitch: "https://twitch.tv/ShibikoX" } },
    { method: "GET", path: "/me", summary: "The logged-in streamer", params: [], response: { id: "23256990", login: "shibikox", inRace: true } },
    { method: "GET", path: "/twitch/auth", summary: "Log the mod into Twitch (opens the Twitch consent page)", description: "Implicit OAuth with your own Twitch app (`settings.twitchClientId`; add the redirect URI to the app first). Asks for moderator:read:followers, user:write:chat, user:read:chat, channel:read:subscriptions. The token lands in settings; follower checks and chat replies use it.", params: [param("clientId", "string", "override/set the client id", { in: "query" })], response: { redirect: "302 → id.twitch.tv" }, errors: "400 no client id" },
    { method: "GET", path: "/twitch/token", summary: "Twitch login status", params: [], response: { connected: true, login: "shibikox", userId: "23256990", clientId: "…", scopes: ["moderator:read:followers", "user:write:chat"], wanted: ["…"], redirectUri: "http://localhost:8793/twitch/callback", features: { followerChecks: true, chatReplies: true }, missing: [] } },
    { method: "POST", path: "/twitch/token", summary: "Store a token (the callback page does this)", description: "Validated against id.twitch.tv; login, user id and scopes are remembered.", params: [param("body", "object", "{token, state?}", { in: "body" })], body: { token: "abc…" }, response: { connected: true, login: "shibikox" }, errors: "401 token rejected" },
    { method: "DELETE", path: "/twitch/token", summary: "Forget the Twitch login", params: [], response: { ok: true, affected: 1 } },
    { method: "GET", path: "/inventory/:login", summary: "What a racer has left: boosts and chat respawns", description: "`respawns` is -1 when `settings.respawnLimit` is -1 (unlimited); the default limit is 0 = chat respawns off. Chat respawns count against the limit; the streamer's POST /respawn does not. Bots answer `!race inv` with this.", params: [TARGET], response: { login: "shibikox", displayName: "ShibikoX", boosts: 1, respawns: 2, respawnLimit: 2, running: true }, errors: "404 no vehicle" },
    { method: "PUT", path: "/tier/:login", summary: "Tell the mod a viewer's Twitch tier (from your bot)", description: "The game's own token can't see followers, so a bot with the scopes pushes `{follower, subscriber}` per login (on join, on follow/sub events). Wins over the game's flags; the perk grant is re-run and only the difference in extra boosts is added. `GET /tier/:login` reads it back, `DELETE /tier` forgets all. Emits a `tier` event.", params: [TARGET, param("body", "object", "{follower?: bool, subscriber?: bool, source?: string}", { in: "body" })], body: { follower: true, subscriber: false, source: "voisona-bot" }, response: { login: "viewer", follower: true, subscriber: false, source: "voisona-bot", inRace: true, granted: 1 }, errors: "400 nothing to set" },
    { method: "GET", path: "/perks/:login", summary: "Why a viewer did or didn't get extra boosts", description: "Settings → Perks decides `extraBoosts` (follower + subscriber + developer + host, stacking). `followerChecks`: `ok` = lookups run; `no token` = no Twitch token + client id pasted on the Settings page, so followers are never detected; `unknown` = a token is set but the last Helix lookup failed (`followerChecksError` says why, e.g. 401 = the token lacks moderator:read:followers for this channel). An unknown follower status starts a lookup: ask again a second later. `granted` = the boosts were already added to the car.", params: [param("login", "string", "Twitch login (any casing)", { in: "path", example: "shibikox" })], response: { login: "shibikox", inRace: true, follower: false, followerKnown: false, subscriber: true, developer: false, host: false, extraBoosts: 1, why: ["sub", "follower boost needs a Twitch token with moderator:read:followers + its client id (Settings -> Perks)"], followerChecks: "no token", followerChecksError: null, granted: true, boosts: 3, perks: { colorCommand: "follower", coloredNames: "everyone", boostFollower: 1, boostSubscriber: 1, boostDeveloper: 1, boostHost: 0 } }, errors: "400 no login" },
    { method: "GET", path: "/track", summary: "Route outline for maps", description: "Points are [x, z] in world units along the road. `raw=1` returns the bare waypoints instead of the densified line.", params: [param("raw", "0|1", "bare waypoint transforms", { in: "query", default: 0 })], response: { points: [[291.9, -738.4], [289.1, -742.0], ["…"]], bounds: { minX: -153.4, maxX: 291.9, minZ: -1080.6, maxZ: -738.4 } } },
    { method: "GET", path: "/track/zones", summary: "Good straights to boost on, for the current map", description: "Computed from the route: stretches where it turns less than 12° and stays flat over the next 40 units, at least 55 long. Route distances match each car's `progress`. Bots with auto-boost use these.", params: [], response: { map: 845, mapName: "Locate Yourself", length: 2016.3, finishAt: 1616, zones: [{ start: 120, end: 310, length: 190 }, { start: 640, end: 760, length: 120 }] } },
    { method: "GET", path: "/maps", summary: "Map list from the game's backend", description: "First call kicks off the fetch and answers 202 with `loading: true`; call again.", params: [], response: { loading: false, maps: [{ id: 845, name: "Gherkin Gauntlent", creator: "UnclePickle89", official: false, length: "NORMAL", avgTime: 228.2 }] } },
  ]],
  ["Lobby & race flow", [
    { method: "POST", path: "/lobby", summary: "Create a lobby from any screen", description: "Fetches the map list, puts your pick first in the game's playlist and presses PLAY. Uses the Play tab's saved options (max cars, type, time). Answers 202 while loading; watch the `lobby` SSE event.", params: [param("map", "string", "map id or part of a name; default = first map", { in: "query", example: "locate" })], response: { ok: true, state: "opening" }, errors: "409 race running / already in a lobby" },
    { method: "POST", path: "/join", summary: "Add racers to the lobby", description: "Non-Twitch names work (bots). `image` (URL or local path) replaces the avatar in the lobby and results lists and is served back at `/image/:login`.", params: [param("body", "object | array", "{id, login, displayName, color, sub, image}", { in: "body" })], body: [{ id: "", login: "mybot", displayName: "My Bot", color: "#ff00aa", image: "C:/overlays/bot.png" }], response: { ok: true, affected: 1 }, errors: "409 race running" },
    { method: "POST", path: "/join/me", summary: "Add the streamer's own car", description: "Same as the lobby's JOIN GAME button. Color defaults to Settings → My car color.", params: [param("color", "hex", "car / name color", { in: "query", example: "#ff8a00" })], response: { ok: true, affected: 1 }, errors: "409 no lobby, not logged in, already joined" },
    { method: "POST", path: "/autojoin/join", summary: "Join the saved auto-join list now", params: [], response: { ok: true, affected: 3 }, errors: "409 race running" },
    { method: "POST", path: "/kick/:x", summary: "Remove a racer from the lobby", params: [TARGET], response: { ok: true, affected: 1 }, errors: "404 no vehicle · 409 race running" },
    { method: "POST", path: "/lobby/exit", summary: "Leave the lobby (the EXIT button)", description: "Back to the home screen; the cars in the lobby are dropped.", params: [], response: { ok: true, affected: 1 }, errors: "409 not in a lobby" },
    { method: "POST", path: "/race/start", summary: "Start the lobby countdown", description: "What the START button does. The race begins when the countdown (Settings → start countdown) hits zero.", params: [param("now", "0|1", "skip the countdown, start immediately", { in: "query", default: 0 })], response: { ok: true, affected: 1 }, errors: "409 no lobby with cars" },
    { method: "POST", path: "/race/end", summary: "Force-end the race", params: [], response: { ok: true, affected: 1 } },
    { method: "POST", path: "/race/next", summary: "Next map from the queue", description: "Works from the post-game screen or anywhere idle. The queue is set by `/lobby` or the Play tab.", params: [], response: { ok: true, affected: 1 }, errors: "409 race running / nothing queued" },
    { method: "POST", path: "/color/:x", summary: "Set a racer's color", description: "Hex (#ff8800 / ff8800) or a name (red, cyan, orange, pink, gold, lime, …). Applied now (car, label, lobby row, leaderboard, overlays) and remembered for future joins: a saved color is put on the car before the game builds its lobby row, and the lobby/results name is painted in it (same `perks.coloredNames` tier and `colorLeaderboard` switch as the in-game leaderboard). Omit `color` to forget the saved one; `/color/reset` forgets all. Viewers can do the same from chat with any alias in `settings.colorCommand` (default `!race color|!color`); when `settings.chatReplies` is on the game's own chat connection answers `@name color set to #ff8800` or `@name color is for follower+`.", params: [TARGET, param("color", "string", "hex or name", { in: "query", example: "#ff8800" })], response: { ok: true, affected: 1 }, errors: "400 bad color" },
    { method: "POST", path: "/finish/:x", summary: "Mark a racer as finished", description: "Runs the car's own finish-line trigger. For the game bug where a car crosses the line without touching the trigger.", params: [TARGET], response: { ok: true, affected: 1 }, errors: "404 no vehicle · 409 already finished / no race" },
  ]],
  ["Chaos", [
    { method: "POST", path: "/boom", summary: "Boom one random car", description: "Skips cars already mid-boom. A boom is a stun: RIP banner, 5 s fuse, the car is launched, drives again after 4 s.", params: [], response: { ok: true, affected: 1 }, errors: "409 no race running" },
    { method: "POST", path: "/boom/:n", summary: "Boom n random cars", params: [param("n", "1–200", "how many; `affected` is the real hit count", { in: "path", example: 3 })], response: { ok: true, affected: 3 } },
    { method: "POST", path: "/boom/:x", summary: "Boom a specific car", params: [TARGET], response: { ok: true, affected: 1 }, errors: "404 · 409 finished or mid-boom" },
    { method: "POST", path: "/boom/all", summary: "Boom everyone", params: [param("except", "string", "id or login to spare", { in: "query", example: "shibikox" })], response: { ok: true, affected: 6 } },
    { method: "POST", path: "/boost/me", summary: "Spend one of the streamer's boosts", description: "Identical to typing !boost (same as the in-game hotkey).", params: [], response: { ok: true, affected: 1 }, errors: "404 not in race · 409 no boosts left" },
    { method: "POST", path: "/boost/:x", summary: "Fire a free boost on a car", description: "Does not touch their !boost pool. Omit params for the game's random strength. Negative force shoves backwards.", params: [param("x", "string | all", "id, login, or `all`", { in: "path", example: "all" }), param("force", "number", "velocity kick per quarter second", { in: "query", example: 10 }), param("seconds", "number", "duration", { in: "query", example: 3 })], response: { ok: true, affected: 1 } },
    { method: "POST", path: "/boost/:x/use", summary: "Spend one of a car's own boosts", description: "Same as that racer typing !boost: takes one from their pool. This is how a third-party bot drives its own car (turn its auto-boost off on the Bots page first). Use GET /track/zones to know where the straights are.", params: [TARGET], response: { ok: true, affected: 1, boosts: 1 }, errors: "404 no vehicle · 409 no boosts left / not driving" },
    { method: "POST", path: "/boost/:x/add", summary: "Add boosts to a car's !boost pool", description: "Fires a `boosts` SSE event per car (`{login, boosts, delta}`). A single target answers with the new pool.", params: [param("x", "string | all", "id, login, or `all`", { in: "path", example: "all" }), param("n", "int", "how many to add", { in: "query", default: 1 })], response: { ok: true, affected: 1, boosts: 3 } },
    { method: "POST", path: "/speed/:x", summary: "Top-speed multiplier for a while", description: "0.3 = crawl, 1.5 = faster than everyone. Re-applied every frame over the AI's own throttle logic.", params: [param("x", "string | all", "id, login, or `all`", { in: "path", example: "all" }), param("mult", "number", "multiplier", { in: "query", default: 0.5 }), param("seconds", "number", "duration", { in: "query", default: 5 })], response: { ok: true, affected: 7 } },
    { method: "POST", path: "/respawn/:x", summary: "The game's stuck-car respawn", description: "Blink for 2 s, drop back onto the track a little ahead. Viewers respawn their own car from chat with any alias in `settings.respawnCommand` (default `!race respawn|!respawn`).", params: [param("x", "string | all", "id, login, or `all`", { in: "path", example: "all" })], response: { ok: true, affected: 7 } },
  ]],
  ["Camera", [
    { method: "GET", path: "/camera", summary: "Current camera state", description: "`shots` = the director's per-shot toggles (`settings.camera.shots`, all on by default): a shot switched off is never picked by the director; manual `POST /camera/<shot>` still works. Also the `camera` SSE event.", params: [], response: { auto: true, mode: "pack", target: "4 cars", cars: ["filian", "faker", "snoopdogg", "elonmusk"], fov: 75, shots: { grid: true, high: true, side: true, sweep: true, pack: true, front: true, chase: true, orbit: true, overhead: true, prop: true, finish: true, duel: true, pileup: true, boom: true } }, note: "mode: follow · free · chase · front · pack · sweep · side · high · orbit · overhead · prop · finish · manual" },
    { method: "POST", path: "/camera/auto", summary: "Toggle the auto director", description: "Wide shots first (pack, side, sweep, high), the leader every other cut, the game's fixed track cams when a group approaches one, cuts to booms, a finish camera when cars near the line. Any camera key or mouse look by the streamer pauses it for 5 s. Each kind of shot can be switched off in `settings.camera.shots` (grid, high, side, sweep, pack, front, chase, orbit, overhead, prop, finish, duel, pileup, boom).", params: [param("on", "0|1", "set instead of toggle", { in: "query" })], response: { auto: true, mode: "pack", target: "4 cars", cars: ["…"], fov: 75, shots: { grid: true, "…": true } } },
    { method: "POST", path: "/camera/focus/:x", summary: "The game's follow cam on a racer", description: "What pressing 1–9 does.", params: [TARGET, SECONDS], response: { auto: true, mode: "follow", target: "shibikox", cars: ["shibikox"], fov: 60 } },
    { method: "POST", path: "/camera/chase/:x", summary: "Behind the car, road ahead visible", params: [param("x", "string", "id or login; omit for the leader", { in: "path", example: "shibikox" }), SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "chase", target: "shibikox", cars: ["shibikox"], fov: 75 } },
    { method: "POST", path: "/camera/front/:x", summary: "Ahead of the car looking back", description: "Shows the chasers too. The director prefers this for leaders.", params: [param("x", "string", "id or login; omit for the leader", { in: "path", example: "shibikox" }), SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "front", target: "shibikox", cars: ["shibikox"], fov: 75 } },
    { method: "POST", path: "/camera/orbit/:x", summary: "Slow pan around a car", params: [param("x", "string", "id or login; omit for the leader", { in: "path", example: "shibikox" }), SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "orbit", target: "shibikox", cars: ["shibikox"], fov: 60 } },
    { method: "POST", path: "/camera/pack", summary: "Behind the densest group, wide", params: [SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "pack", target: "5 cars", cars: ["…"], fov: 75 } },
    { method: "POST", path: "/camera/side", summary: "Alongside the group, looking ahead", description: "Jumps, inclines and corners show before the cars reach them.", params: [SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "side", target: "5 cars", cars: ["…"], fov: 75 } },
    { method: "POST", path: "/camera/grid", summary: "Start shot: on the road ahead, looking back at the field", description: "The director opens every race with this for 6 s, then a high overview.", params: [SECONDS], response: { auto: false, mode: "grid", target: "7 cars", cars: ["…"], fov: 60 } },
    { method: "POST", path: "/camera/sweep", summary: "Crane pan as the group goes by", description: "Camera parked beside the track ahead of the group; only turns.", params: [SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "sweep", target: "5 cars", cars: ["…"], fov: 70 } },
    { method: "POST", path: "/camera/high", summary: "High and behind, down the road", params: [SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "high", target: "5 cars", cars: ["…"], fov: 75 } },
    { method: "POST", path: "/camera/overhead", summary: "Top-down of the whole track", params: [SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "overhead", target: null, cars: [], fov: 60 } },
    { method: "POST", path: "/camera/finish", summary: "Finish-line camera", description: "High and to the side of the real finish line, angled at the line and the last stretch of road.", params: [SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "finish", target: "finish", cars: ["faker"], fov: 70 } },
    { method: "POST", path: "/camera/prop", summary: "One of the game's fixed track cameras", description: "Nearest intact prop cam ahead of the leader. Knocked-over cams are skipped.", params: [SECONDS], response: { auto: false, mode: "prop", target: "Inner", cars: ["…"], fov: 60 }, errors: "409 none in range" },
    { method: "POST", path: "/camera/boom", summary: "Orbit the most recently boomed car", params: [SECONDS, ...FOV_PARAMS], response: { auto: false, mode: "orbit", target: "snoopdogg", cars: ["snoopdogg"], fov: 60 }, errors: "409 nothing boomed yet" },
    { method: "POST", path: "/camera/wide/:x", summary: "Follow cam, zoomed out, pack in frame", description: "The game's number-key follow the way a streamer uses it: wheel zoomed out to 40, tilted 22° down, orbited so the rest of the field sits behind the target. Omit :x for the leader. The director's most-used shot.", params: [param("x", "string", "id or login; omit for the leader", { in: "path", example: "shibikox" }), SECONDS], response: { auto: false, mode: "followwide", target: "shibikox", cars: ["shibikox"], fov: 60 } },
    { method: "POST", path: "/camera/leader", summary: "Game follow cam on 1st", params: [SECONDS], response: { auto: false, mode: "follow", target: "filian", cars: ["filian"], fov: 60 } },
    { method: "POST", path: "/camera/free", summary: "Release to the game's free cam", params: [], response: { auto: false, mode: "free", target: null, cars: [], fov: 60 } },
    { method: "POST", path: "/minimap", summary: "Toggle the in-game mini map", description: "Placement and look live in Settings → Mini map (or `settings.minimap`).", params: [param("on", "0|1", "set instead of toggle", { in: "query" })], response: { enabled: true, x: 0.02, y: 0.03, w: 0.18, h: 0, marker: 3, bg: "#000000", alpha: 0.55, track: "#ffffff", pad: 1.15, leaderBig: true, names: true, aspect: "16:9", live: true } },
  ]],
  ["Settings & Twitch", [
    { method: "GET", path: "/settings", summary: "All persisted settings", description: "`followerChecks` (ok · no token · unknown) says whether follower perks can work; `twitchTokenSet` whether a token is stored (the token itself is never returned). `camera.shots` = director toggles, `chatReplies` = confirm chat commands in Twitch chat, `respawnCommand` / `colorCommand` = alias lists separated by `|` or `,`.", params: [], response: { autoJoinStreamer: true, streamerColor: "#ff8a00", autoJoin: [{ id: "", login: "mybot", displayName: "My Bot", color: "#ff00aa", sub: false, image: null }], respawnCommand: "!race respawn|!respawn", colorCommand: "!race color|!color", chatReplies: true, perks: { colorCommand: "follower", coloredNames: "everyone", boostFollower: 1, boostSubscriber: 1, boostDeveloper: 1, boostHost: 0 }, twitchClientId: "", twitchTokenSet: false, followerChecks: "no token", followerChecksError: null, camera: { shots: { grid: true, high: true, side: true, sweep: true, pack: true, front: true, chase: true, orbit: true, overhead: true, prop: true, finish: true, duel: true, pileup: true, boom: true } }, ui: { boomCount: 1 }, overlay: { size: 40, board: 10 }, minimap: { enabled: true, aspect: "16:9" }, bots: ["elonmusk", "mrbeast6000"], config: { port: 8793, tokenRequired: false, bindAll: false, hotkeyBoost: "R", camUp: "Space", camDown: "C" } } },
    { method: "PUT", path: "/settings", summary: "Update settings (merge)", description: "Send only the keys you own; the rest is kept. Broadcast to every page as the `settings` SSE event. `autoJoin` = everyone who joins every lobby (viewers and ★ bots alike; no master switch), `bots` = Twitch logins that race as AI cars, `customBots` = your own, `botOptions[login].autoBoost=false` = you drive that car's boosts via POST /boost/:x/use, `webhooks` = [{event, url, method, header, body, enabled}] fired on events (empty body = event JSON, else `{json}`/`{event}` are filled in). `camera.shots` = {grid, high, side, sweep, pack, front, chase, orbit, overhead, prop, finish, duel, pileup, boom}: false = the director never picks that shot (shots you leave out stay on). `chatReplies` (default true) = answer the color command in Twitch chat. `respawnCommand` / `colorCommand` take several aliases: `!race respawn|!respawn`. `twitchToken` + `twitchClientId` (a token with moderator:read:followers) turn follower checks on; `oauth:` / `Bearer` prefixes are stripped.", params: [param("body", "object", "settings", { in: "body" })], body: { autoJoinStreamer: true, streamerColor: "#ff8a00", autoJoin: [{ login: "mybot", displayName: "My Bot" }], ui: {}, overlay: {}, minimap: { enabled: true }, bots: ["elonmusk", "snoopdogg"], camera: { shots: { grid: false } }, chatReplies: true, respawnCommand: "!race respawn|!respawn" }, response: { autoJoinStreamer: true, "…": "…" } },
    { method: "PUT", path: "/config", summary: "Change plugin config live", description: "Writes the BepInEx config and applies it immediately. A `port` or `bindAll` change restarts the HTTP server about half a second after the reply; reconnect to `url`. Keys are Unity KeyCode names (R, Space, Alpha1, F5, …).", params: [param("body", "object", "any of: port, bindAll, token, hotkeyBoost, camUp, camDown, tickHz, posHz", { in: "body" })], body: { hotkeyBoost: "R", camUp: "Space", camDown: "C", posHz: 60 }, response: { ok: true, errors: [], restarting: false, url: "http://127.0.0.1:8793/", config: { port: 8793, tokenRequired: false, bindAll: false, tickHz: 4, posHz: 60, hotkeyBoost: "R", camUp: "Space", camDown: "C" } } },
    { method: "GET", path: "/twitch/users", summary: "Resolve Twitch logins", description: "Uses the game's own Twitch token (Helix). Unknown logins are simply omitted.", params: [param("logins", "csv", "up to 100 login names", { in: "query", example: "mrbeast6000,snoopdogg" })], response: { users: [{ id: "38746172", login: "mrbeast6000", displayName: "MrBeast6000", image: "https://static-cdn.jtvnw.net/…png", description: "Go watch Beast Games now on Prime Video!!!" }] } },
    { method: "GET", path: "/chat", summary: "Can the mod talk in Twitch chat?", description: "The mod sends through the game's own chat connection (logged in as you with the game's token). `connected` = that connection is up; `canSend` = the game token has the `chat:edit` scope (looked up once an hour at id.twitch.tv; null until known). Without `chat:edit` Twitch drops sent lines silently: then relay the `color` / `denied` SSE events from your own bot instead. `replies` mirrors `settings.chatReplies`.", params: [], response: { connected: true, channel: "shibikox", login: "shibikox", replies: true, canSend: true, scopes: ["chat:read", "chat:edit"], scopesError: null, note: null } },
    { method: "POST", path: "/chat/say", summary: "Say something in Twitch chat", description: "For bots and for testing the chat reply path. Goes out as the streamer through the game's connection (see GET /chat). Also emitted as the `chat` SSE event.", params: [param("text", "string", "the message (or a `{text}` JSON body); cut at 480 chars", { in: "query", example: "hello from the API" })], response: { ok: true, affected: 1, channel: "shibikox", text: "hello from the API" }, errors: "400 no text · 409 chat not connected" },
    { method: "PUT", path: "/image/:login", summary: "Upload a picture for a login", description: "Saved under BepInEx/config/shibiko.streamracer.images and returned as a path you can put in a join / custom bot / auto-join entry. The Bots page uses this for its file picker.", params: [param("login", "string", "login name", { in: "path", example: "chanbot" }), param("body", "object", "{data: 'data:image/png;base64,…'}", { in: "body" })], body: { data: "data:image/png;base64,iVBORw0KGgo…" }, response: { ok: true, path: "C:/…/BepInEx/config/shibiko.streamracer.images/chanbot.png", url: "/image/chanbot" } },
    { method: "GET", path: "/image/:login", summary: "A racer's custom join image", description: "Served by the plugin so local file paths work in browser overlays. `avatar` in snapshots points here when set.", params: [param("login", "string", "login name", { in: "path", example: "mybot" })], response: "image bytes" },
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
  ["tier", "{login, follower, subscriber, source, inRace}: a bot pushed a viewer's tier (PUT /tier/:login)"], ["perk", "{login, displayName, extraBoosts, reasons:[follower|sub|dev|host], boosts, followerChecks, follower} — the extra-boost decision for a joined car (Settings → Perks); fired even for 0 so the reason is visible; followerChecks = ok · no token · unknown"],
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

const CLIENT_SAMPLE = `const BASE = "http://127.0.0.1:8793";
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

const TABS = [["routes", "Routes"], ["events", "Events (SSE)"], ["client", "Client sample"]];

export default function ApiPage() {
  useCss("pages/api.css");
  const [filter, setFilter] = useState("");
  const [tab, setTab] = useState("routes");
  const matchesFilter = (route) => !filter || (route.path + " " + route.summary + " " + (route.description || "")).toLowerCase().includes(filter.toLowerCase());
  return html`
    <div class="api">
      <article class="api-head">
        <header>API <span class="base">${location.origin}</span></header>
        <div class="api-nav">
          <div class="tabs2">
            ${TABS.map(([key, label]) => html`<button key=${key} class=${tab === key ? "active" : ""} onClick=${() => setTab(key)}>${label}</button>`)}
          </div>
          ${tab === "routes" ? html`<input type="search" placeholder="filter routes" value=${filter} onInput=${(event) => setFilter(event.target.value)} />` : null}
        </div>
        <p class="desc">Everything is plain HTTP + Server-Sent Events, CORS open. <code>:x</code> = Twitch user id (digits) or login name. Errors come back as <code>{"error": "…"}</code>:
          401 token required · 404 no such car · 409 not applicable (no race, finished, lobby closed) · 501 a game update renamed something.
          ${getToken() ? html` Token from Settings is attached to every call here.` : null}</p>
      </article>

      ${tab === "routes" ? GROUPS.map(([name, routes]) => {
        const matching = routes.filter(matchesFilter); if (!matching.length) return null;
        return html`<section class="group" key=${name}><h3>${name}</h3>${matching.map((route) => html`<${RouteCard} key=${route.method + route.path} route=${route} />`)}</section>`;
      }) : null}

      ${tab === "events" ? html`
        <article>
          <header>GET /events</header>
          <p class="desc">One long-lived request; the browser's <code>EventSource</code> handles it (Node 22+ too). Each line below is an event name; the payload is JSON.</p>
          <table class="params"><tbody>${EVENTS.map(([name, description]) => html`<tr key=${name}><td><code>${name}</code></td><td>${description}</td></tr>`)}</tbody></table>
          <h4>Live</h4>
          <${EventLog} events=${EVENTS} />
        </article>` : null}

      ${tab === "client" ? html`
        <article>
          <header>Client sample</header>
          <${Code} code=${CLIENT_SAMPLE} lang="javascript" label="javascript" />
          <h4>Browser sources</h4>
          <p class="desc">Transparent pages served by the plugin for OBS, one source each; they reconnect on their own, follow Settings → Overlay look / Mini map live, and draw nothing outside a race (the bar and the leaderboard fade out after <code>race_end</code>; <code>settings.overlay.showInLobby</code> shows the field in the lobby too).</p>
          <table class="params sources"><thead><tr><th>page</th><th>what</th><th>query params (override the saved look for that source)</th></tr></thead><tbody>
            ${SOURCES.map(([path, what, params]) => html`<tr key=${path}><td><code>${path}</code></td><td>${what}</td><td><code>${params}</code></td></tr>`)}
          </tbody></table>
        </article>` : null}
    </div>`;
}
