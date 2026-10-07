using System.Collections.Generic;
using UnityEngine;

/// Final placing of one Zone Racer (results screens).
public class ZoneStanding
{
    public int    place;
    public string name;
    public Color  color;
    public bool   local, ai;
    public float  time;          // when this car crossed the line
    public int    hits, bonuses, passesMade, timesPassed, shieldSaves;
}

/// ZONE RACE, single player: you and the AI rivals run the whole race inside
/// the drift zone as one train.  Positions only change when somebody ahead
/// crashes into a blocker — see ZoneTrain for the debt rules.  You start at
/// the back.  Built by RaceBootstrap; RaceMenus shows pause + results.
[DisallowMultipleComponent]
public class ZoneRaceSolo : MonoBehaviour, IZoneRaceView
{
    [System.NonSerialized] public Track track;
    [System.NonSerialized] public CarController player;
    [System.NonSerialized] public List<CarController> rivals = new List<CarController>();
    [System.NonSerialized] public List<int> rivalDifficulty = new List<int>();
    [System.NonSerialized] public int laps = 3;

    public event System.Action RaceFinished;
    public List<ZoneStanding> Results { get; private set; }
    public float RaceClock => clock;

    // IZoneRaceView
    public ZoneTrain Train       => train;
    public ZoneRacer Local       => localRacer;
    public ZonePilot LocalPilot  => localPilot;
    public int   Laps            => laps;
    public float Countdown       => started ? 0f : countdown;
    public bool  Racing          => started && !finished;
    public bool  Finished        => finished;
    public float TrackLength     => track != null ? track.Length : 0f;

    readonly ZoneTrain train = new ZoneTrain();
    readonly Dictionary<ulong, ZonePilot> pilots = new Dictionary<ulong, ZonePilot>();
    ZoneCourse course;
    ZoneCourseView view;
    ZonePilot localPilot;
    ZoneRacer localRacer;
    SpatialDriftTuning tuning;

    float v, gap, h0, finishLine;
    float countdown = 3.6f, clock;
    bool  started, finished;

    const float Ramp = 2.2f;   // seconds to reach train speed

    void Start()
    {
        if (track == null || player == null) { enabled = false; return; }
        tuning = SpatialDriftTuning.Shared;
        train.MaxDebt = Mathf.Max(1, tuning.zoneMaxDebt);
        gap = tuning.zoneGapLength;

        var gs = player.GetComponent<GearSystem>();
        float top = gs != null && gs.gearMaxSpeeds != null && gs.gearMaxSpeeds.Length > 0
                  ? gs.gearMaxSpeeds[gs.gearMaxSpeeds.Length - 1] : 145f;
        v = (top * tuning.speedMultiplier + tuning.speedAdditive) * tuning.zoneSpeedScale;

        // Grid: rivals in a shuffled order, you at the back.
        var cars = new List<(CarController car, int diff, int aiIndex)>();
        for (int i = 0; i < rivals.Count; i++)
            cars.Add((rivals[i], i < rivalDifficulty.Count ? rivalDifficulty[i] : 1, i));
        for (int i = cars.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (cars[i], cars[j]) = (cars[j], cars[i]); }
        cars.Add((player, 1, -1));

        int n = cars.Count;
        h0 = (n - 1) * gap;
        float len = track.Length;
        finishLine = Mathf.Max(1, laps) * len;
        if (finishLine < h0 + 400f) finishLine += len;          // tiny tracks: at least one real lap

        course = new ZoneCourse(track, tuning, Random.Range(1, int.MaxValue), h0 + 170f, finishLine - 25f);
        course.GenerateUntil(h0 + 700f);

        for (int k = 0; k < n; k++)
        {
            var (car, diff, aiIndex) = cars[k];
            bool human = car == player;
            var racer = new ZoneRacer
            {
                id = (ulong)k,
                name = human ? PlayerProfile.Name : PlayerProfile.AIName(aiIndex),
                colorId = human ? PlayerProfile.LocalColorId : PlayerProfile.AIColorId(aiIndex),
                ai = !human, aiDifficulty = diff,
            };
            train.Add(racer);

            float dist = (n - 1 - k) * gap;
            float rail = k % 2 == 0 ? -0.5f : 0.5f;
            car.startDistance      = dist;
            car.startLateralOffset = rail * course.Spacing;
            car.ResetToStart();
            car.inputEnabled = false;

            var pilot = car.gameObject.AddComponent<ZonePilot>();
            pilot.Begin(car, course, racer.id, human, diff, dist, rail);
            pilot.RowResolved += OnRow;
            pilots[racer.id] = pilot;
            if (human) { localPilot = pilot; localRacer = racer; }
        }

        view = gameObject.AddComponent<ZoneCourseView>();
        view.Init(track, course, tuning);
        view.SetFocus(localPilot);

        train.Hit         += r => { if (r == localRacer) ZoneFeed.Post("HIT!", UITheme.Bad); };
        train.ShieldSaved += r => { if (r == localRacer) ZoneFeed.Post("SHIELD SAVED YOU", UITheme.Cyan); };
        train.Bonus       += (r, cleared) => { if (r == localRacer) ZoneFeed.Post(cleared ? "DEBT CLEARED" : "SHIELD UP", UITheme.Gold); };
        train.Passed      += OnPassed;

        new GameObject("ZoneRaceHUD").AddComponent<ZoneRaceHUD>().Bind(this);
    }

