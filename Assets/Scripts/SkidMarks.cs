using UnityEngine;

/// Tyre marks while drifting, in two layers:
///   FRESH  two dark TrailRenderers at the rear wheels, laid FLAT on the road
///          (they face the car's up axis, not the camera), fading over a few
///          seconds — the crisp black streak right behind you.
///   RUBBER every mark also lays rubber into TrackRubber, which never fades:
///          where drift lines overlap lap after lap the racing line burns in.
/// Trails emit in WORLD space, so marks stay where they were laid, banked
/// sections included.  Auto-attached by CarController.Awake.
[DisallowMultipleComponent]
public class SkidMarks : MonoBehaviour
{
    [Header("Source")]
    public CarController car;

    [Header("Emitter Positions (local to car)")]
    [Tooltip("Local position of the left rear wheel.  Y=0 = on the track surface.")]
    public Vector3 leftRearLocal  = new Vector3(-0.75f, 0.03f, -1.55f);
    public Vector3 rightRearLocal = new Vector3( 0.75f, 0.03f, -1.55f);

    [Header("Look")]
    [Range(0.02f, 0.5f)] public float trailWidth    = 0.18f;
    [Range(1f, 30f)]     public float trailLifetime = 8f;
    public Color trailColor = new Color(0.05f, 0.05f, 0.06f, 0.62f);

    [Header("Rubber build-up")]
    [Tooltip("Width of the rubber each wheel lays (m).")]
    [Range(0.1f, 1f)] public float rubberWidth = 0.32f;

    [Header("Threshold")]
    [Tooltip("Minimum speed before skid marks appear (m/s).")]
    [Range(0f, 30f)] public float minSpeed = 8f;

    TrailRenderer leftTrail, rightTrail;
    bool  hadPrev;
    float prevDL, prevLL, prevDR, prevLR;    // last wheel positions in track space

    void Awake()
    {
        if (car == null) car = GetComponent<CarController>();

        var v = VfxTuning.Load();
        if (v != null)
        {
            trailWidth    = v.skidTrailWidth;
            trailLifetime = v.skidTrailLifetime;
            trailColor    = v.skidTrailColor;
            trailColor.a  = Mathf.Min(trailColor.a, 0.62f);   // marks read as rubber, not paint
            minSpeed      = v.skidMinSpeed;
        }

        leftTrail  = BuildTrail("LeftSkid",  leftRearLocal);
        rightTrail = BuildTrail("RightSkid", rightRearLocal);
    }

    TrailRenderer BuildTrail(string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        // TransformZ alignment faces the strip along this transform's Z — point
        // Z up the car so the mark lies flat on the road surface.
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        var tr = go.AddComponent<TrailRenderer>();
        tr.time              = trailLifetime;
        tr.startWidth        = trailWidth;
        tr.endWidth          = trailWidth * 0.85f;
        tr.startColor        = trailColor;
        tr.endColor          = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
        tr.minVertexDistance = 0.12f;
        tr.numCornerVertices = 0;
        tr.numCapVertices    = 0;
        tr.emitting          = false;
        tr.alignment         = LineAlignment.TransformZ;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows    = false;

        // Translucent, two-sided (lying flat its face can point down) — the
        // default particle material was opaque, so marks drew solid black.
        tr.sharedMaterial = FxMaterials.PlainAlpha;

        return tr;
    }

    void Update()
    {
        bool emit = car != null && car.IsDrifting && car.Speed > minSpeed && !car.spatialDriftActive;
        if (leftTrail  != null) leftTrail.emitting  = emit;
        if (rightTrail != null) rightTrail.emitting = emit;

        if (!emit || car.track == null) { hadPrev = false; return; }

        // Rear wheels → track space (distance along, lateral) via the car's
        // own track coordinates plus each wheel's offset in the track frame.
        var track = car.track;
        track.Sample(car.DistanceAlongTrack, car.LateralOffset, out Vector3 cp, out _, out Vector3 fwd, out Vector3 up);
        Vector3 right = Vector3.Cross(up, fwd).normalized;
        WheelToTrack(leftRearLocal,  cp, fwd, right, out float dL, out float lL);
        WheelToTrack(rightRearLocal, cp, fwd, right, out float dR, out float lR);

        // Bigger slides lay more rubber.
        float strength = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(car.SlideAngle / 30f));
        if (hadPrev)
        {
            TrackRubber.Lay(track, prevDL, prevLL, dL, lL, rubberWidth, strength);
            TrackRubber.Lay(track, prevDR, prevLR, dR, lR, rubberWidth, strength);
        }
        prevDL = dL; prevLL = lL; prevDR = dR; prevLR = lR;
        hadPrev = true;
    }

    void WheelToTrack(Vector3 local, Vector3 carPos, Vector3 fwd, Vector3 right, out float d, out float lat)
    {
        Vector3 delta = transform.TransformPoint(local) - carPos;
        d   = car.track.WrapDistance(car.DistanceAlongTrack + Vector3.Dot(delta, fwd));
        lat = car.LateralOffset + Vector3.Dot(delta, right);
    }
}
