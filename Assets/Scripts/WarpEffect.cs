using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// "Redline" warp effect.  Above startSpeed, post-processing intensities ramp
/// up: lens distortion pulls the corners in (pincushion), chromatic aberration
/// splits the RGB channels at the edges, vignette darkens the periphery, and
/// motion blur smears anything not moving with the camera.
///
/// Runs as a Global Volume on the camera at high priority so it stacks on top
/// of any scene volumes you might add later.  All thresholds + max intensities
/// are tunable in the inspector — start conservative, push them after testing.
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class WarpEffect : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Auto-finds the CarController in the scene if blank.")]
    public CarController car;

    [Header("Speed Range (fraction of top gear's max speed)")]
    [Tooltip("Fraction of the highest gear's max speed at which the warp starts ramping up. " +
             "0.45 ≈ 65 m/s with the default gear curve — late enough that normal " +
             "driving stays crisp.")]
    [Range(0f, 1f)] public float startFraction = 0.45f;
    [Tooltip("Fraction of the highest gear's max speed at which the warp reaches max intensity. " +
             "0.65 ≈ 94 m/s with the default gear curve.")]
    [Range(0f, 1f)] public float maxFraction   = 0.65f;

    [Header("Max Intensities (set 0 to disable individual effects)")]
    [Range(-1f, 0f)] public float lensDistortionMax      = -0.40f;   // negative = pincushion
    [Range(0f,  1f)] public float chromaticAberrationMax =  0.35f;   // lower = less edge fringing/fuzz
    [Range(0f,  1f)] public float vignetteMax            =  0.40f;
    [Tooltip("Motion blur is the main source of the 'fuzzy when driving' look. Kept low " +
             "by default; set to 0 to disable entirely for maximum sharpness.")]
    [Range(0f,  1f)] public float motionBlurMax          =  0.10f;

    [Header("Ramp")]
    [Tooltip("Eases the linear (startSpeed..maxSpeed) value before applying it.")]
    public AnimationCurve rampCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    Volume               volume;
    VolumeProfile        profile;
    LensDistortion       lensDistortion;
    ChromaticAberration  chromaticAberration;
    Vignette             vignette;
    MotionBlur           motionBlur;
    ColorAdjustments     colorAdj;
    GearSystem           gears;   // cached lookup for top-gear speed

    // ── One-shot pulse (rift crossings, big impacts) ─────────────────────────
    static float _pulse;
    static Color _pulseTint = Color.white;

    /// A burst on top of the speed warp: chromatic split, lens punch and an
    /// exposure flash tinted `tint`, decaying over ~0.4 s.  amount 0..1.
    public static void Pulse(float amount, Color tint)
    {
        _pulse     = Mathf.Clamp01(Mathf.Max(_pulse, amount));
        _pulseTint = tint;
    }

    void Awake()
    {
        if (car == null) car = CarController.FindLocalPlayer();

        // Seed tunable fields from the shared VfxTuning asset (if present) so
        // they're editable in edit mode despite this component being runtime-added.
        var v = VfxTuning.Load();
        if (v != null)
        {
            startFraction           = v.warpStartFraction;
            maxFraction             = v.warpMaxFraction;
            lensDistortionMax       = v.warpLensDistortion;
            chromaticAberrationMax  = v.warpChromaticAberration;
            vignetteMax             = v.warpVignette;
            motionBlurMax           = v.warpMotionBlur;
        }

        EnsurePostProcessingEnabled();
        BuildVolume();
    }

    void OnDestroy()
    {
        if (profile != null) Destroy(profile);
    }

    void EnsurePostProcessingEnabled()
    {
        var cam = GetComponent<Camera>();
        if (cam == null) return;

        // Adds UniversalAdditionalCameraData if missing and switches PP on.
        var camData = cam.GetUniversalAdditionalCameraData();
        if (camData != null) camData.renderPostProcessing = true;
    }

    void BuildVolume()
    {
        profile           = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name      = "WarpEffectProfile";
        profile.hideFlags = HideFlags.HideAndDontSave;

        lensDistortion = profile.Add<LensDistortion>(true);
        lensDistortion.intensity.overrideState = true;
        lensDistortion.intensity.value         = 0f;
        lensDistortion.scale.overrideState     = true;
        lensDistortion.scale.value             = 1f;

        chromaticAberration = profile.Add<ChromaticAberration>(true);
        chromaticAberration.intensity.overrideState = true;
        chromaticAberration.intensity.value         = 0f;

        vignette = profile.Add<Vignette>(true);
        vignette.intensity.overrideState  = true;
        vignette.intensity.value          = 0f;
        vignette.color.overrideState      = true;
        vignette.color.value              = Color.black;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value         = 0.45f;

        motionBlur = profile.Add<MotionBlur>(true);
        motionBlur.intensity.overrideState = true;
        motionBlur.intensity.value         = 0f;
        motionBlur.quality.overrideState   = true;
        motionBlur.quality.value           = MotionBlurQuality.Medium;

        colorAdj = profile.Add<ColorAdjustments>(true);
        colorAdj.postExposure.overrideState = true;
        colorAdj.postExposure.value         = 0f;
        colorAdj.colorFilter.overrideState  = true;
        colorAdj.colorFilter.value          = Color.white;

        volume               = gameObject.AddComponent<Volume>();
        volume.isGlobal      = true;
        volume.priority      = 100f;
        volume.sharedProfile = profile;
    }

    void Update()
    {
        if (car == null) car = CarController.FindLocalPlayer();
        if (car == null) return;

        // Resolve thresholds against the top gear's max speed so the warp
        // auto-scales with the gear curve.
        if (gears == null) gears = car.GetComponent<GearSystem>();
        float topSpeed = 145f;   // safe fallback if gears not resolved yet
        if (gears != null && gears.gearMaxSpeeds != null && gears.gearMaxSpeeds.Length > 0)
            topSpeed = gears.gearMaxSpeeds[gears.gearMaxSpeeds.Length - 1];

        float startSpeed = startFraction * topSpeed;
        float maxSpeed   = maxFraction   * topSpeed;

        float t = Mathf.InverseLerp(startSpeed, maxSpeed, car.Speed);
        float k = rampCurve.Evaluate(t);

        _pulse = Mathf.MoveTowards(_pulse, 0f, Time.unscaledDeltaTime / 0.42f);
        float p = _pulse * _pulse;

        if (lensDistortion      != null) lensDistortion.intensity.value      = Mathf.Clamp(k * lensDistortionMax - p * 0.55f, -1f, 1f);
        if (chromaticAberration != null) chromaticAberration.intensity.value = Mathf.Clamp01(k * chromaticAberrationMax + p);
        if (colorAdj != null)
        {
            colorAdj.postExposure.value = p * 1.6f;
            colorAdj.colorFilter.value  = Color.Lerp(Color.white, _pulseTint, p * 0.6f);
        }
        if (vignette            != null) vignette.intensity.value            = k * vignetteMax;
        if (motionBlur          != null) motionBlur.intensity.value          = k * motionBlurMax;
    }
}
