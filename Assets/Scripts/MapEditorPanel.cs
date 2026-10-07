using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// The map editor's side panel — a baked, themed uGUI inspector (it replaces
/// the old IMGUI box).  Mouse-first, like any creation tool: every control
/// calls MapEditor's UI_* API, which records undo and rebuilds the preview.
///   TRACK   name, SAVE / OPEN / NEW
///   VIEW    grid size, snap, grid visibility, undo
///   NODE    X/Y/Z nudges (Y shows the contour colour), width, scenery,
///           on-ground, bank + presets, delete
///   ADD NODE · TEST DRIVE · BACK
/// OPEN shows a dialog listing built-in + saved maps with a live preview of
/// whichever one you hover.
[DisallowMultipleComponent]
public class MapEditorPanel : MonoBehaviour
{
    /// True while the name field has keyboard focus — MapEditor mutes its
    /// single-key shortcuts (WASD, G, Z, Delete, Esc) so typing is safe.
    public static bool Typing { get; private set; }
    /// True while the open-track dialog is up.
    public static bool DialogOpen { get; private set; }

    [Header("Track")]
    public TMP_InputField  nameInput;
    public TextMeshProUGUI messageText;
    public Button          saveButton, openButton, newButton;

    [Header("View")]
    public TextMeshProUGUI gridValue;
    public Button          gridMinus, gridPlus, undoButton;
    public Toggle          snapToggle, gridToggle;

    [Header("Node")]
    public CanvasGroup     nodeGroup;
    public GameObject      nodeHint;
    public TextMeshProUGUI nodeHeader;
    public TextMeshProUGUI xValue, yValue, zValue;
    public Image           yChip;
    public Button          xMinus, xPlus, yMinus, yPlus, zMinus, zPlus;
    public TextMeshProUGUI widthValue;
    public Slider          widthSlider;
    public Button          widthAll;
    public TextMeshProUGUI sceneryValue;
    public Button          sceneryPrev, sceneryNext, sceneryAll;
    public Toggle          groundToggle;
    public TextMeshProUGUI bankValue;
    public Slider          bankSlider;
    public Button          bankMinus15, bankFlat, bankFlip, bankPlus15;
    public Button          deleteButton;

    [Header("Actions")]
    public Button addButton, testButton, backButton, prevNodeButton, nextNodeButton;

    [Header("Open dialog")]
    public CanvasGroup       dialog;
    public RectTransform     listRoot;
    public Button            entryTemplate;
    public TextMeshProUGUI   headerTemplate;
    public Button            dialogClose;
    public MapPreviewGraphic dialogPreview;
    public TextMeshProUGUI   dialogPreviewTitle, dialogPreviewStats;

    MapEditor editor;
    readonly List<GameObject> listItems = new List<GameObject>();
    bool   syncing;
    string lastKey;
    float  dialogFade;

    void Awake()
    {
        MenuSceneRoot.EnsureEventSystem();
        MenuAudio.Ensure();
        UITheme.ApplyFont(gameObject);
        if (dialog != null) { dialog.alpha = 0f; dialog.blocksRaycasts = false; }
        if (entryTemplate != null)  entryTemplate.gameObject.SetActive(false);
        if (headerTemplate != null) headerTemplate.gameObject.SetActive(false);
    }

    void OnDestroy() { Typing = false; DialogOpen = false; }

