using System.Collections.Generic;
using UnityEngine;

/// One obstacle row of the Zone Race course, at a RACE distance (unwrapped —
/// lap 2's rows differ from lap 1's).  Rails are indexed left→right 0..2·pairs−1.
public struct ZoneRow
{
    public int   index;
    public float raceDist;
    public byte  pairs;     // rail pairs that fit the road here (1..3)
    public byte  blocked;   // bitmask of blocked rails
    public sbyte bonus;     // rail with a gold bonus gate, or -1

    public int  Rails => pairs * 2;
    public bool Blocked(int rail) => rail >= 0 && rail < 8 && (blocked & (1 << rail)) != 0;
}

/// The SHARED course every Zone Racer drives through — the same rows at the
/// same spots for everyone, so the car ahead is literally showing you the line.
/// Generation is sequential from a seed (a guaranteed-open path walks at most
/// two rails per row, so every gap is always two flicks away); online, the
/// host generates and streams the rows to everyone.
public class ZoneCourse
{
    public readonly List<ZoneRow> rows = new List<ZoneRow>();   // ascending index

    readonly Track track;
    readonly SpatialDriftTuning tuning;
    readonly System.Random rng;
    readonly float firstDist, lastDist;
    float nextDist;
    int   nextIndex;
    float pathOff;                 // guaranteed-open rail, half-integer offset

    public float Spacing   => Mathf.Max(1f, tuning.railsLaneSpacing);
    public int   MaxPairs  => Mathf.Clamp(tuning.railsMaxLanes / 2, 1, 3);
    public int   Generated => nextIndex;

    public ZoneCourse(Track track, SpatialDriftTuning tuning, int seed, float firstDist, float lastDist)
    {
        this.track = track;
        this.tuning = tuning;
        rng = new System.Random(seed);
        this.firstDist = firstDist;
        this.lastDist  = lastDist;
        nextDist = firstDist;
    }

    /// Rail pairs that fit the road at a (wrapped) track distance: 1..3.
    public int PairsAt(float trackDist)
    {
        float usable = track.HalfWidthAt(trackDist) - 1.6f;
        return Mathf.Clamp(Mathf.FloorToInt(usable / Spacing + 0.5f), 1, MaxPairs);
    }

    public static float RailOffset(int rail, int pairs) => rail - pairs + 0.5f;     // lane units
    public static int   RailIndex(float offset, int pairs) => Mathf.RoundToInt(offset + pairs - 0.5f);
    public float RailLateral(int rail, int pairs) => RailOffset(rail, pairs) * Spacing;

    /// Generate rows up to `raceDist` (never past the finish).  Returns the new ones.
    public List<ZoneRow> GenerateUntil(float raceDist)
    {
        var made = new List<ZoneRow>();
        float len = track.Length;
        if (len <= 60f) return made;
        int guard = 64;
        while (nextDist <= Mathf.Min(raceDist, lastDist) && guard-- > 0)
        {
            var row = MakeRow(nextIndex, nextDist, track.WrapDistance(nextDist));
            rows.Add(row);
            made.Add(row);
            nextIndex++;

            float progress = Mathf.InverseLerp(firstDist, lastDist, nextDist);
            float spacing  = Mathf.Lerp(tuning.zoneRowSpacingStart, tuning.zoneRowSpacingEnd, progress);
            nextDist += Mathf.Max(12f, spacing);
        }
        return made;
    }

    ZoneRow MakeRow(int index, float raceDist, float trackDist)
    {
        int p = PairsAt(trackDist), count = p * 2;

        // Walk the guaranteed-open path rail ≤2 rails per row.
        pathOff = Mathf.Clamp(pathOff + rng.Next(-2, 3), -(p - 0.5f), p - 0.5f);
        int path = RailIndex(pathOff, p);

        var others = new List<int>(count);
        for (int i = 0; i < count; i++) if (i != path) others.Add(i);
        for (int i = others.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (others[i], others[j]) = (others[j], others[i]);
        }
        int nBlocked = Mathf.Min(rng.Next(1, count), others.Count);
        byte mask = 0;
        for (int k = 0; k < nBlocked; k++) mask |= (byte)(1 << others[k]);

        // Rare gold bonus gate on a free rail.
        sbyte bonus = -1;
        if (rng.NextDouble() < tuning.zoneBonusChance)
        {
            var free = new List<int>();
            for (int i = 0; i < count; i++) if ((mask & (1 << i)) == 0) free.Add(i);
            if (free.Count > 0) bonus = (sbyte)free[rng.Next(0, free.Count)];
        }

        return new ZoneRow { index = index, raceDist = raceDist, pairs = (byte)p, blocked = mask, bonus = bonus };
    }

    /// Clients: rows streamed from the host.
    public void Add(ZoneRow r)
    {
        if (rows.Count > 0 && r.index <= rows[rows.Count - 1].index)
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].index == r.index) return;   // duplicate
            int at = rows.FindIndex(x => x.index > r.index);
            if (at < 0) rows.Add(r); else rows.Insert(at, r);
        }
        else rows.Add(r);
        nextIndex = Mathf.Max(nextIndex, r.index + 1);
    }

    /// Highest index seen so far (−1 = none).
    public int LastIndex => rows.Count > 0 ? rows[rows.Count - 1].index : -1;

    /// Forget rows well behind everyone who still needs them.
    public void Prune(float behindRaceDist)
    {
        int n = 0;
        while (n < rows.Count && rows[n].raceDist < behindRaceDist) n++;
        if (n > 0) rows.RemoveRange(0, n);
    }
}
