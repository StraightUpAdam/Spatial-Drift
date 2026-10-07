using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Mathematics;
using System.Collections.Generic;
using System.IO;
using TMPro;

/// The project's scene/asset factory menus.  Everything is data-driven now
/// (maps are TrackData JSON; tracks, grids and scenery build at runtime), so
/// the menu set is small:
///   Racing Game ▸ Build All Scenes         — MainMenu/MapSelect/Race/Options/ShifterLab
///   Racing Game ▸ Fix Build Settings       — reset the canonical 9-scene list
///   Racing Game ▸ Fix Build Shaders        — run once before making a BUILD
///   Racing Game ▸ Create/Capture … Tuning  — the ScriptableObject assets
/// (Legacy per-map scene builders, track scaling and spline-export menus were
/// removed once maps became data — the map editor covers all of it.)
public static class SceneSetup
{
    // ──────────────────────────────────────────────────────────────────────────
    //  SCENE BUILDERS
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("Racing Game/Build All Scenes (Menu + Maps)")]
    static void BuildAllScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        MenuScenes.BuildMainMenu("Assets/Scenes/MainMenu.unity");
        MenuScenes.BuildSinglePlayer("Assets/Scenes/MapSelect.unity");
        BuildRaceSceneCore();
        MenuScenes.BuildOptions("Assets/Scenes/Options.unity");
        MenuScenes.BuildShifterLab("Assets/Scenes/ShifterLab.unity");
        RegisterScenesInBuildSettings();

        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        Debug.Log("[RacingGame] Built MainMenu + MapSelect (race setup) + Race (pause/results) + " +
                  "Options + ShifterLab.  Also run 'Multiplayer / Build Full Multiplayer' and " +
                  "'Map Editor / Build Map Editor Scenes' so the lobby, online race and editor get the new menus.");
    }

    // Race — one scene; RaceBootstrap builds the selected map + grid at runtime.
    // The pause + results menus are BAKED into it.
    // (No baked ground slab — TrackScenery's grass base plane is the ground now.)
    static void BuildRaceSceneCore()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        new GameObject("RaceBootstrap").AddComponent<RaceBootstrap>();
        MenuScenes.BakeRaceMenus();
        MenuBaker.EventSystem();

        MenuBaker.SaveScene("Assets/Scenes/Race.unity");
    }

    static void RegisterScenesInBuildSettings()
    {
        const string menu    = "Assets/Scenes/MainMenu.unity";
        const string maps    = "Assets/Scenes/MapSelect.unity";
        const string race    = "Assets/Scenes/Race.unity";
        const string options = "Assets/Scenes/Options.unity";
        const string lab     = "Assets/Scenes/ShifterLab.unity";
        const string drift   = "Assets/Scenes/DriftTest.unity";
        const string speed   = "Assets/Scenes/SpeedTest.unity";

        // Preserve whatever's already registered (Lobby / MP / Map Editor scenes),
        // drop the deprecated per-map test scenes, and ensure ours are present.
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == drift || s.path == speed);

        void Ensure(string p)
        {
            if (File.Exists(p) && !list.Exists(s => s.path == p))
                list.Add(new EditorBuildSettingsScene(p, true));
        }
        Ensure(menu); Ensure(maps); Ensure(race); Ensure(options); Ensure(lab);

        // MainMenu must be index 0 so the build boots into it.
        int mi = list.FindIndex(s => s.path == menu);
        if (mi > 0) { var m = list[mi]; list.RemoveAt(mi); list.Insert(0, m); }

        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log("[RacingGame] Build Settings: MainMenu (0) + MapSelect + Race added; " +
                  "DriftTest/SpeedTest removed; existing MP/editor scenes kept.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  FIX BUILD SETTINGS — reset the registered scene list to EXACTLY the
    //  canonical seven, wherever they live in the project, MainMenu first.  Does
    //  NOT delete any files — it just unregisters the stray duplicate scenes so
    //  the game stops being confused about which scene is which.
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("Racing Game/Fix Build Settings (Canonical Scenes)")]
    static void FixBuildSettings()
    {
        // Order matters: MainMenu must be index 0 (the build boots into it).
        string[] names = { "MainMenu", "MapSelect", "Race", "Lobby", "MP_Race",
                           "MapEditor", "MapTest", "Options", "ShifterLab" };

        var list    = new System.Collections.Generic.List<EditorBuildSettingsScene>();
        var missing = new System.Collections.Generic.List<string>();

        foreach (var n in names)
        {
            string path = null;
            foreach (var guid in AssetDatabase.FindAssets($"t:Scene {n}"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == n) { path = p; break; }
            }
            if (path != null) list.Add(new EditorBuildSettingsScene(path, true));
            else              missing.Add(n);
        }

        EditorBuildSettings.scenes = list.ToArray();

        string msg = $"[RacingGame] Build Settings reset to {list.Count} canonical scene(s), MainMenu first. " +
                     "Stray/duplicate scenes were unregistered (no files deleted).";
        if (missing.Count > 0)
            msg += $"  NOT FOUND (build them first): {string.Join(", ", missing)} — " +
                   "run 'Build All Scenes' and 'Multiplayer / Build Full Multiplayer'.";
        Debug.Log(msg);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  FIX BUILD SHADERS
    //  The track mesh, skid marks, trails, slipstream and speed lines create
    //  materials at RUNTIME via Shader.Find(...).  Unity strips any shader no
    //  built material references, so those Shader.Find calls return NULL in a
    //  build → "new Material(null)" throws → the race scene dies on load.
    //  Forcing them into Always-Included Shaders keeps them in the build.
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("Racing Game/Fix Build Shaders (Always Include)")]
    static void FixBuildShaders()
    {
        string[] names =
        {
            "SpatialDrift/VertexColorLit",     // lit vertex-colour world (shadows)
            "SpatialDrift/CloudPlane",         // volumetric cloud raymarcher
            "SpatialDrift/RailLine",           // drift-zone rail guides (x-ray pass)
            "SpatialDrift/Rift",               // the tear into / out of Spatial Drift
            "SpatialDrift/Ghost",              // the car's ghost form in the zone
            "SpatialDrift/Rubber",             // laid-down rubber on the racing line
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Particles/Unlit",
            "Sprites/Default",
            "Particles/Standard Unlit",
            "Unlit/Color",
            "Standard",
        };

        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (assets == null || assets.Length == 0)
        { Debug.LogError("[RacingGame] Could not load GraphicsSettings.asset."); return; }

        var so   = new SerializedObject(assets[0]);
        var list = so.FindProperty("m_AlwaysIncludedShaders");
        if (list == null)
        { Debug.LogError("[RacingGame] m_AlwaysIncludedShaders property not found."); return; }

        int added = 0;
        var notFound = new System.Collections.Generic.List<string>();
        foreach (var n in names)
        {
            var shader = Shader.Find(n);
            if (shader == null) { notFound.Add(n); continue; }

            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
            if (present) continue;

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            added++;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(assets[0]);
        AssetDatabase.SaveAssets();

        string msg = $"[RacingGame] Always-Included Shaders: added {added}. This fixes the build " +
                     "freeze (runtime shaders were being stripped).  REBUILD now.";
        if (notFound.Count > 0) msg += "  (not in this project, skipped: " + string.Join(", ", notFound) + ")";
        Debug.Log(msg);
    }

    // Editor-time sun for scenes baked by other builders (Lobby / MP_Race).
    // Uses the same Atmosphere profile as the runtime-built scenes.
    public static void BuildLighting() =>
        Atmosphere.ApplySun(new GameObject("Sun").AddComponent<Light>());

    // ──────────────────────────────────────────────────────────────────────────
    //  CREATE SPATIAL DRIFT TUNING ASSET
    //  Drops a SpatialDriftTuning ScriptableObject into Resources/ so the
    //  SpatialDriftMode component can Resources.Load it at startup.  Inspector
    //  edits to this asset persist across Play sessions (the whole reason this
    //  exists — Inspector edits on auto-added components revert when Play ends).
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("Racing Game/Create Spatial Drift Tuning Asset")]
    static void CreateSpatialDriftTuningAsset()
    {
        const string dir  = "Assets/Resources";
        const string path = dir + "/SpatialDriftTuning.asset";

        Directory.CreateDirectory(dir);

        var existing = AssetDatabase.LoadAssetAtPath<SpatialDriftTuning>(path);
        if (existing != null)
        {
            EditorGUIUtility.PingObject(existing);
            Selection.activeObject = existing;
            Debug.Log($"[RacingGame] SpatialDriftTuning asset already exists at {path}.");
            return;
        }

        var asset = ScriptableObject.CreateInstance<SpatialDriftTuning>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(asset);
        Selection.activeObject = asset;
        Debug.Log($"[RacingGame] Created {path}. Edit values here — they persist across Play sessions.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  CAR TUNING ASSET
    //  Create the global car-tuning asset, and capture the values off the car
    //  in the currently-open scene (so you can tune the DriftTest car you like,
    //  then bake those numbers in as the across-the-board default).
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("Racing Game/Create Car Tuning Asset")]
    static void CreateCarTuningAsset()
    {
        const string dir  = "Assets/Resources";
        const string path = dir + "/CarTuning.asset";

        Directory.CreateDirectory(dir);

        var existing = AssetDatabase.LoadAssetAtPath<CarTuning>(path);
        if (existing != null)
        {
            EditorGUIUtility.PingObject(existing);
            Selection.activeObject = existing;
            Debug.Log($"[RacingGame] CarTuning asset already exists at {path}. " +
                      "Edit it directly, or use 'Capture Car Tuning From Current Scene'.");
            return;
        }

        var asset = ScriptableObject.CreateInstance<CarTuning>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(asset);
        Selection.activeObject = asset;
        Debug.Log($"[RacingGame] Created {path}. Every car now loads it on Awake. " +
                  "Tune it here (persists across Play sessions), or capture from a scene.");
    }

    [MenuItem("Racing Game/Capture Car Tuning From Current Scene")]
    static void CaptureCarTuningFromScene()
    {
        // Prefer the explicit local player; otherwise the first non-AI car.
        CarController car = null;
        var race = Object.FindFirstObjectByType<RaceManager>();
        if (race != null && race.localPlayer != null) car = race.localPlayer;
        if (car == null)
        {
            foreach (var c in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (!c.aiControlled) { car = c; break; }
        }
        if (car == null)
        {
            Debug.LogError("[RacingGame] No player CarController in the open scene to capture from.");
            return;
        }

        const string dir  = "Assets/Resources";
        const string path = dir + "/CarTuning.asset";
        Directory.CreateDirectory(dir);

        var asset = AssetDatabase.LoadAssetAtPath<CarTuning>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<CarTuning>();
            AssetDatabase.CreateAsset(asset, path);
        }

        asset.CaptureFrom(car);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(asset);
        Selection.activeObject = asset;
        Debug.Log($"[RacingGame] Captured driving + gear values from '{car.name}' into {path}. " +
                  "These are now the default for every car in every scene.");
    }

    [MenuItem("Racing Game/Create VFX Tuning Asset")]
    static void CreateVfxTuningAsset()
    {
        const string dir  = "Assets/Resources";
        const string path = dir + "/VfxTuning.asset";

        Directory.CreateDirectory(dir);

        var existing = AssetDatabase.LoadAssetAtPath<VfxTuning>(path);
        if (existing != null)
        {
            EditorGUIUtility.PingObject(existing);
            Selection.activeObject = existing;
            Debug.Log($"[RacingGame] VfxTuning asset already exists at {path}.");
            return;
        }

        var asset = ScriptableObject.CreateInstance<VfxTuning>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(asset);
        Selection.activeObject = asset;
        Debug.Log($"[RacingGame] Created {path}. Edit the WarpEffect / SpeedLines / Boost / Hover / " +
                  "Skid / BoostTrail values here — they persist across Play sessions.");
    }

}
