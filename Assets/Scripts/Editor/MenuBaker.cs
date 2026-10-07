using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// Editor-side kit that BAKES the menu design system into scenes: every row,
/// panel, prompt bar and dialog is a real GameObject saved in the .unity file
/// (art-pass them in the Inspector); the runtime components only animate.
/// Also writes the UI sprite PNGs (Resources/UI) on first use — replace any
/// PNG with your own art and every menu picks it up.
public static class MenuBaker
{
    public const float RowH   = 62f;
    public const float RowGap = 10f;
    public const float Shear  = 0.2f;

    // ══════════════════════════════════════════════════════════════════════════
    //  Art
    // ══════════════════════════════════════════════════════════════════════════
    const string ArtDir = "Assets/Resources/UI";

    public static void EnsureArt()
    {
        Directory.CreateDirectory(ArtDir);
        var fresh = new System.Collections.Generic.List<string>();
        foreach (var n in UISpriteFactory.Names)
        {
            string path = $"{ArtDir}/{n}.png";
            if (File.Exists(path)) continue;
            var tex = UISpriteFactory.CreateTexture(n, out _, out _);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            fresh.Add(n);
        }
        if (fresh.Count > 0) AssetDatabase.Refresh();

        foreach (var n in UISpriteFactory.Names)
        {
            string path = $"{ArtDir}/{n}.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            if (!fresh.Contains(n) && ti.textureType == TextureImporterType.Sprite) continue;   // keep custom art settings
            var tmp = UISpriteFactory.CreateTexture(n, out var border, out bool repeat);
            Object.DestroyImmediate(tmp);
            ti.textureType         = TextureImporterType.Sprite;
            ti.spriteImportMode    = SpriteImportMode.Single;
            ti.spriteBorder        = border;
            ti.spritePixelsPerUnit = 100f;
            ti.mipmapEnabled       = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode            = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.filterMode          = FilterMode.Bilinear;
            ti.textureCompression  = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
    }

    public static Sprite S(string name) =>
        AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{name}.png");

    // ══════════════════════════════════════════════════════════════════════════
    //  Scene scaffolding
    // ══════════════════════════════════════════════════════════════════════════
    /// Menu camera: the real sky/clouds/fog/post (menu dusk mood) + the plains.
    public static Camera MenuCamera(bool drift, float startYaw, float fov = 55f)
    {
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView   = fov;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane  = 12000f;
        camGO.AddComponent<AudioListener>();
        camGO.AddComponent<SceneAmbiance>().menuMood = true;
        var bd = camGO.AddComponent<MenuBackdrop>();
        bd.driftCamera = drift;
        bd.startYaw    = startYaw;
        camGO.transform.SetPositionAndRotation(new Vector3(0f, 9f, 0f), Quaternion.Euler(-3f, startYaw, 0f));

        var sun = new GameObject("Sun");
        Atmosphere.ApplySun(sun.AddComponent<Light>(), Atmosphere.Menu);
        return cam;
    }

    public static void EventSystem()
    {
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>().sendNavigationEvents = false;
        es.AddComponent<StandaloneInputModule>();
    }

    public static MenuSceneRoot SceneRoot(bool music = true)
    {
        var r = new GameObject("MenuSceneRoot").AddComponent<MenuSceneRoot>();
        r.playMusic = music;
        return r;
    }

    public static Canvas Canvas(string name, int order)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Primitives
    // ══════════════════════════════════════════════════════════════════════════
    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static RectTransform Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(-r, -t);
        return rt;
    }

