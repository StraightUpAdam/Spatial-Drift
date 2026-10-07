using UnityEngine;
using UnityEngine.Rendering;

/// The game's sky / light / fog "weather" in one place.  The normal world is
/// REAL-LIFE OUTSIDE (blue sky, sunlight, atmospheric haze) — the purple void
/// look now belongs to the Spatial Drift zone only.
///
/// Everything reads Atmosphere.Current: SceneAmbiance (dome, fog, ambient),
/// the scene builders (sun), and TrackScenery (far-layer haze).  Swapping the
/// profile is one assignment, which is the seam for the planned day/night and
/// clear/stormy variation — set Current before a race scene loads (or call
/// PickRandom()) and the whole world dresses itself accordingly.
public static class Atmosphere
{
    [System.Serializable]
    public struct Profile
    {
        public string name;
        public Color  skyTop, skyHorizon, skyBottom;    // gradient dome
        public Color  fog;                              // also the scenery haze
        public float  fogDensity;                       // ExponentialSquared
        public Color  sun;
        public float  sunIntensity;
        public Vector3 sunEuler;
        public Color  ambientSky, ambientEquator, ambientGround;

        // Volumetric cloud layer (CloudLayer): sky coverage, tint, wind.
        public float  cloudiness;                       // 0 = clear, 1 = overcast
        public Color  cloudColor;
        public float  cloudDrift;                       // wind strength

        // Cars switch their headlights on in dark weathers.
        public bool   headlights;
    }

    /// True when cars should run headlights.  The sun-intensity fallback covers
    /// old saved override files from before the flag existed.
    public static bool HeadlightsOn =>
        Current.headlights || Current.sunIntensity <= 0.8f;

    public static readonly Profile Day = new Profile
    {
        name          = "Day",
        skyTop        = new Color(0.22f, 0.46f, 0.90f),
        skyHorizon    = new Color(0.66f, 0.79f, 0.93f),
        skyBottom     = new Color(0.45f, 0.52f, 0.48f),
        fog           = new Color(0.69f, 0.79f, 0.91f),
        fogDensity    = 0.0016f,
        sun           = new Color(1.00f, 0.96f, 0.88f),
        sunIntensity  = 1.25f,
        sunEuler      = new Vector3(48f, 50f, 0f),
        ambientSky    = new Color(0.60f, 0.70f, 0.86f),
        ambientEquator= new Color(0.55f, 0.56f, 0.52f),
        ambientGround = new Color(0.28f, 0.31f, 0.26f),
        cloudiness    = 0.45f,
        cloudColor    = new Color(1.00f, 0.99f, 0.97f),
        cloudDrift    = 0.25f,
    };

    public static readonly Profile Sunset = new Profile
    {
        name          = "Sunset",
        skyTop        = new Color(0.22f, 0.26f, 0.52f),
        skyHorizon    = new Color(0.98f, 0.62f, 0.35f),
        skyBottom     = new Color(0.30f, 0.24f, 0.28f),
        fog           = new Color(0.85f, 0.62f, 0.48f),
        fogDensity    = 0.0055f,
        sun           = new Color(1.00f, 0.72f, 0.45f),
        sunIntensity  = 1.0f,
        sunEuler      = new Vector3(14f, 70f, 0f),
        ambientSky    = new Color(0.48f, 0.42f, 0.60f),
        ambientEquator= new Color(0.62f, 0.45f, 0.38f),
        ambientGround = new Color(0.24f, 0.20f, 0.22f),
        cloudiness    = 0.55f,
        cloudColor    = new Color(1.00f, 0.70f, 0.52f),   // lit undersides
        cloudDrift    = 0.30f,
    };

