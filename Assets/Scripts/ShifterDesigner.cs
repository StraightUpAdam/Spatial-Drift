using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// SHIFTER LAB — design your own shift gate: nodes joined by LINES (the ball
/// can only travel along a line, like the slots of a real gate) with gears on
/// the nodes.  Two focus zones, swap with LB/RB (Q/E) or just use the mouse:
///
///   GRID     move the cursor (d-pad / stick / arrows / hover)
///            A · Enter · click        TAP: place the next gear, or grab a gear
///                                     and drop it elsewhere (drop on a gear = swap)
///            HOLD A + move · drag     DRAW lines along the way
///            X · Del · right-click    TAP: erase the gear (or this node's lines)
///            HOLD X + move · r-drag   CUT lines along the way
///            1–9, 0                   put that gear right here (keyboard)
///   ACTIONS  TEST DRIVE, PRESET, GRID WIDTH / HEIGHT (1–10 each — wide gates,
///            tall sequential ladders), SAVE, REVERT, RESET TO H-GATE
///
/// Live validation + a gear tray show what's missing; SAVE is only allowed
/// once every gear is placed and connected to gear 1 by lines.
[DisallowMultipleComponent]
public class ShifterDesigner : MonoBehaviour
{
    [Header("Grid (baked)")]
    public RectTransform   gridRoot;
    public ShifterCell     cellTemplate;
    public Image           connectorTemplate;
    public RectTransform   cursor;
    public TextMeshProUGUI cursorHeld;
    public RectTransform   ball;

    [Header("Actions (baked)")]
    public MenuScreen  actions;
    public MenuButton  testButton, saveButton, revertButton, resetButton, backButton;
    public MenuStepper presetRow, widthRow, heightRow;

    [Header("Info (baked)")]
    public Image[]           trayChips;
    public TextMeshProUGUI[] trayLabels;
    public TextMeshProUGUI   statusText, zoneLabel, brushText, testGearText;
    public CanvasGroup       testOverlay;
    public MenuModal         modal;
    public PromptBar         prompts;

    public string menuScene = "MainMenu";

    // Grid metrics — recomputed to FIT the card for any grid size.
    float pitch = 100f, cellSize = 86f;

    enum Zone { Grid, Actions, Test }
    Zone zone = Zone.Grid;

    enum Stroke { None, Draw, Cut }
    Stroke stroke;
    bool   strokeMoved, strokeMouse;

    ShifterLayout layout;
    ShifterCell[] cells;
    readonly List<Image> connectors = new List<Image>();
    Vector2Int cur;
    int  held;                  // gear number being carried (0 = none)
    Vector2Int heldFrom;
    bool dirty;
    float flashUntil;

    // Test drive: a real GearShifter + GearSystem on a hidden object.
    GameObject  testGO;
    GearShifter testShifter;
    GearSystem  testGears;

    // ── Setup ─────────────────────────────────────────────────────────────────
    void Start()
    {
        layout = ShifterLayout.Load() ?? ShifterLayout.Preset(0);

        presetRow.SetOptions(ShifterLayout.PresetNames, 0);
        presetRow.Submitted += () => AskPreset(presetRow.Index);
        var sizes = new List<string>();
        for (int i = ShifterLayout.MinSize; i <= ShifterLayout.MaxSize; i++) sizes.Add(i.ToString());
        if (widthRow != null)  { widthRow.SetOptions(sizes, 0);  widthRow.wrap = false;  widthRow.Changed  += _ => ResizeTo(SizeFrom(widthRow), layout.height); }
        if (heightRow != null) { heightRow.SetOptions(sizes, 0); heightRow.wrap = false; heightRow.Changed += _ => ResizeTo(layout.width, SizeFrom(heightRow)); }

        testButton.Clicked   += BeginTest;
        saveButton.Clicked   += Save;
        revertButton.Clicked += Revert;
        resetButton.Clicked  += AskReset;
        if (backButton != null) backButton.Clicked += AskExit;
        actions.Backed       += AskExit;
        actions.TabLeft      += () => SetZone(Zone.Grid);
        actions.TabRight     += () => SetZone(Zone.Grid);
        actions.InputRequested += () => { if (zone == Zone.Grid) SetZone(Zone.Actions, silent: true); };

        if (testOverlay != null) testOverlay.alpha = 0f;
        if (ball != null) ball.gameObject.SetActive(false);

        cellTemplate.gameObject.SetActive(false);
        connectorTemplate.gameObject.SetActive(false);
        LayoutChanged(resetCursor: true);
        SetZone(Zone.Grid, silent: true);
    }