    public static Image Img(Transform parent, string name, string sprite, Color c, bool sliced = false, bool raycast = false)
    {
        var rt = Rect(parent, name);
        var img = rt.gameObject.AddComponent<Image>();
        if (!string.IsNullOrEmpty(sprite)) img.sprite = S(sprite);
        img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color c,
                                       FontStyles style = FontStyles.Normal,
                                       TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var rt = Rect(parent, name);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.fontStyle = style;
        t.alignment = align;
        t.raycastTarget = false;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    public static void AddShear(Graphic g, float s = Shear) => g.gameObject.AddComponent<UIShear>().shear = s;

    // ══════════════════════════════════════════════════════════════════════════
    //  Chrome
    // ══════════════════════════════════════════════════════════════════════════
    /// Legibility shading over the 3D backdrop: a left gradient under the
    /// rows, soft top/bottom bands and a vignette.
    public static void Shades(Transform canvas, float leftWidth = 1150f, float leftAlpha = 0.82f)
    {
        var left = Img(canvas, "ShadeLeft", "ui_grad_h", new Color(0.02f, 0.015f, 0.04f, leftAlpha));
        left.rectTransform.anchorMin = new Vector2(0f, 0f);
        left.rectTransform.anchorMax = new Vector2(0f, 1f);
        left.rectTransform.pivot     = new Vector2(0f, 0.5f);
        left.rectTransform.anchoredPosition = Vector2.zero;
        left.rectTransform.sizeDelta = new Vector2(leftWidth, 0f);

        var top = Img(canvas, "ShadeTop", "ui_grad_v", new Color(0.02f, 0.015f, 0.04f, 0.55f));
        top.rectTransform.anchorMin = new Vector2(0f, 1f);
        top.rectTransform.anchorMax = new Vector2(1f, 1f);
        top.rectTransform.pivot     = new Vector2(0.5f, 1f);
        top.rectTransform.sizeDelta = new Vector2(0f, 230f);

        var bottom = Img(canvas, "ShadeBottom", "ui_grad_v", new Color(0.02f, 0.015f, 0.04f, 0.7f));
        bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
        bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
        bottom.rectTransform.pivot     = new Vector2(0.5f, 0f);
        bottom.rectTransform.sizeDelta = new Vector2(0f, 200f);
        bottom.rectTransform.localScale = new Vector3(1f, -1f, 1f);
        bottom.rectTransform.anchoredPosition = new Vector2(0f, 200f);

        var vig = Img(canvas, "Vignette", "ui_vignette", new Color(0f, 0f, 0f, 0.5f));
        Stretch(vig.rectTransform);
    }

    /// Breadcrumb + big italic title + accent bar (top-left).
    public static TextMeshProUGUI Header(Transform canvas, string crumb, string title)
    {
        var root = Place(Rect(canvas, "Header"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(110f, -58f), new Vector2(1000f, 130f));
        var c = Text(root, "Crumb", crumb, 19, UITheme.TextDim, FontStyles.Bold);
        c.characterSpacing = 8f;
        Place(c.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1000f, 26f));
        var t = Text(root, "Title", title, 62, UITheme.Text, FontStyles.Bold | FontStyles.Italic);
        Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-4f, -24f), new Vector2(1000f, 76f));
        var bar = Img(root, "AccentBar", null, UITheme.Accent);
        Place(bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -110f), new Vector2(120f, 6f));
        AddShear(bar, 1f);
        var line = Img(root, "Rule", null, UITheme.Line);
        Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(136f, -112f), new Vector2(760f, 2f));
        return t;
    }

    /// Glass card: dark rounded panel with a thin outline and a top accent.
    public static RectTransform Card(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
                                     bool accentTop = true)
    {
        var rt = Place(Rect(parent, name), anchor, pivot, pos, size);
        var shadow = Img(rt, "Shadow", "ui_soft", new Color(0f, 0f, 0f, 0.55f), true);
        Stretch(shadow.rectTransform, -26, -30, -26, -22);
        var bg = Img(rt, "BG", "ui_round", UITheme.Panel, true, true);
        Stretch(bg.rectTransform);
        var line = Img(rt, "Outline", "ui_round_line", UITheme.Alpha(Color.white, 0.07f), true);
        Stretch(line.rectTransform);
        if (accentTop)
        {
            var acc = Img(rt, "TopAccent", null, UITheme.Accent);
            Place(acc.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -1f), new Vector2(90f, 4f));
        }
        return rt;
    }

    public static TextMeshProUGUI CardTitle(RectTransform card, string text, float y = -26f)
    {
        var t = Text(card, "CardTitle", text, 20, UITheme.TextDim, FontStyles.Bold);
        t.characterSpacing = 6f;
        Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, y), new Vector2(card.sizeDelta.x - 56f, 28f));
        return t;
    }

    public static TextMeshProUGUI HintLine(Transform canvas, Vector2 pos, float width = 900f)
    {
        var t = Text(canvas, "Hint", "", 22, UITheme.TextDim);
        Place(t.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), pos, new Vector2(width, 60f));
        t.textWrappingMode = TextWrappingModes.Normal;
        t.alignment = TextAlignmentOptions.BottomLeft;
        return t;
    }

    public static PromptBar Prompts(Transform canvas)
    {
        var root = Place(Rect(canvas, "Prompts"), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-84f, 42f), new Vector2(1200f, 44f));
        var h = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleRight;
        h.spacing = 30f;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        h.reverseArrangement = false;

        var e = Rect(root, "PromptTemplate");
        var eh = e.gameObject.AddComponent<HorizontalLayoutGroup>();
        eh.childAlignment = TextAnchor.MiddleLeft;
        eh.spacing = 10f;
        eh.childControlWidth = true; eh.childControlHeight = true;
        eh.childForceExpandWidth = false; eh.childForceExpandHeight = false;
        var entry = e.gameObject.AddComponent<PromptEntry>();

        var glyph = Img(e, "Glyph", "ui_circle", UITheme.PadA, false, true);
        var le = glyph.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 36f; le.minWidth = 36f; le.preferredHeight = 34f; le.minHeight = 34f;
        var gt = Text(glyph.transform, "GlyphText", "A", 18, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(gt.rectTransform);
        var aA = Img(glyph.transform, "ArrowA", "ui_chevron", UITheme.Text);
        Place(aA.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-12f, 0f), new Vector2(16f, 16f));
        var aB = Img(glyph.transform, "ArrowB", "ui_chevron", UITheme.Text);
        Place(aB.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(12f, 0f), new Vector2(16f, 16f));

        var label = Text(e, "Label", "SELECT", 20, UITheme.TextDim, FontStyles.Bold);
        label.characterSpacing = 4f;
        label.raycastTarget = true;

        entry.glyphBg = glyph; entry.glyphText = gt; entry.arrowA = aA; entry.arrowB = aB; entry.label = label;
        e.gameObject.SetActive(false);

        var bar = root.gameObject.AddComponent<PromptBar>();
        bar.template = entry;
        return bar;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Screens + rows
    // ══════════════════════════════════════════════════════════════════════════
    /// A MenuScreen whose rows stack top-down inside `rect`.  With scroll, rows
    /// live in a masked viewport and the screen keeps the focused row visible.
    public static MenuScreen Screen(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos,
                                    Vector2 size, bool showOnStart, bool scroll, out RectTransform rows)
    {
        var rt = Place(Rect(parent, name), anchor, pivot, pos, size);
        rt.gameObject.AddComponent<CanvasGroup>();
        var screen = rt.gameObject.AddComponent<MenuScreen>();
        screen.group = rt.GetComponent<CanvasGroup>();
        screen.showOnStart = showOnStart;
        if (!showOnStart) screen.group.alpha = 0f;

        Transform host = rt;
        if (scroll)
        {
            var vp = Stretch(Rect(rt, "Viewport"), -30, 0, -30, 0);
            vp.gameObject.AddComponent<RectMask2D>();
            screen.viewport = vp;
            host = vp;
        }

        rows = Rect(host, "Rows");
        rows.anchorMin = new Vector2(0f, 1f);
        rows.anchorMax = new Vector2(1f, 1f);
        rows.pivot     = new Vector2(0.5f, 1f);
        rows.anchoredPosition = Vector2.zero;
        rows.sizeDelta = new Vector2(scroll ? -60f : 0f, 0f);
        var v = rows.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = RowGap;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.padding = new RectOffset(0, 14, 6, 6);
        var fit = rows.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        if (scroll) screen.scrollContent = rows;
        return screen;
    }

    static RectTransform RowBase(Transform rows, string name, float height, out RectTransform content,
                                 out Image bg, out Image bar, out Image edge, out Image glow, out TextMeshProUGUI label,
                                 string text, bool primary)
    {
        var rt = Rect(rows, "Row_" + name);
        rt.sizeDelta = new Vector2(0f, height);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = height; le.minHeight = height;
        rt.gameObject.AddComponent<CanvasGroup>();

        content = Stretch(Rect(rt, "Content"));
        glow = Img(content, "Glow", "ui_soft", Color.clear, true);
        Stretch(glow.rectTransform, -22, -22, -22, -22);
        bg = Img(content, "BG", "ui_round", primary ? UITheme.AccentDeep : UITheme.Row, true, true);
        Stretch(bg.rectTransform);
        AddShear(bg);

        bar = Img(content, "FocusBar", "ui_grad_h", Color.clear);
        bar.rectTransform.anchorMin = new Vector2(0f, 0f);
        bar.rectTransform.anchorMax = new Vector2(0.75f, 1f);
        bar.rectTransform.pivot     = new Vector2(0f, 0.5f);
        bar.rectTransform.offsetMin = new Vector2(2f, 2f);
        bar.rectTransform.offsetMax = new Vector2(0f, -2f);
        AddShear(bar);

        edge = Img(content, "Edge", null, Color.clear);
        edge.rectTransform.anchorMin = new Vector2(0f, 0f);
        edge.rectTransform.anchorMax = new Vector2(0f, 1f);
        edge.rectTransform.pivot     = new Vector2(0f, 0.5f);
        edge.rectTransform.offsetMin = new Vector2(0f, 4f);
        edge.rectTransform.offsetMax = new Vector2(5f, -4f);
        AddShear(edge);

        label = Text(content, "Label", text, primary ? 30 : 25, UITheme.TextDim, FontStyles.Bold,
                     primary ? TextAlignmentOptions.Center : TextAlignmentOptions.Left);
        label.characterSpacing = primary ? 8f : 3f;
        if (primary) Stretch(label.rectTransform);
        else
        {
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.55f, 1f);
            label.rectTransform.offsetMin = new Vector2(32f, 0f);
            label.rectTransform.offsetMax = new Vector2(0f, 0f);
        }
        return rt;
    }

    static void WireBase(MenuRow row, RectTransform content, Image bg, Image bar, Image edge, Image glow,
                         TextMeshProUGUI label, string hint, bool primary)
    {
        row.content = content; row.background = bg; row.focusBar = bar; row.edge = edge; row.glow = glow;
        row.label = label; row.hint = hint; row.primary = primary;
    }

    public static MenuButton ButtonRow(Transform rows, string text, string hint, bool primary = false,
                                       float height = RowH, Sfx sound = Sfx.Confirm)
    {
        var rt = RowBase(rows, text, primary ? height + 16f : height, out var content, out var bg, out var bar,
                         out var edge, out var glow, out var label, text, primary);
        var b = rt.gameObject.AddComponent<MenuButton>();
        WireBase(b, content, bg, bar, edge, glow, label, hint, primary);
        b.sound = sound;
        if (!primary)
        {
            // A small chevron on the right says "this opens something".
            var chev = Img(content, "Chevron", "ui_chevron", UITheme.Alpha(UITheme.TextFaint, 0.8f));
            Place(chev.rectTransform, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-30f, 0f), new Vector2(20f, 20f));
        }
        return b;
    }

    static RectTransform ValueArea(RectTransform content, float width)
    {
        var va = Rect(content, "Value");
        va.anchorMin = new Vector2(1f, 0f);
        va.anchorMax = new Vector2(1f, 1f);
        va.pivot     = new Vector2(1f, 0.5f);
        va.anchoredPosition = new Vector2(-16f, 0f);
        va.sizeDelta = new Vector2(width, 0f);
        return va;
    }

    public static MenuStepper StepperRow(Transform rows, string text, string hint, float valueWidth = 330f,
                                         int swatches = 0, float height = RowH)
    {
        var rt = RowBase(rows, text, swatches > 0 ? height + 18f : height, out var content, out var bg, out var bar,
                         out var edge, out var glow, out var label, text, false);
        var s = rt.gameObject.AddComponent<MenuStepper>();
        WireBase(s, content, bg, bar, edge, glow, label, hint, false);

        var va = ValueArea(content, valueWidth);
        var al = Img(va, "ArrowL", "ui_chevron", UITheme.TextFaint, false, true);
        Place(al.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, swatches > 0 ? 10f : 0f), new Vector2(26f, 26f));
        al.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
        var ar = Img(va, "ArrowR", "ui_chevron", UITheme.TextFaint, false, true);
        Place(ar.rectTransform, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-18f, swatches > 0 ? 10f : 0f), new Vector2(26f, 26f));
        var vt = Text(va, "ValueText", "VALUE", 24, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
        vt.characterSpacing = 3f;
        Stretch(vt.rectTransform, 38, swatches > 0 ? 20 : 0, 38, 0);
        s.valueText = vt; s.arrowLeft = al; s.arrowRight = ar;

        if (swatches > 0)
        {
            var strip = Rect(va, "Swatches");
            Place(strip, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(valueWidth - 70f, 18f));
            s.swatches = new Image[swatches];
            float step = (valueWidth - 90f) / Mathf.Max(1, swatches - 1);
            var cursor = Img(strip, "Cursor", "ui_ring", UITheme.Text);
            Place(cursor.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10f, 0f), new Vector2(26f, 26f));
            for (int i = 0; i < swatches; i++)
            {
                var sw = Img(strip, "Swatch" + i, "ui_circle", Color.white);
                Place(sw.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10f + i * step, 0f), new Vector2(16f, 16f));
                s.swatches[i] = sw;
            }
            cursor.transform.SetAsLastSibling();
            s.swatchCursor = cursor.rectTransform;
        }
        return s;
    }

    public static MenuSlider SliderRow(Transform rows, string text, string hint, float valueWidth = 330f)
    {
        var rt = RowBase(rows, text, RowH, out var content, out var bg, out var bar, out var edge, out var glow,
                         out var label, text, false);
        var s = rt.gameObject.AddComponent<MenuSlider>();
        WireBase(s, content, bg, bar, edge, glow, label, hint, false);

        var va = ValueArea(content, valueWidth);
        var track = Img(va, "Track", "ui_pill", UITheme.Alpha(Color.white, 0.08f), true, true);
        Place(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(valueWidth - 110f, 12f));
        var fill = Img(track.transform, "Fill", "ui_pill", UITheme.Accent, true);
        fill.rectTransform.anchorMin = new Vector2(0f, 0f);
        fill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        fill.rectTransform.offsetMin = Vector2.zero;
        fill.rectTransform.offsetMax = Vector2.zero;
        var knob = Img(track.transform, "Knob", "ui_circle", Color.white);
        Place(knob.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f));
        var vt = Text(va, "ValueText", "100%", 22, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Right);
        Place(vt.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(90f, 40f));
        s.track = track.rectTransform; s.fill = fill.rectTransform; s.knob = knob.rectTransform; s.valueText = vt;
        return s;
    }

    public static MenuTextField TextRow(Transform rows, string text, string hint, float valueWidth = 330f)
    {
        var rt = RowBase(rows, text, RowH, out var content, out var bg, out var bar, out var edge, out var glow,
                         out var label, text, false);
        var f = rt.gameObject.AddComponent<MenuTextField>();
        WireBase(f, content, bg, bar, edge, glow, label, hint, false);
        var va = ValueArea(content, valueWidth);
        var box = Img(va, "Box", "ui_round", UITheme.Alpha(Color.black, 0.35f), true);
        Stretch(box.rectTransform, 0, 10, 0, 10);
        var vt = Text(va, "ValueText", "DRIVER", 24, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
        vt.characterSpacing = 3f;
        Stretch(vt.rectTransform, 12, 0, 12, 0);
        f.valueText = vt;
        return f;
    }

    public static MenuCodeField CodeRow(Transform rows, string text, string hint, float valueWidth = 330f)
    {
        var rt = RowBase(rows, text, RowH + 8f, out var content, out var bg, out var bar, out var edge, out var glow,
                         out var label, text, false);
        var f = rt.gameObject.AddComponent<MenuCodeField>();
        WireBase(f, content, bg, bar, edge, glow, label, hint, false);
        var va = ValueArea(content, valueWidth);
        f.slots = new TextMeshProUGUI[6];
        f.slotBgs = new Image[6];
        float w = (valueWidth - 10f) / 6f;
        for (int i = 0; i < 6; i++)
        {
            var sb = Img(va, "Slot" + i, "ui_round", UITheme.Alpha(Color.black, 0.45f), true);
            Place(sb.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * w, 0f), new Vector2(w - 6f, 48f));
            var st = Text(sb.transform, "Char", "", 28, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(st.rectTransform);
            f.slots[i] = st; f.slotBgs[i] = sb;
        }
        return f;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Modal
    // ══════════════════════════════════════════════════════════════════════════
    public static MenuModal Modal(Transform canvas)
    {
        var root = Stretch(Rect(canvas, "Modal"));
        root.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
        var screen = root.gameObject.AddComponent<MenuScreen>();
        screen.group = root.GetComponent<CanvasGroup>();
        screen.showOnStart = false;
        var dim = Img(root, "Dim", null, new Color(0.01f, 0.005f, 0.03f, 0.72f), false, true);
        Stretch(dim.rectTransform);

        var card = Card(root, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780f, 400f));
        var title = Text(card, "Title", "ARE YOU SURE?", 44, UITheme.Text, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(720f, 60f));
        var body = Text(card, "Body", "", 23, UITheme.TextDim, FontStyles.Normal, TextAlignmentOptions.Center);
        body.textWrappingMode = TextWrappingModes.Normal;
        Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(680f, 70f));

        var rows = Rect(card, "Rows");
        Place(rows, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(460f, 150f));
        var v = rows.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = RowGap; v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.LowerCenter;
        var yes = ButtonRow(rows, "YES", "", true, RowH - 16f);
        var no  = ButtonRow(rows, "NO", "", false, RowH, Sfx.Back);
        screen.items.Add(yes); screen.items.Add(no);

        var modal = root.gameObject.AddComponent<MenuModal>();
        modal.title = title; modal.body = body; modal.yesButton = yes; modal.noButton = no;
        return modal;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Small displays
    // ══════════════════════════════════════════════════════════════════════════
    /// Label over a big value (stat tiles on map cards).
    public static TextMeshProUGUI Stat(RectTransform parent, string caption, Vector2 pos, float width = 180f)
    {
        var root = Place(Rect(parent, "Stat_" + caption), new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(width, 64f));
        var c = Text(root, "Caption", caption, 16, UITheme.TextFaint, FontStyles.Bold);
        c.characterSpacing = 5f;
        Place(c.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(width, 20f));
        var v = Text(root, "Value", "—", 30, UITheme.Text, FontStyles.Bold);
        Place(v.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -22f), new Vector2(width, 40f));
        return v;
    }

    public static MapPreviewGraphic Preview(RectTransform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var rt = Place(Rect(parent, "MapPreview"), anchor, pivot, pos, size);
        var frame = Img(rt, "Frame", "ui_round", UITheme.Alpha(Color.black, 0.35f), true);
        Stretch(frame.rectTransform);
        var gridA = Img(rt, "Grid", "ui_stripes", UITheme.Alpha(Color.white, 0.02f));
        gridA.type = Image.Type.Tiled;
        Stretch(gridA.rectTransform, 4, 4, 4, 4);

        var gRT = Stretch(Rect(rt, "Line"));
        var g = gRT.gameObject.AddComponent<MapPreviewGraphic>();
        g.raycastTarget = false;
        var runner = Img(gRT, "Runner", "ui_glow", UITheme.Alpha(Color.white, 0.9f));
        Place(runner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
        var dot = Img(runner.transform, "Dot", "ui_circle", Color.white);
        Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
        g.runner = runner.rectTransform;
        return g;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Tool widgets (the map editor panel — plain uGUI, themed, mouse-first)
    // ══════════════════════════════════════════════════════════════════════════
    public static Button ToolButton(Transform parent, string label, float w, float h, bool accent = false, bool danger = false)
    {
        var img = Img(parent, "Btn_" + label, "ui_round", Color.white, true, true);
        img.rectTransform.sizeDelta = new Vector2(w, h);
        var le = img.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.preferredHeight = h; le.minHeight = h;
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        var cb = b.colors;
        Color baseC = accent ? UITheme.AccentDeep : danger ? new Color(0.36f, 0.10f, 0.14f, 0.95f) : new Color(0.12f, 0.10f, 0.20f, 0.95f);
        cb.normalColor      = baseC;
        cb.highlightedColor = accent ? UITheme.Accent : danger ? new Color(0.62f, 0.18f, 0.24f, 1f) : UITheme.RowFocus;
        cb.pressedColor     = UITheme.AccentHot;
        cb.selectedColor    = baseC;
        cb.disabledColor    = new Color(0.10f, 0.09f, 0.14f, 0.5f);
        cb.fadeDuration     = 0.08f;
        b.colors = cb;
        var t = Text(img.transform, "Label", label, accent ? 22 : 17, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
        t.characterSpacing = 2f;
        Stretch(t.rectTransform);
        return b;
    }

    public static Slider ToolSlider(Transform parent, float w)
    {
        var rt = Rect(parent, "Slider");
        rt.sizeDelta = new Vector2(w, 26f);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.preferredHeight = 26f; le.flexibleWidth = 1f;
        var bg = Img(rt, "Track", "ui_pill", UITheme.Alpha(Color.white, 0.08f), true, true);
        Stretch(bg.rectTransform, 0, 8, 0, 8);
        var fa = Stretch(Rect(rt, "Fill Area"), 0, 8, 0, 8);
        var fill = Img(fa, "Fill", "ui_pill", UITheme.Accent, true);
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = new Vector2(10f, 0f);
        var ha = Stretch(Rect(rt, "Handle Area"), 8, 0, 8, 0);
        var handle = Img(ha, "Handle", "ui_circle", Color.white, false, true);
        handle.rectTransform.sizeDelta = new Vector2(22f, 22f);
        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
        var nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
        return s;
    }

    public static Toggle ToolToggle(Transform parent, string label, float w)
    {
        var rt = Rect(parent, "Toggle_" + label);
        rt.sizeDelta = new Vector2(w, 34f);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.preferredHeight = 34f;
        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        var box = Img(rt, "Box", "ui_round", new Color(0.12f, 0.10f, 0.20f, 0.95f), true, true);
        Place(box.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
        var check = Img(box.transform, "Check", "ui_round", UITheme.Accent, true);
        Stretch(check.rectTransform, 6, 6, 6, 6);
        var t = Text(rt, "Label", label, 17, UITheme.TextDim, FontStyles.Bold);
        t.rectTransform.anchorMin = new Vector2(0f, 0f); t.rectTransform.anchorMax = new Vector2(1f, 1f);
        t.rectTransform.offsetMin = new Vector2(40f, 0f); t.rectTransform.offsetMax = Vector2.zero;
        var tog = rt.gameObject.AddComponent<Toggle>();
        tog.targetGraphic = box; tog.graphic = check;
        var nav = tog.navigation; nav.mode = Navigation.Mode.None; tog.navigation = nav;
        return tog;
    }

    public static TMP_InputField ToolInput(Transform parent, float w)
    {
        var bg = Img(parent, "NameInput", "ui_round", new Color(0.02f, 0.02f, 0.05f, 0.85f), true, true);
        bg.rectTransform.sizeDelta = new Vector2(w, 40f);
        var le = bg.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.preferredHeight = 40f; le.flexibleWidth = 1f;
        var area = Stretch(Rect(bg.transform, "Text Area"), 12, 4, 12, 4);
        area.gameObject.AddComponent<RectMask2D>();
        var ph = Text(area, "Placeholder", "Track name…", 19, UITheme.TextFaint, FontStyles.Italic);
        Stretch(ph.rectTransform);
        ph.alignment = TextAlignmentOptions.MidlineLeft;
        var tx = Text(area, "Text", "", 19, UITheme.Text, FontStyles.Bold);
        Stretch(tx.rectTransform);
        tx.alignment = TextAlignmentOptions.MidlineLeft;
        var input = bg.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area; input.textComponent = tx; input.placeholder = ph;
        input.fontAsset = TMP_Settings.defaultFontAsset;
        input.characterLimit = 32;
        input.caretColor = UITheme.Accent; input.customCaretColor = true;
        input.selectionColor = UITheme.Alpha(UITheme.Accent, 0.4f);
        var nav = input.navigation; nav.mode = Navigation.Mode.None; input.navigation = nav;
        return input;
    }

    // ══════════════════════════════════════════════════════════════════════════
    public static void SaveScene(string path)
    {
        Directory.CreateDirectory(Application.dataPath + "/Scenes");
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(), path);
    }
}
