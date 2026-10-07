using System.Collections.Generic;
using UnityEngine;

/// Arcade car-to-car bumping, F-Zero style: contact shoves both cars apart and
/// trades a little speed, but never stops anyone dead (no Mario Kart full
/// stops).  Lives next to RaceManager (auto-added by it) and resolves every
/// racer pair each physics step.
///
/// Because all cars are spline-parameterised, "collision detection" is just a
/// box test in (along-track, lateral) space — no PhysX involved, perfectly
/// deterministic, and it costs a handful of float ops for a 4-car field.
///
/// Spatial Drift ghosts are untouchable in both directions (core rule).
[DisallowMultipleComponent]
public class CarBumpSystem : MonoBehaviour
{
    [Header("Contact Box (spline space)")]
    [Tooltip("Cars closer than this along the track can touch (car length + slop).")]
    public float alongReach   = 4.2f;
    [Tooltip("Cars closer than this laterally can touch (car width + slop).")]
    public float lateralReach = 2.2f;

    [Header("Response")]
    [Tooltip("Speed kept by the REAR car of a contact (it eats most of the cost).")]
    [Range(0.7f, 1f)] public float rearSpeedKept  = 0.94f;
    [Tooltip("Speed kept by the FRONT car (light tap — being hit barely slows you).")]
    [Range(0.7f, 1f)] public float frontSpeedKept = 0.99f;
    [Tooltip("Degrees of heading wobble applied away from the contact.")]
    [Range(0f, 10f)]  public float headingNudgeDeg = 2.5f;
    [Tooltip("Minimum seconds between bump SOUNDS for the same pair (the physical " +
             "push-apart itself runs every step so side-by-side grinding stays smooth).")]
    public float pairSoundCooldown = 0.35f;

    RaceManager race;
    Track       track;
    readonly Dictionary<int, float> lastSoundTime = new Dictionary<int, float>();

    void Awake() => race = GetComponent<RaceManager>();

    void FixedUpdate()
    {
        if (race == null || race.racers.Count < 2) return;
        if (track == null) { track = race.track; if (track == null) return; }

        float len = track.Length;
        var cars  = race.racers;

        for (int i = 0; i < cars.Count; i++)
        {
            var a = cars[i];
            if (a == null || a.spatialDriftActive) continue;

            for (int j = i + 1; j < cars.Count; j++)
            {
                var b = cars[j];
                if (b == null || b.spatialDriftActive) continue;

                float along = WrapDelta(b.DistanceAlongTrack - a.DistanceAlongTrack, len);
                if (Mathf.Abs(along) > alongReach) continue;

                float latDiff = b.LateralOffset - a.LateralOffset;
                if (Mathf.Abs(latDiff) > lateralReach) continue;

                ResolveContact(a, b, along, latDiff, i * 64 + j);
            }
        }
    }

    void ResolveContact(CarController a, CarController b,
                        float along, float latDiff, int pairKey)
    {
        // Push-apart: split the lateral overlap between both cars.  For a
        // rear-end hit (aligned lanes, big along gap) the lateral overlap is
        // large but a sideways shove would look wrong — scale it down and let
        // the speed trade carry the impact instead.
        bool  sideBySide = Mathf.Abs(along) < 2f;
        float overlap    = lateralReach - Mathf.Abs(latDiff);
        float push       = (overlap * 0.5f + 0.05f) * (sideBySide ? 1f : 0.4f);
        float dir        = latDiff >= 0f ? 1f : -1f;     // b sits on a's right

        // Rear car eats the speed cost; front car barely feels it.
        bool aIsRear = along >= 0f;     // b is ahead of a
        float aSpeedKept = aIsRear ? rearSpeedKept  : frontSpeedKept;
        float bSpeedKept = aIsRear ? frontSpeedKept : rearSpeedKept;

        a.ApplyBump(-dir * push, aSpeedKept, -dir * headingNudgeDeg);
        b.ApplyBump( dir * push, bSpeedKept,  dir * headingNudgeDeg);

        // Thud — only the player has CarAudio, so this is silent for AI-vs-AI.
        if (!lastSoundTime.TryGetValue(pairKey, out float last)
            || Time.time - last >= pairSoundCooldown)
        {
            lastSoundTime[pairKey] = Time.time;
            float intensity = Mathf.Clamp01(Mathf.Max(a.Speed, b.Speed) / 80f);
            a.GetComponent<CarAudio>()?.PlayBump(intensity);
            b.GetComponent<CarAudio>()?.PlayBump(intensity);
        }
    }

    static float WrapDelta(float delta, float length)
    {
        if (length <= 0f) return delta;
        delta %= length;
        if (delta >  length * 0.5f) delta -= length;
        if (delta < -length * 0.5f) delta += length;
        return delta;
    }
}