    static int SizeFrom(MenuStepper s) => ShifterLayout.MinSize + s.Index;

    void OnDestroy() { if (testGO != null) Destroy(testGO); }

    void SetZone(Zone z, bool silent = false)
    {
        if (z != Zone.Grid) EndStroke(apply: false);
        zone = z;
        actions.inputEnabled = z == Zone.Actions;
        if (z == Zone.Actions && !actions.IsOpen) actions.Show();
        if (!silent) MenuAudio.Play(Sfx.Toggle);
        if (zoneLabel != null)
            zoneLabel.text = z == Zone.Grid ? "EDITING  <color=#B06BFF>GATE</color>"
                           : z == Zone.Actions ? "EDITING  <color=#B06BFF>ACTIONS</color>" : "TEST DRIVE";
        UpdatePrompts();
    }

    void UpdatePrompts()
    {
        if (prompts == null) return;
        switch (zone)
        {
            case Zone.Grid:
                prompts.Set((MenuAction.Confirm, held > 0 ? "DROP" : "GEAR · HOLD+MOVE DRAWS"),
                            (MenuAction.Alt1, "ERASE · HOLD+MOVE CUTS"),
                            (MenuAction.TabRight, "ACTIONS"), (MenuAction.Back, "BACK"));
                break;
            case Zone.Actions:
                prompts.Set((MenuAction.Confirm, "SELECT"), (MenuAction.Adjust, "CHANGE"),
                            (MenuAction.TabRight, "GATE"), (MenuAction.Back, "BACK"));
                break;
            default:
                prompts.Set((MenuAction.Back, "STOP TEST"));
                break;
        }
    }

    // ── Presets / resize ──────────────────────────────────────────────────────
    void AskPreset(int i)
    {
        void Load()
        {
            layout = ShifterLayout.Preset(i);
            held = 0; dirty = true;
            LayoutChanged(resetCursor: true);
            Status($"Loaded the {ShifterLayout.PresetNames[i]} preset — SAVE to race on it.", UITheme.Cyan);
        }
        if (modal == null) { Load(); return; }
        modal.Ask($"LOAD {ShifterLayout.PresetNames[i]}?", "Your current gate will be replaced.", "LOAD", "CANCEL", Load);
    }

    void ResizeTo(int w, int h)
    {
        if (w == layout.width && h == layout.height) return;
        if (held > 0) CancelHold();
        if (w * h < GearSystem.GearCount)
        {
            Nope($"{w} × {h} is too small — a gate needs room for all {GearSystem.GearCount} gears.");
            SyncSizeRows();
            return;
        }
        var n = layout.Resized(w, h, out int lost);
        if (lost > 0)
        {
            Nope($"Gear {lost} would fall off the edge — move it in first.");
            SyncSizeRows();
            return;
        }
        layout = n;
        dirty = true;
        MenuAudio.Play(Sfx.Adjust);
        LayoutChanged(resetCursor: false);
    }

    void SyncSizeRows()
    {
        widthRow?.SetIndex(layout.width - ShifterLayout.MinSize);
        heightRow?.SetIndex(layout.height - ShifterLayout.MinSize);
    }

