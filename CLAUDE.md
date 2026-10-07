# CLAUDE.md — Spatial Drift

Working guide for Claude Code in this repo.  Detailed history lives in Claude's
auto-memory for this folder (`project_spatial_drift.md`); this file is the
condensed "how the game works + how to work on it".

## What it is

**Spatial Drift** — Adam's (CUBED Pluto) multiplayer arcade racer: F-Zero GX ×
anime *Redline*.  **Unity 6000.4.7f1, URP 17.4**, Splines, Netcode for
GameObjects 2.x + Unity Relay.  **No git repo** — be careful with destructive
edits; back up anything you overwrite outside generated files.

Normal world = "real life" outdoors (Day/Sunset/Night/Storm weather).  Purple is
reserved for the **drift zone** identity.

## Core model (read before touching driving code)

- Cars are spline-locked: `(DistanceAlongTrack, LateralOffset, worldForward, Speed)`
  on a `Track` (SplineContainer wrapper, `Sample(dist, lateral, out pos, out rot,
  out fwd, out up)`, `HalfWidthAt`, `WrapDistance`).  Kinematic Rigidbody
  (MovePosition/MoveRotation).  Loops, corkscrews and barrel rolls just work.
- `CarController.Awake` **auto-adds** almost everything: CarVisual (procedural
  body under `CarVisual.Root`), HoverBody, Headlights, SkidMarks, BoostTrails,
  BoostEffects, DriftFx, SlipstreamTrail, GearShifter.  Player-only systems
  (`SpatialDriftMode`, `CarAudio`) come from `EnablePlayerSystems()` (deferred
  for network cars so only the local owner gets them).
- **Input seam:** humans, `AIDriver` and network code all write
  `CarController.LocalInput`.
- 10-gear `GearSystem` (RPM boost zones, external boosts) + grid `GearShifter`
  (walls between cells; custom gates from the Shifter Lab via `ShifterLayout`).
- Drift: brake + steer flick at speed; style **Hop** (MK8-like, default) or
  Slide.  `DriftCharge` → release boost.
- Tuning lives in ScriptableObjects (components are runtime-added, so not
  inspector-editable): `Resources/CarTuning`, `SpatialDriftTuning`, `VfxTuning`.
  In-game dev panels: **F8** atmosphere, **F9** drift-zone tuning, **F10** drift
  feel (time trial) — they save JSON overrides in `persistentDataPath`.

## Maps, scenes, modes

- Maps are **data** (`TrackData` JSON), never baked into scenes.  `MapCatalog`
  = built-ins (Speed Circuit, Drift Circuit) + user maps
  (`persistentDataPath/Maps`).  `MatchConfig` carries the selection
  (`MapName/MapBuiltIn/MapJson`, `GameMode`, `Laps`, AI list, weather).
- **Canonical scenes (9):** MainMenu, MapSelect, Race, Lobby, MP_Race,
  MapEditor, MapTest, Options, ShifterLab.  Solo `Race` is built at runtime by
  `RaceBootstrap`; MP `MP_Race` by `MatchManager` (map JSON sent over a
  NetworkVariable).
- **Scenes are GENERATED** — never hand-edit them; regenerate with the menus:
  `Racing Game ▸ Build All Scenes (Menu + Maps)`,
  `Multiplayer ▸ Build Full Multiplayer (Lobby + Maps)`,
  `Racing Game ▸ Fix Build Settings`, and **`Racing Game ▸ Fix Build Shaders
  (Always Include)` before any player build** (runtime `Shader.Find` shaders get
  stripped otherwise).  Menu UI is baked by `Editor/MenuBaker.cs`,
  `Editor/MenuScenes.cs`, `Editor/MenuScenesExtra.cs`.
- Modes (`MatchConfig.Mode`): **Race**, **TimeTrial** (standing start, PB
  ghost), **ZoneRace** (whole race single-file inside the drift zone — rules in
  `Zone/ZoneTrain.cs`; solo `ZoneRaceSolo`, online `ZoneRaceNet`).
- Multiplayer is **client-authoritative** (owner publishes pose; server owns
  laps/finish) via `NetworkCarSync`, `NetworkRaceDirector`, `MatchManager`;
  lobby = `LobbyController` (session) + `LobbyMenu` (UI) over Relay.

## Systems map (Assets/Scripts)

