using UnityEngine;

/// Hidden live-tuning panel for Spatial Drift — press F9 (in a time trial;
/// solo and multiplayer both spawn it) to open, F9 / close button to hide.
///
/// Edits the loaded SpatialDriftTuning instance in place, so every change
/// applies IMMEDIATELY (the next drift entry uses the new values — most take
/// effect even mid-run).  SAVE writes the values to a JSON override in
/// persistentDataPath that's re-applied on every launch (works in builds,
/// where the .asset can't be edited).  RESET restores the asset's values and
/// deletes the override.
[DisallowMultipleComponent]
public class DriftTuningPanel : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F9;

    bool    open;
    Vector2 scroll;
    float   savedFlash;
    SpatialDriftTuning t;

    GUIStyle _hdr, _lbl, _val, _btn;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) open = !open;
        if (savedFlash > 0f) savedFlash -= Time.deltaTime;
    }

    SpatialDriftTuning FindTuning()
    {
        if (t != null) return t;
        var car = CarController.LocalPlayerOverride;
        if (car == null)
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (!c.aiControlled) { car = c; break; }
        var sd = car != null ? car.GetComponent<SpatialDriftMode>() : null;
        t = sd != null ? sd.tuning : null;
        return t;
    }

    void EnsureStyles()
    {
        if (_hdr != null) return;
        _hdr = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        _hdr.normal.textColor = new Color(0.69f, 0.42f, 1f);
        _lbl = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _lbl.normal.textColor = new Color(0.85f, 0.85f, 0.92f);
        _val = new GUIStyle(_lbl) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        _btn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
    }

    void OnGUI()
    {
        if (!open) return;
        EnsureStyles();

        var tun = FindTuning();
        const float w = 330f;
        var area = new Rect(12, 12, w, Screen.height - 24);
        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 10, area.y + 8, area.width - 20, area.height - 16));

        GUILayout.BeginHorizontal();
        GUILayout.Label("DRIFT TUNING  (F9)", _hdr);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("✕", _btn, GUILayout.Width(26))) open = false;
        GUILayout.EndHorizontal();

        if (tun == null)
        {
            GUILayout.Label("No local car / tuning found yet…", _lbl);
            GUILayout.EndArea();
            return;
        }

        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("MINIGAME", _hdr);
        GUILayout.BeginHorizontal();
        bool rhythm = tun.minigame == SpatialDriftTuning.Minigame.Rhythm;
        if (GUILayout.Toggle(rhythm,  " Rhythm (DDR)") && !rhythm)
            tun.minigame = SpatialDriftTuning.Minigame.Rhythm;
        if (GUILayout.Toggle(!rhythm, " Rails (dodge)") && rhythm)
            tun.minigame = SpatialDriftTuning.Minigame.Rails;
        GUILayout.EndHorizontal();

        GUILayout.Label("SESSION", _hdr);
        tun.maxDuration      = F("Max time (0=∞)", tun.maxDuration,      0f, 30f, "0.0s");
        tun.rechargeDuration = F("Recharge",       tun.rechargeDuration, 0f, 30f, "0.0s");

        GUILayout.Label("AUTOPILOT", _hdr);
        tun.speedMultiplier = F("Speed ×",   tun.speedMultiplier, 1f,  2.5f, "0.00");
        tun.speedAdditive   = F("Speed +",   tun.speedAdditive,   0f,  80f,  "0");
        tun.minRPMToEnter   = F("Entry RPM", tun.minRPMToEnter,   0.5f, 1f,  "0.00");
        tun.requiresTopGear = GUILayout.Toggle(tun.requiresTopGear, " Requires top gear");
        tun.maxLives        = I("Lives", tun.maxLives, 1, 10);

        GUILayout.Label("RAILS", _hdr);
        tun.railsLaneSpacing        = F("Lane spacing",   tun.railsLaneSpacing,        2f,  8f,   "0.0");
        tun.railsMaxLanes           = I("Max rails",      tun.railsMaxLanes,           2,   6);
        tun.railsLaneSwitchSpeed    = F("Switch speed",   tun.railsLaneSwitchSpeed,    5f,  120f, "0");
        tun.railsObstacleSpacing    = F("Row gap start",  tun.railsObstacleSpacing,    15f, 200f, "0");
        tun.railsObstacleSpacingMin = F("Row gap end",    tun.railsObstacleSpacingMin, 10f, 120f, "0");
        tun.railsSpawnAhead         = F("Spawn ahead",    tun.railsSpawnAhead,         40f, 300f, "0");
        tun.railsScorePerDodge      = I("Score / dodge",  tun.railsScorePerDodge,      10,  500);

        GUILayout.Label("RHYTHM", _hdr);
        tun.noteSpawnInterval = F("Note interval", tun.noteSpawnInterval, 0.2f,  1.5f, "0.00");
        tun.noteLeadTime      = F("Lead time",     tun.noteLeadTime,      0.5f,  3f,   "0.00");
        tun.hitWindow         = F("Hit window",    tun.hitWindow,         0.05f, 0.4f, "0.00");
        tun.difficultyRampEnabled = GUILayout.Toggle(tun.difficultyRampEnabled, " Difficulty ramp");
        tun.rampDuration      = F("Ramp length",   tun.rampDuration,      5f,    120f, "0");

        GUILayout.Label("EXIT BOOST", _hdr);
        tun.exitBoostMin          = F("Boost min",   tun.exitBoostMin,          0f, 40f, "0");
        tun.exitBoostMax          = F("Boost max",   tun.exitBoostMax,          0f, 60f, "0");
        tun.postExitGraceDuration = F("Exit grace",  tun.postExitGraceDuration, 0f, 3f,  "0.0s");

        GUILayout.EndScrollView();

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(savedFlash > 0f ? "SAVED ✓" : "SAVE", _btn, GUILayout.Height(30)))
        {
            tun.SaveOverrides();
            savedFlash = 1.2f;
        }
        if (GUILayout.Button("RESET DEFAULTS", _btn, GUILayout.Height(30)))
            tun.ResetToAssetDefaults();
        GUILayout.EndHorizontal();
        GUILayout.Label(tun.HasSavedOverrides ? "Override file active (survives restarts)."
                                              : "No override file — using asset values.", _lbl);

        GUILayout.EndArea();
    }

    // Labelled slider row for a float; shows the live value on the right.
    float F(string label, float value, float min, float max, string fmt)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _lbl, GUILayout.Width(110));
        float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
        GUILayout.Label(v.ToString(fmt), _val, GUILayout.Width(48));
        GUILayout.EndHorizontal();
        return v;
    }

    // Labelled − / + stepper row for an int.
    int I(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _lbl, GUILayout.Width(110));
        if (GUILayout.Button("−", _btn, GUILayout.Width(26))) value--;
        GUILayout.Label(value.ToString(), _val, GUILayout.Width(40));
        if (GUILayout.Button("+", _btn, GUILayout.Width(26))) value++;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        return Mathf.Clamp(value, min, max);
    }
}
