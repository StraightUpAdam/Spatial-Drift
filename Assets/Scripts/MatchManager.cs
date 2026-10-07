using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Splines;

/// Lives in every multiplayer race scene as an in-scene NetworkObject.  When
/// the scene finishes its networked load, the SERVER spawns the grid: one car
/// per connected client (owned by that client, client-authoritative) plus
/// MatchConfig.AICount AI cars (server-owned, server-simulated).  Clients
/// receive all of them as synced ghosts; each player's own car lights up its
/// camera/HUD via NetworkCarSync.
///
/// No NetworkManager in this scene — it persists from the lobby.  The car
/// prefab is assigned in the editor by MultiplayerSetup.
[RequireComponent(typeof(NetworkObject))]
public class MatchManager : NetworkBehaviour
{
    [Tooltip("The NetworkCar prefab to spawn (assigned by the scene builder).")]
    public GameObject carPrefab;

    [Tooltip("Metres between grid rows; cars alternate left/right.")]
    public float gridRowSpacing = 6f;
    public float gridLateral    = 3.5f;
    [Tooltip("First grid slot's distance ahead of the start line.")]
    public float gridStartDistance = 6f;

    // Selected map, serialized — the SERVER publishes it so every client builds
    // the IDENTICAL track at runtime (clients don't have a host's custom-map file).
    readonly NetworkVariable<FixedString4096Bytes> mapJson =
        new NetworkVariable<FixedString4096Bytes>(writePerm: NetworkVariableWritePermission.Server);

    // Host-chosen weather preset (index into Atmosphere.Presets; -1 = unchanged).
    readonly NetworkVariable<int> weather =
        new NetworkVariable<int>(-1, writePerm: NetworkVariableWritePermission.Server);

    bool trackBuilt;

    public override void OnNetworkSpawn()
    {
        mapJson.OnValueChanged += OnMapChanged;

        if (IsServer)
        {
            weather.Value = MatchConfig.WeatherPreset;
            ApplyWeather(weather.Value);

            // Resolve the host's selection (built-in regenerates; custom loads the
            // file) and publish a compact JSON so clients build the same track.
            var data   = MatchConfig.ResolveMap();
            string json = JsonUtility.ToJson(data);
            if (json.Length <= 4000) mapJson.Value = new FixedString4096Bytes(json);
            else Debug.LogWarning($"[MatchManager] Map JSON {json.Length}B exceeds the 4KB " +
                                  "network limit — clients may not receive this custom map.");

            BuildTrackIfNeeded(json);   // server builds locally
            SpawnGrid();
        }
        else ApplyWeather(weather.Value);

        if (!IsServer && mapJson.Value.Length > 0)
        {
            BuildTrackIfNeeded(mapJson.Value.ToString());
        }
    }

    // Dress the world in the host's weather — BEFORE the track builds, since
    // the trackside haze reads the fog colour.  The camera's SceneAmbiance
    // already dressed itself at scene load, so re-dress it live.
    static void ApplyWeather(int preset)
    {
        if (preset < 0 || preset >= Atmosphere.Presets.Length) return;
        Atmosphere.Current = Atmosphere.Presets[preset];
        var amb = FindAnyObjectByType<SceneAmbiance>();
        if (amb != null) amb.Redress();
    }

    public override void OnNetworkDespawn()
    {
        mapJson.OnValueChanged -= OnMapChanged;
    }

    void OnMapChanged(FixedString4096Bytes _, FixedString4096Bytes v)
    {
        if (v.Length > 0) BuildTrackIfNeeded(v.ToString());
    }

    // Build the track once from the synced map JSON.  Cars find it via their
    // CarController track-retry even if it lands a frame after they spawn.
    void BuildTrackIfNeeded(string json)
    {
        if (trackBuilt || string.IsNullOrEmpty(json)) return;
        TrackData data;
        try { data = JsonUtility.FromJson<TrackData>(json); }
        catch { return; }
        if (data == null || data.nodes == null || data.nodes.Count < 3) return;

        // Reflect the actual (synced) map name so clients — who never picked it —
        // key their local time-trial best correctly.
        if (!string.IsNullOrEmpty(data.name)) MatchConfig.MapName = data.name;

        var go = new GameObject("Track");
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

        // Scenery is deterministic from the synced map data — every client
        // builds the identical trackside layers.
        TrackScenery.Build(track, data);

        new GameObject("MiniMap").AddComponent<MiniMap>();

        trackBuilt = true;
    }

