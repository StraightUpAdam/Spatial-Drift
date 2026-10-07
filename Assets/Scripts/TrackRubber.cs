using System.Collections.Generic;
using UnityEngine;

/// Rubber laid down on the racing surface — like an F1 circuit "rubbering
/// in": every drift deposits a little, and where drift lines overlap lap after
/// lap the racing line burns darker and darker until it's plain to see.
///
/// Stored in TRACK SPACE, in 128 m chunks along the track, each a coverage map
/// (u = distance along, v = across the road) drawn by a thin overlay mesh that
/// hugs the road (shader SpatialDrift/Rubber).  Chunks only exist where rubber
/// has been laid.  Coverage only accumulates (it never fades) for the life of
/// the scene, so race restarts keep the rubbered-in line.  Fed by SkidMarks.
[DisallowMultipleComponent]
public class TrackRubber : MonoBehaviour
{
    const float ChunkLen  = 128f;     // metres per chunk
    const int   TexW      = 512;      // texels along a chunk  (4 / m)
    const int   TexH      = 96;       // texels across the road
    const float MeshStep  = 2f;       // overlay mesh sample spacing (m)
    const float LayHeight = 0.035f;   // above the road surface
    const float UploadGap = 0.08f;    // seconds between GPU uploads per chunk

    /// Deposit per metre of wheel travel at full strength (a road texel sees
    /// ~1.5 stamps per pass → ~0.19 coverage).  One drift pass reads as a
    /// faint scuff (~15%); ~6 overlapping passes go properly dark.
    public static float DepositPerPass = 0.7f;

    class Chunk
    {
        public int   index;
        public float d0, half;
        public float[] cov = new float[TexW * TexH];
        public byte[]  px  = new byte[TexW * TexH];
        public Texture2D tex;
        public GameObject go;
        public Material mat;
        public Mesh mesh;
        public bool  dirty;
        public float lastUpload;
    }

    static TrackRubber inst;
    Track track;
    readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
    static Shader _shader;

    // ── API ───────────────────────────────────────────────────────────────────
    /// Lay rubber along a wheel's path between two track-space points
    /// (distance, lateral).  `width` in metres, `strength` 0..1.
    public static void Lay(Track track, float d0, float l0, float d1, float l1, float width, float strength)
    {
        if (track == null || track.Length < ChunkLen * 0.25f) return;
        if (inst == null || inst.track != track)
        {
            if (inst != null) Destroy(inst.gameObject);
            inst = new GameObject("TrackRubber").AddComponent<TrackRubber>();
            inst.track = track;
        }
        inst.LaySegment(d0, l0, d1, l1, width, strength);
    }

    /// Wipe all rubber (e.g. the track was rebuilt).
    public static void ClearAll()
    {
        if (inst != null) Destroy(inst.gameObject);
        inst = null;
    }

    void OnDestroy()
    {
        foreach (var c in chunks.Values)
        {
            if (c.tex  != null) Destroy(c.tex);
            if (c.mat  != null) Destroy(c.mat);
            if (c.mesh != null) Destroy(c.mesh);
        }
        chunks.Clear();
        if (inst == this) inst = null;
    }

    // ── Stamping ──────────────────────────────────────────────────────────────
    void LaySegment(float d0, float l0, float d1, float l1, float width, float strength)
    {
        float len = track.Length;
        float dd  = WrapDelta(d1 - d0, len);
        float dl  = l1 - l0;
        float seg = Mathf.Sqrt(dd * dd + dl * dl);
        if (seg > 8f) { d0 = d1; l0 = l1; dd = 0f; dl = 0f; seg = 0f; }   // teleport / reset

        // One stamp per ~texel of travel; each carries its share of the deposit.
        int   steps = Mathf.Max(1, Mathf.CeilToInt(seg / 0.25f));
        float per   = DepositPerPass * strength * Mathf.Max(seg, 0.25f) / steps;
        for (int s = 1; s <= steps; s++)
        {
            float f = s / (float)steps;
            Stamp(track.WrapDistance(d0 + dd * f), l0 + dl * f, width, per);
        }
    }

