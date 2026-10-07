using UnityEngine;

/// The car's GHOST form inside Spatial Drift.  Swaps every renderer of the
/// procedural body (CarVisual.Root) to the translucent SpatialDrift/Ghost
/// material, with a white-hot flash at the moment of crossing a rift, and
/// flashes back out to the real paint on exit.  Added on demand by
/// SpatialDriftMode.
[DisallowMultipleComponent]
public class CarGhost : MonoBehaviour
{
    Renderer[]   rends;
    Material[][] saved;
    Material     ghostMat;
    bool  ghost, swapped;
    float flash;
    float restoreAt = -1f;

    public bool IsGhost => ghost;

    static Shader _shader;

    void Collect()
    {
        if (rends != null) return;
        var cv = GetComponent<CarVisual>();
        var root = cv != null && cv.Root != null ? cv.Root : transform;
        rends = root.GetComponentsInChildren<MeshRenderer>(true);
        saved = new Material[rends.Length][];
        for (int i = 0; i < rends.Length; i++) saved[i] = rends[i].sharedMaterials;

        if (_shader == null) _shader = Shader.Find("SpatialDrift/Ghost");
        if (_shader != null)
        {
            ghostMat = new Material(_shader) { name = "CarGhost" };
            var t = SpatialDriftTuning.Shared;
            if (t != null)
            {
                Color rim = t.ghostEmission;
                if (rim.maxColorComponent > 0.01f)
                    ghostMat.SetColor("_RimColor", rim / Mathf.Max(1f, rim.maxColorComponent));
            }
        }
    }

    /// Into / out of ghost form.  `flashIt` = white-hot burst on the change.
    public void SetGhost(bool on, bool flashIt = true)
    {
        Collect();
        if (ghostMat == null) return;
        ghost = on;
        if (on)
        {
            Swap(true);
            flash = flashIt ? 1f : 0f;
            restoreAt = -1f;
        }
        else if (flashIt && swapped)
        {
            // Burn bright for a beat in ghost form, then the real paint returns.
            flash = 1f;
            restoreAt = Time.time + 0.16f;
        }
        else
        {
            Swap(false);
            flash = 0f;
        }
    }

    void Swap(bool toGhost)
    {
        if (rends == null || toGhost == swapped) return;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;
            if (toGhost)
            {
                var arr = new Material[saved[i].Length];
                for (int k = 0; k < arr.Length; k++) arr[k] = ghostMat;
                rends[i].sharedMaterials = arr;
                rends[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else
            {
                rends[i].sharedMaterials = saved[i];
                rends[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }
        swapped = toGhost;
    }

    void Update()
    {
        if (ghostMat == null) return;
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime / 0.45f);
        ghostMat.SetFloat("_Flash", flash * flash);
        if (restoreAt > 0f && Time.time >= restoreAt) { restoreAt = -1f; Swap(false); }
    }

    void OnDestroy()
    {
        Swap(false);
        if (ghostMat != null) Destroy(ghostMat);
    }
}
