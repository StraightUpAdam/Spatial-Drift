using UnityEngine;

/// AI opponent.  Drives its CarController by writing CarInput every frame —
/// the exact same seam a human (or, later, the network) uses — so AI cars get
/// identical physics, gears, boosts, and wall behaviour for free.
///
/// Behaviour model:
///   • Corner slowdown — samples the spline tangent `lookaheadTime` seconds
///     ahead; the angle between the tangent now and there is the corner
///     severity, which sets a target speed.  Brake when well over it.
///   • Steering — aims at a point on the spline ahead at the AI's preferred
///     lateral offset, with a small Perlin wobble that fades with skill.
///   • Shifting — calls GearShifter.Upshift() when RPM crosses a per-shift
///     target rolled around the PERFECT zone centre.  High skill clusters
///     tightly on it (frequent boosts); low skill sprays early/late.
///   • Avoidance — if another car is just ahead in the same lane, shifts its
///     target lateral to pass on the freer side.  (No collision physics yet —
///     cars are ghosts; this keeps them from visually overlapping.)
///
/// Set `CarController.aiControlled = true` on the same GameObject so the
/// player-input path stands down.
[DisallowMultipleComponent]
public class AIDriver : MonoBehaviour
{
    [Header("Skill")]
    [Tooltip("0 = clumsy backmarker, 1 = ace.  Drives shift timing precision " +
             "and steering steadiness.")]
    [Range(0f, 1f)] public float skill = 0.5f;

    [Header("Driving")]
    [Tooltip("Highest gear this AI will shift into — a soft speed cap that " +
             "keeps weaker AI honest on the straights.")]
    [Range(1, 10)] public int gearCap = 10;
    [Tooltip("Seconds of travel the AI looks ahead when judging corners.")]
    public float lookaheadTime = 0.9f;
    public float minLookahead  = 14f;
    [Tooltip("Higher = slows down more for corners.")]
    [Range(0.3f, 2f)] public float cornerCaution = 1f;
    [Tooltip("Preferred lateral offset — gives each AI its own racing line.")]
    public float preferredLateral = 0f;

    [Header("Drift")]
    [Tooltip("Chance this AI commits to a drift when a sharp corner approaches. " +
             "0 = never drifts, 1 = drifts every real corner.")]
    [Range(0f, 1f)] public float driftAffinity = 0.5f;
    [Tooltip("Corner severity (deg of tangent change over the lookahead) that " +
             "counts as drift-worthy.")]
    public float driftCornerAngle = 45f;

    CarController car;
    GearSystem    gears;
    GearShifter   shifter;
    Track         track;
    RaceManager   race;

    float targetLateral;
    float nextShiftCheck;
    float shiftTargetRPM;
    float noiseSeed;
    bool  driftIntent;     // currently holding a deliberate drift
    bool  cornerLatch;     // rising-edge detect so we roll the dice once per corner

    void Awake()
    {
        car       = GetComponent<CarController>();
        gears     = GetComponent<GearSystem>();
        shifter   = GetComponent<GearShifter>();
        noiseSeed = Random.value * 100f;
        targetLateral = preferredLateral;
        RollShiftTarget();
    }

    void Start()
    {
        if (car != null && track == null) track = car.track;
        if (race == null) race = FindFirstObjectByType<RaceManager>();
    }

