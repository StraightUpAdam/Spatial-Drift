using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// OPTIONS — four tabbed pages, all baked; this wires them to GameSettings.
///   DISPLAY  : resolution, display mode, v-sync, frame limit, quality, AA
///   AUDIO    : master, music, effects, interface
///   GAMEPLAY : camera FOV, speed units, minimap, time-trial ghost
///   CONTROLS : reference card (keyboard + controller)
/// LB/RB (Q/E) or a click switches tabs; the right-hand card explains the
/// focused setting.  Every change applies (and saves) immediately.
[DisallowMultipleComponent]
public class OptionsMenu : MonoBehaviour
{
    [Header("Tabs (baked)")]
    public MenuScreen[]      pages;          // DISPLAY, AUDIO, GAMEPLAY, CONTROLS
    public TextMeshProUGUI[] tabLabels;
    public RectTransform     tabUnderline;

    [Header("Display (baked)")]
    public MenuStepper resolutionRow, displayModeRow, vsyncRow, frameLimitRow, qualityRow, aaRow;
    [Header("Audio (baked)")]
    public MenuSlider masterRow, musicRow, effectsRow, interfaceRow;
    [Header("Gameplay (baked)")]
    public MenuSlider  fovRow;
    public MenuStepper unitsRow, minimapRow, ghostRow;

    [Header("Chrome (baked)")]
    public TextMeshProUGUI descTitle, descBody;
    public MenuModal       modal;
    public PromptBar       prompts;

    public string menuScene = "MainMenu";

    static readonly string[] OffOn = { "OFF", "ON" };
    static readonly int[] FrameCaps = { 30, 60, 120, 144, 165, 240, 0 };
    static readonly FullScreenMode[] Modes =
        { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

    readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    int tab;

    void Start()
    {
        for (int i = 0; i < pages.Length; i++)
        {
            var p = pages[i];
            p.showOnStart = false;
            p.Backed       += () => SceneFader.Load(menuScene);
            p.TabLeft      += () => SwitchTab(tab - 1);
            p.TabRight     += () => SwitchTab(tab + 1);
            p.Alt2         += AskReset;
            p.FocusChanged += Describe;
        }
        for (int i = 0; i < tabLabels.Length; i++)
        {
            if (tabLabels[i] == null) continue;
            tabLabels[i].raycastTarget = true;
            var click = tabLabels[i].gameObject.GetComponent<OptionsTabClick>();
            if (click == null) click = tabLabels[i].gameObject.AddComponent<OptionsTabClick>();
            click.owner = this;
            click.index = i;
        }

        WireDisplay();
        WireAudio();
        WireGameplay();
        LoadValues();

        prompts?.Set((MenuAction.TabLeft, ""), (MenuAction.TabRight, "TAB"),
                     (MenuAction.Adjust, "CHANGE"), (MenuAction.Alt2, "DEFAULTS"),
                     (MenuAction.Back, "BACK"));

        SwitchTab(0, silent: true);
    }

    // ── Tabs ─────────────────────────────────────────────────────────────────
    public void SwitchTab(int t, bool silent = false)
    {
        t = (t % pages.Length + pages.Length) % pages.Length;
        if (!silent && t == tab && pages[t].IsOpen) return;
        for (int i = 0; i < pages.Length; i++) if (i != t && pages[i].IsOpen) pages[i].Hide();
        tab = t;
        pages[t].Show(0);
        if (!silent) MenuAudio.Play(Sfx.Toggle);
        Describe(pages[t].Focused);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < tabLabels.Length; i++)
            if (tabLabels[i] != null)
                tabLabels[i].color = Color.Lerp(tabLabels[i].color, i == tab ? UITheme.Text : UITheme.TextFaint,
                                                1f - Mathf.Exp(-14f * dt));
        if (tabUnderline != null && tab < tabLabels.Length && tabLabels[tab] != null)
        {
            var target = tabLabels[tab].rectTransform;
            var p = tabUnderline.anchoredPosition;
            tabUnderline.anchoredPosition = new Vector2(Mathf.Lerp(p.x, target.anchoredPosition.x, 1f - Mathf.Exp(-16f * dt)), p.y);
            var s = tabUnderline.sizeDelta;
            float w = tabLabels[tab].preferredWidth + 24f;
            tabUnderline.sizeDelta = new Vector2(Mathf.Lerp(s.x, w, 1f - Mathf.Exp(-16f * dt)), s.y);
        }
    }

    void Describe(MenuRow row)
    {
        if (descTitle == null || descBody == null) return;
        if (row == null)
        {
            descTitle.text = tab == 3 ? "CONTROLS" : "";
            descBody.text  = tab == 3 ? "Every menu works with a controller, the keyboard or the mouse." : "";
            return;
        }
        descTitle.text = row.label != null ? row.label.text : "";
        descBody.text  = row.hint;
    }

