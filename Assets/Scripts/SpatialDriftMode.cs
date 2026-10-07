using System;
using System.Collections.Generic;
using UnityEngine;

/// The game's namesake mechanic.  When you've held top gear at high RPM long
/// enough, the car phases into a "ghost" autopilot — it locks to the spline
/// centerline at boosted speed, becomes immune to collisions, and the arrow
/// keys turn into rhythm-game input (DDR-style scrolling notes).  Hit prompts
/// to sustain the mode; misses bleed stability.  When stability hits zero you
/// drop back into manual control with a boost scaled by your score.
///
/// Phases:
///   Idle     → normal driving
///   Entering → the entry key TEARS A RIFT open in the road ahead
///              (SpatialRift); you're still driving.  Crossing its plane
///              flashes you into ghost form (CarGhost) and starts…
///   Active   → ghost autopilot + minigame input.  Lives ≤ 0 → Exit, which
///              bursts a rift open around the car and spits it back out.
///
/// Auto-attached by CarController.Awake.
[DisallowMultipleComponent]
public class SpatialDriftMode : MonoBehaviour
{
    public enum Phase { Idle, Entering, Active }

    [Header("Tuning")]
    [Tooltip("ScriptableObject holding every Spatial Drift tuning value.  Leave " +
             "blank and we'll Resources.Load 'SpatialDriftTuning' at startup, " +
             "or fall back to an in-memory default.  Edits to the SO persist " +
             "across Play sessions, unlike Inspector edits on the component.")]
    public SpatialDriftTuning tuning;

    // ── Proxy fields kept under their old names for external readers
    //    (CarController accesses these — keeping the names avoids touching it).
    public float speedMultiplier     => tuning != null ? tuning.speedMultiplier     : 1.4f;
    public float speedAdditive       => tuning != null ? tuning.speedAdditive       : 25f;
    public float headingLockResponse => tuning != null ? tuning.headingLockResponse : 8f;
    public float postExitGraceDuration => tuning != null ? tuning.postExitGraceDuration : 1f;

    /// Lateral movement speed for CarController's snap: lane-switch speed while
    /// the Rails minigame is live, the usual center-snap speed otherwise.
    public float lateralSnapSpeed =>
        tuning == null ? 60f
        : (RailsVariant && CurrentPhase == Phase.Active) ? tuning.railsLaneSwitchSpeed
        : tuning.lateralSnapSpeed;

    public bool RailsVariant =>
        tuning != null && tuning.minigame == SpatialDriftTuning.Minigame.Rails;

    // ── Public read-only state (HUD / VFX read these) ────────────────────────
    public Phase  CurrentPhase       { get; private set; } = Phase.Idle;
    public int    Lives              { get; private set; }
    public int    Score              { get; private set; }
    public IReadOnlyList<Note> Notes => notes;

    /// Seconds until the mode can be entered again after an exit (0 = ready).
    public float CooldownRemaining => Mathf.Max(0f, cooldownUntil - Time.time);

    /// Seconds of Active time left before the max-duration auto-exit;
    /// -1 when inactive or unlimited (maxDuration = 0).
    public float ActiveRemaining =>
        CurrentPhase == Phase.Active && tuning != null && tuning.maxDuration > 0f
            ? Mathf.Max(0f, tuning.maxDuration - (Time.time - activeStartTime))
            : -1f;

    /// True when the player can press the entry key to enter Spatial Drift.
    /// HUD reads this to show the "PRESS E" prompt.
    public bool CanEnter
    {
        get
        {
            if (CurrentPhase != Phase.Idle || gears == null || tuning == null) return false;
            if (Time.time < cooldownUntil) return false;   // still recharging
            if (tuning.requiresTopGear && gears.CurrentGear != GearSystem.GearCount) return false;
            if (gears.RPM < tuning.minRPMToEnter) return false;
            return true;
        }
    }

    public class Note
    {
        public KeyCode key;          // arrow key (or face button equivalent) to press
        public float   hitTime;      // game time at which the HEAD should be hit
        public float   leadTime;     // lead time at the moment this note was spawned
        public float   duration;     // 0 = tap; > 0 = hold of this many seconds
        public bool    consumed;     // tap: hit; hold: head was caught
        public bool    holdComplete; // hold: completed (success or early release)
    }

    /// 0..1 — how far through the difficulty ramp we currently are.
    public float DifficultyT => tuning != null && tuning.difficultyRampEnabled && CurrentPhase == Phase.Active
        ? Mathf.Clamp01((Time.time - activeStartTime) / Mathf.Max(0.01f, tuning.rampDuration))
        : 0f;

