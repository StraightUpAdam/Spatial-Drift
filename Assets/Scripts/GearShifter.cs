using UnityEngine;

/// Grid-based shifter.  Arrow keys move a "stick" cell-by-cell across the grid.
/// Landing on a Gear cell engages that gear.  Landing on an Empty cell = neutral.
/// Walls between cells (wallsH / wallsV) shape the layout — the default is a
/// classic H-gate, but any layout (maze, sequential, weird shapes) just needs
/// different cell + wall data.
public class GearShifter : MonoBehaviour
{
    public enum CellKind { Empty, Gear, Wall }

    [System.Serializable]
    public struct GridCell
    {
        public CellKind kind;
        public int      gear;   // 1–10 if kind == Gear, otherwise ignored
    }

    [Header("Grid Size")]
    public int width  = 5;
    public int height = 3;

    [Header("Cells (row-major, top→bottom, left→right)")]
    [SerializeField] GridCell[] cells;

    [Header("Walls")]
    [Tooltip("Horizontal edges, length = (width-1) * height. true = blocked")]
    [SerializeField] bool[] wallsH;
    [Tooltip("Vertical edges, length = width * (height-1). true = blocked")]
    [SerializeField] bool[] wallsV;

    [Header("Start Cell (col, row)")]
    public Vector2Int startPosition = new Vector2Int(0, 0);

    [Header("References")]
    public GearSystem gearSystem;

    [Header("Skip Kickback")]
    [Tooltip("Seconds the ball lingers on the invalid gear before bouncing back to the previous valid gear.")]
    [Range(0.05f, 0.6f)] public float kickbackDuration = 0.25f;

    [Header("Right-Stick Shifter (Gamepad)")]
    [Tooltip("Magnitude on the right stick to register a flick as a 'press'.")]
    [Range(0.3f, 0.95f)] public float stickFlickThreshold   = 0.55f;
    [Tooltip("Magnitude the stick must drop below before another flick can register.")]
    [Range(0.05f, 0.6f)] public float stickReleaseThreshold = 0.30f;

    // ── State ─────────────────────────────────────────────────────────────────
    public Vector2Int Position    { get; private set; }
    public int        Width       => width;
    public int        Height      => height;
    public GridCell[] Cells       => cells;
    public bool[]     WallsH      => wallsH;
    public bool[]     WallsV      => wallsV;

    CarController _car;
    bool ArrowsLocked => _car != null && _car.spatialDriftActive;

    /// Fired when the player tries to land on a gear cell that would skip a
    /// gear (e.g. 1 → 3 directly).  Args: (attempted gear, last valid gear).
    public event System.Action<int, int> OnSkipRejected;

    /// Fired when the game automatically moves the shifter to a lower gear
    /// because speed dropped out of the current gear's range.
    public event System.Action<int> OnAutoShift;

    /// Fired whenever the ball moves to a new cell from player input — gear cell
    /// OR empty cell.  Audio uses this to click on every move, not just on gear
    /// engagement.  Arg = true if the landing cell is a gear cell.
    public event System.Action<bool> OnMove;

    /// RPM captured the moment we left the most recent gear cell.  Used to
    /// determine the boost zone when we arrive at the *next* gear cell — so
    /// timing your decision to leave the current gear is what matters.
    float lastGearRPM;

    // Kickback state — when the player tries to skip a gear, the ball briefly
    // visits the invalid cell, then bounces back to the previous valid gear.
    float      kickbackTimer;
    Vector2Int kickbackTarget;
    public bool IsKickingBack => kickbackTimer > 0f;

    // Right-stick flick state — track whether the stick is currently "engaged"
    // in each axis so a held push only fires one move per flick.
    bool rsXEngaged;
    bool rsYEngaged;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (cells == null || cells.Length != width * height)
            BuildDefaultHGate();

        // The player's own layout from the Shifter Lab replaces the H-gate.
        ShifterLayout.Load()?.ApplyTo(this);

