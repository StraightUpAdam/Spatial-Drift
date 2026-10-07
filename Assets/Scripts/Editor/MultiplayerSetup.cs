using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.IO;

/// One-click multiplayer test setup, mirroring how SceneSetup builds the
/// single-player scenes.  Produces:
///   • Assets/Prefabs/NetworkCar.prefab — the spawnable player car (NetworkObject
///     + NetworkCarSync + the usual gameplay components, with player-only systems
///     deferred so only the local owner gets HUD/audio/Spatial Drift).
///   • Assets/Scenes/MultiplayerTest.unity — a clean track scene with a
///     NetworkManager (Unity Transport + NetworkBootstrap) and that prefab wired
///     in as the Player Prefab.
public static class MultiplayerSetup
{
    const string PrefabDir   = "Assets/Prefabs";
    const string PrefabPath  = PrefabDir + "/NetworkCar.prefab";
    const string MatPath     = PrefabDir + "/NetworkCarBody.mat";
    const string LobbyPath   = "Assets/Scenes/Lobby.unity";
    const string MPRacePath  = "Assets/Scenes/MP_Race.unity";   // one data-driven MP race scene

    [MenuItem("Multiplayer/Build Full Multiplayer (Lobby + Maps)")]
    static void BuildFullMultiplayer()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // Always rebuild the prefab so component changes on the network car are
        // picked up (this used to need a separate menu step).
        var prefab = BuildNetworkCarPrefab();

        BuildRaceSceneCore(MPRacePath, prefab);   // ONE scene; map is data, built at runtime
        BuildLobbySceneCore();

        // Build settings: Lobby first so the menu's MULTIPLAYER button has a target,
        // then the single data-driven race scene every map loads into.
        SetBuildScenes(LobbyPath, MPRacePath);

        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(LobbyPath);
        Debug.Log("[Multiplayer] Built Lobby + MP_Race (data-driven — the lobby picks any map, " +
                  "built-in or custom, and MatchManager syncs it to clients). MP_Speed/MP_Drift are no " +
                  "longer used; you can delete them. Open Lobby, Play, Host; join from a virtual player.");
    }

    // (No standalone menu — "Build Full Multiplayer" always rebuilds this.)
    public static GameObject BuildNetworkCarPrefab()
    {
        Directory.CreateDirectory(Application.dataPath + "/Prefabs");

        var car = new GameObject("NetworkCar");

        // Visual body
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(car.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        body.transform.localScale    = new Vector3(1.8f, 0.6f, 3.6f);
        Object.DestroyImmediate(body.GetComponent<Collider>());

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(shader) { color = new Color(0.30f, 0.55f, 1f) };
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        body.GetComponent<Renderer>().sharedMaterial = mat;

        // Physics
        var col   = car.AddComponent<BoxCollider>();
        col.size  = new Vector3(1.8f, 0.6f, 3.6f);
        col.center = new Vector3(0f, 0.3f, 0f);

        var rb           = car.AddComponent<Rigidbody>();
        rb.mass          = 1200f;
        rb.isKinematic   = true;
        rb.useGravity    = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Gameplay — player-only systems deferred until owner spawn.
        var gears = car.AddComponent<GearSystem>();
        var ctrl  = car.AddComponent<CarController>();
        ctrl.gearSystem                = gears;
        ctrl.networkDeferPlayerSystems = true;

        var shifter = car.AddComponent<GearShifter>();
        shifter.gearSystem = gears;

        // Networking
        car.AddComponent<NetworkObject>();
        car.AddComponent<NetworkCarSync>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(car, PrefabPath);
        Object.DestroyImmediate(car);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Multiplayer] Built network car prefab at {PrefabPath}.");
        return prefab;
    }

    // ── Lobby + MP race scene builders ───────────────────────────────────────
    static void BuildLobbySceneCore()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // The baked lobby UI (camera + sky backdrop, connect / room pages).
        MenuScenesExtra.BakeLobbyUI();

        // The persistent NetworkManager lives here (carries into race scenes).
        var nmGO = new GameObject("NetworkManager");
        var nm   = nmGO.AddComponent<NetworkManager>();
        var utp  = nmGO.AddComponent<UnityTransport>();
        nmGO.AddComponent<LobbyController>();

        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport      = utp;
        nm.NetworkConfig.PlayerPrefab          = null;   // MatchManager spawns cars manually
        nm.NetworkConfig.EnableSceneManagement = true;

        Directory.CreateDirectory(Application.dataPath + "/Scenes");
        EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(), LobbyPath);
    }

    static void BuildRaceSceneCore(string scenePath, GameObject carPrefab)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        SceneSetup.BuildLighting();
        // (No baked ground slab — TrackScenery's grass base plane is the ground now.)

        // No baked track — MatchManager builds the lobby-selected map at runtime.

        // Camera — NetworkCarSync points it at the local player's car on spawn.
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        camGO.AddComponent<AudioListener>();
        var camData = cam.GetUniversalAdditionalCameraData();
        if (camData != null) camData.renderPostProcessing = true;
        camGO.AddComponent<RacingCamera>().localOffset = new Vector3(0f, 3.5f, -8f);

        // RaceSystems: ONE in-scene NetworkObject hosting both NetworkBehaviours.
        // (Two separate in-scene NetworkObjects created via script both get
        // GlobalObjectIdHash 0 and collide — one object avoids that entirely.)
        var sysGO = new GameObject("RaceSystems");
        sysGO.AddComponent<NetworkObject>();
        var mm = sysGO.AddComponent<MatchManager>();
        mm.carPrefab = carPrefab;
        sysGO.AddComponent<NetworkRaceDirector>();
        sysGO.AddComponent<ZoneRaceNet>();          // idles unless the match is a Zone Race

        // Baked pause + results + next-race menus.
        MenuScenes.BakeMPRaceMenus();
        MenuBaker.EventSystem();

        // NOTE: no NetworkManager here — it persists from the Lobby scene.

        Directory.CreateDirectory(Application.dataPath + "/Scenes");
        EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scenePath);
    }

    static void SetBuildScenes(params string[] paths)
    {
        // Merge with whatever's already registered (keeps the solo + editor scenes),
        // but drop the deprecated per-map MP scenes.
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == "Assets/Scenes/MP_Speed.unity"
                           || s.path == "Assets/Scenes/MP_Drift.unity");
        foreach (var p in paths)
            if (!scenes.Exists(s => s.path == p))
                scenes.Add(new EditorBuildSettingsScene(p, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