    public static readonly Profile Night = new Profile
    {
        name          = "Night",
        skyTop        = new Color(0.02f, 0.03f, 0.08f),
        skyHorizon    = new Color(0.10f, 0.14f, 0.26f),
        skyBottom     = new Color(0.02f, 0.03f, 0.05f),
        fog           = new Color(0.06f, 0.09f, 0.16f),
        fogDensity    = 0.0060f,
        sun           = new Color(0.55f, 0.65f, 0.95f),   // moonlight
        sunIntensity  = 0.35f,
        sunEuler      = new Vector3(55f, -30f, 0f),
        ambientSky    = new Color(0.12f, 0.16f, 0.28f),
        ambientEquator= new Color(0.10f, 0.12f, 0.18f),
        ambientGround = new Color(0.04f, 0.05f, 0.08f),
        cloudiness    = 0.28f,
        cloudColor    = new Color(0.30f, 0.35f, 0.50f),   // moonlit wisps
        cloudDrift    = 0.18f,
        headlights    = true,
    };

    public static readonly Profile Storm = new Profile
    {
        name          = "Storm",
        skyTop        = new Color(0.30f, 0.33f, 0.38f),
        skyHorizon    = new Color(0.55f, 0.58f, 0.62f),
        skyBottom     = new Color(0.28f, 0.30f, 0.30f),
        fog           = new Color(0.52f, 0.55f, 0.60f),
        fogDensity    = 0.0085f,
        sun           = new Color(0.75f, 0.78f, 0.85f),
        sunIntensity  = 0.65f,
        sunEuler      = new Vector3(60f, 20f, 0f),
        ambientSky    = new Color(0.45f, 0.48f, 0.54f),
        ambientEquator= new Color(0.40f, 0.42f, 0.45f),
        ambientGround = new Color(0.22f, 0.24f, 0.25f),
        cloudiness    = 1.0f,
        cloudColor    = new Color(0.40f, 0.42f, 0.48f),   // heavy grey sheets
        cloudDrift    = 0.8f,
        headlights    = true,
    };

    /// The menus' own mood — violet dusk with a warm horizon, a low sun raking
    /// the plains and lit cloud undersides.  Menu cameras dress with this (their
    /// SceneAmbiance.menuMood is on) and never touch Current (the race weather).
    public static readonly Profile Menu = new Profile
    {
        name          = "Menu Dusk",
        skyTop        = new Color(0.10f, 0.08f, 0.26f),
        skyHorizon    = new Color(0.92f, 0.50f, 0.44f),
        skyBottom     = new Color(0.46f, 0.28f, 0.34f),
        fog           = new Color(0.80f, 0.47f, 0.47f),   // ≈ horizon, so the plains melt into the sky
        fogDensity    = 0.0046f,
        sun           = new Color(1.00f, 0.62f, 0.48f),
        sunIntensity  = 0.95f,
        sunEuler      = new Vector3(9f, 200f, 0f),
        ambientSky    = new Color(0.36f, 0.30f, 0.58f),
        ambientEquator= new Color(0.55f, 0.36f, 0.42f),
        ambientGround = new Color(0.16f, 0.12f, 0.18f),
        cloudiness    = 0.52f,
        cloudColor    = new Color(1.00f, 0.62f, 0.66f),
        cloudDrift    = 0.22f,
    };

    /// What every scene dresses itself with.  Change BEFORE a scene loads (or
    /// call SceneAmbiance.Redress() to re-dress a live scene — the F8 panel does).
    public static Profile Current = LoadInitial();

    public static readonly Profile[] Presets = { Day, Sunset, Night, Storm };

    /// The player's F8-saved sky if there is one, else Day — without touching
    /// Current (menu previews use this).
    public static Profile SavedOrDefault() => LoadInitial();

