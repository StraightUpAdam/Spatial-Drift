using UnityEngine;

/// Tuning values for the visual-effect components that CarController and
/// RacingCamera add at runtime (WarpEffect, SpeedLinesEffect, BoostEffects,
/// HoverBody, SkidMarks, BoostTrails).  Because those components are created in
/// Awake they don't exist in edit mode, so their Inspector values can't be
/// tuned persistently — this asset fixes that, exactly like SpatialDriftTuning.
///
/// Create it via "Racing Game / Create VFX Tuning Asset" — it lands in
/// Assets/Resources/VfxTuning.asset and each effect Resources.Loads it on Awake.
/// Each component seeds its own fields from here, so leaving the asset absent
/// just falls back to the in-code defaults.
[CreateAssetMenu(fileName = "VfxTuning", menuName = "Racing Game/VFX Tuning")]
public class VfxTuning : ScriptableObject
{
    [Header("Warp / Post-Processing")]
    [Tooltip("Speed (as a fraction of top gear's max) where the warp starts / peaks.")]
    [Range(0f, 1f)] public float warpStartFraction        = 0.45f;
    [Range(0f, 1f)] public float warpMaxFraction          = 0.65f;
    [Tooltip("Negative = pincushion lens pull.")]
    [Range(-1f, 0f)] public float warpLensDistortion       = -0.40f;
    [Range(0f, 1f)] public float warpChromaticAberration  = 0.35f;
    [Range(0f, 1f)] public float warpVignette             = 0.40f;
    [Tooltip("Main source of the 'fuzzy' look — keep low or 0 for sharpness.")]
    [Range(0f, 1f)] public float warpMotionBlur           = 0.10f;

    [Header("Speed Lines")]
    public float speedLinesMinSpeed   = 40f;
    public float speedLinesMaxSpeed   = 120f;
    public Color speedLinesColor      = new Color(1f, 1f, 1f, 0.40f);
    [Range(0f, 600f)] public float speedLinesMaxEmitRate = 220f;
    [Range(0f, 30f)]  public float speedLinesStretch     = 12f;

    [Header("Speed Lines — Origin / Shape")]
    [Tooltip("How far ahead of the camera the streaks spawn (the origin point). " +
             "Lower = streaks start closer to you and rush past faster.")]
    [Range(1f, 30f)]  public float speedLinesAheadDistance = 8f;
    [Tooltip("Vertical shift of the origin (− = below eye line, + = above).")]
    [Range(-10f, 10f)] public float speedLinesVerticalOffset = 0f;
    [Tooltip("Horizontal shift of the origin (− = left, + = right).")]
    [Range(-10f, 10f)] public float speedLinesHorizontalOffset = 0f;
    [Tooltip("Radius of the ring the streaks spawn on — bigger = a wider tunnel " +
             "that frames the screen edges; smaller = streaks cluster near center.")]
    [Range(1f, 20f)]  public float speedLinesRingRadius    = 7f;
    [Tooltip("Thickness of the spawn ring (how spread-out across the ring band).")]
    [Range(0f, 4f)]   public float speedLinesRingThickness = 1.5f;
    [Range(0.1f, 1f)] public float speedLinesLifetime      = 0.45f;

    [Header("Boost Particles")]
    public Color boostParticleColor    = new Color(0.30f, 0.85f, 1.0f, 1f);
    [Range(0.1f, 2f)]  public float boostParticleLifetime = 0.55f;
    [Range(1f, 40f)]   public float boostParticleSpeed    = 16f;
    [Range(10f, 400f)] public float boostEmitRate         = 160f;
    [Range(0.05f, 1f)] public float boostStartSize        = 0.35f;

    [Header("Hover Body (feel)")]
    [Range(0f, 1f)]    public float hoverExtraHeight    = 0.15f;
    [Range(0f, 0.2f)]  public float hoverBobAmplitude   = 0.04f;
    [Range(0.2f, 4f)]  public float hoverBobFrequency   = 1.1f;
    [Range(0f, 30f)]   public float hoverLeanAngleMax   = 14f;
    [Range(0f, 30f)]   public float hoverDriftLeanBonus = 10f;
    [Range(2f, 30f)]   public float hoverLeanResponse   = 10f;
    [Range(0f, 0.4f)]  public float hoverBankSinkMax    = 0.12f;

    [Header("Skid Marks (drift)")]
    [Range(0.02f, 0.5f)] public float skidTrailWidth    = 0.18f;
    [Range(1f, 30f)]     public float skidTrailLifetime = 8f;
    public Color skidTrailColor = new Color(0.08f, 0.08f, 0.10f, 0.85f);
    [Range(0f, 30f)]     public float skidMinSpeed      = 8f;

    [Header("Boost Trails (drift-release taillights)")]
    [Range(0.05f, 0.6f)] public float boostTrailWidth      = 0.20f;
    [Range(0.1f, 2.5f)]  public float boostTrailLifetime   = 0.6f;
    public Color boostTrailColorStart = new Color(1.0f, 0.18f, 0.10f, 1f);
    public Color boostTrailColorEnd   = new Color(1.0f, 0.30f, 0.10f, 0f);

    /// Convenience loader used by each effect component.  Returns the asset in
    /// Resources, or null if it hasn't been created.
    public static VfxTuning Load() => Resources.Load<VfxTuning>("VfxTuning");
}
