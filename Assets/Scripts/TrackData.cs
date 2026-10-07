using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// Serialisable description of a player-made track: a ring of control nodes,
/// each with a world position and a bank angle.  This is the save format for
/// the map editor (JSON in persistentDataPath/Maps) AND the bridge to the
/// game's Unity Spline — ApplyToSpline() pushes these nodes into a real
/// SplineContainer that the existing TrackMeshBuilder + CarController drive.
[Serializable]
public class NodeData
{
    public float x, y, z;       // world position (y = elevation)
    public float roll;          // bank degrees about the track tangent
    public float width = 14f;   // drivable width at this node (tapers between nodes)
    public int   scenery = 0;   // TrackScenery style index (0 = none); blends to the
                                // next node's style across the segment
    public bool  onGround = true;   // grass skirt + scenery walls hug this section;
                                    // false = an AIR section (elevated / flying) —
                                    // no ground follows the track up

    public NodeData() { }
    public NodeData(float x, float y, float z, float roll, float width = 14f)
    { this.x = x; this.y = y; this.z = z; this.roll = roll; this.width = width; }

    public Vector3 Pos
    {
        get => new Vector3(x, y, z);
        set { x = value.x; y = value.y; z = value.z; }
    }
}

[Serializable]
public class TrackData
{
    public string name   = "New Track";
    public float  width  = 14f;
    public bool   closed = true;
    public List<NodeData> nodes = new List<NodeData>();

    /// The live "current document" slot the editor and test-drive scene share.
    public const string ScratchName = "__scratch__";

    public static string MapsDir        => Path.Combine(Application.persistentDataPath, "Maps");
    public static string PathFor(string mapName) => Path.Combine(MapsDir, mapName + ".json");

    public void Save(string mapName)
    {
        // The editor's scratch slot is a storage location, not a name — keep the
        // document's real name when autosaving there.
        if (mapName != ScratchName) name = mapName;
        Directory.CreateDirectory(MapsDir);
        File.WriteAllText(PathFor(mapName), JsonUtility.ToJson(this, true));
    }

    public static TrackData Load(string mapName)
    {
        string p = PathFor(mapName);
        if (!File.Exists(p)) return null;
        try { return JsonUtility.FromJson<TrackData>(File.ReadAllText(p)); }
        catch { return null; }
    }

    public static bool Exists(string mapName) => File.Exists(PathFor(mapName));

    /// All saved map names (excluding the editor's scratch slot), sorted.
    public static List<string> ListMaps()
    {
        var result = new List<string>();
        if (!Directory.Exists(MapsDir)) return result;
        foreach (var f in Directory.GetFiles(MapsDir, "*.json"))
        {
            string n = Path.GetFileNameWithoutExtension(f);
            if (n != ScratchName) result.Add(n);
        }
        result.Sort();
        return result;
    }

    /// A default oval to start editing from — a closed loop of 8 flat nodes.
    public static TrackData Template()
    {
        var d = new TrackData { name = "New Track", width = 14f, closed = true };
        const float cx = 0f, cz = 120f, y = 10f, rx = 90f, rz = 130f;
        const int n = 8;
        for (int i = 0; i < n; i++)
        {
            float a = (float)i / n * Mathf.PI * 2f;
            d.nodes.Add(new NodeData(cx + Mathf.Sin(a) * rx, y, cz + Mathf.Cos(a) * rz, 0f));
        }
        return d;
    }

    /// Push the whole document onto a live Track: per-node widths, a uniform
    /// fallback width, and the spline geometry.  The map editor and the
    /// test-drive scene both go through here so widths always stay in sync.
    public void ApplyTo(Track track)
    {
        if (track == null) return;
        track.nodeWidths = NodeWidths();
        track.width      = nodes.Count > 0 ? nodes[0].width : width;
        if (track.Container != null) ApplyToSpline(track.Container.Spline);
    }

    float[] NodeWidths()
    {
        var arr = new float[nodes.Count];
        for (int i = 0; i < nodes.Count; i++) arr[i] = nodes[i].width;
        return arr;
    }

    /// Push these nodes into a spline (clears + rebuilds).  AutoSmooth gives the
    /// flowing curves; the per-node roll becomes the knot's banking rotation —
    /// the exact same construction the hand-authored tracks use.
    ///
    /// Determinism matters here: AutoSmooth tangents are computed from the
    /// neighbours present AT THE TIME each knot changes, so the result used to
    /// depend on add order and on whether the spline was already closed (the
    /// editor re-applies into a still-closed spline; a fresh scene adds into an
    /// open one and closes at the end).  That's why a map could show one bank in
    /// the editor and a different tilt in the test drive.  Fix: set Closed FIRST,
    /// then re-assert every knot once the whole ring exists so every tangent is
    /// recomputed against its final neighbours — identical output on every path.
    public void ApplyToSpline(Spline spline)
    {
        if (spline == null) return;
        spline.Clear();
        spline.Closed = closed && nodes.Count >= 3;
        foreach (var nd in nodes)
        {
            spline.Add(new BezierKnot
            {
                Position = new float3(nd.x, nd.y, nd.z),
                Rotation = quaternion.AxisAngle(new float3(0, 0, 1), math.radians(nd.roll)),
            }, TangentMode.AutoSmooth);
        }
        for (int i = 0; i < spline.Count; i++) spline[i] = spline[i];
    }

    /// Deep copy — used for the map editor's undo history.
    public TrackData Clone()
    {
        var d = new TrackData { name = name, width = width, closed = closed };
        d.nodes = new List<NodeData>(nodes.Count);
        foreach (var n in nodes)
            d.nodes.Add(new NodeData(n.x, n.y, n.z, n.roll, n.width)
                        { scenery = n.scenery, onGround = n.onGround });
        return d;
    }

    public Vector3 Center()
    {
        if (nodes.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var n in nodes) sum += n.Pos;
        return sum / nodes.Count;
    }
}