    public float CurrentSpawnInterval => tuning != null
        ? Mathf.Lerp(tuning.noteSpawnInterval, tuning.noteSpawnIntervalMin, DifficultyT) : 0.65f;
    public float CurrentLeadTime      => tuning != null
        ? Mathf.Lerp(tuning.noteLeadTime,      tuning.noteLeadTimeMin,      DifficultyT) : 1.6f;
    public float CurrentHitWindow     => tuning != null
        ? Mathf.Lerp(tuning.hitWindow,         tuning.hitWindowMin,         DifficultyT) : 0.20f;

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action       OnEnterBegin;      // the rift starts tearing open ahead
    public event Action       OnActivate;        // crossed the rift → ghost
    public event Action<int>  OnExit;            // arg: final score
    public event Action<Note> OnNoteSpawn;
    public event Action<int, KeyCode> OnHit;     // (tier, lane key) — 0 = miss/bad, 1 = good, 2 = perfect
    public event Action<Note> OnNoteMissed;      // a note timed out without being hit
    public event Action<int>  OnLifeLost;        // arg: remaining lives after the loss

    // ── Internal ──────────────────────────────────────────────────────────────
    readonly List<Note> notes = new List<Note>();
    float nextNoteSpawnTime;
    float activeStartTime;
    float cooldownUntil;                        // recharge gate (Time.time)
    readonly int[] _freeLaneBuf = new int[4];   // cached for stacking check

    // Rails minigame state.  Rails are SYMMETRIC PAIRS around the centreline at
    // FIXED spacing — offsets ±0.5 / ±1.5 / ±2.5 lane-units → 2, 4 or 6 rails
    // depending on road width.  Fixed offsets mean a rail never slides sideways
    // as the road breathes; a narrowing road simply removes the outermost pair
    // (6 → 4 → 2), nudging only cars that were riding it.
    sealed class RailsRow
    {
        public float dist;                                       // wrapped track distance
        public readonly List<float>      laneLats = new List<float>();  // per blocked rail
        public readonly List<int>        groupOf  = new List<int>();    // rail → merged box
        public readonly List<GameObject> gos      = new List<GameObject>();  // merged boxes
        public bool  resolved;
    }
    readonly List<RailsRow> railsRows = new List<RailsRow>();
    GameObject railsRoot;
    float      railsOffset;        // player's rail: half-integer lane-units from centre
    float      railsPathOffset;    // spawner's guaranteed-open rail (walks ≤2 per row)
    float      railsSpawnDist;
    bool       railsSteerLatched;
    static Material _railsObstacleMat;

    // Rail line visuals — glowing strips along the whole track, shown only while
    // the mode is active; the player's current rail glows brighter.  Each rail
    // has a paired X-RAY renderer (ZTest Greater) that faintly draws the line
    // where the world hides it, so guides read continuous through obstacles.
    readonly List<MeshRenderer> railLines       = new List<MeshRenderer>();
    readonly List<MeshRenderer> railLinesXray   = new List<MeshRenderer>();
    readonly List<float>        railLineOffsets = new List<float>();
    static readonly Color RailDim    = new Color(0.55f, 0.32f, 1.00f, 0.28f);
    static readonly Color RailBright = new Color(0.95f, 0.70f, 1.00f, 0.85f);
    const float RailXrayAlpha = 0.32f;   // ghost pass = this fraction of the alpha

    CarController car;
    GearSystem    gears;
    GearShifter   shifter;
    CarGhost      ghostFx;

    // Rift entry.
    SpatialRift   rift;
    float         entryStart;
    const float   RiftLeadTime   = 0.72f;   // seconds of travel to the rift at entry speed
    const float   RiftMinAhead   = 26f, RiftMaxAhead = 120f;
    const float   EntryTimeout   = 1.8f;    // crossing is forced after this (car stopped, etc.)
    static readonly Color RiftTint = new Color(1f, 0.45f, 0.95f);

    static readonly KeyCode[] AllDirs =
    {
        KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
    };

    // ════════════════════════════════════════════════════════════════════════════
    void Awake()
    {
        car     = GetComponent<CarController>();
        gears   = GetComponent<GearSystem>();
        shifter = GetComponent<GearShifter>();

        // Resolve tuning: explicit inspector ref > Resources/SpatialDriftTuning > in-memory default.
        if (tuning == null) tuning = Resources.Load<SpatialDriftTuning>("SpatialDriftTuning");
        if (tuning == null)
        {
            tuning      = ScriptableObject.CreateInstance<SpatialDriftTuning>();
            tuning.name = "Default (in-memory)";
            Debug.LogWarning("[SpatialDriftMode] No SpatialDriftTuning asset found. " +
                             "Using runtime defaults — run 'Racing Game / Create Spatial Drift Tuning Asset' " +
                             "to create an editable asset that persists across plays.");
        }

        // Apply any values saved by the in-game tuning panel (F9 in time trials).
        tuning.EnsureRuntimeOverrides();
    }

