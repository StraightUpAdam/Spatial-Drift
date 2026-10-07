using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Draws a track outline straight into the UI mesh — crisp at any resolution,
/// anti-aliased with feathered edges, coloured by elevation (low cyan → violet
/// → high gold) over a soft glow pass, with a start/finish tick.  A baked
/// "runner" dot laps the circuit so the preview feels alive, and switching
/// maps draws the new line on with a quick sweep.
[RequireComponent(typeof(CanvasRenderer))]
public class MapPreviewGraphic : MaskableGraphic
{
    [Header("Look")]
    public float lineWidth = 6f;
    public float glowWidth = 22f;
    public float padding   = 34f;
    [Tooltip("Seconds per preview lap of the runner dot.")]
    public float lapSeconds = 7f;

    [Header("Baked")]
    public RectTransform runner;

    readonly List<Vector2> pts   = new List<Vector2>();   // local (rect) coords
    readonly List<float>   hts   = new List<float>();     // 0..1 elevation
    readonly List<float>   cum   = new List<float>();     // cumulative length
    float total;
    float reveal = 1f;
    TrackStats stats;

    public void SetTrack(TrackStats s)
    {
        stats  = s;
        reveal = 0f;
        Rebuild();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        if (stats != null) Rebuild();
    }

    void Rebuild()
    {
        pts.Clear(); hts.Clear(); cum.Clear(); total = 0f;
        if (stats == null || stats.outline.Count < 3) { SetVerticesDirty(); return; }

        // Fit the XZ outline into the rect, preserving aspect.
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
        foreach (var p in stats.outline)
        {
            min = Vector2.Min(min, new Vector2(p.x, p.z));
            max = Vector2.Max(max, new Vector2(p.x, p.z));
        }
        var r = rectTransform.rect;
        float availW = Mathf.Max(10f, r.width - padding * 2f);
        float availH = Mathf.Max(10f, r.height - padding * 2f);
        Vector2 size = max - min;
        float scale = Mathf.Min(availW / Mathf.Max(1f, size.x), availH / Mathf.Max(1f, size.y));
        Vector2 mid = (min + max) * 0.5f;
        float span = Mathf.Max(1f, stats.Climb);

        foreach (var p in stats.outline)
        {
            var q = (new Vector2(p.x, p.z) - mid) * scale + r.center;
            if (pts.Count > 0) total += Vector2.Distance(pts[pts.Count - 1], q);
            cum.Add(total);
            pts.Add(q);
            hts.Add(stats.Climb < 1f ? 0.35f : (p.y - stats.minY) / span);
        }
        total += Vector2.Distance(pts[pts.Count - 1], pts[0]);   // closing segment
        SetVerticesDirty();
    }

    void Update()
    {
        if (reveal < 1f)
        {
            reveal = Mathf.Min(1f, reveal + Time.unscaledDeltaTime / 0.55f);
            SetVerticesDirty();
        }
        if (runner != null)
        {
            bool show = pts.Count > 2 && reveal >= 1f;
            runner.gameObject.SetActive(show);
            if (show)
            {
                // The runner is a child, so rect-local points are its localPosition.
                float d = (Time.unscaledTime / Mathf.Max(0.5f, lapSeconds) % 1f) * total;
                runner.localPosition = PointAtLocal(d);
            }
        }
    }

    Vector2 PointAtLocal(float d)
    {
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            float a = cum[i];
            float b = i + 1 < n ? cum[i + 1] : total;
            if (d <= b)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % n];
                return Vector2.Lerp(p0, p1, b > a ? (d - a) / (b - a) : 0f);
            }
        }
        return pts[0];
    }

    // ── Mesh ─────────────────────────────────────────────────────────────────
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int n = pts.Count;
        if (n < 3) return;

        int drawn = Mathf.Clamp(Mathf.CeilToInt(n * UITheme.EaseOutCubic(reveal)), 2, n);
        bool closed = drawn >= n;

        Strip(vh, drawn, closed, glowWidth, 0f, 0.16f, glowPass: true);
        Strip(vh, drawn, closed, lineWidth, 1.4f, 1f,   glowPass: false);

        // Start / finish tick across the line.
        Vector2 dir = (pts[1] - pts[0]).normalized;
        Vector2 nrm = new Vector2(-dir.y, dir.x);
        Quad(vh, pts[0] - nrm * 13f - dir * 2.5f, pts[0] + nrm * 13f - dir * 2.5f,
                 pts[0] + nrm * 13f + dir * 2.5f, pts[0] - nrm * 13f + dir * 2.5f, Color.white);
    }

    void Strip(VertexHelper vh, int count, bool closed, float width, float feather, float alpha, bool glowPass)
    {
        int n = pts.Count;
        int start = vh.currentVertCount;
        float half = width * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector2 prev = pts[(i - 1 + n) % n];
            Vector2 next = pts[(i + 1) % n];
            if (!closed && i == 0)          prev = pts[i] - (next - pts[i]);
            if (!closed && i == count - 1)  next = pts[i] + (pts[i] - prev);
            Vector2 t = (next - prev).normalized;
            Vector2 nrm = new Vector2(-t.y, t.x);

            Color c = UITheme.HeightTint(hts[i]);
            Color core = c; core.a = alpha;
            Color edge = c; edge.a = 0f;

            if (glowPass)
            {
                // Centre bright → edges transparent.
                vh.AddVert(pts[i] - nrm * half, edge, Vector4.zero);
                vh.AddVert(pts[i],              core, Vector4.zero);
                vh.AddVert(pts[i] + nrm * half, edge, Vector4.zero);
            }
            else
            {
                // Solid core with feathered rims (4 verts across).
                vh.AddVert(pts[i] - nrm * (half + feather), edge, Vector4.zero);
                vh.AddVert(pts[i] - nrm * half,             core, Vector4.zero);
                vh.AddVert(pts[i] + nrm * half,             core, Vector4.zero);
                vh.AddVert(pts[i] + nrm * (half + feather), edge, Vector4.zero);
            }
        }

        int across = glowPass ? 3 : 4;
        int segs = closed ? count : count - 1;
        for (int i = 0; i < segs; i++)
        {
            int a = start + i * across;
            int b = start + ((i + 1) % count) * across;
            for (int k = 0; k < across - 1; k++)
            {
                vh.AddTriangle(a + k, b + k, b + k + 1);
                vh.AddTriangle(a + k, b + k + 1, a + k + 1);
            }
        }
    }

    static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, col, Vector4.zero);
        vh.AddVert(b, col, Vector4.zero);
        vh.AddVert(c, col, Vector4.zero);
        vh.AddVert(d, col, Vector4.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}
