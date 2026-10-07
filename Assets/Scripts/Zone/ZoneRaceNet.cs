using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// ZONE RACE online.  Lives on the MP race scene's RaceSystems object beside
/// MatchManager + NetworkRaceDirector; only wakes up when the session mode is
/// Zone Race.
///   HOST     owns the train (order, debts, shields), generates the course and
///            streams its rows to everyone, drives the AI rivals, calls the finish
///   EVERYONE drives their own car on its train slot, dodges the same rows and
///            reports each row's result (clean / crash / gold) to the host
/// The train state travels as one compact string; every client derives its
/// call-outs ("PASSED VEGA!") from how that state changes.
[RequireComponent(typeof(NetworkObject))]
public class ZoneRaceNet : NetworkBehaviour, IZoneRaceView
{
    readonly NetworkVariable<bool>  ready       = new NetworkVariable<bool>(false);
    readonly NetworkVariable<bool>  finishedNet = new NetworkVariable<bool>(false);
    readonly NetworkVariable<float> speedNet    = new NetworkVariable<float>(0f);
    readonly NetworkVariable<float> gapNet      = new NetworkVariable<float>(14f);
    readonly NetworkVariable<float> h0Net       = new NetworkVariable<float>(0f);
    readonly NetworkVariable<float> finishNet   = new NetworkVariable<float>(0f);
    readonly NetworkVariable<int>   lapsNet     = new NetworkVariable<int>(3);
    readonly NetworkVariable<int>   maxDebtNet  = new NetworkVariable<int>(3);
    readonly NetworkVariable<FixedString512Bytes> stateNet = new NetworkVariable<FixedString512Bytes>();

    NetworkRaceDirector director;
    Track track;
    SpatialDriftTuning tuning;

    readonly ZoneTrain train = new ZoneTrain();
    ZoneCourse course;
    ZoneCourseView view;
    readonly Dictionary<ulong, ZonePilot> aiPilots = new Dictionary<ulong, ZonePilot>();   // server
    readonly Dictionary<ulong, int> lastReported = new Dictionary<ulong, int>();          // server
    ZonePilot localPilot;
    ZoneRacer localRacer;
    bool serverInit, clientInit, hudBuilt;
    float readySince = -1f;
    readonly List<ulong> prevOrder = new List<ulong>();
    int prevSaves, prevBonuses;
    bool prevShield;

    const float Ramp = 2.2f;

    // ── IZoneRaceView ─────────────────────────────────────────────────────────
    public ZoneTrain Train      => train;
    public ZoneRacer Local      => localRacer;
    public ZonePilot LocalPilot => localPilot;
    public int   Laps           => lapsNet.Value;
    public float Countdown      => director != null ? director.CountdownRemaining : 0f;
    public bool  Racing         => director != null && director.CurrentState == NetworkRaceDirector.RaceState.Racing && !finishedNet.Value;
    public bool  Finished       => finishedNet.Value;
    public float TrackLength    => track != null ? track.Length : 0f;

    public override void OnNetworkSpawn()  => stateNet.OnValueChanged += OnState;
    public override void OnNetworkDespawn() => stateNet.OnValueChanged -= OnState;

    bool Active => director != null && director.IsSpawned && director.IsZoneRace;

    float TrainDistance(float t) =>
        t < Ramp ? speedNet.Value * t * t / (2f * Ramp) : speedNet.Value * (t - Ramp * 0.5f);

    float Head => h0Net.Value + (director != null ? TrainDistance(director.RaceElapsed) : 0f);
    float HeadSpeed => speedNet.Value * Mathf.Clamp01((director != null ? director.RaceElapsed : 0f) / Ramp);

