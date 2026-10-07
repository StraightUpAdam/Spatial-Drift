using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Canvas-based HUD.  Builds the entire UI hierarchy at runtime so there's
/// nothing for you to wire up in the editor — just attach this to the car and
/// hit Play.  You can later customise individual elements by hand if you want,
/// or replace BuildAll() with a prefab.
///
/// Tachometer dial: each boost zone is a Filled Radial360 Image.  Needle is a
/// RectTransform with its pivot at the bottom; rotating it sweeps from the
/// dial centre.  Shifter gate: cells, passages, and gear labels are generated
/// directly from GearShifter's grid data so any layout (H-gate, maze, …)
/// renders correctly with no extra work.
[DisallowMultipleComponent]
public class RacingHUD : MonoBehaviour
{
    [Header("Data Sources (auto-found on this GameObject)")]
    public CarController    car;
    public GearSystem       gears;
    public GearShifter      shifter;
    public SpatialDriftMode sdMode;
    [Tooltip("Auto-found in the scene if blank.  HUD shows lap counter + countdown + results when set.")]
    public RaceManager      race;
    [Tooltip("Multiplayer race state source (auto-found).  Used instead of `race` in MP scenes.")]
    public NetworkRaceDirector mpRace;

    [Header("Canvas")]
    public Vector2 referenceResolution = new Vector2(1920, 1080);
    [Range(50, 200)] public int canvasSortingOrder = 100;

    // ── Visual constants ──────────────────────────────────────────────────────
    const float DialRadius        = 190f;
    const float ZoneInnerRatio    = 0.74f;
    const float NeedleLength      = 158f;
    const float NeedleWidth       = 6f;
    const float TickLengthMajor   = 22f;
    const float TickLengthMinor   = 12f;
    const float CenterCapRadius   = 14f;

    const float CellSize          = 56f;
    const float BallRadius        = 16f;
    const float BallGlowRadius    = 26f;
    const float PassageThickness  = 6f;
    const float NodeRadiusGear    = 10f;
    const float NodeRadiusEmpty   = 5f;

    // Dial sweep: 0% RPM at 240° CW from up (≈8 o'clock).  100% at 480° CW (≈4 o'clock).
    const float DialStartCW = 240f;
    const float DialSweepCW = 240f;

    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color PanelBg       = new Color(0.08f, 0.08f, 0.10f, 0.88f);
    static readonly Color PanelBorder   = new Color(0.42f, 0.42f, 0.48f, 0.75f);
    static readonly Color DialBg        = new Color(0.07f, 0.07f, 0.10f, 0.92f);
    static readonly Color OuterRing     = new Color(0.42f, 0.42f, 0.48f, 0.80f);
    static readonly Color NeedleColor   = new Color(1f, 1f, 1f, 1f);
    static readonly Color CenterCap     = new Color(0.85f, 0.85f, 0.92f, 1f);
    static readonly Color TickMajor     = new Color(0.95f, 0.95f, 0.95f, 0.95f);
    static readonly Color TickMinor     = new Color(0.85f, 0.85f, 0.85f, 0.55f);
    static readonly Color PassageColor  = new Color(0.55f, 0.55f, 0.62f, 0.85f);
    static readonly Color GearNode      = new Color(0.78f, 0.78f, 0.84f, 0.95f);
    static readonly Color EmptyNode     = new Color(0.45f, 0.45f, 0.52f, 0.75f);
    static readonly Color BallColor     = new Color(0.97f, 0.97f, 1f, 1f);
    static readonly Color BallGlowOn    = new Color(0.20f, 1f, 0.35f, 0.55f);
    static readonly Color BallGlowOff   = new Color(0f, 0f, 0f, 0f);

    static readonly Color[] ZoneArc = {
        new Color(0.55f, 0.30f, 0.00f, 0.90f),   // GOOD  (early)
        new Color(1.00f, 0.65f, 0.00f, 0.95f),   // GREAT
        new Color(0.00f, 1.00f, 0.25f, 1.00f),   // PERFECT
        new Color(1.00f, 0.65f, 0.00f, 0.95f),   // GREAT
        new Color(0.55f, 0.30f, 0.00f, 0.90f),   // GOOD  (late)
    };
    static readonly Color[] ZoneFlash = {
        Color.white,
        new Color(1f,   0.85f, 0f,   1f),
        new Color(0.1f, 1f,    0.3f, 1f),
        new Color(1f,   0.85f, 0f,   1f),
        Color.white,
    };

    // ── Built UI references ───────────────────────────────────────────────────
    Canvas              canvas;

    // Top-left
    TextMeshProUGUI     gearText, speedText, controlsText, slipstreamText;
    Image               boostBarBg, boostBarFill;

    // Tachometer
    Image[]             zoneArcImages;
    RectTransform       needleRT;
    TextMeshProUGUI     rpmText;

    // Shifter
    RectTransform       gridRT;
    RectTransform       ballRT;
    Image               ballGlow;
    TextMeshProUGUI[]   gearLabels;

    // Flash
    TextMeshProUGUI     flashText;

    // Cached display values — used to skip redundant text rebuilds (TMP
    // regenerates its mesh on every .text assignment, so we only assign when
    // the visible value actually changed).
    int  _shownGear   = int.MinValue;
    int  _shownKmh    = int.MinValue;
    int  _shownRpmPct = int.MinValue;
    int  _labeledGear = int.MinValue;
    int  _hudLap = int.MinValue, _hudPos = int.MinValue, _hudPhase = int.MinValue;
    int  _mpKmh = int.MinValue;        // (unused placeholder kept for symmetry)
    float _mpGoTimer;                  // brief "GO!" flash after countdown
    int  _mpLastState = -1;
    bool _mpResultsShown;

    // Race UI
    RectTransform       lapPanel;
    TextMeshProUGUI     lapText, currentLapTimeText, lastLapText, bestLapText;
    TextMeshProUGUI     countdownText;
    RectTransform       resultsPanel;
    Image               resultsBg;
    TextMeshProUGUI     resultsTitle, resultsBody, resultsHint;
    float               goBannerFadeTimer;
    bool                raceSubscribed;

    // Spatial Drift UI (DDR-style vertical lanes, purple/void theme)
    RectTransform       sdPanel;          // wraps lanes + lives + score
    RectTransform       sdLanesContainer; // the 4-lane play area
    RectTransform       sdHitZoneBar;     // horizontal bar across lanes at the bottom
    Image               sdHitZoneFlash;
    TextMeshProUGUI     sdScoreText;
    RectTransform       sdReadyPanel;     // "PRESS E / RB" prompt when CanEnter
    TextMeshProUGUI     sdReadyLabel;
    // Lives icons (3 circles, X'd out as you lose lives)
    Image[]             sdLifeIcons       = new Image[3];
    TextMeshProUGUI[]   sdLifeXs          = new TextMeshProUGUI[3];
    bool                sdSubscribed;
    float               sdHitFlashTimer;
    int                 sdLastScoreShown = int.MinValue;
    Vector2             sdPanelFullSize;               // rhythm layout size
    int                 sdLastTimeTenths = int.MinValue;
    int                 sdLastRechargeTenths = int.MinValue;

    // Per-note UI: each active note has a "head" (arrow glyph) and optionally
    // a "tail" (rect representing the hold duration).  Both are POOLED — we
    // pre-create a batch at HUD build time and just toggle activation, so
    // spawning a note doesn't allocate a new GameObject mid-game (which was
    // the source of the hit-time hitches).
    readonly System.Collections.Generic.Dictionary<SpatialDriftMode.Note, TextMeshProUGUI> sdNoteHeads
        = new System.Collections.Generic.Dictionary<SpatialDriftMode.Note, TextMeshProUGUI>();
    readonly System.Collections.Generic.Dictionary<SpatialDriftMode.Note, Image> sdNoteTails
        = new System.Collections.Generic.Dictionary<SpatialDriftMode.Note, Image>();
    readonly System.Collections.Generic.Queue<TextMeshProUGUI> sdHeadPool
        = new System.Collections.Generic.Queue<TextMeshProUGUI>();
    readonly System.Collections.Generic.Queue<Image> sdTailPool
        = new System.Collections.Generic.Queue<Image>();
    readonly System.Collections.Generic.List<SpatialDriftMode.Note> sdStaleNotes
        = new System.Collections.Generic.List<SpatialDriftMode.Note>();
    const int SD_POOL_SIZE = 32;

    // Full-screen purple wash that fades in/out as you enter/exit ghost mode.
    Image  sdScreenTint;
    float  sdScreenTintTarget;
    float  sdScreenTintCurrent;

    // Hit particles — small image bursts that pop up out of the hit zone when
    // you land a note, plus a slow stream while a hold is being maintained.
    sealed class SDParticle
    {
        public RectTransform rt;
        public Image         image;
        public Vector2       pos;
        public Vector2       vel;
        public float         life;
        public float         maxLife;
        public Color         baseColor;
    }
    readonly System.Collections.Generic.List<SDParticle>  sdActiveParticles = new System.Collections.Generic.List<SDParticle>();
    readonly System.Collections.Generic.Queue<SDParticle> sdParticlePool    = new System.Collections.Generic.Queue<SDParticle>();
    const int SD_PARTICLE_POOL_SIZE = 96;

    // osu!-style hit feedback: an expanding ring that pops outward from the
    // hit point + a judgment word (PERFECT / GOOD / MISS) that floats up and
    // fades.  Both pooled — zero allocation per hit.
    sealed class SDRingFx  { public Image ring;  public float age; }
    sealed class SDJudgeFx { public TextMeshProUGUI text; public float age; public Vector2 basePos; }
    readonly System.Collections.Generic.List<SDRingFx>   sdActiveRings  = new System.Collections.Generic.List<SDRingFx>();
    readonly System.Collections.Generic.Queue<SDRingFx>  sdRingPool     = new System.Collections.Generic.Queue<SDRingFx>();
    readonly System.Collections.Generic.List<SDJudgeFx>  sdActiveJudges = new System.Collections.Generic.List<SDJudgeFx>();
    readonly System.Collections.Generic.Queue<SDJudgeFx> sdJudgePool    = new System.Collections.Generic.Queue<SDJudgeFx>();

    // ── Runtime state ─────────────────────────────────────────────────────────
    float flashTimer;
    Color flashColor;
    bool  ballInited;
    bool  subscribed;
    bool  shifterSubscribed;
    bool  carSubscribed;

    // ── Procedural sprites + font (lazy) ──────────────────────────────────────
    static Sprite _circleSprite;
    static Sprite _ringSprite;
    static Sprite CircleSprite => _circleSprite ?? (_circleSprite = MakeCircleSprite(256));
    static Sprite RingSprite   => _ringSprite   ?? (_ringSprite   = MakeRingSprite(256, ZoneInnerRatio));

