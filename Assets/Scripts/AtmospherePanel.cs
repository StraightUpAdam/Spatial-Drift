using UnityEngine;

/// Hidden live-tuning panel for the sky / fog / sun — press F8 in a time trial
/// (solo or multiplayer).  Sits next to F9 (drift zone) and F10 (drift feel).
///
/// Pick a preset (Day / Sunset / Night / Storm), then drag sliders; every change
/// re-dresses the LIVE scene instantly through SceneAmbiance.Redress().  SAVE
/// persists the values (persistentDataPath/atmosphere.json) — they're loaded on
/// every launch, in builds too.  RESET restores the built-in Day and deletes
/// the file.
[DisallowMultipleComponent]
public class AtmospherePanel : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F8;

    bool    open;
    Vector2 scroll;
    float   savedFlash;
    SceneAmbiance ambiance;

    GUIStyle _hdr, _lbl, _val, _btn;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) open = !open;
        if (savedFlash > 0f) savedFlash -= Time.deltaTime;
    }

    SceneAmbiance FindAmbiance()
    {
        if (ambiance == null) ambiance = FindFirstObjectByType<SceneAmbiance>();
        return ambiance;
    }

    void EnsureStyles()
    {
        if (_hdr != null) return;
        _hdr = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        _hdr.normal.textColor = new Color(0.55f, 0.80f, 1f);          // sky blue
        _lbl = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _lbl.normal.textColor = new Color(0.85f, 0.85f, 0.92f);
        _val = new GUIStyle(_lbl) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        _btn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
    }

    void OnGUI()
    {
        if (!open) return;
        EnsureStyles();

        const float w = 330f;
        // Centred column so it can sit open alongside F9 (left) and F10 (right).
        var area = new Rect((Screen.width - w) * 0.5f, 12, w, Screen.height - 24);
        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 10, area.y + 8, area.width - 20, area.height - 16));

        GUILayout.BeginHorizontal();
        GUILayout.Label("ATMOSPHERE  (F8)", _hdr);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("✕", _btn, GUILayout.Width(26))) open = false;
        GUILayout.EndHorizontal();

        scroll = GUILayout.BeginScrollView(scroll);

        // ── Presets ───────────────────────────────────────────────────────────
        GUILayout.Label("PRESET", _hdr);
        GUILayout.BeginHorizontal();
        foreach (var preset in Atmosphere.Presets)
        {
            if (GUILayout.Button(preset.name, _btn))
            {
                Atmosphere.Current = preset;
                FindAmbiance()?.Redress();
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        // ── Live values (any change re-dresses the scene immediately) ─────────
        var p = Atmosphere.Current;
        GUI.changed = false;

        GUILayout.Label("FOG", _hdr);
        p.fogDensity = F("Density ×1000", p.fogDensity * 1000f, 0.2f, 8f, "0.00") / 1000f;
        p.fog        = ColorRows(p.fog);

        GUILayout.Label("SUN", _hdr);
        p.sunIntensity = F("Intensity", p.sunIntensity, 0f, 2f, "0.00");
        p.sun          = ColorRows(p.sun);

        GUILayout.Label("CLOUDS", _hdr);
        p.cloudiness = F("Coverage",   p.cloudiness, 0f, 1f, "0.00");
        p.cloudDrift = F("Drift °/s",  p.cloudDrift, 0f, 2f, "0.00");
        p.cloudColor = ColorRows(p.cloudColor);

        GUILayout.Label("SKY TOP", _hdr);
        p.skyTop = ColorRows(p.skyTop);

        GUILayout.Label("SKY HORIZON", _hdr);
        p.skyHorizon = ColorRows(p.skyHorizon);

        GUILayout.Label("SKY BELOW HORIZON", _hdr);
        p.skyBottom = ColorRows(p.skyBottom);

        GUILayout.Label("AMBIENT LIGHT", _hdr);
        GUILayout.Label("(sky / horizon / ground fill light)", _lbl);
        p.ambientSky     = ColorRows(p.ambientSky);
        p.ambientEquator = ColorRows(p.ambientEquator);
        p.ambientGround  = ColorRows(p.ambientGround);

        if (GUI.changed)
        {
            Atmosphere.Current = p;
            FindAmbiance()?.Redress();
        }

        GUILayout.EndScrollView();

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(savedFlash > 0f ? "SAVED ✓" : "SAVE", _btn, GUILayout.Height(30)))
        {
            Atmosphere.SaveOverrides();
            savedFlash = 1.2f;
        }
        if (GUILayout.Button("RESET (Day)", _btn, GUILayout.Height(30)))
        {
            Atmosphere.ResetToDefaults();
            FindAmbiance()?.Redress();
        }
        GUILayout.EndHorizontal();
        GUILayout.Label(Atmosphere.HasSavedOverrides
                            ? "Override file active (loads on every launch)."
                            : "No override file — using built-in preset.", _lbl);

        GUILayout.EndArea();
    }

    // Labelled slider row for a float; shows the live value on the right.
    float F(string label, float value, float min, float max, string fmt)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _lbl, GUILayout.Width(104));
        float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
        GUILayout.Label(v.ToString(fmt), _val, GUILayout.Width(44));
        GUILayout.EndHorizontal();
        return v;
    }

    // Compact R / G / B slider triple with a live colour swatch on the right.
    Color ColorRows(Color c)
    {
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical();
        c.r = F("R", c.r, 0f, 1f, "0.00");
        c.g = F("G", c.g, 0f, 1f, "0.00");
        c.b = F("B", c.b, 0f, 1f, "0.00");
        GUILayout.EndVertical();
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = new Color(c.r, c.g, c.b, 1f);
        GUILayout.Box(GUIContent.none, GUILayout.Width(30), GUILayout.Height(56));
        GUI.backgroundColor = prev;
        GUILayout.EndHorizontal();
        return c;
    }
}
