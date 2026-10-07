using UnityEngine;

/// Drives ONE car through a Zone Race: keeps it permanently in the zone
/// (ghost autopilot), holds it on its slot in the train (speed control toward
/// the target race distance the orchestrator writes), hops rails — from the
/// player's input or an AI brain — and judges every row it crosses
/// (clean / hit / bonus), reporting back through RowResolved.
[DisallowMultipleComponent]
public class ZonePilot : MonoBehaviour
{
    public CarController Car { get; private set; }
    public ulong Id          { get; private set; }
    public bool  Human       { get; private set; }
    public int   Difficulty  { get; private set; }

    /// Unwrapped distance along the race (lap 2 = track length + …).
    public float RaceDist    { get; private set; }
    /// Current rail as a half-integer offset from the centreline (lane units).
    public float RailOff     { get; private set; }

    /// Written each frame by the orchestrator.
    [System.NonSerialized] public float Target;        // desired race distance
    [System.NonSerialized] public float TrainSpeed;    // base speed of the train
    [System.NonSerialized] public bool  Driving;       // after the green light
    [System.NonSerialized] public bool  InputAllowed;  // human rail hops enabled

    public int LastResolved { get; private set; } = -1;

    /// (pilot, row, result) — result: 0 clean, 1 hit, 2 bonus.
    public System.Action<ZonePilot, ZoneRow, int> RowResolved;

    ZoneCourse course;
    SpatialDriftTuning tuning;
    float lastWrapped;
    bool  begun;

    // Human rail input (same feel as the drift zone: taps / stick flicks).
    bool steerLatched = true;
    int  lastFlick;

    // AI.
    int decidedFor = -1;
    System.Random rng;

    static readonly float[] Accuracy  = { 0.80f, 0.88f, 0.94f, 0.975f };   // Easy..Ace
    static readonly float[] ReactTime = { 0.34f, 0.28f, 0.22f, 0.18f };

    float Spacing => course != null ? course.Spacing : 3.2f;

    public void Begin(CarController car, ZoneCourse zoneCourse, ulong id, bool human, int difficulty,
                      float startRaceDist, float startRailOff)
    {
        Car = car;
        course = zoneCourse;
        tuning = SpatialDriftTuning.Shared;
        Id = id; Human = human; Difficulty = Mathf.Clamp(difficulty, 0, 3);
        rng = new System.Random(unchecked((int)(id * 7919UL + 17UL)));

        // The normal drift-zone component would fight us (its own entry key,
        // rails and exit) — the whole race IS the zone, so park it.
        var sd = car.GetComponent<SpatialDriftMode>();
        if (sd != null) { sd.ForceExit(); sd.enabled = false; }

        car.spatialDriftActive    = true;
        car.zoneSnapSpeedOverride = tuning.railsLaneSwitchSpeed;
        car.zoneSpeedOverride     = 0f;

        RailOff = startRailOff;
        car.spatialDriftTargetLateral = RailOff * Spacing;
        RaceDist    = startRaceDist;
        lastWrapped = car.DistanceAlongTrack;
        begun = true;
    }

    /// Re-home after a teleport (the owner reset to the grid slot).
    public void Rebase(float raceDist)
    {
        RaceDist    = raceDist;
        lastWrapped = Car != null ? Car.DistanceAlongTrack : 0f;
    }

    void OnDestroy()
    {
        if (Car != null) { Car.zoneSpeedOverride = -1f; Car.zoneSnapSpeedOverride = -1f; }
    }

    void Update()
    {
        if (!begun || Car == null || Car.track == null || course == null) return;
        var track = Car.track;
        float len = track.Length;
        if (len <= 1f) return;

        // Unwrapped progress.  Before the green light the car is parked on the
        // grid (start distances are < one lap), so any jump is a re-home, not travel.
        float w = Car.DistanceAlongTrack;
        if (!Driving) RaceDist = w;
        else          RaceDist += WrapDelta(w - lastWrapped, len);
        lastWrapped = w;

        // Rails that fit the road right here.
        int p = course.PairsAt(w);
        RailOff = Mathf.Clamp(RailOff, -(p - 0.5f), p - 0.5f);

        if (Human)
        {
            int move = InputAllowed && !GamePause.InputBlocked ? PressDir() : 0;
            if (move != 0)
            {
                float before = RailOff;
                RailOff = Mathf.Clamp(RailOff + move, -(p - 0.5f), p - 0.5f);
                if (!Mathf.Approximately(before, RailOff)) Car.RailHop();
            }
        }
        else if (Driving) TickAI();

        Car.spatialDriftTargetLateral = RailOff * Spacing;

        // Hold the slot: base train speed plus a correction toward the target.
        if (Driving)
        {
            float err = Target - RaceDist;
            float v   = TrainSpeed + Mathf.Clamp(err * 2.6f, -0.5f * TrainSpeed, 0.7f * TrainSpeed);
            Car.zoneSpeedOverride = Mathf.Max(0f, v);
        }
        else Car.zoneSpeedOverride = 0f;

        ResolveRows();
    }

