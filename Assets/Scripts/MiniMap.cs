using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Simple top-right minimap: the track outline drawn once into a texture
/// (top-down XZ), with live dots for every car — the local player is the big
/// white one.  Self-contained like the other runtime HUD pieces: drop the
/// component in a scene with a Track and it wires itself.
[DisallowMultipleComponent]
public class MiniMap : MonoBehaviour
{
    const int   TexSize = 256;
    const float UISize  = 210f;   // on-screen size (canvas units)
    const float Margin  = 14f;    // world-units padding around the track bounds

    Track   track;
    Canvas  canvas;
    RectTransform mapRT;
    Vector2 worldMin, worldSize;
    float   refreshTimer;

    sealed class Dot { public CarController car; public Image img; }
    readonly List<Dot> dots = new List<Dot>();

    static Sprite _dotSprite;

    void LateUpdate()
    {
        if (track == null || track.Length <= 0f)
        {
            track = FindFirstObjectByType<Track>();
            if (track == null || track.Length <= 0f) return;
            Build();
        }
        if (canvas == null) return;

        // Options ▸ Gameplay ▸ Minimap.
        bool show = GameSettings.ShowMinimap;
        if (canvas.enabled != show) canvas.enabled = show;

        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f) { refreshTimer = 2f; RefreshCars(); }

        for (int i = dots.Count - 1; i >= 0; i--)
        {
            var d = dots[i];
            if (d.car == null || d.img == null)
            {
                if (d.img != null) Destroy(d.img.gameObject);
                dots.RemoveAt(i);
                continue;
            }
            d.img.rectTransform.anchoredPosition = WorldToMap(d.car.transform.position);
        }
    }

    // ── Build ─────────────────────────────────────────────────────────────────
    void Build()
    {
        // World bounds of the track (XZ), padded.
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        float len = track.Length;
        for (int i = 0; i < 256; i++)
        {
            track.Sample(len * i / 256f, out Vector3 p, out _, out _);
            min = Vector2.Min(min, new Vector2(p.x, p.z));
            max = Vector2.Max(max, new Vector2(p.x, p.z));
        }
        min -= Vector2.one * Margin;
        max += Vector2.one * Margin;
        worldMin  = min;
        // Square area (uniform scale) so the layout isn't stretched.
        float ext = Mathf.Max(max.x - min.x, max.y - min.y);
        worldSize = new Vector2(ext, ext);

        var go = new GameObject("MiniMapCanvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        // Backing panel, top-right (below the MP host's End Race button).
        var bg = new GameObject("Bg", typeof(RectTransform)).AddComponent<Image>();
        bg.transform.SetParent(canvas.transform, false);
        bg.color         = new Color(0f, 0f, 0f, 0.32f);
        bg.raycastTarget = false;
        mapRT = bg.rectTransform;
        mapRT.anchorMin = mapRT.anchorMax = mapRT.pivot = new Vector2(1f, 1f);
        mapRT.anchoredPosition = new Vector2(-16f, -56f);
        mapRT.sizeDelta        = new Vector2(UISize, UISize);

        // Track outline texture.
        var outline = new GameObject("Track", typeof(RectTransform)).AddComponent<RawImage>();
        outline.transform.SetParent(mapRT, false);
        outline.texture       = DrawTrackTexture();
        outline.raycastTarget = false;
        var ort = outline.rectTransform;
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = Vector2.zero; ort.offsetMax = Vector2.zero;

        RefreshCars();
    }

    Texture2D DrawTrackTexture()
    {
        var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };

        var clear = new Color(0, 0, 0, 0);
        var road  = new Color(0.92f, 0.92f, 0.97f, 0.95f);
        var pixels = new Color[TexSize * TexSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
        tex.SetPixels(pixels);

        float len = track.Length;
        int samples = Mathf.Clamp(Mathf.CeilToInt(len), 256, 4096);
        for (int i = 0; i <= samples; i++)
        {
            track.Sample(len * i / samples, out Vector3 p, out _, out _);
            Vector2 uv = new Vector2((p.x - worldMin.x) / worldSize.x,
                                     (p.z - worldMin.y) / worldSize.y);
            int px = Mathf.Clamp(Mathf.RoundToInt(uv.x * (TexSize - 1)), 1, TexSize - 2);
            int py = Mathf.Clamp(Mathf.RoundToInt(uv.y * (TexSize - 1)), 1, TexSize - 2);
            for (int ox = -1; ox <= 1; ox++)
            for (int oy = -1; oy <= 1; oy++)
                tex.SetPixel(px + ox, py + oy, road);
        }

        // Start/finish marker.
        track.Sample(0f, out Vector3 s, out _, out _);
        Vector2 suv = new Vector2((s.x - worldMin.x) / worldSize.x,
                                  (s.z - worldMin.y) / worldSize.y);
        int sx = Mathf.Clamp(Mathf.RoundToInt(suv.x * (TexSize - 1)), 2, TexSize - 3);
        int sy = Mathf.Clamp(Mathf.RoundToInt(suv.y * (TexSize - 1)), 2, TexSize - 3);
        var mark = new Color(1f, 0.85f, 0.25f, 1f);
        for (int ox = -2; ox <= 2; ox++)
        for (int oy = -2; oy <= 2; oy++)
            tex.SetPixel(sx + ox, sy + oy, mark);

        tex.Apply();
        return tex;
    }

    // ── Car dots ──────────────────────────────────────────────────────────────
    void RefreshCars()
    {
        foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
        {
            bool known = false;
            foreach (var d in dots) if (d.car == car) { known = true; break; }
            if (known) continue;

            bool local = !car.aiControlled &&
                         (CarController.LocalPlayerOverride == null ||
                          CarController.LocalPlayerOverride == car);

            var img = new GameObject("Dot", typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(mapRT, false);
            img.sprite        = DotSprite();
            img.raycastTarget = false;
            img.color         = DotColor(car, local);
            img.rectTransform.sizeDelta = Vector2.one * (local ? 13f : 9f);
            dots.Add(new Dot { car = car, img = img });
        }
    }

    static Color DotColor(CarController car, bool local)
    {
        if (local) return Color.white;
        var sync = car.GetComponent<NetworkCarSync>();
        if (sync != null && sync.ColorId.Value >= 0)
            return PlayerProfile.ColorById(sync.ColorId.Value);
        return car.aiControlled ? new Color(0.65f, 0.65f, 0.70f, 1f)
                                : new Color(0.45f, 0.75f, 1f, 1f);
    }

    Vector2 WorldToMap(Vector3 world)
    {
        float u = (world.x - worldMin.x) / worldSize.x;
        float v = (world.z - worldMin.y) / worldSize.y;
        return new Vector2((u - 0.5f) * UISize, (v - 0.5f) * UISize);
    }

    static Sprite DotSprite()
    {
        if (_dotSprite != null) return _dotSprite;
        const int S = 24;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f;
            float r  = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float a  = Mathf.Clamp01((1f - r) * 6f);          // hard disc, soft rim
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        _dotSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return _dotSprite;
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
