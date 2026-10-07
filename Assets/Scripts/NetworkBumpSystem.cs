using Unity.Netcode;
using UnityEngine;

/// Multiplayer car-to-car bumping.  In client-authoritative networking each car
/// is simulated by its OWNER, so a central system can't shove them.  Instead
/// every owned car bumps ITSELF away from any car it overlaps (read from the
/// replicated spline positions Distance/Lateral).  Both owners do this, so the
/// contact is mutual — F-Zero style: a shove + a little speed cost, never a stop.
///
/// Auto-added by NetworkCarSync to each owned car.  Mirrors the single-player
/// CarBumpSystem tuning so MP contact feels the same.
[RequireComponent(typeof(NetworkCarSync))]
[RequireComponent(typeof(CarController))]
public class NetworkBumpSystem : MonoBehaviour
{
    [Tooltip("Cars closer than this along the track can touch (car length + slop).")]
    public float alongReach   = 4.2f;
    [Tooltip("Cars closer than this laterally can touch (car width + slop).")]
    public float lateralReach = 2.2f;
    [Tooltip("Speed kept by MY car on a contact (light — being bumped barely slows you).")]
    [Range(0.7f, 1f)] public float speedKept = 0.96f;
    [Tooltip("Degrees of heading wobble away from the contact.")]
    [Range(0f, 10f)]  public float headingNudge = 2.5f;

    CarController       car;
    NetworkCarSync      sync;
    Track               track;
    NetworkRaceDirector director;
    float               lastSound;

    void Awake()
    {
        car  = GetComponent<CarController>();
        sync = GetComponent<NetworkCarSync>();
    }

    void FixedUpdate()
    {
        // Only the owner pushes its own car; ghosts are driven by the network.
        if (sync == null || !sync.IsOwner) return;
        if (car == null || car.spatialDriftActive) return;

        // Time trial is non-contact — cars pass through each other.
        if (director == null) director = FindFirstObjectByType<NetworkRaceDirector>();
        if (director != null && (director.IsTimeTrial || director.IsZoneRace)) return;   // no contact

        if (track == null) { track = car.track; if (track == null) return; }

        float len = track.Length;
        if (len <= 0f) return;

        float myDist = car.DistanceAlongTrack;
        float myLat  = car.LateralOffset;

        foreach (var other in FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None))
        {
            if (other == sync) continue;

            float along = WrapDelta(other.Distance.Value - myDist, len);
            if (Mathf.Abs(along) > alongReach) continue;

            float latDiff = other.Lateral.Value - myLat;
            if (Mathf.Abs(latDiff) > lateralReach) continue;

            // Push myself away from the other car (it gets pushed by its owner).
            bool  sideBySide = Mathf.Abs(along) < 2f;
            float overlap    = lateralReach - Mathf.Abs(latDiff);
            float push       = (overlap * 0.5f + 0.05f) * (sideBySide ? 1f : 0.4f);
            float dir        = latDiff >= 0f ? -1f : 1f;   // other on my right → shove me left

            car.ApplyBump(dir * push, speedKept, dir * headingNudge);

            if (Time.time - lastSound > 0.35f)
            {
                lastSound = Time.time;
                car.GetComponent<CarAudio>()?.PlayBump(Mathf.Clamp01(car.Speed / 80f));
            }
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
