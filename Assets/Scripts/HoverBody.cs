using UnityEngine;

/// Visual-only body animation: hover bob, lean on steering (motorcycle-style,
/// into the turn), and compression sink on banked corners under load.  All
/// effects are local-space offsets applied to the visual body GameObject —
/// the underlying physics / spline position is untouched.
///
/// Auto-attached by CarController.Awake.  Finds the first child with a
/// MeshRenderer and treats that as the body to animate.  Override `body` in
/// the inspector if you have a more complex visual hierarchy.
[DisallowMultipleComponent]
public class HoverBody : MonoBehaviour
{
    [Header("References (auto-found)")]
    public CarController car;
    [Tooltip("The visual mesh to animate.  Defaults to the first child with a MeshRenderer.")]
    public Transform     body;

    [Header("Hover")]
    [Tooltip("Constant extra height applied on top of whatever the body's base " +
             "local Y is.  Use this to make the car visibly float.")]
    [Range(0f, 1f)]      public float extraHoverHeight = 0.15f;
    [Tooltip("Sine-wave bob amplitude (m).  Set 0 to disable.")]
    [Range(0f, 0.2f)]    public float bobAmplitude     = 0.04f;
    [Tooltip("Bob frequency in Hz.")]
    [Range(0.2f, 4f)]    public float bobFrequency     = 1.1f;

    [Header("Steering Lean")]
    [Tooltip("Max roll angle (degrees) at full steering input.")]
    [Range(0f, 30f)]     public float leanAngleMax     = 14f;
    [Tooltip("Additional roll added while drifting.")]
    [Range(0f, 30f)]     public float driftLeanBonus   = 10f;
    [Tooltip("How quickly the body eases toward the target lean (higher = snappier).")]
    [Range(2f, 30f)]     public float leanResponse     = 10f;

    [Header("Banking Sink (inertia compression)")]
    [Tooltip("Maximum drop in body Y when in a heavily banked corner at speed.")]
    [Range(0f, 0.4f)]    public float bankSinkMax      = 0.12f;
    [Tooltip("How quickly the sink eases.")]
    [Range(2f, 20f)]     public float bankSinkResponse = 5f;

    // ── State ─────────────────────────────────────────────────────────────────
    Vector3 baseLocalPos;
    float   bobPhase;
    float   curLean;       // smoothed lean degrees
    float   curSink;       // smoothed Y offset (negative = compressed down)
    float   topGearSpeed = 145f;

    void Awake()
    {
        if (car == null) car = GetComponent<CarController>();

        var v = VfxTuning.Load();
        if (v != null)
        {
            extraHoverHeight = v.hoverExtraHeight;
            bobAmplitude     = v.hoverBobAmplitude;
            bobFrequency     = v.hoverBobFrequency;
            leanAngleMax     = v.hoverLeanAngleMax;
            driftLeanBonus   = v.hoverDriftLeanBonus;
            leanResponse     = v.hoverLeanResponse;
            bankSinkMax      = v.hoverBankSinkMax;
        }

        if (body == null)
        {
            // Prefer the procedural CarVisual root; fall back to any mesh child.
            var visual = GetComponent<CarVisual>();
            if (visual != null && visual.Root != null) body = visual.Root;
        }
        if (body == null)
        {
            foreach (Transform child in transform)
            {
                if (child.GetComponent<MeshRenderer>() != null) { body = child; break; }
            }
        }
        if (body != null) baseLocalPos = body.localPosition;

        if (car != null && car.gearSystem != null
            && car.gearSystem.gearMaxSpeeds != null && car.gearSystem.gearMaxSpeeds.Length > 0)
        {
            topGearSpeed = car.gearSystem.gearMaxSpeeds[car.gearSystem.gearMaxSpeeds.Length - 1];
        }
    }

    void LateUpdate()
    {
        if (car == null || body == null) return;
        float dt = Time.deltaTime;

        // ─── Steering lean (roll about local Z) ──────────────────────────────
        // Motorcycle / F-Zero style: lean INTO the turn.  Steering right (+steer)
        // → roll right (negative Z euler).  Drift adds extra in the same direction.
        float steer       = car.LocalInput.steer;
        float targetLean  = -steer * leanAngleMax;
        if (car.IsDrifting && Mathf.Abs(steer) > 0.05f)
            targetLean += Mathf.Sign(targetLean) * driftLeanBonus;
        curLean = Mathf.Lerp(curLean, targetLean,
                             1f - Mathf.Exp(-leanResponse * dt));

        // ─── Banking sink ────────────────────────────────────────────────────
        // bankDot = 1 on a flat surface, drops as the car tilts.  Compute a
        // 0-to-1 "bank-ness" that grows with tilt but smoothly fades to 0 for
        // very steep / inverted orientations (so corkscrew sections don't sink).
        float bankDot  = Vector3.Dot(transform.up, Vector3.up);
        float bankNorm = bankDot > 0.3f ? Mathf.Clamp01(1f - bankDot) : 0f;

        float speedFrac  = topGearSpeed > 0.01f
            ? Mathf.Clamp01(car.Speed / topGearSpeed)
            : 0f;
        float targetSink = -bankSinkMax * bankNorm * speedFrac;
        curSink = Mathf.Lerp(curSink, targetSink,
                             1f - Mathf.Exp(-bankSinkResponse * dt));

        // ─── Bob ─────────────────────────────────────────────────────────────
        bobPhase += dt * bobFrequency * 2f * Mathf.PI;
        float bobOffset = Mathf.Sin(bobPhase) * bobAmplitude;

        // ─── Apply ───────────────────────────────────────────────────────────
        Vector3 pos = baseLocalPos;
        pos.y += extraHoverHeight + bobOffset + curSink;
        body.localPosition = pos;

        Vector3 e = body.localEulerAngles;
        e.z = curLean;
        body.localEulerAngles = e;
    }
}
