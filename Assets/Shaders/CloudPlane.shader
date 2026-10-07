// Realistic raymarched volumetric clouds, rendered on a huge sky disc.
//
// The disc is just a canvas: each pixel casts a ray from the camera and
// marches it through a cloud slab (_BaseY … _BaseY + _Thickness), accumulating
// procedural FBM density with Beer's-law absorption and a short secondary
// march toward the sun for self-shadowed, silver-lined lighting.  No render
// features or pipeline hooks needed — it's a transparent mesh in the scene, so
// mountains/walls still occlude it correctly through the depth buffer.
//
// Driven per-frame by CloudLayer.cs from the Atmosphere profile
// (coverage / tint / wind / sun).
Shader "SpatialDrift/CloudPlane"
{
    Properties
    {
        _Coverage   ("Coverage",     Range(0, 1)) = 0.45
        _Density    ("Density",      Range(0.1, 4)) = 1.6
        _CloudColor ("Cloud Tint",   Color) = (1, 1, 1, 1)
        _SunColor   ("Sun Color",    Color) = (1, 0.96, 0.88, 1)
        _AmbientSky ("Ambient Sky",  Color) = (0.6, 0.7, 0.86, 1)
        _SunDir     ("Sun Direction",Vector) = (0.3, -0.7, 0.6, 0)
        _Wind       ("Wind (xz m/s)",Vector) = (6, 0, 2, 0)
        _BaseY      ("Cloud Base Y", Float) = 230
        _Thickness  ("Thickness",    Float) = 110
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent-50"
            "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CloudPlane"
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half   _Coverage;
                half   _Density;
                half4  _CloudColor;
                half4  _SunColor;
                half4  _AmbientSky;
                float4 _SunDir;
                float4 _Wind;
                float  _BaseY;
                float  _Thickness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // ── Cheap 3D value noise + 3-octave FBM (no textures needed) ──────
            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(hash(i + float3(0,0,0)), hash(i + float3(1,0,0)), f.x),
                         lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x),
                         lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y),
                    f.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0;
                v += 0.55 * noise3(p);  p *= 2.13;
                v += 0.28 * noise3(p);  p *= 2.31;
                v += 0.17 * noise3(p);
                return v;
            }

            // Cloud density at a world point: large-scale coverage shapes broken
            // by mid-frequency FBM, rounded off at the slab's bottom and top.
            float cloudDensity(float3 p)
            {
                float3 wp = p + float3(_Wind.x, 0, _Wind.z) * _Time.y;

                float h = saturate((p.y - _BaseY) / max(_Thickness, 1.0));
                float heightShape = smoothstep(0.0, 0.18, h) * smoothstep(1.0, 0.55, h);

                // Big puffy formations …
                float shape  = fbm(wp * 0.0035);
                // … eroded by finer detail.
                float detail = fbm(wp * 0.012);

                float cov = lerp(0.72, 0.28, _Coverage);   // higher coverage → lower threshold
                float d = shape * 0.8 + detail * 0.35 - cov;
                return saturate(d * 2.2) * heightShape;
            }

            // Short march toward the sun → self-shadowing / silver lining.
            float lightMarch(float3 p, float3 toSun)
            {
                float stepLen = _Thickness * 0.22;
                float acc = 0.0;
                [unroll]
                for (int i = 1; i <= 3; i++)
                    acc += cloudDensity(p + toSun * stepLen * i);
                return exp(-acc * stepLen * 0.010 * _Density);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 cam = _WorldSpaceCameraPos;
                float3 rd  = normalize(input.positionWS - cam);

                // Intersect the horizontal cloud slab.
                if (abs(rd.y) < 1e-3) return 0;
                float t0 = (_BaseY - cam.y) / rd.y;
                float t1 = (_BaseY + _Thickness - cam.y) / rd.y;
                float tEnter = max(min(t0, t1), 0.0);
                float tExit  = max(t0, t1);
                if (tExit <= tEnter) return 0;

                const float MaxDist = 9000.0;
                tExit = min(tExit, MaxDist);
                if (tEnter >= tExit) return 0;

                const int STEPS = 26;
                float stepLen = (tExit - tEnter) / STEPS;

                // Tiny per-pixel jitter breaks the step banding.
                float jitter = frac(sin(dot(input.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453);
                float t = tEnter + stepLen * jitter;

                float3 toSun = normalize(-_SunDir.xyz);

                float  transmittance = 1.0;
                float3 lightAccum    = 0.0;

                [loop]
                for (int i = 0; i < STEPS; i++)
                {
                    float3 p = cam + rd * t;
                    float  d = cloudDensity(p);
                    if (d > 0.001)
                    {
                        float sunT = lightMarch(p, toSun);
                        // Sunlit tops + ambient sky fill, denser core = darker.
                        float3 lit = _SunColor.rgb * (sunT * 1.15)
                                   + _AmbientSky.rgb * 0.55;

                        float absorb = d * stepLen * 0.016 * _Density;
                        lightAccum  += lit * transmittance * absorb;
                        transmittance *= exp(-absorb * 1.55);
                        if (transmittance < 0.02) break;
                    }
                    t += stepLen;
                }

                float alpha = saturate(1.0 - transmittance);

                // Fade the whole layer out toward the far horizon.
                float horizonFade = saturate(1.0 - tEnter / MaxDist);
                alpha *= horizonFade * horizonFade;
                if (alpha < 0.003) return 0;

                float3 col = lightAccum * _CloudColor.rgb / max(alpha, 0.02);
                return half4(col, alpha * _CloudColor.a);
            }
            ENDHLSL
        }
    }
}
