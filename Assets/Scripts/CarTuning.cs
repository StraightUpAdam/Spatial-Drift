using System.IO;
using UnityEngine;

/// THE single source of truth for how the car drives — acceleration, steering,
/// drift, walls, and the whole gear/boost curve.  Every car (player, AI, and
/// networked) loads this on Awake and applies it, so one asset tunes the entire
/// game "across the board" instead of each scene's car carrying its own copy.
///
/// Why a ScriptableObject: values edited on a CarController/GearSystem in Play
/// mode revert when you stop, and values edited per-scene only affect that
/// scene.  An asset persists and applies everywhere.  Exactly the same approach
/// as SpatialDriftTuning and VfxTuning.
///
/// Workflow:
///   1. "Racing Game ▸ Create Car Tuning Asset"  (seeds Assets/Resources/CarTuning.asset)
///   2. Tune the car you like in a scene, then "Racing Game ▸ Capture Car Tuning
///      From Current Scene" to bake those values into the asset — or just edit
///      the asset directly in the Inspector.
///
/// TIRES / LOADOUTS (future): this is the *base* tune.  A per-player loadout
/// (different tires, etc.) will Clone() this and nudge a few fields, then call
/// ApplyTo() — so the modifier layer has exactly one choke point to hook.
[CreateAssetMenu(fileName = "CarTuning", menuName = "Racing Game/Car Tuning")]
public class CarTuning : ScriptableObject
{
    [Header("Driving Feel")]
    public float accelerationScale = 1f;
    public float brakePower        = 5f;
    public float coastDrag         = 1.2f;

    [Header("Steering")]
    public float steerRateDeg            = 75f;
    public float minSteerSpeed           = 1.5f;
    [Range(0f, 2f)] public float bankSteeringMultiplier = 0.6f;
    [Tooltip("Keyboard steer ramp rate (analog-stick feel); gamepad passes through raw.")]
    [Range(0f, 30f)] public float keyboardSteerSmoothRate = 16f;

    [Header("Drift Style")]
    [Tooltip("Slide = free-steer Initial-D slide.  Hop = Mario-Kart: flick locks " +
             "the direction, the stick widens/tightens the arc, release boosts.")]
    public CarController.DriftStyle driftStyle = CarController.DriftStyle.Hop;

    [Header("Hop Drift (Mario-Kart feel)")]
    [Range(2f, 30f)]    public float hopAngleMin      = 8f;
    [Range(10f, 60f)]   public float hopAngleMax      = 32f;
    [Range(1f, 20f)]    public float hopAngleResponse = 7f;
    [Range(1f, 8f)]     public float hopTurnPerAngle  = 3.6f;
    [Range(0f, 1.5f)]   public float hopHeight        = 0.45f;
    [Range(0.1f, 0.8f)] public float hopDuration      = 0.28f;

    [Header("Drift (brake + steer)")]
    public bool  driftEnabled            = true;
    [Range(0.05f, 0.5f)] public float driftSteerThreshold  = 0.20f;
    [Range(5f, 50f)]     public float driftMinSpeed        = 12f;
    [Range(1f, 3f)]      public float driftSteerMultiplier = 1.7f;
    [Range(0.1f, 1f)]    public float driftMinDuration     = 0.4f;
    [Range(0.5f, 3f)]    public float driftMaxDuration     = 1.5f;
    [Range(2f, 20f)]     public float driftBoostMin        = 5f;
    [Range(2f, 30f)]     public float driftBoostMax        = 14f;
    [Range(0.1f, 2f)]    public float driftBoostDuration   = 0.55f;
    [Range(0f, 30f)]     public float driftDrag            = 0.5f;

    [Header("Drift Slide (Initial-D feel)")]
    [Range(5f, 40f)] public float normalGrip         = 24f;
    [Range(1f, 15f)] public float driftGrip          = 3f;
    [Range(0f, 30f)] public float driftEntryFlickDeg = 8f;
    [Range(20f, 90f)] public float maxSlideAngleDeg  = 55f;
    [Range(0f, 30f)] public float driftSlideScrub    = 6f;

    [Header("Walls")]
    [Range(0f, 1f)] public float wallSpeedRetained = 0.8f;
    [Range(0f, 1f)] public float wallBounce        = 0.35f;

    [Header("Assists")]
    [Range(0f, 5f)] public float centerMagnet           = 0f;
    public bool  autoDownshiftEnabled                   = true;
    [Range(0.5f, 1f)] public float autoDownshiftThreshold = 0.85f;

