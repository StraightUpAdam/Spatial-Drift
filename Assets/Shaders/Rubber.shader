// Laid-down rubber on the racing surface (TrackRubber).  A coverage map in
// track space (R8: u = along the chunk, v = across the road) tints the road
// toward burnt black where drifts have overlapped — one pass is a faint scuff,
// lap after lap the racing line darkens in.  Drawn as a thin overlay just
// above the road with a depth offset; fogged like the world.
Shader "SpatialDrift/Rubber"
{
    Properties
    {
        _MainTex ("Coverage", 2D) = "black" {}
        _Color   ("Rubber", Color) = (0.03, 0.028, 0.03, 0.88)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent-50"
            "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Rubber"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -2, -2
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv  = i.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half cov = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                half a   = cov * _Color.a;
                half3 c  = MixFog(_Color.rgb, i.fog);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