    void Start()
    {
        editor = FindAnyObjectByType<MapEditor>();
        if (editor == null) { enabled = false; return; }

        Click(saveButton,   () => editor.UI_Save());
        Click(openButton,   OpenDialog);
        Click(newButton,    () => editor.UI_New());
        Click(undoButton,   () => editor.UI_Undo());
        Click(gridMinus,    () => editor.UI_GridStep(-1));
        Click(gridPlus,     () => editor.UI_GridStep(+1));
        Click(xMinus, () => editor.UI_Nudge(0, -1)); Click(xPlus, () => editor.UI_Nudge(0, +1));
        Click(yMinus, () => editor.UI_Nudge(1, -1)); Click(yPlus, () => editor.UI_Nudge(1, +1));
        Click(zMinus, () => editor.UI_Nudge(2, -1)); Click(zPlus, () => editor.UI_Nudge(2, +1));
        Click(widthAll,     () => editor.UI_WidthAll());
        Click(sceneryPrev,  () => editor.UI_CycleScenery(-1));
        Click(sceneryNext,  () => editor.UI_CycleScenery(+1));
        Click(sceneryAll,   () => editor.UI_SceneryAll());
        Click(bankMinus15,  () => editor.UI_RollPreset(0));
        Click(bankFlat,     () => editor.UI_RollPreset(1));
        Click(bankFlip,     () => editor.UI_RollPreset(2));
        Click(bankPlus15,   () => editor.UI_RollPreset(3));
        Click(deleteButton, () => editor.UI_DeleteNode());
        Click(addButton,    () => editor.UI_AddNode());
        Click(testButton,   () => editor.UI_TestDrive(), Sfx.Start);
        Click(backButton,   () => editor.UI_Back(), Sfx.Back);
        Click(prevNodeButton, () => editor.UI_Select(Wrap(editor.Selected - 1)));
        Click(nextNodeButton, () => editor.UI_Select(Wrap(editor.Selected + 1)));
        Click(dialogClose,  CloseDialog, Sfx.Back);

        if (snapToggle != null)   snapToggle.onValueChanged.AddListener(v => { if (!syncing) { editor.UI_SetSnap(v); MenuAudio.Play(Sfx.Toggle); } });
        if (gridToggle != null)   gridToggle.onValueChanged.AddListener(v => { if (!syncing) { editor.UI_SetGrid(v); MenuAudio.Play(Sfx.Toggle); } });
        if (groundToggle != null) groundToggle.onValueChanged.AddListener(v => { if (!syncing) { editor.UI_SetOnGround(v); MenuAudio.Play(Sfx.Toggle); } });

        if (widthSlider != null)
        {
            widthSlider.minValue = 6f; widthSlider.maxValue = 40f;
            widthSlider.onValueChanged.AddListener(v => { if (!syncing) editor.UI_SetWidth(v, false); });
            AddRelease(widthSlider, () => editor.UI_SetWidth(widthSlider.value, true));
        }
        if (bankSlider != null)
        {
            bankSlider.minValue = -180f; bankSlider.maxValue = 180f;
            bankSlider.onValueChanged.AddListener(v => { if (!syncing) editor.UI_SetRoll(v, false); });
            AddRelease(bankSlider, () => editor.UI_SetRoll(bankSlider.value, true));
        }
        if (nameInput != null)
        {
            nameInput.text = editor.MapName;
            nameInput.onValueChanged.AddListener(v => { if (!syncing) editor.MapName = v; });
            nameInput.onSubmit.AddListener(_ => editor.UI_Save());
        }
    }

    int Wrap(int i)
    {
        int n = editor.Data.nodes.Count;
        return n == 0 ? -1 : ((i % n) + n) % n;
    }

    static void Click(Button b, System.Action a, Sfx s = Sfx.Adjust)
    {
        if (b == null) return;
        b.onClick.AddListener(() => { MenuAudio.Play(s, 0.8f); a(); });
    }

    static void AddRelease(Slider s, System.Action onRelease)
    {
        s.gameObject.AddComponent<UIPointerUpHook>().onUp = onRelease;
    }

    // ── Sync from the editor (only when something changed) ───────────────────
    void LateUpdate()
    {
        if (editor == null) return;
        Typing = nameInput != null && nameInput.isFocused;

        var nd = editor.SelectedNode;
        string key = nd == null
            ? $"-|{editor.Message}|{editor.GridSize}|{editor.SnapOn}|{editor.GridOn}|{editor.UndoDepth}"
            : $"{editor.Selected}|{editor.Data.nodes.Count}|{nd.x}|{nd.y}|{nd.z}|{nd.width}|{nd.roll}|{nd.scenery}|{nd.onGround}|" +
              $"{editor.Message}|{editor.GridSize}|{editor.SnapOn}|{editor.GridOn}|{editor.UndoDepth}";
        if (key != lastKey) { lastKey = key; Sync(nd); }

        if (!Typing && nameInput != null && nameInput.text != editor.MapName)
        { syncing = true; nameInput.SetTextWithoutNotify(editor.MapName); syncing = false; }

        // Dialog fade + Esc to close it.
        float dt = Time.unscaledDeltaTime;
        dialogFade = Mathf.MoveTowards(dialogFade, DialogOpen ? 1f : 0f, dt / 0.15f);
        if (dialog != null) { dialog.alpha = dialogFade; dialog.blocksRaycasts = DialogOpen; }
        if (DialogOpen && Input.GetKeyDown(KeyCode.Escape)) CloseDialog();
    }