| Area | Key files |
|---|---|
| Driving | CarController, GearSystem, GearShifter, AIDriver, CarTuning, SlipstreamTrail, CarBumpSystem / NetworkBumpSystem |
| Spatial Drift (namesake) | SpatialDriftMode (Idle → **Entering** → Active), SpatialRift (tear-in-space portal), CarGhost (ghost form), SpatialDriftTuning; minigame = **Rails** (default) or Rhythm |
| Race flow | RaceManager, RaceBootstrap, TrackRecords (BEST + PURE lap, wiped when a track's shape changes), TimeTrialGhost, NetworkTimeTrial |
| World | Track, TrackData, TrackMeshBuilder, TrackScenery (parallax walls, skirt, **heightfield terrain**, base plane), SceneAmbiance, Atmosphere, CloudLayer |
| FX | DriftFx (tiered sparks/smoke), SkidMarks → TrackRubber (persistent rubber build-up), BoostEffects, BoostTrails, WarpEffect (`Pulse()`), RacingCamera (`Kick()`), FxMaterials |
| Audio | CarAudio (all synthesized in code), UI/MenuAudio, UI/MenuMusic |
| HUD | RacingHUD (runtime-built), Zone/ZoneRaceHUD, MiniMap, ModeHud |
| Menus | `UI/` kit: UITheme, MenuInput (`Consume()` once a press is used), MenuScreen, MenuRow/Button/Stepper/Slider, MenuModal, PromptBar, SceneFader, GamePause; screens: MainMenuCarousel, SinglePlayerMenu, LobbyMenu, OptionsMenu, ShifterDesigner, RaceMenus, MPRaceMenus, MapEditorPanel |
| Shaders (`Assets/Shaders`) | VertexColorLit, CloudPlane, RailLine, Rift, Ghost, Rubber — all must be in Fix Build Shaders' list |

## How to verify changes

1. **Offline compile check (fast, editor open or not):**
   `bash Tools/Verify/check.sh` → prints errors or `Build succeeded.`
   (re-run `powershell -File Tools/Verify/make.ps1` after package/Unity changes).
2. **Live editor via MCP for Unity** (`UnityMCP` server, stdio; in Unity:
   Window ▸ MCP for Unity ▸ Transport **Stdio** ▸ **Start Session**).  Useful:
   `execute_code` (C# in the editor, play-mode introspection, reflection on
   private methods), `refresh_unity` (compile), `manage_editor` play/stop/pause,
   `manage_camera` screenshot (pass `output_folder: Temp/...` — the default
   writes into `Assets/Screenshots`), `read_console`.
   - When Unity isn't focused, play mode barely ticks: run once per play session
     `Application.runInBackground = true;` and
     `EditorApplication.update += () => { if (EditorApplication.isPlaying && !EditorApplication.isPaused) EditorApplication.QueuePlayerLoopUpdate(); };`
   - Capture exact moments with an `EditorApplication.update` watcher that sets
     `EditorApplication.isPaused = true` on a condition, then screenshot.
   - To make the player car drive itself: add `AIDriver` + `car.aiControlled = true`.
   - Unity's *built-in* AI Assistant MCP (`unity-mcp`) is plan-gated on Adam's
     account and always reports "Connection revoked" — don't try to fix/bypass it.
3. **Headless batch mode** (only when the editor is closed — or on a robocopy'd
   copy of the project): `"C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe" -batchmode -quit -projectPath "<proj>" -executeMethod Class.Method -logFile <log>`.
4. Logs: editor `%LOCALAPPDATA%\Unity\Editor\Editor.log`; builds
   `%USERPROFILE%\AppData\LocalLow\CUBED Pluto\Spatial Drift\Player.log`.

## Gotchas that have bitten before

- MonoBehaviours baked into scenes must live in a file named after the class.
- Play mode doesn't fire `sceneLoaded` for the scene already open in the editor.
- `GetComponent<T>() ?? x` is wrong for Unity objects (fake null) — use explicit null checks.
- Linear colour space: very low alphas are still visible.
- Runtime meshes/materials aren't freed with their GameObjects — Destroy them.
- Per-vertex fog: huge triangles (ground planes) need tessellation near the camera.
- `MenuRow` not "MenuItem" (clashes with `UnityEditor.MenuItem`).
- RPC method names must end in `Rpc` (NGO).
- Don't overwrite the player's saved files during tests
  (`persistentDataPath`: shifter_layout.json, Maps/, Ghosts/, *_tuning.json) —
  back up and restore byte-for-byte if a test needs them.

## Not yet verified / open

- Two-machine online play for: Zone Race, the ghost/rift sync
  (`NetworkCarSync.InSpatialDrift`), lobby/results flows.
- New synthesized sounds (rift tear/cross/exit, spark tier "ching") not heard by ear.
- Gameplay feel pass still to do: handling, camera, AI racecraft, collisions, boost.
