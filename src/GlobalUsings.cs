// Readable names for the game's Beebyte-obfuscated types. This file and GameNames.cs are the only two that spell an
// obfuscated identifier; a game update that renames one is fixed here. Everything else uses the alias.
global using Vehicle = JBLBOKEBOFC;                  // one racer: car object, profile, route progress, finished flag, boost pool
global using RacerProfile = MNBMPDEOHHO;             // who drives it: twitch id, login, display name, color, sub flag, backend title
global using RacerStats = NAEIJPIPJFK;               // the stat block a profile carries (ten floats; the game fills it, we only allocate it)
global using VehicleType = KEFNDHKCANG;              // car model enum (REGULAR, COUPE, JEEP, PICKUP, RACECAR, ...)
global using TwitchUser = HEOBMEABCKC;               // the logged-in streamer: id, login, access token
global using BackendSession = GFFGAEGBFBK;           // the game's login/backend session (singleton)
global using MapApi = GLMMOFINBGE;                   // backend client for the map list (singleton)
global using MapQueueGame = OPCAJNNKCDM;             // the game's static map playlist (what the Play tab and "next map" use)
global using RacerTitles = KKPIKNPFKPM;              // static helper: the title a lobby row prints under a name
global using AvatarLoader = LCIDCOHAOCK;             // paints a lobby/results RawImage: driver portrait, else the Twitch picture
global using GameSettings = BKPAKGOLBIL;             // GameController's current game: map id/name and the Play-tab options
global using RoutePoint = DOBHNCNHAOD.EIEBPBHDOLB; // position + direction at a route distance
global using RaceResultCallback = GLMMOFINBGE.DEDLGMNHGKJ; // MapApi's reply handler for a race result upload
global using FeedClick = PHPNEIOKAFG.CEFDDOHCLAG; // what clicking a line in the event feed does
global using MinimapView = NFAPCNIEGLC;             // the game's own in-race mini map (Oct 2026 build)
// Oct 21 2026 build: the readable game classes got obfuscated too, so they are aliased here under their old names.
global using GameController = JAJHLABANCD;
global using VehicleManager = MFDPBEOJLOF;
global using WaypointController = KCBPCBPEMLP;
global using WaypointCircuit = DOBHNCNHAOD;          // was UnityStandardAssets.Utility.WaypointCircuit
global using CarController = DAHPNKIPBPB;            // was UnityStandardAssets.Vehicles.Car.CarController
global using CarAIControl = AIKCAMBADEI;             // was UnityStandardAssets.Vehicles.Car.CarAIControl
global using Car = DCECCHEAMAO;
global using FollowCam = KFGPCOFJJBA;
global using FreeCam = GKHLKAKPJPI;
global using CamController = INFLIIFNONP;
global using InGameScreenController = GFFOKBKBFCG;
global using TwitchCommandListener = LCEMDPNNCAF;
global using VehicleLeaderboardUIItem = MFDGPHINLAH;
global using PreGamePlayerListItem = PIAECKCMDOG;
global using PostGamePlayerListItem = ADHHMJPGKHH;
global using VehicleLabel = MLFFHMHNFCB;
global using CarLabel = IHGLPLCBHCJ;
global using EventManager = PHPNEIOKAFG;             // the RIP feed
global using GameEvent = HBHIJDLAMCA;                // one line in that feed
global using ApplicationController = KHBBPHGCECI;
global using ChaosEffectStatus = BCAJBHNFGPP;        // the game's chaos banner (SHRINK RAY, ...)
global using VirtualSafetyCarController = IHIAOHCHHGC;
global using OilSpillController = HPIBDMGLKDB;
global using OilSpill = NINEDFKCFHK;                 // one spill patch on the road (child of OilSpillController, ~25 s)
global using DriverPortraits = PODINHDAOOK;         // game 2.7: driver portrait store (one per racer, from the join reply)
global using MinimapCapture = DGIEGMKMLKL;          // the map's top-down road picture (a RenderTexture) + its world projection

// Namespaces every file uses.
global using System.Collections.Generic;
global using System.Linq;
global using Cage.StreamRacer;
global using UnityEngine;