    /// The layout object or its size changed: rebuild the grid to fit.
    void LayoutChanged(bool resetCursor)
    {
        EndStroke(apply: false);
        SyncSizeRows();
        BuildGrid();
        cur = resetCursor ? FindGear(1) ?? new Vector2Int(layout.width / 2, layout.height / 2)
                          : new Vector2Int(Mathf.Clamp(cur.x, 0, layout.width - 1), Mathf.Clamp(cur.y, 0, layout.height - 1));
        if (cursor != null) cursor.anchoredPosition = CellPos(cur.x, cur.y);
        RefreshAll();
        UpdatePrompts();
    }

    Vector2Int? FindGear(int g)
    {
        for (int r = 0; r < layout.height; r++)
        for (int c = 0; c < layout.width; c++)
            if (layout.Cell(c, r) == g) return new Vector2Int(c, r);
        return null;
    }

    // ── Grid build ────────────────────────────────────────────────────────────
    void BuildGrid()
    {
        if (cells != null) foreach (var c in cells) if (c != null) Destroy(c.gameObject);

        // Fit the grid into the card (header above, test strip below).
        var area  = gridRoot.parent as RectTransform;
        Vector2 avail = area != null ? area.rect.size - new Vector2(70f, 150f) : new Vector2(810f, 530f);
        pitch    = Mathf.Min(100f, avail.x / layout.width, avail.y / layout.height);
        cellSize = pitch * (pitch >= 80f ? 0.80f : 0.74f);

        int w = layout.width, h = layout.height;
        cells = new ShifterCell[w * h];
        for (int r = 0; r < h; r++)
        for (int c = 0; c < w; c++)
        {
            var cell = Instantiate(cellTemplate, gridRoot);
            cell.gameObject.SetActive(true);
            cell.name  = $"Cell_{c}_{r}";
            cell.owner = this;
            cell.pos   = new Vector2Int(c, r);
            ((RectTransform)cell.transform).anchoredPosition = CellPos(c, r);
            cell.SetSize(cellSize);
            cells[r * w + c] = cell;
        }
        if (cursor != null)
        {
            cursor.sizeDelta = Vector2.one * (cellSize + 14f);
            if (cursorHeld != null) cursorHeld.fontSize = Mathf.Round(cellSize * 0.52f);
        }
        if (ball != null) ball.sizeDelta = Vector2.one * cellSize;
        RebuildConnectors();
    }

    Vector2 CellPos(int c, int r) =>
        new Vector2((c - (layout.width - 1) * 0.5f) * pitch, ((layout.height - 1) * 0.5f - r) * pitch);

    void RebuildConnectors()
    {
        foreach (var c in connectors) if (c != null) Destroy(c.gameObject);
        connectors.Clear();
        float thick = Mathf.Clamp(pitch * 0.12f, 5f, 12f);
        int w = layout.width, h = layout.height;
        for (int r = 0; r < h; r++)
        for (int c = 0; c < w; c++)
        {
            if (c + 1 < w && layout.LinkH(c, r)) AddConnector(CellPos(c, r), CellPos(c + 1, r), new Vector2(pitch, thick));
            if (r + 1 < h && layout.LinkV(c, r)) AddConnector(CellPos(c, r), CellPos(c, r + 1), new Vector2(thick, pitch));
        }
        foreach (var cell in cells) cell.transform.SetAsLastSibling();
        if (cursor != null) cursor.SetAsLastSibling();
        if (ball != null) ball.SetAsLastSibling();
    }

    void AddConnector(Vector2 a, Vector2 b, Vector2 size)
    {
        var img = Instantiate(connectorTemplate, gridRoot);
        img.gameObject.SetActive(true);
        img.rectTransform.anchoredPosition = (a + b) * 0.5f;
        img.rectTransform.sizeDelta = size;
        img.color = UITheme.Alpha(UITheme.Accent, 0.92f);
        connectors.Add(img);
    }

    // ── Per frame ─────────────────────────────────────────────────────────────
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bool modalUp = modal != null && modal.IsOpen;