    void Update()
    {
        if (director == null) director = GetComponent<NetworkRaceDirector>();
        if (!IsSpawned || !Active) return;
        if (track == null) { track = FindAnyObjectByType<Track>(); if (track == null || track.Length < 1f) return; }
        if (tuning == null) tuning = SpatialDriftTuning.Shared;

        if (IsServer) ServerTick();
        ClientTick();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Server
    // ══════════════════════════════════════════════════════════════════════════
    void ServerTick()
    {
        if (!serverInit) { ServerInit(); if (!serverInit) return; }

        float h = Head;
        var made = course.GenerateUntil(h + 700f);
        if (made.Count > 0) SendRows(made);

        bool racing = director.CurrentState == NetworkRaceDirector.RaceState.Racing;
        for (int i = 0; i < train.order.Count; i++)
        {
            if (!aiPilots.TryGetValue(train.order[i].id, out var p) || p == null) continue;
            p.Target     = h - train.UnitsBehindLeader(i) * gapNet.Value;
            p.TrainSpeed = HeadSpeed;
            p.Driving    = racing || finishedNet.Value;
        }

        if (racing && !finishedNet.Value && h >= finishNet.Value) ServerFinishNow();
    }

    void ServerInit()
    {
        var syncs = FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None);
        int expected = NetworkManager.ConnectedClientsIds.Count + MatchConfig.AICount;
        if (syncs.Length < expected || syncs.Length == 0) return;

        // Train order = the single-file grid MatchManager laid out (front first).
        var cars = new List<NetworkCarSync>(syncs);
        cars.Sort((a, b) => b.GetComponent<CarController>().startDistance
                              .CompareTo(a.GetComponent<CarController>().startDistance));

        var any = cars[0].GetComponent<GearSystem>();
        float top = any != null && any.gearMaxSpeeds != null && any.gearMaxSpeeds.Length > 0
                  ? any.gearMaxSpeeds[any.gearMaxSpeeds.Length - 1] : 145f;
        float v   = (top * tuning.speedMultiplier + tuning.speedAdditive) * tuning.zoneSpeedScale;
        float gap = tuning.zoneGapLength;
        int   n   = cars.Count;
        float h0  = (n - 1) * gap;
        int   laps = Mathf.Clamp(MatchConfig.Laps, 1, 20);
        float finish = laps * track.Length;
        if (finish < h0 + 400f) finish += track.Length;

        train.order.Clear();
        train.MaxDebt = Mathf.Max(1, tuning.zoneMaxDebt);
        foreach (var s in cars)
        {
            var r = new ZoneRacer { id = s.NetworkObjectId, ai = s.IsAI, colorId = s.ColorId.Value, name = s.DisplayName };
            if (r.ai)
            {
                int ai = Mathf.Max(0, s.ColorId.Value - 100);
                r.aiDifficulty = ai < MatchConfig.AIDifficulties.Count ? (int)MatchConfig.AIDifficulties[ai] : 1;
            }
            train.Add(r);
        }

        course = new ZoneCourse(track, tuning, Random.Range(1, int.MaxValue), h0 + 170f, finish - 25f);

        // AI rivals are server-owned: pilot them here.
        foreach (var s in cars)
        {
            if (!s.IsAI) continue;
            var car = s.GetComponent<CarController>();
            var r = train.Find(s.NetworkObjectId);
            car.ResetToStart();                     // make sure it sits on its slot now
            float rail = Mathf.Round(car.startLateralOffset / course.Spacing - 0.5f) + 0.5f;
            var p = car.gameObject.AddComponent<ZonePilot>();
            p.Begin(car, course, r.id, false, r.aiDifficulty, car.startDistance, rail);
            p.RowResolved += OnAIRow;
            aiPilots[r.id] = p;
        }

        speedNet.Value   = v;
        gapNet.Value     = gap;
        h0Net.Value      = h0;
        finishNet.Value  = finish;
        lapsNet.Value    = laps;
        maxDebtNet.Value = train.MaxDebt;
        Publish();
        ready.Value = true;
        serverInit  = true;

        var first = course.GenerateUntil(h0 + 700f);
        if (first.Count > 0) SendRows(first);
    }

    void Publish() => stateNet.Value = new FixedString512Bytes(train.Serialize());

    void OnAIRow(ZonePilot p, ZoneRow row, int result) => ServerApply(p.Id, row.index, result);

    void ServerApply(ulong racerId, int rowIndex, int result)
    {
        if (finishedNet.Value || director.CurrentState != NetworkRaceDirector.RaceState.Racing) return;
        if (lastReported.TryGetValue(racerId, out int last) && rowIndex <= last) return;   // duplicate
        lastReported[racerId] = rowIndex;
        if (train.Resolve(racerId, rowIndex, result)) Publish();
    }

