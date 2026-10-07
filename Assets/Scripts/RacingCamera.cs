using UnityEngine;

/// Follows behind and above the car, rotating with it.  Uses the car's local
/// up vector (not world up) so corkscrews and loops feel right — the camera
/// rolls along with the car instead of fighting the inversion.
///
/// Also pulses the camera FOV when the car is boosting: fast snap outward
/// when boost starts, slower easing back when it ends.  Auto-adds a
/// SpeedLinesEffect for high-speed streaks.
[RequireComponent(typeof(Camera))]
public class RacingCamera : MonoBehaviour
{
    [Header("Follow")]
    public Transform target;
    [Tooltip("Camera offset from the car at idle / normal speed.")]
    public Vector3   localOffset      = new Vector3(0f, 3.5f, -8f);
    [Tooltip("Camera offset while boost is active.  Pulls in toward the car to " +
             "counteract the FOV widening — without this, things look further away " +
             "during boost, not faster.")]
    public Vector3   boostLocalOffset = new Vector3(0f, 3.0f, -6f);
    [Tooltip("Base follow responsiveness.  Low = floaty, high = locked.")]
    public float     followSpeed              = 12f;
    [Tooltip("Extra responsiveness added at top speed.  Exponential lerp has a " +
             "fixed bandwidth, so without this the camera trails the car by " +
             "~velocity / followSpeed meters — ~18m at 145 m/s.  Scaling with " +
             "speed keeps the gap roughly constant.")]
    public float     topSpeedFollowBoost      = 18f;
    public float     rotationSpeed            = 10f;
    [Tooltip("Extra rotation responsiveness at top speed.")]
    public float     topSpeedRotationBoost    = 8f;

    [Header("Boost FOV")]
    [Tooltip("Idle FOV.  Captured from the Camera on Awake if not overridden.")]
    public float baseFOV          = 70f;
    [Tooltip("Degrees added to FOV while boosting.")]
    [Range(0f, 30f)] public float boostFOVDelta = 16f;
    [Tooltip("How quickly FOV snaps WIDE when a boost starts (higher = snappier).")]
    [Range(2f, 40f)] public float fovEaseOutSpeed = 22f;
    [Tooltip("How quickly FOV eases BACK when the boost ends (lower = smoother return).")]
    [Range(2f, 40f)] public float fovEaseInSpeed  = 5f;

    Camera         cam;
    GearSystem     gears;
    CarController  carCtrl;
    float          topGearSpeed = 145f;   // fallback; resolved from GearSystem

    // Camera shake: any system can kick it (Zone Race hits, bumps).
    static float _shake;
    float   shakeSeed;
    Vector3 smoothPos;
    bool    smoothInit;
    public static void Kick(float amount) => _shake = Mathf.Clamp01(Mathf.Max(_shake, amount));

    void OnEnable()  => GameSettings.Changed += OnSettingsChanged;
    void OnDisable() => GameSettings.Changed -= OnSettingsChanged;
    void OnSettingsChanged() => baseFOV = GameSettings.CameraFov;

    void Awake()
    {
        cam = GetComponent<Camera>();
        // Options ▸ Gameplay ▸ Camera FOV (live — the pause settings change it).
        baseFOV = GameSettings.CameraFov;
        if (cam != null) cam.fieldOfView = baseFOV;

        // Auto-add speed lines on the camera if it isn't already there
        if (GetComponent<SpeedLinesEffect>() == null)
            gameObject.AddComponent<SpeedLinesEffect>();

        // Auto-add the "Redline" warp post-processing volume
        if (GetComponent<WarpEffect>() == null)
            gameObject.AddComponent<WarpEffect>();

        // Auto-add the environment: sky dome / fog / ambient, dressed from
        // Atmosphere.Current.  (Long documented as auto-added, but nothing
        // actually added it — the atmosphere never ran until this line.)
        if (GetComponent<SceneAmbiance>() == null)
            gameObject.AddComponent<SceneAmbiance>();

        // Auto-add the gamepad debug overlay (press F11 in Play mode to toggle)
        if (GetComponent<GamepadDebug>() == null)
            gameObject.AddComponent<GamepadDebug>();
    }

