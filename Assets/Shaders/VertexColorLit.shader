// Lit vertex-colour shader for the track / terrain / scenery walls.
//
// Why it exists: everything procedural in this game carries its colour in
// VERTEX COLOURS, and Unity's URP Lit shader ignores those — so the world was
// unlit, and unlit surfaces can neither receive nor meaningfully cast shadows.
// This shader keeps the vertex-colour pipeline and adds:
//   • main-light diffuse (soft wrap) + ambient probe light
//   • RECEIVING real-time main-light shadows (cars shadow the road)
//   • a ShadowCaster pass with alpha-cutout support (tree-line sprites cast
//     tree-shaped shadows; walls and the road itself cast onto the terrain)
//   • fog, SRP-batcher-compatible CBUFFER
//
// Meshes here have NO normals — lighting uses the flat geometric normal from
// screen-space derivatives, which also suits the low-poly art direction.
Shader "SpatialDrift/VertexColorLit"
{
    Properties
    {
        _BaseMap   ("Texture",      2D)           = "white" {}
        _BaseColor ("Color",        Color)        = (1, 1, 1, 1)
        _Cutoff    ("Alpha Cutoff", Range(0, 1))  = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
            CBUFFER_END

            // Globals set by CloudLayer — drifting cloud shadows ("sun spots").
            // Same shape noise as the volumetric sky, projected along the sun
            // onto the ground, so the dark patches track the actual clouds.
            float  _SD_CloudShadow;      // 0 = off
            float  _SD_CloudCoverage;
            float4 _SD_CloudWind;

            float sdHash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float sdNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(sdHash(i + float3(0,0,0)), sdHash(i + float3(1,0,0)), f.x),
                         lerp(sdHash(i + float3(0,1,0)), sdHash(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(sdHash(i + float3(0,0,1)), sdHash(i + float3(1,0,1)), f.x),
                         lerp(sdHash(i + float3(0,1,1)), sdHash(i + float3(1,1,1)), f.x), f.y),
                    f.z);
            }

            float sdFbm(float3 p)
            {
                float v = 0.0;
                v += 0.55 * sdNoise3(p);  p *= 2.13;
                v += 0.28 * sdNoise3(p);  p *= 2.31;
                v += 0.17 * sdNoise3(p);
                return v;
            }

            half CloudShadow(float3 positionWS, float3 toSun)
            {
                if (_SD_CloudShadow <= 0.001 || toSun.y < 0.06) return 1.0;
                // Project the ground point up the sun ray to the cloud slab.
                float3 p = positionWS + toSun * ((260.0 - positionWS.y) / toSun.y);
                p += float3(_SD_CloudWind.x, 0, _SD_CloudWind.z) * _Time.y;
                float shape = sdFbm(p * 0.0035);
                float cov   = lerp(0.72, 0.28, _SD_CloudCoverage);
                float occ   = saturate((shape * 0.8 + 0.17 - cov) * 3.0);
                return half(1.0 - occ * _SD_CloudShadow);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                float3 positionWS : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                o.color      = input.color;
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv)
                             * input.color * _BaseColor;
                #ifdef _ALPHATEST_ON
                    clip(albedo.a - _Cutoff);
                #endif

                // Flat geometric normal from derivatives, oriented at the camera
                // (meshes have no normal data; double-sided quads just work).
                float3 n = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                float3 v = normalize(_WorldSpaceCameraPos - input.positionWS);
                n = dot(n, v) < 0 ? -n : n;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);

                // Soft-wrapped diffuse — arcade look, nothing goes pitch black.
                half ndl      = saturate(dot(n, mainLight.direction)) * 0.65h + 0.35h;
                half cloudOcc = CloudShadow(input.positionWS, mainLight.direction);
                half3 lighting = SampleSH(n)
                               + mainLight.color * (mainLight.shadowAttenuation * ndl * cloudOcc);

                // Additional lights (car headlights at night/storm).
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light addl = GetAdditionalLight(lightIndex, input.positionWS);
                    half andl  = saturate(dot(n, addl.direction)) * 0.6h + 0.4h;
                    lighting  += addl.color *
                                 (addl.distanceAttenuation * addl.shadowAttenuation * andl);
                LIGHT_LOOP_END
                #endif

                half3 color = albedo.rgb * lighting;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float3 _LightDirection;

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   alpha      : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // Meshes carry no normals, so skip the normal-bias term and pull
                // the caster slightly toward the light to avoid self-shadow acne.
                positionWS -= _LightDirection * 0.12;
                o.positionCS = TransformWorldToHClip(positionWS);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv    = TRANSFORM_TEX(input.uv, _BaseMap);
                o.alpha = input.color.a * _BaseColor.a;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                    half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * input.alpha;
                    clip(a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   alpha      : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                o.alpha      = input.color.a * _BaseColor.a;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                    half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * input.alpha;
                    clip(a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
}
