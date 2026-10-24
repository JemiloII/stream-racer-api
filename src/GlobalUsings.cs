// Readable names for the game's Beebyte-obfuscated types. This file and GameNames.cs are the only two that spell an
// obfuscated identifier; a game update that renames one is fixed here. Everything else uses the alias.
global using Vehicle = DBGFFKNPKDO;                  // one racer: car object, profile, route progress, finished flag, boost pool
global using RacerProfile = MMLBLMPGOBO;             // who drives it: twitch id, login, display name, color, sub flag, backend title
global using RacerStats = GDPDMKEHHJB;               // the stat block a profile carries (ten floats; the game fills it, we only allocate it)
global using VehicleType = DFBFEIKJBHB;              // car model enum (REGULAR, COUPE, JEEP, PICKUP, RACECAR, ...)
global using TwitchUser = APNFPOGOGNN;               // the logged-in streamer: id, login, access token
global using BackendSession = IEGPGIEOCLH;           // the game's login/backend session (singleton)
global using MapApi = ALOAIBGFNHE;                   // backend client for the map list (singleton)
global using MapQueueGame = KNDNEMOFHKE;             // the game's static map playlist (what the Play tab and "next map" use)
global using RacerTitles = CDAGIBGJLJK;              // static helper: the title a lobby row prints under a name
global using AvatarLoader = MGPLIBIKGFB;             // loads Twitch profile pictures into the lobby/results RawImages
global using GameSettings = HMKMFHKHNOO;             // GameController's current game: map id/name and the Play-tab options
global using RoutePoint = BACPJIEAPJP.CLDMJPFPDCD; // position + direction at a route distance
global using RaceResultCallback = ALOAIBGFNHE.KONLIKOMPIH; // MapApi's reply handler for a race result upload
global using FeedClick = INEGIPLHFHK.NJDHLGHLLCF; // what clicking a line in the event feed does
global using MinimapView = FDCEOJOEIGK;             // the game's own in-race mini map (Oct 2026 build)
// Oct 21 2026 build: the readable game classes got obfuscated too, so they are aliased here under their old names.
global using GameController = JAHHIODBKDK;
global using VehicleManager = IKCBCAEOBIF;
global using WaypointController = MLOMIHPEEKI;
global using WaypointCircuit = BACPJIEAPJP;          // was UnityStandardAssets.Utility.WaypointCircuit
global using CarController = FPGJEOFKLKO;            // was UnityStandardAssets.Vehicles.Car.CarController
global using CarAIControl = NCNFOHNLFAH;             // was UnityStandardAssets.Vehicles.Car.CarAIControl
global using Car = CBNJKDIALFI;
global using FollowCam = JGKOAGHJKEC;
global using FreeCam = NJBNNDPDMGK;
global using CamController = FHEHNKBPHOL;
global using InGameScreenController = BCLJJNIKPPA;
global using TwitchCommandListener = DCIPKADMLEL;
global using VehicleLeaderboardUIItem = DOJKANGNPMG;
global using PreGamePlayerListItem = OBFKFOLHBDA;
global using PostGamePlayerListItem = EBCFGHAMNLB;
global using VehicleLabel = DAADNDJBGBB;
global using CarLabel = ANNHJHEPEFF;
global using EventManager = INEGIPLHFHK;             // the RIP feed
global using GameEvent = IEKPAJEOFFF;                // one line in that feed
global using ApplicationController = FIELODFEEIO;
global using ChaosEffectStatus = NACKBOILOPK;        // the game's chaos banner (SHRINK RAY, ...)
global using VirtualSafetyCarController = GKJBDAPFOBP;
global using OilSpillController = NJKJGAIKHGL;
global using MinimapCapture = JBCAOFBHAAL;          // the map's top-down road picture (a RenderTexture) + its world projection

// Namespaces every file uses.
global using System.Collections.Generic;
global using System.Linq;
global using Cage.StreamRacer;
global using UnityEngine;
