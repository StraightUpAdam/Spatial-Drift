using UnityEngine;

/// Persistent player settings — PlayerPrefs-backed, applied at boot (and on
/// every change).  The Options menu and the in-race pause settings write
/// these; gameplay reads them live:
///   Display  — resolution, display mode, v-sync, frame limit, quality, AA
///   Audio    — master (AudioListener), music, effects (cars), interface
///   Gameplay — camera FOV, speed units, minimap, time-trial ghost
public static class GameSettings
{
    const string MASTER_VOLUME_KEY = "MasterVolume";
    const string MUSIC_KEY         = "spatialdrift.music";
    const string EFFECTS_KEY       = "spatialdrift.effects";
    const string UI_KEY            = "spatialdrift.ui";
    const string QUALITY_KEY       = "spatialdrift.quality";
    const string FULLSCREEN_KEY    = "spatialdrift.fullscreen";   // legacy bool
    const string DISPLAYMODE_KEY   = "spatialdrift.displaymode";
    const string RES_W_KEY         = "spatialdrift.res_w";
    const string RES_H_KEY         = "spatialdrift.res_h";
    const string VSYNC_KEY         = "spatialdrift.vsync";
    const string FPS_KEY           = "spatialdrift.fps";
    const string AA_KEY            = "spatialdrift.aa";
    const string FOV_KEY           = "spatialdrift.fov";
    const string MPH_KEY           = "spatialdrift.mph";
    const string MINIMAP_KEY       = "spatialdrift.minimap";
    const string GHOST_KEY         = "spatialdrift.ghost";

    /// Fires after any setting changes (live views re-read what they need).
    public static event System.Action Changed;

    static float GetF(string k, float d) => PlayerPrefs.GetFloat(k, d);
    static void  SetF(string k, float v) { PlayerPrefs.SetFloat(k, v); PlayerPrefs.Save(); Changed?.Invoke(); }
    static bool  GetB(string k, bool d)  => PlayerPrefs.GetInt(k, d ? 1 : 0) == 1;
    static void  SetB(string k, bool v)  { PlayerPrefs.SetInt(k, v ? 1 : 0); PlayerPrefs.Save(); Changed?.Invoke(); }

