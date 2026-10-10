// Readable names for the game's Beebyte-obfuscated types. This file and GameNames.cs are the only two that spell an
// obfuscated identifier; a game update that renames one is fixed here. Everything else uses the alias.
global using Vehicle = JHNEFBPNGOO;                  // one racer: car object, profile, route progress, finished flag, boost pool
global using RacerProfile = FAGGHLGNHAN;             // who drives it: twitch id, login, display name, color, sub flag, backend title
global using RacerStats = OLAAKJFKGPO;               // the stat block a profile carries (ten floats; the game fills it, we only allocate it)
global using VehicleType = DJEJHAPCLKB;              // car model enum (REGULAR, COUPE, JEEP, PICKUP, RACECAR, ...)
global using TwitchUser = MMHNAAICOII;               // the logged-in streamer: id, login, access token
global using BackendSession = OEPEEEPALFH;           // the game's login/backend session (singleton)
global using MapApi = EPHALAALFKL;                   // backend client for the map list (singleton)
global using MapQueueGame = AGIGHEAMBNM;             // the game's static map playlist (what the Play tab and "next map" use)
global using RacerTitles = PFCGGEJPGAO;              // static helper: the title a lobby row prints under a name
global using AvatarLoader = LFBLNOAILMD;             // loads Twitch profile pictures into the lobby/results RawImages
global using GameSettings = EPCGJLOBOLE;             // GameController's current game: map id/name and the Play-tab options
global using RoutePoint = UnityStandardAssets.Utility.WaypointCircuit.CDJHADMFGIA; // position + direction at a route distance
global using RaceResultCallback = EPHALAALFKL.JCAMLAGILFE; // MapApi's reply handler for a race result upload
global using FeedClick = Cage.StreamRacer.Events.EventManager.BICCFJOMCMA; // what clicking a line in the event feed does
global using MinimapView = Cage.StreamRacer.Minimap.MinimapView; // the game's own in-race mini map (Oct 2026 build)
global using MinimapCapture = AHANGIHJHGP;          // the map's top-down road picture (a RenderTexture) + its world projection

// Namespaces every file uses.
global using System.Collections.Generic;
global using System.Linq;
global using Cage.StreamRacer;
global using UnityEngine;
