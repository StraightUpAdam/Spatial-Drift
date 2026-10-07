using UnityEngine;
using System;

[Serializable]
public struct BoostZone
{
    public float  minRPM;
    public float  maxRPM;
    public float  boostSpeed;   // m/s added on shift
    public string label;
}

public class GearSystem : MonoBehaviour
{
    public const int GearCount = 10;

    [Header("Gear Max Speeds (m/s)")]
    public float[] gearMaxSpeeds = { 15f, 27f, 40f, 54f, 68f, 82f, 97f, 112f, 128f, 145f };

    [Header("Per-Gear Acceleration (m/s²)")]
    public float[] gearAcceleration =
        { 42f, 36f, 29f, 23f, 18f, 13f, 10f, 7.5f, 5.5f, 4f };

    [Header("Boost Zones (low → high RPM)")]
    public BoostZone[] boostZones = new BoostZone[]
    {
        new BoostZone { minRPM = 0.63f, maxRPM = 0.72f, boostSpeed = 2.5f,  label = "GOOD"    },
        new BoostZone { minRPM = 0.72f, maxRPM = 0.81f, boostSpeed = 4.5f,  label = "GREAT"   },
        new BoostZone { minRPM = 0.81f, maxRPM = 0.87f, boostSpeed = 8.0f,  label = "PERFECT" },
        new BoostZone { minRPM = 0.87f, maxRPM = 0.94f, boostSpeed = 4.5f,  label = "GREAT"   },
        new BoostZone { minRPM = 0.94f, maxRPM = 0.99f, boostSpeed = 2.5f,  label = "GOOD"    },
    };

    [Header("Boost Timing")]
    public float boostDuration = 0.45f;

    [Header("Engine Power Curve")]
    [Tooltip("Below this fraction of the gear's RPM range, the engine 'lugs' (low torque). " +
             "0 = fully linear RPM rise (no lugging). Try 0.35 to add a lug penalty.")]
    [Range(0f, 0.5f)] public float powerbandEntry = 0f;

    [Tooltip("Acceleration multiplier at zero RPM. Lower = bigger lug penalty.")]
    [Range(0.05f, 1f)] public float minEfficiency = 1f;

    // ── Public state ──────────────────────────────────────────────────────────
    /// 0 = neutral, 1–10 = engaged gear.
    public int   CurrentGear      { get; private set; } = 0;
    /// Last non-zero gear engaged.  Stays sticky across neutral transitions so
    /// we can detect what counts as a "sequential upshift" for boost purposes.
    public int   LastEngagedGear  { get; private set; } = 0;
    public float RPM              { get; private set; }
    public float RawRPM           { get; private set; }
    public float BoostRemaining   { get; private set; }
    public float ActiveBoostSpeed { get; private set; }
    public bool  IsBoosting       => BoostRemaining > 0f;
    public bool  InNeutral        => CurrentGear == 0;

    public float CurrentMaxSpeed     =>
        CurrentGear > 0 ? gearMaxSpeeds[CurrentGear - 1] : 0f;

    public float CurrentAcceleration =>
        CurrentGear > 0 ? gearAcceleration[CurrentGear - 1] * AccelerationEfficiency : 0f;

    public float AccelerationEfficiency
    {
        get
        {
            if (CurrentGear == 0)    return 0f;
            if (powerbandEntry <= 0f) return 1f;   // fully linear case

            if (RawRPM < 0f)
                return Mathf.Lerp(0.04f, minEfficiency, Mathf.Clamp01(1f + RawRPM * 2.5f));
            if (RawRPM < powerbandEntry)
                return Mathf.Lerp(minEfficiency, 1f, RawRPM / powerbandEntry);
            return 1f;
        }
    }

    /// Fires whenever the gear changes.  int = boost-zone index hit
    /// (-1 = downshift / neutral / no zone).
    public event Action<int> OnShift;

    // ── Per-frame update (called from CarController.FixedUpdate) ──────────────
    public void UpdateRPM(float speed)
    {
        if (CurrentGear > 0)
        {
            float rangeMin = CurrentGear > 1 ? gearMaxSpeeds[CurrentGear - 2] : 0f;
            float range    = CurrentMaxSpeed - rangeMin;
            RawRPM = range > 0f ? (speed - rangeMin) / range : 0f;
            RPM    = Mathf.Clamp01(RawRPM);
        }
        // In neutral: RPM stays frozen at its last-engaged value so the needle
        // shows you which boost zone you committed to when you started the shift.
        // (AccelerationEfficiency still returns 0 in neutral — no power either way.)

        if (BoostRemaining > 0f)
            BoostRemaining = Mathf.Max(0f, BoostRemaining - Time.fixedDeltaTime);
        else
            ActiveBoostSpeed = 0f;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Engage a specific gear.  Called by GearShifter when the stick lands on
    //  a cell.  shiftRPM is the RPM captured at the moment we left the previous
    //  gear cell — that's what determines the boost zone for upshifts.
    //
    //  Boost is awarded ONLY for a genuine sequential upshift, evaluated
    //  against LastEngagedGear (which is sticky across neutral).  So:
    //    1 → 2                  → boost eligible
    //    1 → neutral → 2        → boost eligible
    //    1 → neutral → 1        → no boost (same gear)
    //    1 → neutral → 3        → no boost (skip — this is also blocked by
    //                                       GearShifter so it shouldn't happen)
    //    5 → 2 (downshift)      → no boost
    //    auto-downshift         → no boost (caller passes shiftRPM = 0)
    // ──────────────────────────────────────────────────────────────────────────
    public void SetGear(int newGear, float shiftRPM)
    {
        newGear = Mathf.Clamp(newGear, 0, GearCount);
        if (newGear == CurrentGear) return;

        int zone = -1;

        // Boost only if this is a sequential upshift relative to the LAST gear
        // we were actually engaged in (not the gear we just left, which might
        // be neutral).
        if (newGear > 0 && newGear == LastEngagedGear + 1)
        {
            zone = ZoneAt(shiftRPM);
            if (zone >= 0)
            {
                ActiveBoostSpeed = boostZones[zone].boostSpeed;
                BoostRemaining   = boostDuration;
            }
        }

        CurrentGear = newGear;
        if (newGear > 0) LastEngagedGear = newGear;

        OnShift?.Invoke(zone);
    }

    public int ZoneAt(float rpm)
    {
        for (int i = 0; i < boostZones.Length; i++)
            if (rpm >= boostZones[i].minRPM && rpm < boostZones[i].maxRPM) return i;
        return -1;
    }

    /// Apply a boost from a source other than gear shifting (e.g. drift release).
    /// Overrides any in-progress boost — the new value wins.  IsBoosting becomes
    /// true so camera/particle/warp effects react identically to shift boosts.
    public void ApplyExternalBoost(float boostSpeed, float duration)
    {
        if (boostSpeed <= 0f || duration <= 0f) return;
        ActiveBoostSpeed = boostSpeed;
        BoostRemaining   = duration;
    }

    /// Reset to a clean pre-race state: neutral, no RPM, no boost, no sticky
    /// "last engaged" gear (so the first shift to gear 1 is eligible for boost
    /// again like a fresh launch).  Called by RaceManager before each race.
    public void ResetForRace()
    {
        CurrentGear      = 0;
        LastEngagedGear  = 0;
        RPM              = 0f;
        RawRPM           = 0f;
        BoostRemaining   = 0f;
        ActiveBoostSpeed = 0f;
        OnShift?.Invoke(-1);
    }
}
