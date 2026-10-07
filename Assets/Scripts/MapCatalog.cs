using System;
using System.Collections.Generic;

/// The runtime source of truth for the built-in maps (as TrackData), plus a
/// unified listing of every map the game can load — the hand-authored circuits
/// AND the player's own saved maps from the editor.  This is the seam that lets
/// ONE code path race any map instead of one scene per map.
public static class MapCatalog
{
    public struct Entry { public string name; public bool builtIn; public string display; }

    /// SPEED CIRCUIT — Adam's hand-built speed map (transcribed 2026-07-11 from
    /// his saved "Speed": corkscrew, big elevation jump, long banked sweeps).
    public static TrackData Speed()
    {
        var d = new TrackData { name = "Speed Circuit", width = 14f, closed = true };
        void N(float x, float y, float z, float roll) =>
            d.nodes.Add(new NodeData(x, y, z, roll, 14f));
        N(0,      20,    0,      0);
        N(0,      20,  240,      0);
        N(0,      20,  360,     90);
        N(0,      20,  480,   -180);
        N(0,      20,  660,   -180);
        N(0,      20,  780,     90);
        N(0,      20,  900,      0);
        N(0,      20, 1320,   -6.8f);
        N(180,   150, 1560,    180);
        N(480,    20, 1680,   -5.6f);
        N(780,    20, 1560,    5.5f);
        N(960,    20, 1320,   -0.1f);
        N(389.8f, 10,  563.9f, 80.8f);
        N(840,    20,  -69.1f, -91.7f);
        N(480,    20,   -4.2f,  28.8f);
        N(180,    20, -248.3f, -41.5f);
        return d;
    }

    /// DRIFT CIRCUIT — Adam's hand-built drift map (transcribed 2026-07-11 from
    /// his saved "Drift": wide 20 m road, rolls, flowing drift sweeps).
    public static TrackData Drift()
    {
        var d = new TrackData { name = "Drift Circuit", width = 20f, closed = true };
        void N(float x, float y, float z, float roll) =>
            d.nodes.Add(new NodeData(x, y, z, roll, 20f));
        N(0,      10,    0,      0);
        N(0,      10,  110,      0);
        N(0,      10,  175,    79.2f);
        N(-0.7f,  10,  260.3f,  180);
        N(0,      10,  335,    141.7f);
        N(0,      10,  440,     90);
        N(5,      10,  535,      0);
        N(0,      10,  660,      0);
        N(-78.5f, 10,  780,      0);
        N(240,    10,  840,      0);
        N(450.3f, 10,  740.8f,   0);
        N(414.6f, 10,  520.6f,   0);
        N(480,    10,  120,      0);
        N(420,    10,    0,     29.7f);
        N(240,    10,  -60,     -9.0f);
        N(90,     10,  -11,      8.4f);
        return d;
    }

    public static readonly (string name, Func<TrackData> make)[] BuiltIns =
    {
        ("Speed Circuit", Speed),
        ("Drift Circuit", Drift),
    };

    public static TrackData GetBuiltIn(string name)
    {
        foreach (var b in BuiltIns) if (b.name == name) return b.make();
        return null;
    }

    /// Every selectable map: built-ins, then the editor's current draft (if any),
    /// then the player's saved maps.
    public static List<Entry> All()
    {
        var list = new List<Entry>();
        foreach (var b in BuiltIns)
            list.Add(new Entry { name = b.name, display = b.name, builtIn = true });

        // The editor's scratch slot — your latest editor work, raceable without
        // an explicit Save (Save it under a name to keep a permanent copy).
        if (TrackData.Exists(TrackData.ScratchName))
            list.Add(new Entry { name = TrackData.ScratchName, display = "Editor draft (latest)", builtIn = false });

        foreach (var n in TrackData.ListMaps())
            list.Add(new Entry { name = n, display = n, builtIn = false });
        return list;
    }

    /// Resolve a catalog entry to its TrackData (built-in regenerates; user maps load).
    public static TrackData Resolve(Entry e) =>
        e.builtIn ? GetBuiltIn(e.name) : TrackData.Load(e.name);
}
