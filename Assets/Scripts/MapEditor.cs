using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

/// In-game node-drag track editor.  You edit a TrackData (a ring of control
/// nodes); it's pushed live into a real SplineContainer so the existing
/// TrackMeshBuilder renders exactly what you'll drive.
///
/// Mouse:  left-click a node, drag to move it; the left panel sets the selected
/// node's X/Y/Z (grid-snapped), width, and bank.  Right-drag / WASD pan, scroll
/// zoom, Q/E orbit, R/F tilt.
///
/// Controller (uses the same axes as driving — run "Racing Game ▸ Configure
/// Gamepad Input" once):
///   Right stick  move selected node      RT/LT  raise/lower
///   X / Y        bank − / +              LB/RB  previous/next node
///   A add node   B delete node           Left stick X orbit
///   Start test-drive   Back to menu      (camera auto-frames the selected node)
///
/// Build the scenes with "Map Editor ▸ Build Map Editor Scenes".
[DisallowMultipleComponent]
public class MapEditor : MonoBehaviour
{
    public string returnMenuScene = "MainMenu";
    public string testDriveScene  = "MapTest";

    // Scene wiring (resolved / created at runtime)
    Track            track;
    TrackMeshBuilder meshBuilder;
    Camera           cam;

    // Document
    TrackData data;
    int       selected = -1;
    bool      draggingNode;
    bool      wasManipulating;   // true last frame → snap to grid on release

    // Node handles
    readonly List<GameObject> handles = new List<GameObject>();
    Material handleMat, handleSelMat;

    // Build camera
    Vector3 camTarget;
    float   camDist = 240f, camYaw = 0f, camPitch = 58f;

    // Grid
    float gridSize = 5f;
    bool  snap     = true;
    bool  showGrid = true;
    GameObject gridGO;
    Mesh       gridMesh;
    GameObject stemGO;       // node-elevation stems (ground plane → node)
    Mesh       stemMesh;
    GameObject sceneryGO;    // trackside 2D scenery preview
    float _gCx = float.NaN, _gCz, _gGs, _gY;     // last-built grid key
    static readonly Color GridColor = new Color(0.45f, 0.55f, 0.72f, 0.5f);

    // Undo — one snapshot per gesture (mouse press or pad edit), taken pre-edit.
    readonly List<TrackData> undo = new List<TrackData>();
    const int UndoCap = 60;
    bool _holdRecorded;   // mouse-hold coalescing (drag / click / slider)
    bool _padRecorded;    // gamepad-gesture coalescing

    // Panel state (the baked MapEditorPanel reads these)
    string       mapName = "MyTrack";
    string       message = "Drag nodes to shape the track.";

    static HashSet<string> _badAxes;

    void Start()
    {
        cam = Camera.main;
        EnsureTrack();
        BuildHandleMaterials();

        data      = TrackData.Load(TrackData.ScratchName) ?? TrackData.Template();
        mapName   = string.IsNullOrEmpty(data.name) || data.name == TrackData.ScratchName ? "MyTrack" : data.name;
        camTarget = data.Center();

        RebuildAll();
    }

    // ── Scene setup ──────────────────────────────────────────────────────────
    void EnsureTrack()
    {
        track = FindFirstObjectByType<Track>();
        if (track == null)
        {
            var go = new GameObject("Track");
            go.AddComponent<SplineContainer>();
            track       = go.AddComponent<Track>();
            track.width = 14f;

            var meshGO = new GameObject("TrackMesh");
            meshGO.transform.SetParent(go.transform, false);
            meshGO.AddComponent<MeshFilter>();
            meshGO.AddComponent<MeshRenderer>();
            meshBuilder = meshGO.AddComponent<TrackMeshBuilder>();
            meshBuilder.track = track;
        }
        else
        {
            meshBuilder = FindFirstObjectByType<TrackMeshBuilder>();
        }
    }

