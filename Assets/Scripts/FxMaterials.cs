using UnityEngine;
using UnityEngine.Rendering;

/// Shared particle materials for the car / drift-zone effects: URP
/// Particles/Unlit set up as a real transparent material (additive glow or
/// soft alpha) with a procedural soft round sprite — so particles read as
/// glowing sparks and smoke puffs instead of hard squares.  Built once, shared.
public static class FxMaterials
{
    static Texture2D _soft;
    static Material  _additive, _alpha, _plain;

    /// 64×64 soft round dot (gaussian falloff), generated in code.
    public static Texture2D SoftDot
    {
        get
        {
            if (_soft != null) return _soft;
            const int N = 64;
            _soft = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { name = "FxSoftDot", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = Mathf.Clamp01(Mathf.Exp(-r * r * 4.5f) * 1.15f - 0.02f) * (1f - Mathf.SmoothStep(0.85f, 1f, r));
                byte  b  = (byte)Mathf.RoundToInt(a * 255f);
                px[y * N + x] = new Color32(255, 255, 255, b);
            }
            _soft.SetPixels32(px);
            _soft.Apply(false, true);
            return _soft;
        }
    }

    /// Glowing sparks / energy: SrcAlpha + One.
    public static Material Additive => _additive != null ? _additive : (_additive = Make(true, "FxAdditive"));

    /// Smoke / dust: regular alpha blend.
    public static Material AlphaBlend => _alpha != null ? _alpha : (_alpha = Make(false, "FxAlpha"));

    /// Untextured, two-sided alpha blend — for trails (skid marks), where a
    /// round sprite would smear along the whole ribbon.
    public static Material PlainAlpha
    {
        get
        {
            if (_plain != null) return _plain;
            _plain = Make(false, "FxPlainAlpha");
            _plain.mainTexture = Texture2D.whiteTexture;
            if (_plain.HasProperty("_BaseMap")) _plain.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (_plain.HasProperty("_Cull"))    _plain.SetFloat("_Cull", 0f);
            _plain.renderQueue = (int)RenderQueue.Transparent - 40;   // under cars / smoke
            return _plain;
        }
    }

    static Material Make(bool additive, string name)
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Particles/Standard Unlit")
                  ?? Shader.Find("Sprites/Default");
        var m = new Material(shader) { name = name };
        m.SetOverrideTag("RenderType", "Transparent");
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend"))   m.SetFloat("_Blend", additive ? 2f : 0f);   // URP: 0 alpha · 2 additive
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.mainTexture = SoftDot;
        if (m.HasProperty("_BaseMap"))   m.SetTexture("_BaseMap", SoftDot);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        return m;
    }
}