        _car = GetComponent<CarController>();
        Position = ClampToBounds(startPosition);
    }

    /// Overwrite the whole grid with a custom layout (Shifter Lab).
    public void ApplyCustomLayout(int w, int h, GridCell[] newCells,
                                  bool[] newWallsH, bool[] newWallsV, Vector2Int start)
    {
        width  = w;
        height = h;
        cells  = newCells;
        wallsH = newWallsH;
        wallsV = newWallsV;
        startPosition = start;
        Position      = ClampToBounds(start);
    }

    void Start()
    {
        // Engage starting gear if we're sitting on a gear cell.
        var cell = CellAt(Position);
        if (cell.kind == CellKind.Gear && gearSystem != null)
            gearSystem.SetGear(cell.gear, 0f);
    }

    void Update()
    {
        // While the ball is bouncing back from an invalid skip, lock input
        // and tick the timer.  When it fires, snap to the bounce target and
        // re-engage that gear (no boost).
        if (IsKickingBack)
        {
            kickbackTimer -= Time.deltaTime;
            if (kickbackTimer <= 0f)
            {
                Position = kickbackTarget;
                var landed = CellAt(kickbackTarget);
                if (gearSystem != null && landed.kind == CellKind.Gear)
                    gearSystem.SetGear(landed.gear, 0f);
                OnMove?.Invoke(landed.kind == CellKind.Gear);
            }
            return;
        }

        // AI cars never read player input — AIDriver calls Upshift()/Downshift()
        // directly and auto-downshift uses AutoShiftTo.  Without this gate, one
        // keypress would shift EVERY car in the race.
        if (_car != null && _car.aiControlled) return;

        // Pause menu up (or just closed) — menu input must not shift gears.
        if (GamePause.InputBlocked) return;

        // Bumper paddle shifting (Xbox: RB upshifts, LB downshifts).  These
        // sit BEFORE the arrow-key check so a gamepad press doesn't also try
        // to move the grid ball — they bypass the cell graph entirely.
        if (Input.GetKeyDown(KeyCode.JoystickButton5)) { Upshift();   return; }
        if (Input.GetKeyDown(KeyCode.JoystickButton4)) { Downshift(); return; }

        Vector2Int dir = Vector2Int.zero;
        // Arrow keys are repurposed as rhythm input during Spatial Drift.
        if (!ArrowsLocked)
        {
            if      (Input.GetKeyDown(KeyCode.UpArrow))    dir = new Vector2Int( 0, -1);
            else if (Input.GetKeyDown(KeyCode.DownArrow))  dir = new Vector2Int( 0,  1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow))  dir = new Vector2Int(-1,  0);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) dir = new Vector2Int( 1,  0);
        }

        // Right stick → directional flicks.  Also locked during Spatial Drift.
        if (dir == Vector2Int.zero && !ArrowsLocked)
        {
            float rsx = SafeAxis("PadShiftX");
            float rsy = SafeAxis("PadShiftY");

            if (!rsXEngaged && Mathf.Abs(rsx) > stickFlickThreshold)
            {
                rsXEngaged = true;
                dir.x      = rsx > 0 ? 1 : -1;
            }
            else if (rsXEngaged && Mathf.Abs(rsx) < stickReleaseThreshold)
            {
                rsXEngaged = false;
            }

            if (!rsYEngaged && Mathf.Abs(rsy) > stickFlickThreshold)
            {
                rsYEngaged = true;
                // Cell grid: positive Y = row below.  Stick UP (rsy > 0 after
                // inversion) should map to UP arrow (cell dir Y = -1).
                dir.y      = rsy > 0 ? -1 : 1;
            }
            else if (rsYEngaged && Mathf.Abs(rsy) < stickReleaseThreshold)
            {
                rsYEngaged = false;
            }
        }

        if (dir != Vector2Int.zero) TryMove(dir);
    }

    // Caches unregistered axis names so a missing axis throws only ONCE rather
    // than 60×/sec (an exception storm that would tank the frame rate).
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

    // ── Paddle shifting (gamepad bumpers) ─────────────────────────────────────
    /// Engage the next sequential gear if there is one.  Awards boost if the
    /// current RPM is in a boost zone — same rule as moving up through the grid.
    public void Upshift()
    {
        if (gearSystem == null) return;
        int current = gearSystem.CurrentGear;
        // From neutral, "upshift" engages the last gear we were in + 1 (or 1).
        int reference = current > 0 ? current : gearSystem.LastEngagedGear;
        int target    = reference + 1;
        if (target < 1 || target > GearSystem.GearCount) return;

        float shiftRPM = gearSystem.RPM;     // capture BEFORE engaging the new gear
        Position       = FindGearCell(target);
        gearSystem.SetGear(target, shiftRPM);
        OnMove?.Invoke(true);
    }

    /// Engage the previous sequential gear if there is one.  Never awards boost.
    public void Downshift()
    {
        if (gearSystem == null) return;
        int current = gearSystem.CurrentGear;
        int reference = current > 0 ? current : gearSystem.LastEngagedGear;
        int target    = reference - 1;
        if (target < 1) return;

        Position = FindGearCell(target);
        gearSystem.SetGear(target, 0f);      // downshifts never boost
        OnMove?.Invoke(true);
    }

    // ── Movement ──────────────────────────────────────────────────────────────
    public bool TryMove(Vector2Int dir)
    {
        Vector2Int next = Position + dir;
        if (!InBounds(next)) return false;
        if (HasWallBetween(Position, next)) return false;

        var nextCell = CellAt(next);
        if (nextCell.kind == CellKind.Wall) return false;

        // Skip-check: land briefly on the invalid gear, then bounce back to
        // the previous valid gear.  Player visibly sees their mistake.
        // (Downshifts and same-gear returns are allowed.)
        if (gearSystem != null && nextCell.kind == CellKind.Gear)
        {
            int lastEngaged = gearSystem.LastEngagedGear;
            if (lastEngaged > 0 && nextCell.gear > lastEngaged + 1)
            {
                // Snapshot RPM if we're leaving a gear cell
                if (CellAt(Position).kind == CellKind.Gear)
                    lastGearRPM = gearSystem.RPM;

                // Visit the invalid cell — car drops to neutral during kickback
                Position = next;
                gearSystem.SetGear(0, 0f);

                // Schedule the bounce back to the last valid gear
                kickbackTarget = FindGearCell(lastEngaged);
                kickbackTimer  = kickbackDuration;

                OnMove?.Invoke(true);                 // ball moved onto the (invalid) gear cell
                OnSkipRejected?.Invoke(nextCell.gear, lastEngaged);
                return true;
            }
        }

        // Leaving a gear cell?  Snapshot the RPM — this becomes the "shift RPM"
        // used when (and if) we arrive at the next gear cell.
        if (CellAt(Position).kind == CellKind.Gear)
            lastGearRPM = gearSystem != null ? gearSystem.RPM : 0f;

        Position = next;

        if (gearSystem != null)
        {
            if (nextCell.kind == CellKind.Gear)
                gearSystem.SetGear(nextCell.gear, lastGearRPM);
            else
                gearSystem.SetGear(0, 0f);   // neutral
        }

        OnMove?.Invoke(nextCell.kind == CellKind.Gear);
        return true;
    }

    /// Snap the ball back to the configured start cell and engage that gear
    /// (or neutral, if the start cell isn't a gear cell).  Used by RaceManager
    /// when resetting between races.
    public void ResetToStart()
    {
        kickbackTimer = 0f;
        Position      = ClampToBounds(startPosition);

        if (gearSystem == null) return;

        var cell = CellAt(Position);
        if (cell.kind == CellKind.Gear)
            gearSystem.SetGear(cell.gear, 0f);   // no boost on reset
        else
            gearSystem.SetGear(0, 0f);
    }

    /// Move the stick automatically to a gear cell — used by the auto-downshift
    /// system when speed drops out of the current gear's range.  Bypasses the
    /// skip check (auto-shifts are inherently valid) and passes shiftRPM = 0
    /// so no boost is awarded.
    public void AutoShiftTo(int gear)
    {
        if (gearSystem == null) return;
        if (gear < 1 || gear > GearSystem.GearCount) return;
        if (gear == gearSystem.CurrentGear) return;

        // Find the cell with this gear number
        for (int r = 0; r < height; r++)
        for (int c = 0; c < width;  c++)
        {
            var cell = cells[r * width + c];
            if (cell.kind == CellKind.Gear && cell.gear == gear)
            {
                Position = new Vector2Int(c, r);
                gearSystem.SetGear(gear, 0f);   // no boost on auto-shift
                OnAutoShift?.Invoke(gear);
                return;
            }
        }
    }

    // ── Queries ───────────────────────────────────────────────────────────────
    public GridCell CellAt(Vector2Int p) => cells[p.y * width + p.x];

    public bool InBounds(Vector2Int p) =>
        p.x >= 0 && p.x < width && p.y >= 0 && p.y < height;

    /// True iff a wall exists on the edge between two ADJACENT cells.
    public bool HasWallBetween(Vector2Int a, Vector2Int b)
    {
        if (a.y == b.y && Mathf.Abs(a.x - b.x) == 1)
        {
            int minX = Mathf.Min(a.x, b.x);
            return wallsH[a.y * (width - 1) + minX];
        }
        if (a.x == b.x && Mathf.Abs(a.y - b.y) == 1)
        {
            int minY = Mathf.Min(a.y, b.y);
            return wallsV[minY * width + a.x];
        }
        return true;   // non-adjacent → effectively walled
    }

    Vector2Int ClampToBounds(Vector2Int p) =>
        new Vector2Int(Mathf.Clamp(p.x, 0, width - 1),
                       Mathf.Clamp(p.y, 0, height - 1));

    Vector2Int FindGearCell(int gear)
    {
        for (int r = 0; r < height; r++)
        for (int c = 0; c < width;  c++)
        {
            var cell = cells[r * width + c];
            if (cell.kind == CellKind.Gear && cell.gear == gear)
                return new Vector2Int(c, r);
        }
        return Position;   // fallback — shouldn't happen for valid gears
    }

    // ── Default layout: classic H-gate for 10 gears ───────────────────────────
    //
    //   Row 0:  [1] [3] [5] [7] [9]      ← top row: walls between every column
    //   Row 1:  [ ] [ ] [ ] [ ] [ ]      ← neutral row: no walls (the crossbar)
    //   Row 2:  [2] [4] [6] [8] [10]     ← bottom row: walls between every column
    //
    //   All vertical edges (top↔mid, mid↔bot) are OPEN.
    //
    void BuildDefaultHGate()
    {
        width  = 5;
        height = 3;

        cells = new GridCell[width * height];

        // Top row → odd gears 1, 3, 5, 7, 9
        for (int c = 0; c < width; c++)
            cells[0 * width + c] = new GridCell { kind = CellKind.Gear, gear = c * 2 + 1 };

        // Middle row → neutral
        for (int c = 0; c < width; c++)
            cells[1 * width + c] = new GridCell { kind = CellKind.Empty };

        // Bottom row → even gears 2, 4, 6, 8, 10
        for (int c = 0; c < width; c++)
            cells[2 * width + c] = new GridCell { kind = CellKind.Gear, gear = (c + 1) * 2 };

        // Horizontal walls: top and bottom rows are fully walled off
        wallsH = new bool[(width - 1) * height];
        for (int c = 0; c < width - 1; c++)
        {
            wallsH[0 * (width - 1) + c] = true;   // top row
            wallsH[1 * (width - 1) + c] = false;  // middle row (crossbar)
            wallsH[2 * (width - 1) + c] = true;   // bottom row
        }

        // Vertical walls: all open (false)
        wallsV = new bool[width * (height - 1)];

        startPosition = new Vector2Int(0, 0);   // gear 1
    }
}
