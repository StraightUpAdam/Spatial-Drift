using System.Collections.Generic;
using UnityEngine;

/// Kirby Air Ride-style draft trail.  Every car records where it has just
/// driven (spline-space samples: distance + lateral + timestamp) and renders a
/// faint wake ribbon.  Any OTHER car whose position lands inside a fresh
/// sample's corridor gets a gentle speed-cap bonus — stay in the wake of the
/// car ahead and you slowly reel them in.
///
/// Detection is done per car against a static registry of all trails — no
/// scene manager object needed.  Costs a few dozen float comparisons per car
/// per frame.  Auto-added to every car by CarController.Awake.
[DisallowMultipleComponent]
public class SlipstreamTrail : MonoBehaviour
{
    // All live trails in the scene — each car checks every trail but its own.
    static readonly List<SlipstreamTrail> All = new List<SlipstreamTrail>();

    [Header("Trail Recording")]
    [Tooltip("A sample is dropped every this many metres of travel.")]
    public float sampleSpacing  = 4f;
    [Tooltip("Samples older than this stop giving a bonus (and the ribbon fades).")]
    public float trailDuration  = 3f;
    [Tooltip("Below this speed the car leaves no wake (no idle slipstreams).")]
    public float minSpeedToLeaveTrail = 25f;

    [Header("Corridor")]
    [Tooltip("Half-width of the wake corridor — how far off the leader's line " +
             "you can be and still draft.")]
    public float corridorHalfWidth = 1.7f;
    [Tooltip("Along-track tolerance when matching against a sample.  Must be " +
             "more than half the sample spacing or the corridor has gaps.")]
    public float alongTolerance = 3f;

    [Header("Bonus")]
    [Tooltip("m/s added to the drafting car's speed cap while in the wake.")]
    public float bonusSpeed = 7f;
    [Tooltip("How quickly the bonus ramps in/out (m/s per second) — keeps the " +
             "speedometer from twitching at the corridor edge.")]
    public float bonusRamp = 18f;

    [Header("Wake Visual")]
    [Tooltip("Keep this VERY subtle — it's a hint, not a light show.")]
    public Color wakeColor  = new Color(1f, 1f, 1f, 0.06f);
    [Range(0.05f, 4f)] public float wakeWidth = 0.35f;

    struct Sample { public float dist, lat, time; }

    const int BUF = 48;
    readonly Sample[] buf = new Sample[BUF];
    int   bufCount, bufHead;
    float lastSampleDist = float.NegativeInfinity;

    CarController car;
    Track         track;
    TrailRenderer wake;

    void Awake()
    {
        car = GetComponent<CarController>();
        BuildWakeVisual();
    }

    void OnEnable()  => All.Add(this);
    void OnDisable() => All.Remove(this);

    void BuildWakeVisual()
    {
        var go = new GameObject("SlipstreamWake");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.12f, -1.8f);   // rear, just off the deck

        wake = go.AddComponent<TrailRenderer>();
        wake.time              = trailDuration;
        wake.startWidth        = wakeWidth;
        wake.endWidth          = wakeWidth * 0.6f;
        wake.startColor        = wakeColor;
        wake.endColor          = new Color(wakeColor.r, wakeColor.g, wakeColor.b, 0f);
        wake.minVertexDistance = 1.5f;
        wake.numCornerVertices = 0;
        wake.numCapVertices    = 0;
        wake.emitting          = false;
        wake.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wake.receiveShadows    = false;

        // A faint sheen lying FLAT on the road (it used to face the camera,
        // standing up as a white sheet behind every car ahead), on a real
        // transparent material that honours the low vertex alpha.
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        wake.alignment      = LineAlignment.TransformZ;
        wake.sharedMaterial = FxMaterials.PlainAlpha;
    }

    void Update()
    {
        if (car == null) return;
        if (track == null) { track = car.track; if (track == null) return; }

        bool fastEnough = car.Speed > minSpeedToLeaveTrail;

        // ── Record our own wake ───────────────────────────────────────────────
        if (wake != null) wake.emitting = fastEnough;
        if (fastEnough)
        {
            float travelled = Mathf.Abs(WrapDelta(car.DistanceAlongTrack - lastSampleDist,
                                                  track.Length));
            if (lastSampleDist == float.NegativeInfinity || travelled >= sampleSpacing)
            {
                buf[bufHead] = new Sample
                {
                    dist = car.DistanceAlongTrack,
                    lat  = car.LateralOffset,
                    time = Time.time,
                };
                bufHead  = (bufHead + 1) % BUF;
                bufCount = Mathf.Min(bufCount + 1, BUF);
                lastSampleDist = car.DistanceAlongTrack;
            }
        }

        // ── Are WE inside anyone else's wake? ────────────────────────────────
        bool drafting = false;
        if (!car.spatialDriftActive && car.Speed > 5f)
        {
            for (int i = 0; i < All.Count && !drafting; i++)
            {
                var other = All[i];
                if (other == this || other == null) continue;
                drafting = other.ContainsPoint(car.DistanceAlongTrack, car.LateralOffset,
                                               track.Length);
            }
        }

        float target = drafting ? bonusSpeed : 0f;
        car.SlipstreamBonus = Mathf.MoveTowards(car.SlipstreamBonus, target,
                                                bonusRamp * Time.deltaTime);
    }

    /// Is the given spline-space point inside a fresh part of THIS car's wake?
    bool ContainsPoint(float dist, float lat, float trackLength)
    {
        float now = Time.time;
        for (int i = 0; i < bufCount; i++)
        {
            ref readonly var s = ref buf[i];
            if (now - s.time > trailDuration) continue;
            if (Mathf.Abs(s.lat - lat) > corridorHalfWidth) continue;
            if (Mathf.Abs(WrapDelta(s.dist - dist, trackLength)) > alongTolerance) continue;
            return true;
        }
        return false;
    }

    // Shortest signed distance between two track positions on a closed loop.
    static float WrapDelta(float delta, float length)
    {
        if (length <= 0f) return delta;
        delta %= length;
        if (delta >  length * 0.5f) delta -= length;
        if (delta < -length * 0.5f) delta += length;
        return delta;
    }
}