        if (!modalUp && !SceneFader.Busy)
        {
            switch (zone)
            {
                case Zone.Grid:    TickGrid(); break;
                case Zone.Actions: break;   // the actions MenuScreen drives itself
                case Zone.Test:    TickTest(); break;
            }
        }
        // Mouse stroke released anywhere (even off the grid) ends it.
        if (stroke != Stroke.None && strokeMouse && !Input.GetMouseButton(0) && !Input.GetMouseButton(1))
            EndStroke(apply: true);

        // Cursor follows the grid focus (hidden outside the grid).
        if (cursor != null)
        {
            bool on = zone == Zone.Grid;
            cursor.gameObject.SetActive(on);
            if (on)
            {
                cursor.anchoredPosition = Vector2.Lerp(cursor.anchoredPosition, CellPos(cur.x, cur.y), 1f - Mathf.Exp(-22f * dt));
                float pulse = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 6f);
                cursor.localScale = new Vector3(pulse, pulse, 1f);
                var img = cursor.GetComponent<Image>();
                if (img != null)
                    img.color = stroke == Stroke.Draw ? UITheme.Cyan : stroke == Stroke.Cut ? UITheme.Bad : UITheme.AccentHot;
            }
        }
        if (cursorHeld != null) cursorHeld.text = held > 0 ? held.ToString() : "";

        if (testOverlay != null)
            testOverlay.alpha = Mathf.MoveTowards(testOverlay.alpha, zone == Zone.Test ? 1f : 0f, dt / 0.2f);

        if (statusText != null && flashUntil > 0f && Time.unscaledTime > flashUntil) { flashUntil = 0f; RefreshStatus(); }
    }

    static bool DrawHeld => (!MenuInput.TextCapture && (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)
                                                      || Input.GetKey(KeyCode.Space)))
                            || Input.GetKey(KeyCode.JoystickButton0);
    static bool CutHeld  => (!MenuInput.TextCapture && (Input.GetKey(KeyCode.Delete) || Input.GetKey(KeyCode.Backspace)))
                            || Input.GetKey(KeyCode.JoystickButton2);

    void TickGrid()
    {
        int x = MenuInput.NavX, y = MenuInput.NavY;
        if (x != 0 || y != 0)
        {
            var n = new Vector2Int(Mathf.Clamp(cur.x + x, 0, layout.width - 1),
                                   Mathf.Clamp(cur.y + y, 0, layout.height - 1));
            if (n != cur)
            {
                if (stroke != Stroke.None && !strokeMouse) StrokeTo(n);
                else { cur = n; MenuAudio.Play(Sfx.Move, 0.7f); }
            }
        }

        if (MenuInput.Confirm) BeginStroke(Stroke.Draw, mouse: false);
        if (MenuInput.Alt1)    BeginStroke(Stroke.Cut,  mouse: false);
        if (stroke != Stroke.None && !strokeMouse && !(stroke == Stroke.Draw ? DrawHeld : CutHeld))
            EndStroke(apply: true);

        if (MenuInput.TabLeft || MenuInput.TabRight)
        {
            if (held > 0) CancelHold();
            SetZone(Zone.Actions);
        }
        if (MenuInput.Back)
        {
            MenuAudio.Play(Sfx.Back);
            if (stroke != Stroke.None) EndStroke(apply: false);
            else if (held > 0) CancelHold();
            else AskExit();
        }

        // Keyboard: number keys drop that exact gear here.
        if (!MenuInput.TextCapture)
            for (int g = 1; g <= GearSystem.GearCount; g++)
            {
                var key = g == 10 ? KeyCode.Alpha0 : KeyCode.Alpha0 + g;
                var pad = g == 10 ? KeyCode.Keypad0 : KeyCode.Keypad0 + g;
                if (Input.GetKeyDown(key) || Input.GetKeyDown(pad)) PutGear(cur, g);
            }
    }

    // ── Strokes: tap = gear action, hold + move = draw / cut lines ─────────────
    void BeginStroke(Stroke s, bool mouse)
    {
        stroke      = s;
        strokeMoved = false;
        strokeMouse = mouse;
        if (brushText != null) RefreshBrush();
    }

    void StrokeTo(Vector2Int target)
    {
        bool changed = false;
        // Walk one node at a time (a fast mouse can skip nodes).
        int guard = 64;
        while (cur != target && guard-- > 0)
        {
            var d = target - cur;
            var step = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new Vector2Int((int)Mathf.Sign(d.x), 0)
                                                        : new Vector2Int(0, (int)Mathf.Sign(d.y));
            var next = cur + step;
            changed |= layout.SetLinked(cur, next, stroke == Stroke.Draw);
            cur = next;
        }
        strokeMoved = true;
        if (changed)
        {
            dirty = true;
            MenuAudio.Play(stroke == Stroke.Draw ? Sfx.Adjust : Sfx.Back, 0.7f);
            if (cells != null) cells[cur.y * layout.width + cur.x].Pop();
            RebuildConnectors();
            RefreshAll();
        }
        else MenuAudio.Play(Sfx.Move, 0.5f);
    }

    void EndStroke(bool apply)
    {
        if (stroke == Stroke.None) return;
        var s = stroke;
        bool moved = strokeMoved;
        stroke = Stroke.None;
        strokeMouse = false;
        if (apply && !moved)
        {
            if (s == Stroke.Draw) Activate(cur);
            else                  Erase(cur);
        }
        RefreshBrush();
        UpdatePrompts();
    }

    // ── Pointer (ShifterCell) ─────────────────────────────────────────────────
    public void PointerEnter(Vector2Int p)
    {
        if (zone == Zone.Test || (modal != null && modal.IsOpen)) return;
        if (stroke != Stroke.None && strokeMouse) { if (p != cur) StrokeTo(p); return; }
        if (zone != Zone.Grid) SetZone(Zone.Grid, silent: true);
        if (cur != p) { cur = p; MenuAudio.Play(Sfx.Move, 0.5f); }
    }

    public void PointerDown(Vector2Int p, bool right)
    {
        if (zone == Zone.Test || (modal != null && modal.IsOpen)) return;
        if (zone != Zone.Grid) SetZone(Zone.Grid, silent: true);
        cur = p;
        BeginStroke(right ? Stroke.Cut : Stroke.Draw, mouse: true);
    }

    public void PointerUp()
    {
        if (stroke != Stroke.None && strokeMouse) EndStroke(apply: true);
    }

    // ── Editing ───────────────────────────────────────────────────────────────
    void Activate(Vector2Int p)
    {
        int v = layout.Cell(p.x, p.y);
        if (held > 0)
        {
            // Drop: onto a gear = swap; onto an empty node = place.
            if (v > 0 && v != held) layout.SetCell(heldFrom.x, heldFrom.y, v);
            layout.SetCell(p.x, p.y, held);
            held = 0;
            Changed(Sfx.Confirm);
            return;
        }
        if (v > 0)
        {
            held = v;
            heldFrom = p;
            layout.SetCell(p.x, p.y, 0);
            MenuAudio.Play(Sfx.Toggle);
            RefreshAll();
            UpdatePrompts();
            return;
        }
        int next = NextUnplacedGear();
        if (next < 0) { Nope("All 10 gears are placed — grab one to move it."); return; }
        layout.SetCell(p.x, p.y, next);
        Changed(Sfx.Adjust);
    }

    void PutGear(Vector2Int p, int g)
    {
        if (held > 0) CancelHold();
        for (int i = 0; i < layout.cells.Length; i++) if (layout.cells[i] == g) layout.cells[i] = 0;
        layout.SetCell(p.x, p.y, g);
        Changed(Sfx.Adjust);
    }

    void Erase(Vector2Int p)
    {
        if (held > 0) { CancelHold(); return; }
        if (layout.Cell(p.x, p.y) > 0) { layout.SetCell(p.x, p.y, 0); Changed(Sfx.Back); return; }
        if (layout.ClearLinks(p.x, p.y)) { Changed(Sfx.Back); return; }   // empty node: tap X clears its lines
        Nope(null);
    }

    void CancelHold()
    {
        if (held <= 0) return;
        layout.SetCell(heldFrom.x, heldFrom.y, held);
        held = 0;
        MenuAudio.Play(Sfx.Back);
        RefreshAll();
        UpdatePrompts();
    }

    void Changed(Sfx sound)
    {
        dirty = true;
        MenuAudio.Play(sound);
        if (cells != null && cur.y * layout.width + cur.x < cells.Length) cells[cur.y * layout.width + cur.x].Pop();
        RebuildConnectors();
        RefreshAll();
        UpdatePrompts();
    }

    void Nope(string why)
    {
        MenuAudio.Play(Sfx.Error);
        if (cells != null && zone == Zone.Grid) cells[cur.y * layout.width + cur.x].Shake();
        if (why != null) Status(why, UITheme.Gold);
    }

    int NextUnplacedGear()
    {
        var seen = new bool[GearSystem.GearCount + 1];
        foreach (int v in layout.cells) if (v >= 1 && v <= GearSystem.GearCount) seen[v] = true;
        if (held > 0) seen[held] = true;
        for (int g = 1; g <= GearSystem.GearCount; g++) if (!seen[g]) return g;
        return -1;
    }

    // ── Visual refresh ────────────────────────────────────────────────────────
    void RefreshAll()
    {
        for (int r = 0; r < layout.height; r++)
        for (int c = 0; c < layout.width; c++)
        {
            int v = layout.Cell(c, r);
            bool ghost = held > 0 && heldFrom == new Vector2Int(c, r);
            cells[r * layout.width + c].Show(v, ghost ? held : 0, layout.HasAnyLink(c, r));
        }

        // Gear tray.
        var placed = new bool[GearSystem.GearCount + 1];
        foreach (int v in layout.cells) if (v >= 1 && v <= GearSystem.GearCount) placed[v] = true;
        int next = NextUnplacedGear();
        for (int g = 1; g <= GearSystem.GearCount; g++)
        {
            int i = g - 1;
            if (trayChips != null && i < trayChips.Length && trayChips[i] != null)
                trayChips[i].color = placed[g] ? UITheme.Alpha(UITheme.Accent, 0.85f)
                                   : g == next ? UITheme.Alpha(UITheme.AccentHot, 0.35f)
                                   : UITheme.Alpha(Color.white, 0.07f);
            if (trayLabels != null && i < trayLabels.Length && trayLabels[i] != null)
                trayLabels[i].color = placed[g] ? UITheme.Text : g == next ? UITheme.AccentHot : UITheme.TextFaint;
        }

        RefreshBrush();
        RefreshStatus();
    }

    void RefreshBrush()
    {
        if (brushText == null) return;
        int next = NextUnplacedGear();
        brushText.text = stroke == Stroke.Draw && strokeMoved ? "<color=#4DE6FF>DRAWING LINES</color>"
                       : stroke == Stroke.Cut  && strokeMoved ? "<color=#FF4D6A>CUTTING LINES</color>"
                       : held > 0  ? $"MOVING GEAR <color=#FFCC4D>{held}</color>"
                       : next > 0  ? $"NEXT: GEAR <color=#B06BFF>{next}</color>"
                       : $"{layout.width} × {layout.height}  ·  ALL GEARS PLACED";
    }

    void RefreshStatus()
    {
        if (statusText == null || flashUntil > 0f) return;
        string problem = held > 0 ? $"Drop gear {held} somewhere first." : layout.Validate();
        if (problem == null)
        {
            statusText.text  = dirty ? "READY  —  SAVE TO RACE ON THIS GATE" : "SAVED  —  THIS IS YOUR RACE GATE";
            statusText.color = UITheme.Good;
        }
        else
        {
            statusText.text  = problem.ToUpperInvariant();
            statusText.color = UITheme.Gold;
        }
        saveButton.SetInteractable(problem == null);
        testButton.SetInteractable(problem == null);
        revertButton.SetInteractable(dirty);
    }

    void Status(string s, Color c, float seconds = 2.4f)
    {
        if (statusText == null) return;
        statusText.text  = s.ToUpperInvariant();
        statusText.color = c;
        flashUntil = Time.unscaledTime + seconds;
    }

    // ── Actions ───────────────────────────────────────────────────────────────
    void Save()
    {
        string problem = layout.Validate();
        if (problem != null) { Status(problem, UITheme.Bad); MenuAudio.Play(Sfx.Error); return; }
        layout.Save();
        dirty = false;
        Status("Saved!  Every car now shifts on your gate.", UITheme.Good);
        RefreshAll();
    }

    void Revert()
    {
        layout = ShifterLayout.Load() ?? ShifterLayout.Preset(0);
        held = 0;
        dirty = false;
        LayoutChanged(resetCursor: true);
        Status("Reverted to your last saved gate.", UITheme.Cyan);
    }

    void AskReset()
    {
        void Do()
        {
            ShifterLayout.Delete();
            layout = ShifterLayout.Preset(0);
            held = 0; dirty = false;
            LayoutChanged(resetCursor: true);
            Status("Back to the classic H-gate (custom gate deleted).", UITheme.Cyan);
        }
        if (modal == null) { Do(); return; }
        modal.Ask("RESET TO H-GATE?", "Your saved custom gate will be deleted.", "RESET", "CANCEL", Do);
    }

    void AskExit()
    {
        if (!dirty || modal == null) { SceneFader.Load(menuScene); return; }
        modal.Ask("LEAVE WITHOUT SAVING?", "Your changes to the gate will be lost.", "LEAVE", "STAY",
                  () => SceneFader.Load(menuScene));
    }

    // ── Test drive ────────────────────────────────────────────────────────────
    void BeginTest()
    {
        if (layout.Validate() != null) { Nope(layout.Validate()); return; }
        if (testGO != null) Destroy(testGO);
        testGO = new GameObject("ShifterTest");
        testGO.SetActive(false);
        testGears   = testGO.AddComponent<GearSystem>();
        testShifter = testGO.AddComponent<GearShifter>();
        testShifter.gearSystem = testGears;
        testGO.SetActive(true);
        layout.ApplyTo(testShifter);
        testShifter.ResetToStart();
        testShifter.OnSkipRejected += (tried, back) =>
        {
            MenuAudio.Play(Sfx.Error);
            Status($"Skipped to {tried} — kicked back to {back}.  Shift in order!", UITheme.Bad, 1.6f);
        };
        testShifter.OnMove += onGear => MenuAudio.Play(onGear ? Sfx.Adjust : Sfx.Move, 0.8f);

        if (ball != null) { ball.gameObject.SetActive(true); ball.anchoredPosition = CellPos(testShifter.Position.x, testShifter.Position.y); }
        SetZone(Zone.Test);
        actions.inputEnabled = false;
        Status("Shift with the ARROWS or RIGHT STICK (bumpers = paddles).", UITheme.Cyan, 3f);
    }

    void TickTest()
    {
        if (testShifter != null && ball != null)
        {
            var p = testShifter.Position;
            ball.anchoredPosition = Vector2.Lerp(ball.anchoredPosition, CellPos(p.x, p.y),
                                                 1f - Mathf.Exp(-24f * Time.unscaledDeltaTime));
        }
        if (testGearText != null && testGears != null)
            testGearText.text = testGears.CurrentGear > 0 ? testGears.CurrentGear.ToString() : "N";

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1)
            || Input.GetKeyDown(KeyCode.Backspace))
        {
            MenuAudio.Play(Sfx.Back);
            EndTest();
        }
    }

    void EndTest()
    {
        if (testGO != null) Destroy(testGO);
        testGO = null; testShifter = null; testGears = null;
        if (ball != null) ball.gameObject.SetActive(false);
        flashUntil = 0f;
        SetZone(Zone.Actions);
        RefreshStatus();
    }
}
