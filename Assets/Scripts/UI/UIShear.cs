using UnityEngine;
using UnityEngine.UI;

/// Slants any UI graphic into a parallelogram — the racing-game "speed" look
/// used on menu rows, bars and wipes.  Works on Images (simple or sliced):
/// each vertex shifts horizontally in proportion to its height above the
/// rect's centre, so the shape stays centred where the layout put it.
[RequireComponent(typeof(Graphic))]
[DisallowMultipleComponent]
public class UIShear : BaseMeshEffect
{
    [Tooltip("Horizontal shift per unit of height (0.2 ≈ 11° lean).  Negative leans left.")]
    [Range(-1f, 1f)] public float shear = 0.18f;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || Mathf.Approximately(shear, 0f)) return;
        var rt = (RectTransform)transform;
        float cy = rt.rect.center.y;

        var v = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            v.position.x += (v.position.y - cy) * shear;
            vh.SetUIVertex(v, i);
        }
    }

    public void SetShear(float s)
    {
        if (Mathf.Approximately(s, shear)) return;
        shear = s;
        if (graphic != null) graphic.SetVerticesDirty();
    }
}