    void OnDestroy()
    {
        if (rift != null) Destroy(rift.gameObject);
        ClearRails();
    }

    void Update()
    {
        switch (CurrentPhase)
        {
            case Phase.Idle:     TickIdle();     break;
            case Phase.Entering: TickEntering(); break;
            case Phase.Active:   TickActive();   break;
        }
    }

    /// Cancel any in-progress mode and return to Idle without applying the
    /// exit boost / gear drop.  Called by RaceManager (via CarController.ResetToStart)
    /// so a restart doesn't leave the car ghost-piloting itself.
    public void ForceExit()
    {
        cooldownUntil = 0f;                       // fresh attempt → recharge cleared
        if (rift != null) { Destroy(rift.gameObject); rift = null; }
        if (CurrentPhase == Phase.Entering) { CurrentPhase = Phase.Idle; return; }
        if (CurrentPhase != Phase.Active) return;
        CurrentPhase = Phase.Idle;
        notes.Clear();
        ClearRails();
        if (car != null) car.spatialDriftActive = false;
        ApplyBodyVisual(false, flash: false);
        // Notify listeners (the HUD) so the rhythm UI is torn down and the shifter
        // gate comes back — otherwise a cancel (race restart / TT reset) leaves the
        // drift-zone overlay stuck on screen.
        OnExit?.Invoke(Score);
    }

    // ── Phase: Idle ───────────────────────────────────────────────────────────
    void TickIdle()
    {
        // Manual entry: player must press the entry key while conditions are met.
        if (!CanEnter || tuning == null || GamePause.InputBlocked) return;
        if (Input.GetKeyDown(tuning.entryKey) || Input.GetKeyDown(tuning.entryGamepadButton))
            BeginEntry();
    }

    // ── Phase: Entering — tear the world open ahead, drive through it ─────────
    bool IsLocal => car != null && CarController.FindLocalPlayer() == car;

    void BeginEntry()
    {
        if (car == null || car.track == null) { Activate(); return; }
        CurrentPhase = Phase.Entering;
        entryStart   = Time.time;
        float ahead  = Mathf.Clamp(car.Speed * RiftLeadTime, RiftMinAhead, RiftMaxAhead);
        rift = SpatialRift.SpawnEntry(car, ahead);
        if (IsLocal) RacingCamera.Kick(0.25f);
        OnEnterBegin?.Invoke();
    }

    void TickEntering()
    {
        if (rift == null || car == null || car.track == null) { CrossRift(); return; }
        // Crossed once the car's nose reaches the portal plane.
        float d = WrapDelta(car.DistanceAlongTrack - rift.Distance, car.track.Length);
        if (d >= -2.2f || Time.time - entryStart > EntryTimeout) CrossRift();
    }

    void CrossRift()
    {
        if (rift != null) { rift.Collapse(); rift = null; }
        if (IsLocal)
        {
            WarpEffect.Pulse(1f, RiftTint);
            RacingCamera.Kick(0.7f);
        }
        Activate();
    }

    // ── Phase: Active ─────────────────────────────────────────────────────────
    void Activate()
    {
        CurrentPhase       = Phase.Active;
        Lives              = tuning != null ? tuning.maxLives : 3;
        Score              = 0;
        notes.Clear();
        activeStartTime    = Time.time;
        nextNoteSpawnTime  = Time.time + 1.2f;

        if (car != null)
        {
            car.spatialDriftActive        = true;
            car.spatialDriftTargetLateral = 0f;
        }

        if (RailsVariant) InitRails();

        ApplyBodyVisual(true, flash: true);
        OnActivate?.Invoke();
    }

    void TickActive()
    {
        // Session time limit (both variants): exit with the normal score boost.
        if (tuning != null && tuning.maxDuration > 0f
            && Time.time - activeStartTime >= tuning.maxDuration)
        {
            Exit();
            return;
        }

        if (RailsVariant) { TickActiveRails(); return; }
        TickActiveRhythm();
    }

    void TickActiveRhythm()
    {
        // 1. Spawn notes at the difficulty-scaled interval
        if (Time.time >= nextNoteSpawnTime)
        {
            SpawnNote();
            nextNoteSpawnTime = Time.time + CurrentSpawnInterval;
        }

        // 2. Read directional press (arrow keys OR face buttons)
        KeyCode pressed = GetArrowPressedThisFrame();
        if (pressed != KeyCode.None) ResolvePress(pressed);

        // 3. Hold tracking — for hold notes whose head was caught
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.duration <= 0f || !n.consumed || n.holdComplete) continue;

