using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Zone Race HUD (replaces the speedo / tach / shifter — the whole race is in
/// the zone).  Reads an IZoneRaceView each frame:
///   top-left    your place + lap
///   top-centre  the DEBT duel: places you trail the car ahead | places the car
///               behind trails you (1 = one crash and a pass happens), shield
///   right       the live train — every driver, their debt pips and shields
///   centre      call-outs (HIT! / PASSED VEGA! / SHIELD UP) and the 3-2-1
/// Runtime-built like the other race HUDs, styled with the menu kit.
[DisallowMultipleComponent]
public class ZoneRaceHUD : MonoBehaviour
{
    IZoneRaceView view;
    Canvas canvas;
    RectTransform root;

    TextMeshProUGUI placeText, placeOf, lapText;
    TextMeshProUGUI aheadCaption, aheadName, aheadNote, behindCaption, behindName, behindNote;
    Image[] aheadPips, behindPips;
    Image   youChip, shieldRing;
    TextMeshProUGUI flash, countdown, hint;
    RectTransform flashRT, listRoot;
    readonly List<(GameObject go, Image hl, TextMeshProUGUI pos, Image paint, TextMeshProUGUI name, Image[] pips, Image shield)> rows
        = new List<(GameObject, Image, TextMeshProUGUI, Image, TextMeshProUGUI, Image[], Image)>();

    float flashT, hintT = 7f;
    int   lastPlace;
    float placePunch;

    const int PipCount = 3;

    public void Bind(IZoneRaceView v)
    {
        view = v;
        Build();
        ZoneFeed.Posted += OnPosted;
    }

    void OnDestroy()
    {
        ZoneFeed.Posted -= OnPosted;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void OnPosted(string text, Color c, bool big)
    {
        if (flash == null) return;
        flash.text = text;
        flash.color = c;
        flash.fontSize = big ? 76 : 34;
        flashT = big ? 1.4f : 1.1f;
    }

    // ── Per frame ─────────────────────────────────────────────────────────────
    void LateUpdate()
    {
        if (view == null || canvas == null) return;
        var train = view.Train;
        var me = view.Local;
        float dt = Time.unscaledDeltaTime;

        // Countdown.
        float cd = view.Countdown;
        if (cd > 0f)
        {
            int n = Mathf.CeilToInt(cd);
            countdown.text = n > 3 ? "" : n.ToString();
            float frac = cd - Mathf.Floor(cd);
            countdown.alpha = Mathf.Clamp01(frac * 3f);          // hold, then a quick fade
            countdown.rectTransform.localScale = Vector3.one * (1f + (1f - frac) * 0.25f);
        }
        else countdown.text = "";

        // Call-out.
        if (flashT > 0f)
        {
            flashT -= dt;
            float a = Mathf.Clamp01(flashT / 0.35f);
            flash.alpha = a;
            float punch = Mathf.Clamp01((flashT - 1.0f) / 0.4f);
            flashRT.localScale = Vector3.one * (1f + punch * 0.25f);
        }
        else if (flash.alpha > 0f) flash.alpha = 0f;

        if (hintT > 0f)
        {
            if (view.Racing) hintT -= dt;
            hint.alpha = Mathf.Clamp01(hintT);
        }

        if (train == null || me == null || train.order.Count == 0) return;
        int idx   = train.order.IndexOf(me);
        int place = idx + 1;

        // Place + lap.
        if (place != lastPlace) { if (lastPlace != 0) placePunch = 1f; lastPlace = place; }
        placePunch = Mathf.MoveTowards(placePunch, 0f, dt * 3f);
        placeText.text  = UITheme.Ordinal(place);
        placeText.color = UITheme.PlaceColor(place) == UITheme.TextDim ? UITheme.Text : UITheme.PlaceColor(place);
        placeText.rectTransform.localScale = Vector3.one * (1f + placePunch * 0.3f);
        placeOf.text = $"/ {train.order.Count}";
        var pilot = view.LocalPilot;
        float len = view.TrackLength;
        int lap = pilot != null && len > 1f ? Mathf.Clamp(Mathf.FloorToInt(pilot.RaceDist / len) + 1, 1, view.Laps) : 1;
        lapText.text = view.Finished ? "FINISHED" : $"LAP  {lap} / {view.Laps}";

        // Duel panel.
        var ahead  = idx > 0 ? train.order[idx - 1] : null;
        var behind = idx + 1 < train.order.Count ? train.order[idx + 1] : null;
        if (ahead != null)
        {
            aheadCaption.text = "YOUR DEBT TO";
            aheadName.text    = ahead.name;
            aheadName.color   = PlayerProfile.ColorById(ahead.colorId);
            SetPips(aheadPips, me.debt, train.MaxDebt, UITheme.Bad);
            aheadNote.text = me.debt <= 0 ? "ALONGSIDE — CLEAR THIS ROW AND YOU'RE BY"
                           : me.debt == 1 ? "ON THEIR BUMPER — ONE CRASH AND YOU'RE BY"
                                          : $"THEY NEED {me.debt} CRASHES TO LOSE THE SPOT";
        }
        else
        {
            aheadCaption.text = "LEADING";
            aheadName.text    = "THE TRAIN";
            aheadName.color   = UITheme.Gold;
            SetPips(aheadPips, 0, train.MaxDebt, UITheme.Bad);
            aheadNote.text = "NOBODY TO CATCH — DON'T CRASH";
        }
        if (behind != null)
        {
            behindCaption.text = "THEIR DEBT TO YOU";
            behindName.text    = behind.name;
            behindName.color   = PlayerProfile.ColorById(behind.colorId);
            SetPips(behindPips, behind.debt, train.MaxDebt, UITheme.Good);
            behindNote.text = behind.debt <= 0 ? "ALONGSIDE — THEY'RE COMING BY"
                            : behind.debt == 1 ? (me.shield ? "ON YOUR BUMPER — SHIELD UP" : "ON YOUR BUMPER — ONE CRASH AND THEY PASS")
                                               : $"YOU CAN AFFORD {behind.debt - 1} CRASH{(behind.debt - 1 == 1 ? "" : "ES")}";
        }
        else
        {
            behindCaption.text = "LAST";
            behindName.text    = "—";
            behindName.color   = UITheme.TextDim;
            SetPips(behindPips, 0, train.MaxDebt, UITheme.Good);
            behindNote.text = "NOBODY BEHIND YOU";
        }
        youChip.color = PlayerProfile.ColorById(me.colorId);
        shieldRing.color = me.shield ? UITheme.Alpha(UITheme.Cyan, 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f))
                                     : UITheme.Alpha(Color.white, 0.08f);

        // Train list.
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool on = i < train.order.Count;
            row.go.SetActive(on);
            if (!on) continue;
            var r = train.order[i];
            bool mine = r == me;
            row.pos.text = $"P{i + 1}";
            row.pos.color = UITheme.PlaceColor(i + 1) == UITheme.TextDim ? UITheme.TextDim : UITheme.PlaceColor(i + 1);
            row.paint.color = PlayerProfile.ColorById(r.colorId);
            row.name.text = r.name;
            row.name.color = mine ? UITheme.Text : UITheme.TextDim;
            row.hl.color = mine ? UITheme.Alpha(UITheme.Accent, 0.35f) : UITheme.Alpha(Color.black, 0.35f);
            SetPips(row.pips, i == 0 ? 0 : r.debt, train.MaxDebt, UITheme.Accent);
            row.shield.enabled = r.shield;
        }
    }

    static void SetPips(Image[] pips, int filled, int max, Color c)
    {
        for (int i = 0; i < pips.Length; i++)
        {
            bool used = i < max;
            pips[i].gameObject.SetActive(used);
            if (used) pips[i].color = i < filled ? c : UITheme.Alpha(Color.white, 0.12f);
        }
    }

    // ── Build ─────────────────────────────────────────────────────────────────
    void Build()
    {
        var go = new GameObject("ZoneRaceHUD_Canvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 105;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        root = (RectTransform)go.transform;

        // Zone tint: the purple drift-zone vignette, for the whole race.
        var tuning = SpatialDriftTuning.Shared;
        var tint = Img(root, "ZoneTint", "ui_vignette", UITheme.Alpha(tuning.screenTintColor, Mathf.Clamp01(tuning.screenTintAlpha * 2.2f)));
        Stretch(tint.rectTransform);

        // Place + lap (top-left).
        placeText = Txt(root, "Place", "1ST", 92, UITheme.Text, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.BottomLeft);
        Place(placeText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(56f, -24f), new Vector2(300f, 110f));
        placeOf = Txt(root, "Of", "/ 6", 34, UITheme.TextDim, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.BottomLeft);
        Place(placeOf.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(232f, -44f), new Vector2(160f, 70f));
        lapText = Txt(root, "Lap", "LAP 1 / 3", 24, UITheme.TextDim, FontStyles.Bold, TextAlignmentOptions.Left);
        lapText.characterSpacing = 6f;
        Place(lapText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -140f), new Vector2(400f, 32f));

        // Debt duel (top-centre).
        var duel = Panel(root, "Duel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(900f, 132f));
        BuildSide(duel, true,  out aheadCaption,  out aheadName,  out aheadPips,  out aheadNote);
        BuildSide(duel, false, out behindCaption, out behindName, out behindPips, out behindNote);
        shieldRing = Img(duel, "Shield", "ui_ring", UITheme.Alpha(Color.white, 0.08f));
        Place(shieldRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(64f, 64f));
        youChip = Img(duel, "You", "ui_circle", Color.white);
        Place(youChip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(34f, 34f));
        var youLbl = Txt(duel, "YouLbl", "YOU", 14, UITheme.TextDim, FontStyles.Bold, TextAlignmentOptions.Center);
        youLbl.characterSpacing = 6f;
        Place(youLbl.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(120f, 20f));

        // Train list (left, under the place readout — the minimap owns the top-right).
        listRoot = Place(NewRect(root, "Train"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -222f), new Vector2(330f, 8 * 42f));
        var trainCap = Txt(listRoot, "Caption", "THE TRAIN", 16, UITheme.TextDim, FontStyles.Bold, TextAlignmentOptions.Left);
        trainCap.characterSpacing = 8f;
        Place(trainCap.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(4f, 4f), new Vector2(300f, 22f));
        for (int i = 0; i < 8; i++)
        {
            var r = Place(NewRect(listRoot, "Row" + i), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -i * 42f), new Vector2(330f, 38f));
            var hl = Img(r, "BG", "ui_round", UITheme.Alpha(Color.black, 0.35f), sliced: true);
            Stretch(hl.rectTransform);
            var pos = Txt(r, "Pos", "P1", 18, UITheme.TextDim, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Left);
            Place(pos.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(44f, 30f));
            var paint = Img(r, "Paint", null, Color.white);
            Place(paint.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(6f, 24f));
            var name = Txt(r, "Name", "DRIVER", 18, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Left);
            Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(66f, 0f), new Vector2(170f, 30f));
            var pips = new Image[5];
            for (int k = 0; k < pips.Length; k++)
            {
                pips[k] = Img(r, "Pip" + k, null, Color.white);
                Place(pips[k].rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f - (pips.Length - 1 - k) * 14f, 0f), new Vector2(10f, 14f));
            }
            var shield = Img(r, "Shield", "ui_ring", UITheme.Cyan);
            Place(shield.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(20f, 20f));
            rows.Add((r.gameObject, hl, pos, paint, name, pips, shield));
        }

        // Call-outs + countdown + hint.
        flash = Txt(root, "Flash", "", 76, UITheme.Text, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
        flashRT = Place(flash.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(1400f, 110f));
        flash.alpha = 0f;
        countdown = Txt(root, "Countdown", "", 250, UITheme.Text, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
        Place(countdown.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(700f, 300f));
        hint = Txt(root, "Hint", "<color=#B06BFF>A / D</color>  or  <color=#B06BFF>STICK FLICKS</color>  HOP RAILS     ·     DODGE THE <color=#FF3399>PINK</color>     ·     GRAB THE <color=#FFCC4D>GOLD</color>",
                   22, UITheme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
        hint.characterSpacing = 3f;
        Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1500f, 34f));

        UITheme.ApplyFont(go);
    }

    void BuildSide(RectTransform duel, bool left, out TextMeshProUGUI caption, out TextMeshProUGUI name,
                   out Image[] pips, out TextMeshProUGUI note)
    {
        float sign = left ? -1f : 1f;
        var align = left ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        var anchor = new Vector2(0.5f, 1f);
        var pivot  = new Vector2(left ? 1f : 0f, 1f);
        float x = sign * 64f;

        caption = Txt(duel, left ? "AheadCap" : "BehindCap", "", 14, UITheme.TextDim, FontStyles.Bold, align);
        caption.characterSpacing = 6f;
        Place(caption.rectTransform, anchor, pivot, new Vector2(x, -14f), new Vector2(360f, 20f));
        name = Txt(duel, left ? "AheadName" : "BehindName", "", 30, UITheme.Text, FontStyles.Bold | FontStyles.Italic, align);
        Place(name.rectTransform, anchor, pivot, new Vector2(x, -34f), new Vector2(360f, 38f));

        pips = new Image[5];
        for (int k = 0; k < pips.Length; k++)
        {
            pips[k] = Img(duel, (left ? "AheadPip" : "BehindPip") + k, null, Color.white);
            // Pips grow outward from the centre chip.
            float px = sign * (70f + k * 30f);
            Place(pips[k].rectTransform, anchor, new Vector2(0.5f, 1f), new Vector2(px, -80f), new Vector2(24f, 12f));
            pips[k].gameObject.AddComponent<UIShear>().shear = 0.8f;
        }

        note = Txt(duel, left ? "AheadNote" : "BehindNote", "", 13, UITheme.TextDim, FontStyles.Bold, align);
        note.characterSpacing = 2f;
        Place(note.rectTransform, anchor, pivot, new Vector2(x, -100f), new Vector2(380f, 20f));
    }

    // ── tiny UI helpers ──────────────────────────────────────────────────────
    static RectTransform NewRect(Transform parent, string name)
    {
        var g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static Image Img(Transform parent, string name, string sprite, Color c, bool sliced = false)
    {
        var img = NewRect(parent, name).gameObject.AddComponent<Image>();
        if (sprite != null) img.sprite = UITheme.Sprite(sprite);
        img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI Txt(Transform parent, string name, string text, float size, Color c,
                               FontStyles style, TextAlignmentOptions align)
    {
        var t = NewRect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var rt = Place(NewRect(parent, name), anchor, pivot, pos, size);
        var bg = Img(rt, "BG", "ui_round", UITheme.Alpha(UITheme.Panel, 0.8f), sliced: true);
        Stretch(bg.rectTransform);
        var line = Img(rt, "Outline", "ui_round_line", UITheme.Alpha(Color.white, 0.07f), sliced: true);
        Stretch(line.rectTransform);
        return rt;
    }
}