    // ── Audio ─────────────────────────────────────────────────────────────────
    public static float MasterVolume
    {
        get => GetF(MASTER_VOLUME_KEY, 0.8f);
        set { PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, Mathf.Clamp01(value)); PlayerPrefs.Save(); ApplyAudio(); Changed?.Invoke(); }
    }
    public static float MusicVolume   { get => GetF(MUSIC_KEY, 0.55f); set => SetF(MUSIC_KEY, Mathf.Clamp01(value)); }
    public static float EffectsVolume { get => GetF(EFFECTS_KEY, 1f);  set => SetF(EFFECTS_KEY, Mathf.Clamp01(value)); }
    public static float UiVolume      { get => GetF(UI_KEY, 0.8f);     set => SetF(UI_KEY, Mathf.Clamp01(value)); }

    // ── Display ───────────────────────────────────────────────────────────────
    /// Index into QualitySettings.names.
    public static int QualityIndex
    {
        get => PlayerPrefs.GetInt(QUALITY_KEY, QualitySettings.GetQualityLevel());
        set
        {
            int n = QualitySettings.names.Length;
            PlayerPrefs.SetInt(QUALITY_KEY, Mathf.Clamp(value, 0, n - 1));
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }
    }

    public static FullScreenMode DisplayMode
    {
        get
        {
            if (PlayerPrefs.HasKey(DISPLAYMODE_KEY))
                return (FullScreenMode)PlayerPrefs.GetInt(DISPLAYMODE_KEY);
            bool legacyFull = PlayerPrefs.GetInt(FULLSCREEN_KEY, Screen.fullScreen ? 1 : 0) == 1;
            return legacyFull ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }
        set { PlayerPrefs.SetInt(DISPLAYMODE_KEY, (int)value); PlayerPrefs.Save(); ApplyDisplay(); Changed?.Invoke(); }
    }

    /// Chosen resolution; (0,0) = keep whatever the window currently is.
    public static Vector2Int Resolution
    {
        get => new Vector2Int(PlayerPrefs.GetInt(RES_W_KEY, 0), PlayerPrefs.GetInt(RES_H_KEY, 0));
        set
        {
            PlayerPrefs.SetInt(RES_W_KEY, value.x);
            PlayerPrefs.SetInt(RES_H_KEY, value.y);
            PlayerPrefs.Save();
            ApplyDisplay();
            Changed?.Invoke();
        }
    }

    public static bool VSync
    {
        get => GetB(VSYNC_KEY, true);
        set { PlayerPrefs.SetInt(VSYNC_KEY, value ? 1 : 0); PlayerPrefs.Save(); Apply(); Changed?.Invoke(); }
    }

    /// Frame cap when v-sync is off (0 = unlimited).
    public static int FrameLimit
    {
        get => PlayerPrefs.GetInt(FPS_KEY, 0);
        set { PlayerPrefs.SetInt(FPS_KEY, Mathf.Max(0, value)); PlayerPrefs.Save(); Apply(); Changed?.Invoke(); }
    }

    public static bool AntiAliasing { get => GetB(AA_KEY, true); set => SetB(AA_KEY, value); }

    // ── Gameplay ──────────────────────────────────────────────────────────────
    public static float CameraFov   { get => GetF(FOV_KEY, 70f);      set => SetF(FOV_KEY, Mathf.Clamp(value, 55f, 100f)); }
    public static bool  SpeedMph    { get => GetB(MPH_KEY, false);    set => SetB(MPH_KEY, value); }
    public static bool  ShowMinimap { get => GetB(MINIMAP_KEY, true); set => SetB(MINIMAP_KEY, value); }
    public static bool  ShowGhost   { get => GetB(GHOST_KEY, true);   set => SetB(GHOST_KEY, value); }

    /// Speed in the player's units, rounded for display.
    public static int DisplaySpeed(float metresPerSecond) =>
        Mathf.RoundToInt(metresPerSecond * (SpeedMph ? 2.23694f : 3.6f));
    public static string SpeedUnit => SpeedMph ? "mph" : "km/h";

    // ── Apply ─────────────────────────────────────────────────────────────────
    /// Push every stored setting onto the engine.  Safe to call any time.
    public static void Apply()
    {
        ApplyAudio();

        int q = Mathf.Clamp(QualityIndex, 0, QualitySettings.names.Length - 1);
        if (QualitySettings.GetQualityLevel() != q)
            QualitySettings.SetQualityLevel(q, applyExpensiveChanges: true);

        // vSyncCount is a quality-level setting — re-assert AFTER quality.
        QualitySettings.vSyncCount  = VSync ? 1 : 0;
        Application.targetFrameRate = VSync || FrameLimit <= 0 ? -1 : FrameLimit;

        ApplyDisplay();
    }

    static void ApplyAudio() => AudioListener.volume = MasterVolume;

    static void ApplyDisplay()
    {
#if !UNITY_EDITOR
        var mode = DisplayMode;
        var res  = Resolution;
        int w = res.x > 0 ? res.x : Screen.width;
        int h = res.y > 0 ? res.y : Screen.height;
        if (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode)
            Screen.SetResolution(w, h, mode);
#endif
    }

    /// Restore audio/gameplay/sync options to defaults (Options ▸ reset).
    public static void ResetAll()
    {
        foreach (var k in new[] { MASTER_VOLUME_KEY, MUSIC_KEY, EFFECTS_KEY, UI_KEY, VSYNC_KEY,
                                  FPS_KEY, AA_KEY, FOV_KEY, MPH_KEY, MINIMAP_KEY, GHOST_KEY })
            PlayerPrefs.DeleteKey(k);
        PlayerPrefs.Save();
        Apply();
        Changed?.Invoke();
    }
}
