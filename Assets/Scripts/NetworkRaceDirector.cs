using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// Server-authoritative race state for multiplayer.  Lives as an in-scene
/// NetworkObject in each MP race scene, alongside MatchManager.
///
///   PreRace   → brief settle while MatchManager finishes spawning the grid.
///   Countdown → 3-2-1, synced via server time; cars frozen (input locked).
///   Racing    → server counts laps from each car's replicated Distance,
///               assigns finish positions (+ times) as cars cross for the last time.
///   Finished  → all human players done.
///
/// Lap counting + finish order run only on the server and are published per-car
/// via NetworkCarSync.Lap / .FinishPos / .FinishTime.  Every client reads those
/// for the HUD.  The pause / results / next-race MENUS are baked into the
/// scene (MPRaceMenus) and call the public actions below.
[RequireComponent(typeof(NetworkObject))]
public class NetworkRaceDirector : NetworkBehaviour
{
    public enum RaceState : byte { PreRace, Countdown, Racing, Finished }

    [Tooltip("Seconds to wait after the scene loads before the countdown, so all "
           + "cars have spawned and settled onto the grid.")]
    public float preRaceDelay   = 1.5f;
    public float countdownTime  = 3f;
    public int   totalLaps      = 3;

    readonly NetworkVariable<RaceState> state =
        new NetworkVariable<RaceState>(RaceState.PreRace);
    // Server time (NGO clock) at which the countdown ends / racing begins.
    readonly NetworkVariable<double> goTime = new NetworkVariable<double>();
    // Laps synced so clients' results/HUD agree with the host's setting.
    readonly NetworkVariable<int> lapsNet = new NetworkVariable<int>(3);

    // Session mode, published by the server so clients behave identically.
    // Time Trial = everyone free-drives together (no countdown/laps/finish/
    // collision); each client times themselves locally via NetworkTimeTrial.
    readonly NetworkVariable<byte> gameMode =
        new NetworkVariable<byte>((byte)MatchConfig.Mode.Race);
    public bool IsTimeTrial => gameMode.Value == (byte)MatchConfig.Mode.TimeTrial;
    public bool IsZoneRace  => gameMode.Value == (byte)MatchConfig.Mode.ZoneRace;

    public RaceState CurrentState => state.Value;
    public int       TotalLaps    => IsSpawned ? lapsNet.Value : totalLaps;

    // Seconds left on the countdown (≤0 once racing).  Drives the 3-2-1 overlay.
    public float CountdownRemaining =>
        state.Value == RaceState.Countdown
            ? Mathf.Max(0f, (float)(goTime.Value - NetworkManager.ServerTime.Time))
            : 0f;

    // Seconds since the green light.  Drives the race clock in the HUD.
    public float RaceElapsed =>
        state.Value >= RaceState.Racing
            ? Mathf.Max(0f, (float)(NetworkManager.ServerTime.Time - goTime.Value))
            : 0f;

    // ── Server-only lap bookkeeping ───────────────────────────────────────────
    class Bookkeeping
    {
        public float lastDist;
        public int   lap = 1;
        public bool  finished;
        public float netProgress;   // anti-exploit: laps need real forward driving
    }
    readonly Dictionary<NetworkCarSync, Bookkeeping> book = new Dictionary<NetworkCarSync, Bookkeeping>();
    Track track;
    double preRaceStart;
    int    finishCounter;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            gameMode.Value = (byte)MatchConfig.GameMode;
            totalLaps      = MatchConfig.Laps;
            lapsNet.Value  = totalLaps;
            preRaceStart   = NetworkManager.ServerTime.Time;

