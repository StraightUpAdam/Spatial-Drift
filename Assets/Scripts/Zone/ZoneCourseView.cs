using System.Collections.Generic;
using UnityEngine;

/// Renders the Zone Race course around the local car: the rail guide lines
/// along the whole track (your rail glows), the magenta blockers (adjacent
/// blocked rails merge into one wide wall) and the spinning gold bonus gates.
/// Only rows near the focus car exist as GameObjects.
[DisallowMultipleComponent]
public class ZoneCourseView : MonoBehaviour
{
    public float viewAhead  = 320f;
    public float viewBehind = 40f;

    ZoneCourse course;
    Track track;
    ZonePilot focus;
    SpatialDriftTuning tuning;

    sealed class RowView { public GameObject root; public Transform bonus; }
    readonly Dictionary<int, RowView> live = new Dictionary<int, RowView>();
    readonly HashSet<int> collected = new HashSet<int>();
    readonly HashSet<int> keep = new HashSet<int>();   // per-frame scratch
    readonly List<int>    drop = new List<int>();

    readonly List<MeshRenderer> railLines     = new List<MeshRenderer>();
    readonly List<MeshRenderer> railLinesXray = new List<MeshRenderer>();
    readonly List<float>        railOffsets   = new List<float>();
    GameObject railRoot, rowRoot;

    static Material _blockMat, _bonusMat;
    static readonly Color RailDim    = new Color(0.55f, 0.32f, 1.00f, 0.30f);
    static readonly Color RailBright = new Color(0.95f, 0.70f, 1.00f, 0.90f);

    public void Init(Track t, ZoneCourse c, SpatialDriftTuning tune)
    {
        track = t; course = c; tuning = tune;
        rowRoot  = new GameObject("ZoneRows");
        railRoot = new GameObject("ZoneRails");
        BuildRailLines();
    }

    public void SetFocus(ZonePilot p) => focus = p;

    /// The local car took this row's bonus — hide it (for us only).
    public void Collect(int rowIndex)
    {
        collected.Add(rowIndex);
        if (live.TryGetValue(rowIndex, out var v) && v.bonus != null) v.bonus.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        foreach (var mr in railLines)
        {
            if (mr == null) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            if (mr.sharedMaterial != null) Destroy(mr.sharedMaterial);
        }
        foreach (var mr in railLinesXray) if (mr != null && mr.sharedMaterial != null) Destroy(mr.sharedMaterial);
        if (railRoot != null) Destroy(railRoot);
        if (rowRoot != null) Destroy(rowRoot);
    }

    void LateUpdate()
    {
        if (course == null || track == null) return;
        float center = focus != null ? focus.RaceDist : 0f;
        float lo = center - viewBehind, hi = center + viewAhead;

        // Spawn rows entering the window, drop rows leaving it.
        keep.Clear();
        foreach (var row in course.rows)
        {
            if (row.raceDist < lo) continue;
            if (row.raceDist > hi) break;
            keep.Add(row.index);
            if (!live.ContainsKey(row.index)) live[row.index] = Build(row);
        }
        drop.Clear();
        foreach (var kv in live) if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
        foreach (int k in drop) { if (live[k].root != null) Destroy(live[k].root); live.Remove(k); }

        // Bonus gates spin + bob.
        float t = Time.time;
        foreach (var kv in live)
        {
            var b = kv.Value.bonus;
            if (b == null || !b.gameObject.activeSelf) continue;
            b.localRotation = Quaternion.Euler(0f, t * 220f, 45f);
            b.localPosition = new Vector3(0f, 1.6f + Mathf.Sin(t * 5f + kv.Key) * 0.25f, 0f);
        }

        UpdateRailHighlight();
    }

