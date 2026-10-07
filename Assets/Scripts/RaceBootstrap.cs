using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

/// Builds a complete single-player race from the SELECTED MAP (MatchConfig) at
/// runtime — track, AI grid, player car, camera and RaceManager — so one "Race"
/// scene plays ANY map (built-in or custom) instead of a scene per map.
///
/// Mirrors what the editor's SceneSetup did, but data-driven and at runtime,
/// reusing the same gameplay components (CarController/GearSystem/AIDriver/
/// RaceManager/RacingCamera/RacingHUD) and your CarTuning.
[DisallowMultipleComponent]
public class RaceBootstrap : MonoBehaviour
{
    public string menuScene = "MainMenu";

    void Start()
    {
        switch (MatchConfig.GameMode)
        {
            case MatchConfig.Mode.TimeTrial: BuildTimeTrial(); break;
            case MatchConfig.Mode.ZoneRace:  BuildZoneRace();  break;
            default:                         BuildRace();      break;
        }
    }

    // ── Classic race: player + AI grid + RaceManager ──────────────────────────
    void BuildRace()
    {
        var data  = MatchConfig.ResolveMap();
        BuildLighting();
        var track = BuildTrack(data);

        var diffs   = MatchConfig.AIDifficulties ?? new List<MatchConfig.Difficulty>();
        int aiCount = Mathf.Clamp(diffs.Count, 0, 7);

        const float baseDist = 6f, spacing = 6f, lat = 3.5f;

        // AI line up ahead of the player; the player starts at the back.
        for (int i = 0; i < aiCount; i++)
        {
            float d = baseDist + (i + 1) * spacing;
            float l = (i % 2 == 0) ? -lat : lat;
            BuildCar(track, "AI " + (i + 1),
                     PlayerProfile.ColorById(PlayerProfile.AIColorId(i)),
                     d, l, ai: true, diffs[i]);
        }

        var player = BuildCar(track, "Car (Player)", PlayerProfile.LocalColor,
                              baseDist, lat, ai: false, MatchConfig.Difficulty.Medium);

        BuildCamera(player.transform);

        // RaceManager.Awake auto-finds the track, the racers and the local player
        // (first non-AI), and adds the bump system when there are opponents.
        var rmGO = new GameObject("RaceManager");
        var race = rmGO.AddComponent<RaceManager>();
        race.track     = track;
        race.totalLaps = Mathf.Clamp(MatchConfig.Laps, 1, 20);
        race.menuScene = menuScene;

        new GameObject("MiniMap").AddComponent<MiniMap>();
    }

    // ── Time trial: one car, one flying lap, instant restart ──────────────────
    void BuildTimeTrial()
    {
        var data  = MatchConfig.ResolveMap();
        BuildLighting();
        var track = BuildTrack(data);

        // Start on the line so the first full loop is the timed lap.
        var player = BuildCar(track, "Car (Player)", PlayerProfile.LocalColor,
                              0f, 0f, ai: false, MatchConfig.Difficulty.Medium);
        BuildCamera(player.transform);

        // Build inactive so timeTrial is set BEFORE RaceManager.Awake runs
        // (Awake forces 1 lap + keys the best-time record off the mode).
        var rmGO = new GameObject("RaceManager");
        rmGO.SetActive(false);
        var race = rmGO.AddComponent<RaceManager>();
        race.track     = track;
        race.timeTrial = true;       // 1 lap, instant restart, no terminal screen
        race.menuScene = menuScene;
        rmGO.SetActive(true);

        // Ghost of your personal best — replays on every reset so you can chase it.
        var ghost = new GameObject("TimeTrialGhost").AddComponent<TimeTrialGhost>();
        ghost.player  = player.GetComponent<CarController>();
        ghost.race    = race;
        ghost.track   = track;
        ghost.mapName = MatchConfig.MapName;

        new GameObject("MiniMap").AddComponent<MiniMap>();

        // Hidden dev panels: F8 = atmosphere, F9 = drift-zone tuning, F10 = drift
        // feel.  All save/reset persistent overrides.
        new GameObject("AtmospherePanel").AddComponent<AtmospherePanel>();
        new GameObject("DriftTuningPanel").AddComponent<DriftTuningPanel>();
        new GameObject("DriftFeelPanel").AddComponent<DriftFeelPanel>();

        AddHint("HIT THE GAS TO START THE CLOCK      ·      R / B  RESTART      ·      ESC / START  PAUSE");
    }

