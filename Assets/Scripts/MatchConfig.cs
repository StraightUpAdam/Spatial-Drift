using System.Collections.Generic;
using UnityEngine;

/// Chosen match settings.  Static so they survive a scene load (the menus set
/// them, then the race scene reads them).  Now map-data driven: instead of a
/// scene per map, the selection names a map (built-in or custom) that one race
/// scene loads via ResolveMap().
public static class MatchConfig
{
    public enum Difficulty { Easy, Medium, Hard, Ace }

    /// What kind of session the solo "Race" scene builds:
    ///   Race      — the classic grid race vs AI (laps, finish order).
    ///   TimeTrial — solo against the clock: 1 lap, instant restart, best-time tracking.
    ///   ZoneRace  — the whole race runs inside the drift zone as one train; places
    ///               only change when somebody ahead crashes (see ZoneTrain).
    public enum Mode { Race, TimeTrial, ZoneRace }
    public static Mode GameMode = Mode.Race;

    // ── Selected map ──────────────────────────────────────────────────────────
    public static string MapName    = "Speed Circuit";
    public static bool   MapBuiltIn = true;
    /// When non-empty, a serialized TrackData taking priority — used to carry a
    /// host's CUSTOM map to clients over the network (clients have no file).
    public static string MapJson    = "";

    /// Resolve the selection to a concrete TrackData (json → built-in → user file).
    public static TrackData ResolveMap()
    {
        if (!string.IsNullOrEmpty(MapJson))
        {
            try
            {
                var j = JsonUtility.FromJson<TrackData>(MapJson);
                if (j != null && j.nodes != null && j.nodes.Count >= 3) return j;
            }
            catch { /* fall through */ }
        }
        if (MapBuiltIn)
        {
            var b = MapCatalog.GetBuiltIn(MapName);
            if (b != null) return b;
        }
        return TrackData.Load(MapName) ?? MapCatalog.Speed();
    }

    /// Set the selection from a catalog entry (clears any stale custom json).
    public static void SelectMap(MapCatalog.Entry e)
    {
        MapName    = e.name;
        MapBuiltIn = e.builtIn;
        MapJson    = "";
    }

    /// One entry per AI car — each AI gets its OWN difficulty.  The list length
    /// is the number of AI on the grid (0..7).  The host edits this in the lobby.
    public static List<Difficulty> AIDifficulties =
        new List<Difficulty> { Difficulty.Medium, Difficulty.Medium };

    /// Number of AI cars to spawn (derived from the per-AI list).
    public static int AICount => AIDifficulties.Count;

    public static int Laps = 3;

    /// Multiplayer weather: index into Atmosphere.Presets chosen by the host (Random is
    /// resolved on the host before the race), synced by MatchManager.  -1 = leave
    /// Atmosphere.Current alone (solo modes set Current directly).
    public static int WeatherPreset = -1;

    public static float SkillFor(Difficulty d) => d switch
    {
        Difficulty.Easy   => 0.25f,
        Difficulty.Medium => 0.50f,
        Difficulty.Hard   => 0.75f,
        Difficulty.Ace    => 0.92f,
        _                 => 0.50f,
    };

    public static int GearCapFor(Difficulty d) => d switch
    {
        Difficulty.Easy   => 8,
        Difficulty.Medium => 9,
        _                 => 10,
    };
}
