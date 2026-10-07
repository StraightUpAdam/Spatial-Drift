using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// One-click builder for the map-editor scenes, mirroring SceneSetup /
/// MultiplayerSetup.  Produces:
///   • Assets/Scenes/MapEditor.unity — the node-drag editor (MapEditor builds
///     its own Track at runtime; the scene just needs a camera + light + ground).
///   • Assets/Scenes/MapTest.unity   — the free-drive test scene (MapTestRunner
///     builds the track + car from the editor's scratch save at runtime).
/// Both are registered in Build Settings so the menu button + Test Drive work.
public static class MapEditorSetup
{
    const string EditorPath = "Assets/Scenes/MapEditor.unity";
    const string TestPath   = "Assets/Scenes/MapTest.unity";

    [MenuItem("Map Editor/Build Map Editor Scenes")]
    static void BuildScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        BuildEditorScene();
        BuildTestScene();
        RegisterScenes(EditorPath, TestPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene(EditorPath);
        Debug.Log("[MapEditor] Built MapEditor + MapTest and registered them in Build Settings. " +
                  "Open MapEditor and press Play — or use the MAP EDITOR button on the main menu.");
    }

    static void BuildEditorScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera — MapEditor drives this transform directly each frame.
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.03f, 0.10f);
        cam.fieldOfView     = 60f;
        camGO.AddComponent<AudioListener>();

        // Light.
        var sun = new GameObject("Sun");
        var l   = sun.AddComponent<Light>();
        l.type      = LightType.Directional;
        l.intensity = 1.2f;
        l.color     = new Color(1f, 0.96f, 0.88f);
        sun.transform.rotation = Quaternion.Euler(48f, 50f, 0f);

        // Ground reference (no collider — so node picking only hits handles).
        var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
        g.name = "GroundReference";
        g.transform.localScale = new Vector3(120f, 1f, 120f);
        g.transform.position   = new Vector3(0f, 0f, 120f);
        Object.DestroyImmediate(g.GetComponent<Collider>());
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        g.GetComponent<Renderer>().sharedMaterial =
            new Material(sh) { color = new Color(0.10f, 0.11f, 0.14f) };

        // The editor controller + its baked inspector panel.
        new GameObject("MapEditor").AddComponent<MapEditor>();
        MenuScenesExtra.BakeMapEditorPanel();
        MenuBaker.EventSystem();

        SaveScene(EditorPath);
    }

    static void BuildTestScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Everything (track, car, camera, light) is built at runtime from the
        // scratch save — the scene just needs the runner.
        new GameObject("MapTestRunner").AddComponent<MapTestRunner>();
        MenuScenes.BakeRaceMenus();      // pause: resume / reset car / settings / back to editor
        MenuBaker.EventSystem();
        SaveScene(TestPath);
    }

    static void SaveScene(string path)
    {
        Directory.CreateDirectory(Application.dataPath + "/Scenes");
        EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(), path);
    }

    static void RegisterScenes(params string[] paths)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);
        foreach (var p in paths)
            if (!scenes.Exists(s => s.path == p))
                scenes.Add(new EditorBuildSettingsScene(p, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