            if (!IsKeyHeld(n.key))
            {
                // Released too early — hold ends with no bonus (you got the
                // head, just missed the tail bonus).  No life loss.
                n.holdComplete = true;
            }
            else if (Time.time >= n.hitTime + n.duration)
            {
                // Held to completion — bonus reward.
                n.holdComplete = true;
                Score += tuning.scorePerHoldComplete;
                OnHit?.Invoke(2, n.key);
            }
        }

        // 4. Expire notes that passed the window unhit, or finished cleanly
        float window = CurrentHitWindow;
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            var n       = notes[i];
            bool isHold = n.duration > 0f;

            if (n.consumed)
            {
                float cleanupAt = isHold
                    ? n.hitTime + n.duration + 0.30f
                    : n.hitTime + 0.40f;
                if (Time.time > cleanupAt) notes.RemoveAt(i);
                continue;
            }
            if (Time.time > n.hitTime + window)
            {
                // MISSED — costs a life, fires events for HUD to show X marks.
                notes.RemoveAt(i);
                Lives = Mathf.Max(0, Lives - 1);
                OnNoteMissed?.Invoke(n);
                OnLifeLost?.Invoke(Lives);
                OnHit?.Invoke(0, n.key);
            }
        }

        // 5. Exit when lives are gone
        if (Lives <= 0) Exit();
    }

    void SpawnNote()
    {
        // Collect lanes that aren't currently occupied by an in-flight note.
        // "Occupied" = note with the same key that hasn't been hit yet, OR a
        // hold whose tail is still visually traversing the playfield.
        int freeCount = 0;
        for (int d = 0; d < AllDirs.Length; d++)
        {
            if (!IsLaneOccupied(AllDirs[d]))
                _freeLaneBuf[freeCount++] = d;
        }
        if (freeCount == 0) return;     // all lanes busy — silently skip this tick

        // Decide tap vs hold.  Respect the simultaneous-hold cap.
        int activeHolds = CountActiveHolds();
        bool wantHold   = UnityEngine.Random.value < tuning.holdNoteChance;
        bool isHold     = wantHold && activeHolds < tuning.maxSimultaneousHolds;

        float duration = isHold
            ? UnityEngine.Random.Range(tuning.holdDurationMin, tuning.holdDurationMax)
            : 0f;
        float lead     = CurrentLeadTime;
        var n = new Note
        {
            key      = AllDirs[_freeLaneBuf[UnityEngine.Random.Range(0, freeCount)]],
            hitTime  = Time.time + lead,
            leadTime = lead,                // stored per-note so visuals are stable across difficulty changes
            duration = duration,
        };
        notes.Add(n);
        OnNoteSpawn?.Invoke(n);
    }

    bool IsLaneOccupied(KeyCode key)
    {
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.key != key) continue;
            if (!n.consumed) return true;
            // Hold's tail still relevant in the lane until its visual lifetime ends.
            if (n.duration > 0f && Time.time < n.hitTime + n.duration + 0.2f) return true;
        }
        return false;
    }

    int CountActiveHolds()
    {
        int count = 0;
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.duration <= 0f) continue;
            if (!n.consumed) { count++; continue; }
            if (!n.holdComplete) count++;
        }
        return count;
    }

    void ResolvePress(KeyCode pressed)
    {
        float window     = CurrentHitWindow;
        Note  best       = null;
        float bestAbsDt  = float.PositiveInfinity;
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.consumed || n.key != pressed) continue;
            float dt = Mathf.Abs(Time.time - n.hitTime);
            if (dt > window) continue;
            if (dt < bestAbsDt) { bestAbsDt = dt; best = n; }
        }

        if (best == null)
        {
            // Bad press: hit-zone flash, but no life loss — exploration is free.
            OnHit?.Invoke(0, pressed);
            return;
        }

        bool perfect    = bestAbsDt < window * 0.35f;
        best.consumed   = true;
        Score          += perfect ? tuning.scorePerPerfect : tuning.scorePerHit;
        OnHit?.Invoke(perfect ? 2 : 1, best.key);
    }

    // ── Rails minigame ────────────────────────────────────────────────────────
    // The car is locked to one of N lanes spread across the road (N follows the
    // road's width).  Obstacle rows spawn ahead; steer left/right to hop lanes
    // and thread the gaps.  A clipped obstacle costs a life.
    void InitRails()
    {
        ClearRails();
        if (car == null || car.track == null) return;

        railsRoot         = new GameObject("SD_RailsObstacles");
        railsSteerLatched = true;    // absorb whatever steer is held at entry

        float dist = car.DistanceAlongTrack;
        int   p    = RailsPairs(dist);

        // Snap to the nearest valid half-integer rail offset.
        railsOffset = Mathf.Clamp(Mathf.Floor(car.LateralOffset / RailSpacing) + 0.5f,
                                  -(p - 0.5f), p - 0.5f);
        railsPathOffset = railsOffset;
        car.spatialDriftTargetLateral = railsOffset * RailSpacing;

        // First row spawns a comfortable distance ahead.
        railsSpawnDist = car.track.WrapDistance(dist + 55f);

        BuildRailLines();
    }

    void TickActiveRails()
    {
        if (car == null || car.track == null) return;
        var   track = car.track;
        float len   = track.Length;
        if (len <= 60f) return;

        float dist = car.DistanceAlongTrack;
        int   p    = RailsPairs(dist);

        // A narrowing road removes the outermost pair — only cars riding it get
        // nudged inward; every other rail keeps its exact lateral position.
        railsOffset = Mathf.Clamp(railsOffset, -(p - 0.5f), p - 0.5f);

        // Rail switching: arrows / A-D taps, or a flick of the stick.  A
        // successful hop gets a small physical bounce — the flick FEELS like one.
        int move = RailsSteerPressDir();
        if (move != 0)
        {
            float before = railsOffset;
            railsOffset  = Mathf.Clamp(railsOffset + move, -(p - 0.5f), p - 0.5f);
            if (!Mathf.Approximately(before, railsOffset)) car.RailHop();
        }

        car.spatialDriftTargetLateral = railsOffset * RailSpacing;
        UpdateRailHighlight();

        // Keep obstacle rows spawned out to the lookahead horizon.
        float ahead = tuning != null ? tuning.railsSpawnAhead : 120f;
        int   guard = 16;                       // hard cap per frame (safety)
        while (WrapDelta(railsSpawnDist - dist, len) < ahead && guard-- > 0)
        {
            SpawnRailsRow(railsSpawnDist);
            float spacing = Mathf.Lerp(
                tuning != null ? tuning.railsObstacleSpacing    : 60f,
                tuning != null ? tuning.railsObstacleSpacingMin : 32f,
                DifficultyT);
            railsSpawnDist = track.WrapDistance(railsSpawnDist + Mathf.Max(10f, spacing));
        }

        // Resolve rows the car just crossed; recycle rows well behind.
        float hitHalfWidth = RailSpacing * 0.45f;
        for (int i = railsRows.Count - 1; i >= 0; i--)
        {
            var   row = railsRows[i];
            float d   = WrapDelta(dist - row.dist, len);

            if (!row.resolved && d > 0f && d < 30f)
            {
                row.resolved = true;
                bool hit = false;
                for (int o = 0; o < row.laneLats.Count; o++)
                {
                    if (Mathf.Abs(car.LateralOffset - row.laneLats[o]) >= hitHalfWidth)
                        continue;
                    hit = true;
                    var box = row.gos[row.groupOf[o]];
                    if (box != null) box.SetActive(false);   // "smashed"
                    break;
                }

                if (hit)
                {
                    Lives = Mathf.Max(0, Lives - 1);
                    OnLifeLost?.Invoke(Lives);
                    OnHit?.Invoke(0, KeyCode.None);
                    car.GetComponent<CarAudio>()?.PlayBump(0.8f);
                }
                else
                {
                    Score += tuning != null ? tuning.railsScorePerDodge : 100;
                }
            }

            if (row.resolved && d > 25f && d < len * 0.5f)
            {
                foreach (var g in row.gos) if (g != null) Destroy(g);
                railsRows.RemoveAt(i);
            }
        }

        if (Lives <= 0) Exit();
    }

    float RailSpacing => tuning != null ? Mathf.Max(1f, tuning.railsLaneSpacing) : 3.2f;

    /// Symmetric rail pairs that fit the road here: 1..3 pairs → 2/4/6 rails.
    /// The outermost rail sits at (pairs − 0.5) × spacing from the centreline.
    int RailsPairs(float dist)
    {
        int   maxPairs = Mathf.Clamp((tuning != null ? tuning.railsMaxLanes : 6) / 2, 1, 3);
        float usable   = car.track.HalfWidthAt(dist) - 1.6f;      // margin for car width
        return Mathf.Clamp(Mathf.FloorToInt(usable / RailSpacing + 0.5f), 1, maxPairs);
    }

    // One press (or one stick flick) = one rail hop.  The latch stops a held
    // stick from machine-gunning across the rails — but it re-arms on a PARTIAL
    // return (0.45, not centre) and instantly on a direction reversal, so fast
    // double-flicks register crisply.
    int railsLastFlickDir;

    int RailsSteerPressDir()
    {
        if (GamePause.InputBlocked) return 0;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
        { railsSteerLatched = true; railsLastFlickDir = -1; return -1; }
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
        { railsSteerLatched = true; railsLastFlickDir = +1; return +1; }

        float axis = car != null ? car.LocalInput.steer : 0f;
        int   dir  = axis > 0.55f ? 1 : axis < -0.55f ? -1 : 0;

        if (railsSteerLatched)
        {
            if      (Mathf.Abs(axis) < 0.45f)              railsSteerLatched = false;
            else if (dir != 0 && dir != railsLastFlickDir) railsSteerLatched = false;
            else return 0;
        }
        if (dir != 0)
        {
            railsSteerLatched = true;
            railsLastFlickDir = dir;
            return dir;
        }
        return 0;
    }

    void SpawnRailsRow(float d)
    {
        var track = car.track;
        int p     = RailsPairs(d);
        int count = p * 2;

        // Walk the guaranteed-open PATH rail at most 2 rails per row — every gap
        // is always reachable with ≤2 flicks from the previous gap.
        railsPathOffset = Mathf.Clamp(railsPathOffset + UnityEngine.Random.Range(-2, 3),
                                      -(p - 0.5f), p - 0.5f);

        // Candidate rails (all except the path rail), shuffled.
        var lanes = new List<float>(count);
        for (int i = 0; i < count; i++)
        {
            float off = i - p + 0.5f;
            if (Mathf.Abs(off - railsPathOffset) > 0.25f) lanes.Add(off);
        }
        for (int i = lanes.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (lanes[i], lanes[j]) = (lanes[j], lanes[i]);
        }

        int blockedCount = Mathf.Min(UnityEngine.Random.Range(1, count), lanes.Count);
        var blocked = lanes.GetRange(0, blockedCount);
        blocked.Sort();

        // Adjacent blocked rails merge into ONE wide obstacle.
        var row = new RailsRow { dist = d };
        int g = 0;
        for (int i = 0; i < blocked.Count; )
        {
            int end = i;
            while (end + 1 < blocked.Count && blocked[end + 1] - blocked[end] < 1.5f) end++;

            float centerLat = (blocked[i] + blocked[end]) * 0.5f * RailSpacing;
            float boxWidth  = (blocked[end] - blocked[i]) * RailSpacing + RailSpacing * 0.72f;

            track.Sample(d, centerLat, out Vector3 pos, out Quaternion rot, out _, out Vector3 up);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SD_Obstacle";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(railsRoot != null ? railsRoot.transform : null, false);
            go.transform.SetPositionAndRotation(pos + up * 1.2f, rot);
            go.transform.localScale = new Vector3(boxWidth, 2.4f, 0.7f);
            go.GetComponent<Renderer>().sharedMaterial = RailsObstacleMaterial();
            row.gos.Add(go);

            for (int k = i; k <= end; k++)
            {
                row.laneLats.Add(blocked[k] * RailSpacing);
                row.groupOf.Add(g);
            }
            g++;
            i = end + 1;
        }
        railsRows.Add(row);
    }

    static Material RailsObstacleMaterial()
    {
        if (_railsObstacleMat != null) return _railsObstacleMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _railsObstacleMat = new Material(sh);
        var col = new Color(1f, 0.20f, 0.55f);        // hot magenta — reads at speed
        _railsObstacleMat.color = col;
        _railsObstacleMat.SetColor("_BaseColor", col);
        return _railsObstacleMat;
    }

    // ── Rail line visuals ─────────────────────────────────────────────────────
    // One translucent strip per rail offset, following the whole track, spans
    // skipped where the road is too narrow for that rail.  Only exists while
    // the mode is active; the current rail is re-tinted brighter per frame.
    void BuildRailLines()
    {
        var track = car.track;
        float len = track.Length;
        if (len <= 20f || railsRoot == null) return;

        int steps    = Mathf.Clamp(Mathf.CeilToInt(len / 4f), 16, 2000);
        int maxPairs = Mathf.Clamp((tuning != null ? tuning.railsMaxLanes : 6) / 2, 1, 3);

        var shader = Shader.Find("SpatialDrift/RailLine")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Sprites/Default");
        if (shader == null) return;
        bool hasZTest = shader.name == "SpatialDrift/RailLine";

        for (int li = -maxPairs; li < maxPairs; li++)
        {
            float off = li + 0.5f;

            var verts  = new List<Vector3>((steps + 1) * 2);
            var tris   = new List<int>(steps * 12);
            var exists = new bool[steps + 1];

            for (int i = 0; i <= steps; i++)
            {
                float d = (i % steps) * (len / steps);   // seam sealed
                exists[i] = RailsPairs(d) - 0.5f >= Mathf.Abs(off) - 0.01f;

                track.Sample(d, off * RailSpacing,
                             out Vector3 pos, out _, out Vector3 fwd, out Vector3 up);
                Vector3 right = Vector3.Cross(up, fwd).normalized;
                verts.Add(pos + up * 0.15f - right * 0.15f);
                verts.Add(pos + up * 0.15f + right * 0.15f);
            }
            // Heal isolated one-sample dropouts (width sitting on a pair
            // boundary flickers the exists test → pinhole gaps in the line).
            for (int i = 1; i < steps; i++)
                if (!exists[i] && exists[i - 1] && exists[i + 1]) exists[i] = true;

            for (int i = 0; i < steps; i++)
            {
                if (!exists[i] || !exists[i + 1]) continue;
                int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                tris.Add(b0); tris.Add(t0); tris.Add(t1);
                tris.Add(b0); tris.Add(t1); tris.Add(b1);
                tris.Add(b0); tris.Add(t1); tris.Add(t0);
                tris.Add(b0); tris.Add(b1); tris.Add(t1);
            }
            if (tris.Count == 0) continue;

            var go = new GameObject($"Rail_{off:+0.0;-0.0}");
            go.transform.SetParent(railsRoot.transform, false);
            var mesh = new Mesh { name = go.name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mat = new Material(shader) { name = go.name };
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 20;
            SetRailColor(mat, RailDim);

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            railLines.Add(mr);
            railLineOffsets.Add(off);

            // X-ray twin: same mesh, ZTest Greater — draws only where the normal
            // pass is HIDDEN, so the guide ghosts through obstacles instead of
            // showing a gap behind every blocker.
            MeshRenderer xr = null;
            if (hasZTest)
            {
                var xgo = new GameObject(go.name + "_xray");
                xgo.transform.SetParent(railsRoot.transform, false);
                xgo.AddComponent<MeshFilter>().sharedMesh = mesh;

                var xmat = new Material(shader) { name = xgo.name };
                xmat.SetFloat("_ZTest",
                    (float)UnityEngine.Rendering.CompareFunction.Greater);
                xmat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 19;
                SetRailColor(xmat, Fade(RailDim, RailXrayAlpha));

                xr = xgo.AddComponent<MeshRenderer>();
                xr.sharedMaterial    = xmat;
                xr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                xr.receiveShadows    = false;
            }
            railLinesXray.Add(xr);
        }
    }

    static Color Fade(Color c, float alphaMul)
    {
        c.a *= alphaMul;
        return c;
    }

    void UpdateRailHighlight()
    {
        // Exactly ONE rail highlights: the nearest to the player's offset —
        // structurally impossible for two to light at once.
        int best = -1; float bestGap = float.PositiveInfinity;
        for (int i = 0; i < railLines.Count; i++)
        {
            float gap = Mathf.Abs(railLineOffsets[i] - railsOffset);
            if (gap < bestGap) { bestGap = gap; best = i; }
        }
        for (int i = 0; i < railLines.Count; i++)
        {
            if (railLines[i] == null) continue;
            var mat = railLines[i].sharedMaterial;
            if (mat == null) continue;
            var target = i == best ? RailBright : RailDim;
            if (mat.color != target)
            {
                SetRailColor(mat, target);
                if (i < railLinesXray.Count && railLinesXray[i] != null
                    && railLinesXray[i].sharedMaterial != null)
                    SetRailColor(railLinesXray[i].sharedMaterial, Fade(target, RailXrayAlpha));
            }
        }
    }

    static void SetRailColor(Material m, Color c)
    {
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
    }

    void ClearRails()
    {
        foreach (var row in railsRows)
            foreach (var g in row.gos) if (g != null) Destroy(g);
        railsRows.Clear();

        // Rail line meshes/materials are runtime objects — free them explicitly.
        // (The x-ray twin SHARES the main line's mesh — destroy it once.)
        foreach (var mr in railLines)
        {
            if (mr == null) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            if (mr.sharedMaterial != null) Destroy(mr.sharedMaterial);
        }
        foreach (var mr in railLinesXray)
            if (mr != null && mr.sharedMaterial != null) Destroy(mr.sharedMaterial);
        railLines.Clear();
        railLinesXray.Clear();
        railLineOffsets.Clear();

        if (railsRoot != null) Destroy(railsRoot);
        if (car != null) car.spatialDriftTargetLateral = 0f;
    }

    static float WrapDelta(float delta, float length)
    {
        if (length <= 0f) return delta;
        delta %= length;
        if (delta >  length * 0.5f) delta -= length;
        if (delta < -length * 0.5f) delta += length;
        return delta;
    }

    // ── Exit ──────────────────────────────────────────────────────────────────
    void Exit()
    {
        int   finalScore = Score;
        int   exGear     = tuning != null ? tuning.exitGear : 6;
        float boostMin   = tuning != null ? tuning.exitBoostMin           : 5f;
        float boostMax   = tuning != null ? tuning.exitBoostMax           : 25f;
        int   sat        = tuning != null ? tuning.exitBoostSaturationScore : 2000;
        float boostDur   = tuning != null ? tuning.exitBoostDuration      : 1.2f;

        float scoreFrac = Mathf.Clamp01((float)Score / Mathf.Max(1, sat));
        float boost     = Mathf.Lerp(boostMin, boostMax, scoreFrac);
        if (gears != null) gears.ApplyExternalBoost(boost, boostDur);

        if (shifter != null && exGear >= 1 && exGear <= GearSystem.GearCount)
            shifter.AutoShiftTo(exGear);

        if (car != null && gears != null
            && exGear >= 1 && exGear <= gears.gearMaxSpeeds.Length)
        {
            car.Speed = gears.gearMaxSpeeds[exGear - 1];
        }

        // Hand off to CarController's post-exit grace lock so the player
        // gets ~1s of center-lock + heading-lock before regaining full control.
        if (car != null)
        {
            car.spatialDriftActive  = false;
            car.postExitLockTimer   = postExitGraceDuration;
        }

        // Recharge: the mode can't be re-entered until this runs out.
        cooldownUntil = Time.time + (tuning != null ? tuning.rechargeDuration : 6f);

        CurrentPhase   = Phase.Idle;
        notes.Clear();
        ClearRails();
        ApplyBodyVisual(false, flash: true);
        // Torn back out into the real world.
        SpatialRift.SpawnExit(car);
        if (IsLocal)
        {
            WarpEffect.Pulse(0.7f, RiftTint);
            RacingCamera.Kick(0.55f);
        }
        OnExit?.Invoke(finalScore);
    }

    // ── Body visual (ghost form) ──────────────────────────────────────────────
    void ApplyBodyVisual(bool ghost, bool flash)
    {
        if (ghostFx == null)
        {
            ghostFx = GetComponent<CarGhost>();
            if (ghostFx == null) ghostFx = gameObject.AddComponent<CarGhost>();
        }
        ghostFx.SetGhost(ghost, flash);
    }

    // ── Input ─────────────────────────────────────────────────────────────────
    // Keyboard arrows OR Xbox face buttons mapped to their spatial positions:
    //   Y (top)    = ↑    A (bottom) = ↓
    //   X (left)   = ←    B (right)  = →
    // The face-button layout matches the directional intuition without any
    // InputManager configuration.
    static KeyCode GetArrowPressedThisFrame()
    {
        if (GamePause.InputBlocked) return KeyCode.None;
        if (Input.GetKeyDown(KeyCode.UpArrow))         return KeyCode.UpArrow;
        if (Input.GetKeyDown(KeyCode.DownArrow))       return KeyCode.DownArrow;
        if (Input.GetKeyDown(KeyCode.LeftArrow))       return KeyCode.LeftArrow;
        if (Input.GetKeyDown(KeyCode.RightArrow))      return KeyCode.RightArrow;
        if (Input.GetKeyDown(KeyCode.JoystickButton3)) return KeyCode.UpArrow;     // Y
        if (Input.GetKeyDown(KeyCode.JoystickButton0)) return KeyCode.DownArrow;   // A
        if (Input.GetKeyDown(KeyCode.JoystickButton2)) return KeyCode.LeftArrow;   // X
        if (Input.GetKeyDown(KeyCode.JoystickButton1)) return KeyCode.RightArrow;  // B
        return KeyCode.None;
    }

    static bool IsKeyHeld(KeyCode key)
    {
        if (Input.GetKey(key)) return true;
        switch (key)
        {
            case KeyCode.UpArrow:    return Input.GetKey(KeyCode.JoystickButton3);  // Y
            case KeyCode.DownArrow:  return Input.GetKey(KeyCode.JoystickButton0);  // A
            case KeyCode.LeftArrow:  return Input.GetKey(KeyCode.JoystickButton2);  // X
            case KeyCode.RightArrow: return Input.GetKey(KeyCode.JoystickButton1);  // B
        }
        return false;
    }
}
