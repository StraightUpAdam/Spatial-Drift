using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// Every scene change in the game goes through here: a slanted ink wipe with
/// accent edges sweeps across, the scene loads underneath (with a loading card
/// + tip for races), then the wipe carries on off the other side.  Also boots
/// the persistent menu services (sounds, music) before the first scene.
///
/// This overlay is a persistent SYSTEM (like a loading screen), so it's built
/// once in code; every actual menu is baked into its scene.
public class SceneFader : MonoBehaviour
{
    static SceneFader _i;

    /// True while a transition is running (menus ignore input meanwhile).
    public static bool Busy => _i != null && _i.busy;

    static readonly string[] Tips =
    {
        "Flick the stick while drifting to widen or tighten your angle.",
        "Shift inside the RPM boost window for a burst of speed.",
        "Drift Zone rails: every gap is at most two flicks away.",
        "Time Trial: the clock starts when you move.  R or B resets instantly.",
        "Design your own shift gate in the Shifter Lab.",
        "Tracks you build in the Map Editor are raceable everywhere — online too.",
        "Skipping a gear kicks the shifter back.  Shift in order.",
        "Weather changes the whole world.  Try a night race — headlights on.",
        "Share your join code and your friends can race you from anywhere.",
        "Hold the drift longer for a bigger boost on release.",
    };

    Canvas          canvas;
    RectTransform   root, wipe;
    CanvasGroup     loadGroup;
    TextMeshProUGUI title, subtitle, tip;
    RectTransform   spinner;
    float           pos = 1f;          // 1 = parked off right, 0 = covering, -1 = gone left
    bool            busy;
    bool            bootReveal;        // first scene of the session wipes in
    bool            awaitingExternal;  // covered for a load someone else drives
    bool            externalLoaded;    // …and that load has now arrived

    // ── Boot ──────────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Ensure();
        MenuAudio.Ensure();
        MenuMusic.Ensure();
        GameSettings.Apply();
        _i.SetPos(0f);                 // start covered → the first scene wipes in
        _i.bootReveal = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void Ensure()
    {
        if (_i != null) return;
        var go = new GameObject("[SceneFader]");
        DontDestroyOnLoad(go);
        _i = go.AddComponent<SceneFader>();
        _i.Build();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        GamePause.Clear();
        MenuInput.TextCapture = false;
        // Music only lives in menu scenes (they carry a MenuSceneRoot).
        var menuRoot = FindAnyObjectByType<MenuSceneRoot>();
        MenuMusic.SetWanted(menuRoot != null && menuRoot.playMusic);
        if (_i == null) return;
        if (_i.bootReveal) { _i.bootReveal = false; _i.StartCoroutine(_i.RevealAfterLoad(false)); }
        else if (_i.awaitingExternal) _i.externalLoaded = true;   // CoverRoutine reveals once its wipe lands
    }

