using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static MenuBaker;

/// Bakes every menu scene / in-game menu canvas from the MenuBaker kit.
/// Called by the three builder menus (Racing Game ▸ Build All Scenes,
/// Multiplayer ▸ Build Full Multiplayer, Map Editor ▸ Build Map Editor Scenes).
public static class MenuScenes
{
    static readonly Vector2 TL = new Vector2(0f, 1f);
    static readonly Vector2 TR = new Vector2(1f, 1f);
    static readonly Vector2 BL = new Vector2(0f, 0f);
    static readonly Vector2 BR = new Vector2(1f, 0f);
    static readonly Vector2 C  = new Vector2(0.5f, 0.5f);

    static void Spacer(Transform rows, float h)
    {
        var s = Rect(rows, "Spacer");
        var le = s.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = h; le.minHeight = h;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MAIN MENU
    // ══════════════════════════════════════════════════════════════════════════
    public static void BuildMainMenu(string path)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EnsureArt();

        var cam = MenuCamera(drift: false, startYaw: 0f, fov: 45f);
        SceneRoot(true);
        EventSystem();

        // Showcase dip-to-black lives UNDER the menu canvas.
        var fadeCanvas = Canvas("ShowcaseFadeCanvas", 5);
        var fade = Img(fadeCanvas.transform, "Fade", null, Color.black);
        Stretch(fade.rectTransform);

        var canvas = Canvas("MenuCanvas", 10);
        var ct = canvas.transform;
        Shades(ct, 1050f, 0.72f);

        // ── Wheel group ──────────────────────────────────────────────────────
        var wheelRT = Stretch(Rect(ct, "Wheel"));
        var wheel = wheelRT.gameObject.AddComponent<CanvasGroup>();

        var logo = Text(wheelRT, "Logo", "SPATIAL <color=#B06BFF>DRIFT</color>", 66, UITheme.Text,
                        FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Right);
        Place(logo.rectTransform, TR, TR, new Vector2(-84f, -52f), new Vector2(900f, 84f));
        var tag = Text(wheelRT, "Tagline", "HIGH-SPEED DRIFT RACING", 18, UITheme.TextDim, FontStyles.Bold, TextAlignmentOptions.Right);
        tag.characterSpacing = 10f;
        Place(tag.rectTransform, TR, TR, new Vector2(-88f, -134f), new Vector2(900f, 26f));

        var items = Place(Rect(wheelRT, "Items"), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(60f, 20f), new Vector2(10f, 10f));
        var bar = Img(items, "SelectBar", "ui_grad_h", UITheme.Alpha(UITheme.Accent, 0.55f));
        Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(520f, 78f));
        AddShear(bar, 0.35f);
        var barEdge = Img(bar.transform, "Edge", null, UITheme.AccentHot);
        barEdge.rectTransform.anchorMin = new Vector2(0f, 0f); barEdge.rectTransform.anchorMax = new Vector2(0f, 1f);
        barEdge.rectTransform.pivot = new Vector2(0f, 0.5f);
        barEdge.rectTransform.sizeDelta = new Vector2(7f, 0f);
        AddShear(barEdge, 0.35f);