    void Sync(NodeData nd)
    {
        syncing = true;
        if (messageText != null) messageText.text = editor.Message;
        if (gridValue != null)   gridValue.text   = $"{editor.GridSize:0} M";
        if (snapToggle != null)  snapToggle.SetIsOnWithoutNotify(editor.SnapOn);
        if (gridToggle != null)  gridToggle.SetIsOnWithoutNotify(editor.GridOn);
        if (undoButton != null)  undoButton.interactable = editor.UndoDepth > 0;

        bool has = nd != null;
        if (nodeGroup != null) { nodeGroup.alpha = has ? 1f : 0.25f; nodeGroup.interactable = has; nodeGroup.blocksRaycasts = has; }
        if (nodeHint != null)  nodeHint.SetActive(!has);
        if (has)
        {
            if (nodeHeader != null) nodeHeader.text = $"NODE  <color=#B06BFF>{editor.Selected + 1}</color> / {editor.Data.nodes.Count}";
            if (xValue != null) xValue.text = $"{nd.x:0.0}";
            if (yValue != null) yValue.text = $"{nd.y:0.0}";
            if (zValue != null) zValue.text = $"{nd.z:0.0}";
            if (yChip != null)  yChip.color = MapEditor.HeightColor(nd.y);
            if (widthValue != null) widthValue.text = $"{nd.width:0.0} M";
            if (widthSlider != null) widthSlider.SetValueWithoutNotify(nd.width);
            int sc = Mathf.Clamp(nd.scenery, 0, TrackScenery.StyleCount - 1);
            if (sceneryValue != null) sceneryValue.text = TrackScenery.StyleNames[sc].ToUpperInvariant();
            if (groundToggle != null) groundToggle.SetIsOnWithoutNotify(nd.onGround);
            if (bankValue != null)  bankValue.text = $"{nd.roll:0}°";
            if (bankSlider != null) bankSlider.SetValueWithoutNotify(nd.roll);
        }
        else if (nodeHeader != null) nodeHeader.text = "NODE";
        syncing = false;
    }

    // ── Open dialog ───────────────────────────────────────────────────────────
    void OpenDialog()
    {
        foreach (var go in listItems) if (go != null) Destroy(go);
        listItems.Clear();

        Header("BUILT-IN CIRCUITS  ·  opens a copy");
        foreach (var b in MapCatalog.BuiltIns)
        {
            string n = b.name;
            Entry(n.ToUpperInvariant(), new MapCatalog.Entry { name = n, builtIn = true, display = n },
                  () => { editor.UI_OpenBuiltIn(n); CloseDialog(); });
        }
        Header("YOUR MAPS");
        var mine = TrackData.ListMaps();
        if (mine.Count == 0) Header("   (nothing saved yet)");
        foreach (var n in mine)
        {
            string name = n;
            Entry(name.ToUpperInvariant(), new MapCatalog.Entry { name = name, builtIn = false, display = name },
                  () => { editor.UI_Open(name); CloseDialog(); });
        }

        DialogOpen = true;
        MenuAudio.Play(Sfx.Open);
        if (dialogPreview != null) dialogPreview.SetTrack(null);
        if (dialogPreviewTitle != null) dialogPreviewTitle.text = "HOVER A MAP";
        if (dialogPreviewStats != null) dialogPreviewStats.text = "";
    }

    void CloseDialog()
    {
        DialogOpen = false;
    }

    void Header(string text)
    {
        if (headerTemplate == null) return;
        var h = Instantiate(headerTemplate, listRoot);
        h.gameObject.SetActive(true);
        h.text = text;
        listItems.Add(h.gameObject);
    }

    void Entry(string label, MapCatalog.Entry e, System.Action pick)
    {
        if (entryTemplate == null) return;
        var b = Instantiate(entryTemplate, listRoot);
        b.gameObject.SetActive(true);
        var t = b.GetComponentInChildren<TextMeshProUGUI>(true);
        if (t != null) t.text = label;
        b.onClick.AddListener(() => { MenuAudio.Play(Sfx.Confirm); pick(); });
        b.gameObject.AddComponent<UIHoverHook>().onEnter = () => Preview(e);
        listItems.Add(b.gameObject);
    }

    void Preview(MapCatalog.Entry e)
    {
        var st = TrackStats.For(e);
        if (dialogPreview != null) dialogPreview.SetTrack(st);
        if (dialogPreviewTitle != null) dialogPreviewTitle.text = e.display.ToUpperInvariant();
        if (dialogPreviewStats != null)
            dialogPreviewStats.text = st != null
                ? $"{st.LengthText}  ·  {st.ClimbText} CLIMB  ·  {st.nodes} NODES" : "";
        MenuAudio.Play(Sfx.Move, 0.4f);
    }
}
