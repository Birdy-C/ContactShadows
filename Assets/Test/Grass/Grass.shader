Shader "Hidden/Grass"
{
    Properties
    {
        [MainColor] _BaseColor("BaseColor", Color) = (1,1,1,1)
        _GroundColor("_GroundColor", Color) = (0.5,0.5,0.5)
    }

        SubShader
        {
            Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline"}

            Pass
            {
                ZWrite On
                Cull Back //use default culling because this shader is billboard 
                ZTest Less


                Tags { "LightMode" = "UniversalForward" "RenderType" = "Opaque" }

                HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                float4 _Color;
                float _Extent;
                float _Scale;
                float4x4 _LocalToWorld;
                float4x4 _WorldToLocal;
                // -------------------------------------
                // Universal Render Pipeline keywords
                // When doing custom shaders you most often want to copy and paste these #pragmas
                // These multi_compile variants are stripped from the build depending on:
                // 1) Settings in the URP Asset assigned in the GraphicsSettings at build time
                // e.g If you disabled AdditionalLights in the asset then all _ADDITIONA_LIGHTS variants
                // will be stripped from build
                // 2) Invalid combinations are stripped. e.g variants with _MAIN_LIGHT_SHADOWS_CASCADE
                // but not _MAIN_LIGHT_SHADOWS are invalid and therefore stripped.
                #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
                #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
                #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
                #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
                #pragma multi_compile _ _SHADOWS_SOFT
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                // -------------------------------------
                // Unity defined keywords
                #pragma multi_compile_fog
                // -------------------------------------
                #define UNITY_PI 3.1415

                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"


                uint mHash(uint s)
                {
                    s ^= 2747636419u;
                    s *= 2654435769u;
                    s ^= s >> 16;
                    s *= 2654435769u;
                    s ^= s >> 16;
                    s *= 2654435769u;
                    return s;
                }

                float Random(uint seed)
                {
                    return float(mHash(seed)) / 4294967295.0; // 2^32-1
                }

                // Euler rotation matrix
                float3x3 Euler3x3(float3 v)
                {
                    float sx, cx;
                    float sy, cy;
                    float sz, cz;

                    sincos(v.x, sx, cx);
                    sincos(v.y, sy, cy);
                    sincos(v.z, sz, cz);

                    float3 row1 = float3(sx * sy * sz + cy * cz, sx * sy * cz - cy * sz, cx * sy);
                    float3 row3 = float3(sx * cy * sz - sy * cz, sx * cy * cz + sy * sz, cx * cy);
                    float3 row2 = float3(cx * sz, cx * cz, -sx);

                    return float3x3(row1, row2, row3);
                }

                float3 setup(uint id, out float4x4 w2o, out float4x4 o2w)
                {
                    //uint id = unity_InstanceID;
                    uint seed = id * 6;

                    float2 pos = float2(Random(seed), Random(seed + 1));
                    pos = (pos - 0.5) * 2 * _Extent;

                    float ry = Random(seed + 3) * UNITY_PI * 2;
                    float rx = (Random(seed + 4) - 0.5) * 0.8;
                    float rz = (Random(seed + 5) - 0.5) * 0.8;

                    float3x3 rot = Euler3x3(float3(rx, ry, rz));

                    float scale = _Scale * (Random(seed + 6) + 0.5);

                    float3x3 R = rot * scale;

                    o2w = float4x4(
                        R._11, R._12, R._13, pos.x,
                        R._21, R._22, R._23, 0,
                        R._31, R._32, R._33, pos.y,
                        0, 0, 0, 1
                    );

                    R = rot / scale;

                    w2o = float4x4(
                        R._11, R._21, R._31, -pos.x,
                        R._12, R._22, R._32, 0,
                        R._13, R._23, R._33, -pos.y,
                        0, 0, 0, 1
                    );

                    unity_ObjectToWorld = mul(_LocalToWorld, o2w);
                    unity_WorldToObject = mul(w2o, _WorldToLocal);
                    return float3(pos.x, 0, pos.y);
                }

                struct Attributes
                {
                    float4 positionOS   : POSITION;
                    float3 normalOS     : NORMAL;
                };

                struct Varyings
                {
                    float4 positionCS  : SV_POSITION;
                    float3 positionWS  : TEXCOORD0;
                    half3 color        : COLOR;
                };

                CBUFFER_START(UnityPerMaterial)
                    half3 _BaseColor;
                    half3 _GroundColor;
                CBUFFER_END


                TEXTURE2D(_ContactShadowsMask);       SAMPLER(sampler_ContactShadowsMask);

                float SampleContactShadowsMask(float2 uv)
                {
                    return SAMPLE_TEXTURE2D(_ContactShadowsMask, sampler_ContactShadowsMask, uv);
                }

                Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
                {
                    Varyings OUT;
                    float4x4 w2o, o2w;
                    float3 perGrassPivotPosWS = setup(instanceID, w2o, o2w);//we pre-transform to posWS in C# now

                    //move grass posOS -> posWS
                    OUT.positionWS = mul(o2w, IN.positionOS);
                    OUT.positionCS = TransformWorldToHClip(OUT.positionWS);

                    float4 normalWS = mul(o2w, IN.normalOS);
                    Light mainLight = GetMainLight();
                    half directDiffuse = dot(normalWS, mainLight.direction) * 0.5 + 0.5; //half lambert, to fake grass SSS
                    //fog
                    float fogFactor = ComputeFogFactor(OUT.positionCS.z);
                    half3 albedo = lerp(_GroundColor, _BaseColor, IN.positionOS.y);//you can use texture if you wish to
                    OUT.color = albedo * directDiffuse;

                    return OUT;
                }

                half4 frag(Varyings IN) : SV_Target
                {
                    float4 screenPosition = ComputeScreenPos(TransformWorldToHClip(IN.positionWS));
                    float2 uv = screenPosition.xy / screenPosition.w;
                    float shadow = SampleContactShadowsMask(uv);
                    return half4(IN.color * (0.2 + shadow * 0.8), 1);
                }
                ENDHLSL
            }

            //copy pass, change LightMode to ShadowCaster will make grass cast shadow
            //copy pass, change LightMode to DepthOnly will make grass render into _CameraDepthTexture
        }
}