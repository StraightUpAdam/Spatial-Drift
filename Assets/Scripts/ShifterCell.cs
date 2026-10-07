using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// One node of the Shifter Lab grid (cloned from a baked template).
/// Mouse: press + drag across nodes draws lines (right button erases); a
/// press-release without dragging is a plain click (place / grab / erase gear).
public class ShifterCell : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler
{
    public Image           bg, stripes, glow;
    public TextMeshProUGUI label;

    [System.NonSerialized] public ShifterDesigner owner;
    [System.NonSerialized] public Vector2Int pos;

    float pop, shake;

    /// v: 0 empty node / 1..10 gear.  linked: a gate line touches this node.
    public void Show(int v, int ghostGear, bool linked)
    {
        bool gear = v > 0;
        if (bg != null)
            bg.color = gear   ? UITheme.AccentDeep
                     : linked ? new Color(0.20f, 0.14f, 0.34f, 0.95f)       // junction on the gate
                              : new Color(0.10f, 0.09f, 0.17f, 0.55f);      // unused socket
        if (stripes != null) stripes.color = Color.clear;
        if (glow != null)    glow.color    = gear ? UITheme.Alpha(UITheme.Accent, 0.55f)
                                         : linked ? UITheme.Alpha(UITheme.Accent, 0.12f) : Color.clear;
        if (label != null)
        {
            label.text  = gear ? v.ToString() : ghostGear > 0 ? ghostGear.ToString() : linked ? "·" : "";
            label.color = gear ? Color.white : ghostGear > 0 ? UITheme.Alpha(UITheme.Text, 0.18f)
                                                             : UITheme.Alpha(UITheme.Accent, 0.6f);
        }
        // Unused sockets sit smaller so the drawn gate reads at a glance.
        var body = transform.childCount > 0 ? (RectTransform)transform.GetChild(0) : null;
        if (body != null) body.localScale = Vector3.one * (gear || linked || ghostGear > 0 ? 1f : 0.72f);
    }

    public void SetSize(float size)
    {
        ((RectTransform)transform).sizeDelta = new Vector2(size, size);
        if (label != null) label.fontSize = Mathf.Round(size * 0.46f);
    }

    public void Pop()   => pop = 1f;
    public void Shake() => shake = 1f;

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        pop   = Mathf.MoveTowards(pop, 0f, dt * 5f);
        shake = Mathf.MoveTowards(shake, 0f, dt * 4f);
        float k = 1f + pop * 0.12f;
        transform.localScale = new Vector3(k, k, 1f);
        if (shake > 0f && transform.childCount > 0)
        {
            var c = (RectTransform)transform.GetChild(0);
            c.anchoredPosition = new Vector2(Mathf.Sin(shake * 40f) * shake * 8f, 0f);
        }
    }

    public void OnPointerEnter(PointerEventData e) => owner?.PointerEnter(pos);

    public void OnPointerDown(PointerEventData e)
    {
        if (owner == null) return;
        if (e.button == PointerEventData.InputButton.Left)  owner.PointerDown(pos, false);
        if (e.button == PointerEventData.InputButton.Right) owner.PointerDown(pos, true);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (owner == null) return;
        if (e.button == PointerEventData.InputButton.Left || e.button == PointerEventData.InputButton.Right)
            owner.PointerUp();
    }
}