    float TrainDistance(float t) => t < Ramp ? v * t * t / (2f * Ramp) : v * (t - Ramp * 0.5f);
    float TrainSpeedAt(float t)  => v * Mathf.Clamp01(t / Ramp);

    void Update()
    {
        if (course == null) return;
        float dt = Time.deltaTime;

        if (!started)
        {
            countdown -= dt;
            if (countdown <= 0f)
            {
                started = true;
                player.inputEnabled = true;
                ZoneFeed.Post("GO!", UITheme.Good);
            }
        }
        else clock += dt;

        float h = h0 + (started ? TrainDistance(clock) : 0f);

        course.GenerateUntil(h + 700f);
        float minDist = float.MaxValue;
        foreach (var p in pilots.Values) minDist = Mathf.Min(minDist, p.RaceDist);
        course.Prune(minDist - 60f);

        for (int i = 0; i < train.order.Count; i++)
        {
            if (!pilots.TryGetValue(train.order[i].id, out var p)) continue;
            p.Target       = h - train.UnitsBehindLeader(i) * gap;
            p.TrainSpeed   = started ? TrainSpeedAt(clock) : 0f;
            p.Driving      = started;
            p.InputAllowed = started && !finished;
        }

        if (started && !finished && h >= finishLine) Finish();
    }

    void OnRow(ZonePilot pilot, ZoneRow row, int result)
    {
        if (!started || finished) return;
        bool local = pilot == localPilot;
        train.Resolve(pilot.Id, row.index, result);
        if (result == 1)
        {
            if (local)
            {
                RacingCamera.Kick(0.75f);
                pilot.Car.Impact(0.8f);
                pilot.Car.GetComponent<CarAudio>()?.PlayZoneHit();
            }
        }
        else if (result == 2)
        {
            if (local)
            {
                view.Collect(row.index);
                pilot.Car.GetComponent<CarAudio>()?.PlayZoneBonus();
            }
        }
    }

    void OnPassed(ZoneRacer passer, ZoneRacer passed)
    {
        if (passer == localRacer)      { ZoneFeed.Post($"PASSED {passed.name}!", UITheme.Gold); MenuAudio.Play(Sfx.Confirm, 0.8f); }
        else if (passed == localRacer) { ZoneFeed.Post($"PASSED BY {passer.name}", UITheme.Bad); MenuAudio.Play(Sfx.Back, 0.8f); }
        else ZoneFeed.Post($"{passer.name} PASSES {passed.name}", UITheme.TextDim, big: false);
    }

    void Finish()
    {
        finished = true;
        train.Frozen = true;
        var results = new List<ZoneStanding>();
        for (int i = 0; i < train.order.Count; i++)
        {
            var r = train.order[i];
            results.Add(new ZoneStanding
            {
                place = i + 1, name = r.name, color = PlayerProfile.ColorById(r.colorId),
                local = r == localRacer, ai = r.ai,
                time = clock + train.UnitsBehindLeader(i) * gap / Mathf.Max(1f, v),
                hits = r.hits, bonuses = r.bonuses, passesMade = r.passesMade,
                timesPassed = r.timesPassed, shieldSaves = r.shieldSaves,
            });
        }
        Results = results;

        int mine = train.order.IndexOf(localRacer) + 1;
        TrackRecords.ValidateCurrent();
        PreviousBestFinish = TrackRecords.BestFinish(MatchConfig.MapName);
        NewBestFinish      = TrackRecords.SubmitFinish(MatchConfig.MapName, mine);

        ZoneFeed.Post(mine == 1 ? "VICTORY!" : "FINISH", mine == 1 ? UITheme.Gold : UITheme.Text);
        RaceFinished?.Invoke();
    }

    public bool NewBestFinish      { get; private set; }
    public int  PreviousBestFinish { get; private set; }
}