        string[] labels = { "SINGLE PLAYER", "MULTIPLAYER", "GARAGE", "SHIFTER LAB", "MAP EDITOR", "OPTIONS", "EXIT" };
        string[] scenes = { "MapSelect", "Lobby", MainMenuCarousel.GarageTarget, "ShifterLab", "MapEditor", "Options", "" };
        string[] descs  =
        {
            "Race named rivals or chase the clock — on any circuit, built-in or your own.",
            "Host an online lobby or join one with a six-character code.",
            "Pick your paint and set your driver name.",
            "Design your own shift gate — where every gear lives is up to you.",
            "Build a circuit node by node, then test-drive it instantly.",
            "Display, audio, gameplay and controls.",
            "Close Spatial Drift.",
        };
        var texts = new TextMeshProUGUI[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            var t = Text(items, "Item_" + labels[i], labels[i], 56, Color.white, FontStyles.Bold | FontStyles.Italic);
            t.characterSpacing = 2f;
            Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(820f, 74f));
            t.alignment = TextAlignmentOptions.MidlineLeft;
            texts[i] = t;
        }
        var desc = Text(wheelRT, "Description", "", 22, UITheme.TextDim);
        Place(desc.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, -38f), new Vector2(760f, 30f));

        // Profile chip (top-left).
        var chip = Card(wheelRT, "ProfileChip", TL, TL, new Vector2(60f, -44f), new Vector2(380f, 70f), accentTop: false);
        var sw = Img(chip, "Paint", "ui_circle", PlayerProfile.LocalColor);
        Place(sw.rectTransform, new Vector2(0f, 0.5f), C, new Vector2(38f, 0f), new Vector2(30f, 30f));
        var cap = Text(chip, "Caption", "DRIVER", 14, UITheme.TextFaint, FontStyles.Bold);
        cap.characterSpacing = 6f;
        Place(cap.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(66f, 13f), new Vector2(290f, 18f));
        var pname = Text(chip, "Name", "DRIVER-0000", 24, UITheme.Text, FontStyles.Bold);
        Place(pname.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(66f, -10f), new Vector2(300f, 30f));

        // ── Title page ───────────────────────────────────────────────────────
        var titleRT = Stretch(Rect(ct, "TitlePage"));
        var title = titleRT.gameObject.AddComponent<CanvasGroup>();
        var big = Text(titleRT, "Logo", "SPATIAL <color=#B06BFF>DRIFT</color>", 132, UITheme.Text,
                       FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
        Place(big.rectTransform, C, C, new Vector2(0f, 300f), new Vector2(1700f, 160f));   // above the car
        var ul = Img(titleRT, "Underline", null, UITheme.Accent);
        Place(ul.rectTransform, C, C, new Vector2(0f, 205f), new Vector2(420f, 6f));
        AddShear(ul, 1f);
        var press = Text(titleRT, "PressStart", "PRESS ANY BUTTON", 28, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
        press.characterSpacing = 14f;
        Place(press.rectTransform, C, C, new Vector2(0f, -400f), new Vector2(900f, 40f));
        var copy = Text(titleRT, "Copyright", "© CUBED PLUTO", 16, UITheme.TextFaint, FontStyles.Bold, TextAlignmentOptions.Center);
        copy.characterSpacing = 8f;
        Place(copy.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(600f, 24f));

        // ── Garage page ──────────────────────────────────────────────────────
        var garage = Screen(ct, "Garage", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110f, -30f),
                            new Vector2(660f, 300f), showOnStart: false, scroll: false, out var gRows);
        var gHead = Text(garage.transform, "GarageTitle", "GARAGE", 62, UITheme.Text, FontStyles.Bold | FontStyles.Italic);
        Place(gHead.rectTransform, TL, new Vector2(0f, 0f), new Vector2(0f, 20f), new Vector2(660f, 76f));
        var gSub = Text(garage.transform, "GarageSub", "YOUR CAR  ·  YOUR NAME", 18, UITheme.TextDim, FontStyles.Bold);
        gSub.characterSpacing = 8f;
        Place(gSub.rectTransform, TL, new Vector2(0f, 0f), new Vector2(4f, 96f), new Vector2(660f, 26f));
        var gName  = TextRow(gRows, "DRIVER NAME", "Shown in lobbies and on the results screen.  Enter to type, Enter to confirm.");
        var gPaint = StepperRow(gRows, "PAINT", "Your paint — used in every mode, online too.", 360f, PlayerProfile.PlayerPalette.Length);
        Spacer(gRows, 6f);
        var gDone  = ButtonRow(gRows, "DONE", "Back to the menu.", false, RowH, Sfx.Back);
        garage.items.AddRange(new MenuRow[] { gName, gPaint, gDone });
        garage.hintText = HintLine(garage.transform, new Vector2(0f, -110f), 660f);
        garage.hintText.rectTransform.anchorMin = garage.hintText.rectTransform.anchorMax = BL;

        var prompts = Prompts(ct);

        // ── Wire ─────────────────────────────────────────────────────────────
        var menu = new GameObject("MainMenu").AddComponent<MainMenuCarousel>();
        menu.itemTexts = texts; menu.sceneNames = scenes; menu.descriptions = descs;
        menu.wheelGroup = wheel; menu.selectBar = bar.rectTransform; menu.descText = desc;
        menu.titlePage = title; menu.pressStart = press;
        menu.garageScreen = garage; menu.garageName = gName; menu.garagePaint = gPaint; menu.garageDone = gDone;
        menu.profileName = pname; menu.profileSwatch = sw; menu.prompts = prompts;

        var showcase = new GameObject("CarShowcase").AddComponent<CarShowcase>();
        showcase.cam = cam;
        showcase.shotFade = fade;
        menu.showcase = showcase;

        SaveScene(path);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SINGLE PLAYER (MapSelect)
    // ══════════════════════════════════════════════════════════════════════════
    public static void BuildSinglePlayer(string path)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EnsureArt();
        var cam = MenuCamera(drift: true, startYaw: 30f);
        SceneRoot(true);
        EventSystem();

        var canvas = Canvas("MenuCanvas", 10);
        var ct = canvas.transform;
        Shades(ct);
        Header(ct, "SPATIAL DRIFT   /   SINGLE PLAYER", "RACE SETUP");

        var screen = Screen(ct, "Setup", TL, TL, new Vector2(110f, -250f), new Vector2(720f, 620f),
                            showOnStart: true, scroll: false, out var rows);
        var map     = StepperRow(rows, "CIRCUIT", "Built-in circuits, your editor draft and every map you've saved.", 400f);
        var mode    = StepperRow(rows, "MODE", "Race the grid, or run a solo Time Trial against your ghost.", 400f);
        var weather = StepperRow(rows, "WEATHER", "Sets the sky, light and fog for the race — previewed behind this menu.", 400f);
        var rivals  = StepperRow(rows, "RIVALS", "How many named AI drivers line up on the grid.", 400f);
        var diff    = StepperRow(rows, "DIFFICULTY", "How hard your rivals push — Ace drivers shift and drift like pros.", 400f);
        var laps    = StepperRow(rows, "LAPS", "Race distance.", 400f);
        Spacer(rows, 10f);
        var start   = ButtonRow(rows, "START RACE", "Wipe into the race.", primary: true, sound: Sfx.Start);
        screen.items.AddRange(new MenuRow[] { map, mode, weather, rivals, diff, laps, start });
        screen.hintText = HintLine(ct, new Vector2(110f, 96f), 720f);

        // Map card.
        var card = Card(ct, "CircuitCard", TR, TR, new Vector2(-110f, -250f), new Vector2(820f, 700f));
        CardTitle(card, "CIRCUIT");
        var counter = Text(card, "Counter", "01 / 01", 20, UITheme.TextDim, FontStyles.Bold, TextAlignmentOptions.Right);
        Place(counter.rectTransform, TR, TR, new Vector2(-28f, -26f), new Vector2(200f, 28f));
        var preview = Preview(card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(764f, 360f));
        var mTitle = Text(card, "MapTitle", "SPEED CIRCUIT", 44, UITheme.Text, FontStyles.Bold | FontStyles.Italic);
        Place(mTitle.rectTransform, TL, TL, new Vector2(28f, -440f), new Vector2(764f, 54f));
        var mTag = Text(card, "MapTag", "BUILT-IN CIRCUIT", 16, UITheme.Accent, FontStyles.Bold);
        mTag.characterSpacing = 8f;
        Place(mTag.rectTransform, TL, TL, new Vector2(30f, -494f), new Vector2(500f, 22f));
        var sLen   = Stat(card, "LENGTH",    new Vector2(30f,  -530f));
        var sClimb = Stat(card, "CLIMB",     new Vector2(220f, -530f));
        var sWidth = Stat(card, "WIDTH",     new Vector2(410f, -530f));
        var pbL    = Text(card, "PBLabel", "BEST LAP", 16, UITheme.TextFaint, FontStyles.Bold, TextAlignmentOptions.Right);
        pbL.characterSpacing = 5f;
        Place(pbL.rectTransform, TR, TR, new Vector2(-30f, -530f), new Vector2(260f, 20f));
        var pbV    = Text(card, "PBValue", "NO TIME SET", 34, UITheme.Gold, FontStyles.Bold, TextAlignmentOptions.Right);
        Place(pbV.rectTransform, TR, TR, new Vector2(-30f, -552f), new Vector2(300f, 44f));
        var pbP    = Text(card, "PBPure", "", 17, UITheme.Cyan, FontStyles.Bold, TextAlignmentOptions.Right);
        pbP.characterSpacing = 3f;
        Place(pbP.rectTransform, TR, TR, new Vector2(-30f, -598f), new Vector2(400f, 22f));
        var blurb  = Text(card, "ModeBlurb", "", 20, UITheme.TextDim);
        blurb.textWrappingMode = TextWrappingModes.Normal;
        Place(blurb.rectTransform, BL, BL, new Vector2(30f, 22f), new Vector2(760f, 52f));
        blurb.alignment = TextAlignmentOptions.BottomLeft;

        var prompts = Prompts(ct);

        var sp = new GameObject("SinglePlayerMenu").AddComponent<SinglePlayerMenu>();
        sp.screen = screen; sp.mapRow = map; sp.modeRow = mode; sp.weatherRow = weather;
        sp.rivalsRow = rivals; sp.difficultyRow = diff; sp.lapsRow = laps; sp.startButton = start;
        sp.preview = preview; sp.mapTitle = mTitle; sp.mapTag = mTag; sp.mapCounter = counter;
        sp.statLength = sLen; sp.statClimb = sClimb; sp.statWidth = sWidth;
        sp.pbLabel = pbL; sp.pbValue = pbV; sp.pbPure = pbP; sp.modeBlurb = blurb;
        sp.backdrop = cam.GetComponent<MenuBackdrop>(); sp.prompts = prompts;

        SaveScene(path);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  OPTIONS
    // ══════════════════════════════════════════════════════════════════════════
    public static void BuildOptions(string path)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EnsureArt();
        MenuCamera(drift: true, startYaw: 140f);
        SceneRoot(true);
        EventSystem();

        var canvas = Canvas("MenuCanvas", 10);
        var ct = canvas.transform;
        Shades(ct);
        Header(ct, "SPATIAL DRIFT   /   OPTIONS", "OPTIONS");

        // Tab bar.
        string[] tabs = { "DISPLAY", "AUDIO", "GAMEPLAY", "CONTROLS" };
        float[] tabX = { 70f, 250f, 440f, 650f };
        var tabBar = Place(Rect(ct, "Tabs"), TL, TL, new Vector2(110f, -196f), new Vector2(760f, 48f));
        var tabLabels = new TextMeshProUGUI[tabs.Length];
        for (int i = 0; i < tabs.Length; i++)
        {
            var t = Text(tabBar, "Tab_" + tabs[i], tabs[i], 24, UITheme.TextFaint, FontStyles.Bold, TextAlignmentOptions.Center);
            t.characterSpacing = 6f;
            Place(t.rectTransform, TL, new Vector2(0.5f, 1f), new Vector2(tabX[i], 0f), new Vector2(180f, 36f));
            tabLabels[i] = t;
        }
        var under = Img(tabBar, "Underline", null, UITheme.Accent);
        Place(under.rectTransform, TL, new Vector2(0.5f, 1f), new Vector2(tabX[0], -38f), new Vector2(120f, 4f));

        Vector2 pagePos = new Vector2(110f, -270f), pageSize = new Vector2(760f, 600f);
        var display = Screen(ct, "Page_Display", TL, TL, pagePos, pageSize, false, true, out var dRows);
        var res     = StepperRow(dRows, "RESOLUTION", "Screen resolution.  Applies immediately.");
        var dmode   = StepperRow(dRows, "DISPLAY MODE", "Fullscreen takes over the display; Borderless is a fullscreen window; Windowed is a window.");
        var vsync   = StepperRow(dRows, "V-SYNC", "Locks the frame rate to your monitor to stop tearing.");
        var fps     = StepperRow(dRows, "FRAME LIMIT", "Caps the frame rate when V-Sync is off.");
        var quality = StepperRow(dRows, "QUALITY", "Overall graphics preset (shadows, textures, effects).");
        var aa      = StepperRow(dRows, "ANTI-ALIASING", "SMAA smooths jagged edges for a cleaner image.");
        display.items.AddRange(new MenuRow[] { res, dmode, vsync, fps, quality, aa });

        var audio   = Screen(ct, "Page_Audio", TL, TL, pagePos, pageSize, false, true, out var aRows);
        var master  = SliderRow(aRows, "MASTER VOLUME", "Everything.");
        var music   = SliderRow(aRows, "MUSIC", "Menu music.  Drop your own track at Resources/UI/Audio/menu_music.");
        var effects = SliderRow(aRows, "EFFECTS", "Engines, shifts, boosts, tyres and the drift zone.");
        var ui      = SliderRow(aRows, "INTERFACE", "Menu sounds.");
        audio.items.AddRange(new MenuRow[] { master, music, effects, ui });

        var game    = Screen(ct, "Page_Gameplay", TL, TL, pagePos, pageSize, false, true, out var gRows);
        var fov     = SliderRow(gRows, "CAMERA FOV", "Chase-camera field of view.  Wider feels faster; boost still punches it out.");
        var units   = StepperRow(gRows, "SPEED UNITS", "Speedometer units.");
        var mini    = StepperRow(gRows, "MINIMAP", "The circuit map in the top-right corner.");
        var ghost   = StepperRow(gRows, "TIME TRIAL GHOST", "Race a replay of your personal best in Time Trial.");
        game.items.AddRange(new MenuRow[] { fov, units, mini, ghost });

        var controls = Screen(ct, "Page_Controls", TL, TL, pagePos, pageSize, false, false, out _);
        ControlsCard(controls.transform);

        // Description card.
        var desc = Card(ct, "DescribeCard", TR, TR, new Vector2(-110f, -270f), new Vector2(680f, 280f));
        var dTitle = Text(desc, "Title", "", 34, UITheme.Text, FontStyles.Bold | FontStyles.Italic);
        Place(dTitle.rectTransform, TL, TL, new Vector2(30f, -34f), new Vector2(620f, 46f));
        var dBody = Text(desc, "Body", "", 22, UITheme.TextDim);
        dBody.textWrappingMode = TextWrappingModes.Normal;
        Place(dBody.rectTransform, TL, TL, new Vector2(30f, -92f), new Vector2(620f, 160f));
        dBody.alignment = TextAlignmentOptions.TopLeft;

        var modal = Modal(ct);
        var prompts = Prompts(ct);

        var o = new GameObject("Options").AddComponent<OptionsMenu>();
        o.pages = new[] { display, audio, game, controls };
        o.tabLabels = tabLabels; o.tabUnderline = under.rectTransform;
        o.resolutionRow = res; o.displayModeRow = dmode; o.vsyncRow = vsync; o.frameLimitRow = fps;
        o.qualityRow = quality; o.aaRow = aa;
        o.masterRow = master; o.musicRow = music; o.effectsRow = effects; o.interfaceRow = ui;
        o.fovRow = fov; o.unitsRow = units; o.minimapRow = mini; o.ghostRow = ghost;
        o.descTitle = dTitle; o.descBody = dBody; o.modal = modal; o.prompts = prompts;

        SaveScene(path);
    }

    static void ControlsCard(Transform parent)
    {
        var card = Card(parent, "ControlsCard", TL, TL, new Vector2(0f, 0f), new Vector2(1000f, 560f));
        string kb =
            "<color=#B06BFF>KEYBOARD</color>\n\n" +
            "W  ·  throttle          S / SPACE  ·  brake\n" +
            "A / D  ·  steer\n" +
            "ARROWS  ·  shifter gate\n" +
            "BRAKE + STEER  ·  drift\n" +
            "R  ·  restart run (time trial)\n" +
            "ESC  ·  pause";
        string pad =
            "<color=#B06BFF>CONTROLLER</color>\n\n" +
            "RT  ·  throttle          LT  ·  brake\n" +
            "LEFT STICK  ·  steer\n" +
            "RIGHT STICK  ·  shifter gate\n" +
            "LB / RB  ·  paddle shift\n" +
            "LT + STEER  ·  drift (flick to hop)\n" +
            "B  ·  restart run (time trial)\n" +
            "MENU  ·  pause";
        var a = Text(card, "Keyboard", kb, 22, UITheme.Text);
        a.textWrappingMode = TextWrappingModes.Normal;
        Place(a.rectTransform, TL, TL, new Vector2(34f, -34f), new Vector2(450f, 480f));
        a.alignment = TextAlignmentOptions.TopLeft;
        a.lineSpacing = 18f;
        var b = Text(card, "Controller", pad, 22, UITheme.Text);
        b.textWrappingMode = TextWrappingModes.Normal;
        Place(b.rectTransform, TL, TL, new Vector2(510f, -34f), new Vector2(470f, 480f));
        b.alignment = TextAlignmentOptions.TopLeft;
        b.lineSpacing = 18f;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SHIFTER LAB
    // ══════════════════════════════════════════════════════════════════════════
    public static void BuildShifterLab(string path)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EnsureArt();
        MenuCamera(drift: true, startYaw: 250f);
        SceneRoot(true);
        EventSystem();

        var canvas = Canvas("MenuCanvas", 10);
        var ct = canvas.transform;
        Shades(ct, 1300f, 0.8f);
        Header(ct, "SPATIAL DRIFT   /   SHIFTER LAB", "SHIFTER LAB");

        // Grid card.
        var gridCard = Card(ct, "GridCard", TL, TL, new Vector2(110f, -250f), new Vector2(880f, 680f));
        var zone = Text(gridCard, "Zone", "EDITING GRID", 20, UITheme.TextDim, FontStyles.Bold);
        zone.characterSpacing = 6f;
        Place(zone.rectTransform, TL, TL, new Vector2(28f, -24f), new Vector2(420f, 28f));
        var brush = Text(gridCard, "Brush", "NEXT: GEAR 1", 20, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Right);
        brush.characterSpacing = 4f;
        Place(brush.rectTransform, TR, TR, new Vector2(-28f, -24f), new Vector2(420f, 28f));
        var gridRoot = Place(Rect(gridCard, "Grid"), C, C, new Vector2(0f, 16f), new Vector2(10f, 10f));

        // Cell template.
        var cellRT = Place(Rect(gridRoot, "CellTemplate"), C, C, Vector2.zero, new Vector2(86f, 86f));
        var cellHit = cellRT.gameObject.AddComponent<Image>();
        cellHit.color = Color.clear;
        var body = Stretch(Rect(cellRT, "Body"));
        var cGlow = Img(body, "Glow", "ui_soft", Color.clear, true);
        Stretch(cGlow.rectTransform, -16, -16, -16, -16);
        var cBg = Img(body, "BG", "ui_round", new Color(0.1f, 0.09f, 0.17f, 0.85f), true);
        Stretch(cBg.rectTransform);
        var cStr = Img(body, "Stripes", "ui_stripes", Color.clear);
        cStr.type = Image.Type.Tiled;
        Stretch(cStr.rectTransform, 6, 6, 6, 6);
        var cLbl = Text(body, "Label", "", 40, Color.white, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
        Stretch(cLbl.rectTransform);
        var cell = cellRT.gameObject.AddComponent<ShifterCell>();
        cell.bg = cBg; cell.stripes = cStr; cell.glow = cGlow; cell.label = cLbl;

        var conn = Img(gridRoot, "ConnectorTemplate", null, UITheme.Alpha(UITheme.Accent, 0.35f));
        conn.rectTransform.sizeDelta = new Vector2(40f, 8f);

        var cursor = Img(gridRoot, "Cursor", "ui_round_line", UITheme.AccentHot, true);
        cursor.rectTransform.sizeDelta = new Vector2(100f, 100f);
        var cGlow2 = Img(cursor.transform, "CursorGlow", "ui_soft", UITheme.Alpha(UITheme.Accent, 0.35f), true);
        Stretch(cGlow2.rectTransform, -18, -18, -18, -18);
        cGlow2.transform.SetAsFirstSibling();
        var held = Text(cursor.transform, "Held", "", 46, UITheme.Gold, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
        Stretch(held.rectTransform);

        var ball = Img(gridRoot, "Ball", "ui_glow", UITheme.Alpha(UITheme.Cyan, 0.9f));
        ball.rectTransform.sizeDelta = new Vector2(84f, 84f);
        var ballDot = Img(ball.transform, "Dot", "ui_circle", Color.white);
        Place(ballDot.rectTransform, C, C, Vector2.zero, new Vector2(34f, 34f));

        // Test overlay strip on the grid card.
        var testRT = Place(Rect(gridCard, "TestOverlay"), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(820f, 60f));
        var testGroup = testRT.gameObject.AddComponent<CanvasGroup>();
        testGroup.blocksRaycasts = false;
        var testBg = Img(testRT, "BG", "ui_round", UITheme.Alpha(UITheme.Cyan, 0.16f), true);
        Stretch(testBg.rectTransform);
        var testLbl = Text(testRT, "Label", "TEST DRIVE  ·  ARROWS / RIGHT STICK  ·  BUMPERS = PADDLES", 18, UITheme.Cyan, FontStyles.Bold);
        testLbl.characterSpacing = 4f;
        Place(testLbl.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(640f, 30f));
        var testGear = Text(testRT, "Gear", "1", 44, UITheme.Text, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Right);
        Place(testGear.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-22f, 0f), new Vector2(120f, 56f));

        // Actions.
        var actions = Screen(ct, "Actions", TR, TR, new Vector2(-110f, -250f), new Vector2(600f, 520f),
                             showOnStart: true, scroll: false, out var aRows);
        var test   = ButtonRow(aRows, "TEST DRIVE", "Drive the shift ball with the real in-race shifter on this layout.");
        var preset = StepperRow(aRows, "PRESET", "Pick a starting layout, then press A / Enter to load it.", 260f);
        var gw     = StepperRow(aRows, "GRID WIDTH", "Columns of nodes — widen for big H-gates.", 200f);
        var gh     = StepperRow(aRows, "GRID HEIGHT", "Rows of nodes — go tall for a vertical / sequential gate.", 200f);
        var save   = ButtonRow(aRows, "SAVE GATE", "Every car you drive will use this gate.", primary: true);
        var revert = ButtonRow(aRows, "REVERT CHANGES", "Go back to your last saved gate.", sound: Sfx.Back);
        var reset  = ButtonRow(aRows, "RESET TO H-GATE", "Delete your custom gate and use the classic H-gate.");
        actions.items.AddRange(new MenuRow[] { test, preset, gw, gh, save, revert, reset });
        actions.hintText = HintLine(ct, new Vector2(1210f, 118f), 600f);

        // Gear tray.
        var tray = Card(ct, "GearTray", TR, TR, new Vector2(-110f, -790f), new Vector2(600f, 120f));
        CardTitle(tray, "GEARS", -20f);
        var chips = new Image[10];
        var chipLbls = new TextMeshProUGUI[10];
        for (int i = 0; i < 10; i++)
        {
            var ch = Img(tray, "Gear" + (i + 1), "ui_round", UITheme.Alpha(Color.white, 0.07f), true);
            Place(ch.rectTransform, TL, TL, new Vector2(24f + i * 56f, -52f), new Vector2(48f, 48f));
            var l = Text(ch.transform, "N", (i + 1).ToString(), 24, UITheme.TextFaint, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
            Stretch(l.rectTransform);
            chips[i] = ch; chipLbls[i] = l;
        }

        var status = Text(ct, "Status", "", 22, UITheme.Good, FontStyles.Bold);
        status.characterSpacing = 3f;
        Place(status.rectTransform, BL, BL, new Vector2(110f, 104f), new Vector2(1000f, 32f));

        var modal = Modal(ct);
        var prompts = Prompts(ct);

        var lab = new GameObject("ShifterLab").AddComponent<ShifterDesigner>();
        lab.gridRoot = gridRoot; lab.cellTemplate = cell; lab.connectorTemplate = conn;
        lab.cursor = cursor.rectTransform; lab.cursorHeld = held; lab.ball = ball.rectTransform;
        lab.actions = actions; lab.testButton = test; lab.saveButton = save; lab.revertButton = revert;
        lab.resetButton = reset; lab.presetRow = preset; lab.widthRow = gw; lab.heightRow = gh;
        lab.trayChips = chips; lab.trayLabels = chipLbls;
        lab.statusText = status; lab.zoneLabel = zone; lab.brushText = brush; lab.testGearText = testGear;
        lab.testOverlay = testGroup; lab.modal = modal; lab.prompts = prompts;

        SaveScene(path);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  In-race menus (shared pieces)
    // ══════════════════════════════════════════════════════════════════════════
    static Canvas RaceCanvas(string name)
    {
        EnsureArt();
        var c = Canvas(name, 300);
        return c;
    }

    static RectTransform PauseRoot(Transform ct, out CanvasGroup group, out TextMeshProUGUI title, out TextMeshProUGUI sub)
    {
        var root = Stretch(Rect(ct, "Pause"));
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false;
        var dim = Img(root, "Dim", null, new Color(0.01f, 0.005f, 0.03f, 0.55f), false, true);
        Stretch(dim.rectTransform);
        Shades(root, 1100f, 0.85f);
        title = Text(root, "Title", "PAUSED", 76, UITheme.Text, FontStyles.Bold | FontStyles.Italic);
        Place(title.rectTransform, TL, TL, new Vector2(106f, -96f), new Vector2(900f, 90f));
        var bar = Img(root, "AccentBar", null, UITheme.Accent);
        Place(bar.rectTransform, TL, TL, new Vector2(110f, -192f), new Vector2(120f, 6f));
        AddShear(bar, 1f);
        sub = Text(root, "Sub", "", 19, UITheme.TextDim, FontStyles.Bold);
        sub.characterSpacing = 5f;
        Place(sub.rectTransform, TL, TL, new Vector2(110f, -208f), new Vector2(1400f, 28f));
        return root;
    }

    static PauseSettingsPanel SettingsPanel(Transform parent)
    {
        var s = Screen(parent, "Settings", TL, TL, new Vector2(740f, -262f), new Vector2(640f, 480f), false, false, out var rows);
        var bg = Card(s.transform, "Card", TL, TL, new Vector2(-30f, 24f), new Vector2(700f, 540f));
        bg.SetAsFirstSibling();
        CardTitle(bg, "SETTINGS");
        rows.anchoredPosition = new Vector2(0f, -40f);
        var master  = SliderRow(rows, "MASTER VOLUME", "Everything.");
        var effects = SliderRow(rows, "EFFECTS", "Engines, shifts, boosts and tyres.");
        var fov     = SliderRow(rows, "CAMERA FOV", "Chase-camera field of view — applies right away.");
        var units   = StepperRow(rows, "SPEED UNITS", "Speedometer units.");
        var mini    = StepperRow(rows, "MINIMAP", "The circuit map in the corner.");
        var ghost   = StepperRow(rows, "TT GHOST", "Your personal-best replay in Time Trial.");
        s.items.AddRange(new MenuRow[] { master, effects, fov, units, mini, ghost });
        var panel = s.gameObject.AddComponent<PauseSettingsPanel>();
        panel.screen = s; panel.master = master; panel.effects = effects; panel.fov = fov;
        panel.units = units; panel.minimap = mini; panel.ghost = ghost;
        return panel;
    }

    static RectTransform ResultsRoot(Transform ct, out CanvasGroup group, out RectTransform placeBlock,
                                     out TextMeshProUGUI place, out TextMeshProUGUI placeSub)
    {
        var root = Stretch(Rect(ct, "Results"));
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false;
        var dim = Img(root, "Dim", null, new Color(0.01f, 0.005f, 0.03f, 0.62f), false, true);
        Stretch(dim.rectTransform);
        Shades(root, 1300f, 0.7f);
        var crumb = Text(root, "Crumb", "RESULTS", 19, UITheme.TextDim, FontStyles.Bold);
        crumb.characterSpacing = 10f;
        Place(crumb.rectTransform, TL, TL, new Vector2(112f, -58f), new Vector2(600f, 26f));
        placeBlock = Place(Rect(root, "PlaceBlock"), TL, new Vector2(0f, 0.5f), new Vector2(106f, -150f), new Vector2(1000f, 120f));
        place = Text(placeBlock, "Place", "VICTORY", 112, UITheme.Gold, FontStyles.Bold | FontStyles.Italic);
        Place(place.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1000f, 130f));
        placeSub = Text(root, "PlaceSub", "", 20, UITheme.TextDim, FontStyles.Bold);
        placeSub.characterSpacing = 6f;
        Place(placeSub.rectTransform, TL, TL, new Vector2(112f, -222f), new Vector2(1000f, 28f));
        return root;
    }

    static ResultRow[] Standings(RectTransform parent, Vector2 pos, float width, string lastCol)
    {
        var card = Card(parent, "Standings", TL, TL, pos, new Vector2(width, 590f));
        var heads = new[] { ("POS", 28f), ("DRIVER", 150f), ("TIME", width - 380f), (lastCol, width - 190f) };
        foreach (var (h, x) in heads)
        {
            var t = Text(card, "H_" + h, h, 15, UITheme.TextFaint, FontStyles.Bold);
            t.characterSpacing = 6f;
            Place(t.rectTransform, TL, TL, new Vector2(x, -22f), new Vector2(200f, 20f));
        }
        var rows = new ResultRow[8];
        for (int i = 0; i < 8; i++)
        {
            var r = Place(Rect(card, "Row" + (i + 1)), TL, TL, new Vector2(16f, -54f - i * 64f), new Vector2(width - 32f, 58f));
            var g = r.gameObject.AddComponent<CanvasGroup>();
            var hl = Img(r, "Highlight", "ui_round", UITheme.Alpha(Color.white, 0.03f), true);
            Stretch(hl.rectTransform);
            AddShear(hl, 0.15f);
            var pos2 = Text(r, "Pos", "1ST", 28, UITheme.Gold, FontStyles.Bold | FontStyles.Italic);
            Place(pos2.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(100f, 40f));
            var paint = Img(r, "Paint", null, Color.white);
            Place(paint.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(116f, 0f), new Vector2(8f, 34f));
            AddShear(paint, 0.3f);
            var name = Text(r, "Name", "DRIVER", 26, UITheme.Text, FontStyles.Bold);
            Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(136f, 0f), new Vector2(width - 560f, 40f));
            var time = Text(r, "Time", "0:00.00", 26, UITheme.Text, FontStyles.Bold);
            Place(time.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(width - 396f, 0f), new Vector2(190f, 40f));
            var best = Text(r, "Best", "0:00.00", 22, UITheme.TextDim, FontStyles.Bold);
            Place(best.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(width - 206f, 0f), new Vector2(180f, 40f));
            rows[i] = new ResultRow { root = r.gameObject, group = g, paint = paint, highlight = hl, pos = pos2, name = name, time = time, best = best };
        }
        return rows;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SOLO race / test-drive menus
    // ══════════════════════════════════════════════════════════════════════════
    public static RaceMenus BakeRaceMenus()
    {
        var canvas = RaceCanvas("RaceMenusCanvas");
        var ct = canvas.transform;

        var pauseRoot = PauseRoot(ct, out var pauseGroup, out var pTitle, out var pSub);
        var pause = Screen(pauseRoot, "PauseMenu", TL, TL, new Vector2(110f, -262f), new Vector2(560f, 440f), false, false, out var pr);
        var resume   = ButtonRow(pr, "RESUME", "Back to the race.", sound: Sfx.Back);
        var restart  = ButtonRow(pr, "RESTART RACE", "Start this race again from the grid.");
        var settings = ButtonRow(pr, "SETTINGS", "Volume, camera and HUD options.", sound: Sfx.Open);
        var setup    = ButtonRow(pr, "CHANGE SETUP", "Back to race setup — circuit, mode, weather, rivals.");
        var quit     = ButtonRow(pr, "QUIT TO MAIN MENU", "Leave the race.");
        pause.items.AddRange(new MenuRow[] { resume, restart, settings, setup, quit });
        pause.hintText = HintLine(pauseRoot, new Vector2(110f, 100f), 800f);
        var panel = SettingsPanel(pauseRoot);

        var resRoot = ResultsRoot(ct, out var resGroup, out var placeBlock, out var place, out var placeSub);
        var rows = Standings(resRoot, new Vector2(110f, -270f), 1060f, "BEST LAP");

        var laps = Card(resRoot, "YourRace", TR, TR, new Vector2(-110f, -270f), new Vector2(560f, 370f));
        CardTitle(laps, "YOUR RACE");
        var pb = Text(laps, "PBBadge", "NEW PERSONAL BEST", 16, UITheme.Gold, FontStyles.Bold, TextAlignmentOptions.Right);
        pb.characterSpacing = 4f;
        Place(pb.rectTransform, TR, TR, new Vector2(-26f, -24f), new Vector2(360f, 24f));
        var lapsText = Text(laps, "Laps", "", 22, UITheme.Text, FontStyles.Bold);
        lapsText.textWrappingMode = TextWrappingModes.Normal;
        lapsText.lineSpacing = 8f;
        Place(lapsText.rectTransform, TL, TL, new Vector2(28f, -66f), new Vector2(300f, 290f));
        lapsText.alignment = TextAlignmentOptions.TopLeft;
        var total = Stat(laps, "TOTAL", new Vector2(340f, -70f), 200f);
        var bestL = Stat(laps, "BEST LAP", new Vector2(340f, -160f), 200f);
        bestL.color = UITheme.Gold;

        var resScreen = Screen(resRoot, "ResultsMenu", BR, BR, new Vector2(-110f, 116f), new Vector2(560f, 240f), false, false, out var rr);
        var again  = ButtonRow(rr, "RACE AGAIN", "Same circuit, same grid.", primary: true, sound: Sfx.Start);
        var change = ButtonRow(rr, "CHANGE SETUP", "Pick another circuit, mode or weather.");
        var home   = ButtonRow(rr, "MAIN MENU", "Back to the main menu.", sound: Sfx.Back);
        resScreen.items.AddRange(new MenuRow[] { again, change, home });

        var modal = Modal(ct);
        var prompts = Prompts(ct);

        var m = canvas.gameObject.AddComponent<RaceMenus>();
        m.pauseRoot = pauseGroup; m.pauseScreen = pause; m.pauseTitle = pTitle; m.pauseSub = pSub;
        m.resumeButton = resume; m.restartButton = restart; m.settingsButton = settings; m.setupButton = setup; m.quitButton = quit;
        m.settings = panel;
        m.resultsRoot = resGroup; m.resultsScreen = resScreen; m.againButton = again; m.resultsSetupButton = change; m.resultsMenuButton = home;
        m.placeText = place; m.placeSub = placeSub; m.placeBlock = placeBlock; m.rows = rows;
        m.lapsText = lapsText; m.totalText = total; m.bestText = bestL; m.pbBadge = pb;
        m.modal = modal; m.prompts = prompts;
        return m;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MULTIPLAYER race menus
    // ══════════════════════════════════════════════════════════════════════════
    public static MPRaceMenus BakeMPRaceMenus()
    {
        var canvas = RaceCanvas("MPRaceMenusCanvas");
        var ct = canvas.transform;

        var pauseRoot = PauseRoot(ct, out var pauseGroup, out var pTitle, out var pSub);
        var pause = Screen(pauseRoot, "PauseMenu", TL, TL, new Vector2(110f, -262f), new Vector2(560f, 440f), false, false, out var pr);
        var resume   = ButtonRow(pr, "RESUME", "Back to the race — it never stopped.", sound: Sfx.Back);
        var runReset = ButtonRow(pr, "RESTART RUN", "Back to your grid slot; the clock waits until you move.");
        var endRace  = ButtonRow(pr, "END RACE", "Host: stop the race now and go to the results.");
        var settings = ButtonRow(pr, "SETTINGS", "Volume, camera and HUD options.", sound: Sfx.Open);
        var leave    = ButtonRow(pr, "LEAVE MATCH", "Leave this session.");
        pause.items.AddRange(new MenuRow[] { resume, runReset, endRace, settings, leave });
        pause.hintText = HintLine(pauseRoot, new Vector2(110f, 100f), 800f);
        var panel = SettingsPanel(pauseRoot);

        var resRoot = ResultsRoot(ct, out var resGroup, out _, out var place, out var placeSub);
        var rows = Standings(resRoot, new Vector2(110f, -270f), 1060f, "");
        var waiting = Text(resRoot, "Waiting", "", 22, UITheme.TextDim, FontStyles.Bold);
        waiting.characterSpacing = 4f;
        Place(waiting.rectTransform, BL, BL, new Vector2(112f, 110f), new Vector2(1060f, 32f));

        var nextRT = Stretch(Rect(resRoot, "Next"));
        var nextGroup = nextRT.gameObject.AddComponent<CanvasGroup>();
        nextGroup.alpha = 0f;
        var nextCard = Card(nextRT, "NextCard", TR, TR, new Vector2(-110f, -270f), new Vector2(600f, 640f));
        CardTitle(nextCard, "NEXT RACE");
        var host = Screen(nextRT, "HostNext", TR, TR, new Vector2(-130f, -316f), new Vector2(560f, 580f), false, false, out var hr);
        var nMap   = StepperRow(hr, "CIRCUIT", "Any circuit — clients receive custom maps automatically.", 300f);
        var nLaps  = StepperRow(hr, "LAPS", "Race distance.", 300f);
        var nRiv   = StepperRow(hr, "RIVALS", "AI drivers on the grid.", 300f);
        var nDiff  = StepperRow(hr, "DIFFICULTY", "How hard the AI pushes.", 300f);
        var nWeat  = StepperRow(hr, "WEATHER", "Sky, light and fog for everyone.", 300f);
        Spacer(hr, 6f);
        var nStart = ButtonRow(hr, "START NEXT RACE", "Everyone loads straight into the next race.", primary: true, sound: Sfx.Start);
        var nEnd   = ButtonRow(hr, "END MATCH", "Everyone returns to the main menu.", sound: Sfx.Back);
        host.items.AddRange(new MenuRow[] { nMap, nLaps, nRiv, nDiff, nWeat, nStart, nEnd });
        host.hintText = HintLine(nextRT, new Vector2(1210f, 100f), 600f);

        var client = Screen(nextRT, "ClientNext", TR, TR, new Vector2(-130f, -316f), new Vector2(560f, 120f), false, false, out var cr);
        var cLeave = ButtonRow(cr, "LEAVE MATCH", "Return to the main menu.", sound: Sfx.Back);
        client.items.Add(cLeave);

        var modal = Modal(ct);
        var prompts = Prompts(ct);

        var m = canvas.gameObject.AddComponent<MPRaceMenus>();
        m.pauseRoot = pauseGroup; m.pauseScreen = pause; m.pauseTitle = pTitle; m.pauseSub = pSub;
        m.resumeButton = resume; m.restartRunButton = runReset; m.endRaceButton = endRace;
        m.settingsButton = settings; m.leaveButton = leave; m.settings = panel;
        m.resultsRoot = resGroup; m.placeText = place; m.placeSub = placeSub; m.waitingText = waiting; m.rows = rows;
        m.nextPanel = nextGroup; m.hostScreen = host;
        m.nextMap = nMap; m.nextLaps = nLaps; m.nextRivals = nRiv; m.nextDifficulty = nDiff; m.nextWeather = nWeat;
        m.nextStart = nStart; m.endMatchButton = nEnd;
        m.clientScreen = client; m.clientLeave = cLeave;
        m.modal = modal; m.prompts = prompts;
        return m;
    }
}