    // ── Rows ─────────────────────────────────────────────────────────────────
    void ResolveRows()
    {
        var rows = course.rows;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.index <= LastResolved) continue;
            if (row.raceDist > RaceDist) break;
            LastResolved = row.index;
            RowResolved?.Invoke(this, row, Judge(row));
        }
    }

    int Judge(ZoneRow row)
    {
        float lat = Car.LateralOffset;
        float s   = Spacing;
        for (int r = 0; r < row.Rails; r++)
        {
            float d = Mathf.Abs(lat - course.RailLateral(r, row.pairs));
            if (row.Blocked(r) && d < s * 0.45f) return 1;
            if (row.bonus == r && d < s * 0.5f)  return 2;
        }
        return 0;
    }

    /// The next row this car hasn't crossed yet (HUD / AI).
    public bool NextRow(out ZoneRow row)
    {
        foreach (var r in course.rows)
            if (r.index > LastResolved) { row = r; return true; }
        row = default;
        return false;
    }

    // ── AI ───────────────────────────────────────────────────────────────────
    void TickAI()
    {
        if (!NextRow(out var next)) return;
        float ahead = next.raceDist - RaceDist;
        float react = TrainSpeed * ReactTime[Difficulty];
        if (next.index == decidedFor || ahead > Mathf.Max(react, 8f)) return;
        decidedFor = next.index;

        int rp  = next.pairs;
        int cur = Mathf.Clamp(ZoneCourse.RailIndex(Mathf.Clamp(RailOff, -(rp - 0.5f), rp - 0.5f), rp), 0, rp * 2 - 1);
        int pick = cur;
        bool blunder = rng.NextDouble() > Accuracy[Difficulty];

        if (!blunder)
        {
            if (next.bonus >= 0 && Mathf.Abs(next.bonus - cur) <= 2 && rng.NextDouble() < 0.75) pick = next.bonus;
            else if (next.Blocked(cur)) pick = NearestFree(next, cur);
        }
        else if (!next.Blocked(cur))
        {
            // Misjudged it: drift onto a neighbouring blocker (if there is one).
            int dir = rng.Next(0, 2) == 0 ? -1 : 1;
            for (int d = 1; d <= 2 && pick == cur; d++)
            {
                if (next.Blocked(cur + d * dir)) pick = cur + d * dir;
                else if (next.Blocked(cur - d * dir)) pick = cur - d * dir;
            }
        }

        float off = ZoneCourse.RailOffset(pick, rp);
        if (!Mathf.Approximately(off, RailOff)) { RailOff = off; Car.RailHop(); }
    }

    int NearestFree(ZoneRow row, int from)
    {
        int first = rng.Next(0, 2) == 0 ? -1 : 1;
        for (int d = 1; d < row.Rails; d++)
        {
            int a = from + d * first, b = from - d * first;
            if (a >= 0 && a < row.Rails && !row.Blocked(a)) return a;
            if (b >= 0 && b < row.Rails && !row.Blocked(b)) return b;
        }
        return from;
    }

    // ── Human input: one tap / one stick flick = one rail ────────────────────
    int PressDir()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
        { steerLatched = true; lastFlick = -1; return -1; }
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
        { steerLatched = true; lastFlick = +1; return +1; }

        float axis = Car != null ? Car.LocalInput.steer : 0f;
        int   dir  = axis > 0.55f ? 1 : axis < -0.55f ? -1 : 0;
        if (steerLatched)
        {
            if (Mathf.Abs(axis) < 0.45f)               steerLatched = false;
            else if (dir != 0 && dir != lastFlick)      steerLatched = false;
            else return 0;
        }
        if (dir != 0) { steerLatched = true; lastFlick = dir; return dir; }
        return 0;
    }

    static float WrapDelta(float delta, float length)
    {
        delta %= length;
        if (delta >  length * 0.5f) delta -= length;
        if (delta < -length * 0.5f) delta += length;
        return delta;
    }
}