    RowView Build(ZoneRow row)
    {
        var v = new RowView { root = new GameObject($"ZoneRow_{row.index}") };
        v.root.transform.SetParent(rowRoot.transform, false);
        float s = course.Spacing;
        float d = track.WrapDistance(row.raceDist);

        // Merge adjacent blocked rails into single walls.
        for (int i = 0; i < row.Rails; )
        {
            if (!row.Blocked(i)) { i++; continue; }
            int end = i;
            while (end + 1 < row.Rails && row.Blocked(end + 1)) end++;
            float lat   = (course.RailLateral(i, row.pairs) + course.RailLateral(end, row.pairs)) * 0.5f;
            float width = (end - i) * s + s * 0.72f;
            track.Sample(d, lat, out Vector3 pos, out Quaternion rot, out _, out Vector3 up);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Blocker";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(v.root.transform, false);
            go.transform.SetPositionAndRotation(pos + up * 1.2f, rot);
            go.transform.localScale = new Vector3(width, 2.4f, 0.7f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BlockMat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            i = end + 1;
        }

        if (row.bonus >= 0 && !collected.Contains(row.index))
        {
            track.Sample(d, course.RailLateral(row.bonus, row.pairs), out Vector3 pos, out Quaternion rot, out _, out _);
            var anchor = new GameObject("BonusAnchor").transform;
            anchor.SetParent(v.root.transform, false);
            anchor.SetPositionAndRotation(pos, rot);
            var gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gem.name = "BonusGate";
            Destroy(gem.GetComponent<Collider>());
            gem.transform.SetParent(anchor, false);
            gem.transform.localScale = new Vector3(1.3f, 1.3f, 1.3f);
            var mr = gem.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BonusMat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            v.bonus = gem.transform;
        }
        return v;
    }

    static Material BlockMat()
    {
        if (_blockMat != null) return _blockMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _blockMat = new Material(sh) { name = "ZoneBlocker" };
        var col = new Color(1f, 0.20f, 0.55f);
        _blockMat.color = col;
        if (_blockMat.HasProperty("_BaseColor")) _blockMat.SetColor("_BaseColor", col);
        return _blockMat;
    }

    static Material BonusMat()
    {
        if (_bonusMat != null) return _bonusMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _bonusMat = new Material(sh) { name = "ZoneBonus" };
        var col = new Color(2.4f, 1.7f, 0.45f);      // HDR gold — blooms
        _bonusMat.color = col;
        if (_bonusMat.HasProperty("_BaseColor")) _bonusMat.SetColor("_BaseColor", col);
        return _bonusMat;
    }

    // ── Rail guides (whole track; your rail glows) ────────────────────────────
    void BuildRailLines()
    {
        float len = track.Length;
        if (len <= 20f) return;
        int steps    = Mathf.Clamp(Mathf.CeilToInt(len / 4f), 16, 2000);
        int maxPairs = course.MaxPairs;
        float s      = course.Spacing;

        var shader = Shader.Find("SpatialDrift/RailLine")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Sprites/Default");
        if (shader == null) return;
        bool hasZTest = shader.name == "SpatialDrift/RailLine";

        for (int li = -maxPairs; li < maxPairs; li++)
        {
            float off = li + 0.5f;
            var verts  = new List<Vector3>((steps + 1) * 2);
            var tris   = new List<int>(steps * 12);
            var exists = new bool[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float d = (i % steps) * (len / steps);
                exists[i] = course.PairsAt(d) - 0.5f >= Mathf.Abs(off) - 0.01f;
                track.Sample(d, off * s, out Vector3 pos, out _, out Vector3 fwd, out Vector3 up);
                Vector3 right = Vector3.Cross(up, fwd).normalized;
                verts.Add(pos + up * 0.15f - right * 0.15f);
                verts.Add(pos + up * 0.15f + right * 0.15f);
            }
            for (int i = 1; i < steps; i++)
                if (!exists[i] && exists[i - 1] && exists[i + 1]) exists[i] = true;
            for (int i = 0; i < steps; i++)
            {
                if (!exists[i] || !exists[i + 1]) continue;
                int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                tris.Add(b0); tris.Add(t0); tris.Add(t1);
                tris.Add(b0); tris.Add(t1); tris.Add(b1);
                tris.Add(b0); tris.Add(t1); tris.Add(t0);
                tris.Add(b0); tris.Add(b1); tris.Add(t1);
            }
            if (tris.Count == 0) continue;

            var go = new GameObject($"ZoneRail_{off:+0.0;-0.0}");
            go.transform.SetParent(railRoot.transform, false);
            var mesh = new Mesh { name = go.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mat = new Material(shader) { name = go.name };
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 20;
            SetColor(mat, RailDim);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            railLines.Add(mr);
            railOffsets.Add(off);

            MeshRenderer xr = null;
            if (hasZTest)
            {
                var xgo = new GameObject(go.name + "_xray");
                xgo.transform.SetParent(railRoot.transform, false);
                xgo.AddComponent<MeshFilter>().sharedMesh = mesh;
                var xmat = new Material(shader) { name = xgo.name };
                xmat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Greater);
                xmat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 19;
                SetColor(xmat, Fade(RailDim, 0.32f));
                xr = xgo.AddComponent<MeshRenderer>();
                xr.sharedMaterial = xmat;
                xr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                xr.receiveShadows = false;
            }
            railLinesXray.Add(xr);
        }
    }

    void UpdateRailHighlight()
    {
        if (focus == null) return;
        int best = -1; float bestGap = float.PositiveInfinity;
        for (int i = 0; i < railLines.Count; i++)
        {
            float gap = Mathf.Abs(railOffsets[i] - focus.RailOff);
            if (gap < bestGap) { bestGap = gap; best = i; }
        }
        for (int i = 0; i < railLines.Count; i++)
        {
            var mat = railLines[i] != null ? railLines[i].sharedMaterial : null;
            if (mat == null) continue;
            var target = i == best ? RailBright : RailDim;
            if (mat.color == target) continue;
            SetColor(mat, target);
            if (railLinesXray[i] != null && railLinesXray[i].sharedMaterial != null)
                SetColor(railLinesXray[i].sharedMaterial, Fade(target, 0.32f));
        }
    }

    static Color Fade(Color c, float a) { c.a *= a; return c; }
    static void SetColor(Material m, Color c)
    {
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
    }
}
