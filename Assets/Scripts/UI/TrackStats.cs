using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// Everything a menu wants to say about a map without building it: a sampled
/// outline (for previews) and headline stats (length, elevation, width).
/// Builds a throwaway Spline from the TrackData exactly like the race does,
/// so the numbers and the shape match the real thing.
public class TrackStats
{
    public string name;
    public float  length;          // metres
    public float  minY, maxY;      // elevation range of the driven line
    public float  minWidth, maxWidth;
    public int    nodes, airNodes;
    public readonly List<Vector3> outline = new List<Vector3>();   // world XYZ, evenly spaced

    public float Climb => maxY - minY;

    static readonly Dictionary<string, TrackStats> _cache = new Dictionary<string, TrackStats>();

    /// Stats for a catalog entry (cached per map — custom maps re-read when saved).
    public static TrackStats For(MapCatalog.Entry e)
    {
        string key = (e.builtIn ? "B:" : "U:") + e.name;
        if (!e.builtIn)
        {
            // Custom maps can change between visits — key by file time too.
            try
            {
                var path = TrackData.PathFor(e.name);
                if (System.IO.File.Exists(path)) key += System.IO.File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch { }
        }
        if (_cache.TryGetValue(key, out var s)) return s;

        TrackData data = e.builtIn ? MapCatalog.GetBuiltIn(e.name) : TrackData.Load(e.name);
        s = Compute(data);
        if (s != null) { s.name = e.display; _cache[key] = s; }
        return s;
    }

    public static TrackStats Compute(TrackData data, int samples = 240)
    {
        if (data == null || data.nodes == null || data.nodes.Count < 3) return null;
        var st = new TrackStats { name = data.name, nodes = data.nodes.Count };

        var spline = new Spline();
        data.ApplyToSpline(spline);
        st.length = SplineUtility.CalculateLength(spline, float4x4.identity);

        st.minY = float.MaxValue; st.maxY = float.MinValue;
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float3 p = SplineUtility.EvaluatePosition(spline, t);
            var v = new Vector3(p.x, p.y, p.z);
            st.outline.Add(v);
            st.minY = Mathf.Min(st.minY, v.y);
            st.maxY = Mathf.Max(st.maxY, v.y);
        }

        st.minWidth = float.MaxValue; st.maxWidth = 0f;
        foreach (var n in data.nodes)
        {
            st.minWidth = Mathf.Min(st.minWidth, n.width);
            st.maxWidth = Mathf.Max(st.maxWidth, n.width);
            if (!n.onGround) st.airNodes++;
        }
        return st;
    }

    public string LengthText => length >= 1000f ? $"{length / 1000f:0.00} KM" : $"{length:0} M";
    public string ClimbText  => $"{Climb:0} M";
    public string WidthText  => Mathf.Approximately(minWidth, maxWidth)
                                ? $"{minWidth:0} M" : $"{minWidth:0}–{maxWidth:0} M";
}