    /// TMP picks this up from project settings.  If TMP Essentials weren't
    /// imported it'll be null and labels will render blank — warn once.
    static TMP_FontAsset Font_
    {
        get
        {
            var f = TMP_Settings.defaultFontAsset;
            if (f == null && !_fontWarned)
            {
                Debug.LogWarning("[RacingHUD] TMP_Settings.defaultFontAsset is null — " +
                                 "import TMP Essentials via Window → TextMeshPro → Import TMP Essential Resources.");
                _fontWarned = true;
            }
            return f;
        }
    }
    static bool _fontWarned;

    // ════════════════════════════════════════════════════════════════════════════
    //  Lifecycle
    // ════════════════════════════════════════════════════════════════════════════
    void Awake()
    {
        EnsureRefs();
    }

    void Start()
    {
        TryBuild();
    }

    void OnEnable()
    {
        EnsureRefs();
        EnsureSubscription();
    }

    void OnDisable()
    {
        if (subscribed && gears != null)
        {
            gears.OnShift -= HandleShift;
            subscribed = false;
        }
        if (shifterSubscribed && shifter != null)
        {
            shifter.OnSkipRejected -= HandleSkipRejected;
            shifter.OnAutoShift    -= HandleAutoShift;
            shifterSubscribed = false;
        }
        if (raceSubscribed && race != null)
        {
            race.OnRaceStart   -= HandleRaceStart;
            race.OnLapComplete -= HandleLapComplete;
            race.OnRaceFinish  -= HandleRaceFinish;
            raceSubscribed = false;
        }
        if (carSubscribed && car != null)
        {
            car.OnDriftEnd -= HandleDriftEnd;
            carSubscribed   = false;
        }
        if (sdSubscribed && sdMode != null)
        {
            sdMode.OnActivate       -= HandleSDActivate;
            sdMode.OnExit           -= HandleSDExit;
            sdMode.OnNoteSpawn      -= HandleSDNoteSpawn;
            sdMode.OnHit            -= HandleSDHit;
            sdMode.OnNoteMissed     -= HandleSDNoteMissed;
            sdMode.OnLifeLost       -= HandleSDLifeLost;
            sdSubscribed = false;
        }
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void LateUpdate()
    {
        EnsureRefs();
        EnsureSubscription();
        if (canvas == null) TryBuild();
        if (canvas == null || gears == null) return;

        UpdateText();
        UpdateNeedle();
        UpdateBall();
        UpdateBoostBar();
        UpdateFlash();
        UpdateRaceUI();
        UpdateMPRaceUI();
        UpdateSpatialDriftUI();
    }

    void EnsureRefs()
    {
        if (car     == null) car     = GetComponent<CarController>();
        if (gears   == null) gears   = GetComponent<GearSystem>();
        if (shifter == null) shifter = GetComponent<GearShifter>();
        if (sdMode  == null) sdMode  = GetComponent<SpatialDriftMode>();
        if (race    == null) race    = FindFirstObjectByType<RaceManager>();
        if (mpRace  == null) mpRace  = FindFirstObjectByType<NetworkRaceDirector>();
    }

    void EnsureSubscription()
    {
        if (!subscribed && gears != null)
        {
            gears.OnShift += HandleShift;
            subscribed = true;
        }
        if (!shifterSubscribed && shifter != null)
        {
            shifter.OnSkipRejected += HandleSkipRejected;
            shifter.OnAutoShift    += HandleAutoShift;
            shifterSubscribed = true;
        }
        if (!raceSubscribed && race != null)
        {
            race.OnRaceStart    += HandleRaceStart;
            race.OnLapComplete  += HandleLapComplete;
            race.OnRaceFinish   += HandleRaceFinish;
            raceSubscribed = true;
        }
        if (!carSubscribed && car != null)
        {
            car.OnDriftEnd += HandleDriftEnd;
            carSubscribed   = true;
        }
        if (!sdSubscribed && sdMode != null)
        {
            sdMode.OnActivate       += HandleSDActivate;
            sdMode.OnExit           += HandleSDExit;
            sdMode.OnNoteSpawn      += HandleSDNoteSpawn;
            sdMode.OnHit            += HandleSDHit;
            sdMode.OnNoteMissed     += HandleSDNoteMissed;
            sdMode.OnLifeLost       += HandleSDLifeLost;
            sdSubscribed = true;
        }
    }

    void HandleShift(int zone)
    {
        if (zone < 0 || flashText == null) return;
        flashText.text = (zone < gears.boostZones.Length ? gears.boostZones[zone].label : "GO") + "!";
        flashColor     = (zone < ZoneFlash.Length) ? ZoneFlash[zone] : Color.white;
        flashTimer     = 0.9f;
    }

    void HandleSkipRejected(int attempted, int lastValid)
    {
        if (flashText == null) return;
        flashText.text = "CAN'T SKIP";
        flashColor     = new Color(1f, 0.30f, 0.20f, 1f);   // red
        flashTimer     = 0.6f;
    }

    void HandleAutoShift(int newGear)
    {
        if (flashText == null) return;
        flashText.text = $"DOWNSHIFT  {newGear}";
        flashColor     = new Color(0.30f, 0.75f, 1f, 1f);   // cyan
        flashTimer     = 0.7f;
    }

    // ── Race-event handlers ──────────────────────────────────────────────────
    void HandleRaceStart()
    {
        if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
    }

    void HandleLapComplete(RaceManager.RacerEntry entry)
    {
        if (race == null || entry.car != race.localPlayer) return;
        if (flashText == null) return;
        if (entry.finished) return;   // suppress flash on the final lap; results panel handles it

        int   completedLap = entry.lapTimes.Count;            // 1 after first lap
        float lastLapTime  = entry.lapTimes[completedLap - 1];

        if (entry.newBestLastLap)
        {
            flashText.text = entry.newPureLastLap ? "NEW BEST + PURE LAP!" : "NEW BEST LAP!";
            flashColor     = new Color(0.20f, 1f, 0.40f, 1f);
        }
        else if (entry.newPureLastLap)
        {
            flashText.text = $"NEW PURE LAP!  {RaceManager.FormatTime(lastLapTime)}";
            flashColor     = new Color(0.30f, 0.90f, 1f, 1f);
        }
        else
        {
            flashText.text = $"LAP {completedLap}  {RaceManager.FormatTime(lastLapTime)}";
            flashColor     = new Color(0.95f, 0.95f, 1f, 1f);
        }
        flashTimer = 1.2f;
    }

    void HandleRaceFinish()
    {
        ShowResults();
    }

    void HandleDriftEnd(float charge)
    {
        if (flashText == null || charge < 0.01f) return;

        if (charge >= 1f)
        {
            flashText.text = "DRIFT BOOST! MAX";
            flashColor     = new Color(1f, 0.45f, 0.20f, 1f);   // orange
        }
        else
        {
            int pct = Mathf.RoundToInt(charge * 100f);
            flashText.text = $"DRIFT  +{pct}%";
            flashColor     = new Color(1f, 0.65f, 0.30f, 1f);   // amber
        }
        flashTimer = 0.7f;
    }

    void TryBuild()
    {
        if (canvas != null || gears == null || shifter == null) return;
        BuildAll();
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Per-frame updates
    // ════════════════════════════════════════════════════════════════════════════
    // Re-set HUD text ONLY when the visible value changes.  Every `.text`
    // assignment forces a TMP mesh rebuild + (with interpolation) a string
    // allocation, so gating on a cached int keeps the steady state allocation-
    // and rebuild-free.  Speed/RPM are quantised to the integer we actually
    // display, so they update ~10×/sec (when the number ticks) instead of 60+.
    void UpdateText()
    {
        int gear = gears.InNeutral ? 0 : gears.CurrentGear;
        if (gear != _shownGear)
        {
            gearText.text = gears.InNeutral
                ? "GEAR —"
                : $"GEAR {gear} / {GearSystem.GearCount}";
            _shownGear = gear;
        }

        int spd = GameSettings.DisplaySpeed(car.Speed) * 2 + (GameSettings.SpeedMph ? 1 : 0);   // low bit = unit
        if (spd != _shownKmh)
        {
            speedText.text = $"{spd / 2} {GameSettings.SpeedUnit}";
            _shownKmh = spd;
        }

        int rpmPct = Mathf.RoundToInt(gears.RPM * 100f);
        if (rpmPct != _shownRpmPct)
        {
            rpmText.text = $"{rpmPct}% RPM";
            _shownRpmPct = rpmPct;
        }

        // Slipstream indicator — SetActive only on change.
        if (slipstreamText != null)
        {
            bool slip = car.InSlipstream;
            if (slipstreamText.gameObject.activeSelf != slip)
                slipstreamText.gameObject.SetActive(slip);
        }

        // Gear-label styling only changes when the engaged gear changes.
        if (gearLabels != null && gears.CurrentGear != _labeledGear)
        {
            _labeledGear = gears.CurrentGear;
            for (int g = 1; g <= GearSystem.GearCount; g++)
            {
                if (g - 1 >= gearLabels.Length || gearLabels[g - 1] == null) continue;
                bool active = (g == gears.CurrentGear);
                gearLabels[g - 1].fontSize  = active ? 22 : 17;
                gearLabels[g - 1].color     = active ? Color.white
                                                     : new Color(0.55f, 0.55f, 0.60f, 0.85f);
                gearLabels[g - 1].fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            }
        }
    }

    void UpdateNeedle()
    {
        float cw = DialStartCW + gears.RPM * DialSweepCW;
        // Unity z-rotation is counter-clockwise positive, so negate.
        needleRT.localEulerAngles = new Vector3(0, 0, -cw);
    }

    void UpdateBall()
    {
        if (ballRT == null || shifter == null) return;

        Vector2 target = CellPosLocal(shifter.Position);
        if (!ballInited) { ballRT.anchoredPosition = target; ballInited = true; }
        else             ballRT.anchoredPosition = Vector2.Lerp(ballRT.anchoredPosition, target,
                                                                20f * Time.deltaTime);

        // Glow tracks the ball
        if (ballGlow != null)
            ballGlow.rectTransform.anchoredPosition = ballRT.anchoredPosition;

        bool inZone = !gears.InNeutral && gears.ZoneAt(gears.RPM) >= 0;
        ballGlow.color = inZone ? BallGlowOn : BallGlowOff;
    }

    void UpdateBoostBar()
    {
        bool boosting = gears.IsBoosting;
        if (boostBarBg.gameObject.activeSelf != boosting)
            boostBarBg.gameObject.SetActive(boosting);
        if (boostBarFill.gameObject.activeSelf != boosting)
            boostBarFill.gameObject.SetActive(boosting);
        if (boosting && gears.boostDuration > 0f)
            boostBarFill.fillAmount = gears.BoostRemaining / gears.boostDuration;
    }

    void UpdateFlash()
    {
        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;
            float a = Mathf.Clamp01(flashTimer * 2f);
            flashText.color = new Color(flashColor.r, flashColor.g, flashColor.b, a);
        }
        else if (!string.IsNullOrEmpty(flashText.text))
        {
            flashText.text = "";
        }
    }

    // Position inside the CROPPED, scaled gate (see BuildShifterGate).
    Vector2 CellPosLocal(Vector2Int cell)
    {
        float x = (cell.x - gateMin.x - (gateSize.x - 1) * 0.5f) * gateCell;
        float y = ((gateSize.y - 1) * 0.5f - (cell.y - gateMin.y)) * gateCell;
        return new Vector2(x, y);
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Build hierarchy
    // ════════════════════════════════════════════════════════════════════════════
    void BuildAll()
    {
        var canvasGO = new GameObject("RacingHUDCanvas");
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        BuildTopLeft();
        BuildTachometer();
        BuildShifterGate();
        BuildFlash();
        BuildLapPanel();
        BuildCountdown();
        BuildResultsPanel();
        BuildSpatialDriftUI();
    }

    // ── Top-left panel ────────────────────────────────────────────────────────
    void BuildTopLeft()
    {
        var panel = MakeRect("TopLeft", canvas.transform);
        SetAnchor(panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
        panel.anchoredPosition = new Vector2(36, -36);
        panel.sizeDelta        = new Vector2(560, 240);

        gearText = MakeText("Gear", panel, 56, Color.white, FontStyles.Bold);
        StretchTop(gearText.rectTransform, 0, 70);

        speedText = MakeText("Speed", panel, 40, Color.white, FontStyles.Bold);
        StretchTop(speedText.rectTransform, 78, 50);

        // Slipstream indicator — visible only while riding another car's wake.
        slipstreamText = MakeText("Slipstream", panel, 22,
                                  new Color(0.45f, 0.90f, 1f, 0.95f), FontStyles.Bold);
        StretchTop(slipstreamText.rectTransform, 134, 26);
        slipstreamText.text = ">>  SLIPSTREAM";
        slipstreamText.gameObject.SetActive(false);

        // Boost bar
        boostBarBg = MakeImage("BoostBarBg", panel, new Color(0, 0, 0, 0.35f));
        SetAnchor(boostBarBg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
        boostBarBg.rectTransform.anchoredPosition = new Vector2(0, -172);
        boostBarBg.rectTransform.sizeDelta        = new Vector2(320, 14);

        boostBarFill = MakeImage("BoostBarFill", panel, new Color(0.22f, 0.82f, 1f, 0.92f));
        boostBarFill.type         = Image.Type.Filled;
        boostBarFill.fillMethod   = Image.FillMethod.Horizontal;
        boostBarFill.fillOrigin   = (int)Image.OriginHorizontal.Left;
        boostBarFill.fillAmount   = 0f;
        SetAnchor(boostBarFill.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
        boostBarFill.rectTransform.anchoredPosition = new Vector2(0, -172);
        boostBarFill.rectTransform.sizeDelta        = new Vector2(320, 14);

        boostBarBg.gameObject.SetActive(false);
        boostBarFill.gameObject.SetActive(false);

        controlsText = MakeText("Controls", panel, 18,
                                new Color(0.62f, 0.62f, 0.68f, 0.85f), FontStyles.Normal);
        StretchTop(controlsText.rectTransform, 202, 22);
        controlsText.text = "W/S throttle/brake   |   A/D steer   |   Arrow keys move shifter";
    }

    // ── Tachometer ────────────────────────────────────────────────────────────
    void BuildTachometer()
    {
        var tach = MakeRect("Tachometer", canvas.transform);
        SetAnchor(tach, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0));
        tach.anchoredPosition = new Vector2(-(36 + DialRadius), 36 + DialRadius);
        tach.sizeDelta        = new Vector2(DialRadius * 2, DialRadius * 2);

        // Background circle
        var bg = MakeImage("Bg", tach, DialBg, CircleSprite);
        Stretch(bg.rectTransform);

        // Outer ring
        var ring = MakeImage("OuterRing", tach, OuterRing, RingSprite);
        Stretch(ring.rectTransform);
        // The ring sprite is already a thin ring (innerRatio = 0.74), but we want a
        // very thin outer line.  Easier path: scale the ring image slightly so only
        // the outermost portion is visible.  Actually skip — just keep the sprite.

        // Zone arcs — one Image per boost zone, filled Radial360
        zoneArcImages = new Image[gears.boostZones.Length];
        for (int i = 0; i < gears.boostZones.Length; i++)
        {
            var zone = gears.boostZones[i];
            var col  = i < ZoneArc.Length ? ZoneArc[i] : Color.gray;

            var arc = MakeImage($"Zone{i}", tach, col, RingSprite);
            arc.type          = Image.Type.Filled;
            arc.fillMethod    = Image.FillMethod.Radial360;
            arc.fillOrigin    = (int)Image.Origin360.Top;
            arc.fillClockwise = true;
            arc.fillAmount    = (zone.maxRPM - zone.minRPM) * DialSweepCW / 360f;
            Stretch(arc.rectTransform);

            // Rotate so the START of the fill is at the zone's start angle
            float startCW = DialStartCW + zone.minRPM * DialSweepCW;
            arc.rectTransform.localEulerAngles = new Vector3(0, 0, -startCW);

            zoneArcImages[i] = arc;
        }

        // Tick marks at every 10%
        for (int t = 0; t <= 10; t++)
        {
            bool  major = (t % 5 == 0);
            float len   = major ? TickLengthMajor : TickLengthMinor;
            float w     = major ? 4f : 2f;
            Color c     = major ? TickMajor : TickMinor;

            var tick = MakeImage($"Tick{t}", tach, c);
            tick.rectTransform.sizeDelta = new Vector2(w, len);

            // Compute position on the outer edge in UI local coords
            float cw       = DialStartCW + t / 10f * DialSweepCW;
            float angleRad = (90f - cw) * Mathf.Deg2Rad;     // convert "CW from up" → standard math angle
            float r        = DialRadius - len * 0.5f - 6f;
            float x        = r * Mathf.Cos(angleRad);
            float y        = r * Mathf.Sin(angleRad);

            tick.rectTransform.anchoredPosition = new Vector2(x, y);
            tick.rectTransform.localEulerAngles = new Vector3(0, 0, -cw);
        }

        // RPM label (sits low inside the dial)
        rpmText = MakeText("RpmLabel", tach, 18, new Color(0.95f, 0.95f, 0.95f, 0.85f), FontStyles.Bold);
        SetAnchor(rpmText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        rpmText.rectTransform.anchoredPosition = new Vector2(0, -DialRadius * 0.45f);
        rpmText.rectTransform.sizeDelta        = new Vector2(160, 26);
        rpmText.alignment                       = TextAlignmentOptions.Center;

        // Needle — pivot at bottom-centre so rotation pivots around the dial centre
        var needleImg = MakeImage("Needle", tach, NeedleColor);
        needleRT = needleImg.rectTransform;
        SetAnchor(needleRT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
        needleRT.anchoredPosition = new Vector2(0, 0);
        needleRT.sizeDelta        = new Vector2(NeedleWidth, NeedleLength);

        // Centre cap (on top of needle base)
        var cap = MakeImage("Cap", tach, CenterCap, CircleSprite);
        SetAnchor(cap.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        cap.rectTransform.anchoredPosition = Vector2.zero;
        cap.rectTransform.sizeDelta        = new Vector2(CenterCapRadius * 2, CenterCapRadius * 2);
    }

    // ── Shifter gate ──────────────────────────────────────────────────────────
    // Drawn straight from the GearShifter grid: only the nodes the gate
    // actually uses (a gear, or a passage to a neighbour) and only its open
    // passages, cropped to that area and scaled to fit, so a wide H, a tall
    // sequential ladder or a maze all sit neatly in the same slot.
    const float GateMaxW = 300f, GateMaxH = 220f;
    Vector2Int gateMin = Vector2Int.zero, gateSize = Vector2Int.one;
    float      gateCell = CellSize;

    bool Open(Vector2Int a, Vector2Int b) =>
        shifter.InBounds(a) && shifter.InBounds(b)
        && shifter.CellAt(a).kind != GearShifter.CellKind.Wall
        && shifter.CellAt(b).kind != GearShifter.CellKind.Wall
        && !shifter.HasWallBetween(a, b);

    bool UsedNode(Vector2Int p)
    {
        var k = shifter.CellAt(p).kind;
        if (k == GearShifter.CellKind.Wall) return false;
        if (k == GearShifter.CellKind.Gear) return true;
        return Open(p, p + Vector2Int.left) || Open(p, p + Vector2Int.right)
            || Open(p, p + Vector2Int.up)   || Open(p, p + Vector2Int.down);
    }

    void BuildShifterGate()
    {
        int W = shifter.Width, H = shifter.Height;

        // Crop to the used nodes.
        var lo = new Vector2Int(W, H); var hi = new Vector2Int(-1, -1);
        for (int r = 0; r < H; r++)
        for (int c = 0; c < W; c++)
            if (UsedNode(new Vector2Int(c, r)))
            { lo = Vector2Int.Min(lo, new Vector2Int(c, r)); hi = Vector2Int.Max(hi, new Vector2Int(c, r)); }
        if (hi.x < 0) { lo = Vector2Int.zero; hi = new Vector2Int(W - 1, H - 1); }
        gateMin  = lo;
        gateSize = hi - lo + Vector2Int.one;

        // Scale the pitch to fit the HUD slot.
        float fitW = gateSize.x > 1 ? GateMaxW / (gateSize.x - 1) : CellSize;
        float fitH = gateSize.y > 1 ? GateMaxH / (gateSize.y - 1) : CellSize;
        gateCell = Mathf.Clamp(Mathf.Min(CellSize, fitW, fitH), 20f, CellSize);
        float k  = gateCell / CellSize;

        float panelW = (gateSize.x - 1) * gateCell + 96f;
        float panelH = (gateSize.y - 1) * gateCell + 118f;

        var panel = MakeRect("ShifterGate", canvas.transform);
        SetAnchor(panel, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
        panel.anchoredPosition = new Vector2(0, 36);
        panel.sizeDelta        = new Vector2(panelW, panelH);

        var bg = MakeImage("Bg", panel, PanelBg);
        Stretch(bg.rectTransform);
        AddBorder(panel, panelW, panelH);

        var title = MakeText("Title", panel, 16, new Color(0.65f, 0.65f, 0.7f, 0.95f), FontStyles.Bold);
        SetAnchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
        title.rectTransform.anchoredPosition = new Vector2(0, -8);
        title.rectTransform.sizeDelta        = new Vector2(panelW, 20);
        title.alignment                       = TextAlignmentOptions.Center;
        title.text                            = "SHIFTER";

        gridRT = MakeRect("Grid", panel);
        SetAnchor(gridRT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        gridRT.anchoredPosition = new Vector2(0, -12);
        gridRT.sizeDelta        = new Vector2((gateSize.x - 1) * gateCell, (gateSize.y - 1) * gateCell);

        float thick = Mathf.Max(3f, PassageThickness * k);
        for (int r = 0; r < H; r++)
        for (int c = 0; c < W; c++)
        {
            var a = new Vector2Int(c, r);
            var right = new Vector2Int(c + 1, r);
            var below = new Vector2Int(c, r + 1);          // rows grow downward
            if (Open(a, right))
            {
                var line = MakeImage($"PassageH_{c}_{r}", gridRT, PassageColor);
                line.rectTransform.anchoredPosition = (CellPosLocal(a) + CellPosLocal(right)) * 0.5f;
                line.rectTransform.sizeDelta        = new Vector2(gateCell, thick);
            }
            if (Open(a, below))
            {
                var line = MakeImage($"PassageV_{c}_{r}", gridRT, PassageColor);
                line.rectTransform.anchoredPosition = (CellPosLocal(a) + CellPosLocal(below)) * 0.5f;
                line.rectTransform.sizeDelta        = new Vector2(thick, gateCell);
            }
        }

        // Nodes + gear labels.  Each label goes on a side of its node that has
        // no passage, so numbers never sit on top of the gate.
        gearLabels = new TextMeshProUGUI[GearSystem.GearCount];
        float off = Mathf.Max(15f, 22f * k);
        for (int r = 0; r < H; r++)
        for (int c = 0; c < W; c++)
        {
            var p = new Vector2Int(c, r);
            if (!UsedNode(p)) continue;
            var cellData = shifter.CellAt(p);
            Vector2 at    = CellPosLocal(p);
            bool   isGear = cellData.kind == GearShifter.CellKind.Gear;
            float  nodeR  = (isGear ? NodeRadiusGear : NodeRadiusEmpty) * Mathf.Max(0.6f, k);

            var node = MakeImage($"Node_{c}_{r}", gridRT, isGear ? GearNode : EmptyNode, CircleSprite);
            node.rectTransform.anchoredPosition = at;
            node.rectTransform.sizeDelta        = new Vector2(nodeR * 2, nodeR * 2);

            if (isGear && cellData.gear >= 1 && cellData.gear <= GearSystem.GearCount)
            {
                // Grid row - 1 is screen-up.
                Vector2 side = !Open(p, new Vector2Int(c, r - 1)) ? Vector2.up
                             : !Open(p, new Vector2Int(c, r + 1)) ? Vector2.down
                             : !Open(p, new Vector2Int(c + 1, r)) ? Vector2.right
                             : !Open(p, new Vector2Int(c - 1, r)) ? Vector2.left
                             : new Vector2(0.75f, 0.75f);
                var lbl = MakeText($"Label_{cellData.gear}", gridRT, 17,
                                   new Color(0.55f, 0.55f, 0.60f, 0.85f), FontStyles.Normal);
                lbl.rectTransform.anchoredPosition = at + side * off;
                lbl.rectTransform.sizeDelta        = new Vector2(32, 20);
                lbl.alignment                       = TextAlignmentOptions.Center;
                lbl.text                            = cellData.gear.ToString();
                gearLabels[cellData.gear - 1]       = lbl;
            }
        }

        var glowImg = MakeImage("BallGlow", gridRT, BallGlowOff, CircleSprite);
        ballGlow    = glowImg;
        ballGlow.rectTransform.sizeDelta = Vector2.one * (BallGlowRadius * 2 * Mathf.Max(0.6f, k));

        var ballImg = MakeImage("Ball", gridRT, BallColor, CircleSprite);
        ballRT      = ballImg.rectTransform;
        ballRT.sizeDelta = Vector2.one * (BallRadius * 2 * Mathf.Max(0.6f, k));
    }

    void AddBorder(RectTransform panel, float w, float h)
    {
        var top    = MakeImage("BorderT", panel, PanelBorder);
        SetAnchor(top.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
        top.rectTransform.sizeDelta        = new Vector2(0, 2);
        top.rectTransform.anchoredPosition = Vector2.zero;

        var bot = MakeImage("BorderB", panel, PanelBorder);
        SetAnchor(bot.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0));
        bot.rectTransform.sizeDelta        = new Vector2(0, 2);
        bot.rectTransform.anchoredPosition = Vector2.zero;

        var lt = MakeImage("BorderL", panel, PanelBorder);
        SetAnchor(lt.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f));
        lt.rectTransform.sizeDelta        = new Vector2(2, 0);
        lt.rectTransform.anchoredPosition = Vector2.zero;

        var rt = MakeImage("BorderR", panel, PanelBorder);
        SetAnchor(rt.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f));
        rt.rectTransform.sizeDelta        = new Vector2(2, 0);
        rt.rectTransform.anchoredPosition = Vector2.zero;
    }

    // ── Flash ─────────────────────────────────────────────────────────────────
    void BuildFlash()
    {
        flashText = MakeText("Flash", canvas.transform, 96, new Color(1, 1, 1, 0), FontStyles.Bold);
        SetAnchor(flashText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        flashText.rectTransform.anchoredPosition = new Vector2(0, 80);
        flashText.rectTransform.sizeDelta        = new Vector2(700, 140);
        flashText.alignment                       = TextAlignmentOptions.Center;
        flashText.text                            = "";
    }

    // ── Race UI: lap counter (top-center) ────────────────────────────────────
    void BuildLapPanel()
    {
        lapPanel = MakeRect("LapPanel", canvas.transform);
        SetAnchor(lapPanel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
        lapPanel.anchoredPosition = new Vector2(0, -28);
        lapPanel.sizeDelta        = new Vector2(380, 140);

        var bg = MakeImage("Bg", lapPanel, new Color(0.06f, 0.06f, 0.08f, 0.55f));
        Stretch(bg.rectTransform);

        lapText = MakeText("Lap", lapPanel, 22, new Color(0.85f, 0.85f, 0.9f, 0.95f), FontStyles.Bold);
        StretchTop(lapText.rectTransform, 8, 26);
        lapText.alignment = TextAlignmentOptions.Center;
        lapText.text      = "LAP - / -";

        currentLapTimeText = MakeText("CurLap", lapPanel, 44, Color.white, FontStyles.Bold);
        StretchTop(currentLapTimeText.rectTransform, 36, 52);
        currentLapTimeText.alignment = TextAlignmentOptions.Center;
        currentLapTimeText.text      = "0:00.00";

        lastLapText = MakeText("Last", lapPanel, 15,
                               new Color(0.7f, 0.7f, 0.75f, 0.95f), FontStyles.Normal);
        StretchTop(lastLapText.rectTransform, 92, 18);
        lastLapText.alignment = TextAlignmentOptions.Center;
        lastLapText.text      = "Last  -:--.--";

        bestLapText = MakeText("Best", lapPanel, 15,
                               new Color(0.45f, 0.95f, 0.55f, 0.95f), FontStyles.Bold);
        StretchTop(bestLapText.rectTransform, 112, 18);
        bestLapText.alignment = TextAlignmentOptions.Center;
        bestLapText.text      = "Best  -:--.--";

        if (race == null) lapPanel.gameObject.SetActive(false);
    }

    // ── Race UI: huge centered countdown / GO! banner ────────────────────────
    void BuildCountdown()
    {
        countdownText = MakeText("Countdown", canvas.transform, 220,
                                 new Color(1, 1, 1, 0), FontStyles.Bold);
        SetAnchor(countdownText.rectTransform,
                  new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        countdownText.rectTransform.anchoredPosition = Vector2.zero;
        countdownText.rectTransform.sizeDelta        = new Vector2(800, 280);
        countdownText.alignment                       = TextAlignmentOptions.Center;
        countdownText.text                            = "";
    }

    // ── Race UI: full-screen results panel ───────────────────────────────────
    void BuildResultsPanel()
    {
        resultsPanel = MakeRect("ResultsPanel", canvas.transform);
        SetAnchor(resultsPanel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        resultsPanel.offsetMin = Vector2.zero;
        resultsPanel.offsetMax = Vector2.zero;

        resultsBg = MakeImage("Dim", resultsPanel, new Color(0, 0, 0, 0.72f));
        Stretch(resultsBg.rectTransform);

        resultsTitle = MakeText("Title", resultsPanel, 96,
                                new Color(0.45f, 0.95f, 0.55f, 1f), FontStyles.Bold);
        SetAnchor(resultsTitle.rectTransform,
                  new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
        resultsTitle.rectTransform.anchoredPosition = new Vector2(0, -90);
        resultsTitle.rectTransform.sizeDelta        = new Vector2(1200, 120);
        resultsTitle.alignment                       = TextAlignmentOptions.Center;
        resultsTitle.text                            = "FINISHED";

        resultsBody = MakeText("Body", resultsPanel, 38, Color.white, FontStyles.Normal);
        SetAnchor(resultsBody.rectTransform,
                  new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        resultsBody.rectTransform.anchoredPosition = new Vector2(0, 20);
        resultsBody.rectTransform.sizeDelta        = new Vector2(900, 400);
        resultsBody.alignment                       = TextAlignmentOptions.Center;
        resultsBody.text                            = "";

        resultsHint = MakeText("Hint", resultsPanel, 26,
                               new Color(0.85f, 0.85f, 0.9f, 0.9f), FontStyles.Normal);
        SetAnchor(resultsHint.rectTransform,
                  new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
        resultsHint.rectTransform.anchoredPosition = new Vector2(0, 80);
        resultsHint.rectTransform.sizeDelta        = new Vector2(800, 40);
        resultsHint.alignment                       = TextAlignmentOptions.Center;
        resultsHint.text                            = "Press  R  to return to menu";

        resultsPanel.gameObject.SetActive(false);
    }

    /// Set by the baked results screens (RaceMenus / MPRaceMenus): when they
    /// exist, the HUD's own fallback results panel stays hidden.
    public static bool ExternalResults;

    void ShowResults()
    {
        if (ExternalResults) return;
        if (resultsPanel == null || race == null) return;
        var entry = race.LocalEntry;
        if (entry == null) { resultsPanel.gameObject.SetActive(true); return; }

        // Title reflects placement when racing against opponents.
        if (resultsTitle != null)
        {
            if (race.RacerCount > 1 && entry.finishPosition > 0)
            {
                bool won = entry.finishPosition == 1;
                resultsTitle.text  = won ? "VICTORY!" : $"FINISHED  ·  P{entry.finishPosition}";
                resultsTitle.color = won
                    ? new Color(1f, 0.85f, 0.30f, 1f)      // gold
                    : new Color(0.45f, 0.95f, 0.55f, 1f);  // green
            }
            else
            {
                resultsTitle.text  = "FINISHED";
                resultsTitle.color = new Color(0.45f, 0.95f, 0.55f, 1f);
            }
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < entry.lapTimes.Count; i++)
        {
            string mark = (entry.bestLap < float.PositiveInfinity &&
                           Mathf.Approximately(entry.lapTimes[i], entry.bestLap)) ? "  ★" : "";
            sb.Append($"Lap {i + 1}    {RaceManager.FormatTime(entry.lapTimes[i])}{mark}\n");
        }
        sb.Append($"\nTotal     {RaceManager.FormatTime(entry.totalTime)}");
        if (entry.bestLap < float.PositiveInfinity)
            sb.Append($"\nBest lap  {RaceManager.FormatTime(entry.bestLap)}");

        resultsBody.text = sb.ToString();
        resultsPanel.gameObject.SetActive(true);
    }

    // ── Race UI: per-frame update ────────────────────────────────────────────
    void UpdateRaceUI()
    {
        if (race == null) return;
        if (lapPanel != null && !lapPanel.gameObject.activeSelf)
            lapPanel.gameObject.SetActive(true);

        var entry = race.LocalEntry;
        if (entry == null) return;

        // Lap panel — with live race position when there's more than one car.
        // Rebuilt only when something visible changes (TMP rebuild + string
        // alloc otherwise happen every frame).
        if (lapText != null)
        {
            int displayLap = Mathf.Clamp(entry.currentLap, 0, race.totalLaps);
            int pos        = race.RacerCount > 1 ? race.GetLivePosition(entry) : 0;
            int phase      = entry.finished ? 2
                           : race.CurrentState == RaceManager.State.PreRace ? 0 : 1;

            if (displayLap != _hudLap || pos != _hudPos || phase != _hudPhase)
            {
                _hudLap = displayLap; _hudPos = pos; _hudPhase = phase;
                string posPrefix = pos > 0 ? $"P{pos}   ·   " : "";
                lapText.text = phase switch
                {
                    2 => $"{posPrefix}FINISHED  {race.totalLaps} / {race.totalLaps}",
                    0 => $"{posPrefix}LAP  -  / {race.totalLaps}",
                    _ => $"{posPrefix}LAP  {displayLap}  / {race.totalLaps}",
                };
            }
        }
        if (currentLapTimeText != null)
            currentLapTimeText.text = RaceManager.FormatTime(entry.currentLapTime);
        if (lastLapText != null)
            lastLapText.text = entry.lapTimes.Count > 0
                ? $"Last  {RaceManager.FormatTime(entry.lapTimes[entry.lapTimes.Count - 1])}"
                : "Last  -:--.--";
        if (bestLapText != null)
            bestLapText.text = entry.bestLap < float.PositiveInfinity
                ? $"Best  {RaceManager.FormatTime(entry.bestLap)}"
                : "Best  -:--.--";

        // Countdown / GO banner
        if (countdownText != null)
        {
            if (race.CurrentState == RaceManager.State.PreRace)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(race.CountdownRemaining));
                countdownText.text  = n.ToString();
                // Pulse in / fade out within each one-second slice
                float frac          = race.CountdownRemaining - Mathf.Floor(race.CountdownRemaining);
                float alpha         = Mathf.Clamp01(frac);   // 1 → 0 across the second
                countdownText.color = new Color(1f, 0.9f, 0.4f, alpha);
            }
            else if (race.GoBannerRemaining > 0f)
            {
                countdownText.text  = "GO!";
                float a             = Mathf.Clamp01(race.GoBannerRemaining / Mathf.Max(0.01f, race.goBannerDuration));
                countdownText.color = new Color(0.30f, 1f, 0.45f, a);
            }
            else if (!string.IsNullOrEmpty(countdownText.text))
            {
                countdownText.text = "";
            }
        }
    }

    // ── Multiplayer race UI (driven by NetworkRaceDirector) ───────────────────
    void UpdateMPRaceUI()
    {
        if (mpRace == null || race != null) return;   // SP path owns the UI when present

        // Zone Race has its own HUD (the whole race is in the zone) — hide ours.
        if (mpRace.IsZoneRace)
        {
            if (canvas != null && canvas.enabled) canvas.enabled = false;
            return;
        }

        // Time trial: NetworkTimeTrial owns the timer UI — hide the race lap panel.
        if (mpRace.IsTimeTrial)
        {
            if (lapPanel != null && lapPanel.gameObject.activeSelf)
                lapPanel.gameObject.SetActive(false);
            return;
        }

        if (lapPanel != null && !lapPanel.gameObject.activeSelf)
            lapPanel.gameObject.SetActive(true);

        var localCar = CarController.LocalPlayerOverride;
        var sync     = localCar != null ? localCar.GetComponent<NetworkCarSync>() : null;

        var  st       = mpRace.CurrentState;
        int  total    = mpRace.TotalLaps;
        bool racing   = st == NetworkRaceDirector.RaceState.Racing
                     || st == NetworkRaceDirector.RaceState.Finished;
        bool finished = sync != null && sync.FinishPos.Value > 0;

        // Lap + position
        if (lapText != null && sync != null)
        {
            int lap   = Mathf.Clamp(sync.Lap.Value, 1, total);
            int pos   = mpRace.RacerCount > 1 ? mpRace.PositionOf(sync) : 0;
            int phase = finished ? 2 : racing ? 1 : 0;

            if (lap != _hudLap || pos != _hudPos || phase != _hudPhase)
            {
                _hudLap = lap; _hudPos = pos; _hudPhase = phase;
                string posPrefix = pos > 0 ? $"P{pos}   ·   " : "";
                lapText.text = phase switch
                {
                    2 => $"{posPrefix}FINISHED  {total} / {total}",
                    0 => $"{posPrefix}LAP  -  / {total}",
                    _ => $"{posPrefix}LAP  {lap}  / {total}",
                };
            }
        }
        if (currentLapTimeText != null) currentLapTimeText.text = RaceManager.FormatTime(mpRace.RaceElapsed);
        if (lastLapText != null) lastLapText.text = "";
        if (bestLapText != null) bestLapText.text = "";

        // Countdown / GO
        if ((int)st != _mpLastState)
        {
            if (st == NetworkRaceDirector.RaceState.Racing) _mpGoTimer = 1.0f;
            _mpLastState = (int)st;
        }
        if (_mpGoTimer > 0f) _mpGoTimer -= Time.deltaTime;

        if (countdownText != null)
        {
            if (st == NetworkRaceDirector.RaceState.Countdown)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(mpRace.CountdownRemaining));
                countdownText.text  = n.ToString();
                float frac          = mpRace.CountdownRemaining - Mathf.Floor(mpRace.CountdownRemaining);
                countdownText.color = new Color(1f, 0.9f, 0.4f, Mathf.Clamp01(frac));
            }
            else if (_mpGoTimer > 0f)
            {
                countdownText.text  = "GO!";
                countdownText.color = new Color(0.30f, 1f, 0.45f, Mathf.Clamp01(_mpGoTimer));
            }
            else if (!string.IsNullOrEmpty(countdownText.text))
            {
                countdownText.text = "";
            }
        }

        // Results when the local player crosses the final line.
        if (finished && !_mpResultsShown)
        {
            _mpResultsShown = true;
            ShowMPResults(sync.FinishPos.Value, mpRace.RacerCount, mpRace.RaceElapsed);
        }
    }

    void ShowMPResults(int pos, int racers, float elapsed)
    {
        if (ExternalResults) return;
        if (resultsPanel == null) return;

        if (resultsTitle != null)
        {
            bool won = pos == 1;
            resultsTitle.text  = won ? "VICTORY!" : $"FINISHED  ·  P{pos}";
            resultsTitle.color = won ? new Color(1f, 0.85f, 0.30f, 1f)
                                     : new Color(0.45f, 0.95f, 0.55f, 1f);
        }
        if (resultsBody != null)
            resultsBody.text = $"Position   P{pos} / {racers}\n\nRace time   {RaceManager.FormatTime(elapsed)}";
        if (resultsHint != null)
            resultsHint.text = "Press  Esc  to leave the race";

        resultsPanel.gameObject.SetActive(true);
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Spatial Drift UI (DDR-style, purple / void theme)
    // ════════════════════════════════════════════════════════════════════════════
    // Layout: 4 vertical lanes side-by-side, notes fall top → bottom.
    //   Hit zone is a horizontal bar near the bottom of the lanes.
    //   Lane order (left → right): ← ↓ ↑ →  (DDR pad convention)
    const float SD_LANE_W           = 96f;
    const float SD_LANE_H           = 560f;
    const float SD_LANE_SPACING     = 118f;     // center-to-center x distance between lanes
    const float SD_SPAWN_Y          = 260f;     // top of the lanes (notes appear here)
    const float SD_HIT_Y            = -230f;    // bottom of the lanes (hit zone)
    const float SD_HIT_BAR_HEIGHT   = 30f;
    const float SD_RING_LIFE        = 0.28f;    // osu!-style hit ring expand+fade
    const float SD_JUDGE_LIFE       = 0.50f;    // judgment popup float+fade

    static readonly Color SD_HIT_ZONE_COLOR  = new Color(0.85f, 0.55f, 1f, 0.35f);
    static readonly Color SD_LANE_BG         = new Color(0.10f, 0.04f, 0.20f, 0.50f);
    static readonly Color SD_PANEL_BG        = new Color(0.06f, 0.02f, 0.14f, 0.55f);
    static readonly Color SD_READY_PULSE_A   = new Color(0.85f, 0.55f, 1.10f, 1f);
    static readonly Color SD_READY_PULSE_B   = new Color(1.00f, 0.85f, 1.00f, 1f);
    static readonly Color SD_LIFE_ALIVE      = new Color(0.85f, 0.45f, 1.10f, 1f);
    static readonly Color SD_LIFE_LOST       = new Color(0.20f, 0.10f, 0.30f, 0.7f);
    static readonly Color SD_LIFE_X_COLOR    = new Color(1f,    0.30f, 0.35f, 1f);
    static readonly Color SD_MISS_RED        = new Color(1f,    0.30f, 0.35f, 1f);

    static readonly KeyCode[] SD_LANE_ORDER =
    {
        KeyCode.LeftArrow, KeyCode.DownArrow, KeyCode.UpArrow, KeyCode.RightArrow,
    };

    // Arrow colors match the Xbox face-button mapping so it's instantly readable:
    //   ↑ = Y (yellow)    ↓ = A (green)    ← = X (blue)    → = B (red)
    static readonly System.Collections.Generic.Dictionary<KeyCode, (string glyph, Color color)> SD_NOTE_STYLE
        = new System.Collections.Generic.Dictionary<KeyCode, (string, Color)>
        {
            { KeyCode.LeftArrow,  ("←", new Color(0.20f, 0.55f, 1.00f, 1f)) },   // X — blue
            { KeyCode.DownArrow,  ("↓", new Color(0.30f, 0.85f, 0.35f, 1f)) },   // A — green
            { KeyCode.UpArrow,    ("↑", new Color(1.00f, 0.85f, 0.10f, 1f)) },   // Y — yellow
            { KeyCode.RightArrow, ("→", new Color(1.00f, 0.30f, 0.30f, 1f)) },   // B — red
        };

    static float SDLaneX(KeyCode key)
    {
        for (int i = 0; i < SD_LANE_ORDER.Length; i++)
            if (SD_LANE_ORDER[i] == key)
                return (i - (SD_LANE_ORDER.Length - 1) * 0.5f) * SD_LANE_SPACING;
        return 0f;
    }

    void BuildSpatialDriftUI()
    {
        // ── Full-screen purple wash (renders behind everything else) ─────────
        sdScreenTint = MakeImage("SDScreenTint", canvas.transform,
                                  new Color(0.45f, 0.20f, 0.85f, 0f));
        Stretch(sdScreenTint.rectTransform);
        sdScreenTint.rectTransform.SetSiblingIndex(0);     // first child = back of canvas

        // ── "PRESS E" ready prompt (visible when CanEnter) ────────────────────
        sdReadyPanel = MakeRect("SDReadyPanel", canvas.transform);
        SetAnchor(sdReadyPanel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
        sdReadyPanel.anchoredPosition = new Vector2(0, -185);
        sdReadyPanel.sizeDelta        = new Vector2(520, 44);

        var readyBg = MakeImage("Bg", sdReadyPanel, new Color(0.10f, 0.03f, 0.20f, 0.75f));
        Stretch(readyBg.rectTransform);

        sdReadyLabel = MakeText("Label", sdReadyPanel, 22,
                                 SD_READY_PULSE_A, FontStyles.Bold);
        Stretch(sdReadyLabel.rectTransform);
        sdReadyLabel.alignment = TextAlignmentOptions.Center;
        sdReadyLabel.text      = "PRESS  E  /  RB  TO  ENTER  SPATIAL  DRIFT";

        sdReadyPanel.gameObject.SetActive(false);

        // ── Main rhythm panel ────────────────────────────────────────────────
        float totalLanesWidth = SD_LANE_SPACING * (SD_LANE_ORDER.Length - 1) + SD_LANE_W;
        float panelW = totalLanesWidth + 120;
        float panelH = SD_LANE_H + 200;

        // Docked to the RIGHT edge so the track view stays clear — osu! plays
        // big and off to the side, not floating over the road.
        sdPanel = MakeRect("SDPanel", canvas.transform);
        SetAnchor(sdPanel, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
        sdPanel.anchoredPosition = new Vector2(-36f, 0f);
        sdPanel.sizeDelta        = new Vector2(panelW, panelH);
        sdPanelFullSize          = sdPanel.sizeDelta;

        var panelBg = MakeImage("Bg", sdPanel, SD_PANEL_BG);
        Stretch(panelBg.rectTransform);

        // ── 3 life icons across the top of the panel ─────────────────────────
        const float LIFE_SIZE    = 36f;
        const float LIFE_SPACING = 56f;
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * LIFE_SPACING;
            var icon = MakeImage($"Life{i}", sdPanel, SD_LIFE_ALIVE, CircleSprite);
            SetAnchor(icon.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            icon.rectTransform.anchoredPosition = new Vector2(x, -28);
            icon.rectTransform.sizeDelta        = new Vector2(LIFE_SIZE, LIFE_SIZE);
            sdLifeIcons[i] = icon;

            var xMark = MakeText($"LifeX{i}", sdPanel, 38, SD_LIFE_X_COLOR, FontStyles.Bold);
            SetAnchor(xMark.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            xMark.rectTransform.anchoredPosition = new Vector2(x, -28);
            xMark.rectTransform.sizeDelta        = new Vector2(LIFE_SIZE, LIFE_SIZE);
            xMark.alignment = TextAlignmentOptions.Center;
            xMark.text      = "✕";
            xMark.gameObject.SetActive(false);
            sdLifeXs[i] = xMark;
        }

        // ── Score (top-right of panel) ───────────────────────────────────────
        sdScoreText = MakeText("Score", sdPanel, 22, Color.white, FontStyles.Bold);
        SetAnchor(sdScoreText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1));
        sdScoreText.rectTransform.anchoredPosition = new Vector2(-30, -40);
        sdScoreText.rectTransform.sizeDelta        = new Vector2(280, 30);
        sdScoreText.alignment = TextAlignmentOptions.MidlineRight;
        sdScoreText.text      = "SCORE  0";

        // ── 4 vertical lane backgrounds ──────────────────────────────────────
        sdLanesContainer = MakeRect("Lanes", sdPanel);
        SetAnchor(sdLanesContainer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        sdLanesContainer.anchoredPosition = new Vector2(0, -10);
        sdLanesContainer.sizeDelta        = new Vector2(totalLanesWidth, SD_LANE_H);

        for (int i = 0; i < SD_LANE_ORDER.Length; i++)
        {
            float x = (i - (SD_LANE_ORDER.Length - 1) * 0.5f) * SD_LANE_SPACING;
            var laneBg = MakeImage($"Lane_{SD_LANE_ORDER[i]}", sdLanesContainer, SD_LANE_BG);
            SetAnchor(laneBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            laneBg.rectTransform.anchoredPosition = new Vector2(x, 0);
            laneBg.rectTransform.sizeDelta        = new Vector2(SD_LANE_W, SD_LANE_H);

            // Permanent column label below the hit zone — reminds the player
            // which arrow belongs to which lane.
            var (glyph, color) = SD_NOTE_STYLE[SD_LANE_ORDER[i]];
            var label = MakeText($"Label_{SD_LANE_ORDER[i]}", sdLanesContainer, 42,
                                  new Color(color.r, color.g, color.b, 0.85f), FontStyles.Bold);
            SetAnchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            label.rectTransform.anchoredPosition = new Vector2(x, SD_HIT_Y - 56);
            label.rectTransform.sizeDelta        = new Vector2(SD_LANE_W, 48);
            label.alignment = TextAlignmentOptions.Center;
            label.text      = glyph;
        }

        // ── Hit zone bar (horizontal across all lanes near the bottom) ───────
        sdHitZoneBar = MakeRect("HitZone", sdLanesContainer);
        SetAnchor(sdHitZoneBar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        sdHitZoneBar.anchoredPosition = new Vector2(0, SD_HIT_Y);
        sdHitZoneBar.sizeDelta        = new Vector2(totalLanesWidth, SD_HIT_BAR_HEIGHT);
        sdHitZoneFlash = MakeImage("Flash", sdHitZoneBar, SD_HIT_ZONE_COLOR);
        Stretch(sdHitZoneFlash.rectTransform);

        // If the HUD is being rebuilt (e.g. canvas was destroyed and remade in
        // a multiplayer scene transition), the pools still hold references to
        // the DESTROYED objects from the previous build — clear them so we don't
        // Peek/Dequeue a dead TextMeshProUGUI.
        sdHeadPool.Clear();        sdTailPool.Clear();
        sdParticlePool.Clear();    sdRingPool.Clear();    sdJudgePool.Clear();
        sdActiveParticles.Clear(); sdActiveRings.Clear(); sdActiveJudges.Clear();
        sdNoteHeads.Clear();       sdNoteTails.Clear();

        // ── Warm the note pools — pre-allocate everything we'll ever need so
        //    runtime spawns are just toggle-and-reposition, no allocations.
        for (int i = 0; i < SD_POOL_SIZE; i++)
        {
            var head = MakeText("SDHead", sdLanesContainer, 72, Color.white, FontStyles.Bold);
            head.alignment      = TextAlignmentOptions.Center;
            head.rectTransform.sizeDelta = new Vector2(SD_LANE_W, SD_LANE_W);
            head.gameObject.SetActive(false);
            sdHeadPool.Enqueue(head);

            var tail = MakeImage("SDTail", sdLanesContainer, Color.white);
            tail.rectTransform.SetSiblingIndex(0);   // render behind heads/labels
            tail.gameObject.SetActive(false);
            sdTailPool.Enqueue(tail);
        }

        // Particle pool — small circle images for hit bursts / hold streams.
        for (int i = 0; i < SD_PARTICLE_POOL_SIZE; i++)
            sdParticlePool.Enqueue(BuildParticle());

        // Hit-ring + judgment pools (osu!-style feedback).
        for (int i = 0; i < 10; i++)
        {
            var ring = MakeImage("SDRing", sdLanesContainer, Color.white, RingSprite);
            SetAnchor(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ring.rectTransform.sizeDelta = new Vector2(SD_LANE_W * 1.15f, SD_LANE_W * 1.15f);
            ring.gameObject.SetActive(false);
            sdRingPool.Enqueue(new SDRingFx { ring = ring });

            var judge = MakeText("SDJudge", sdLanesContainer, 30, Color.white, FontStyles.Bold);
            judge.alignment = TextAlignmentOptions.Center;
            SetAnchor(judge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            judge.rectTransform.sizeDelta = new Vector2(220, 40);
            judge.gameObject.SetActive(false);
            sdJudgePool.Enqueue(new SDJudgeFx { text = judge });
        }

        // Prime TMP's dynamic atlas with every glyph we'll ever show in the
        // lane (arrows + miss X).  Without this, the FIRST occurrence of each
        // glyph mid-game causes a one-frame TMP rebuild hitch — most visible
        // on the first miss showing the ✕.  ForceMeshUpdate triggers the
        // atlas insert without rendering it.
        if (sdHeadPool.Count > 0)
        {
            var primer = sdHeadPool.Peek();
            primer.gameObject.SetActive(true);
            foreach (var g in new[] { "←", "↓", "↑", "→", "✕" })
            {
                primer.text = g;
                primer.ForceMeshUpdate();
            }
            primer.text = "";
            primer.gameObject.SetActive(false);
        }

        sdPanel.gameObject.SetActive(false);
    }

    SDParticle BuildParticle()
    {
        var img = MakeImage("SDPart", sdLanesContainer, Color.white, CircleSprite);
        SetAnchor(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        img.rectTransform.sizeDelta = new Vector2(10, 10);
        img.gameObject.SetActive(false);
        return new SDParticle { rt = img.rectTransform, image = img };
    }

    void EmitHitBurst(KeyCode lane, Color color, int count)
    {
        float laneX = SDLaneX(lane);
        for (int i = 0; i < count; i++)
        {
            var p = sdParticlePool.Count > 0 ? sdParticlePool.Dequeue() : BuildParticle();
            p.pos        = new Vector2(laneX, SD_HIT_Y);
            p.vel        = new Vector2(UnityEngine.Random.Range(-55f, 55f),
                                       UnityEngine.Random.Range(160f, 230f));
            p.life       = 0f;
            p.maxLife    = 0.60f;
            p.baseColor  = color;
            p.image.color = color;
            p.rt.anchoredPosition = p.pos;
            p.rt.gameObject.SetActive(true);
            sdActiveParticles.Add(p);
        }
    }

    void UpdateParticles(float dt)
    {
        const float gravity = 600f;
        for (int i = sdActiveParticles.Count - 1; i >= 0; i--)
        {
            var p = sdActiveParticles[i];
            p.life += dt;
            if (p.life >= p.maxLife)
            {
                p.rt.gameObject.SetActive(false);
                sdParticlePool.Enqueue(p);
                sdActiveParticles.RemoveAt(i);
                continue;
            }
            p.vel.y -= gravity * dt;
            p.pos   += p.vel * dt;
            p.rt.anchoredPosition = p.pos;
            float a = 1f - p.life / p.maxLife;
            var c   = p.baseColor; c.a = a;
            p.image.color = c;
        }
    }

    void ReturnAllParticlesToPool()
    {
        for (int i = 0; i < sdActiveParticles.Count; i++)
        {
            var p = sdActiveParticles[i];
            p.rt.gameObject.SetActive(false);
            sdParticlePool.Enqueue(p);
        }
        sdActiveParticles.Clear();
    }

    // ── Spatial Drift event handlers ──────────────────────────────────────────
    void HandleSDActivate()
    {
        if (sdReadyPanel != null) sdReadyPanel.gameObject.SetActive(false);
        if (sdPanel      != null) sdPanel.gameObject.SetActive(true);
        // Rails variant plays on the TRACK (dodge the obstacles), so the note
        // lanes stay hidden — the panel just shows lives / score / time.
        bool rails = sdMode != null && sdMode.RailsVariant;
        if (sdLanesContainer != null) sdLanesContainer.gameObject.SetActive(!rails);
        // Compact strip (lives + score + time) in rails; full playfield in rhythm.
        if (sdPanel != null && sdPanelFullSize != Vector2.zero)
            sdPanel.sizeDelta = rails ? new Vector2(sdPanelFullSize.x, 132f) : sdPanelFullSize;
        // Hide the shifter while in ghost mode — arrows are now minigame input.
        if (gridRT != null) gridRT.parent.gameObject.SetActive(false);
        sdLastScoreShown = int.MinValue;     // force first-frame score paint

        // Fade in the purple screen tint.
        if (sdMode != null && sdMode.tuning != null)
        {
            sdScreenTintTarget = sdMode.tuning.screenTintAlpha;
            if (sdScreenTint != null)
            {
                var t = sdMode.tuning.screenTintColor;
                t.a = sdScreenTintCurrent;
                sdScreenTint.color = t;
            }
        }

        // Reset all 3 life icons to alive state.
        for (int i = 0; i < 3; i++)
        {
            if (sdLifeIcons[i] != null) sdLifeIcons[i].color = SD_LIFE_ALIVE;
            if (sdLifeXs[i]    != null) sdLifeXs[i].gameObject.SetActive(false);
        }

        if (flashText != null)
        {
            flashText.text = "SPATIAL  DRIFT";
            flashColor     = new Color(0.75f, 0.45f, 1.1f, 1f);   // purple
            flashTimer     = 1.4f;
        }
    }

    void HandleSDExit(int finalScore)
    {
        if (sdPanel != null) sdPanel.gameObject.SetActive(false);
        if (gridRT  != null) gridRT.parent.gameObject.SetActive(true);

        // Return all active note UI to the pools — no Destroy calls.
        foreach (var kv in sdNoteHeads) ReturnHeadToPool(kv.Value);
        foreach (var kv in sdNoteTails) ReturnTailToPool(kv.Value);
        sdNoteHeads.Clear();
        sdNoteTails.Clear();
        ReturnAllParticlesToPool();

        // Return any in-flight hit fx to their pools.
        for (int i = 0; i < sdActiveRings.Count; i++)
        {
            sdActiveRings[i].ring.gameObject.SetActive(false);
            sdRingPool.Enqueue(sdActiveRings[i]);
        }
        sdActiveRings.Clear();
        for (int i = 0; i < sdActiveJudges.Count; i++)
        {
            sdActiveJudges[i].text.gameObject.SetActive(false);
            sdJudgePool.Enqueue(sdActiveJudges[i]);
        }
        sdActiveJudges.Clear();

        // Fade out the purple wash.
        sdScreenTintTarget = 0f;

        if (flashText != null)
        {
            flashText.text = $"DRIFT END   {finalScore}";
            flashColor     = new Color(0.85f, 0.55f, 1f, 1f);
            flashTimer     = 1.4f;
        }
    }

    void HandleSDNoteSpawn(SpatialDriftMode.Note note)
    {
        if (sdLanesContainer == null) return;
        var (glyph, color) = SD_NOTE_STYLE.TryGetValue(note.key, out var s) ? s : ("?", Color.white);

        // Hold tail FIRST (renders behind the head).
        if (note.duration > 0f)
        {
            var tail = sdTailPool.Count > 0 ? sdTailPool.Dequeue() : MakeImage("SDTail", sdLanesContainer, Color.white);
            tail.gameObject.SetActive(true);
            tail.color = new Color(color.r, color.g, color.b, 0.45f);
            SetAnchor(tail.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            sdNoteTails[note] = tail;
        }

        // Head
        var head = sdHeadPool.Count > 0 ? sdHeadPool.Dequeue() : MakeText("SDHead", sdLanesContainer, 72, color, FontStyles.Bold);
        head.gameObject.SetActive(true);
        head.text  = glyph;
        head.color = color;
        SetAnchor(head.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        head.rectTransform.sizeDelta        = new Vector2(SD_LANE_W, SD_LANE_W);
        head.rectTransform.anchoredPosition = new Vector2(SDLaneX(note.key), SD_SPAWN_Y);
        sdNoteHeads[note] = head;
    }

    void ReturnHeadToPool(TextMeshProUGUI head)
    {
        if (head == null) return;
        head.gameObject.SetActive(false);
        sdHeadPool.Enqueue(head);
    }

    void ReturnTailToPool(Image tail)
    {
        if (tail == null) return;
        tail.gameObject.SetActive(false);
        sdTailPool.Enqueue(tail);
    }

    void HandleSDHit(int hit, KeyCode lane)
    {
        // 0 = miss, 1 = good, 2 = perfect.  Flash the hit zone + emit particles.
        if (sdHitZoneFlash != null)
        {
            sdHitFlashTimer = 0.15f;
            sdHitZoneFlash.color = hit switch
            {
                2 => new Color(0.55f, 1.0f, 0.85f, 0.9f),   // perfect — cyan-green
                1 => new Color(0.95f, 0.85f, 1.0f, 0.85f),  // good — pale lavender
                _ => new Color(1.0f,  0.30f, 0.40f, 0.9f),  // miss/bad — magenta-red
            };
        }

        // Hit particles for taps that landed (not for misses / bad presses).
        if (hit > 0 && SD_NOTE_STYLE.TryGetValue(lane, out var s))
        {
            int count = hit == 2 ? 4 : 3;
            // Brighten perfect-hit particles a touch.
            Color c = hit == 2
                ? Color.Lerp(s.color, Color.white, 0.30f)
                : s.color;
            EmitHitBurst(lane, c, count);
        }

        // osu!-style ring + judgment popup at the hit point (all tiers).
        SpawnHitFx(hit, lane);
    }

    void SpawnHitFx(int hit, KeyCode lane)
    {
        Vector2 pos = new Vector2(SDLaneX(lane), SD_HIT_Y);
        Color  c;
        string word;
        switch (hit)
        {
            case 2:  c = new Color(0.45f, 1f,    0.75f, 1f); word = "PERFECT"; break;
            case 1:  c = new Color(0.92f, 0.85f, 1f,    1f); word = "GOOD";    break;
            default: c = new Color(1f,    0.32f, 0.38f, 1f); word = "MISS";    break;
        }

        if (sdRingPool.Count > 0)
        {
            var fx = sdRingPool.Dequeue();
            fx.age = 0f;
            fx.ring.color = c;
            fx.ring.rectTransform.anchoredPosition = pos;
            fx.ring.rectTransform.localScale       = Vector3.one * 0.7f;
            fx.ring.gameObject.SetActive(true);
            sdActiveRings.Add(fx);
        }
        if (sdJudgePool.Count > 0)
        {
            var fx = sdJudgePool.Dequeue();
            fx.age     = 0f;
            fx.basePos = pos + new Vector2(0f, 72f);
            fx.text.text  = word;
            fx.text.color = c;
            fx.text.rectTransform.anchoredPosition = fx.basePos;
            fx.text.rectTransform.localScale       = Vector3.one * 1.25f;
            fx.text.gameObject.SetActive(true);
            sdActiveJudges.Add(fx);
        }
    }

    void UpdateHitFx(float dt)
    {
        for (int i = sdActiveRings.Count - 1; i >= 0; i--)
        {
            var fx = sdActiveRings[i];
            fx.age += dt;
            float t = fx.age / SD_RING_LIFE;
            if (t >= 1f)
            {
                fx.ring.gameObject.SetActive(false);
                sdRingPool.Enqueue(fx);
                sdActiveRings.RemoveAt(i);
                continue;
            }
            float ease = 1f - (1f - t) * (1f - t);            // ease-out expand
            fx.ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.7f, 2.4f, ease);
            var c = fx.ring.color; c.a = (1f - t) * 0.95f; fx.ring.color = c;
        }

        for (int i = sdActiveJudges.Count - 1; i >= 0; i--)
        {
            var fx = sdActiveJudges[i];
            fx.age += dt;
            float t = fx.age / SD_JUDGE_LIFE;
            if (t >= 1f)
            {
                fx.text.gameObject.SetActive(false);
                sdJudgePool.Enqueue(fx);
                sdActiveJudges.RemoveAt(i);
                continue;
            }
            float rise = 1f - (1f - t) * (1f - t);
            fx.text.rectTransform.anchoredPosition = fx.basePos + new Vector2(0f, 38f * rise);
            // quick settle from the 1.25× punch, then fade out in the back 60%.
            fx.text.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, Mathf.Min(1f, t * 4f));
            var c = fx.text.color;
            c.a = t < 0.4f ? 1f : 1f - (t - 0.4f) / 0.6f;
            fx.text.color = c;
        }
    }

    void HandleSDNoteMissed(SpatialDriftMode.Note note)
    {
        // Repurpose the note's head as a red ✕ that scrolls and fades out.  The
        // existing per-frame update keeps moving it down using its hitTime, and
        // the cleanup path returns it to the pool when it goes offscreen.
        if (sdNoteHeads.TryGetValue(note, out var head) && head != null)
        {
            head.text  = "✕";
            head.color = SD_MISS_RED;
        }
        // Also fade the tail if it's a missed hold.
        if (sdNoteTails.TryGetValue(note, out var tail) && tail != null)
        {
            var c = SD_MISS_RED; c.a = 0.35f;
            tail.color = c;
        }
    }

    void HandleSDLifeLost(int remaining)
    {
        // Mark the icon at index = remaining (rightmost first to go).
        int lostIdx = Mathf.Clamp(remaining, 0, 2);
        if (sdLifeIcons[lostIdx] != null) sdLifeIcons[lostIdx].color = SD_LIFE_LOST;
        if (sdLifeXs[lostIdx]    != null) sdLifeXs[lostIdx].gameObject.SetActive(true);
    }

    // ── Spatial Drift per-frame update ────────────────────────────────────────
    void UpdateSpatialDriftUI()
    {
        if (sdMode == null) return;

        // Screen tint always animates so it can fade out on exit.
        if (sdScreenTint != null)
        {
            sdScreenTintCurrent = Mathf.MoveTowards(sdScreenTintCurrent, sdScreenTintTarget,
                                                     1.2f * Time.deltaTime);
            if (Mathf.Abs(sdScreenTint.color.a - sdScreenTintCurrent) > 0.001f)
            {
                var tc = sdScreenTint.color;
                tc.a   = sdScreenTintCurrent;
                sdScreenTint.color = tc;
            }
        }

        bool active = sdMode.CurrentPhase == SpatialDriftMode.Phase.Active;

        // Hit-zone flash decay (active or not, so post-exit color settles)
        if (sdHitZoneFlash != null && sdHitFlashTimer > 0f)
        {
            sdHitFlashTimer = Mathf.Max(0f, sdHitFlashTimer - Time.deltaTime);
            if (sdHitFlashTimer <= 0f) sdHitZoneFlash.color = SD_HIT_ZONE_COLOR;
        }

        if (!active)
        {
            // Ready prompt when the player CAN enter; recharge countdown while
            // the mode is cooling down (so the silence reads as "not yet", not
            // "broken").
            bool  ready    = sdMode.CanEnter;
            float recharge = sdMode.CooldownRemaining;
            bool  cooling  = !ready && recharge > 0.05f;
            bool  show     = ready || cooling;

            if (sdReadyPanel != null && sdReadyPanel.gameObject.activeSelf != show)
                sdReadyPanel.gameObject.SetActive(show);

            if (sdReadyLabel != null)
            {
                if (cooling)
                {
                    int tenths = Mathf.CeilToInt(recharge * 10f);
                    if (tenths != sdLastRechargeTenths)
                    {
                        sdLastRechargeTenths = tenths;
                        sdReadyLabel.text = $"DRIFT  RECHARGING   {tenths / 10f:0.0}s";
                    }
                    sdReadyLabel.color = new Color(0.55f, 0.45f, 0.75f, 0.9f);   // dim purple
                }
                else if (ready)
                {
                    if (sdLastRechargeTenths != int.MinValue)
                    {
                        sdLastRechargeTenths = int.MinValue;
                        sdReadyLabel.text = "PRESS  E  /  RB  TO  ENTER  SPATIAL  DRIFT";
                    }
                    // Pulse between two purples on a sine — eye-catching.
                    float t = Mathf.PingPong(Time.time * 1.6f, 1f);
                    sdReadyLabel.color = Color.Lerp(SD_READY_PULSE_A, SD_READY_PULSE_B, t);
                }
            }
            return;
        }

        // Active: hide ready prompt if it's somehow up
        if (sdReadyPanel != null && sdReadyPanel.gameObject.activeSelf)
            sdReadyPanel.gameObject.SetActive(false);

        // Score + remaining mode time — only re-set when a visible value changed
        // (avoids per-frame string allocation + TMP mesh rebuild).
        if (sdScoreText != null)
        {
            float remain = sdMode.ActiveRemaining;
            int   tenths = remain >= 0f ? Mathf.CeilToInt(remain * 10f) : -1;
            if (sdMode.Score != sdLastScoreShown || tenths != sdLastTimeTenths)
            {
                sdLastScoreShown = sdMode.Score;
                sdLastTimeTenths = tenths;
                sdScoreText.text = tenths >= 0
                    ? $"SCORE  {sdMode.Score}    ·    {tenths / 10f:0.0}s"
                    : "SCORE  " + sdMode.Score;
            }
        }

        // Hold streams — emit a trickle of particles for any hold being held.
        var liveNotes = sdMode.Notes;
        for (int i = 0; i < liveNotes.Count; i++)
        {
            var n = liveNotes[i];
            if (n.duration <= 0f || !n.consumed || n.holdComplete) continue;
            if (UnityEngine.Random.value < 0.5f)   // ~30/sec at 60fps
            {
                var (_, color) = SD_NOTE_STYLE[n.key];
                EmitHitBurst(n.key, color, 1);
            }
        }

        // Particle physics + fade, then ring/judgment animation.
        UpdateParticles(Time.deltaTime);
        UpdateHitFx(Time.deltaTime);

        // Update each head + optional tail.  Uses per-note leadTime so the
        // visual speed of an in-flight note never jumps when difficulty ramps.
        sdStaleNotes.Clear();
        foreach (var kv in sdNoteHeads)
        {
            var n    = kv.Key;
            var head = kv.Value;
            if (head == null) { sdStaleNotes.Add(n); continue; }

            float scrollSpeed = (SD_SPAWN_Y - SD_HIT_Y) / Mathf.Max(0.01f, n.leadTime);
            float headY       = SD_HIT_Y + (n.hitTime - Time.time) * scrollSpeed;
            float laneX       = SDLaneX(n.key);

            head.rectTransform.anchoredPosition = new Vector2(laneX, headY);

            // Tail: extends UP from head by (duration * scrollSpeed).  Bottom of
            // the tail tracks the head's Y exactly; top stays a fixed offset above.
            if (n.duration > 0f && sdNoteTails.TryGetValue(n, out var tail) && tail != null)
            {
                float tailHeight  = n.duration * scrollSpeed;
                float tailCenterY = headY + tailHeight * 0.5f;
                tail.rectTransform.sizeDelta        = new Vector2(SD_LANE_W * 0.55f, tailHeight);
                tail.rectTransform.anchoredPosition = new Vector2(laneX, tailCenterY);

                // Fade the tail when the hold is finished (success OR release).
                if (n.holdComplete)
                {
                    var c = tail.color; c.a = 0.15f; tail.color = c;
                }
            }

            // Consumed head fades.  For holds the head fades immediately on catch
            // so the player can see they got the head; the tail signals the rest.
            if (n.consumed)
            {
                var c = head.color; c.a = 0.30f; head.color = c;
            }

            // Cleanup rules differ for hit-then-fading vs missed (X-marked):
            float cleanupY = SD_HIT_Y - 100f;
            bool readyToCleanup;
            if (n.consumed)
            {
                // hit (tap) or hold: wait until below + (for holds) tail end + brief linger
                readyToCleanup = headY < cleanupY
                    && (n.duration <= 0f || n.holdComplete || Time.time > n.hitTime + n.duration + 0.3f);
            }
            else
            {
                // missed — HandleSDNoteMissed already swapped the glyph to ✕.
                // Drop after a short window so the X is briefly visible past the bar.
                readyToCleanup = Time.time > n.hitTime + 0.55f;
            }
            if (readyToCleanup) sdStaleNotes.Add(n);
        }

        for (int i = 0; i < sdStaleNotes.Count; i++)
        {
            var n = sdStaleNotes[i];
            if (sdNoteHeads.TryGetValue(n, out var h)) { ReturnHeadToPool(h); sdNoteHeads.Remove(n); }
            if (sdNoteTails.TryGetValue(n, out var t)) { ReturnTailToPool(t); sdNoteTails.Remove(n); }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  UI element factory helpers
    // ════════════════════════════════════════════════════════════════════════════
    static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Image MakeImage(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var rt  = MakeRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color  = color;
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI MakeText(string name, Transform parent, int fontSize, Color color, FontStyles style)
    {
        var rt   = MakeRect(name, parent);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.font                = Font_;
        text.fontSize            = fontSize;
        text.fontStyle           = style;
        text.color               = color;
        text.alignment           = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping  = false;
        text.overflowMode        = TextOverflowModes.Overflow;
        text.raycastTarget       = false;
        return text;
    }

    static void SetAnchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
    }

    /// Stretch a child to fill its parent.
    static void Stretch(RectTransform rt)
    {
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.offsetMin        = Vector2.zero;
        rt.offsetMax        = Vector2.zero;
    }

    /// Anchor a child to the top of its parent, with a Y offset (px down from top)
    /// and an explicit height.  Horizontal stretch.
    static void StretchTop(RectTransform rt, float yOffsetFromTop, float height)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -yOffsetFromTop);
        rt.sizeDelta        = new Vector2(0, height);
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Procedural sprite generation (cached)
    // ════════════════════════════════════════════════════════════════════════════
    static Sprite MakeCircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.filterMode = FilterMode.Bilinear;

        var px = new Color32[size * size];
        float r  = size * 0.5f - 1f;
        float cx = size * 0.5f, cy = size * 0.5f;
        float aa = 1.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            float a  = Mathf.Clamp01((r - d) / aa);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);

        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    static Sprite MakeRingSprite(int size, float innerFrac)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags  = HideFlags.HideAndDontSave;
        tex.filterMode = FilterMode.Bilinear;

        var px = new Color32[size * size];
        float outerR = size * 0.5f - 1f;
        float innerR = outerR * innerFrac;
        float cx = size * 0.5f, cy = size * 0.5f;
        float aa = 1.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            float oa = Mathf.Clamp01((outerR - d) / aa);
            float ia = Mathf.Clamp01((d - innerR) / aa);
            float a  = Mathf.Min(oa, ia);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);

        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}