    [Header("Gamepad")]
    [Range(0f, 0.4f)]    public float gamepadDeadzone        = 0.12f;
    [Range(0.05f, 0.9f)] public float gamepadBrakeThreshold  = 0.15f;

    [Header("Gears — Max Speed (m/s)")]
    public float[] gearMaxSpeeds = { 15f, 27f, 40f, 54f, 68f, 82f, 97f, 112f, 128f, 145f };

    [Header("Gears — Acceleration (m/s²)")]
    public float[] gearAcceleration = { 42f, 36f, 29f, 23f, 18f, 13f, 10f, 7.5f, 5.5f, 4f };

    [Header("Gears — Boost")]
    public BoostZone[] boostZones =
    {
        new BoostZone { minRPM = 0.63f, maxRPM = 0.72f, boostSpeed = 2.5f, label = "GOOD"    },
        new BoostZone { minRPM = 0.72f, maxRPM = 0.81f, boostSpeed = 4.5f, label = "GREAT"   },
        new BoostZone { minRPM = 0.81f, maxRPM = 0.87f, boostSpeed = 8.0f, label = "PERFECT" },
        new BoostZone { minRPM = 0.87f, maxRPM = 0.94f, boostSpeed = 4.5f, label = "GREAT"   },
        new BoostZone { minRPM = 0.94f, maxRPM = 0.99f, boostSpeed = 2.5f, label = "GOOD"    },
    };
    public float boostDuration = 0.45f;
    [Range(0f, 0.5f)] public float powerbandEntry = 0f;
    [Range(0.05f, 1f)] public float minEfficiency = 1f;

    /// Loader used by CarController on Awake.  Returns the asset in Resources, or
    /// null if it hasn't been created (cars then keep their in-code defaults).
    public static CarTuning Load() => Resources.Load<CarTuning>("CarTuning");

    // ── Runtime overrides (F10 in-game panel) ─────────────────────────────────
    // Same pattern as SpatialDriftTuning: the panel edits the live car, SAVE
    // captures + persists the whole tune as JSON, and every CarController.Awake
    // re-applies it — so build-time tweaks survive restarts.  RESET restores the
    // asset's shipped values and deletes the file.
    static string OverridePath => Path.Combine(Application.persistentDataPath, "car_tuning.json");
    string assetDefaultsJson;
    public bool HasSavedOverrides => File.Exists(OverridePath);

