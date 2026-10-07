using System.IO;
using UnityEngine;

/// All tuning knobs for SpatialDriftMode live here.  Stored as a
/// ScriptableObject so edits persist between Play sessions and you don't have
/// to navigate to the Car GameObject to change values mid-iteration.
///
/// To create the asset: `Racing Game → Create Spatial Drift Tuning Asset`.
/// It's saved to Assets/Resources/SpatialDriftTuning.asset so SpatialDriftMode
/// can find it via Resources.Load at startup.  Drop the asset on the
/// component's `tuning` field to override; leave blank and the runtime falls
/// back to whatever's in Resources (or in-memory defaults if neither exists).
///
/// The in-game tuning panel (F9 in time trials) edits this instance live and
/// can Save the values as a JSON override in persistentDataPath — loaded on
/// startup so tweaks made in a BUILD survive a restart.  Reset deletes it.
[CreateAssetMenu(fileName = "SpatialDriftTuning",
                 menuName = "Racing Game/Spatial Drift Tuning")]
public class SpatialDriftTuning : ScriptableObject
{
    /// Which minigame runs while the mode is Active:
    ///   Rhythm — DDR-style falling notes on the side panel (the original).
    ///   Rails  — the car locks to lanes across the road (count scales with the
    ///            road's width) and you dodge obstacles by switching lanes.
    public enum Minigame { Rhythm, Rails }

    [Header("Minigame")]
    public Minigame minigame = Minigame.Rails;

    [Header("Session limits")]
    [Tooltip("Maximum seconds the mode can stay Active.  Hitting the limit exits " +
             "with the normal score boost.  0 = unlimited.")]
    [Range(0f, 60f)] public float maxDuration = 8f;
    [Tooltip("Seconds after ANY exit before the mode can be entered again.")]
    [Range(0f, 60f)] public float rechargeDuration = 6f;

    [Header("Entry")]
    public bool requiresTopGear = true;
    [Range(0.5f, 1f)] public float minRPMToEnter = 0.85f;
    public KeyCode entryKey            = KeyCode.E;
    public KeyCode entryGamepadButton  = KeyCode.JoystickButton5;   // RB

    [Header("Autopilot (during Active)")]
    public float speedMultiplier              = 1.4f;
    public float speedAdditive                = 25f;
    [Range(2f, 30f)]  public float headingLockResponse = 8f;
    [Range(2f, 200f)] public float lateralSnapSpeed    = 60f;

    [Header("Rails minigame")]
    [Tooltip("Metres between rails.  Rails are symmetric pairs around the centreline " +
             "at this fixed spacing — the road's width decides how many pairs fit " +
             "(2, 4 or 6 rails), so rails never slide sideways as the road tapers.")]
    [Range(2f, 8f)]   public float railsLaneSpacing     = 3.2f;
    [Tooltip("Cap on total rails (rounded down to a whole pair — 6 = up to 3 pairs).")]
    [Range(2, 6)]     public int   railsMaxLanes        = 6;
    [Tooltip("How fast the car slides between rails (m/s).  High = a snappy hop.")]
    [Range(5f, 120f)] public float railsLaneSwitchSpeed = 78f;
    [Tooltip("Metres between obstacle rows at the START of a run…")]
    [Range(15f, 200f)] public float railsObstacleSpacing    = 70f;
    [Tooltip("…shrinking to this by the end of the difficulty ramp.")]
    [Range(10f, 120f)] public float railsObstacleSpacingMin = 42f;
    [Tooltip("How far ahead of the car rows are spawned (metres).")]
    [Range(40f, 300f)] public float railsSpawnAhead     = 120f;
    public int railsScorePerDodge = 100;

    [Header("Zone Race (game mode: the whole race runs in the zone)")]
    [Tooltip("Train speed = the drift-zone autopilot speed × this.")]
    [Range(0.4f, 1.2f)] public float zoneSpeedScale      = 0.8f;
    [Tooltip("Metres per place of DEBT between cars in the train.")]
    [Range(6f, 30f)]    public float zoneGapLength       = 14f;
    [Tooltip("Most places of debt you can fall behind the car ahead.")]
    [Range(1, 5)]       public int   zoneMaxDebt         = 3;
    [Tooltip("Chance a row carries a gold BONUS gate (clears 1 debt, or grants a shield).")]
    [Range(0f, 0.3f)]   public float zoneBonusChance     = 0.07f;
    [Tooltip("Metres between obstacle rows at the start of the race…")]
    [Range(20f, 200f)]  public float zoneRowSpacingStart = 80f;
    [Tooltip("…tightening to this by the final lap.")]
    [Range(15f, 150f)]  public float zoneRowSpacingEnd   = 50f;