    // ── Display ──────────────────────────────────────────────────────────────
    void WireDisplay()
    {
        resolutions.Clear();
        foreach (var r in Screen.resolutions)
        {
            var v = new Vector2Int(r.width, r.height);
            if (v.x >= 1024 && !resolutions.Contains(v)) resolutions.Add(v);
        }
        var cur = new Vector2Int(Screen.width, Screen.height);
        if (!resolutions.Contains(cur)) resolutions.Add(cur);
        resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var resNames = new List<string>();
        foreach (var r in resolutions) resNames.Add($"{r.x} × {r.y}");
        resolutionRow.SetOptions(resNames, 0);
        resolutionRow.wrap = false;
        resolutionRow.Changed += i => GameSettings.Resolution = resolutions[i];

        displayModeRow.SetOptions(new[] { "FULLSCREEN", "BORDERLESS", "WINDOWED" }, 1);
        displayModeRow.Changed += i => GameSettings.DisplayMode = Modes[i];

        vsyncRow.SetOptions(OffOn, 1);
        vsyncRow.Changed += i => { GameSettings.VSync = i == 1; frameLimitRow.SetInteractable(i == 0); };

        var caps = new List<string>();
        foreach (int c in FrameCaps) caps.Add(c == 0 ? "UNLIMITED" : $"{c} FPS");
        frameLimitRow.SetOptions(caps, caps.Count - 1);
        frameLimitRow.Changed += i => GameSettings.FrameLimit = FrameCaps[i];

        var q = new List<string>();
        foreach (var n in QualitySettings.names) q.Add(n.ToUpperInvariant());
        qualityRow.SetOptions(q, 0);
        qualityRow.Changed += i => GameSettings.QualityIndex = i;

        aaRow.SetOptions(new[] { "OFF", "SMAA" }, 1);
        aaRow.Changed += i => GameSettings.AntiAliasing = i == 1;
    }

    // ── Audio ────────────────────────────────────────────────────────────────
    void WireAudio()
    {
        masterRow.Changed    += v => GameSettings.MasterVolume  = v;
        musicRow.Changed     += v => GameSettings.MusicVolume   = v;
        effectsRow.Changed   += v => GameSettings.EffectsVolume = v;
        interfaceRow.Changed += v => { GameSettings.UiVolume = v; };
    }

    // ── Gameplay ─────────────────────────────────────────────────────────────
    void WireGameplay()
    {
        fovRow.min = 55f; fovRow.max = 100f; fovRow.step = 1f;
        fovRow.percent = false; fovRow.format = "{0:0}°";
        fovRow.Changed += v => GameSettings.CameraFov = v;

        unitsRow.SetOptions(new[] { "KM/H", "MPH" }, 0);
        unitsRow.Changed += i => GameSettings.SpeedMph = i == 1;
        minimapRow.SetOptions(OffOn, 1);
        minimapRow.Changed += i => GameSettings.ShowMinimap = i == 1;
        ghostRow.SetOptions(OffOn, 1);
        ghostRow.Changed += i => GameSettings.ShowGhost = i == 1;
    }

    void LoadValues()
    {
        var res = GameSettings.Resolution;
        var want = res.x > 0 ? res : new Vector2Int(Screen.width, Screen.height);
        int ri = resolutions.IndexOf(want);
        resolutionRow.SetIndex(ri >= 0 ? ri : resolutions.Count - 1);
        int mi = System.Array.IndexOf(Modes, GameSettings.DisplayMode);
        displayModeRow.SetIndex(mi >= 0 ? mi : 1);
        vsyncRow.SetIndex(GameSettings.VSync ? 1 : 0);
        int fi = System.Array.IndexOf(FrameCaps, GameSettings.FrameLimit);
        frameLimitRow.SetIndex(fi >= 0 ? fi : FrameCaps.Length - 1);
        frameLimitRow.SetInteractable(!GameSettings.VSync);
        qualityRow.SetIndex(Mathf.Clamp(GameSettings.QualityIndex, 0, QualitySettings.names.Length - 1));
        aaRow.SetIndex(GameSettings.AntiAliasing ? 1 : 0);

        masterRow.SetValue(GameSettings.MasterVolume);
        musicRow.SetValue(GameSettings.MusicVolume);
        effectsRow.SetValue(GameSettings.EffectsVolume);
        interfaceRow.SetValue(GameSettings.UiVolume);

        fovRow.SetValue(GameSettings.CameraFov);
        unitsRow.SetIndex(GameSettings.SpeedMph ? 1 : 0);
        minimapRow.SetIndex(GameSettings.ShowMinimap ? 1 : 0);
        ghostRow.SetIndex(GameSettings.ShowGhost ? 1 : 0);
    }

    void AskReset()
    {
        if (modal == null) { GameSettings.ResetAll(); LoadValues(); return; }
        modal.Ask("RESET OPTIONS?", "Audio, gameplay and frame settings go back to their defaults.",
                  "RESET", "CANCEL", () => { GameSettings.ResetAll(); LoadValues(); MenuAudio.Play(Sfx.Confirm); });
    }
}

/// Click a tab label to switch pages.
public class OptionsTabClick : MonoBehaviour, IPointerClickHandler
{
    public OptionsMenu owner;
    public int index;
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && owner != null) owner.SwitchTab(index);
    }
}
