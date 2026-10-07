using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// One driver in a Zone Race train.
public class ZoneRacer
{
    public ulong  id;
    public string name = "DRIVER";
    public int    colorId;          // PlayerProfile.ColorById
    public bool   ai;
    public int    aiDifficulty = 1; // MatchConfig.Difficulty (AI only)

    /// Places behind the car directly ahead (1 = on their bumper … MaxDebt).
    /// The leader's debt is 0.
    public int  debt;
    /// A bonus gate taken with no debt to clear banks a shield: it absorbs
    /// your next hit.
    public bool shield;

    // Stats (results screen).
    public int hits, bonuses, passesMade, timesPassed, shieldSaves;

    /// Armed pass: the car ahead crashed while you were on its bumper (debt is
    /// now 0 — level).  The pass completes as soon as you get past that same
    /// row yourself; crash on it too and it's a wash.  -1 = not armed.
    [NonSerialized] public int passRow = -1;
}

/// The Zone Race rules — pure data, shared by the solo and online versions.
///
/// Cars run single-file.  Each car's DEBT is how many places it trails the car
/// directly ahead (1 = right on the bumper, max 3):
///   • You HIT a blocker → you drop back a place from the car ahead, and the car
///     behind closes a place on you.  If they were already on your bumper, they
///     draw LEVEL and PASS you once they clear that same row (you drop onto
///     their bumper).  If they crash on the very same row it's a wash.
///   • You take a gold BONUS gate → one of your mistakes is undone: you close a
///     place on the car ahead (the car behind drops back one).  With no debt to
///     clear, you bank a SHIELD that absorbs your next hit.
/// So a follower 3 places back needs the car ahead to crash 3 times to get by.
public class ZoneTrain
{
    public readonly List<ZoneRacer> order = new List<ZoneRacer>();   // [0] = leader
    public int  MaxDebt = 3;
    public bool Frozen;             // finish reached — nothing changes anymore

    public event Action<ZoneRacer>            Hit;          // took a hit (no shield)
    public event Action<ZoneRacer>            ShieldSaved;  // shield absorbed a hit
    public event Action<ZoneRacer, bool>      Bonus;        // true = debt cleared, false = shield
    public event Action<ZoneRacer, ZoneRacer> Passed;       // (passer, passed)

    public void Add(ZoneRacer r)
    {
        r.debt = order.Count == 0 ? 0 : 1;
        order.Add(r);
    }

    public int IndexOf(ulong id)
    {
        for (int i = 0; i < order.Count; i++) if (order[i].id == id) return i;
        return -1;
    }

    public ZoneRacer Find(ulong id)
    {
        int i = IndexOf(id);
        return i >= 0 ? order[i] : null;
    }

    public ZoneRacer Ahead(ZoneRacer r)  { int i = order.IndexOf(r); return i > 0 ? order[i - 1] : null; }
    public ZoneRacer Behind(ZoneRacer r) { int i = order.IndexOf(r); return i >= 0 && i + 1 < order.Count ? order[i + 1] : null; }

    /// Places between the leader and the car at `index` (sum of debts).
    public int UnitsBehindLeader(int index)
    {
        int u = 0;
        for (int i = 1; i <= index && i < order.Count; i++) u += order[i].debt;
        return u;
    }

    // ── Rules ─────────────────────────────────────────────────────────────────
    /// One car crossed one row: 0 clean, 1 crash, 2 gold gate.  Rows arrive in
    /// order per car.  Returns true when the train changed.
    public bool Resolve(ulong id, int rowIndex, int result)
    {
        if (Frozen) return false;
        var r = Find(id);
        if (r == null) return false;
        bool changed = false;

        // Settle an armed pass first — this row is at or beyond the one that armed it.
        if (r.passRow >= 0 && rowIndex >= r.passRow)
        {
            bool wash = rowIndex == r.passRow && result == 1 && !r.shield;
            r.passRow = -1;
            changed = true;
            if (wash)
            {
                // Both crashed on the same row: the two crashes cancel between
                // you (back on their bumper).  Theirs still costs them ground on
                // THEIR car ahead; yours still lets the car behind you close in.
                var a = Ahead(r);
                if (a != null && order.IndexOf(a) > 0) a.debt = Mathf.Min(MaxDebt, a.debt + 1);
                r.debt = 1;
                ApplyHit(r, rowIndex, fallBack: false);
                return true;
            }
            CompletePass(r);
        }

        if (result == 1)      { ApplyHit(r, rowIndex); changed = true; }
        else if (result == 2) { ApplyBonus(r);         changed = true; }
        return changed;
    }

