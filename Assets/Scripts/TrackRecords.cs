using System.IO;
using UnityEngine;

/// Every per-track record in one place (PlayerPrefs + the PB ghost file).
///
///   BEST LAP   fastest lap, Spatial Drift allowed
///   PURE LAP   fastest lap that never entered Spatial Drift
///   (each kept separately for a race "R" and a time trial "TT"; Zone Race
///   keeps its best finishing place)
///
/// Records belong to the track's exact SHAPE: a signature (hash of its data) is
/// stored next to them, and when the track no longer matches — it was edited in
/// the Map Editor — every record and the ghost for that map are wiped.
public static class TrackRecords
{
    public const string Race = "R", TimeTrial = "TT";

    static string LapKey(string mode, string map, bool pure) => $"BestLap_{mode}{(pure ? "_PURE" : "")}_{map}";
    static string SigKey(string map)  => $"TrackSig_{map}";
    static string ZoneKey(string map) => $"ZoneBest_{map}";

    // ── Signature ─────────────────────────────────────────────────────────────
    public static string Signature(TrackData d)
    {
        if (d == null) return "";
        string json = JsonUtility.ToJson(d);
        unchecked
        {
            ulong h = 14695981039346656037UL;                       // FNV-1a 64
            foreach (char c in json) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16");
        }
    }

    /// Call before reading or writing a map's records: if the track changed
    /// since they were set, they're cleared.  Returns true when it wiped them.
    public static bool Validate(string map, TrackData data)
    {
        if (string.IsNullOrEmpty(map) || data == null) return false;
        string sig    = Signature(data);
        string stored = PlayerPrefs.GetString(SigKey(map), "");
        if (stored == sig) return false;

        bool wiped = false;
        if (stored.Length > 0)          // a known older shape → its records are void
        {
            Clear(map);
            wiped = true;
        }
        PlayerPrefs.SetString(SigKey(map), sig);   // (no signature yet = adopt existing records)
        PlayerPrefs.Save();
        return wiped;
    }

    public static bool Validate(MapCatalog.Entry e) => Validate(e.name, MapCatalog.Resolve(e));

    /// Validate the map selected in MatchConfig (what a race is about to run).
    public static bool ValidateCurrent() => Validate(MatchConfig.MapName, MatchConfig.ResolveMap());

    public static void Clear(string map)
    {
        foreach (var mode in new[] { Race, TimeTrial })
        {
            PlayerPrefs.DeleteKey(LapKey(mode, map, false));
            PlayerPrefs.DeleteKey(LapKey(mode, map, true));
        }
        PlayerPrefs.DeleteKey(ZoneKey(map));
        TimeTrialGhost.DeleteFor(map);
        PlayerPrefs.Save();
    }

    // ── Laps ──────────────────────────────────────────────────────────────────
    public static float BestLap(string mode, string map, bool pure) =>
        PlayerPrefs.GetFloat(LapKey(mode, map, pure), float.PositiveInfinity);

    /// Offer a finished lap.  Returns (newBest, newPure).
    public static (bool best, bool pure) SubmitLap(string mode, string map, float time, bool usedDrift)
    {
        if (time <= 0f || string.IsNullOrEmpty(map)) return (false, false);
        bool best = time < BestLap(mode, map, false);
        bool pure = !usedDrift && time < BestLap(mode, map, true);
        if (best) PlayerPrefs.SetFloat(LapKey(mode, map, false), time);
        if (pure) PlayerPrefs.SetFloat(LapKey(mode, map, true),  time);
        if (best || pure) PlayerPrefs.Save();
        return (best, pure);
    }

    // ── Zone Race ─────────────────────────────────────────────────────────────
    public static int BestFinish(string map) => PlayerPrefs.GetInt(ZoneKey(map), 0);

    public static bool SubmitFinish(string map, int place)
    {
        int best = BestFinish(map);
        if (place <= 0 || (best > 0 && place >= best)) return false;
        PlayerPrefs.SetInt(ZoneKey(map), place);
        PlayerPrefs.Save();
        return true;
    }

    public static string Format(float t) =>
        t < float.PositiveInfinity ? RaceManager.FormatTime(t) : "—";
}
