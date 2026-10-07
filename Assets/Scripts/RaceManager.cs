using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Owns the race lifecycle: pre-race countdown → racing → finished.
///
/// Multiplayer-ready by construction: operates on a `List<CarController>` from
/// day one (with just one entry in the single-player prototype).  When netcode
/// is added later, the server runs this component as authority and clients
/// just read state through the public properties + events.
///
/// Lap detection uses spline distance.  Every frame we compare each car's
/// `DistanceAlongTrack` to its previous value — a forward wrap from "near the
/// end" to "near the start" counts as one lap.  Backward wraps (driving in
/// reverse across the line) are ignored.
[DisallowMultipleComponent]
public class RaceManager : MonoBehaviour
{
    public enum State { PreRace, Racing, Finished }

    [Header("Race")]
    [Range(1, 20)] public int   totalLaps         = 3;
    [Range(0f, 10f)] public float countdownDuration = 3f;
    [Tooltip("How long the 'GO!' banner stays on screen after countdown ends.")]
    [Range(0f, 3f)] public float goBannerDuration  = 0.9f;

    [Header("References (auto-found if blank)")]
    public Track            track;
    public List<CarController> racers     = new List<CarController>();
    [Tooltip("The local player's car — used for HUD focus and best-lap PlayerPrefs.")]
    public CarController    localPlayer;

    [Header("Restart")]
    public KeyCode restartKey = KeyCode.R;

    [Header("Time Trial")]
    [Tooltip("Solo against the clock: forces 1 lap, instant restart (R / B), and no " +
             "terminal results screen — finishing freezes the time and you keep driving.")]
    public bool timeTrial = false;
    [Tooltip("Gamepad button that restarts the run instantly (Xbox B = button 1).")]
    public KeyCode restartGamepadButton = KeyCode.JoystickButton1;

    [Header("Return To Menu")]
    [Tooltip("Scene loaded when the player presses Esc / Start.  Must be in Build Settings.")]
    public string menuScene  = "MainMenu";
    public KeyCode menuKey   = KeyCode.Escape;
    [Tooltip("Gamepad button that also returns to the menu (Xbox: button 7 = Start/Menu).")]
    public KeyCode menuGamepadButton = KeyCode.JoystickButton7;

    // ── Public state ──────────────────────────────────────────────────────────
    public State CurrentState       { get; private set; } = State.PreRace;
    public float CountdownRemaining { get; private set; }
    public float GoBannerRemaining  { get; private set; }
    public float TimeSinceStart     { get; private set; }

    // Time-trial state, exposed so the ghost recorder/replayer can sync to it.
    public bool TimeTrial          => timeTrial;
    public bool TimeTrialArmed     => ttArmed;                 // on the line, clock at 0
    public bool TimeTrialFinished  => ttFinished;              // crossed the line, frozen
    public bool TimeTrialRunning   => timeTrial && !ttArmed && !ttFinished
                                                 && CurrentState == State.Racing;

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action               OnRaceStart;
    public event Action<RacerEntry>   OnLapComplete;
    public event Action<RacerEntry>   OnRacerFinish;
    public event Action               OnRaceFinish;     // all racers done
    public event Action               OnTimeTrialBest;  // local player set a new PB (ghost save)

    /// One entry per car.  Lap counts, lap times, current lap-in-progress time,
    /// best lap, and finish state all live here.  HUD reads from
    /// `LocalEntry` to display the local player's progress.
    [Serializable]
    public class RacerEntry
    {
        public CarController car;
        public int    currentLap;      // 1-based once Racing begins; 0 in pre-race
        public float  currentLapTime;
        public float  totalTime;
        public List<float> lapTimes = new List<float>();
        public float  bestLap = float.PositiveInfinity;
        public bool   finished;
        public bool   newBestLastLap;
        public int    finishPosition;  // 1-based order across the line; 0 while racing