    void ApplyHit(ZoneRacer r, int rowIndex, bool fallBack = true)
    {
        int i = order.IndexOf(r);
        if (i < 0) return;

        if (r.shield)
        {
            r.shield = false;
            r.shieldSaves++;
            ShieldSaved?.Invoke(r);
            return;
        }

        r.hits++;
        Hit?.Invoke(r);

        // The car behind closes in — and draws level if it was already on your bumper.
        if (i + 1 < order.Count)
        {
            var f = order[i + 1];
            if (f.passRow < 0)
            {
                f.debt--;
                if (f.debt <= 0)
                {
                    f.debt    = 0;          // level: the pass lands when they clear this row
                    f.passRow = rowIndex;
                    return;                 // this crash is spent on them (unless it's a wash)
                }
            }
        }

        // You fall back from the car ahead.
        if (fallBack && i > 0) r.debt = Mathf.Min(MaxDebt, r.debt + 1);
    }

    void CompletePass(ZoneRacer f)
    {
        int i = order.IndexOf(f);
        if (i <= 0) { f.debt = 0; return; }
        var r = order[i - 1];
        f.debt = r.debt;        // takes over their spot relative to the car ahead
        r.debt = 1;             // they drop onto your bumper
        order[i - 1] = f;
        order[i]     = r;
        f.passesMade++;
        r.timesPassed++;

        // Whoever was level with YOU was level with a car that just moved on.
        if (i + 1 < order.Count && order[i + 1].passRow >= 0)
        {
            order[i + 1].passRow = -1;
            order[i + 1].debt = 1;
        }
        Passed?.Invoke(f, r);
    }

    void ApplyBonus(ZoneRacer r)
    {
        int i = order.IndexOf(r);
        if (i < 0) return;
        r.bonuses++;

        if (i > 0 && r.debt > 1)
        {
            r.debt--;
            if (i + 1 < order.Count && order[i + 1].passRow < 0)
                order[i + 1].debt = Mathf.Min(MaxDebt, order[i + 1].debt + 1);
            Bonus?.Invoke(r, true);
        }
        else
        {
            r.shield = true;
            Bonus?.Invoke(r, false);
        }
    }

    // ── Network snapshot ("id,debt,shield,hits,bonuses,passes,passed,saves;…") ─
    public string Serialize()
    {
        var sb = new StringBuilder();
        foreach (var r in order)
        {
            sb.Append(r.id).Append(',').Append(r.debt).Append(',').Append(r.shield ? 1 : 0).Append(',')
              .Append(r.hits).Append(',').Append(r.bonuses).Append(',').Append(r.passesMade).Append(',')
              .Append(r.timesPassed).Append(',').Append(r.shieldSaves).Append(';');
        }
        if (Frozen) sb.Append('F');
        return sb.ToString();
    }

    /// Rebuild the order from a snapshot.  `make` supplies racers not seen yet.
    public void ApplySerialized(string s, Func<ulong, ZoneRacer> make)
    {
        if (string.IsNullOrEmpty(s)) return;
        var known = new Dictionary<ulong, ZoneRacer>();
        foreach (var r in order) known[r.id] = r;
        order.Clear();
        foreach (var part in s.Split(';'))
        {
            if (part.Length == 0) continue;
            if (part == "F") { Frozen = true; continue; }
            var f = part.Split(',');
            if (f.Length < 8 || !ulong.TryParse(f[0], out ulong id)) continue;
            if (!known.TryGetValue(id, out var r)) { r = make(id); if (r == null) continue; }
            int.TryParse(f[1], out r.debt);
            r.shield = f[2] == "1";
            int.TryParse(f[3], out r.hits);
            int.TryParse(f[4], out r.bonuses);
            int.TryParse(f[5], out r.passesMade);
            int.TryParse(f[6], out r.timesPassed);
            int.TryParse(f[7], out r.shieldSaves);
            order.Add(r);
        }
    }
}

/// What the Zone Race HUD / results read — implemented by the solo and online
/// orchestrators.
public interface IZoneRaceView
{
    ZoneTrain Train     { get; }
    ZoneRacer Local     { get; }
    ZonePilot LocalPilot{ get; }
    int   Laps          { get; }
    float Countdown     { get; }   // > 0 during the 3-2-1
    bool  Racing        { get; }
    bool  Finished      { get; }
    float TrackLength   { get; }
}

/// Tiny event bus for HUD call-outs ("PASSED VEGA!", "SHIELD UP").
public static class ZoneFeed
{
    public static event Action<string, Color, bool> Posted;   // (text, colour, big)
    public static void Post(string text, Color c, bool big = true) => Posted?.Invoke(text, c, big);
}