    void Update()
    {
        if (car == null || gears == null) return;
        if (track == null) { track = car.track; if (track == null) return; }

        // Countdown / post-finish lock — same rule as the player.
        if (!car.inputEnabled) { car.LocalInput = default; return; }

        float speed = Mathf.Max(car.Speed, 0.1f);

        // ── Corner severity from spline lookahead ────────────────────────────
        float look = Mathf.Max(minLookahead, speed * lookaheadTime);
        track.Sample(car.DistanceAlongTrack, out _, out Vector3 tanNow, out Vector3 upNow);
        track.Sample(track.WrapDistance(car.DistanceAlongTrack + look),
                     out _, out Vector3 tanAhead, out _);
        float cornerAngle = Vector3.Angle(tanNow, tanAhead);   // 0 on straights & corkscrews

        // ── Target speed ─────────────────────────────────────────────────────
        float topAllowed  = gears.gearMaxSpeeds[Mathf.Clamp(gearCap, 1, GearSystem.GearCount) - 1];
        float severity    = Mathf.Clamp01(cornerAngle / 100f) * cornerCaution;
        float targetSpeed = topAllowed * (1f - 0.72f * severity)
                          + car.SlipstreamBonus;       // let drafting AI actually use the wake

        bool  overSpeed = speed > targetSpeed * 1.15f;
        float accel     = speed < targetSpeed ? 1f : 0f;

        // ── Drift intent ─────────────────────────────────────────────────────
        //     Roll the dice once per corner approach.  While committed, hold
        //     brake+steer (which IS the drift trigger) until the corner opens
        //     up — then release for the drift boost, same rules as the player.
        bool sharpCorner = cornerAngle > driftCornerAngle;
        if (sharpCorner && !cornerLatch)
        {
            cornerLatch = true;
            if (speed > 30f && Random.value < driftAffinity) driftIntent = true;
        }
        else if (!sharpCorner)
        {
            cornerLatch = false;
        }
        if (driftIntent && (cornerAngle < driftCornerAngle * 0.5f || speed < 18f))
            driftIntent = false;

        // Brake = drift trigger when steering.  When NOT committing to a drift,
        // only brake while pointing straight-ish (below the drift steer
        // threshold) so corner braking never trips the slide by accident;
        // otherwise shed speed by coasting.
        bool brake = driftIntent;

        // ── Racing line + avoidance ──────────────────────────────────────────
        float desiredLat = preferredLateral;
        if (race != null)
        {
            float len = track.Length;
            for (int i = 0; i < race.racers.Count; i++)
            {
                var other = race.racers[i];
                if (other == null || other == car) continue;

                float along = other.DistanceAlongTrack - car.DistanceAlongTrack;
                if (track.IsClosed && len > 0f)
                {
                    if (along >  len * 0.5f) along -= len;
                    if (along < -len * 0.5f) along += len;
                }
                if (along > 1f && along < 18f &&
                    Mathf.Abs(other.LateralOffset - car.LateralOffset) < 3f)
                {
                    // Pass on the side we're already biased toward.
                    desiredLat = other.LateralOffset
                               + (car.LateralOffset <= other.LateralOffset ? -3.5f : 3.5f);
                    break;
                }
            }
        }
        float maxLat  = track.HalfWidthAt(car.DistanceAlongTrack) - 2f;
        desiredLat    = Mathf.Clamp(desiredLat, -maxLat, maxLat);
        targetLateral = Mathf.MoveTowards(targetLateral, desiredLat, 8f * Time.deltaTime);

        // ── Steering: aim at a point ahead on the chosen line ────────────────
        float aimDist = Mathf.Max(8f, speed * 0.55f);
        track.Sample(track.WrapDistance(car.DistanceAlongTrack + aimDist), targetLateral,
                     out Vector3 aimPos, out _, out _, out _);
        Vector3 toAim = aimPos - transform.position;
        toAim -= Vector3.Dot(toAim, upNow) * upNow;        // project into surface plane

        float steer = Mathf.Clamp(
            Vector3.SignedAngle(transform.forward, toAim, upNow) / 30f, -1f, 1f);

        // Human-ness wobble — fades out with skill.
        steer += (Mathf.PerlinNoise(Time.time * 0.45f, noiseSeed) - 0.5f) * 0.22f * (1f - skill);
        steer  = Mathf.Clamp(steer, -1f, 1f);

        // Non-drift braking: only while pointing straight enough that the
        // brake can't accidentally trigger the slide (threshold is 0.2).
        if (!driftIntent && overSpeed && Mathf.Abs(steer) < 0.18f)
            brake = true;

        // ── Shifting ─────────────────────────────────────────────────────────
        if (Time.time >= nextShiftCheck)
        {
            nextShiftCheck = Time.time + 0.12f;
            if (!brake && shifter != null
                && gears.CurrentGear > 0 && gears.CurrentGear < gearCap
                && gears.RPM >= shiftTargetRPM)
            {
                shifter.Upshift();      // boost awarded if shiftTargetRPM landed in a zone
                RollShiftTarget();
            }
        }

        car.LocalInput = new CarInput { accel = accel, steer = steer, brake = brake };
    }

    // Pick the RPM this AI will shift at next.  PERFECT zone centre is 0.84;
    // high skill clusters tightly on it, low skill sprays early or late into
    // GOOD / GREAT / nothing.
    void RollShiftTarget()
    {
        float err = 1f - skill;
        shiftTargetRPM = Mathf.Clamp(0.84f + Random.Range(-0.16f, 0.12f) * err, 0.66f, 0.97f);
    }
}