    void Stamp(float d, float lat, float width, float amount)
    {
        int ci = Mathf.FloorToInt(d / ChunkLen);
        var c  = GetChunk(ci);
        if (c == null) return;

        float u  = (d - c.d0) / ChunkLen * TexW;
        float v  = (lat + c.half) / (2f * c.half) * TexH;
        float ru = Mathf.Max(0.7f, width * 0.5f / ChunkLen * TexW);           // along radius (texels)
        float rv = Mathf.Max(0.7f, width * 0.5f / (2f * c.half) * TexH);      // across radius

        int x0 = Mathf.FloorToInt(u - ru - 1), x1 = Mathf.CeilToInt(u + ru + 1);
        int y0 = Mathf.FloorToInt(v - rv - 1), y1 = Mathf.CeilToInt(v + rv + 1);
        for (int y = Mathf.Max(0, y0); y <= Mathf.Min(TexH - 1, y1); y++)
        {
            float ny = (y + 0.5f - v) / rv;
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(TexW - 1, x1); x++)
            {
                float nx = (x + 0.5f - u) / ru;
                float w  = Mathf.Exp(-(nx * nx + ny * ny) * 1.6f);
                if (w < 0.02f) continue;
                c.cov[y * TexW + x] += amount * w;
            }
        }
        c.dirty = true;
    }

    // ── Chunks ────────────────────────────────────────────────────────────────
    Chunk GetChunk(int ci)
    {
        int count = Mathf.CeilToInt(track.Length / ChunkLen);
        if (ci < 0 || ci >= count) return null;
        if (chunks.TryGetValue(ci, out var c)) return c;

        c = new Chunk { index = ci, d0 = ci * ChunkLen };
        float end = Mathf.Min(track.Length, c.d0 + ChunkLen);

        float half = 0f;
        for (float d = c.d0; d <= end; d += MeshStep) half = Mathf.Max(half, track.HalfWidthAt(Mathf.Min(d, end)));
        c.half = half + 0.25f;

        // Overlay mesh: two verts across per sample, riding just above the road.
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        int n = Mathf.Max(1, Mathf.CeilToInt((end - c.d0) / MeshStep));
        for (int i = 0; i <= n; i++)
        {
            float d = Mathf.Min(c.d0 + i * MeshStep, end);
            float hw = track.HalfWidthAt(d);
            for (int side = -1; side <= 1; side += 2)
            {
                track.Sample(d, side * hw, out Vector3 p, out _, out _, out Vector3 up);
                verts.Add(p + up * LayHeight);
                uvs.Add(new Vector2((d - c.d0) / ChunkLen, (side * hw + c.half) / (2f * c.half)));
            }
            if (i < n)
            {
                int b = i * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
        }
        c.mesh = new Mesh { name = $"Rubber_{ci}" };
        c.mesh.SetVertices(verts);
        c.mesh.SetUVs(0, uvs);
        c.mesh.SetTriangles(tris, 0);
        c.mesh.RecalculateBounds();

        c.tex = new Texture2D(TexW, TexH, TextureFormat.R8, false, true)
        { name = $"RubberCov_{ci}", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        if (_shader == null) _shader = Shader.Find("SpatialDrift/Rubber");
        if (_shader == null) return null;
        c.mat = new Material(_shader) { name = $"Rubber_{ci}" };
        c.mat.mainTexture = c.tex;

        c.go = new GameObject($"RubberChunk_{ci}");
        c.go.transform.SetParent(transform, false);
        c.go.AddComponent<MeshFilter>().sharedMesh = c.mesh;
        var mr = c.go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = c.mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        chunks[ci] = c;
        return c;
    }

    void LateUpdate()
    {
        float now = Time.time;
        foreach (var c in chunks.Values)
        {
            if (!c.dirty || now - c.lastUpload < UploadGap) continue;
            c.dirty = false;
            c.lastUpload = now;
            var cov = c.cov; var px = c.px;
            for (int i = 0; i < cov.Length; i++)
            {
                float k = cov[i];
                px[i] = k <= 0f ? (byte)0 : (byte)(255f * (1f - Mathf.Exp(-k)));
            }
            c.tex.LoadRawTextureData(px);
            c.tex.Apply(false);
        }
    }

    static float WrapDelta(float delta, float length)
    {
        if (length <= 0f) return delta;
        delta %= length;
        if (delta >  length * 0.5f) delta -= length;
        if (delta < -length * 0.5f) delta += length;
        return delta;
    }
}
