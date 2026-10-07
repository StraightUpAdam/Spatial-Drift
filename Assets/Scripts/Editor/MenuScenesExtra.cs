using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static MenuBaker;

/// Bakers for the two tool-ish screens: the online lobby UI and the map
/// editor's inspector panel (+ its open-track dialog).
public static class MenuScenesExtra
{
    static readonly Vector2 TL = new Vector2(0f, 1f);
    static readonly Vector2 TR = new Vector2(1f, 1f);
    static readonly Vector2 BL = new Vector2(0f, 0f);
    static readonly Vector2 C  = new Vector2(0.5f, 0.5f);

    // ══════════════════════════════════════════════════════════════════════════
    //  LOBBY (the NetworkManager + LobbyController are created by MultiplayerSetup)
    // ══════════════════════════════════════════════════════════════════════════
    public static LobbyMenu BakeLobbyUI()
    {
        EnsureArt();
        MenuCamera(drift: true, startYaw: 80f);
        SceneRoot(true);
        EventSystem();

        var canvas = Canvas("MenuCanvas", 10);
        var ct = canvas.transform;
        Shades(ct, 1150f, 0.8f);
        Header(ct, "SPATIAL DRIFT   /   MULTIPLAYER", "ONLINE");

        // Status line (under the header).
        var status = Place(Rect(ct, "Status"), TL, TL, new Vector2(110f, -196f), new Vector2(900f, 30f));
        var dot = Img(status, "Dot", "ui_circle", UITheme.Gold);
        Place(dot.rectTransform, new Vector2(0f, 0.5f), C, new Vector2(8f, 0f), new Vector2(14f, 14f));
        var spin = Img(status, "Spinner", "ui_ring_arc", UITheme.Accent);
        Place(spin.rectTransform, new Vector2(0f, 0.5f), C, new Vector2(8f, 0f), new Vector2(30f, 30f));
        var statusText = Text(status, "Text", "Connecting to online services…", 20, UITheme.TextDim, FontStyles.Bold);
        Place(statusText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(860f, 30f));

        // ── Connect page ─────────────────────────────────────────────────────
        var connect = Screen(ct, "Connect", TL, TL, new Vector2(110f, -250f), new Vector2(660f, 560f), false, false, out var cr);
        var name   = TextRow(cr, "DRIVER NAME", "How everyone in the lobby sees you.  Enter to type, Enter to confirm.");
        var paint  = StepperRow(cr, "PAINT", "Your car's paint — everyone sees it.", 340f, PlayerProfile.PlayerPalette.Length);
        Spacer(cr, 4f);
        var host   = ButtonRow(cr, "HOST A LOBBY", "Open a lobby and get a code to share with friends.", primary: true, sound: Sfx.Open);
        var code   = CodeRow(cr, "JOIN CODE", "Type or paste (Ctrl+V) a friend's 6-character code.  Controller: A, then up/down letters, left/right slots.");
        var join   = ButtonRow(cr, "JOIN LOBBY", "Join the lobby for the code above.");
        var back   = ButtonRow(cr, "BACK", "Return to the main menu.", sound: Sfx.Back);
        connect.items.AddRange(new MenuRow[] { name, paint, host, code, join, back });

        var connectCardsRT = Stretch(Rect(ct, "ConnectCards"));
        var connectCards = connectCardsRT.gameObject.AddComponent<CanvasGroup>();
        var info = Card(connectCardsRT, "HowItWorks", TR, TR, new Vector2(-110f, -250f), new Vector2(640f, 360f));
        CardTitle(info, "HOW IT WORKS");
        var infoText = Text(info, "Body",
            "<color=#B06BFF>HOST</color>  —  open a lobby and share the six-character code.\n\n" +
            "<color=#B06BFF>JOIN</color>  —  type or paste a friend's code.\n\n" +
            "Races run over Unity Relay — no port forwarding, play from anywhere.\n\n" +
            "Your name and paint travel with you.", 21, UITheme.TextDim);
        infoText.textWrappingMode = TextWrappingModes.Normal;
        Place(infoText.rectTransform, TL, TL, new Vector2(30f, -66f), new Vector2(580f, 270f));
        infoText.alignment = TextAlignmentOptions.TopLeft;

        // ── Room page ────────────────────────────────────────────────────────
        var room = Screen(ct, "Room", TL, TL, new Vector2(110f, -250f), new Vector2(640f, 640f), false, true, out var rr);
        var mode    = StepperRow(rr, "MODE", "Race: grid race with laps.  Time Trial: everyone drives together, no contact, best time wins.", 300f);
        var map     = StepperRow(rr, "CIRCUIT", "Custom maps are sent to everyone automatically.", 300f);
        var laps    = StepperRow(rr, "LAPS", "Race distance.", 300f);
        var weather = StepperRow(rr, "WEATHER", "Sky, light and fog for everyone.", 300f);
        var rivals  = StepperRow(rr, "AI RIVALS", "Named AI drivers that fill out the grid.", 300f);
        var aiRows = new MenuStepper[7];
        for (int i = 0; i < 7; i++) aiRows[i] = StepperRow(rr, $"RIVAL {i + 1}", "This rival's difficulty.", 300f);
        var rpaint  = StepperRow(rr, "YOUR PAINT", "Change your paint any time before the race.", 300f, PlayerProfile.PlayerPalette.Length);
        Spacer(rr, 4f);
        var start   = ButtonRow(rr, "START RACE", "Load everyone into the race.", primary: true, sound: Sfx.Start);
        var leave   = ButtonRow(rr, "LEAVE LOBBY", "Back to host / join.", sound: Sfx.Back);
        room.items.Add(mode); room.items.Add(map); room.items.Add(laps); room.items.Add(weather); room.items.Add(rivals);
        room.items.AddRange(aiRows);
        room.items.Add(rpaint); room.items.Add(start); room.items.Add(leave);

        var roomCardsRT = Stretch(Rect(ct, "RoomCards"));
        var roomCards = roomCardsRT.gameObject.AddComponent<CanvasGroup>();
        roomCards.alpha = 0f;

        // Map card (middle column).
        var mapCard = Card(roomCardsRT, "MapCard", TL, TL, new Vector2(800f, -250f), new Vector2(430f, 420f));
        CardTitle(mapCard, "CIRCUIT");
        var preview = Preview(mapCard, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(390f, 250f));
        var mapTitle = Text(mapCard, "MapTitle", "", 30, UITheme.Text, FontStyles.Bold | FontStyles.Italic);
        Place(mapTitle.rectTransform, TL, TL, new Vector2(22f, -322f), new Vector2(390f, 40f));
        var mapStats = Text(mapCard, "MapStats", "", 16, UITheme.TextDim, FontStyles.Bold);
        mapStats.characterSpacing = 3f;
        Place(mapStats.rectTransform, TL, TL, new Vector2(24f, -366f), new Vector2(390f, 24f));

        // Code card + drivers (right column).
        var codeCard = Card(roomCardsRT, "CodeCard", TR, TR, new Vector2(-110f, -250f), new Vector2(560f, 150f));
        CardTitle(codeCard, "JOIN CODE");
        var codeText = Text(codeCard, "Code", "------", 64, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Left);
        codeText.characterSpacing = 30f;
        Place(codeText.rectTransform, TL, TL, new Vector2(26f, -54f), new Vector2(520f, 80f));

        var players = Card(roomCardsRT, "Drivers", TR, TR, new Vector2(-110f, -420f), new Vector2(560f, 470f));
        var pTitle = CardTitle(players, "DRIVERS  1 / 8");
        var slots = new LobbySlot[8];
        for (int i = 0; i < 8; i++)
        {
            var row = Place(Rect(players, "Slot" + (i + 1)), TL, TL, new Vector2(20f, -60f - i * 44f), new Vector2(520f, 40f));
            var hl = Img(row, "BG", "ui_round", UITheme.Alpha(Color.white, 0.03f), true);
            Stretch(hl.rectTransform);
            var p = Img(row, "Paint", null, Color.white);
            Place(p.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(8f, 26f));
            AddShear(p, 0.3f);
            var n = Text(row, "Name", "OPEN SLOT", 21, UITheme.Text, FontStyles.Bold);
            Place(n.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(32f, 0f), new Vector2(330f, 30f));
            var t = Text(row, "Tag", "", 15, UITheme.Accent, FontStyles.Bold, TextAlignmentOptions.Right);
            t.characterSpacing = 4f;
            Place(t.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(160f, 24f));
            slots[i] = new LobbySlot { root = row.gameObject, paint = p, name = n, tag = t };
        }
        var aiSummary = Text(players, "AISummary", "", 16, UITheme.TextDim, FontStyles.Bold);
        aiSummary.characterSpacing = 3f;
        aiSummary.textWrappingMode = TextWrappingModes.Normal;
        Place(aiSummary.rectTransform, BL, BL, new Vector2(24f, 16f), new Vector2(512f, 40f));

        var hint = HintLine(ct, new Vector2(110f, 96f), 660f);
        connect.hintText = hint;
        room.hintText = hint;

        var modal = Modal(ct);
        var prompts = Prompts(ct);

        var menu = new GameObject("LobbyMenu").AddComponent<LobbyMenu>();
        menu.connectScreen = connect; menu.nameField = name; menu.paintRow = paint; menu.hostButton = host;
        menu.codeField = code; menu.joinButton = join; menu.backButton = back; menu.connectCards = connectCards;
        menu.roomScreen = room; menu.modeRow = mode; menu.mapRow = map; menu.lapsRow = laps; menu.weatherRow = weather;
        menu.aiCountRow = rivals; menu.aiRows = aiRows; menu.roomPaintRow = rpaint;
        menu.startButton = start; menu.leaveButton = leave; menu.roomCards = roomCards;
        menu.codeText = codeText; menu.playersTitle = pTitle; menu.aiSummary = aiSummary; menu.slots = slots;
        menu.preview = preview; menu.mapTitle = mapTitle; menu.mapStats = mapStats;
        menu.statusText = statusText; menu.statusDot = dot; menu.spinner = spin.rectTransform;
        menu.modal = modal; menu.prompts = prompts;
        return menu;
    }