    void Update()
    {
        if (cam == null) return;

        if (gears == null && target != null)
            gears = target.GetComponent<GearSystem>();
        if (carCtrl == null && target != null)
            carCtrl = target.GetComponent<CarController>();

        if (gears == null) return;

        // Cache top-gear max speed for speedFrac calculations.
        if (gears.gearMaxSpeeds != null && gears.gearMaxSpeeds.Length > 0)
            topGearSpeed = gears.gearMaxSpeeds[gears.gearMaxSpeeds.Length - 1];

        float targetFOV = gears.IsBoosting ? baseFOV + boostFOVDelta : baseFOV;
        float speed     = gears.IsBoosting ? fovEaseOutSpeed         : fovEaseInSpeed;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV,
                                     1f - Mathf.Exp(-speed * Time.deltaTime));
    }

    void LateUpdate()
    {
        // Self-acquire the local player's car if we don't have a target yet.
        // In multiplayer the car spawns after the scene loads, and a one-shot
        // assignment at spawn time can miss (camera not found that frame) — so
        // the camera keeps trying until the local-player car exists.
        if (target == null)
        {
            var lp = CarController.FindLocalPlayer();
            if (lp != null) target = lp.transform;
        }
        if (target == null) return;

        // Boost progress (0..1), derived from the camera's current FOV so it
        // tracks the smoothed FOV ease exactly — dolly and zoom stay in lockstep.
        float boostT = boostFOVDelta > 0.01f && cam != null
            ? Mathf.Clamp01((cam.fieldOfView - baseFOV) / boostFOVDelta)
            : 0f;
        Vector3 currentOffset = Vector3.Lerp(localOffset, boostLocalOffset, boostT);

        Vector3 desiredPos = target.TransformPoint(currentOffset);

        // Compute the look direction from the camera's DESIRED position to the
        // target — not from its current (lagging) position.  The old version
        // created a feedback loop: cam lags → dir picks up a lateral component
        // → rotation slerps sideways → cam catches up → dir flips back → repeat.
        // Both endpoints here are derived only from the target's transform, so
        // the result is stable per-frame even when the position lerp is mid-catch-up.
        Vector3 dir = target.position - desiredPos;
        if (dir.sqrMagnitude < 1e-6f) dir = target.forward;
        Quaternion desiredRot = Quaternion.LookRotation(dir, target.up);

        // Speed-aware follow: lerp bandwidth scales with car speed so the
        // camera doesn't trail far behind at top speed.
        float speedFrac = carCtrl != null && topGearSpeed > 0.01f
            ? Mathf.Clamp01(carCtrl.Speed / topGearSpeed)
            : 0f;
        float effFollow = followSpeed   + speedFrac * topSpeedFollowBoost;
        float effRot    = rotationSpeed + speedFrac * topSpeedRotationBoost;

        if (!smoothInit) { smoothPos = transform.position; smoothInit = true; }
        smoothPos = Vector3.Lerp(smoothPos, desiredPos,
                                 1f - Mathf.Exp(-effFollow * Time.deltaTime));

        // Decaying shake on top of the smoothed follow (never feeds back into it).
        Vector3 shake = Vector3.zero;
        if (_shake > 0.001f)
        {
            shakeSeed += Time.deltaTime * 38f;
            float a = _shake * _shake * 0.55f;
            shake = transform.right * ((Mathf.PerlinNoise(shakeSeed, 0.3f) - 0.5f) * 2f * a)
                  + transform.up    * ((Mathf.PerlinNoise(0.7f, shakeSeed) - 0.5f) * 2f * a);
            _shake = Mathf.MoveTowards(_shake, 0f, Time.deltaTime * 2.2f);
        }
        transform.position = smoothPos + shake;
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot,
                                              1f - Mathf.Exp(-effRot * Time.deltaTime));
    }
}