        // internal: previous frame's distance, used for wrap detection
        public float  lastDistance;
        // internal: NET forward progress this lap.  A line crossing only counts
        // when ~a full track length was actually driven — turning around at the
        // start and nipping back over the line banks nothing.
        public float  netProgress;

        /// This lap entered Spatial Drift at some point (it can still set BEST
        /// LAP, but not PURE LAP).
        public bool   usedDrift;
        public bool   newPureLastLap;
    }

    readonly Dictionary<CarController, RacerEntry> entryByCar =
        new Dictionary<CarController, RacerEntry>();

    string bestLapPrefKey;
    string modeTag;
    int    finishCounter;
    bool   ttFinished;        // time-trial: the timed run has crossed the line
    bool   ttArmed;           // time-trial: on the line, clock frozen until you move
    bool   ttRearmPending;    // time-trial: finished while ghost-piloting → re-arm next frame

    /// Number of cars in the race (player + AI).  HUD uses this to decide
    /// whether to show position ("P2 / 4" is meaningless in a solo time trial).
    public int RacerCount => entryByCar.Count;

    // ════════════════════════════════════════════════════════════════════════════
    void Awake()
    {
        if (timeTrial) totalLaps = 1;   // a time-trial run is always a single flying lap

        if (track == null) track = FindFirstObjectByType<Track>();

        if (racers == null) racers = new List<CarController>();
        racers.RemoveAll(c => c == null);
        if (racers.Count == 0)
            racers.AddRange(FindObjectsByType<CarController>(FindObjectsSortMode.None));

        if (localPlayer == null)
        {
            // Prefer the human car — never auto-pick an AI as the local player.
            foreach (var r in racers)
                if (r != null && !r.aiControlled) { localPlayer = r; break; }
            if (localPlayer == null && racers.Count > 0) localPlayer = racers[0];
        }

        // Key the best lap by MAP (not scene — every solo map shares the "Race"
        // scene) and by mode, so a Time-Trial PB is its own record per track.
        modeTag = timeTrial ? TrackRecords.TimeTrial : TrackRecords.Race;
        bestLapPrefKey = $"BestLap_{modeTag}_{MatchConfig.MapName}";
        TrackRecords.ValidateCurrent();     // edited track → its old records are gone

        // Car-to-car bumping — pointless solo, so only with opponents present.
        if (racers.Count > 1 && GetComponent<CarBumpSystem>() == null)
            gameObject.AddComponent<CarBumpSystem>();
    }