    static SpatialDriftTuning _shared;
    /// The project's tuning asset (with saved overrides applied) — or an
    /// in-memory default when no asset exists.
    public static SpatialDriftTuning Shared
    {
        get
        {
            if (_shared != null) return _shared;
            _shared = Resources.Load<SpatialDriftTuning>("SpatialDriftTuning");
            if (_shared == null) _shared = CreateInstance<SpatialDriftTuning>();
            _shared.EnsureRuntimeOverrides();
            return _shared;
        }
    }

    [Header("Rhythm — starting (easy) values")]
    [Range(0.2f, 1.5f)]  public float noteSpawnInterval = 0.65f;
    [Range(0.5f, 3f)]    public float noteLeadTime      = 1.6f;
    [Range(0.05f, 0.4f)] public float hitWindow         = 0.20f;

    [Header("Rhythm — difficulty ramp")]
    public bool difficultyRampEnabled = true;
    [Range(5f, 120f)]    public float rampDuration         = 30f;
    [Range(0.15f, 1f)]   public float noteSpawnIntervalMin = 0.28f;
    [Range(0.5f, 2.5f)]  public float noteLeadTimeMin      = 1.0f;
    [Range(0.05f, 0.3f)] public float hitWindowMin         = 0.11f;

    [Header("Holds (DDR-style sustained notes)")]
    [Range(0f, 0.8f)] public float holdNoteChance      = 0.30f;
    [Range(0.2f, 2f)] public float holdDurationMin     = 0.55f;
    [Range(0.2f, 3f)] public float holdDurationMax     = 1.30f;
    public int        scorePerHoldComplete             = 150;

    [Header("Lives & Score")]
    [Range(1, 10)] public int maxLives            = 3;
    public int    scorePerHit                     = 100;
    public int    scorePerPerfect                 = 200;
    [Range(1, 4)] public int maxSimultaneousHolds = 2;

    [Header("Exit")]
    public float exitBoostMin            = 5f;
    public float exitBoostMax            = 25f;
    public int   exitBoostSaturationScore = 2000;
    public float exitBoostDuration       = 1.2f;
    [Range(1, 10)] public int exitGear   = 6;
    [Tooltip("Seconds after exit during which the car stays center-locked + " +
             "heading-locked so the player isn't immediately thrown into a wall.")]
    [Range(0f, 3f)] public float postExitGraceDuration = 1f;

    [Header("Visuals (purple / void theme)")]
    public Color ghostBodyColor   = new Color(0.55f, 0.28f, 1.10f);
    public Color ghostEmission    = new Color(0.55f, 0.20f, 1.10f);
    [Range(0f, 5f)] public float ghostEmissionIntensity = 2.4f;
    [Tooltip("Alpha of the purple screen tint during Active.  Fades in/out at activate/exit.")]
    [Range(0f, 0.6f)] public float screenTintAlpha      = 0.22f;
    public Color screenTintColor = new Color(0.45f, 0.20f, 0.85f);

    // ── Runtime overrides (in-game tuning panel) ──────────────────────────────
    // The panel edits this loaded instance directly.  Save() persists the whole
    // value set as JSON so a BUILD keeps your tweaks; the asset itself is never
    // modified at runtime.  ResetToAssetDefaults() restores the values the asset
    // shipped with and deletes the override file.
    static string OverridePath => Path.Combine(Application.persistentDataPath, "drift_tuning.json");
    string assetDefaultsJson;                 // snapshot taken before overrides
    public bool HasSavedOverrides => File.Exists(OverridePath);

    /// Called once by SpatialDriftMode after Resources.Load: remember the asset
    /// values, then apply any saved override file on top.
    public void EnsureRuntimeOverrides()
    {
        if (assetDefaultsJson != null) return;   // already initialised this session
        assetDefaultsJson = JsonUtility.ToJson(this);
        try
        {
            if (File.Exists(OverridePath))
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(OverridePath), this);
                Debug.Log("[SpatialDriftTuning] Applied saved overrides from " + OverridePath);
            }
        }
        catch (System.Exception e)
        { Debug.LogWarning("[SpatialDriftTuning] override load failed: " + e.Message); }
    }

    public void SaveOverrides()
    {
        try { File.WriteAllText(OverridePath, JsonUtility.ToJson(this, true)); }
        catch (System.Exception e)
        { Debug.LogWarning("[SpatialDriftTuning] override save failed: " + e.Message); }
    }

    public void ResetToAssetDefaults()
    {
        if (!string.IsNullOrEmpty(assetDefaultsJson))
            JsonUtility.FromJsonOverwrite(assetDefaultsJson, this);
        try { if (File.Exists(OverridePath)) File.Delete(OverridePath); } catch { }
    }
}
