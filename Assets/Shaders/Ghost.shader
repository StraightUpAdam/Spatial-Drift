// The car's GHOST form inside Spatial Drift: a translucent shell — glowing
// fresnel rim, faint body fill, holographic scan bands crawling along it and
// a soft flicker.  _Flash (0..1) washes it white-hot for the moment of crossing.
Shader "SpatialDrift/Ghost"
{
    Properties
    {
        _RimColor  ("Rim Color", Color) = (0.75, 0.45, 1.0, 1)
        _FillColor ("Fill Color", Color) = (0.35, 0.15, 0.9, 0.18)
        _RimPower  ("Rim Power", Range(0.5, 8)) = 2.4
        _Intensity ("Intensity", Range(0, 8)) = 2.5
        _Flash     ("Flash", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent+5"
            "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Ghost"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor, _FillColor;
                half  _RimPower, _Intensity, _Flash;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
                float3 posWS      : TEXCOORD2;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws    = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS   = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS     = GetWorldSpaceViewDir(ws);
                o.posWS      = ws;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float  fres  = pow(1.0 - saturate(abs(dot(n, v))), _RimPower);
                float  t     = _Time.y;
                float  bands = 0.5 + 0.5 * sin(i.posWS.y * 14.0 - t * 9.0);
                bands = smoothstep(0.75, 1.0, bands) * 0.5;
                float  flick = 0.88 + 0.12 * sin(t * 37.0) * sin(t * 23.0);

                // Premultiplied: a faint translucent fill (occludes a little)
                // plus an additive glowing rim / scan bands.
                float3 glow = _RimColor.rgb * (fres * _Intensity + bands * 0.6) * flick;
                float  a    = saturate(_FillColor.a + fres * 0.25);
                float3 col  = _FillColor.rgb * a + glow;

                col = lerp(col, float3(3, 3, 3), _Flash);
                a   = lerp(a, 1.0, _Flash);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
