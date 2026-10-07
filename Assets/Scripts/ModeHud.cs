using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// A tiny self-contained overlay that prints a single line of control hints at
/// the bottom of the screen (e.g. Time-Trial restart keys, Practice reset).
/// Built entirely at runtime like RacingHUD, but kept separate so it doesn't
/// touch the large race HUD.  Set `hint` before/after AddComponent — it rebuilds
/// the text whenever it changes.
[DisallowMultipleComponent]
public class ModeHud : MonoBehaviour
{
    [TextArea] public string hint = "";

    Canvas          canvas;
    TextMeshProUGUI label;
    string          shown = null;

    void Start() => Build();

    void Build()
    {
        var go = new GameObject("ModeHudCanvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;                  // above the race HUD

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        var t = new GameObject("Hint", typeof(RectTransform));
        t.transform.SetParent(canvas.transform, false);
        label = t.AddComponent<TextMeshProUGUI>();
        label.font          = TMP_Settings.defaultFontAsset;
        label.fontSize      = 26;
        label.color         = new Color(0.85f, 0.83f, 0.92f, 0.9f);
        label.fontStyle     = FontStyles.Bold;
        label.alignment     = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.enableWordWrapping = false;

        var rt = label.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0, 24);
        rt.sizeDelta        = new Vector2(1400, 34);

        shown = null;
    }

    void LateUpdate()
    {
        if (label == null) return;
        if (hint != shown) { label.text = hint; shown = hint; }
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