    public void EnsureRuntimeOverrides()
    {
        if (assetDefaultsJson != null) return;
        assetDefaultsJson = JsonUtility.ToJson(this);
        try
        {
            if (File.Exists(OverridePath))
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(OverridePath), this);
                Debug.Log("[CarTuning] Applied saved overrides from " + OverridePath);
            }
        }
        catch (System.Exception e)
        { Debug.LogWarning("[CarTuning] override load failed: " + e.Message); }
    }

    public void SaveOverrides()
    {
        try { File.WriteAllText(OverridePath, JsonUtility.ToJson(this, true)); }
        catch (System.Exception e)
        { Debug.LogWarning("[CarTuning] override save failed: " + e.Message); }
    }

    public void ResetToAssetDefaults()
    {
        if (!string.IsNullOrEmpty(assetDefaultsJson))
            JsonUtility.FromJsonOverwrite(assetDefaultsJson, this);
        try { if (File.Exists(OverridePath)) File.Delete(OverridePath); } catch { }
    }

    // ── Apply (one choke point — a future tire/loadout layer hooks here) ──────
    public void ApplyTo(CarController c)
    {
        if (c == null) return;
        c.accelerationScale       = accelerationScale;
        c.brakePower              = brakePower;
        c.coastDrag               = coastDrag;
        c.steerRateDeg            = steerRateDeg;
        c.minSteerSpeed           = minSteerSpeed;
        c.bankSteeringMultiplier  = bankSteeringMultiplier;
        c.keyboardSteerSmoothRate = keyboardSteerSmoothRate;
        c.driftStyle              = driftStyle;
        c.hopAngleMin             = hopAngleMin;
        c.hopAngleMax             = hopAngleMax;
        c.hopAngleResponse        = hopAngleResponse;
        c.hopTurnPerAngle         = hopTurnPerAngle;
        c.hopHeight               = hopHeight;
        c.hopDuration             = hopDuration;
        c.driftEnabled            = driftEnabled;
        c.driftSteerThreshold     = driftSteerThreshold;
        c.driftMinSpeed           = driftMinSpeed;
        c.driftSteerMultiplier    = driftSteerMultiplier;
        c.driftMinDuration        = driftMinDuration;
        c.driftMaxDuration        = driftMaxDuration;
        c.driftBoostMin           = driftBoostMin;
        c.driftBoostMax           = driftBoostMax;
        c.driftBoostDuration      = driftBoostDuration;
        c.driftDrag               = driftDrag;
        c.normalGrip              = normalGrip;
        c.driftGrip               = driftGrip;
        c.driftEntryFlickDeg      = driftEntryFlickDeg;
        c.maxSlideAngleDeg        = maxSlideAngleDeg;
        c.driftSlideScrub         = driftSlideScrub;
        c.wallSpeedRetained       = wallSpeedRetained;
        c.wallBounce              = wallBounce;
        c.centerMagnet            = centerMagnet;
        c.autoDownshiftEnabled    = autoDownshiftEnabled;
        c.autoDownshiftThreshold  = autoDownshiftThreshold;
        c.gamepadDeadzone         = gamepadDeadzone;
        c.gamepadBrakeThreshold   = gamepadBrakeThreshold;
    }

    public void ApplyTo(GearSystem g)
    {
        if (g == null) return;
        if (gearMaxSpeeds    != null && gearMaxSpeeds.Length    > 0) g.gearMaxSpeeds    = (float[])gearMaxSpeeds.Clone();
        if (gearAcceleration != null && gearAcceleration.Length > 0) g.gearAcceleration = (float[])gearAcceleration.Clone();
        if (boostZones       != null && boostZones.Length       > 0) g.boostZones       = (BoostZone[])boostZones.Clone();
        g.boostDuration  = boostDuration;
        g.powerbandEntry = powerbandEntry;
        g.minEfficiency  = minEfficiency;
    }

    /// Read the values off a live car (and its gear system) INTO this asset —
    /// backing the "Capture From Current Scene" editor menu.
    public void CaptureFrom(CarController c)
    {
        if (c == null) return;
        accelerationScale       = c.accelerationScale;
        brakePower              = c.brakePower;
        coastDrag               = c.coastDrag;
        steerRateDeg            = c.steerRateDeg;
        minSteerSpeed           = c.minSteerSpeed;
        bankSteeringMultiplier  = c.bankSteeringMultiplier;
        keyboardSteerSmoothRate = c.keyboardSteerSmoothRate;
        driftStyle              = c.driftStyle;
        hopAngleMin             = c.hopAngleMin;
        hopAngleMax             = c.hopAngleMax;
        hopAngleResponse        = c.hopAngleResponse;
        hopTurnPerAngle         = c.hopTurnPerAngle;
        hopHeight               = c.hopHeight;
        hopDuration             = c.hopDuration;
        driftEnabled            = c.driftEnabled;
        driftSteerThreshold     = c.driftSteerThreshold;
        driftMinSpeed           = c.driftMinSpeed;
        driftSteerMultiplier    = c.driftSteerMultiplier;
        driftMinDuration        = c.driftMinDuration;
        driftMaxDuration        = c.driftMaxDuration;
        driftBoostMin           = c.driftBoostMin;
        driftBoostMax           = c.driftBoostMax;
        driftBoostDuration      = c.driftBoostDuration;
        driftDrag               = c.driftDrag;
        normalGrip              = c.normalGrip;
        driftGrip               = c.driftGrip;
        driftEntryFlickDeg      = c.driftEntryFlickDeg;
        maxSlideAngleDeg        = c.maxSlideAngleDeg;
        driftSlideScrub         = c.driftSlideScrub;
        wallSpeedRetained       = c.wallSpeedRetained;
        wallBounce              = c.wallBounce;
        centerMagnet            = c.centerMagnet;
        autoDownshiftEnabled    = c.autoDownshiftEnabled;
        autoDownshiftThreshold  = c.autoDownshiftThreshold;
        gamepadDeadzone         = c.gamepadDeadzone;
        gamepadBrakeThreshold   = c.gamepadBrakeThreshold;

        var g = c.gearSystem != null ? c.gearSystem : c.GetComponent<GearSystem>();
        if (g != null)
        {
            if (g.gearMaxSpeeds    != null) gearMaxSpeeds    = (float[])g.gearMaxSpeeds.Clone();
            if (g.gearAcceleration != null) gearAcceleration = (float[])g.gearAcceleration.Clone();
            if (g.boostZones       != null) boostZones       = (BoostZone[])g.boostZones.Clone();
            boostDuration  = g.boostDuration;
            powerbandEntry = g.powerbandEntry;
            minEfficiency  = g.minEfficiency;
        }
    }
}