    // ── Public API ────────────────────────────────────────────────────────────
    /// Wipe out, load `scene`, wipe in.  A title shows the loading card (races).
    public static void Load(string scene, string loadTitle = null, string loadSubtitle = null,
                            System.Action whenCovered = null)
    {
        Ensure();
        if (_i.busy) return;
        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogError($"[SceneFader] Scene '{scene}' isn't in Build Settings — run " +
                           "'Racing Game / Fix Build Settings'.");
            MenuAudio.Play(Sfx.Error);
            return;
        }
        _i.StartCoroutine(_i.LoadRoutine(scene, loadTitle, loadSubtitle, whenCovered));
    }

    /// Cover the screen for a load somebody else drives (Netcode scene loads);
    /// the wipe clears by itself once the next scene arrives.
    public static void CoverForExternalLoad(string loadTitle = null, string loadSubtitle = null)
    {
        Ensure();
        if (_i.busy) return;
        _i.StartCoroutine(_i.CoverRoutine(loadTitle, loadSubtitle));
    }

    public static void Quit()
    {
        Ensure();
        if (_i.busy) return;
        _i.StartCoroutine(_i.QuitRoutine());
    }

    // ── Routines ─────────────────────────────────────────────────────────────
    IEnumerator LoadRoutine(string scene, string t, string sub, System.Action whenCovered)
    {
        busy = true;
        yield return Cover(t, sub);
        try { whenCovered?.Invoke(); }
        catch (System.Exception e) { Debug.LogException(e); }

        GamePause.Clear();
        float shownAt = Time.unscaledTime;
        var op = SceneManager.LoadSceneAsync(scene);
        while (op != null && !op.isDone) yield return null;

        yield return RevealAfterLoad(t != null, shownAt);
    }

    IEnumerator CoverRoutine(string t, string sub)
    {
        busy = true;
        awaitingExternal = true;       // set BEFORE the wipe: a fast load may land mid-wipe
        externalLoaded   = false;
        float shownAt = Time.unscaledTime;
        yield return Cover(t, sub);
        // Safety: never leave the screen covered if the load never comes.
        float until = Time.unscaledTime + 20f;
        while (!externalLoaded && Time.unscaledTime < until) yield return null;
        awaitingExternal = false;
        yield return RevealAfterLoad(t != null, shownAt);
    }

    IEnumerator QuitRoutine()
    {
        busy = true;
        yield return Cover(null, null);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator Cover(string t, string sub)
    {
        canvas.enabled = true;
        MenuAudio.Play(Sfx.Whoosh, 0.8f);
        SetLoading(t, sub);
        loadGroup.alpha = 0f;

        float from = 1f, dur = 0.36f, e = 0f;
        while (e < dur)
        {
            e += Time.unscaledDeltaTime;
            SetPos(Mathf.Lerp(from, 0f, UITheme.EaseInOutCubic(e / dur)));
            yield return null;
        }
        SetPos(0f);

        if (t != null)
        {
            for (float a = 0f; a < 1f; a += Time.unscaledDeltaTime / 0.18f)
            { loadGroup.alpha = a; yield return null; }
            loadGroup.alpha = 1f;
        }
    }

    IEnumerator RevealAfterLoad(bool hadCard, float shownAt = -1f)
    {
        busy = true;
        // Let the new scene's Start() build (races build their whole world then).
        yield return null;
        yield return null;
        if (hadCard && shownAt > 0f)
            while (Time.unscaledTime - shownAt < 0.9f) yield return null;

        for (float a = loadGroup.alpha; a > 0f; a -= Time.unscaledDeltaTime / 0.15f)
        { loadGroup.alpha = a; yield return null; }
        loadGroup.alpha = 0f;

        float dur = 0.42f, e = 0f;
        while (e < dur)
        {
            e += Time.unscaledDeltaTime;
            SetPos(Mathf.Lerp(0f, -1f, UITheme.EaseInOutCubic(e / dur)));
            yield return null;
        }
        SetPos(1f);
        canvas.enabled = false;
        busy = false;
    }

    void Update()
    {
        // Entering Play mode in the editor doesn't raise sceneLoaded for the scene
        // that was already open — reveal it anyway after a few frames.
        if (bootReveal && Time.frameCount > 3)
        {
            bootReveal = false;
            StartCoroutine(RevealAfterLoad(false));
        }

        if (spinner != null && loadGroup != null && loadGroup.alpha > 0f)
            spinner.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 320f);
    }

    // ── Construction ──────────────────────────────────────────────────────────
    void SetPos(float p)
    {
        pos = p;
        if (wipe == null) return;
        float w = root.rect.width > 1f ? root.rect.width : 1920f;
        wipe.anchoredPosition = new Vector2(p * (w + 520f), 0f);
        canvas.enabled = p > -0.999f && p < 0.999f || busy;
    }

    void SetLoading(string t, string sub)
    {
        title.text    = t ?? "";
        subtitle.text = sub ?? "";
        tip.text      = t != null ? "<color=#B06BFF>TIP</color>   " + Tips[Random.Range(0, Tips.Length)] : "";
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        root = (RectTransform)transform;

        // The wipe: one wide slanted ink panel with accent edges both sides.
        wipe = NewRect("Wipe", root);
        wipe.anchorMin = wipe.anchorMax = new Vector2(0.5f, 0.5f);
        wipe.sizeDelta = new Vector2(3200f, 1300f);
        var ink = wipe.gameObject.AddComponent<Image>();
        ink.color = UITheme.Ink;
        ink.raycastTarget = true;             // swallow clicks mid-transition
        wipe.gameObject.AddComponent<UIShear>().shear = 0.32f;

        var tex = NewRect("Texture", wipe);
        Stretch(tex);
        var stripes = tex.gameObject.AddComponent<Image>();
        stripes.sprite = UITheme.Sprite("ui_stripes");
        stripes.type   = Image.Type.Tiled;
        stripes.color  = new Color(1f, 1f, 1f, 0.006f);   // linear space: tiny alpha reads visibly
        stripes.raycastTarget = false;
        tex.gameObject.AddComponent<UIShear>().shear = 0.32f;

        Edge(wipe, 0f,  18f, UITheme.Accent, 0f);
        Edge(wipe, 0f,   5f, UITheme.Cyan,   30f);
        Edge(wipe, 1f,  18f, UITheme.Accent, 0f);
        Edge(wipe, 1f,   5f, UITheme.Cyan,  -30f);

        // Loading card (bottom right) + tip (bottom left).
        var card = NewRect("Loading", root);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 0f);
        card.anchoredPosition = new Vector2(-90f, 80f);
        card.sizeDelta = new Vector2(900f, 140f);
        loadGroup = card.gameObject.AddComponent<CanvasGroup>();
        loadGroup.alpha = 0f;
        loadGroup.blocksRaycasts = false;

        spinner = NewRect("Spinner", card);
        spinner.anchorMin = spinner.anchorMax = new Vector2(1f, 0.5f);
        spinner.anchoredPosition = new Vector2(-30f, 10f);
        spinner.sizeDelta = new Vector2(60f, 60f);
        var sp = spinner.gameObject.AddComponent<Image>();
        sp.sprite = UITheme.Sprite("ui_ring_arc");
        sp.color  = UITheme.Accent;
        sp.raycastTarget = false;

        title    = Text(card, "Title", 44, UITheme.Text, FontStyles.Bold | FontStyles.Italic,
                        new Vector2(-90f, 36f), TextAlignmentOptions.Right);
        subtitle = Text(card, "Subtitle", 22, UITheme.TextDim, FontStyles.Normal,
                        new Vector2(-92f, -14f), TextAlignmentOptions.Right);
        subtitle.characterSpacing = 6f;

        var tipRT = NewRect("Tip", root);
        tipRT.anchorMin = tipRT.anchorMax = tipRT.pivot = new Vector2(0f, 0f);
        tipRT.anchoredPosition = new Vector2(90f, 90f);
        tipRT.sizeDelta = new Vector2(900f, 40f);
        tip = tipRT.gameObject.AddComponent<TextMeshProUGUI>();
        tip.fontSize = 22; tip.color = UITheme.TextDim; tip.raycastTarget = false;
        tip.alignment = TextAlignmentOptions.Left;
        tip.textWrappingMode = TextWrappingModes.NoWrap;
        tip.richText = true;
        tip.transform.SetParent(card, true);   // fades with the card

        UITheme.ApplyFont(gameObject);
        canvas.enabled = false;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void Edge(RectTransform parent, float side, float width, Color c, float inset)
    {
        var e = NewRect("Edge", parent);
        e.anchorMin = new Vector2(side, 0f);
        e.anchorMax = new Vector2(side, 1f);
        e.pivot     = new Vector2(side, 0.5f);
        e.anchoredPosition = new Vector2(inset, 0f);
        e.sizeDelta = new Vector2(width, 0f);
        var img = e.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        e.gameObject.AddComponent<UIShear>().shear = 0.32f;
    }

    static TextMeshProUGUI Text(RectTransform parent, string name, float size, Color c,
                                FontStyles style, Vector2 pos, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(820f, 56f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