    // ── Zone Race: the whole race inside the drift zone, as one train ─────────
    void BuildZoneRace()
    {
        var data  = MatchConfig.ResolveMap();
        BuildLighting();
        var track = BuildTrack(data);

        var diffs   = MatchConfig.AIDifficulties ?? new List<MatchConfig.Difficulty>();
        int aiCount = Mathf.Clamp(diffs.Count, 0, 7);

        // ZoneRaceSolo lays out the train; these start positions are placeholders.
        var zone = new GameObject("ZoneRace").AddComponent<ZoneRaceSolo>();
        zone.track = track;
        zone.laps  = Mathf.Clamp(MatchConfig.Laps, 1, 20);
        for (int i = 0; i < aiCount; i++)
        {
            var ai = BuildCar(track, "AI " + (i + 1), PlayerProfile.ColorById(PlayerProfile.AIColorId(i)),
                              6f + i * 6f, 0f, ai: true, diffs[i], withBrain: false, withHud: false);
            zone.rivals.Add(ai.GetComponent<CarController>());
            zone.rivalDifficulty.Add((int)diffs[i]);
        }
        var player = BuildCar(track, "Car (Player)", PlayerProfile.LocalColor, 0f, 0f,
                              ai: false, MatchConfig.Difficulty.Medium, withBrain: false, withHud: false);
        zone.player = player.GetComponent<CarController>();

        BuildCamera(player.transform);
        new GameObject("MiniMap").AddComponent<MiniMap>();
    }

    void AddHint(string text)
    {
        var go = new GameObject("ModeHud");
        go.AddComponent<ModeHud>().hint = text;
    }

    static Track BuildTrack(TrackData data)
    {
        var go    = new GameObject("Track");
        go.AddComponent<SplineContainer>();
        var track = go.AddComponent<Track>();
        data.ApplyTo(track);

        var meshGO = new GameObject("TrackMesh");
        meshGO.transform.SetParent(go.transform, false);
        meshGO.AddComponent<MeshFilter>();
        meshGO.AddComponent<MeshRenderer>();
        var mb   = meshGO.AddComponent<TrackMeshBuilder>();
        mb.track = track;
        mb.Rebuild();

        TrackScenery.Build(track, data);   // trackside 2D layers (per-node styles)

        return track;
    }

    GameObject BuildCar(Track track, string name, Color color,
                        float dist, float lat, bool ai, MatchConfig.Difficulty diff,
                        bool withBrain = true, bool withHud = true)
    {
        // Build INACTIVE so we can set aiControlled BEFORE Awake runs — otherwise
        // CarController.Awake (firing at activation) would hand AI cars the
        // player-only systems (Spatial Drift / audio).
        var go = new GameObject(name);
        go.SetActive(false);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        body.transform.localScale    = new Vector3(1.8f, 0.6f, 3.6f);
        Destroy(body.GetComponent<Collider>());
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        body.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = color };

        var rb           = go.AddComponent<Rigidbody>();
        rb.isKinematic   = true;
        rb.useGravity    = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var gs   = go.AddComponent<GearSystem>();
        var ctrl = go.AddComponent<CarController>();
        ctrl.gearSystem         = gs;
        ctrl.track              = track;
        ctrl.startDistance      = dist;
        ctrl.startLateralOffset = lat;
        ctrl.aiControlled       = ai;

        if (ai && withBrain)
        {
            float skill = MatchConfig.SkillFor(diff);
            var brain = go.AddComponent<AIDriver>();
            brain.skill            = Mathf.Clamp01(skill + Random.Range(-0.05f, 0.05f));
            brain.gearCap          = MatchConfig.GearCapFor(diff);
            brain.preferredLateral = Random.Range(-2.5f, 2.5f);
            brain.driftAffinity    = Mathf.Clamp01(0.25f + skill * 0.55f);
        }

        go.SetActive(true);   // Awake runs now, with aiControlled correct

        if (!ai && withHud)
        {
            var hud     = go.AddComponent<RacingHUD>();
            hud.car     = ctrl;
            hud.gears   = gs;
            hud.shifter = go.GetComponent<GearShifter>();   // added by CarController.Awake
        }
        return go;
    }

    static void BuildCamera(Transform target)
    {
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        camGO.AddComponent<AudioListener>();
        camGO.AddComponent<RacingCamera>().target = target;
    }

    static void BuildLighting()
    {
        var sun = new GameObject("Sun");
        Atmosphere.ApplySun(sun.AddComponent<Light>());
    }
}