    static void Spacer(Transform rows, float h)
    {
        var s = Rect(rows, "Spacer");
        var le = s.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = h; le.minHeight = h;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MAP EDITOR panel
    // ══════════════════════════════════════════════════════════════════════════
    public static MapEditorPanel BakeMapEditorPanel()
    {
        EnsureArt();
        var canvas = Canvas("EditorCanvas", 50);
        var ct = canvas.transform;

        // Full-height left panel with a scrolling column of sections.
        var panelRT = Rect(ct, "Panel");
        panelRT.anchorMin = new Vector2(0f, 0f);
        panelRT.anchorMax = new Vector2(0f, 1f);
        panelRT.pivot     = new Vector2(0f, 0.5f);
        panelRT.anchoredPosition = new Vector2(16f, 0f);
        panelRT.sizeDelta = new Vector2(440f, -32f);
        var shadow = Img(panelRT, "Shadow", "ui_soft", new Color(0f, 0f, 0f, 0.5f), true);
        Stretch(shadow.rectTransform, -24, -24, -24, -24);
        var bg = Img(panelRT, "BG", "ui_round", UITheme.Alpha(UITheme.Panel, 0.94f), true, true);
        Stretch(bg.rectTransform);
        var edgeLine = Img(panelRT, "Outline", "ui_round_line", UITheme.Alpha(Color.white, 0.07f), true);
        Stretch(edgeLine.rectTransform);

        var scroll = panelRT.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        var viewport = Stretch(Rect(panelRT, "Viewport"), 0, 186, 0, 8);   // footer below
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(22, 22, 18, 24);
        v.spacing = 8f;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content;

        // Title.
        var title = Label(content, "MAP <color=#B06BFF>EDITOR</color>", 36, UITheme.Text, FontStyles.Bold | FontStyles.Italic, 46f);
        var bar = Img(title.transform, "AccentBar", null, UITheme.Accent);
        Place(bar.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(2f, -4f), new Vector2(90f, 5f));
        AddShear(bar, 1f);
        var msg = Label(content, "Drag nodes to shape the track.", 17, UITheme.TextDim, FontStyles.Normal, 44f);
        msg.textWrappingMode = TextWrappingModes.Normal;

        // TRACK
        Section(content, "TRACK");
        var input = ToolInput(content, 396f);
        var fileRow = HRow(content, 40f);
        var save = ToolButton(fileRow, "SAVE", 124f, 40f, accent: true);
        var open = ToolButton(fileRow, "OPEN", 124f, 40f);
        var neu  = ToolButton(fileRow, "NEW", 124f, 40f);

        // VIEW
        Section(content, "VIEW");
        var gridRow = HRow(content, 36f);
        Label(gridRow, "GRID", 17, UITheme.TextDim, FontStyles.Bold, 36f, 70f);
        var gMinus = ToolButton(gridRow, "-", 40f, 34f);
        var gValue = Label(gridRow, "5 M", 19, UITheme.Text, FontStyles.Bold, 36f, 70f, TextAlignmentOptions.Center);
        var gPlus  = ToolButton(gridRow, "+", 40f, 34f);
        var undo   = ToolButton(gridRow, "UNDO", 110f, 34f);
        var togRow = HRow(content, 34f);
        var snap   = ToolToggle(togRow, "SNAP", 150f);
        var showG  = ToolToggle(togRow, "SHOW GRID", 190f);

        // NODE
        Section(content, "NODE");
        var nodeRow = HRow(content, 38f);
        var prev = ToolButton(nodeRow, "<", 44f, 36f);
        var nodeHeader = Label(nodeRow, "NODE", 22, UITheme.Text, FontStyles.Bold | FontStyles.Italic, 38f, 290f, TextAlignmentOptions.Center);
        var next = ToolButton(nodeRow, ">", 44f, 36f);
        var hint = Label(content, "Click a node in the view (or use the arrows above) to edit it.", 16, UITheme.TextFaint, FontStyles.Italic, 40f);
        hint.textWrappingMode = TextWrappingModes.Normal;

        var nodeRT = Rect(content, "NodeGroup");
        var nodeGroup = nodeRT.gameObject.AddComponent<CanvasGroup>();
        var nv = nodeRT.gameObject.AddComponent<VerticalLayoutGroup>();
        nv.spacing = 8f; nv.childControlWidth = true; nv.childControlHeight = true;
        nv.childForceExpandWidth = true; nv.childForceExpandHeight = false;

        var xVal = AxisRow(nodeRT, "X", null, out var xm, out var xp);
        var yVal = AxisRow(nodeRT, "Y  HEIGHT", "chip", out var ym, out var yp);
        var yChip = yVal.transform.parent.Find("Chip")?.GetComponent<Image>();
        var zVal = AxisRow(nodeRT, "Z", null, out var zm, out var zp);

        var wHead = HRow(nodeRT, 30f);
        Label(wHead, "WIDTH", 17, UITheme.TextDim, FontStyles.Bold, 30f, 150f);
        var wValue = Label(wHead, "14.0 M", 19, UITheme.Text, FontStyles.Bold, 30f, 120f, TextAlignmentOptions.Right);
        var wAll = ToolButton(wHead, "ALL", 70f, 28f);
        var wSlider = ToolSlider(nodeRT, 396f);

        var sRow = HRow(nodeRT, 36f);
        Label(sRow, "SCENERY", 17, UITheme.TextDim, FontStyles.Bold, 36f, 96f);
        var sPrev = ToolButton(sRow, "<", 38f, 34f);
        var sValue = Label(sRow, "NONE", 19, UITheme.Text, FontStyles.Bold, 36f, 130f, TextAlignmentOptions.Center);
        var sNext = ToolButton(sRow, ">", 38f, 34f);
        var sAll = ToolButton(sRow, "ALL", 60f, 34f);

        var ground = ToolToggle(nodeRT, "ON GROUND  (grass + scenery hug it)", 396f);

        var bHead = HRow(nodeRT, 30f);
        Label(bHead, "BANK", 17, UITheme.TextDim, FontStyles.Bold, 30f, 150f);
        var bValue = Label(bHead, "0°", 19, UITheme.Text, FontStyles.Bold, 30f, 200f, TextAlignmentOptions.Right);
        var bSlider = ToolSlider(nodeRT, 396f);
        var bRow = HRow(nodeRT, 34f);
        var bm = ToolButton(bRow, "-15", 90f, 34f);
        var bf = ToolButton(bRow, "FLAT", 90f, 34f);
        var bl = ToolButton(bRow, "FLIP", 90f, 34f);
        var bp = ToolButton(bRow, "+15", 90f, 34f);
        var del = ToolButton(nodeRT, "DELETE NODE", 396f, 38f, danger: true);

        // BUILD — pinned footer, always on screen (never scrolls away).
        var footer = Rect(panelRT, "Footer");
        footer.anchorMin = new Vector2(0f, 0f); footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = Vector2.zero;
        footer.sizeDelta = new Vector2(0f, 186f);
        var fLine = Img(footer, "Line", null, UITheme.Line);
        fLine.rectTransform.anchorMin = new Vector2(0f, 1f); fLine.rectTransform.anchorMax = new Vector2(1f, 1f);
        fLine.rectTransform.pivot = new Vector2(0.5f, 1f);
        fLine.rectTransform.sizeDelta = new Vector2(-44f, 1f);
        var fv = footer.gameObject.AddComponent<VerticalLayoutGroup>();
        fv.padding = new RectOffset(22, 22, 14, 18);
        fv.spacing = 8f;
        fv.childControlWidth = true; fv.childControlHeight = true;
        fv.childForceExpandWidth = true; fv.childForceExpandHeight = false;
        var add  = ToolButton(footer, "ADD NODE", 396f, 40f);
        var test = ToolButton(footer, "TEST DRIVE", 396f, 56f, accent: true);
        var back = ToolButton(footer, "BACK TO MENU", 396f, 38f);
        fLine.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        var help = Label(content,
            "<color=#B06BFF>MOUSE</color>  drag node · right-drag / WASD pan · wheel zoom · Q/E orbit · R/F tilt\n" +
            "<color=#B06BFF>KEYS</color>  G grid · Z undo · Del delete · Esc menu\n" +
            "<color=#B06BFF>PAD</color>  R-stick move · RT/LT height · X/Y bank · LB/RB pick · A add · B delete · L3 undo · R3 grid",
            14, UITheme.TextFaint, FontStyles.Normal, 110f);
        help.textWrappingMode = TextWrappingModes.Normal;

        // ── Open dialog ──────────────────────────────────────────────────────
        var dlgRT = Stretch(Rect(ct, "OpenDialog"));
        var dlg = dlgRT.gameObject.AddComponent<CanvasGroup>();
        dlg.alpha = 0f; dlg.blocksRaycasts = false;
        var dim = Img(dlgRT, "Dim", null, new Color(0.01f, 0.005f, 0.03f, 0.7f), false, true);
        Stretch(dim.rectTransform);
        var card = Card(dlgRT, "Card", C, C, Vector2.zero, new Vector2(1040f, 660f));
        var dTitle = Text(card, "Title", "OPEN TRACK", 40, UITheme.Text, FontStyles.Bold | FontStyles.Italic);
        Place(dTitle.rectTransform, TL, TL, new Vector2(30f, -24f), new Vector2(600f, 52f));

        var listRT = Place(Rect(card, "List"), TL, TL, new Vector2(24f, -92f), new Vector2(460f, 540f));
        var listScroll = listRT.gameObject.AddComponent<ScrollRect>();
        listScroll.horizontal = false; listScroll.scrollSensitivity = 30f;
        listScroll.movementType = ScrollRect.MovementType.Clamped;
        var lbg = Img(listRT, "BG", "ui_round", UITheme.Alpha(Color.black, 0.3f), true, true);
        Stretch(lbg.rectTransform);
        var lvp = Stretch(Rect(listRT, "Viewport"), 6, 6, 6, 6);
        lvp.gameObject.AddComponent<RectMask2D>();
        var lContent = Rect(lvp, "Content");
        lContent.anchorMin = new Vector2(0f, 1f); lContent.anchorMax = new Vector2(1f, 1f);
        lContent.pivot = new Vector2(0.5f, 1f); lContent.sizeDelta = Vector2.zero;
        var lv = lContent.gameObject.AddComponent<VerticalLayoutGroup>();
        lv.padding = new RectOffset(8, 8, 8, 8); lv.spacing = 6f;
        lv.childControlWidth = true; lv.childControlHeight = true;
        lv.childForceExpandWidth = true; lv.childForceExpandHeight = false;
        lContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        listScroll.viewport = lvp; listScroll.content = lContent;

        var entry = ToolButton(lContent, "MAP NAME", 420f, 42f);
        var entryLbl = entry.GetComponentInChildren<TextMeshProUGUI>();
        entryLbl.alignment = TextAlignmentOptions.MidlineLeft;
        entryLbl.rectTransform.offsetMin = new Vector2(16f, 0f);
        var header = Label(lContent, "SECTION", 15, UITheme.Accent, FontStyles.Bold, 30f);
        header.characterSpacing = 5f;

        var dPreview = Preview(card, TR, TR, new Vector2(-24f, -92f), new Vector2(510f, 340f));
        var dPTitle = Text(card, "PreviewTitle", "", 30, UITheme.Text, FontStyles.Bold | FontStyles.Italic);
        Place(dPTitle.rectTransform, TR, TR, new Vector2(-24f, -446f), new Vector2(510f, 40f));
        var dPStats = Text(card, "PreviewStats", "", 16, UITheme.TextDim, FontStyles.Bold);
        dPStats.characterSpacing = 3f;
        Place(dPStats.rectTransform, TR, TR, new Vector2(-24f, -490f), new Vector2(510f, 24f));
        var close = ToolButton(card, "CLOSE", 180f, 44f);
        Place((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(180f, 44f));

        // ── Wire ─────────────────────────────────────────────────────────────
        var panel = canvas.gameObject.AddComponent<MapEditorPanel>();
        panel.nameInput = input; panel.messageText = msg;
        panel.saveButton = save; panel.openButton = open; panel.newButton = neu;
        panel.gridValue = gValue; panel.gridMinus = gMinus; panel.gridPlus = gPlus; panel.undoButton = undo;
        panel.snapToggle = snap; panel.gridToggle = showG;
        panel.nodeGroup = nodeGroup; panel.nodeHint = hint.gameObject; panel.nodeHeader = nodeHeader;
        panel.xValue = xVal; panel.yValue = yVal; panel.zValue = zVal; panel.yChip = yChip;
        panel.xMinus = xm; panel.xPlus = xp; panel.yMinus = ym; panel.yPlus = yp; panel.zMinus = zm; panel.zPlus = zp;
        panel.widthValue = wValue; panel.widthSlider = wSlider; panel.widthAll = wAll;
        panel.sceneryValue = sValue; panel.sceneryPrev = sPrev; panel.sceneryNext = sNext; panel.sceneryAll = sAll;
        panel.groundToggle = ground;
        panel.bankValue = bValue; panel.bankSlider = bSlider;
        panel.bankMinus15 = bm; panel.bankFlat = bf; panel.bankFlip = bl; panel.bankPlus15 = bp;
        panel.deleteButton = del;
        panel.addButton = add; panel.testButton = test; panel.backButton = back;
        panel.prevNodeButton = prev; panel.nextNodeButton = next;
        panel.dialog = dlg; panel.listRoot = lContent; panel.entryTemplate = entry; panel.headerTemplate = header;
        panel.dialogClose = close; panel.dialogPreview = dPreview; panel.dialogPreviewTitle = dPTitle; panel.dialogPreviewStats = dPStats;
        return panel;
    }

    // ── Tool layout helpers ──────────────────────────────────────────────────
    static TextMeshProUGUI Label(Transform parent, string text, float size, Color c, FontStyles style,
                                 float height, float width = -1f, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var t = Text(parent, "Label", text, size, c, style, align);
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = height; le.minHeight = height;
        if (width > 0f) { le.preferredWidth = width; le.minWidth = width; }
        else le.flexibleWidth = 1f;
        return t;
    }

    static void Section(Transform parent, string text)
    {
        var s = Rect(parent, "Section_" + text);
        var le = s.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 34f; le.minHeight = 34f;
        var t = Text(s, "Text", text, 15, UITheme.Accent, FontStyles.Bold);
        t.characterSpacing = 8f;
        Place(t.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 2f), new Vector2(200f, 22f));
        var line = Img(s, "Line", null, UITheme.Line);
        line.rectTransform.anchorMin = new Vector2(0f, 0f); line.rectTransform.anchorMax = new Vector2(1f, 0f);
        line.rectTransform.pivot = new Vector2(0.5f, 0f);
        line.rectTransform.sizeDelta = new Vector2(0f, 1f);
    }

    static RectTransform HRow(Transform parent, float h)
    {
        var r = Rect(parent, "Row");
        var le = r.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = h; le.minHeight = h;
        var hl = r.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = true; hl.childControlHeight = false;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        return r;
    }

    static TextMeshProUGUI AxisRow(Transform parent, string axis, string chip, out Button minus, out Button plus)
    {
        var row = HRow(parent, 36f);
        if (chip != null)
        {
            var ch = Img(row, "Chip", "ui_circle", Color.green);
            ch.name = "Chip";
            var le = ch.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 16f; le.preferredHeight = 16f; le.minWidth = 16f;
            ch.rectTransform.sizeDelta = new Vector2(16f, 16f);
        }
        Label(row, axis, 17, UITheme.TextDim, FontStyles.Bold, 36f, chip != null ? 110f : 134f);
        var val = Label(row, "0.0", 20, UITheme.Text, FontStyles.Bold, 36f, 130f, TextAlignmentOptions.MidlineRight);
        minus = ToolButton(row, "-", 44f, 34f);
        plus  = ToolButton(row, "+", 44f, 34f);
        return val;
    }
}