    /// Host "END RACE" (and the natural finish): freeze and hand out placings.
    public void ServerFinishNow()
    {
        if (!IsServer || finishedNet.Value) return;
        train.Frozen = true;
        finishedNet.Value = true;
        Publish();
        float v = Mathf.Max(1f, speedNet.Value);
        float now = director.RaceElapsed;
        var syncs = FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None);
        for (int i = 0; i < train.order.Count; i++)
        {
            ulong id = train.order[i].id;
            foreach (var s in syncs)
            {
                if (s.NetworkObjectId != id) continue;
                s.FinishPos.Value  = i + 1;
                s.FinishTime.Value = now + train.UnitsBehindLeader(i) * gapNet.Value / v;
            }
        }
        director.ServerZoneFinished();
    }

    // Rows travel as parallel arrays (NGO serialises primitive arrays natively).
    void SendRows(List<ZoneRow> rows)
    {
        int n = rows.Count;
        var idx = new int[n]; var dist = new float[n]; var pairs = new byte[n]; var blocked = new byte[n]; var bonus = new sbyte[n];
        for (int i = 0; i < n; i++)
        {
            idx[i] = rows[i].index; dist[i] = rows[i].raceDist; pairs[i] = rows[i].pairs;
            blocked[i] = rows[i].blocked; bonus[i] = rows[i].bonus;
        }
        RowsRpc(idx, dist, pairs, blocked, bonus);
    }

    [Rpc(SendTo.NotServer)]
    void RowsRpc(int[] idx, float[] dist, byte[] pairs, byte[] blocked, sbyte[] bonus)
    {
        if (course == null) EnsureClientCourse();
        if (course == null || idx == null) return;
        if (idx.Length > 0 && idx[0] > course.LastIndex + 1) RequestRowsRpc(course.LastIndex + 1);   // filled a gap?
        for (int i = 0; i < idx.Length; i++)
            course.Add(new ZoneRow { index = idx[i], raceDist = dist[i], pairs = pairs[i], blocked = blocked[i], bonus = bonus[i] });
    }

    [Rpc(SendTo.Server)]
    void RequestRowsRpc(int fromIndex)
    {
        if (course == null) return;
        var list = new List<ZoneRow>();
        foreach (var r in course.rows) if (r.index >= fromIndex) list.Add(r);
        if (list.Count > 0) SendRows(list);
    }

    [Rpc(SendTo.Server)]
    void ReportRowRpc(int rowIndex, byte result, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        foreach (var s in FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None))
            if (!s.IsAI && s.OwnerClientId == sender) { ServerApply(s.NetworkObjectId, rowIndex, result); return; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Every client (host included)
    // ══════════════════════════════════════════════════════════════════════════
    void EnsureClientCourse()
    {
        if (course != null || track == null || !ready.Value) return;
        if (tuning == null) tuning = SpatialDriftTuning.Shared;
        course = new ZoneCourse(track, tuning, 0, h0Net.Value + 170f, finishNet.Value - 25f);
    }

    void ClientTick()
    {
        if (!ready.Value) return;
        if (readySince < 0f) readySince = Time.unscaledTime;
        EnsureClientCourse();
        if (course == null) return;

        // Missed the first rows (joined the scene late)?  Ask for them.
        if (!IsServer && course.LastIndex < 0 && Time.unscaledTime - readySince > 1.5f)
        {
            readySince = Time.unscaledTime;
            RequestRowsRpc(0);
        }

        if (!clientInit) OnState(default, stateNet.Value);
        RefreshNames();

        // Our car joins the train once it exists locally.
        if (localPilot == null)
        {
            var car = CarController.LocalPlayerOverride;
            if (car == null) return;
            var sync = car.GetComponent<NetworkCarSync>();
            if (sync == null || !sync.HasGridSlot) return;     // wait until we're on our slot
            localRacer = train.Find(sync.NetworkObjectId);
            if (localRacer == null) return;
            float rail = Mathf.Round(car.startLateralOffset / course.Spacing - 0.5f) + 0.5f;
            localPilot = car.gameObject.AddComponent<ZonePilot>();
            localPilot.Begin(car, course, localRacer.id, true, 1, car.startDistance, rail);
            localPilot.RowResolved += OnLocalRow;
            prevSaves = localRacer.shieldSaves; prevBonuses = localRacer.bonuses; prevShield = localRacer.shield;
            RememberOrder();
        }

        if (!hudBuilt)
        {
            hudBuilt = true;
            view = gameObject.AddComponent<ZoneCourseView>();
            view.Init(track, course, tuning);
            view.SetFocus(localPilot);
            new GameObject("ZoneRaceHUD").AddComponent<ZoneRaceHUD>().Bind(this);
        }

        bool racing = director.CurrentState == NetworkRaceDirector.RaceState.Racing || finishedNet.Value;
        float h = Head;
        int idx = train.order.IndexOf(localRacer);
        if (idx >= 0)
        {
            localPilot.Target       = h - train.UnitsBehindLeader(idx) * gapNet.Value;
            localPilot.TrainSpeed   = HeadSpeed;
            localPilot.Driving      = racing;
            localPilot.InputAllowed = racing && !finishedNet.Value;
        }

        // Forget rows everyone on this machine has passed.
        float min = localPilot.RaceDist;
        foreach (var p in aiPilots.Values) if (p != null) min = Mathf.Min(min, p.RaceDist);
        course.Prune(min - 60f);
    }

    void OnLocalRow(ZonePilot p, ZoneRow row, int result)
    {
        if (finishedNet.Value || director.CurrentState != NetworkRaceDirector.RaceState.Racing) return;
        ReportRowRpc(row.index, (byte)result);   // clean rows too: they complete armed passes

        // Instant local feedback; the host's state decides what it cost.
        if (result == 1)
        {
            RacingCamera.Kick(0.75f);
            p.Car.Impact(0.8f);
            p.Car.GetComponent<CarAudio>()?.PlayZoneHit();
            if (localRacer == null || !localRacer.shield) ZoneFeed.Post("HIT!", UITheme.Bad);   // shield call-out comes from the host
        }
        else if (result == 2)
        {
            if (view != null) view.Collect(row.index);
            p.Car.GetComponent<CarAudio>()?.PlayZoneBonus();
        }
    }

    ZoneRacer MakeRacer(ulong id)
    {
        foreach (var s in FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None))
            if (s.NetworkObjectId == id)
                return new ZoneRacer { id = id, ai = s.IsAI, colorId = s.ColorId.Value, name = s.DisplayName };
        return new ZoneRacer { id = id };
    }

    void RefreshNames()
    {
        var syncs = FindObjectsByType<NetworkCarSync>(FindObjectsSortMode.None);
        foreach (var r in train.order)
            foreach (var s in syncs)
                if (s.NetworkObjectId == r.id)
                {
                    r.name = s.DisplayName;
                    if (s.ColorId.Value >= 0) r.colorId = s.ColorId.Value;
                    r.ai = s.IsAI;
                }
    }

    // State changed → rebuild the train, then call out what happened to us.
    void OnState(FixedString512Bytes _, FixedString512Bytes value)
    {
        string s = value.ToString();
        if (string.IsNullOrEmpty(s)) return;
        clientInit = true;

        // prevOrder is the order as of the LAST state we processed (on the host
        // the train has already been changed by the time this fires).
        if (!IsServer) train.ApplySerialized(s, MakeRacer);
        train.MaxDebt = maxDebtNet.Value;
        if (localRacer == null) { RememberOrder(); return; }

        int before = prevOrder.IndexOf(localRacer.id);
        int after  = train.order.IndexOf(localRacer);
        if (before >= 0 && after >= 0 && after != before)
        {
            if (after < before && after + 1 < train.order.Count)
            { ZoneFeed.Post($"PASSED {train.order[after + 1].name}!", UITheme.Gold); MenuAudio.Play(Sfx.Confirm, 0.8f); }
            else if (after > before && after > 0)
            { ZoneFeed.Post($"PASSED BY {train.order[after - 1].name}", UITheme.Bad); MenuAudio.Play(Sfx.Back, 0.8f); }
        }
        else if (before >= 0)
        {
            // Somebody else swapped places.
            for (int i = 0; i + 1 < train.order.Count; i++)
            {
                var a = train.order[i]; var b = train.order[i + 1];
                int pa = prevOrder.IndexOf(a.id), pb = prevOrder.IndexOf(b.id);
                if (pa == i + 1 && pb == i && a != localRacer && b != localRacer)
                    ZoneFeed.Post($"{a.name} PASSES {b.name}", UITheme.TextDim, big: false);
            }
        }

        if (localRacer.shieldSaves > prevSaves) ZoneFeed.Post("SHIELD SAVED YOU", UITheme.Cyan);
        else if (localRacer.bonuses > prevBonuses)
            ZoneFeed.Post(localRacer.shield && !prevShield ? "SHIELD UP" : "DEBT CLEARED", UITheme.Gold);
        prevSaves = localRacer.shieldSaves;
        prevBonuses = localRacer.bonuses;
        prevShield = localRacer.shield;
        RememberOrder();
    }

    void RememberOrder()
    {
        prevOrder.Clear();
        foreach (var r in train.order) prevOrder.Add(r.id);
    }
}