    void Start()
    {
        foreach (var car in racers)
        {
            if (car == null) continue;
            entryByCar[car] = new RacerEntry
            {
                car          = car,
                lastDistance = car.DistanceAlongTrack,
            };
        }

        // Load persistent best lap for the local player only.
        if (localPlayer != null && entryByCar.TryGetValue(localPlayer, out var le))
            le.bestLap = TrackRecords.BestLap(modeTag, MatchConfig.MapName, false);
        StartingBestLap = TrackRecords.BestLap(modeTag, MatchConfig.MapName, false);
        StartingPureLap = TrackRecords.BestLap(modeTag, MatchConfig.MapName, true);

        // Time trial uses a standing start (no countdown — the clock waits for
        // your first movement); a normal race runs the 3-2-1 countdown.
        if (timeTrial) ArmTimeTrial();
        else           EnterPreRace();
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  State transitions
    // ════════════════════════════════════════════════════════════════════════════
    void EnterPreRace()
    {
        CurrentState       = State.PreRace;
        CountdownRemaining = countdownDuration;
        GoBannerRemaining  = 0f;
        TimeSinceStart     = 0f;

        // Disable driving input on all racers; reset positions & gear state.
        foreach (var car in racers)
        {
            if (car == null) continue;
            car.inputEnabled = false;
            car.ResetToStart();

            var gs = car.GetComponent<GearSystem>();
            if (gs != null) gs.ResetForRace();

            var sh = car.GetComponent<GearShifter>();
            if (sh != null) sh.ResetToStart();
        }

        foreach (var entry in entryByCar.Values)
        {
            entry.currentLap     = 0;
            entry.currentLapTime = 0f;
            entry.usedDrift      = false;
            entry.totalTime      = 0f;
            entry.lapTimes.Clear();
            entry.finished       = false;
            entry.newBestLastLap = false;
            entry.finishPosition = 0;
            entry.lastDistance   = entry.car != null ? entry.car.DistanceAlongTrack : 0f;
            entry.netProgress    = 0f;
            // NOTE: entry.bestLap is preserved across restarts so "Best" carries
            // over within a session.  PlayerPrefs handles persistence across runs.
        }
        finishCounter = 0;
    }

    void StartRace()
    {
        CurrentState      = State.Racing;
        GoBannerRemaining = goBannerDuration;

        foreach (var car in racers) if (car != null) car.inputEnabled = true;
        foreach (var entry in entryByCar.Values)
        {
            entry.currentLap = 1;
            // Capture the current distance NOW so the first wrap detection is
            // measured against the on-the-line start position, not stale data.
            if (entry.car != null) entry.lastDistance = entry.car.DistanceAlongTrack;
        }

        OnRaceStart?.Invoke();
    }

    void FinishRace()
    {
        CurrentState = State.Finished;
        foreach (var car in racers) if (car != null) car.inputEnabled = false;
        OnRaceFinish?.Invoke();
    }

    /// Time-trial: throw the car back on the line, armed and ready — no countdown,
    /// no menu.  The clock stays at 0 until the car first moves (standing start),
    /// so you can get your bearings.  Best lap is preserved across attempts.
    void ArmTimeTrial()
    {
        ttFinished     = false;
        ttArmed        = true;
        ttRearmPending = false;

        foreach (var car in racers)
        {
            if (car == null) continue;
            car.ResetToStart();
            car.inputEnabled = true;

            var gs = car.GetComponent<GearSystem>();
            if (gs != null) gs.ResetForRace();
            var sh = car.GetComponent<GearShifter>();
            if (sh != null) sh.ResetToStart();
        }

        foreach (var entry in entryByCar.Values)
        {
            entry.currentLap     = 1;
            entry.currentLapTime = 0f;
            entry.usedDrift      = false;
            entry.totalTime      = 0f;
            entry.lapTimes.Clear();
            entry.finished       = false;
            entry.newBestLastLap = false;
            entry.finishPosition = 0;
            entry.lastDistance   = entry.car != null ? entry.car.DistanceAlongTrack : 0f;
            entry.netProgress    = 0f;
        }

        CurrentState      = State.Racing;
        GoBannerRemaining = 0f;
        TimeSinceStart    = 0f;
        OnRaceStart?.Invoke();   // HUD clears any banner; ghost (future) restarts
    }

    /// Time trial: throw the car back on the line (pause menu "Restart run").
    public void RestartTimeTrial()
    {
        if (timeTrial) ArmTimeTrial();
    }

    /// The PlayerPrefs key this race's best lap is stored under.
    public string BestLapKey => bestLapPrefKey;

    /// The personal best as it stood when the race loaded (results compare
    /// against this to call out a NEW personal best).
    public float StartingBestLap { get; private set; } = float.PositiveInfinity;
    /// Stored PURE LAP (no Spatial Drift) when this session began.
    public float StartingPureLap { get; private set; } = float.PositiveInfinity;
    /// Fastest drift-free lap by the local player this session.
    public float SessionPureLap  { get; private set; } = float.PositiveInfinity;
    public string RecordMode => modeTag;

    // ════════════════════════════════════════════════════════════════════════════
    //  Per-frame
    // ════════════════════════════════════════════════════════════════════════════
    void Update()
    {
        switch (CurrentState)
        {
            case State.PreRace:
                CountdownRemaining -= Time.deltaTime;
                if (CountdownRemaining <= 0f) StartRace();
                break;

            case State.Racing:
                // Time trial: finished mid-Spatial-Drift → kick out + reset to start.
                if (timeTrial && ttRearmPending) { ArmTimeTrial(); break; }

                // Time trial: R / B restarts the run instantly at any moment.
                // While Spatial Drift is active the GAMEPAD button is ignored —
                // B doubles as a rhythm-lane input there (R still works).
                bool sdActive = localPlayer != null && localPlayer.spatialDriftActive;
                if (timeTrial && !GamePause.InputBlocked &&
                    (Input.GetKeyDown(restartKey) ||
                     (!sdActive && Input.GetKeyDown(restartGamepadButton))))
                {
                    ArmTimeTrial();
                    break;
                }

                // Standing start: while armed the clock sits at 0 so you can line
                // up.  It starts the instant the car actually rolls.
                if (timeTrial && ttArmed)
                {
                    if (localPlayer != null && Mathf.Abs(localPlayer.Speed) > 0.25f)
                    {
                        ttArmed = false;
                        TimeSinceStart = 0f;
                        foreach (var e in entryByCar.Values)
                        {
                            e.currentLapTime = 0f;
                            e.totalTime      = 0f;
                            e.usedDrift      = false;
                            e.lastDistance   = e.car != null ? e.car.DistanceAlongTrack : 0f;
                        }
                    }
                }

                // Clock + line-watching pause while armed (waiting to roll) or
                // while a finished run is frozen (coasting until you restart).
                if (!(timeTrial && (ttArmed || ttFinished)))
                {
                    TimeSinceStart += Time.deltaTime;
                    if (GoBannerRemaining > 0f)
                        GoBannerRemaining = Mathf.Max(0f, GoBannerRemaining - Time.deltaTime);

                    foreach (var entry in entryByCar.Values)
                    {
                        if (entry.finished || entry.car == null) continue;
                        entry.currentLapTime += Time.deltaTime;
                        entry.totalTime      += Time.deltaTime;
                        if (entry.car.spatialDriftActive) entry.usedDrift = true;
                        CheckLapCrossing(entry);
                    }
                }

                if (!timeTrial && AllFinished()) FinishRace();
                break;

            case State.Finished:
                // The results screen (RaceMenus) owns what happens next.
                break;
        }
    }

    // ── Lap-crossing detection (forward wrap only) ───────────────────────────
    void CheckLapCrossing(RacerEntry entry)
    {
        if (track == null) return;
        float len = track.Length;
        if (len <= 0f) return;

        float prev = entry.lastDistance;
        float curr = entry.car.DistanceAlongTrack;

        // Accumulate signed progress: driving backward SUBTRACTS, so the exploit
        // of reversing over the line and re-crossing forward nets ~zero.
        entry.netProgress += WrapDelta(curr - prev, len);

        // A forward wrap on a closed spline shows up as previous distance in
        // the upper half (near `len`) and current distance in the lower half
        // (near 0).  Backward wraps go the other way — ignored.  The crossing
        // only banks a lap when (almost) a full track length was truly driven.
        if (track.IsClosed)
        {
            float quarter = len * 0.25f;
            if (prev > len - quarter && curr < quarter
                && entry.netProgress >= len * 0.85f)
            {
                entry.netProgress -= len;
                CompleteLap(entry);
            }
        }
        else
        {
            // Open spline: complete when distance ≥ len.  (Not used by default
            // scene which is closed, but useful if you author point-to-point.)
            if (prev < len && curr >= len) CompleteLap(entry);
        }

        entry.lastDistance = curr;
    }

    static float WrapDelta(float delta, float length)
    {
        if (length <= 0f) return delta;
        delta %= length;
        if (delta >  length * 0.5f) delta -= length;
        if (delta < -length * 0.5f) delta += length;
        return delta;
    }

    void CompleteLap(RacerEntry entry)
    {
        float lapTime = entry.currentLapTime;
        entry.lapTimes.Add(lapTime);

        bool isBest = lapTime < entry.bestLap;
        if (isBest)
        {
            entry.bestLap        = lapTime;
            entry.newBestLastLap = true;

        }
        else
        {
            entry.newBestLastLap = false;
        }

        // Persist the local player's records (BEST + PURE) across sessions.
        entry.newPureLastLap = false;
        if (entry.car == localPlayer)
        {
            var (_, pure) = TrackRecords.SubmitLap(modeTag, MatchConfig.MapName, lapTime, entry.usedDrift);
            entry.newPureLastLap = pure;
            if (!entry.usedDrift && lapTime < SessionPureLap) SessionPureLap = lapTime;
        }
        entry.usedDrift = false;            // the next lap starts clean

        // Time trial: a crossing completes the timed run.  Freeze the clock and
        // wait for the player to restart (R / B) — no terminal results screen, so
        // they can immediately go again without a menu round-trip.
        if (timeTrial)
        {
            ttFinished = true;
            if (isBest && entry.car == localPlayer) OnTimeTrialBest?.Invoke();
            OnLapComplete?.Invoke(entry);   // banner shows the run time / NEW BEST

            // If the line was crossed while phased into Spatial Drift (ghost
            // autopilot), freezing here would leave the car ghost-piloting with a
            // dead clock.  Flag a re-arm so next frame kicks out of it and resets
            // to the start line for a clean attempt.
            if (entry.car != null && entry.car.spatialDriftActive) ttRearmPending = true;
            return;
        }

        // Settle the entry's finished / lap-counter state BEFORE we raise
        // events, so handlers see the canonical post-crossing state.
        bool wasFinalLap = entry.currentLap >= totalLaps;
        if (wasFinalLap)
        {
            entry.finished       = true;
            entry.finishPosition = ++finishCounter;
        }
        else
        {
            entry.currentLap++;
            entry.currentLapTime = 0f;
            entry.usedDrift      = entry.car != null && entry.car.spatialDriftActive;
        }

        OnLapComplete?.Invoke(entry);

        if (wasFinalLap)
        {
            OnRacerFinish?.Invoke(entry);
            // The race ends when the LOCAL PLAYER crosses the line — AI still on
            // track keep their already-banked finish positions ahead of them.
            // (AI that finish earlier keep driving until then; looks alive.)
            if (entry.car == localPlayer && CurrentState == State.Racing)
                FinishRace();
        }
    }

    bool AllFinished()
    {
        foreach (var entry in entryByCar.Values)
            if (!entry.finished) return false;
        return entryByCar.Count > 0;
    }

    // ── Live position ranking ─────────────────────────────────────────────────
    /// 1-based race position.  Finished cars hold their crossing order; cars
    /// still racing rank by total progress (laps banked + distance into the
    /// current lap).  Linear scan — trivial for a handful of cars.
    public int GetLivePosition(RacerEntry entry)
    {
        if (entry == null) return 0;
        if (entry.finished && entry.finishPosition > 0) return entry.finishPosition;

        float myProgress = Progress(entry);
        int   pos        = 1;
        foreach (var other in entryByCar.Values)
        {
            if (other == entry) continue;
            if (other.finished)               { pos++; continue; }   // already across
            if (Progress(other) > myProgress)   pos++;
        }
        return pos;
    }

    float Progress(RacerEntry e) =>
        e.lapTimes.Count * (track != null ? track.Length : 0f)
        + (e.car != null ? e.car.DistanceAlongTrack : 0f);

    // ── Public read-only accessors (HUD reads from these) ────────────────────
    public RacerEntry GetEntry(CarController car) =>
        car != null && entryByCar.TryGetValue(car, out var e) ? e : null;

    public RacerEntry LocalEntry => GetEntry(localPlayer);

    /// "M:SS.ss" formatted lap/total time, or "-:--.--" for unset values.
    public static string FormatTime(float t)
    {
        if (t < 0f || float.IsInfinity(t) || float.IsNaN(t)) return "-:--.--";
        int   minutes = (int)(t / 60f);
        float seconds = t - minutes * 60f;
        return $"{minutes}:{seconds:00.00}";
    }
}
