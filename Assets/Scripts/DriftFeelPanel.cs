using UnityEngine;

/// Hidden live-tuning panel for the NORMAL drift (the racing slide, not the
/// drift-zone minigame — that one is F9).  Press F10 in a time trial (solo or
/// multiplayer) to open.
///
/// Edits the LOCAL car's drift fields directly so every change is felt on the
/// very next corner.  SAVE captures the whole car tune (CarTuning.CaptureFrom)
/// and persists it as a JSON override that CarController.Awake re-applies on
/// every launch — so tweaks made in a build stick.  RESET restores the asset's
/// values (and re-applies them to the live car).
[DisallowMultipleComponent]
public class DriftFeelPanel : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F10;

    bool    open;
    Vector2 scroll;
    float   savedFlash;
    CarController car;

    GUIStyle _hdr, _lbl, _val, _btn;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) open = !open;
        if (savedFlash > 0f) savedFlash -= Time.deltaTime;
    }

    CarController FindCar()
    {
        if (car != null) return car;
        car = CarController.LocalPlayerOverride;
        if (car == null)
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (!c.aiControlled) { car = c; break; }
        return car;
    }

    void EnsureStyles()
    {
        if (_hdr != null) return;
        _hdr = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        _hdr.normal.textColor = new Color(1f, 0.62f, 0.30f);          // amber — drift heat
        _lbl = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _lbl.normal.textColor = new Color(0.85f, 0.85f, 0.92f);
        _val = new GUIStyle(_lbl) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        _btn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
    }

    void OnGUI()
    {
        if (!open) return;
        EnsureStyles();

        var c = FindCar();
        const float w = 330f;
        // Right side so it can sit open alongside the F9 drift-zone panel.
        var area = new Rect(Screen.width - w - 12, 12, w, Screen.height - 24);
        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 10, area.y + 8, area.width - 20, area.height - 16));

        GUILayout.BeginHorizontal();
        GUILayout.Label("DRIFT FEEL  (F10)", _hdr);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("✕", _btn, GUILayout.Width(26))) open = false;
        GUILayout.EndHorizontal();

        if (c == null)
        {
            GUILayout.Label("No local car found yet…", _lbl);
            GUILayout.EndArea();
            return;
        }

        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("STYLE", _hdr);
        GUILayout.BeginHorizontal();
        bool slide = c.driftStyle == CarController.DriftStyle.Slide;
        if (GUILayout.Toggle(!slide, " Hop (Mario Kart)") && slide)
            c.driftStyle = CarController.DriftStyle.Hop;
        if (GUILayout.Toggle(slide, " Slide (Initial D)") && !slide)
            c.driftStyle = CarController.DriftStyle.Slide;
        GUILayout.EndHorizontal();

        GUILayout.Label("HOP DRIFT", _hdr);
        c.hopAngleMin      = F("Angle min",      c.hopAngleMin,      2f,  30f,  "0");
        c.hopAngleMax      = F("Angle max",      c.hopAngleMax,      10f, 60f,  "0");
        c.hopAngleResponse = F("Stick response", c.hopAngleResponse, 1f,  20f,  "0.0");
        c.hopTurnPerAngle  = F("Turn / angle",   c.hopTurnPerAngle,  1f,  8f,   "0.0");
        c.hopHeight        = F("Hop height",     c.hopHeight,        0f,  1.5f, "0.00");

        GUILayout.Label("SLIDE DRIFT", _hdr);
        c.driftSteerMultiplier = F("Steer ×",     c.driftSteerMultiplier, 1f,  3f,  "0.00");
        c.driftGrip            = F("Drift grip",  c.driftGrip,            1f,  15f, "0.0");
        c.driftEntryFlickDeg   = F("Entry flick", c.driftEntryFlickDeg,   0f,  30f, "0");
        c.maxSlideAngleDeg     = F("Max angle",   c.maxSlideAngleDeg,     20f, 90f, "0");
        c.driftSlideScrub      = F("Slide scrub", c.driftSlideScrub,      0f,  30f, "0.0");

        GUILayout.Label("SHARED", _hdr);
        c.driftSteerThreshold = F("Entry steer",  c.driftSteerThreshold, 0.05f, 0.5f, "0.00");
        c.driftMinSpeed       = F("Min speed",    c.driftMinSpeed,       5f,    50f,  "0");
        c.driftDrag           = F("Drift drag",   c.driftDrag,           0f,    30f,  "0.0");

        GUILayout.Label("RELEASE BOOST", _hdr);
        c.driftMinDuration   = F("Min charge t", c.driftMinDuration,   0.1f, 1f,  "0.00");
        c.driftMaxDuration   = F("Full charge t",c.driftMaxDuration,   0.5f, 3f,  "0.00");
        c.driftBoostMin      = F("Boost min",    c.driftBoostMin,      2f,   20f, "0");
        c.driftBoostMax      = F("Boost max",    c.driftBoostMax,      2f,   30f, "0");
        c.driftBoostDuration = F("Boost time",   c.driftBoostDuration, 0.1f, 2f,  "0.00");

        GUILayout.EndScrollView();

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(savedFlash > 0f ? "SAVED ✓" : "SAVE", _btn, GUILayout.Height(30)))
        {
            var tune = CarTuning.Load();
            if (tune != null)
            {
                tune.EnsureRuntimeOverrides();   // snapshot defaults before mutating
                tune.CaptureFrom(c);
                tune.SaveOverrides();
                savedFlash = 1.2f;
            }
        }
        if (GUILayout.Button("RESET DEFAULTS", _btn, GUILayout.Height(30)))
        {
            var tune = CarTuning.Load();
            if (tune != null)
            {
                tune.EnsureRuntimeOverrides();
                tune.ResetToAssetDefaults();
                tune.ApplyTo(c);                 // revert the live car too
            }
        }
        GUILayout.EndHorizontal();

        var t2 = CarTuning.Load();
        GUILayout.Label(t2 != null && t2.HasSavedOverrides
                            ? "Override file active (survives restarts)."
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
}
