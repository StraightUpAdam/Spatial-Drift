// A tear in space — the doorway into Spatial Drift.
//
// Drawn on one quad (UV 0..1).  _Open (0..1) drives the whole animation:
//   0.00–0.25  a hot vertical CRACK splits the air
//   0.25–1.00  the crack rips WIDE into a jagged opening
// Inside: the drift dimension — deep violet void, a swirling nebula and
// hyperspace streaks racing out of the centre.  The torn edge burns white →
// magenta, little fracture cracks flicker outside it, and a soft halo lights
// the air around it.  Premultiplied-alpha output: the interior occludes, the
// glow adds.
Shader "SpatialDrift/Rift"
{
    Properties
    {
        _Open      ("Open", Range(0, 1)) = 0
        _Aspect    ("Width / Height", Float) = 1.3
        _EdgeColor ("Edge Color", Color) = (1.0, 0.35, 0.9, 1)
        _CoreColor ("Edge Core", Color) = (1, 0.95, 1, 1)
        _VoidColor ("Void Color", Color) = (0.06, 0.0, 0.14, 1)
        _NebColor  ("Nebula Color", Color) = (0.45, 0.15, 1.0, 1)
        _Seed      ("Seed", Float) = 0
        _Fade      ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Rift"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half  _Open, _Aspect, _Seed, _Fade;
                half4 _EdgeColor, _CoreColor, _VoidColor, _NebColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            float hash11(float n) { return frac(sin(n * 127.1 + _Seed * 13.7) * 43758.5453); }
            float hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7)) + _Seed * 7.3) * 43758.5453); }

            float noise1(float x)
            {
                float i = floor(x), f = frac(x);
                float u = f * f * (3.0 - 2.0 * f);
                return lerp(hash11(i), hash11(i + 1.0), u);
            }

            float noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                // Centred coords, x stretched by the quad's aspect so circles stay round.
                float2 p = (i.uv - 0.5) * 2.0;
                p.x *= _Aspect;

                float open = saturate(_Open);
                // The opening crack zig-zags like lightning; it straightens as it tears wide.
                float zig = (noise1(p.y * 7.0 + _Seed) - 0.5) * 0.22 + (noise1(p.y * 19.0 - t * 2.0) - 0.5) * 0.06;
                p.x -= zig * (1.0 - smoothstep(0.2, 0.9, open));
                // Rip: first tall and razor thin, then torn wide.
                float ry = lerp(0.25, 0.92, smoothstep(0.0, 0.35, open));
                float rx = lerp(0.015, 0.92 * _Aspect * 0.78, smoothstep(0.18, 1.0, open));

                float ang = atan2(p.y, p.x);
                float2 e  = p / float2(max(rx, 1e-3), ry);
                float  r  = length(e);

                // Jagged, crawling tear edge (two octaves of angular noise).
                float jag = (noise1(ang * 4.0 + t * 1.3) - 0.5) * 0.20
                          + (noise1(ang * 13.0 - t * 2.6) - 0.5) * 0.07;
                float edge = 1.0 + jag * smoothstep(0.1, 0.5, open);

                float inside = 1.0 - smoothstep(edge - 0.02, edge + 0.02, r);

                // ── Interior: the drift dimension ─────────────────────────────
                float2 q   = p * 1.6;
                float  neb = noise2(q * 1.3 + float2(t * 0.25, -t * 0.18))
                           * noise2(q * 2.7 - float2(t * 0.4, t * 0.1));
                float  rp  = length(p);
                // Hyperspace streaks: per-angle-bucket dashes flying outward.
                float bucket = floor((ang / 6.2831853 + 0.5) * 90.0);
                float speed  = 0.9 + hash11(bucket) * 1.6;
                float lane   = frac(hash11(bucket + 31.0) + t * speed * 0.6 - rp * 0.55);
                float streak = smoothstep(0.92, 1.0, lane) * smoothstep(0.05, 0.5, rp);
                float3 interior = lerp(_VoidColor.rgb, _NebColor.rgb, saturate(neb * 1.8))
                                + _CoreColor.rgb * streak * 0.9
                                + _NebColor.rgb * exp(-rp * 3.0) * 0.6;   // bright vanishing point

                // ── Burning edge ──────────────────────────────────────────────
                // Distance to the edge in REAL units (not the stretched ellipse
                // space) so the glow keeps its width even while the slit is thin.
                float rp0   = length(p);
                float d     = abs(r - edge) * rp0 / max(r, 1e-3);
                float core  = exp(-d * 26.0);
                float burn  = exp(-d * 6.0);
                float flick = 0.75 + 0.25 * noise1(ang * 9.0 + t * 12.0);
                float3 edgeCol = (_CoreColor.rgb * core * 2.6 + _EdgeColor.rgb * burn * 1.4) * flick
                               * lerp(1.6, 1.0, smoothstep(0.1, 0.5, open));     // the crack burns hottest

                // ── Fracture cracks outside the tear ──────────────────────────
                float outside = smoothstep(edge, edge + 0.05, r);
                float crackA  = frac((ang / 6.2831853 + 0.5) * 7.0 + noise1(r * 4.0 + _Seed) * 0.15);
                float crack   = exp(-abs(crackA - 0.5) * 120.0)                  // thin angular lines
                              * exp(-max(r - edge, 0.0) * rp0 / max(r, 1e-3) * 3.0) * outside
                              * step(0.45, noise1(floor((ang / 6.2831853 + 0.5) * 7.0) * 3.1 + floor(t * 8.0)));
                float3 crackCol = _EdgeColor.rgb * crack * 1.5 * smoothstep(0.2, 0.6, open);

                // ── Halo ─────────────────────────────────────────────────────
                float halo = exp(-max(r - edge, 0.0) * rp0 / max(r, 1e-3) * 4.0) * outside * 0.4;

                float3 glow  = edgeCol * smoothstep(0.0, 0.05, open) + crackCol + _EdgeColor.rgb * halo;
                float  alpha = inside * smoothstep(0.02, 0.2, open);
                float3 col   = interior * alpha + glow;

                // Soft quad border so the halo never shows a hard edge.
                // Radial fade well inside the quad: no glow ever reaches its
                // (straight) edges, so nothing reads as a box.
                float border = 1.0 - smoothstep(0.62, 0.98, length((i.uv - 0.5) * 2.0));
                col   *= border * _Fade;
                alpha *= border * _Fade;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
