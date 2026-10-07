using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

/// Drives the "MapTest" scene: builds a real Track from the editor's scratch
/// TrackData, spawns the player's car on it, and lets you free-drive to feel
/// the layout.  Reuses the exact gameplay stack (CarController/GearSystem/
/// RacingCamera/HUD) so a test drive plays like the real game — including
/// whatever CarTuning you've set.
///
/// Exit:   Esc  or gamepad Start  → back to the editor.
/// Cheat:  T    or gamepad Back   → snap to gear 9 at its top speed (so you can
///         feel fast corners without a full lap of build-up).
[DisallowMultipleComponent]
public class MapTestRunner : MonoBehaviour
{
    public string editorScene = "MapEditor";

    CarController car;
    GearSystem    gears;

    void Start()
    {
        var data = TrackData.Load(TrackData.ScratchName) ?? TrackData.Template();

        BuildLighting();
        var track = BuildTrack(data);
        var carGO = BuildCar(track);
        BuildCamera(carGO.transform);

        new GameObject("MiniMap").AddComponent<MiniMap>();
    }

    void Update()
    {
        // Exit lives in the pause menu (Esc / Start → BACK TO EDITOR).  L3 is kept
        // as a quick exit since Start is flaky on some pads.
        if (!GamePause.InputBlocked && Input.GetKeyDown(KeyCode.JoystickButton8))
            BackToEditor();

        // Gear-9 cheat: T, Back, or R3 (right-stick click — reliable).
        if (!GamePause.InputBlocked
            && (Input.GetKeyDown(KeyCode.T)
                || Input.GetKeyDown(KeyCode.JoystickButton6)       // Back / View
                || Input.GetKeyDown(KeyCode.JoystickButton9)))     // R3
            BoostToGearNine();
    }

    public void BackToEditor() => SceneFader.Load(editorScene);

    /// Pause menu "Reset car": back on the line, gears reset.
    public void ResetCar()
    {
        if (car == null) return;
        car.ResetToStart();
        if (gears != null) gears.ResetForRace();
        var sh = car.GetComponent<GearShifter>();
        if (sh != null) sh.ResetToStart();
    }

    // Slam the car into gear 9 at gear 9's top speed.
    void BoostToGearNine()
    {
        if (car == null || gears == null) return;
        var shifter = car.GetComponent<GearShifter>();
        if (shifter != null) shifter.AutoShiftTo(9);   // keeps the shift grid in sync too
        else                 gears.SetGear(9, 0f);
        if (gears.gearMaxSpeeds != null && gears.gearMaxSpeeds.Length >= 9)
            car.Speed = gears.gearMaxSpeeds[8];          // gear 9 = index 8
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

        TrackScenery.Build(track, data);   // same scenery you placed in the editor

        return track;
    }

    GameObject BuildCar(Track track)
    {
        var carGO = new GameObject("Car (Player)");

        // Legacy "Body" cube — CarVisual reads its colour, then replaces it with
        // the procedural model.  Tint it with the player's chosen paint.
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(carGO.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        body.transform.localScale    = new Vector3(1.8f, 0.6f, 3.6f);
        Destroy(body.GetComponent<Collider>());
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        body.GetComponent<Renderer>().sharedMaterial =
            new Material(sh) { color = PlayerProfile.LocalColor };

        var rb           = carGO.AddComponent<Rigidbody>();
        rb.isKinematic   = true;
        rb.useGravity    = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // GearSystem before CarController so it wires up on Awake (and the
        // controller auto-adds the GearShifter + visuals + player systems).
        var gs   = carGO.AddComponent<GearSystem>();
        var ctrl = carGO.AddComponent<CarController>();
        ctrl.gearSystem         = gs;
        ctrl.track              = track;
        ctrl.startDistance      = 0f;
        ctrl.startLateralOffset = 0f;

        var hud     = carGO.AddComponent<RacingHUD>();
        hud.car     = ctrl;
        hud.gears   = gs;
        hud.shifter = carGO.GetComponent<GearShifter>();

        car   = ctrl;
        gears = gs;
        return carGO;
    }

    static void BuildCamera(Transform target)
    {
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        camGO.AddComponent<AudioListener>();
        var follow    = camGO.AddComponent<RacingCamera>();
        follow.target = target;
    }

    static void BuildLighting()
    {
        var sun = new GameObject("Sun");
        Atmosphere.ApplySun(sun.AddComponent<Light>());
    }
}
