using UnityEngine;

/// F-Zero-style vehicle on a spline-defined track.
///
/// Key model: the spline provides the **track surface** (position + up vector
/// at any point), but the car's **heading is free** — held in world space and
/// only changed by player steering.  Where the track curves, the spline
/// tangent rotates but the car's heading doesn't, so the player has to
/// actively steer to stay on the racing line.  Don't steer enough and you
/// drift outward and scrape the wall.
///
/// We still parameterise position as (distanceAlongTrack, lateralOffset) so we
/// can constrain the car to the track surface without per-frame nearest-point
/// queries on the spline.
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Physics")]
    [Tooltip("Target physics tick rate (Hz).  Unity's default 50Hz produces visible " +
             "judder when the car moves 4+ meters per FixedUpdate at top speed (228 m/s) " +
             "— what looks like 'going forward and being tugged back'.  100Hz halves " +
             "that to 2.3m per step and aligns far better with 60-120Hz rendering. " +
             "Cheap because our rigidbody is kinematic (no PhysX simulation cost).")]
    [Range(50f, 200f)] public float targetPhysicsRate = 100f;

    [Header("Display / Input Smoothing")]
    [Tooltip("Force V-sync on at startup to prevent screen tearing — most visible during " +
             "fast turns when large parts of the framebuffer change between scanouts. " +
             "Disable if you want uncapped framerate for benchmarking.")]
    public bool forceVSync = true;
    [Tooltip("Rate at which keyboard steering ramps toward its target (per second). " +
             "Smooths the binary 0/1 key input a touch.  HIGHER = crisper / more " +
             "responsive (less input delay); LOWER = floatier.  ~16 ≈ 60ms, barely " +
             "perceptible but still kills the snap.  Set 0 for fully raw input. " +
             "(If keyboard ever feels 'laggy', this is the first knob to raise.) " +
             "Only applies to keyboard; gamepad stick passes through analog.")]
    [Range(0f, 30f)] public float keyboardSteerSmoothRate = 16f;

    [Header("Track")]
    [Tooltip("Spline to drive on. Auto-found in the scene if left blank.")]
    public Track track;

    [Header("Driving Feel")]
    public float accelerationScale = 1f;
    public float brakePower        = 5f;
    public float coastDrag         = 1.2f;

    [Header("Steering")]
    [Tooltip("Degrees per second of heading change at full A/D input.")]
    public float steerRateDeg      = 75f;
    [Tooltip("Minimum forward speed (m/s) before steering input does anything.")]
    public float minSteerSpeed     = 1.5f;

    [Header("Bank-Aware Steering")]
    [Tooltip("On banked sections, steering becomes more effective (gravity helps " +
             "redirect velocity).  At 0 banking does nothing for turning; at 0.6, " +
             "a 45° banked corner gives 60% more turn rate.")]
    [Range(0f, 2f)] public float bankSteeringMultiplier = 0.6f;

    /// How drifting controls:
    ///   Slide — the original Initial-D model: steering is free, the nose leads
    ///           the velocity through a grip lag; angle emerges from physics.
    ///   Hop   — Mario-Kart-8 model: the entry flick LOCKS the drift direction;
    ///           the stick then only widens/tightens the arc (into the turn =
    ///           sharper, away = shallower).  Hold brake to stay in the drift,
    ///           release for the charge boost.
    public enum DriftStyle { Slide, Hop }

    [Header("Drift Style")]
    public DriftStyle driftStyle = DriftStyle.Hop;

    [Header("Hop Drift (Mario-Kart feel — used when driftStyle = Hop)")]
    [Tooltip("Slide angle (deg) with the stick pushed AWAY from the drift.")]
    [Range(2f, 30f)]  public float hopAngleMin = 8f;
    [Tooltip("Slide angle (deg) with the stick pushed INTO the drift.")]
    [Range(10f, 60f)] public float hopAngleMax = 32f;
    [Tooltip("How quickly the angle follows the stick (higher = snappier).")]
    [Range(1f, 20f)]  public float hopAngleResponse = 7f;
    [Tooltip("Turn rate per degree of slide angle (deg/s per deg).  The arc " +
             "tightens as the angle grows — angle IS the steering in a hop drift.")]
    [Range(1f, 8f)]   public float hopTurnPerAngle = 3.6f;
    [Tooltip("Height of the little entry hop (visual only, metres).")]
    [Range(0f, 1.5f)] public float hopHeight = 0.45f;
    [Range(0.1f, 0.8f)] public float hopDuration = 0.28f;

    [Header("Drift (brake + steer)")]
    [Tooltip("If on, holding brake while steering enters a drift — faster turn rate, " +
             "no deceleration, mini-boost on release scaled by hold duration.  " +
             "Braking while NOT steering still decelerates normally.")]
    public bool driftEnabled            = true;
    [Tooltip("Steering input magnitude required to initiate drift.")]
    [Range(0.05f, 0.5f)] public float driftSteerThreshold = 0.20f;
    [Tooltip("Minimum speed required to drift (m/s).")]
    [Range(5f, 50f)]     public float driftMinSpeed       = 12f;
    [Tooltip("Steering rate multiplier while drifting — drift turns sharper.")]
    [Range(1f, 3f)]      public float driftSteerMultiplier= 1.7f;
    [Tooltip("Minimum drift duration before release awards any boost.")]
    [Range(0.1f, 1f)]    public float driftMinDuration    = 0.4f;
    [Tooltip("Drift duration at which boost saturates (longer = same boost).")]
    [Range(0.5f, 3f)]    public float driftMaxDuration    = 1.5f;
    [Tooltip("Boost m/s at minimum drift duration.")]
    [Range(2f, 20f)]     public float driftBoostMin       = 5f;
    [Tooltip("Boost m/s at maximum drift duration.")]
    [Range(2f, 30f)]     public float driftBoostMax       = 14f;
    [Tooltip("How long the drift-release boost lasts (s).")]
    [Range(0.1f, 2f)]    public float driftBoostDuration  = 0.55f;
    [Tooltip("Speed bled per second while drifting (m/s²).  0 = drift maintains " +
             "speed forever; ~5 = noticeable slowdown that makes the release boost " +
             "feel earned; high values make drift a real risk/reward trade.")]
    [Range(0f, 30f)]     public float driftDrag           = 0.5f;

    [Header("Drift Slide (Initial D feel)")]
    [Tooltip("Grip when NOT drifting: how fast the car's actual travel direction " +
             "snaps to its heading.  High = velocity follows the nose instantly " +
             "(tight, kart-like).  ~24 keeps a hair of realistic weight; raise to " +
             "35-40 for the old near-instant steering, lower toward 12 for floatier.")]
    [Range(5f, 40f)] public float normalGrip = 24f;
    [Tooltip("Grip WHILE drifting: low = the car keeps sliding along its old " +
             "momentum while the nose points elsewhere (on-ice feel).  ~2-4 gives " +
             "a long Initial-D slide; ~6-8 is a tighter, grippier drift.")]
    [Range(1f, 15f)] public float driftGrip = 3f;
    [Tooltip("Instant heading kick (degrees) applied the moment a drift starts, in " +
             "the steer direction — the 'flick' that breaks the rear loose so the " +
             "slide initiates crisply instead of mushily.")]
    [Range(0f, 30f)] public float driftEntryFlickDeg = 8f;
    [Tooltip("Maximum angle (degrees) the nose can lead the velocity during a drift. " +
             "Caps the slide so you can't spin out into a full donut — the car holds " +
             "at this angle even if you keep steering in.")]
    [Range(20f, 90f)] public float maxSlideAngleDeg = 55f;
    [Tooltip("Extra speed scrub (m/s²) at a full 90° slide, scaled by sin(slide angle). " +
             "Models the energy lost to lateral tire friction — a big sideways slide " +
             "costs speed, a gentle one barely does.  Stacks on top of driftDrag.")]
    [Range(0f, 30f)] public float driftSlideScrub = 6f;

    [Header("Wall Behaviour")]
    [Tooltip("Fraction of speed kept on a fully head-on wall hit.  Glancing hits keep " +
             "more (scaled by impact angle), so a small mistake costs a little, a full " +
             "broadside costs this fraction.  Applied once per impact, not per frame.")]
    [Range(0f, 1f)] public float wallSpeedRetained = 0.8f;
    [Tooltip("How bouncy the wall is.  0 = pure slide-along (old behaviour, can stick). " +
             "~0.35 = nudges you back onto the track so you don't grind to a halt. " +
             "1 = fully elastic mirror bounce.")]
    [Range(0f, 1f)] public float wallBounce = 0.35f;

    [Header("Track-Center Magnet")]
    [Tooltip("How strongly the car self-corrects toward the centerline by " +
             "rotating its heading inward (so the front leads, like a real car). " +
             "0 = no assist, ~0.3 = barely there, ~1 = clear straight-line stability, " +
             "~3+ = aggressive lane assist.")]
    [Range(0f, 5f)] public float centerMagnet = 0f;

    [Header("Auto-Downshift")]
    [Tooltip("If speed drops out of the current gear's range (e.g. after a wall hit), " +
             "automatically downshift the player to a gear that matches.")]
    public bool autoDownshiftEnabled = true;

    [Tooltip("Speed must fall below this fraction of the gear's lower bound before " +
             "an auto-downshift fires.  Lower = more lenient, fewer auto-shifts.")]
    [Range(0.5f, 1f)] public float autoDownshiftThreshold = 0.85f;

    [Header("References")]
    public GearSystem gearSystem;

    [Header("Start Position")]
    public float startDistance      = 0f;
    public float startLateralOffset = 0f;

    [Header("Input")]
    [Tooltip("When false, all driving input is ignored (used by RaceManager during " +
             "the pre-race countdown and after the finish).  Set false on remote " +
             "players in multiplayer — their input arrives via LocalInput from the network.")]
    public bool inputEnabled = true;

    [Tooltip("True for AI opponents.  Update() stops reading keyboard/gamepad — an " +
             "AIDriver component writes LocalInput instead.  Also skips player-only " +
             "auto-added components (CarAudio, SpatialDriftMode).")]
    public bool aiControlled = false;

    [Tooltip("Set on networked car PREFABS so the player-only systems (HUD, audio, " +
             "Spatial Drift) aren't auto-added in Awake.  NetworkCarSync adds them " +
             "via EnablePlayerSystems() for the local owner only, after spawn.")]
    public bool networkDeferPlayerSystems = false;

    /// A networked local-player car registers itself here on spawn so the
    /// camera effects (which call FindLocalPlayer) lock onto it rather than a
    /// remote ghost.  Null in single-player.
    public static CarController LocalPlayerOverride;

    [Header("Spatial Drift Mode (read-only — set by SpatialDriftMode)")]
    [Tooltip("True while the namesake autopilot mode is active.  Suppresses normal " +
             "input handling, locks lateral to centerline, locks heading to spline " +
             "tangent, and overrides Speed to (top × multiplier + additive).")]
    public bool spatialDriftActive = false;
    [Tooltip("Lateral offset the autopilot steers to while active.  0 = centerline " +
             "(Rhythm minigame); the Rails minigame moves it between lane positions.")]
    public float spatialDriftTargetLateral = 0f;
    [Tooltip("Seconds remaining of the post-exit grace lock — SpatialDriftMode sets " +
             "this on exit so the car stays centered and pointed forward for a beat " +
             "before the player regains lateral / steering control (no more being " +
             "spit into a wall the moment you're kicked out).")]
    public float postExitLockTimer = 0f;

    /// Zone Race: the train controller sets the exact speed (>= 0) and the rail
    /// hop speed; -1 = normal drift-zone behaviour.
    [System.NonSerialized] public float zoneSpeedOverride     = -1f;
    [System.NonSerialized] public float zoneSnapSpeedOverride = -1f;

    /// True while either ghost mode or the post-exit grace is active.
    /// Used by the velocity decompose + lateral snap to keep the car centered.
    bool LateralOrHeadingLocked => spatialDriftActive || postExitLockTimer > 0f;

    [Header("Gamepad")]
    [Tooltip("Stick magnitudes below this are treated as zero (controllers idle " +
             "slightly off-center).  Run 'Racing Game / Configure Gamepad Input' once " +
             "to register the required Input Manager axes.")]
    [Range(0f, 0.4f)] public float gamepadDeadzone   = 0.12f;
    [Tooltip("Trigger value above which the brake is considered held.")]
    [Range(0.05f, 0.9f)] public float gamepadBrakeThreshold = 0.15f;

    // ── Public state (HUD reads) ──────────────────────────────────────────────
    /// Settable so SpatialDriftMode can cut Speed on exit.  Everything else
    /// modifies it through MoveTowards in FixedUpdate.
    public float Speed             { get; set; }
    public float DistanceAlongTrack{ get; private set; }
    public float LateralOffset     { get; private set; }

    /// Per-frame driving inputs.  Filled by local keyboard when `inputEnabled` is
    /// true, or by network replication code (future) when remote-controlled.
    public CarInput LocalInput { get; set; }

    // ── Slipstream (set by SlipstreamTrail each frame) ────────────────────────
    /// Extra m/s added to the current gear's speed cap while riding another
    /// car's wake.  Smoothed by the trail system, consumed in FixedUpdate.
    public float SlipstreamBonus { get; set; }
    /// True while meaningfully inside someone's slipstream (HUD indicator).
    public bool  InSlipstream => SlipstreamBonus > 0.5f;

    // ── Drift state (read by HUD / visuals) ──────────────────────────────────
    public bool  IsDrifting  { get; private set; }
    public float DriftCharge => Mathf.Clamp01(driftTime / Mathf.Max(0.01f, driftMaxDuration));
    /// Angle (degrees) between where the car points and where it's actually
    /// travelling.  ~0 when gripping, grows during a slide.  VFX read this to
    /// scale skid intensity / sideways body yaw.
    public float SlideAngle  { get; private set; }
    /// Fires when a drift ends; arg = charge (0..1) at the moment of release.
    public event System.Action<float> OnDriftEnd;
    /// Fires when this car is bumped by another (arg = impact 0..1).  Camera shake.
    public event System.Action<float> OnBumped;

    // ── Internal state ────────────────────────────────────────────────────────
    Rigidbody         rb;
    GearShifter       shifter;
    SpatialDriftMode  sdMode;
    int               hopDriftDir;     // -1 / +1, locked at hop-drift entry
    float             hopSlide;        // current hop slide angle (deg)
    float             hopTimer;        // entry-hop bounce countdown (visual)
    float             railHopTimer;    // rails-minigame flick bounce (visual)
    const float RailHopDur = 0.16f, RailHopHeight = 0.35f;

    /// Small visual bounce when hopping between drift-zone rails (SpatialDriftMode).
    public void RailHop() => railHopTimer = RailHopDur;

    /// Raise the bump event without physics (Zone Race hits) — camera shake etc.
    public void Impact(float strength) => OnBumped?.Invoke(Mathf.Clamp01(strength));
    Vector3           worldForward;   // car heading (nose direction) in surface plane
    Vector3           velocityDir;    // actual travel direction in surface plane
    bool              initialised;
    float             driftTime;
    float             _smoothedKbSteer;   // ramped keyboard input — see Update()

    void Awake()
    {
        // Bump physics tick rate so high-speed visuals stay smooth.  Idempotent —
        // if multiple cars exist (multiplayer future) they all converge to the
        // smallest requested step.
        float targetDt = 1f / Mathf.Max(50f, targetPhysicsRate);
        if (Time.fixedDeltaTime > targetDt + 0.0001f)
            Time.fixedDeltaTime = targetDt;

        // V-sync stops the GPU from finishing a frame mid-scanout, which kills
        // the tear lines that show up across the screen during fast turns.
        if (forceVSync && QualitySettings.vSyncCount == 0)
            QualitySettings.vSyncCount = 1;

        rb = GetComponent<Rigidbody>();
        rb.isKinematic   = true;
        rb.useGravity    = false;
        rb.constraints   = RigidbodyConstraints.None;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (gearSystem == null) gearSystem = GetComponent<GearSystem>();
        if (track      == null) track      = FindFirstObjectByType<Track>();

        // One global source of truth for driving feel + the gear curve.  Applies
        // to every car (player / AI / networked), so the whole game shares the
        // tune you like.  Absent asset → keep the in-code defaults above.
        // (Future per-player tires/loadouts hook the same ApplyTo choke point.)
        var tuning = CarTuning.Load();
        if (tuning != null)
        {
            tuning.EnsureRuntimeOverrides();   // apply values saved by the F10 panel
            tuning.ApplyTo(this);
            if (gearSystem != null) tuning.ApplyTo(gearSystem);
        }

        shifter = GetComponent<GearShifter>();
        if (gearSystem != null && shifter == null)
        {
            shifter = gameObject.AddComponent<GearShifter>();
            shifter.gearSystem = gearSystem;
        }

        // Boost particle effects
        if (gearSystem != null && GetComponent<BoostEffects>() == null)
        {
            var be = gameObject.AddComponent<BoostEffects>();
            be.gears = gearSystem;
        }

        // Procedural car model (replaces the placeholder cube).  MUST be added
        // before HoverBody so HoverBody finds the new visual root to animate.
        if (GetComponent<CarVisual>() == null)
            gameObject.AddComponent<CarVisual>();

        // Visual body animation (hover bob, steering lean, banked-corner sink).
        if (GetComponent<HoverBody>() == null)
            gameObject.AddComponent<HoverBody>();

        // Headlights — on automatically in dark weathers (Night / Storm).
        if (GetComponent<Headlights>() == null)
            gameObject.AddComponent<Headlights>();

        // Drift VFX: dark skid marks during the hold, red taillight streaks on release.
        if (GetComponent<SkidMarks>() == null)
            gameObject.AddComponent<SkidMarks>();
        if (GetComponent<BoostTrails>() == null)
            gameObject.AddComponent<BoostTrails>();
        // Drift sparks (tiered by boost charge) + tyre smoke.
        if (GetComponent<DriftFx>() == null)
            gameObject.AddComponent<DriftFx>();

        // Slipstream wake — every car leaves one and can ride everyone else's.
        if (GetComponent<SlipstreamTrail>() == null)
            gameObject.AddComponent<SlipstreamTrail>();

        // Player-only systems: the rhythm mode reads global key presses (one
        // player's E would trigger every AI's mode), and per-car 2D audio from
        // four engines at once is cacophony.  AI cars skip both; networked cars
        // defer to EnablePlayerSystems() so only the local owner gets them.
        if (!aiControlled && !networkDeferPlayerSystems)
            EnablePlayerSystems();
    }

    /// Add the player-only behaviour systems (Spatial Drift, audio).  Called
    /// from Awake in single-player, or by NetworkCarSync for the local owner in
    /// multiplayer.  Idempotent.  HUD is intentionally NOT here — single-player
    /// scenes add it via SceneSetup; NetworkCarSync adds it for the MP owner.
    public void EnablePlayerSystems()
    {
        if (GetComponent<SpatialDriftMode>() == null)
            gameObject.AddComponent<SpatialDriftMode>();
        if (GetComponent<CarAudio>() == null)
            gameObject.AddComponent<CarAudio>();
    }

    /// The local human player's car.  Prefer RaceManager's explicit assignment;
    /// fall back to the first non-AI CarController.  Camera-attached effects use
    /// this instead of FindFirstObjectByType so they never latch onto an AI car.
    public static CarController FindLocalPlayer()
    {
        // Multiplayer: the locally-owned networked car wins.
        if (LocalPlayerOverride != null) return LocalPlayerOverride;

        var rm = FindFirstObjectByType<RaceManager>();
        if (rm != null && rm.localPlayer != null) return rm.localPlayer;

        var cars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
        foreach (var c in cars) if (!c.aiControlled) return c;
        return cars.Length > 0 ? cars[0] : null;
    }

    void Start() => InitialisePosition();

    /// Reset the car to the configured start position with zero speed and a
    /// heading aligned to the spline tangent.  Called by RaceManager between
    /// races (or after a finish → restart).
    public void ResetToStart()
    {
        Speed             = 0f;
        postExitLockTimer = 0f;
        if (sdMode == null) sdMode = GetComponent<SpatialDriftMode>();
        if (sdMode != null) sdMode.ForceExit();
        InitialisePosition();
    }

    void InitialisePosition()
    {
        if (track == null) return;

        DistanceAlongTrack = track.WrapDistance(startDistance);
        float startHalf    = track.HalfWidthAt(DistanceAlongTrack);
        LateralOffset      = Mathf.Clamp(startLateralOffset, -startHalf, startHalf);

        track.Sample(DistanceAlongTrack, LateralOffset,
                     out Vector3 pos, out Quaternion rot, out Vector3 fwd, out Vector3 up);

        // Set both transform and rigidbody so Interpolate doesn't slide us from
        // the SceneSetup seed pose to the spline start on the first frame.
        transform.SetPositionAndRotation(pos, rot);
        rb.position  = pos;
        rb.rotation  = rot;
        Physics.SyncTransforms();

        worldForward = fwd;     // start aligned with the spline tangent
        velocityDir  = fwd;     // travelling straight ahead, no slide
        initialised  = true;
    }

    void Update()
    {
        // AI cars: AIDriver owns LocalInput entirely — don't zero it, don't
        // read the keyboard.
        if (aiControlled) return;

        if (!inputEnabled || GamePause.InputBlocked)
        {
            LocalInput       = default;     // zero accel/steer/brake while locked
            _smoothedKbSteer = 0f;          // forget any pending ramp
            return;
        }

        // Local keyboard.  In multiplayer this branch only runs on the
        // local-owner client; remote cars get inputs written into LocalInput
        // by the network layer instead.
        float kbSteerRaw = (Input.GetKey(KeyCode.D) ? 1f : 0f)
                         - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float kbAccel    = Input.GetKey(KeyCode.W) ? 1f : 0f;
        bool  kbBrake    = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.S);

        // Smooth the keyboard steer: instant 0 → 1 transitions cause a
        // perceptible camera lurch at high speed.  Exponential ramp toward the
        // raw target value mimics analog-stick feel.
        if (keyboardSteerSmoothRate > 0f)
            _smoothedKbSteer = Mathf.Lerp(_smoothedKbSteer, kbSteerRaw,
                                          1f - Mathf.Exp(-keyboardSteerSmoothRate * Time.deltaTime));
        else
            _smoothedKbSteer = kbSteerRaw;

        // Gamepad (axes registered by the 'Racing Game / Configure Gamepad
        // Input' menu).  SafeAxis returns 0 if the axis is undefined, so this
        // is a no-op until the user runs the setup once.
        float padSteer = SafeAxis("PadSteerX");
        if (Mathf.Abs(padSteer) < gamepadDeadzone) padSteer = 0f;
        float padThr   = Mathf.Clamp01(SafeAxis("PadThrottle"));
        float padBrake = Mathf.Clamp01(SafeAxis("PadBrake"));

        LocalInput = new CarInput
        {
            accel = Mathf.Clamp01(kbAccel + padThr),
            steer = Mathf.Clamp(_smoothedKbSteer + padSteer, -1f, 1f),
            brake = kbBrake || padBrake > gamepadBrakeThreshold,
        };
    }

    // Remembers axis names that don't exist so we only ever throw ONCE per name.
    // Input.GetAxis throws on an unregistered axis; without this cache a missing
    // axis would throw 60×/sec/axis — an exception storm that tanks the frame rate
    // and feels exactly like lag.
    static System.Collections.Generic.HashSet<string> _badAxes;

    static float SafeAxis(string name)
    {
        if (_badAxes != null && _badAxes.Contains(name)) return 0f;
        try
        {
            return Input.GetAxis(name);
        }
        catch
        {
            (_badAxes ??= new System.Collections.Generic.HashSet<string>()).Add(name);
            return 0f;
        }
    }

    void FixedUpdate()
    {
        if (gearSystem == null) return;
        // In a networked scene load the Track may not have existed when this car
        // Awoke — keep trying so the car recovers onto the spline instead of
        // sitting at the prefab origin (below the track) unable to move.
        if (track == null)
        {
            track = FindFirstObjectByType<Track>();
            if (track == null) return;
        }
        if (!initialised) InitialisePosition();

        float dt = Time.fixedDeltaTime;

        // Tick down the post-exit grace timer (set by SpatialDriftMode.Exit).
        if (postExitLockTimer > 0f)
            postExitLockTimer = Mathf.Max(0f, postExitLockTimer - dt);

        // ─── 1. Sample the spline at our current track distance ──────────────
        track.Sample(DistanceAlongTrack, out Vector3 splinePos,
                     out Vector3 splineTangent, out Vector3 splineUp);
        Vector3 splineRight = Vector3.Cross(splineUp, splineTangent).normalized;

        // ─── 2. Keep heading + velocity in the surface plane (⊥ to up) ───────
        //     "Parallel transport" — as the surface up vector rotates (banking,
        //     corkscrew), both the nose and the travel direction rotate with it.
        worldForward -= Vector3.Dot(worldForward, splineUp) * splineUp;
        if (worldForward.sqrMagnitude < 1e-6f) worldForward = splineTangent;
        else worldForward.Normalize();

        velocityDir -= Vector3.Dot(velocityDir, splineUp) * splineUp;
        if (velocityDir.sqrMagnitude < 1e-6f) velocityDir = worldForward;
        else velocityDir.Normalize();

        // ─── 3. Speed update via gear system (unchanged) ─────────────────────
        gearSystem.UpdateRPM(Speed);

        float cap   = gearSystem.CurrentMaxSpeed
                    + (gearSystem.IsBoosting ? gearSystem.ActiveBoostSpeed : 0f)
                    + SlipstreamBonus;
        float accel = gearSystem.CurrentAcceleration * accelerationScale;

        var input = LocalInput;

        // ─── 3a. Drift state machine (suppressed during Spatial Drift) ───────
        // Entry is the same in both styles: brake + a steer flick at speed.
        // Exit differs: Slide ends when any condition drops (including centering
        // the stick); Hop is LOCKED to the button — steer is free to sweep the
        // whole range (that's the angle control), only releasing brake or
        // slowing right down ends it.
        bool wantDrift;
        if (driftStyle == DriftStyle.Hop && IsDrifting)
            wantDrift = !spatialDriftActive && driftEnabled && input.brake
                     && Speed > driftMinSpeed * 0.6f;
        else
            wantDrift = !spatialDriftActive
                     && driftEnabled && input.brake
                     && Mathf.Abs(input.steer) > driftSteerThreshold
                     && Speed > driftMinSpeed;

        if (wantDrift && !IsDrifting)
        {
            IsDrifting = true;
            driftTime  = 0f;
            // Entry "flick": snap the nose in the steer direction so the rear
            // breaks loose immediately.  velocityDir is unchanged, so this
            // instantly creates a slide angle that the grip model then plays out.
            float flick  = driftEntryFlickDeg * Mathf.Sign(input.steer);
            worldForward = Quaternion.AngleAxis(flick, splineUp) * worldForward;
            worldForward.Normalize();

            // Hop drift: lock the direction and start the little entry hop.
            hopDriftDir = input.steer >= 0f ? 1 : -1;
            hopSlide    = hopAngleMin;
            hopTimer    = driftStyle == DriftStyle.Hop ? hopDuration : 0f;
        }
        else if (!wantDrift && IsDrifting)
        {
            EndDrift();
        }
        if (IsDrifting) driftTime += dt;
        if (hopTimer     > 0f) hopTimer     -= dt;
        if (railHopTimer > 0f) railHopTimer -= dt;

        // ─── 3b. Speed update ────────────────────────────────────────────────
        if (spatialDriftActive && zoneSpeedOverride >= 0f)
        {
            // Zone Race: the train sets our speed exactly (holds the slot).
            Speed = zoneSpeedOverride;
        }
        else if (spatialDriftActive)
        {
            // Override speed in Spatial Drift mode — values come from the
            // SpatialDriftMode component so they tune in one place.
            if (sdMode == null) sdMode = GetComponent<SpatialDriftMode>();
            float mul       = sdMode != null ? sdMode.speedMultiplier : 1.4f;
            float add       = sdMode != null ? sdMode.speedAdditive   : 25f;
            float topMax    = gearSystem.gearMaxSpeeds[GearSystem.GearCount - 1];
            float targetSpd = topMax * mul + add;
            Speed = Mathf.MoveTowards(Speed, targetSpd, 80f * dt);   // smooth ramp into target
        }
        else if (input.accel > 0f && cap > 0f)
            Speed = Mathf.MoveTowards(Speed, cap, accel * dt);
        else if (input.brake && !IsDrifting)
            // Brake decelerates only when NOT drifting — drifting maintains speed.
            Speed = Mathf.MoveTowards(Speed, 0f,
                                      brakePower * Mathf.Max(gearSystem.CurrentMaxSpeed, 30f) * dt);
        else if (IsDrifting)
        {
            // Drift scrub: a base bleed (driftDrag) plus a slide-angle-scaled
            // term (driftSlideScrub × sin(angle)).  Big sideways slides scrub
            // hard; gentle drifts barely lose speed.
            float scrub = driftDrag + driftSlideScrub * Mathf.Sin(SlideAngle * Mathf.Deg2Rad);
            Speed = Mathf.MoveTowards(Speed, 0f, scrub * dt);
        }
        else
            Speed = Mathf.MoveTowards(Speed, 0f, coastDrag * dt);

        // ─── 3b. Auto-downshift if speed dropped out of gear's range ─────────
        CheckAutoDownshift();

        // ─── 4. Steering + grip ──────────────────────────────────────────────
        if (LateralOrHeadingLocked)
        {
            // Ghost autopilot OR post-exit grace: lock heading to spline tangent.
            // Player steering is ignored; velocity follows rigidly (no slide).
            float k = sdMode != null ? sdMode.headingLockResponse : 8f;
            worldForward = Vector3.Slerp(worldForward, splineTangent,
                                         1f - Mathf.Exp(-k * dt));
            worldForward.Normalize();
            velocityDir  = worldForward;
        }
        else if (IsDrifting && driftStyle == DriftStyle.Hop)
        {
            // Mario-Kart hop drift: the direction is locked; the STICK sets the
            // slide angle (into the drift = tighter, away = shallower), and the
            // angle IS the steering — the arc's turn rate scales with it.
            float into   = Mathf.Clamp(input.steer * hopDriftDir, -1f, 1f);
            float target = Mathf.Lerp(hopAngleMin, hopAngleMax, (into + 1f) * 0.5f);
            hopSlide     = Mathf.Lerp(hopSlide, target,
                                      1f - Mathf.Exp(-hopAngleResponse * dt));

            float bankAng   = Vector3.Angle(splineUp, Vector3.up);
            float bankBoost = 1f + Mathf.Clamp01(bankAng / 45f) * bankSteeringMultiplier;

            float turnDeg = hopTurnPerAngle * hopSlide * hopDriftDir * bankBoost * dt;
            velocityDir   = Quaternion.AngleAxis(turnDeg, splineUp) * velocityDir;
            velocityDir.Normalize();

            // Nose leads the travel by the slide angle — the drift pose.
            worldForward = Quaternion.AngleAxis(hopSlide * hopDriftDir, splineUp) * velocityDir;
            worldForward.Normalize();
        }
        else
        {
            // 4a. Steer the NOSE (worldForward) around the surface-up axis.
            if (Mathf.Abs(Speed) > minSteerSpeed)
            {
                float bankAng   = Vector3.Angle(splineUp, Vector3.up);
                float bankBoost = 1f + Mathf.Clamp01(bankAng / 45f) * bankSteeringMultiplier;
                float driftMul  = IsDrifting ? driftSteerMultiplier : 1f;

                float steerDeg = input.steer * steerRateDeg * bankBoost * driftMul * dt;
                worldForward   = Quaternion.AngleAxis(steerDeg, splineUp) * worldForward;
                worldForward.Normalize();
            }

            // 4b. Slide-angle clamp: don't let the nose lead the velocity by more
            //     than maxSlideAngleDeg — this is the anti-spinout guard.
            if (IsDrifting)
            {
                float signed = Vector3.SignedAngle(velocityDir, worldForward, splineUp);
                if (Mathf.Abs(signed) > maxSlideAngleDeg)
                {
                    float pullBack = signed - Mathf.Sign(signed) * maxSlideAngleDeg;
                    worldForward = Quaternion.AngleAxis(-pullBack, splineUp) * worldForward;
                    worldForward.Normalize();
                }
            }

            // 4c. Grip: the actual travel direction chases the nose.  High grip
            //     (normal) → velocity ≈ nose instantly, so the car goes where it
            //     points.  Low grip (drift) → velocity lags behind the nose, so
            //     the car slides while pointed into the corner — the Initial-D feel.
            float grip  = IsDrifting ? driftGrip : normalGrip;
            velocityDir = Vector3.Slerp(velocityDir, worldForward,
                                        1f - Mathf.Exp(-grip * dt));
            velocityDir.Normalize();
        }

        SlideAngle = Vector3.Angle(velocityDir, worldForward);

        // ─── 5. Decompose velocity into spline-frame components ──────────────
        //     Position advances along velocityDir (where the car actually goes),
        //     NOT worldForward (where it points) — that separation IS the slide.
        float forwardComp = Vector3.Dot(velocityDir, splineTangent);
        float rightComp   = Vector3.Dot(velocityDir, splineRight);

        // Spatial Drift (or post-exit grace): force velocity to be 100% along
        // the spline tangent.  Even tiny heading errors at 228 m/s would
        // otherwise generate lateral drift faster than any snap can cancel.
        if (LateralOrHeadingLocked)
        {
            forwardComp = 1f;
            rightComp   = 0f;
        }

        // ─── 6. Advance along the spline + slide laterally ───────────────────
        DistanceAlongTrack = track.WrapDistance(
            DistanceAlongTrack + Speed * forwardComp * dt);
        LateralOffset += Speed * rightComp * dt;

        // ─── 7. Hard walls: clamp + bounce ───────────────────────────────────
        float lim      = track.HalfWidthAt(DistanceAlongTrack) - 0.3f;
        float wallSide = LateralOffset >  lim ?  1f
                       : LateralOffset < -lim ? -1f : 0f;
        if (wallSide != 0f)
        {
            LateralOffset = wallSide * lim;

            // vn = how directly we're moving INTO this wall (0 = parallel graze,
            // 1 = dead-on perpendicular).  velocityDir is unit, so this is the
            // sine of the impact angle.
            float vn = Vector3.Dot(velocityDir, splineRight) * wallSide;
            if (vn > 0f)
            {
                // Speed cost scales with how head-on the hit is — applied once
                // here, and the bounce below ends contact so it can't compound.
                Speed *= Mathf.Lerp(1f, wallSpeedRetained, vn);

                // Reflect the into-wall component back outward by (1 + bounce):
                // bounce 0 just removes it (slide), >0 leaves an outward component
                // that carries the car off the wall within a frame or two.
                velocityDir -= (1f + wallBounce) * vn * (wallSide * splineRight);
                if (velocityDir.sqrMagnitude > 1e-6f) velocityDir.Normalize();
                else velocityDir = splineTangent;
            }

            // The nose just slides along the wall — no visual jerk.
            KillIntoWall(ref worldForward, splineRight, splineTangent, wallSide);
        }

        // ─── 7c. Spatial Drift / post-exit grace lateral snap ────────────────
        //     Snap any pre-existing lateral offset (from before mode entry,
        //     or from the moment of exit) to zero.  Combined with the
        //     (forwardComp=1, rightComp=0) override above, the car stays
        //     dead-center for the lock duration regardless of how sharp the corner.
        if (LateralOrHeadingLocked)
        {
            float v = zoneSnapSpeedOverride >= 0f ? zoneSnapSpeedOverride
                    : sdMode != null ? sdMode.lateralSnapSpeed : 60f;
            // Active mode steers to the minigame's target lane (centerline for
            // Rhythm); the post-exit grace always re-centers.
            float target = spatialDriftActive
                ? Mathf.Clamp(spatialDriftTargetLateral, -lim, lim)
                : 0f;
            LateralOffset = Mathf.MoveTowards(LateralOffset, target, v * dt);
        }

        // ─── 7b. Track-center magnet (optional assist) ───────────────────────
        //     "Lane assist" style: when off-center, gently rotate the car's
        //     heading inward toward the centerline.  Forward motion then
        //     carries the car back to center — the FRONT leads the correction.
        //     (The prior position-decay version yanked the body sideways with
        //     no rotation change, which felt like the back was being pulled.)
        float magnetHalf = track.HalfWidthAt(DistanceAlongTrack);
        if (centerMagnet > 0f && magnetHalf > 0.01f && Mathf.Abs(LateralOffset) > 0.05f)
        {
            float lateralNorm   = Mathf.Clamp(LateralOffset / magnetHalf, -1f, 1f);
            float correctionDeg = -lateralNorm * centerMagnet * 20f * dt;
            worldForward = Quaternion.AngleAxis(correctionDeg, splineUp) * worldForward;
            worldForward.Normalize();
        }

        // ─── 8. Final world pose ─────────────────────────────────────────────
        track.Sample(DistanceAlongTrack, LateralOffset,
                     out Vector3 newPos, out _, out _, out Vector3 newUp);

        // Hop-drift entry bounce (visual only — half-sine arc over hopDuration).
        if (hopTimer > 0f && hopDuration > 0.01f)
            newPos += newUp * (hopHeight * Mathf.Sin(Mathf.PI * (1f - hopTimer / hopDuration)));

        // Rails-minigame flick bounce — a quick little hop with each rail switch.
        if (railHopTimer > 0f)
            newPos += newUp * (RailHopHeight * Mathf.Sin(Mathf.PI * (1f - railHopTimer / RailHopDur)));

        rb.MovePosition(newPos);
        rb.MoveRotation(Quaternion.LookRotation(worldForward, newUp));
    }

    /// Receive a shove from CarBumpSystem.  Shifts the car sideways, scales its
    /// speed, and knocks both the nose and the travel direction by a few
    /// degrees — enough wobble to feel the hit without spinning anyone out.
    /// Ghost-mode cars are untouchable (core Spatial Drift rule).
    public void ApplyBump(float lateralShift, float speedMultiplier, float headingNudgeDeg)
    {
        if (spatialDriftActive || postExitLockTimer > 0f) return;
        if (track != null)
        {
            float lim = track.HalfWidthAt(DistanceAlongTrack) - 0.3f;
            LateralOffset = Mathf.Clamp(LateralOffset + lateralShift, -lim, lim);
        }
        Speed *= speedMultiplier;

        var nudge    = Quaternion.AngleAxis(headingNudgeDeg, transform.up);
        worldForward = (nudge * worldForward).normalized;
        velocityDir  = (nudge * velocityDir).normalized;

        OnBumped?.Invoke(Mathf.Clamp01(Mathf.Abs(headingNudgeDeg) / 6f + 0.3f));
    }

    // Remove the component of `v` pointing into the wall on side `sign`
    // (+1 = right wall, -1 = left wall), so the vector ends up parallel to the
    // wall.  Falls back to the tangent if it collapses to ~zero.
    static void KillIntoWall(ref Vector3 v, Vector3 splineRight, Vector3 tangent, float sign)
    {
        float dot = Vector3.Dot(v, splineRight);
        if (dot * sign <= 0f) return;            // not pointing into this wall
        v -= splineRight * dot;
        if (v.sqrMagnitude > 1e-6f) v.Normalize();
        else v = tangent;
    }

    // ── Auto-downshift ────────────────────────────────────────────────────────
    // Only PROVEN gears auto-downshift: a gear is proven once the car's speed
    // actually reaches its operating range.  Shifting up from a standstill used
    // to get instantly yanked back to 1 (speed 0 is "out of range" for gear 2) —
    // now a freshly selected gear is left alone while you accelerate into it,
    // and the wall-crash recovery this feature exists for still works.
    int _provenGear = 1;

    void CheckAutoDownshift()
    {
        if (!autoDownshiftEnabled) return;
        if (shifter == null || gearSystem == null) return;
        if (gearSystem.InNeutral) return;

        int current = gearSystem.CurrentGear;
        if (current <= 1) { _provenGear = 1; return; }

        float prevMax = gearSystem.gearMaxSpeeds[current - 2];
        if (Speed >= prevMax * autoDownshiftThreshold) _provenGear = current;
        if (_provenGear != current) return;   // still accelerating into this gear

        int target = FindNaturalGearFor(Speed);
        if (target < current)
        {
            shifter.AutoShiftTo(target);
            _provenGear = target;
        }
    }

    // Highest gear where the car's speed sits inside the gear's range (with a
    // small hysteresis buffer to prevent oscillating at boundaries).
    int FindNaturalGearFor(float speed)
    {
        var maxes = gearSystem.gearMaxSpeeds;
        for (int g = GearSystem.GearCount; g >= 1; g--)
        {
            float prevMax = g > 1 ? maxes[g - 2] : 0f;
            if (speed >= prevMax * autoDownshiftThreshold) return g;
        }
        return 1;
    }

    // ── Drift release ────────────────────────────────────────────────────────
    void EndDrift()
    {
        float charge = DriftCharge;
        if (driftTime >= driftMinDuration && gearSystem != null)
        {
            float t     = Mathf.InverseLerp(driftMinDuration, driftMaxDuration, driftTime);
            float boost = Mathf.Lerp(driftBoostMin, driftBoostMax, t);
            gearSystem.ApplyExternalBoost(boost, driftBoostDuration);
        }
        IsDrifting = false;
        driftTime  = 0f;
        OnDriftEnd?.Invoke(charge);
    }
}
