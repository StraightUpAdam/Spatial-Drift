using UnityEngine;

/// Bright red "taillight" streaks emitted from the rear corners of the car
/// while the drift-release boost is active.  Short trail lifetime so they
/// read as glowing motion streaks rather than persistent marks (those are
/// the dark SkidMarks).
///
/// Triggered by CarController.OnDriftEnd: a timer kicks off equal to the
/// drift boost duration, and the trails emit until it elapses.  Auto-attached
/// by CarController.Awake.
[DisallowMultipleComponent]
public class BoostTrails : MonoBehaviour
{
    [Header("Source")]
    public CarController car;

    [Header("Emitter Positions (local to car)")]
    [Tooltip("Local position of the left taillight.")]
    public Vector3 leftRearLocal  = new Vector3(-0.62f, 0.40f, -1.75f);
    public Vector3 rightRearLocal = new Vector3( 0.62f, 0.40f, -1.75f);

    [Header("Look")]
    [Range(0.05f, 0.6f)] public float trailWidth    = 0.20f;
    [Range(0.1f, 2.5f)]  public float trailLifetime = 0.6f;
    public Color trailColorStart = new Color(1.0f, 0.18f, 0.10f, 1f);
    public Color trailColorEnd   = new Color(1.0f, 0.30f, 0.10f, 0f);

    [Header("Min Charge")]
    [Tooltip("Drift charge (0..1) below which the trails don't fire.")]
    [Range(0f, 1f)] public float minCharge = 0.05f;

    TrailRenderer leftTrail, rightTrail;
    float         emitTimer;
    bool          subscribed;

    void Awake()
    {
        if (car == null) car = GetComponent<CarController>();

        var v = VfxTuning.Load();
        if (v != null)
        {
            trailWidth      = v.boostTrailWidth;
            trailLifetime   = v.boostTrailLifetime;
            trailColorStart = v.boostTrailColorStart;
            trailColorEnd   = v.boostTrailColorEnd;
        }

        leftTrail  = BuildTrail("LeftBoostTrail",  leftRearLocal);
        rightTrail = BuildTrail("RightBoostTrail", rightRearLocal);
    }

    void OnEnable()
    {
        if (!subscribed && car != null)
        {
            car.OnDriftEnd += HandleDriftEnd;
            subscribed      = true;
        }
    }

    void OnDisable()
    {
        if (subscribed && car != null)
        {
            car.OnDriftEnd -= HandleDriftEnd;
            subscribed      = false;
        }
    }

    void HandleDriftEnd(float charge)
    {
        if (charge < minCharge || car == null) return;
        // Emit for the duration of the boost the drift just awarded.
        emitTimer = Mathf.Max(emitTimer, car.driftBoostDuration);
    }

    void Update()
    {
        bool emit = emitTimer > 0f;
        if (emit) emitTimer = Mathf.Max(0f, emitTimer - Time.deltaTime);

        if (leftTrail  != null) leftTrail.emitting  = emit;
        if (rightTrail != null) rightTrail.emitting = emit;
    }

    TrailRenderer BuildTrail(string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;

        var tr = go.AddComponent<TrailRenderer>();
        tr.time              = trailLifetime;
        tr.startWidth        = trailWidth;
        tr.endWidth          = trailWidth * 0.4f;
        tr.startColor        = trailColorStart;
        tr.endColor          = trailColorEnd;
        tr.minVertexDistance = 0.05f;
        tr.numCornerVertices = 2;
        tr.numCapVertices    = 2;
        tr.emitting          = false;
        tr.alignment         = LineAlignment.View;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Particles/Standard Unlit")
                  ?? Shader.Find("Sprites/Default");
        tr.material = new Material(shader) { color = Color.white };

        return tr;
    }
}