    /// Blend two profiles (menu weather previews cross-fade with this).
    public static Profile Lerp(Profile a, Profile b, float t)
    {
        t = Mathf.Clamp01(t);
        return new Profile
        {
            name           = t < 0.5f ? a.name : b.name,
            skyTop         = Color.Lerp(a.skyTop, b.skyTop, t),
            skyHorizon     = Color.Lerp(a.skyHorizon, b.skyHorizon, t),
            skyBottom      = Color.Lerp(a.skyBottom, b.skyBottom, t),
            fog            = Color.Lerp(a.fog, b.fog, t),
            fogDensity     = Mathf.Lerp(a.fogDensity, b.fogDensity, t),
            sun            = Color.Lerp(a.sun, b.sun, t),
            sunIntensity   = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
            sunEuler       = new Vector3(Mathf.LerpAngle(a.sunEuler.x, b.sunEuler.x, t),
                                         Mathf.LerpAngle(a.sunEuler.y, b.sunEuler.y, t),
                                         Mathf.LerpAngle(a.sunEuler.z, b.sunEuler.z, t)),
            ambientSky     = Color.Lerp(a.ambientSky, b.ambientSky, t),
            ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t),
            ambientGround  = Color.Lerp(a.ambientGround, b.ambientGround, t),
            cloudiness     = Mathf.Lerp(a.cloudiness, b.cloudiness, t),
            cloudColor     = Color.Lerp(a.cloudColor, b.cloudColor, t),
            cloudDrift     = Mathf.Lerp(a.cloudDrift, b.cloudDrift, t),
            headlights     = t < 0.5f ? a.headlights : b.headlights,
        };
    }

    /// Random weather per race (the solo picker's "Random" option uses this).
    public static void PickRandom() => Current = Presets[Random.Range(0, Presets.Length)];

    /// Restore whatever the player saved via the F8 panel (or Day if nothing
    /// saved) — the solo picker's "Saved" option.
    public static void ReloadSaved() => Current = LoadInitial();

    // ── Persistence (the F8 in-game panel saves/restores here) ────────────────
    static string OverridePath =>
        System.IO.Path.Combine(Application.persistentDataPath, "atmosphere.json");
    public static bool HasSavedOverrides => System.IO.File.Exists(OverridePath);

    static Profile LoadInitial()
    {
        try
        {
            if (System.IO.File.Exists(OverridePath))
            {
                var p = JsonUtility.FromJson<Profile>(System.IO.File.ReadAllText(OverridePath));
                // Override files saved before clouds existed deserialize with
                // zeroed cloud fields — backfill from Day so the sky isn't
                // silently empty forever.
                if (p.cloudColor.a <= 0f && p.cloudiness <= 0f)
                {
                    p.cloudiness = Day.cloudiness;
                    p.cloudColor = Day.cloudColor;
                    p.cloudDrift = Day.cloudDrift;
                }
                return p;
            }
        }
        catch { }
        return Day;
    }

    public static void SaveOverrides()
    {
        try { System.IO.File.WriteAllText(OverridePath, JsonUtility.ToJson(Current, true)); }
        catch (System.Exception e)
        { Debug.LogWarning("[Atmosphere] save failed: " + e.Message); }
    }

    public static void ResetToDefaults()
    {
        Current = Day;
        try { if (HasSavedOverrides) System.IO.File.Delete(OverridePath); } catch { }
    }

    /// Scene-wide light settings that don't live on a component.
    public static void ApplyAmbient() => ApplyAmbient(Current);

    public static void ApplyAmbient(Profile p)
    {
        RenderSettings.ambientMode          = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor      = p.ambientSky;
        RenderSettings.ambientEquatorColor  = p.ambientEquator;
        RenderSettings.ambientGroundColor   = p.ambientGround;
    }

    /// Dress a directional light as this profile's sun.  Soft shadows are a big
    /// part of not looking flat — every sun casts them.
    public static void ApplySun(Light sun) => ApplySun(sun, Current);

    public static void ApplySun(Light sun, Profile p)
    {
        if (sun == null) return;
        sun.type           = LightType.Directional;
        sun.color          = p.sun;
        sun.intensity      = p.sunIntensity;
        sun.shadows        = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.transform.rotation = Quaternion.Euler(p.sunEuler);
    }
}