            // Time trial has no countdown — drop straight into free driving so
            // input flows immediately; each client runs its own standing-start clock.
            if (IsTimeTrial) state.Value = RaceState.Racing;
        }
    }

    void Update()
    {
        // Time trial: give the local player their own standing-start clock + reset
        // (created once the local car exists).  Runs on host and clients alike.
        if (IsTimeTrial) EnsureLocalTimeTrial();

        if (!IsServer) return;
        if (IsTimeTrial) return;   // no server-run countdown / laps / finish in TT
        if (track == null) { track = FindFirstObjectByType<Track>(); if (track == null) return; }

        switch (state.Value)
        {
            case RaceState.PreRace:
                if (NetworkManager.ServerTime.Time - preRaceStart >= preRaceDelay)
                {
                    GatherCars();
                    goTime.Value = NetworkManager.ServerTime.Time + countdownTime;
                    state.Value  = RaceState.Countdown;
                }
                break;

            case RaceState.Countdown:
                if (NetworkManager.ServerTime.Time >= goTime.Value)
                    state.Value = RaceState.Racing;
                break;

            case RaceState.Racing:
                if (IsZoneRace) break;   // ZoneRaceNet runs the train and calls the finish
                TickLaps();
                if (AllHumansFinished()) state.Value = RaceState.Finished;
                break;
        }
    }

    void GatherCars()
    {
        book.Clear();
        foreach (var c in FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None))
            book[c] = new Bookkeeping { lastDist = c.Distance.Value, lap = 1 };
    }

    void TickLaps()
    {
        float len = track.Length;
        if (len <= 0f) return;

        foreach (var kv in book)
        {
            var sync = kv.Key;
            var bk   = kv.Value;
            if (sync == null || bk.finished) continue;

            float curr = sync.Distance.Value;
            bk.netProgress += WrapDelta(curr - bk.lastDist, len);

            // Forward wrap: previous near the end, current near the start.  Only
            // counts with ~a full lap of NET forward driving — no U-turn exploit.
            if (track.IsClosed)
            {
                float q = len * 0.25f;
                if (bk.lastDist > len - q && curr < q && bk.netProgress >= len * 0.85f)
                {
                    bk.netProgress -= len;
                    bk.lap++;
                    if (bk.lap > totalLaps)
                    {
                        bk.finished          = true;
                        sync.FinishPos.Value  = ++finishCounter;
                        sync.FinishTime.Value = RaceElapsed;
                    }
                    sync.Lap.Value = Mathf.Min(bk.lap, totalLaps);
                }
            }
            bk.lastDist = curr;
        }
    }

    static float WrapDelta(float delta, float length)
    {
        if (length <= 0f) return delta;
        delta %= length;
        if (delta >  length * 0.5f) delta -= length;
        if (delta < -length * 0.5f) delta += length;
        return delta;
    }

    bool AllHumansFinished()
    {
        bool anyHuman = false;
        foreach (var kv in book)
        {
            var sync = kv.Key;
            if (sync == null) continue;
            var cc = sync.GetComponent<CarController>();
            if (cc != null && cc.aiControlled) continue;   // AI don't gate the finish
            anyHuman = true;
            if (!kv.Value.finished) return false;
        }
        return anyHuman;
    }

    // ── Read helpers for the HUD (local player) ───────────────────────────────
    /// Live 1-based position by total progress (laps banked + distance), with
    /// finished cars holding their crossing order.
    public int PositionOf(NetworkCarSync me)
    {
        if (me == null) return 0;
        if (me.FinishPos.Value > 0) return me.FinishPos.Value;

        float len    = track != null ? track.Length : 0f;
        float myProg = me.Lap.Value * len + me.Distance.Value;
        int   pos    = 1;
        foreach (var c in FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None))
        {
            if (c == me) continue;
            if (c.FinishPos.Value > 0) { pos++; continue; }
            if (c.Lap.Value * len + c.Distance.Value > myProg) pos++;
        }
        return pos;
    }

    public int RacerCount => FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None).Length;

    // ── Session actions (called by the baked MPRaceMenus) ─────────────────────
    static LobbyController Session =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.GetComponent<LobbyController>() : null;

    /// Full clean teardown (this player only): wipe out, shut the connection,
    /// destroy the persistent NetworkManager, land on the main menu.
    public void LeaveToMenu()
    {
        var s = Session;
        if (s != null) s.LeaveToMainMenu();
        else SceneFader.Load("MainMenu", null, null, () =>
        {
            var nm = NetworkManager.Singleton;
            if (nm != null) { nm.Shutdown(); if (nm.gameObject != null) Destroy(nm.gameObject); }
        });
    }

    /// Host: end the match for everyone and send all clients back to the menu.
    public void EndMatch()
    {
        if (!IsServer) return;
        LeaveAllClientRpc();   // clients tear down
        LeaveToMenu();         // host tears down
    }

    [ClientRpc]
    void LeaveAllClientRpc()
    {
        if (!IsServer) LeaveToMenu();
    }

    /// Host: end the current race early (jump straight to the results).
    public void ForceFinish()
    {
        if (!IsServer || state.Value != RaceState.Racing) return;
        var zone = GetComponent<ZoneRaceNet>();
        if (IsZoneRace && zone != null) zone.ServerFinishNow();   // hands out placings, then finishes
        else state.Value = RaceState.Finished;
    }

    /// Zone Race: the train crossed the line (ZoneRaceNet wrote the placings).
    public void ServerZoneFinished()
    {
        if (IsServer && state.Value == RaceState.Racing) state.Value = RaceState.Finished;
    }

    /// Host: run the next race with the settings in MatchConfig (the results
    /// screen writes them).  Everyone stays connected — no lobby round-trip.
    public void StartNextRace()
    {
        if (!IsServer) return;
        var s = Session;
        if (s != null) s.HostLoadRace();
        else NetworkManager.SceneManager.LoadScene("MP_Race", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    // ── Time-trial: local per-client clock ────────────────────────────────────
    NetworkTimeTrial localTT;
    public NetworkTimeTrial LocalTimeTrial => localTT;

    void EnsureLocalTimeTrial()
    {
        if (localTT != null) return;
        var car = CarController.LocalPlayerOverride;
        if (car == null) return;
        localTT = new GameObject("NetworkTimeTrial").AddComponent<NetworkTimeTrial>();
        localTT.Init(car, this);
    }

    // ── Standings (results table) ─────────────────────────────────────────────
    public struct Standing
    {
        public int    pos;
        public string name;
        public bool   you, finished, ai;
        public int    colorId;
        public float  time;       // finish time (finished cars)
        public int    lap;        // laps banked (unfinished cars)
    }

    public List<Standing> Standings()
    {
        if (track == null) track = FindFirstObjectByType<Track>();
        float len = track != null ? track.Length : 0f;
        var raw = new List<NetworkCarSync>(FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None));
        // Finished cars first (by finish position), then the rest by progress.
        raw.Sort((x, y) =>
        {
            bool xf = x.FinishPos.Value > 0, yf = y.FinishPos.Value > 0;
            if (xf && yf) return x.FinishPos.Value.CompareTo(y.FinishPos.Value);
            if (xf) return -1;
            if (yf) return 1;
            float px = x.Lap.Value * len + x.Distance.Value;
            float py = y.Lap.Value * len + y.Distance.Value;
            return py.CompareTo(px);
        });

        var list = new List<Standing>();
        for (int i = 0; i < raw.Count; i++)
        {
            var c = raw[i];
            list.Add(new Standing
            {
                pos      = i + 1,
                name     = c.DisplayName,
                you      = c.IsLocalPlayerCar,
                finished = c.FinishPos.Value > 0,
                ai       = c.IsAI,
                colorId  = c.ColorId.Value,
                time     = c.FinishTime.Value,
                lap      = c.Lap.Value,
            });
        }
        return list;
    }
}