    void BuildHandleMaterials()
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit")
              ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        handleMat    = MakeMat(sh, new Color(0.30f, 0.80f, 1.00f));
        handleSelMat = MakeMat(sh, new Color(1.00f, 0.85f, 0.20f));
    }

    static Material MakeMat(Shader sh, Color c)
    {
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }

    // ── Rebuild ──────────────────────────────────────────────────────────────
    void RebuildAll() { RebuildTrackVisual(); RebuildHandles(); RebuildScenery(); }

    void RebuildTrackVisual()
    {
        if (track != null) data.ApplyTo(track);     // pushes nodeWidths + spline
        if (meshBuilder != null) meshBuilder.Rebuild();
    }

    // Trackside 2D scenery preview.  Regenerating meshes is too heavy for every
    // drag frame, so this runs on structural changes + drag release (SnapNode).
    void RebuildScenery()
    {
        if (sceneryGO != null)
        {
            // Runtime meshes aren't destroyed with their GameObjects — free them
            // explicitly or every rebuild leaks one mesh per wall.
            foreach (var mf in sceneryGO.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(sceneryGO);
        }
        sceneryGO = TrackScenery.Build(track, data);
    }

    void RebuildHandles()
    {
        foreach (var h in handles) if (h != null) Destroy(h);
        handles.Clear();

        for (int i = 0; i < data.nodes.Count; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Node " + i;
            go.transform.localScale = Vector3.one * 4f;
            go.transform.position   = data.nodes[i].Pos;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = (i == selected) ? handleSelMat : handleMat;
            if (i != selected) ApplyHeightTint(mr, data.nodes[i].y);   // contour colour
            go.AddComponent<NodeHandle>().index = i;
            handles.Add(go);
        }
    }

    void MoveHandle(int i)
    {
        if (i < 0 || i >= handles.Count || handles[i] == null) return;
        handles[i].transform.position = data.nodes[i].Pos;
        if (i != selected)
            ApplyHeightTint(handles[i].GetComponent<MeshRenderer>(), data.nodes[i].y);
    }

    // Per-handle contour tint without material instancing (no leaks).
    static MaterialPropertyBlock _mpb;
    static void ApplyHeightTint(MeshRenderer mr, float y)
    {
        if (mr == null) return;
        (_mpb ??= new MaterialPropertyBlock()).Clear();
        var col = HeightColor(y);
        _mpb.SetColor("_BaseColor", col);
        _mpb.SetColor("_Color", col);
        mr.SetPropertyBlock(_mpb);
    }

    // ── Per-frame input ──────────────────────────────────────────────────────
    void Update()
    {
        if (Input.GetMouseButtonDown(0)) _holdRecorded = false;   // new press → allow one snapshot

        bool manip = false;
        manip |= HandleNodeMouse();
        manip |= HandleGamepadEdit();
        HandleCamera();
        HandleGamepadButtons();

        // Keyboard shortcuts stay quiet while you type a map name.
        bool keys = !MapEditorPanel.Typing && !MapEditorPanel.DialogOpen && !SceneFader.Busy;
        if (keys && Input.GetKeyDown(KeyCode.G)) showGrid = !showGrid;
        if ((keys && Input.GetKeyDown(KeyCode.Z)) || Input.GetKeyDown(KeyCode.JoystickButton8)) Undo();
        if (keys && Input.GetKeyDown(KeyCode.Delete)) { RecordUndoNow(); DeleteNode(); }
        if (keys && Input.GetKeyDown(KeyCode.Escape)) BackToMenu();

        // Free-drag is smooth; snap to the grid when the manipulation ends.
        if (!manip && wasManipulating && selected >= 0) SnapNode(selected);
        wasManipulating = manip;

        UpdateGrid();

        if (!Input.GetMouseButton(0)) _holdRecorded = false;
    }

    // Records ONE undo snapshot per mouse press (pre-mutation), shared by the
    // viewport drag and all panel edits.
    void RecordUndoCoalesced()
    {
        if (_holdRecorded) return;
        RecordUndoNow();
        _holdRecorded = true;
    }

    bool HandleNodeMouse()
    {
        if (cam == null) return false;

        if (Input.GetMouseButtonDown(0) && !PointerOverPanel())
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 6000f))
            {
                var nh = hit.collider.GetComponent<NodeHandle>();
                if (nh != null) { Select(nh.index); draggingNode = true; }
            }
        }
        if (Input.GetMouseButtonUp(0)) draggingNode = false;

        if (draggingNode && selected >= 0 && Input.GetMouseButton(0))
        {
            var ray   = cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, data.nodes[selected].y, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                RecordUndoCoalesced();            // one snapshot at the start of the drag
                Vector3 p = ray.GetPoint(enter);
                data.nodes[selected].x = p.x;     // free; snapped on release
                data.nodes[selected].z = p.z;
                RebuildTrackVisual();
                MoveHandle(selected);
            }
            return true;
        }
        return false;
    }

    bool HandleGamepadEdit()
    {
        if (cam == null || data.nodes.Count == 0) return false;

        float rx   = PadAxis("PadShiftX");
        float ry   = PadAxis("PadShiftY");                       // up = +1 (inverted in config)
        float elev = PadAxis("PadThrottle") - PadAxis("PadBrake");
        bool  bMinus = Input.GetKey(KeyCode.JoystickButton2);    // X
        bool  bPlus  = Input.GetKey(KeyCode.JoystickButton3);    // Y

        bool move    = Mathf.Abs(rx) > 0.2f || Mathf.Abs(ry) > 0.2f;
        bool elevate = Mathf.Abs(elev) > 0.15f;
        bool bank    = bMinus || bPlus;
        if (!(move || elevate || bank)) { _padRecorded = false; return false; }

        if (!_padRecorded) { RecordUndoNow(); _padRecorded = true; }   // one per pad gesture
        if (selected < 0) Select(0);
        var nd = data.nodes[selected];
        float dt = Time.unscaledDeltaTime;

        if (move)
        {
            Vector3 fwd   = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = cam.transform.right;   right.y = 0f; right.Normalize();
            Vector3 p = nd.Pos + (right * rx + fwd * ry) * 70f * dt;
            nd.x = p.x; nd.z = p.z;
        }
        if (elevate) nd.y += elev * 35f * dt;
        if (bank)
            nd.roll = Mathf.Clamp(nd.roll + ((bPlus ? 1f : 0f) - (bMinus ? 1f : 0f)) * 90f * dt,
                                  -180f, 180f);

        RebuildTrackVisual();
        MoveHandle(selected);
        camTarget = Vector3.Lerp(camTarget, nd.Pos, 1f - Mathf.Exp(-5f * dt));
        return move || elevate;     // bank is an angle — no grid snap
    }

    void HandleGamepadButtons()
    {
        if (Input.GetKeyDown(KeyCode.JoystickButton5)) CycleSelection(+1);             // RB
        if (Input.GetKeyDown(KeyCode.JoystickButton4)) CycleSelection(-1);             // LB
        if (Input.GetKeyDown(KeyCode.JoystickButton0)) { RecordUndoNow(); AddNode(); }    // A
        if (Input.GetKeyDown(KeyCode.JoystickButton1)) { RecordUndoNow(); DeleteNode(); } // B
        if (Input.GetKeyDown(KeyCode.JoystickButton9)) showGrid = !showGrid;           // R3 → grid
        if (Input.GetKeyDown(KeyCode.JoystickButton7)) TestDrive();                    // Start
        if (Input.GetKeyDown(KeyCode.JoystickButton6)) BackToMenu();                   // Back
    }

    void HandleCamera()
    {
        if (cam == null) return;
        float dt = Time.unscaledDeltaTime;
        bool keys = !MapEditorPanel.Typing;

        float scroll = PointerOverPanel() ? 0f : Input.mouseScrollDelta.y;   // panel lists scroll instead
        if (Mathf.Abs(scroll) > 0.01f) camDist = Mathf.Clamp(camDist - scroll * 18f, 30f, 700f);

        if (keys && Input.GetKey(KeyCode.Q)) camYaw   -= 70f * dt;
        if (keys && Input.GetKey(KeyCode.E)) camYaw   += 70f * dt;
        if (keys && Input.GetKey(KeyCode.R)) camPitch  = Mathf.Clamp(camPitch + 45f * dt, 12f, 89f);
        if (keys && Input.GetKey(KeyCode.F)) camPitch  = Mathf.Clamp(camPitch - 45f * dt, 12f, 89f);

        float lx = PadAxis("PadSteerX");
        if (Mathf.Abs(lx) > 0.2f) camYaw += lx * 80f * dt;
        float ly = PadAxis("PadMoveY");                       // left stick Y → tilt (like R/F)
        if (Mathf.Abs(ly) > 0.2f) camPitch = Mathf.Clamp(camPitch + ly * 60f * dt, 12f, 89f);

        Vector3 fwd   = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = cam.transform.right;   right.y = 0f; right.Normalize();

        if (Input.GetMouseButton(1))
            camTarget -= (right * Input.GetAxis("Mouse X") + fwd * Input.GetAxis("Mouse Y"))
                       * camDist * 0.05f;

        Vector3 move = Vector3.zero;
        if (keys)
        {
            if (Input.GetKey(KeyCode.W)) move += fwd;
            if (Input.GetKey(KeyCode.S)) move -= fwd;
            if (Input.GetKey(KeyCode.A)) move -= right;
            if (Input.GetKey(KeyCode.D)) move += right;
        }
        if (move != Vector3.zero) camTarget += move.normalized * camDist * 0.6f * dt;

        Quaternion rot = Quaternion.Euler(camPitch, camYaw, 0f);
        cam.transform.position = camTarget + rot * new Vector3(0f, 0f, -camDist);
        cam.transform.rotation = Quaternion.LookRotation(camTarget - cam.transform.position, Vector3.up);
    }

    static float PadAxis(string name)
    {
        if (_badAxes != null && _badAxes.Contains(name)) return 0f;
        try { return Input.GetAxis(name); }
        catch { (_badAxes ??= new HashSet<string>()).Add(name); return 0f; }
    }

    // ── Operations ───────────────────────────────────────────────────────────
    void Select(int i) { selected = i; RebuildHandles(); }

    void CycleSelection(int dir)
    {
        if (data.nodes.Count == 0) return;
        int n = data.nodes.Count;
        int s = selected < 0 ? 0 : ((selected + dir) % n + n) % n;
        Select(s);
        camTarget = data.nodes[s].Pos;
    }

    float Snap(float v) => snap ? Mathf.Round(v / gridSize) * gridSize : v;

    void SnapNode(int i)
    {
        if (i < 0 || i >= data.nodes.Count) return;
        var nd = data.nodes[i];
        nd.x = Snap(nd.x); nd.y = Snap(nd.y); nd.z = Snap(nd.z);
        RebuildTrackVisual();
        MoveHandle(i);
        RebuildScenery();      // drag finished — re-seat the scenery on the new line
    }

    void NudgeNode(float dx, float dy, float dz)
    {
        if (selected < 0) return;
        var nd = data.nodes[selected];
        nd.x = Snap(nd.x + dx); nd.y = Snap(nd.y + dy); nd.z = Snap(nd.z + dz);
        RebuildTrackVisual();
        MoveHandle(selected);
    }

    void AddNode()
    {
        if (data.nodes.Count == 0) { data = TrackData.Template(); RebuildAll(); return; }
        int a = selected >= 0 ? selected : data.nodes.Count - 1;
        int b = (a + 1) % data.nodes.Count;
        Vector3 mid  = (data.nodes[a].Pos + data.nodes[b].Pos) * 0.5f;
        float   roll = (data.nodes[a].roll + data.nodes[b].roll) * 0.5f;
        float   wid  = (data.nodes[a].width + data.nodes[b].width) * 0.5f;
        data.nodes.Insert(a + 1, new NodeData(mid.x, mid.y, mid.z, roll, wid)
                                 { scenery = data.nodes[a].scenery,
                                   onGround = data.nodes[a].onGround });
        selected = a + 1;
        RebuildAll();
        message = "Added a node — drag it to shape the curve.";
    }

    void DeleteNode()
    {
        if (selected < 0) { message = "Select a node first."; return; }
        if (data.nodes.Count <= 3) { message = "A track needs at least 3 nodes."; return; }
        data.nodes.RemoveAt(selected);
        selected = Mathf.Clamp(selected, 0, data.nodes.Count - 1);
        RebuildAll();
        message = "Node deleted.";
    }

    void NewTrack()
    {
        data = TrackData.Template();
        selected = -1; mapName = "MyTrack"; camTarget = data.Center();
        RebuildAll();
        message = "Started a fresh oval.";
    }

    void DoSave()
    {
        string n = Sanitize(mapName);
        if (string.IsNullOrEmpty(n)) { message = "Type a name first."; return; }
        data.Save(n);
        data.Save(TrackData.ScratchName);
        // A changed track voids its lap records + ghost (unchanged = kept).
        bool wiped = TrackRecords.Validate(n, data);
        TrackRecords.Validate(TrackData.ScratchName, data);
        message = "Saved '" + n + "'." + (wiped ? "  Track changed — its lap records were reset." : "");
    }

    void DoLoad(string n)
    {
        var d = TrackData.Load(n);
        if (d == null) { message = "Couldn't load '" + n + "'."; return; }
        data = d; mapName = n; selected = -1; camTarget = data.Center();
        RebuildAll();
        message = "Loaded '" + n + "'.";
    }

    // Open a built-in circuit as an editable copy (save it under your own name).
    void LoadBuiltIn(string n)
    {
        var d = MapCatalog.GetBuiltIn(n);
        if (d == null) { message = "No built-in '" + n + "'."; return; }
        data = d; mapName = n + " (copy)"; selected = -1; camTarget = data.Center();
        RebuildAll();
        message = "Opened built-in '" + n + "' — Save it under a new name.";
    }

    void TestDrive()
    {
        data.Save(TrackData.ScratchName);
        SceneFader.Load(testDriveScene, "TEST DRIVE", "MAP EDITOR");
    }

    void BackToMenu()
    {
        data.Save(TrackData.ScratchName);   // your draft is never lost
        SceneFader.Load(returnMenuScene);
    }

    static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }

    // ── Undo ─────────────────────────────────────────────────────────────────
    void PushUndo(TrackData snapshot)
    {
        if (snapshot == null) return;
        undo.Add(snapshot);
        if (undo.Count > UndoCap) undo.RemoveAt(0);
    }

    void RecordUndoNow() => PushUndo(data.Clone());

    void Undo()
    {
        if (undo.Count == 0) { message = "Nothing to undo."; return; }
        data = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);
        selected = Mathf.Clamp(selected, -1, data.nodes.Count - 1);
        _holdRecorded = true;        // don't let the panel auto-record this undo as a new edit
        RebuildAll();
        message = "Undo.";
    }

    // ── Grid overlay (a real line mesh — renders correctly under URP) ─────────
    // The grid is the GROUND PLANE: always at y = 0.  Node elevation is read
    // through the contour-coloured stems (UpdateStems) that drop from each node
    // down (or up) to this plane.
    void UpdateGrid()
    {
        UpdateStems();

        if (!showGrid)
        {
            if (gridGO != null) gridGO.SetActive(false);
            return;
        }
        EnsureGridObject();
        gridGO.SetActive(true);

        float gs = Mathf.Max(0.5f, gridSize);
        const float y = 0f;
        float cx = Mathf.Round(camTarget.x / gs) * gs;
        float cz = Mathf.Round(camTarget.z / gs) * gs;
        if (cx == _gCx && cz == _gCz && gs == _gGs && y == _gY) return;   // unchanged
        _gCx = cx; _gCz = cz; _gGs = gs; _gY = y;

        const int half = 60;
        float ext = half * gs;
        var verts = new List<Vector3>();
        var cols  = new List<Color>();
        var idx   = new List<int>();
        for (int i = -half; i <= half; i++)
        {
            float x = cx + i * gs;
            AddLine(verts, cols, idx, new Vector3(x, y, cz - ext), new Vector3(x, y, cz + ext));
            float z = cz + i * gs;
            AddLine(verts, cols, idx, new Vector3(cx - ext, y, z), new Vector3(cx + ext, y, z));
        }
        gridMesh.Clear();
        gridMesh.SetVertices(verts);
        gridMesh.SetColors(cols);
        gridMesh.SetIndices(idx, MeshTopology.Lines, 0);
        gridMesh.RecalculateBounds();
    }

    // ── Elevation stems: a line from the ground plane up/down to every node,
    //    contour-coloured (blue below 0 → green low → yellow → red high).
    //    Cheap (2 verts per node) → rebuilt every frame, no caching needed.
    void UpdateStems()
    {
        if (!showGrid || data == null || data.nodes.Count == 0)
        {
            if (stemGO != null) stemGO.SetActive(false);
            return;
        }
        EnsureStemObject();
        stemGO.SetActive(true);

        var verts = new List<Vector3>(data.nodes.Count * 2);
        var cols  = new List<Color>(data.nodes.Count * 2);
        var idx   = new List<int>(data.nodes.Count * 2);
        foreach (var nd in data.nodes)
        {
            if (Mathf.Abs(nd.y) < 0.01f) continue;    // on the plane — no stem
            int i0 = verts.Count;
            verts.Add(new Vector3(nd.x, 0f, nd.z));
            verts.Add(new Vector3(nd.x, nd.y, nd.z));
            cols.Add(HeightColor(0f));
            cols.Add(HeightColor(nd.y));
            idx.Add(i0); idx.Add(i0 + 1);
        }
        stemMesh.Clear();
        stemMesh.SetVertices(verts);
        stemMesh.SetColors(cols);
        stemMesh.SetIndices(idx, MeshTopology.Lines, 0);
        stemMesh.RecalculateBounds();
    }

    void EnsureStemObject()
    {
        if (stemGO != null) return;
        stemGO = new GameObject("EditorStems");
        stemGO.AddComponent<MeshFilter>();
        var mr = stemGO.AddComponent<MeshRenderer>();
        var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        mr.sharedMaterial    = new Material(sh) { color = Color.white };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        stemMesh = new Mesh { name = "EditorStems" };
        stemGO.GetComponent<MeshFilter>().sharedMesh = stemMesh;
    }

    /// Contour-map elevation colour: blue below the plane, green near it,
    /// through yellow to red as the track climbs.
    public static Color HeightColor(float y)
    {
        if (y < 0f)
            return Color.Lerp(new Color(0.20f, 0.85f, 0.35f),     // green at 0
                              new Color(0.15f, 0.40f, 1.00f),     // blue deep
                              Mathf.Clamp01(-y / 30f));
        if (y < 35f)
            return Color.Lerp(new Color(0.20f, 0.85f, 0.35f),     // green low
                              new Color(1.00f, 0.85f, 0.20f),     // yellow mid
                              y / 35f);
        return Color.Lerp(new Color(1.00f, 0.85f, 0.20f),
                          new Color(1.00f, 0.25f, 0.20f),         // red high
                          Mathf.Clamp01((y - 35f) / 45f));
    }

    static void AddLine(List<Vector3> v, List<Color> c, List<int> idx, Vector3 a, Vector3 b)
    {
        int i0 = v.Count;
        v.Add(a); v.Add(b);
        c.Add(GridColor); c.Add(GridColor);
        idx.Add(i0); idx.Add(i0 + 1);
    }

    void EnsureGridObject()
    {
        if (gridGO != null) return;
        gridGO = new GameObject("EditorGrid");
        gridGO.AddComponent<MeshFilter>();
        var mr = gridGO.AddComponent<MeshRenderer>();
        var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        mr.sharedMaterial    = new Material(sh) { color = Color.white };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        gridMesh = new Mesh { name = "EditorGrid" };
        gridMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        gridGO.GetComponent<MeshFilter>().sharedMesh = gridMesh;
    }

    // ── Panel API (the baked MapEditorPanel drives these) ─────────────────────
    // Mouse over any UI (the panel, the open-track dialog) blocks node picking.
    static bool PointerOverPanel() =>
        UnityEngine.EventSystems.EventSystem.current != null &&
        UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    public TrackData Data      => data;
    public int       Selected  => selected;
    public NodeData  SelectedNode => selected >= 0 && selected < data.nodes.Count ? data.nodes[selected] : null;
    public string    Message   => message;
    public float     GridSize  => gridSize;
    public bool      SnapOn    => snap;
    public bool      GridOn    => showGrid;
    public int       UndoDepth => undo.Count;
    public string    MapName   { get => mapName; set => mapName = value; }

    public void UI_Save()                 { DoSave(); }
    public void UI_New()                  { RecordUndoNow(); NewTrack(); }
    public void UI_Open(string n)         { RecordUndoNow(); DoLoad(n); }
    public void UI_OpenBuiltIn(string n)  { RecordUndoNow(); LoadBuiltIn(n); }
    public void UI_Undo()                 { Undo(); }
    public void UI_AddNode()              { RecordUndoNow(); AddNode(); }
    public void UI_DeleteNode()           { RecordUndoNow(); DeleteNode(); }
    public void UI_TestDrive()            { TestDrive(); }
    public void UI_Back()                 { BackToMenu(); }
    public void UI_GridStep(int dir)      { gridSize = Mathf.Clamp(gridSize + dir, 1f, 25f); message = $"Grid {gridSize:0} m."; }
    public void UI_SetSnap(bool on)       { snap = on; }
    public void UI_SetGrid(bool on)       { showGrid = on; }

    public void UI_Nudge(int axis, int dir)
    {
        if (selected < 0) return;
        RecordUndoCoalesced();
        float d = dir * gridSize;
        NudgeNode(axis == 0 ? d : 0f, axis == 1 ? d : 0f, axis == 2 ? d : 0f);
        RebuildScenery();
    }

    public void UI_SetWidth(float w, bool final)
    {
        var nd = SelectedNode;
        if (nd == null) return;
        RecordUndoCoalesced();
        nd.width = Mathf.Clamp(w, 6f, 40f);
        RebuildTrackVisual();
        if (final) RebuildScenery();
    }

    public void UI_WidthAll()
    {
        var nd = SelectedNode;
        if (nd == null) return;
        RecordUndoNow();
        foreach (var x in data.nodes) x.width = nd.width;
        RebuildTrackVisual(); RebuildScenery();
        message = $"Every node is now {nd.width:0.0} m wide.";
    }

    public void UI_CycleScenery(int dir)
    {
        var nd = SelectedNode;
        if (nd == null) return;
        RecordUndoNow();
        int sc = Mathf.Clamp(nd.scenery, 0, TrackScenery.StyleCount - 1);
        nd.scenery = (sc + dir + TrackScenery.StyleCount) % TrackScenery.StyleCount;
        RebuildScenery();
    }

    public void UI_SceneryAll()
    {
        var nd = SelectedNode;
        if (nd == null) return;
        RecordUndoNow();
        foreach (var x in data.nodes) x.scenery = nd.scenery;
        RebuildScenery();
        message = $"Scenery set to {TrackScenery.StyleNames[Mathf.Clamp(nd.scenery, 0, TrackScenery.StyleCount - 1)]} everywhere.";
    }

    public void UI_SetOnGround(bool on)
    {
        var nd = SelectedNode;
        if (nd == null || nd.onGround == on) return;
        RecordUndoNow();
        nd.onGround = on;
        RebuildScenery();
    }

    public void UI_SetRoll(float r, bool final)
    {
        var nd = SelectedNode;
        if (nd == null) return;
        RecordUndoCoalesced();
        nd.roll = Mathf.Clamp(r, -180f, 180f);
        RebuildTrackVisual();
        if (final) RebuildScenery();
    }

    public void UI_RollPreset(int which)   // 0 = -15, 1 = flat, 2 = flip, 3 = +15
    {
        var nd = SelectedNode;
        if (nd == null) return;
        RecordUndoNow();
        nd.roll = which switch
        {
            0 => Mathf.Clamp(nd.roll - 15f, -180f, 180f),
            1 => 0f,
            2 => nd.roll >= 0f ? 180f : -180f,
            _ => Mathf.Clamp(nd.roll + 15f, -180f, 180f),
        };
        RebuildTrackVisual(); RebuildScenery();
    }

    public void UI_Select(int i)
    {
        if (i < 0 || i >= data.nodes.Count) return;
        Select(i);
        camTarget = data.nodes[i].Pos;
    }
}

/// Marker on each draggable node sphere so a raycast can map a hit back to its
/// index in the editor's node list.
public class NodeHandle : MonoBehaviour { public int index; }
