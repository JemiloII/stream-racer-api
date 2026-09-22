// Readable names for the game's Beebyte-obfuscated types. This file and GameNames.cs are the only two that spell an
// obfuscated identifier; a game update that renames one is fixed here. Everything else uses the alias.
global using Vehicle = CFBJLEBOFHJ;                  // one racer: car object, profile, route progress, finished flag, boost pool
global using RacerProfile = CKINOOFAKJL;             // who drives it: twitch id, login, display name, color, sub flag, backend title
global using RacerStats = GLBFHBCFHDF;               // the stat block a profile carries (ten floats; the game fills it, we only allocate it)
global using VehicleType = IBKJOPKFGMF;              // car model enum (REGULAR, COUPE, JEEP, PICKUP, RACECAR, ...)
global using TwitchUser = LJNDDFIILNC;               // the logged-in streamer: id, login, access token
global using BackendSession = HCPAEADBKIA;           // the game's login/backend session (singleton)
global using MapApi = KBCFIIFFLFL;                   // backend client for the map list (singleton)
global using MapQueueGame = LHAGKAJCCIB;             // the game's static map playlist (what the Play tab and "next map" use)
global using RacerTitles = KPDGEINGANI;              // static helper: the title a lobby row prints under a name
global using AvatarLoader = KHECLDCFJHH;             // loads Twitch profile pictures into the lobby/results RawImages
global using GameSettings = PLFMIGFDGMK;             // GameController's current game: map id/name and the Play-tab options
global using RoutePoint = UnityStandardAssets.Utility.WaypointCircuit.LFFLJFECJHK; // position + direction at a route distance

// Namespaces every file uses.
global using System.Collections.Generic;
global using System.Linq;
global using Cage.StreamRacer;
global using UnityEngine;
