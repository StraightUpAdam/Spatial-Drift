using UnityEngine;

/// Procedural source art for the menu kit — white shapes with anti-aliased
/// alpha, tinted per use by Image.color.  The scene builders save these as
/// PNGs under Resources/UI (so they're real, replaceable assets); UITheme
/// falls back to generating them in memory if a PNG is missing.
public static class UISpriteFactory
{
    public static readonly string[] Names =
    {
        "ui_round", "ui_round_line", "ui_soft", "ui_grad_h", "ui_grad_v",
        "ui_circle", "ui_ring", "ui_ring_arc", "ui_chevron", "ui_pill",
        "ui_stripes", "ui_vignette", "ui_glow", "ui_white",
    };

    /// Pixel data + import hints for one named sprite.  border = L,B,R,T (9-slice).
    public static Texture2D CreateTexture(string name, out Vector4 border, out bool repeat)
    {
        border = Vector4.zero;
        repeat = false;
        switch (name)
        {
            case "ui_round":      border = new Vector4(22, 22, 22, 22); return Make(64, 64, (x, y) => RoundRect(x, y, 64, 64, 14f, 0f));
            case "ui_round_line": border = new Vector4(22, 22, 22, 22); return Make(64, 64, (x, y) => RoundRect(x, y, 64, 64, 14f, 2.5f));
            case "ui_soft":       border = new Vector4(30, 30, 30, 30); return Make(64, 64, Soft);
            case "ui_grad_h":     return Make(256, 8, (x, y) => Smooth(1f - x / 255f));
            case "ui_grad_v":     return Make(8, 256, (x, y) => Smooth(y / 255f));
            case "ui_circle":     return Make(128, 128, (x, y) => Disc(x, y, 128, 62f, 0f));
            case "ui_ring":       return Make(128, 128, (x, y) => Disc(x, y, 128, 62f, 10f));
            case "ui_ring_arc":   return Make(128, 128, Arc);
            case "ui_chevron":    return Make(64, 64, Chevron);
            case "ui_pill":       border = new Vector4(31, 31, 31, 31); return Make(128, 64, (x, y) => RoundRect(x, y, 128, 64, 31f, 0f));
            case "ui_stripes":    repeat = true; return Make(32, 32, Stripes);
            case "ui_vignette":   return Make(128, 128, Vignette);
            case "ui_glow":       return Make(128, 128, Glow);
            default:              return Make(4, 4, (x, y) => 1f);   // ui_white
        }
    }

    public static Sprite CreateSprite(string name)
    {
        var tex = CreateTexture(name, out var border, out bool repeat);
        tex.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        var s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                          new Vector2(0.5f, 0.5f), 100f, 0,
                                          SpriteMeshType.FullRect, border);
        s.name = name;
        return s;
    }

    // ── Rasteriser ────────────────────────────────────────────────────────────
    delegate float Shape(float x, float y);

    static Texture2D Make(int w, int h, Shape alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = "UIGen", filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
        };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float a = Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f));
            px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return tex;
    }

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // Signed distance to a rounded rectangle filling (w,h) with 1px padding.
    static float RoundRectSD(float x, float y, float w, float h, float r)
    {
        float cx = w * 0.5f, cy = h * 0.5f;
        float bx = cx - 1f - r, by = cy - 1f - r;
        float qx = Mathf.Abs(x - cx) - bx, qy = Mathf.Abs(y - cy) - by;
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static float RoundRect(float x, float y, float w, float h, float r, float line)
    {
        float d = RoundRectSD(x, y, w, h, r);
        if (line <= 0f) return 0.5f - d;
        // Outline: band of `line` px just inside the edge.
        return 0.5f - (Mathf.Abs(d + line * 0.5f) - line * 0.5f);
    }

    static float Soft(float x, float y)
    {
        // Distance outside a small inner rounded box → gaussian falloff.
        float cx = 32f, cy = 32f;
        float qx = Mathf.Abs(x - cx) - 4f, qy = Mathf.Abs(y - cy) - 4f;
        float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        float k = d / 13f;
        return Mathf.Exp(-k * k);
    }

    static float Disc(float x, float y, int size, float radius, float ring)
    {
        float c = size * 0.5f;
        float d = new Vector2(x - c, y - c).magnitude - radius;
        if (ring <= 0f) return 0.5f - d;
        return 0.5f - (Mathf.Abs(d + ring * 0.5f) - ring * 0.5f);
    }

    static float Arc(float x, float y)
    {
        float ring = Disc(x, y, 128, 62f, 10f);
        if (ring <= 0f) return 0f;
        // 300° comet: bright head at 0°, fading tail, gap at the end.
        float ang = Mathf.Atan2(y - 64f, x - 64f) * Mathf.Rad2Deg;   // -180..180
        float t = Mathf.Repeat(-ang, 360f) / 300f;                   // 0 head → 1 tail
        if (t > 1f) return 0f;
        return ring * (1f - t) * (1f - t);
    }

    static float SegSD(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude;
    }

    static float Chevron(float x, float y)
    {
        var p = new Vector2(x, y);
        float d = Mathf.Min(SegSD(p, new Vector2(22f, 10f), new Vector2(42f, 32f)),
                            SegSD(p, new Vector2(42f, 32f), new Vector2(22f, 54f)));
        return 0.5f - (d - 6f);
    }

    static float Stripes(float x, float y)
    {
        // 4x supersampled 45° bands, tileable on a 32px repeat (period 16).
        float sum = 0f;
        for (int sy = 0; sy < 4; sy++)
        for (int sx = 0; sx < 4; sx++)
        {
            float px = x - 0.5f + (sx + 0.5f) / 4f;
            float py = y - 0.5f + (sy + 0.5f) / 4f;
            sum += Mathf.Repeat(px + py, 16f) < 7f ? 1f : 0f;
        }
        return sum / 16f;
    }

    static float Vignette(float x, float y)
    {
        float dx = (x - 64f) / 64f, dy = (y - 64f) / 64f;
        float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.2f;
        return Smooth((d - 0.35f) / 0.65f);
    }

    static float Glow(float x, float y)
    {
        float r = new Vector2(x - 64f, y - 64f).magnitude / 63f;
        float a = Mathf.Clamp01(1f - r);
        return a * a * a;
    }
}
