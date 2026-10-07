using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// The player's CUSTOM shift gate — designed in the Shifter Lab and saved as
/// JSON.  When a saved layout exists, every GearShifter loads it instead of the
/// default H-gate.
///
/// A gate is a grid of NODES joined by LINES: the shift ball can only travel
/// along a line (exactly like the slots of a real gate), gears sit on nodes,
/// and nodes no line touches are simply unused.  Any size from 1 to 10 cells
/// in each direction — wide H-gates, tall sequential ladders, mazes.
[System.Serializable]
public class ShifterLayout
{
    public const int MinSize = 1, MaxSize = 10;

    public int    version = 2;
    public int    width  = 5;
    public int    height = 3;
    /// Row-major node values: 0 = empty node, 1..10 = that gear.
    public int[]  cells;
    /// Lines between neighbours: linksH[r*(w-1)+c] joins (c,r)–(c+1,r);
    /// linksV[r*w+c] joins (c,r)–(c,r+1).
    public bool[] linksH;
    public bool[] linksV;

    public static string PathFile =>
        Path.Combine(Application.persistentDataPath, "shifter_layout.json");

    public static bool Exists => File.Exists(PathFile);

    // ── Persistence ───────────────────────────────────────────────────────────
    public static ShifterLayout Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var l = JsonUtility.FromJson<ShifterLayout>(File.ReadAllText(PathFile));
                if (l != null) l.Migrate();
                if (l != null && l.Validate() == null) return l;
            }
        }
        catch { }
        return null;
    }

    public void Save()
    {
        try { File.WriteAllText(PathFile, JsonUtility.ToJson(this, true)); }
        catch (System.Exception e)
        { Debug.LogWarning("[ShifterLayout] save failed: " + e.Message); }
    }

    public static void Delete()
    {
        try { if (Exists) File.Delete(PathFile); } catch { }
    }

    /// v1 files had no lines (every edge between two non-wall cells was open)
    /// and -1 "wall" cells.  Convert exactly, so an old gate drives the same.
    void Migrate()
    {
        if (cells == null || cells.Length != width * height) return;
        bool hasLinks = linksH != null && linksV != null
                     && linksH.Length == Mathf.Max(0, width - 1) * height
                     && linksV.Length == width * Mathf.Max(0, height - 1);
        if (version >= 2 && hasLinks) return;

        linksH = new bool[Mathf.Max(0, width - 1) * height];
        linksV = new bool[width * Mathf.Max(0, height - 1)];
        for (int r = 0; r < height; r++)
        for (int c = 0; c < width; c++)
        {
            if (Cell(c, r) < 0) continue;
            if (c + 1 < width  && Cell(c + 1, r) >= 0) SetLinkH(c, r, true);
            if (r + 1 < height && Cell(c, r + 1) >= 0) SetLinkV(c, r, true);
        }
        for (int i = 0; i < cells.Length; i++) if (cells[i] < 0) cells[i] = 0;
        version = 2;
    }

    // ── Construction ──────────────────────────────────────────────────────────
    public static ShifterLayout Blank(int w = 5, int h = 3) => new ShifterLayout
    {
        width  = w, height = h,
        cells  = new int[w * h],
        linksH = new bool[Mathf.Max(0, w - 1) * h],
        linksV = new bool[w * Mathf.Max(0, h - 1)],
    };

    public ShifterLayout Clone() => new ShifterLayout
    {
        version = version, width = width, height = height,
        cells  = (int[])cells.Clone(),
        linksH = (bool[])linksH.Clone(),
        linksV = (bool[])linksV.Clone(),
    };

    /// A resized copy, content kept centred.  `lostGear` = a gear that would
    /// fall off the new grid (0 = nothing lost) — the caller refuses then.
    public ShifterLayout Resized(int w, int h, out int lostGear)
    {
        var n = Blank(w, h);
        int ox = (w - width) / 2, oy = (h - height) / 2;
        lostGear = 0;
        for (int r = 0; r < height; r++)
        for (int c = 0; c < width; c++)
        {
            int nc = c + ox, nr = r + oy;
            bool inside = nc >= 0 && nc < w && nr >= 0 && nr < h;
            int v = Cell(c, r);
            if (!inside) { if (v > 0 && lostGear == 0) lostGear = v; continue; }
            n.SetCell(nc, nr, v);
            if (c + 1 < width  && nc + 1 < w && LinkH(c, r)) n.SetLinkH(nc, nr, true);
            if (r + 1 < height && nr + 1 < h && LinkV(c, r)) n.SetLinkV(nc, nr, true);
        }
        return n;
    }

    // ── Access ────────────────────────────────────────────────────────────────
    public bool InBounds(int c, int r)           => c >= 0 && c < width && r >= 0 && r < height;
    public int  Cell(int c, int r)               => cells[r * width + c];
    public void SetCell(int c, int r, int v)     => cells[r * width + c] = v;
    public bool LinkH(int c, int r)              => linksH[r * (width - 1) + c];
    public bool LinkV(int c, int r)              => linksV[r * width + c];
    public void SetLinkH(int c, int r, bool on)  => linksH[r * (width - 1) + c] = on;
    public void SetLinkV(int c, int r, bool on)  => linksV[r * width + c] = on;

    /// Line between two ORTHOGONALLY ADJACENT nodes (false for anything else).
    public bool Linked(Vector2Int a, Vector2Int b)
    {
        if (!InBounds(a.x, a.y) || !InBounds(b.x, b.y)) return false;
        if (a.y == b.y && Mathf.Abs(a.x - b.x) == 1) return LinkH(Mathf.Min(a.x, b.x), a.y);
        if (a.x == b.x && Mathf.Abs(a.y - b.y) == 1) return LinkV(a.x, Mathf.Min(a.y, b.y));
        return false;
    }

    /// Returns true when the line actually changed.
    public bool SetLinked(Vector2Int a, Vector2Int b, bool on)
    {
        if (!InBounds(a.x, a.y) || !InBounds(b.x, b.y)) return false;
        if (a.y == b.y && Mathf.Abs(a.x - b.x) == 1)
        {
            int c = Mathf.Min(a.x, b.x);
            if (LinkH(c, a.y) == on) return false;
            SetLinkH(c, a.y, on); return true;
        }
        if (a.x == b.x && Mathf.Abs(a.y - b.y) == 1)
        {
            int r = Mathf.Min(a.y, b.y);
            if (LinkV(a.x, r) == on) return false;
            SetLinkV(a.x, r, on); return true;
        }
        return false;
    }

    static readonly Vector2Int[] Dirs = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

    public bool HasAnyLink(int c, int r)
    {
        var p = new Vector2Int(c, r);
        foreach (var d in Dirs) if (Linked(p, p + d)) return true;
        return false;
    }

    /// Remove every line touching a node.  Returns true if any were removed.
    public bool ClearLinks(int c, int r)
    {
        var p = new Vector2Int(c, r);
        bool any = false;
        foreach (var d in Dirs) any |= SetLinked(p, p + d, false);
        return any;
    }

    /// Nodes that matter (a gear or a line) — the HUD crops to these.
    public bool Used(int c, int r) => Cell(c, r) > 0 || HasAnyLink(c, r);

    // ── Validation ────────────────────────────────────────────────────────────
    /// null when the layout is playable, else a human-readable problem.
    public string Validate()
    {
        if (width < MinSize || height < MinSize || width > MaxSize || height > MaxSize) return "Grid size is out of range.";
        if (cells == null || cells.Length != width * height) return "Grid data is broken.";
        if (linksH == null || linksV == null
            || linksH.Length != Mathf.Max(0, width - 1) * height
            || linksV.Length != width * Mathf.Max(0, height - 1)) return "Gate lines are broken.";

        var pos  = new Vector2Int[GearSystem.GearCount + 1];
        var seen = new bool[GearSystem.GearCount + 1];
        for (int r = 0; r < height; r++)
        for (int c = 0; c < width;  c++)
        {
            int v = Cell(c, r);
            if (v < 1 || v > GearSystem.GearCount) continue;
            if (seen[v]) return $"Gear {v} is placed twice.";
            seen[v] = true;
            pos[v]  = new Vector2Int(c, r);
        }
        for (int g = 1; g <= GearSystem.GearCount; g++)
            if (!seen[g]) return $"Gear {g} isn't placed yet.";

        // Every gear reachable from gear 1 along the lines.
        var visited = new bool[width * height];
        var queue   = new Queue<Vector2Int>();
        queue.Enqueue(pos[1]);
        visited[pos[1].y * width + pos[1].x] = true;
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in Dirs)
            {
                var n = p + d;
                if (!Linked(p, n)) continue;
                int i = n.y * width + n.x;
                if (visited[i]) continue;
                visited[i] = true;
                queue.Enqueue(n);
            }
        }
        for (int g = 2; g <= GearSystem.GearCount; g++)
            if (!visited[pos[g].y * width + pos[g].x])
                return $"No line leads from gear 1 to gear {g}.";

        return null;
    }

    // ── To the real shifter ───────────────────────────────────────────────────
    /// Push onto a live GearShifter: a missing line = a wall on that edge.
    public void ApplyTo(GearShifter s)
    {
        if (s == null || Validate() != null) return;

        var grid = new GearShifter.GridCell[width * height];
        Vector2Int start = Vector2Int.zero;
        for (int r = 0; r < height; r++)
        for (int c = 0; c < width;  c++)
        {
            int v = Cell(c, r);
            grid[r * width + c] = v > 0
                ? new GearShifter.GridCell { kind = GearShifter.CellKind.Gear, gear = v }
                : HasAnyLink(c, r) ? new GearShifter.GridCell { kind = GearShifter.CellKind.Empty }
                                   : new GearShifter.GridCell { kind = GearShifter.CellKind.Wall };
            if (v == 1) start = new Vector2Int(c, r);
        }

        var wallsH = new bool[linksH.Length];
        var wallsV = new bool[linksV.Length];
        for (int i = 0; i < wallsH.Length; i++) wallsH[i] = !linksH[i];
        for (int i = 0; i < wallsV.Length; i++) wallsV[i] = !linksV[i];
        s.ApplyCustomLayout(width, height, grid, wallsH, wallsV, start);
    }

    // ── Presets ───────────────────────────────────────────────────────────────
    public static readonly string[] PresetNames = { "H-GATE", "SNAKE", "SPLIT GATE", "LADDER" };

    public static ShifterLayout Preset(int i)
    {
        ShifterLayout l;
        switch (i)
        {
            case 1:   // SNAKE — one winding road: 1–5 across the top, back 6–10 below
                l = Blank(5, 3);
                for (int c = 0; c < 5; c++) { l.SetCell(c, 0, 1 + c); l.SetCell(4 - c, 2, 6 + c); }
                for (int c = 0; c < 4; c++) { l.SetLinkH(c, 0, true); l.SetLinkH(c, 2, true); }
                l.SetLinkV(4, 0, true); l.SetLinkV(4, 1, true);
                break;

            case 2:   // SPLIT GATE — a vertical H: odds down the left, evens down the right
                l = Blank(3, 5);
                for (int r = 0; r < 5; r++)
                {
                    l.SetCell(0, r, r * 2 + 1); l.SetCell(2, r, r * 2 + 2);
                    l.SetLinkH(0, r, true);     l.SetLinkH(1, r, true);
                    if (r < 4) l.SetLinkV(1, r, true);
                }
                break;

            case 3:   // LADDER — sequential: one straight line, 1 at the top
                l = Blank(1, 10);
                for (int r = 0; r < 10; r++) { l.SetCell(0, r, r + 1); if (r < 9) l.SetLinkV(0, r, true); }
                break;

            default:  // H-GATE — the real thing: gears in slots off a neutral crossbar
                l = Blank(5, 3);
                for (int c = 0; c < 5; c++)
                {
                    l.SetCell(c, 0, c * 2 + 1); l.SetCell(c, 2, (c + 1) * 2);
                    l.SetLinkV(c, 0, true);     l.SetLinkV(c, 1, true);
                    if (c < 4) l.SetLinkH(c, 1, true);
                }
                break;
        }
        return l;
    }
}