    void SpawnGrid()
    {
        if (carPrefab == null)
        {
            Debug.LogError("[MatchManager] carPrefab not assigned — rebuild the MP scene.");
            return;
        }

        int slot = 0;

        // Zone Race: one single-file train in a shuffled order (slot 0 = the front).
        zone      = MatchConfig.GameMode == MatchConfig.Mode.ZoneRace;
        zoneCount = NetworkManager.Singleton.ConnectedClientsIds.Count + Mathf.Clamp(MatchConfig.AIDifficulties.Count, 0, 7);
        slotOrder = new int[zoneCount];
        for (int i = 0; i < zoneCount; i++) slotOrder[i] = i;
        if (zone)
            for (int i = zoneCount - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (slotOrder[i], slotOrder[j]) = (slotOrder[j], slotOrder[i]); }

        // ── Player cars: one per connected client, owned by that client ───────
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            var go = Instantiate(carPrefab);
            GridSlot(slot, out float d, out float l);
            PlaceOnGrid(go.GetComponent<CarController>(), d, l);   // server-side
            go.GetComponent<NetworkObject>().SpawnWithOwnership(clientId);
            go.GetComponent<NetworkCarSync>().ServerSetGridSlot(d, l);  // → client owner
            slot++;
        }

        // ── AI cars: server-owned, server-simulated.  Each AI gets its OWN
        //    difficulty from the per-AI list the host set in the lobby ────────
        var   diffs   = MatchConfig.AIDifficulties;
        int   aiCount = Mathf.Clamp(diffs.Count, 0, 7);

        for (int i = 0; i < aiCount; i++)
        {
            var diff    = diffs[i];
            float skill = MatchConfig.SkillFor(diff);
            int   gearCap = MatchConfig.GearCapFor(diff);

            var go   = Instantiate(carPrefab);
            var ctrl = go.GetComponent<CarController>();

            // Set AI flags BEFORE Spawn so NetworkCarSync.OnNetworkSpawn (which
            // fires during Spawn) sees aiControlled and skips the camera/HUD.
            ctrl.aiControlled = true;
            if (!zone)   // Zone Race AI are piloted by ZoneRaceNet instead
            {
                var ai = go.AddComponent<AIDriver>();
                ai.skill            = Mathf.Clamp01(skill + Random.Range(-0.05f, 0.05f));
                ai.gearCap          = gearCap;
                ai.preferredLateral = Random.Range(-2.5f, 2.5f);
                ai.driftAffinity    = Mathf.Clamp01(0.25f + skill * 0.55f);  // better AI drift more
            }

            GridSlot(slot, out float d, out float l);
            PlaceOnGrid(ctrl, d, l);
            go.GetComponent<NetworkObject>().Spawn();   // server ownership

            var sync = go.GetComponent<NetworkCarSync>();
            sync.ServerSetGridSlot(d, l);
            sync.ServerSetColorId(PlayerProfile.AIColorId(i));
            sync.ServerSetName(PlayerProfile.AIName(i));
            slot++;
        }
    }

    bool  zone;
    int   zoneCount;
    int[] slotOrder;

    void GridSlot(int slot, out float distance, out float lateral)
    {
        if (zone && slotOrder != null && slot < slotOrder.Length)
        {
            var t = SpatialDriftTuning.Shared;
            int place = slotOrder[slot];                        // 0 = front of the train
            distance = (zoneCount - 1 - place) * t.zoneGapLength;
            lateral  = (place % 2 == 0 ? -0.5f : 0.5f) * Mathf.Max(1f, t.railsLaneSpacing);
            return;
        }
        // Staggered 2-wide grid marching forward from the line.
        distance = gridStartDistance + slot * gridRowSpacing;
        lateral  = (slot % 2 == 0) ? -gridLateral : gridLateral;
    }

    static void PlaceOnGrid(CarController ctrl, float distance, float lateral)
    {
        if (ctrl == null) return;
        ctrl.startDistance      = distance;
        ctrl.startLateralOffset = lateral;
    }
}
