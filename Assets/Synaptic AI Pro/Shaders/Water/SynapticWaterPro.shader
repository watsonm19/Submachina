Shader "Synaptic/WaterPro"
{
    Properties
    {
        [Header(Color and Depth)]
        _ShallowColor ("Shallow Color", Color) = (0.3, 0.85, 0.92, 0.4)
        _DeepColor ("Deep Color", Color) = (0.02, 0.08, 0.25, 0.95)
        _HorizonColor ("Horizon Color", Color) = (0.6, 0.85, 1.0, 1.0)
        _DepthMaxDistance ("Depth Distance", Float) = 10.0
        _DepthStrength ("Depth Strength", Range(0, 1)) = 0.8

        [Header(Transparency and Absorption)]
        _AbsorptionColor ("Absorption Color", Color) = (0.2, 0.5, 0.4, 1)
        _AbsorptionStrength ("Absorption Strength", Range(0, 2)) = 0.5
        _ClarityDistance ("Clarity Distance", Float) = 5.0
        _UnderwaterFogDensity ("Underwater Fog Density", Range(0, 1)) = 0.15
        _TransparencyDepth ("Transparency Depth", Float) = 3.0

        [Header(Ocean Waves)]
        _OceanScale ("Ocean Scale", Float) = 1.0
        _WaveSpeed ("Wave Speed", Float) = 1.0
        _WaveHeight ("Wave Height", Float) = 1.0
        _WindDirection ("Wind Direction", Vector) = (1, 0, 0.3, 0)
        _Choppiness ("Choppiness", Range(0, 2)) = 1.0

        [Header(Large Swells)]
        _WaveA ("Wave A (dir.xy, steepness, wavelength)", Vector) = (0.85, 0.5, 0.12, 60)
        _WaveB ("Wave B (dir.xy, steepness, wavelength)", Vector) = (0.5, 0.85, 0.10, 40)

        [Header(Medium Waves)]
        _WaveC ("Wave C (dir.xy, steepness, wavelength)", Vector) = (0.95, 0.3, 0.08, 20)
        _WaveD ("Wave D (dir.xy, steepness, wavelength)", Vector) = (0.35, 0.93, 0.07, 14)
        _WaveE ("Wave E (dir.xy, steepness, wavelength)", Vector) = (0.7, 0.7, 0.06, 10)

        [Header(Small Detail)]
        _WaveF ("Wave F (dir.xy, steepness, wavelength)", Vector) = (0.88, -0.45, 0.05, 6)
        _WaveG ("Wave G (dir.xy, steepness, wavelength)", Vector) = (-0.55, 0.82, 0.04, 4)
        _WaveH ("Wave H (dir.xy, steepness, wavelength)", Vector) = (0.4, -0.85, 0.03, 2.5)

        [Header(Extra Small Waves)]
        _WaveI ("Wave I (dir.xy, steepness, wavelength)", Vector) = (0.92, 0.38, 0.025, 1.8)
        _WaveJ ("Wave J (dir.xy, steepness, wavelength)", Vector) = (-0.42, 0.9, 0.02, 1.2)
        _WaveK ("Wave K (dir.xy, steepness, wavelength)", Vector) = (0.65, -0.75, 0.018, 0.8)
        _WaveL ("Wave L (dir.xy, steepness, wavelength)", Vector) = (-0.78, -0.62, 0.015, 0.5)

        [Header(Micro Detail)]
        _MicroWaveScale ("Micro Wave Scale", Float) = 40.0
        _MicroWaveStrength ("Micro Wave Strength", Range(0, 0.5)) = 0.15

        [Header(Normal Maps)]
        _NormalMap1 ("Normal Map 1", 2D) = "bump" {}
        _NormalMap2 ("Normal Map 2", 2D) = "bump" {}
        _BumpMap ("Normal Map (Built-in)", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1.0
        _NormalScale1 ("Normal Scale 1", Float) = 0.08
        _NormalScale2 ("Normal Scale 2", Float) = 0.25
        _NormalSpeed1 ("Normal Speed 1", Vector) = (0.03, 0.02, 0, 0)
        _NormalSpeed2 ("Normal Speed 2", Vector) = (-0.025, 0.03, 0, 0)

        [Header(Surface Ripples)]
        _RippleScale1 ("Ripple Scale 1 (Fine)", Float) = 0.8
        _RippleScale2 ("Ripple Scale 2 (Micro)", Float) = 2.0
        _RippleScale3 ("Ripple Scale 3 (Ultra)", Float) = 4.0
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.1
        _RippleSpeed ("Ripple Speed", Float) = 0.8

        [Header(Flow Map)]
        _FlowMap ("Flow Map", 2D) = "grey" {}
        _FlowStrength ("Flow Strength", Range(0, 1)) = 0.3
        _FlowSpeed ("Flow Speed", Float) = 0.4

        [Header(Reflection)]
        [Toggle(_SSR_ON)] _SSREnabled ("Enable SSR", Float) = 1
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.5
        _SSRSteps ("SSR Steps", Range(8, 64)) = 32
        _SSRStepSize ("SSR Step Size", Range(0.01, 0.5)) = 0.1
        _SSRThickness ("SSR Thickness", Range(0.01, 1)) = 0.2
        _CubeMap ("Cubemap Fallback", CUBE) = "" {}

        [Header(Refraction)]
        _RefractionStrength ("Refraction Strength", Range(0, 0.5)) = 0.15
        _ChromaticAberration ("Chromatic Aberration", Range(0, 0.1)) = 0.02

        [Header(Fresnel)]
        _FresnelPower ("Fresnel Power", Range(1, 10)) = 4.0
        _FresnelBias ("Fresnel Bias", Range(0, 1)) = 0.02

        [Header(Subsurface Scattering)]
        [Toggle(_SSS_ON)] _SSSEnabled ("Enable SSS", Float) = 1
        _SSSColor ("SSS Color", Color) = (0.2, 0.9, 0.6, 1)
        _SSSStrength ("SSS Strength", Range(0, 2)) = 0.8
        _SSSDistortion ("SSS Distortion", Range(0, 1)) = 0.5
        _SSSPower ("SSS Power", Range(1, 16)) = 4

        [Header(Foam)]
        _FoamTexture ("Foam Texture", 2D) = "gray" {}
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDistance ("Shore Foam Distance", Float) = 1.5
        _FoamNoiseScale ("Foam Noise Scale", Float) = 8
        _FoamSpeed ("Foam Speed", Float) = 0.3
        _FoamSharpness ("Foam Sharpness", Range(0, 10)) = 1.5
        [Toggle(_WAVE_FOAM_ON)] _WaveFoamEnabled ("Wave Crest Foam", Float) = 0
        _WaveFoamThreshold ("Wave Foam Threshold", Range(0, 1)) = 0.75
        _WaveFoamSoftness ("Wave Foam Softness", Range(0.01, 1)) = 0.15

        [Header(Caustics)]
        [Toggle(_CAUSTICS_ON)] _CausticsEnabled ("Enable Caustics", Float) = 1
        _CausticsTexture ("Caustics Texture", 2D) = "white" {}
        _CausticsStrength ("Caustics Strength", Range(0, 10)) = 3.5
        _CausticsScale ("Caustics Scale", Float) = 0.25
        _CausticsSpeed ("Caustics Speed", Float) = 0.6
        _CausticsDepth ("Caustics Max Depth", Float) = 8
        _CausticsDistortion ("Caustics Distortion", Range(0, 1)) = 0.4
        _CausticsSplit ("Caustics RGB Split", Range(0, 0.1)) = 0.025

        [Header(Underwater Light Rays)]
        _UnderwaterRaysStrength ("Light Rays Strength", Range(0, 3)) = 1.0
        _UnderwaterRaysScale ("Light Rays Scale", Float) = 0.15
        _UnderwaterRaysSpeed ("Light Rays Speed", Float) = 0.3
        _UnderwaterRaysFalloff ("Light Rays Depth Falloff", Float) = 15.0

        [Header(Specular)]
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92
        _SpecularIntensity ("Specular Intensity", Range(0, 10)) = 1.5

        [Header(Sun Reflection)]
        _SunDiskSize ("Sun Disk Size", Range(0.001, 0.1)) = 0.015
        _SunDiskIntensity ("Sun Disk Intensity", Range(0, 50)) = 10.0
        _SunDiskSharpness ("Sun Disk Sharpness", Range(1, 100)) = 20.0

        [Header(Anisotropic Highlights)]
        _AnisotropyStrength ("Anisotropy Strength", Range(0, 1)) = 0.6
        _AnisotropyDirection ("Anisotropy Direction", Range(0, 1)) = 0.0

        [Header(Micro Facet Glitter)]
        _MicroFacetScale ("Micro Facet Scale", Float) = 150
        _MicroFacetIntensity ("Micro Facet Intensity", Range(0, 5)) = 0.5
        _MicroFacetThreshold ("Micro Facet Threshold", Range(0.9, 1.0)) = 0.96

        [Header(Tessellation)]
        [Toggle(_TESSELLATION_ON)] _TessEnabled ("Enable Tessellation", Float) = 1
        _TessellationFactor ("Tessellation Factor", Range(1, 64)) = 16
        _TessellationMinDist ("Tessellation Min Dist", Float) = 2
        _TessellationMaxDist ("Tessellation Max Dist", Float) = 80
    }

    // ==================== URP SubShader ====================
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "WaterProURP"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex vert
            #pragma fragment frag
            #pragma hull hull
            #pragma domain domain

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS

            #pragma shader_feature_local _SSR_ON
            #pragma shader_feature_local _SSS_ON
            #pragma shader_feature_local _CAUSTICS_ON
            #pragma shader_feature_local _WAVE_FOAM_ON
            #pragma shader_feature_local _TESSELLATION_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap1);
            TEXTURE2D(_NormalMap2);
            TEXTURE2D(_FlowMap);
            TEXTURE2D(_FoamTexture);
            TEXTURE2D(_CausticsTexture);
            TEXTURECUBE(_CubeMap);

            SAMPLER(sampler_NormalMap1);
            SAMPLER(sampler_NormalMap2);
            SAMPLER(sampler_FlowMap);
            SAMPLER(sampler_FoamTexture);
            SAMPLER(sampler_CausticsTexture);
            SAMPLER(sampler_CubeMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _HorizonColor;
                float _DepthMaxDistance;
                float _DepthStrength;
                float4 _AbsorptionColor;
                float _AbsorptionStrength;
                float _ClarityDistance;
                float _UnderwaterFogDensity;
                float _TransparencyDepth;
                float _OceanScale;
                float _WaveSpeed;
                float _WaveHeight;
                float4 _WindDirection;
                float _Choppiness;
                float4 _WaveA;
                float4 _WaveB;
                float4 _WaveC;
                float4 _WaveD;
                float4 _WaveE;
                float4 _WaveF;
                float4 _WaveG;
                float4 _WaveH;
                float4 _WaveI;
                float4 _WaveJ;
                float4 _WaveK;
                float4 _WaveL;
                float _MicroWaveScale;
                float _MicroWaveStrength;
                float4 _NormalMap1_ST;
                float4 _NormalMap2_ST;
                float _NormalStrength;
                float _NormalScale1;
                float _NormalScale2;
                float4 _NormalSpeed1;
                float4 _NormalSpeed2;
                float _RippleScale1;
                float _RippleScale2;
                float _RippleScale3;
                float _RippleStrength;
                float _RippleSpeed;
                float4 _FlowMap_ST;
                float _FlowStrength;
                float _FlowSpeed;
                float _ReflectionStrength;
                float _SSRSteps;
                float _SSRStepSize;
                float _SSRThickness;
                float _RefractionStrength;
                float _ChromaticAberration;
                float _FresnelPower;
                float _FresnelBias;
                float4 _SSSColor;
                float _SSSStrength;
                float _SSSDistortion;
                float _SSSPower;
                float4 _FoamTexture_ST;
                float4 _FoamColor;
                float _FoamDistance;
                float _FoamNoiseScale;
                float _FoamSpeed;
                float _FoamSharpness;
                float _WaveFoamThreshold;
                float4 _CausticsTexture_ST;
                float _CausticsStrength;
                float _CausticsScale;
                float _CausticsSpeed;
                float _CausticsDepth;
                float _CausticsDistortion;
                float _CausticsSplit;
                float _UnderwaterRaysStrength;
                float _UnderwaterRaysScale;
                float _UnderwaterRaysSpeed;
                float _UnderwaterRaysFalloff;
                float4 _SpecularColor;
                float _Smoothness;
                float _SpecularIntensity;
                float _SunDiskSize;
                float _SunDiskIntensity;
                float _SunDiskSharpness;
                float _AnisotropyStrength;
                float _AnisotropyDirection;
                float _MicroFacetScale;
                float _MicroFacetIntensity;
                float _MicroFacetThreshold;
                float _WaveFoamSoftness;
                float _TessellationFactor;
                float _TessellationMinDist;
                float _TessellationMaxDist;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct TessellationControlPoint
            {
                float4 positionOS : INTERNALTESSPOS;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct TessellationFactors
            {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
                float4 screenPos : TEXCOORD5;
                float3 viewDir : TEXCOORD6;
                float fogFactor : TEXCOORD7;
                float waveHeight : TEXCOORD8;
            };

            float3 GerstnerWave(float4 wave, float3 p, inout float3 tangent, inout float3 binormal, float time)
            {
                float steepness = wave.z;
                float wavelength = wave.w;
                float k = 2.0 * PI / wavelength;
                float c = sqrt(9.8 / k);
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, p.xz) - c * time);
                float a = steepness / k;

                tangent += float3(
                    -d.x * d.x * steepness * sin(f),
                    d.x * steepness * cos(f),
                    -d.x * d.y * steepness * sin(f)
                );
                binormal += float3(
                    -d.x * d.y * steepness * sin(f),
                    d.y * steepness * cos(f),
                    -d.y * d.y * steepness * sin(f)
                );

                return float3(d.x * a * cos(f), a * sin(f), d.y * a * cos(f));
            }

            // Simple noise for micro detail
            float SimpleNoise(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            float SmoothNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                f = f * f * (3.0 - 2.0 * f);

                float a = SimpleNoise(i);
                float b = SimpleNoise(i + float2(1, 0));
                float c = SimpleNoise(i + float2(0, 1));
                float d = SimpleNoise(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float FBMNoise(float2 uv, float t)
            {
                float value = 0;
                float amp = 0.5;
                float2 animUV = uv;

                [unroll(4)]
                for (int i = 0; i < 4; i++)
                {
                    animUV += t * 0.1 * float(i + 1);
                    value += amp * SmoothNoise(animUV);
                    animUV *= 2.0;
                    amp *= 0.5;
                }
                return value;
            }

            // ========== PROCEDURAL SURFACE RIPPLES (constant micro-movement) ==========
            // Creates endless small ripples that make water surface feel alive
            // Uses multiple overlapping sine waves at different scales - NO textures needed
            // Hash function for pseudo-random noise (no texture needed)
            float hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float2 hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Smooth noise interpolation
            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // Smoothstep

                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Rotate UV to break grid alignment
            float2 RotateUV(float2 uv, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
            }

            float3 CalculateSurfaceRipples(float2 worldXZ, float time)
            {
                float rippleTime = time * _RippleSpeed;

                // Multiple rotated UV layers to completely break grid patterns
                float2 uv1 = RotateUV(worldXZ * _RippleScale1, 0.0);
                float2 uv2 = RotateUV(worldXZ * _RippleScale1, 1.047); // 60 degrees
                float2 uv3 = RotateUV(worldXZ * _RippleScale1, 2.094); // 120 degrees

                float2 uv4 = RotateUV(worldXZ * _RippleScale2, 0.524); // 30 degrees
                float2 uv5 = RotateUV(worldXZ * _RippleScale2, 1.571); // 90 degrees
                float2 uv6 = RotateUV(worldXZ * _RippleScale2, 2.618); // 150 degrees

                float2 uv7 = RotateUV(worldXZ * _RippleScale3, 0.785); // 45 degrees
                float2 uv8 = RotateUV(worldXZ * _RippleScale3, 2.356); // 135 degrees

                // Noise for additional randomization
                float n1 = valueNoise(worldXZ * 0.3 + rippleTime * 0.05);
                float n2 = valueNoise(worldXZ * 0.7 - rippleTime * 0.07);

                // Layer 1: Three 60-degree rotated waves (no grid alignment possible)
                float w1 = sin(uv1.x * 4.0 + rippleTime * 1.1 + n1 * 3.0);
                float w2 = sin(uv2.x * 4.0 + rippleTime * 0.9 + n1 * 2.5);
                float w3 = sin(uv3.x * 4.0 + rippleTime * 1.3 + n2 * 2.0);

                // Layer 2: Three more rotated waves at different angles
                float w4 = sin(uv4.x * 6.0 + rippleTime * 1.5 + n2 * 4.0);
                float w5 = sin(uv5.x * 6.0 - rippleTime * 1.2 + n1 * 3.5);
                float w6 = sin(uv6.x * 6.0 + rippleTime * 1.7 + n2 * 3.0);

                // Layer 3: Fine detail
                float w7 = sin(uv7.x * 10.0 + rippleTime * 2.0 + n1 * 5.0);
                float w8 = sin(uv8.x * 10.0 - rippleTime * 1.8 + n2 * 4.5);

                // Combine - each direction contributes to both dx and dz due to rotation
                float layer1 = (w1 + w2 + w3) / 3.0;
                float layer2 = (w4 + w5 + w6) / 3.0;
                float layer3 = (w7 + w8) / 2.0;

                // Direct noise contribution
                float directNoise = (valueNoise(worldXZ * _RippleScale2 + rippleTime) - 0.5) * 2.0;

                // Calculate gradient from rotated waves
                float dx = layer1 * 0.5 * cos(0.0) + layer1 * 0.5 * cos(1.047) + layer1 * 0.5 * cos(2.094)
                         + layer2 * 0.35 * cos(0.524) + layer2 * 0.35 * cos(1.571)
                         + layer3 * 0.2 * cos(0.785)
                         + directNoise * 0.2;

                float dz = layer1 * 0.5 * sin(0.0) + layer1 * 0.5 * sin(1.047) + layer1 * 0.5 * sin(2.094)
                         + layer2 * 0.35 * sin(0.524) + layer2 * 0.35 * sin(1.571)
                         + layer3 * 0.2 * sin(0.785)
                         + directNoise * 0.15;

                float3 rippleNormal = float3(0, 1, 0);
                rippleNormal.x = dx * _RippleStrength * 0.12;
                rippleNormal.z = dz * _RippleStrength * 0.12;
                return normalize(rippleNormal);
            }

            // ========== PHYSICS-BASED WATER SPECULAR (Genshin-style) ==========

            // GGX/Trowbridge-Reitz Normal Distribution Function
            float GGX_D(float NdotH, float roughness)
            {
                float a2 = roughness * roughness;
                float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
                return a2 / (PI * d * d + 0.0001);
            }

            // Schlick's Fresnel approximation
            float3 FresnelSchlick(float cosTheta, float3 F0)
            {
                return F0 + (1.0 - F0) * pow(saturate(1.0 - cosTheta), 5.0);
            }

            // Smith's Geometry function for GGX
            float Smith_G1(float NdotV, float roughness)
            {
                float k = roughness * roughness * 0.5;
                return NdotV / (NdotV * (1.0 - k) + k + 0.0001);
            }

            float Smith_G(float NdotV, float NdotL, float roughness)
            {
                return Smith_G1(NdotV, roughness) * Smith_G1(NdotL, roughness);
            }

            // Anisotropic GGX for wave-direction stretched highlights (like Genshin)
            float AnisotropicGGX(float NdotH, float TdotH, float BdotH, float roughnessT, float roughnessB)
            {
                float d = (TdotH * TdotH) / (roughnessT * roughnessT + 0.0001)
                        + (BdotH * BdotH) / (roughnessB * roughnessB + 0.0001)
                        + NdotH * NdotH;
                return 1.0 / (PI * roughnessT * roughnessB * d * d + 0.0001);
            }

            // Sun disk reflection - visible sun reflection on wave peaks
            float SunDiskReflection(float3 reflectDir, float3 lightDir, float diskSize, float sharpness)
            {
                float cosAngle = dot(reflectDir, lightDir);
                // Create sharp disk with soft edge
                float disk = saturate((cosAngle - (1.0 - diskSize)) / diskSize);
                return pow(disk, sharpness);
            }

            // Micro-facet glitter - based on actual wave normal perturbation (not noise pattern)
            // Returns intensity only - caller multiplies by light color
            float MicroFacetGlitter(float3 normal, float3 viewDir, float3 lightDir, float waveHeight)
            {
                // Use wave height derivative as micro-normal perturbation source
                // Higher waves = more micro-facet variation
                float waveFactor = saturate(abs(waveHeight) * 3.0);

                // Calculate specular based on perturbed normal
                float3 H = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(normal, H));

                // Glitter appears where reflection is nearly perfect
                // Wave peaks get more glitter due to steeper micro-facets
                float glitter = smoothstep(_MicroFacetThreshold, 1.0, NdotH);
                glitter *= (1.0 + waveFactor * 0.5); // Boost on wave peaks

                return glitter * _MicroFacetIntensity;
            }

            // Complete wave-based specular calculation (Genshin-style)
            float3 CalculateWaterSpecular(
                float3 worldNormal,
                float3 viewDir,
                float3 lightDir,
                float3 lightColor,
                float3 tangent,
                float3 bitangent,
                float2 worldXZ,
                float waveHeight,
                float time)
            {
                float3 H = normalize(lightDir + viewDir);

                float NdotH = saturate(dot(worldNormal, H));
                float NdotV = saturate(dot(worldNormal, viewDir));
                float NdotL = saturate(dot(worldNormal, lightDir));
                float VdotH = saturate(dot(viewDir, H));
                float TdotH = dot(tangent, H);
                float BdotH = dot(bitangent, H);

                float roughness = 1.0 - _Smoothness;
                roughness = max(roughness, 0.08); // Minimum roughness for wider spread

                // === 1. Base GGX Specular ===
                float D = GGX_D(NdotH, roughness);
                float G = Smith_G(NdotV, NdotL, roughness);
                float3 F0 = float3(0.02, 0.02, 0.02); // Water IOR ~1.33
                float3 F = FresnelSchlick(VdotH, F0);

                float3 specBRDF = (D * G * F) / (4.0 * NdotV * NdotL + 0.0001);
                float3 baseSpecular = specBRDF * NdotL * _SpecularIntensity;

                // === 2. Anisotropic Highlights (wave-direction stretch) ===
                // Stretch highlights perpendicular to wave direction (tangent)
                // High aniso = long horizontal streaks like real water
                float anisoT = roughness * (1.0 + _AnisotropyStrength * 3.0); // Much wider in tangent direction
                float anisoB = roughness * max(0.05, 1.0 - _AnisotropyStrength * 0.8); // Tighter in binormal
                float anisoD = AnisotropicGGX(NdotH, TdotH, BdotH, anisoT, anisoB);
                // Anisotropic should be the PRIMARY specular for water
                float3 anisoSpecular = anisoD * G * F * NdotL * _SpecularIntensity;

                // === 3. Sun Disk Reflection (uses light color) ===
                float3 reflectDir = reflect(-viewDir, worldNormal);
                float sunDisk = SunDiskReflection(reflectDir, lightDir, _SunDiskSize, _SunDiskSharpness);
                // Boost sun disk on wave peaks
                float peakBoost = saturate(waveHeight * 2.0 + 0.3);
                float3 sunReflection = sunDisk * _SunDiskIntensity * peakBoost * lightColor;

                // === 4. Micro-facet Glitter (wave-based, not noise) ===
                float glitterIntensity = MicroFacetGlitter(worldNormal, viewDir, lightDir, waveHeight);
                float3 glitter = glitterIntensity * lightColor;

                // === Combine all specular components ===
                // Blend between isotropic and anisotropic based on strength
                float3 blendedSpecular = lerp(baseSpecular, anisoSpecular, _AnisotropyStrength);
                float3 totalSpecular = blendedSpecular * lightColor + sunReflection + glitter;

                return totalSpecular * _SpecularColor.rgb;
            }

            float3 ApplyWaves(float3 positionOS, inout float3 normal, out float waveHeight)
            {
                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);
                float time = _Time.y * _WaveSpeed;

                // Scale position by ocean scale
                float3 scaledPos = positionOS * _OceanScale;

                // Apply wind influence to wave directions
                float2 windDir = normalize(_WindDirection.xy);

                float3 offset = float3(0, 0, 0);

                // Large swells (long wavelength, slow)
                offset += GerstnerWave(_WaveA, scaledPos, tangent, binormal, time * 0.8);
                offset += GerstnerWave(_WaveB, scaledPos, tangent, binormal, time * 0.9);

                // Medium waves
                offset += GerstnerWave(_WaveC, scaledPos, tangent, binormal, time);
                offset += GerstnerWave(_WaveD, scaledPos, tangent, binormal, time * 1.1);
                offset += GerstnerWave(_WaveE, scaledPos, tangent, binormal, time * 1.2);

                // Small detail waves (short wavelength, fast)
                offset += GerstnerWave(_WaveF, scaledPos, tangent, binormal, time * 1.4);
                offset += GerstnerWave(_WaveG, scaledPos, tangent, binormal, time * 1.6);
                offset += GerstnerWave(_WaveH, scaledPos, tangent, binormal, time * 1.8);

                // Extra small waves (very short wavelength, for surface texture)
                offset += GerstnerWave(_WaveI, scaledPos, tangent, binormal, time * 2.0);
                offset += GerstnerWave(_WaveJ, scaledPos, tangent, binormal, time * 2.2);
                offset += GerstnerWave(_WaveK, scaledPos, tangent, binormal, time * 2.5);
                offset += GerstnerWave(_WaveL, scaledPos, tangent, binormal, time * 2.8);

                // Apply wave height multiplier
                offset.y *= _WaveHeight;

                // Apply choppiness to horizontal displacement
                offset.xz *= _Choppiness;

                // Add micro waves for surface detail (higher frequency noise)
                float2 microUV = scaledPos.xz * _MicroWaveScale;
                float microNoise = FBMNoise(microUV, time * 2.0);
                float microNoise2 = FBMNoise(microUV * 1.7 + 50.0, time * 2.5);
                float microNoise3 = FBMNoise(microUV * 2.5 + 100.0, time * 3.0);
                offset.y += (microNoise - 0.5) * _MicroWaveStrength;
                offset.y += (microNoise2 - 0.5) * _MicroWaveStrength * 0.5;
                offset.y += (microNoise3 - 0.5) * _MicroWaveStrength * 0.25;

                normal = normalize(cross(binormal, tangent));
                waveHeight = offset.y;
                return positionOS + offset;
            }

            float3 FlowUVW(float2 uv, float2 flowVector, float time, float phaseOffset)
            {
                float progress = frac(time + phaseOffset);
                float3 uvw;
                uvw.xy = uv - flowVector * progress;
                uvw.z = 1 - abs(1 - 2 * progress);
                return uvw;
            }

            TessellationControlPoint vert(Attributes IN)
            {
                TessellationControlPoint OUT;
                OUT.positionOS = IN.positionOS;
                OUT.normalOS = IN.normalOS;
                OUT.tangentOS = IN.tangentOS;
                OUT.uv = IN.uv;
                return OUT;
            }

            float CalcDistanceTessFactor(float3 positionWS, float minDist, float maxDist, float tess)
            {
                float dist = distance(positionWS, _WorldSpaceCameraPos);
                float f = clamp(1.0 - (dist - minDist) / (maxDist - minDist), 0.01, 1.0);
                return f * tess;
            }

            TessellationFactors patchConstantFunction(InputPatch<TessellationControlPoint, 3> patch)
            {
                TessellationFactors f;
                #if defined(_TESSELLATION_ON)
                    float3 p0 = TransformObjectToWorld(patch[0].positionOS.xyz);
                    float3 p1 = TransformObjectToWorld(patch[1].positionOS.xyz);
                    float3 p2 = TransformObjectToWorld(patch[2].positionOS.xyz);
                    f.edge[0] = CalcDistanceTessFactor(0.5 * (p1 + p2), _TessellationMinDist, _TessellationMaxDist, _TessellationFactor);
                    f.edge[1] = CalcDistanceTessFactor(0.5 * (p0 + p2), _TessellationMinDist, _TessellationMaxDist, _TessellationFactor);
                    f.edge[2] = CalcDistanceTessFactor(0.5 * (p0 + p1), _TessellationMinDist, _TessellationMaxDist, _TessellationFactor);
                    f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
                #else
                    f.edge[0] = f.edge[1] = f.edge[2] = 1;
                    f.inside = 1;
                #endif
                return f;
            }

            [domain("tri")]
            [outputcontrolpoints(3)]
            [outputtopology("triangle_cw")]
            [partitioning("fractional_odd")]
            [patchconstantfunc("patchConstantFunction")]
            TessellationControlPoint hull(InputPatch<TessellationControlPoint, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            [domain("tri")]
            Varyings domain(TessellationFactors factors, OutputPatch<TessellationControlPoint, 3> patch, float3 barycentricCoordinates : SV_DomainLocation)
            {
                Attributes data;
                #define INTERPOLATE(fieldName) data.fieldName = \
                    patch[0].fieldName * barycentricCoordinates.x + \
                    patch[1].fieldName * barycentricCoordinates.y + \
                    patch[2].fieldName * barycentricCoordinates.z;
                INTERPOLATE(positionOS)
                INTERPOLATE(normalOS)
                INTERPOLATE(tangentOS)
                INTERPOLATE(uv)
                #undef INTERPOLATE

                Varyings OUT;
                float3 waveNormal;
                float waveHeight;
                float3 posOS = ApplyWaves(data.positionOS.xyz, waveNormal, waveHeight);

                OUT.worldPos = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                OUT.uv = data.uv;
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.worldNormal = TransformObjectToWorldNormal(waveNormal);
                OUT.worldTangent = TransformObjectToWorldDir(data.tangentOS.xyz);
                OUT.worldBitangent = cross(OUT.worldNormal, OUT.worldTangent) * data.tangentOS.w;
                OUT.viewDir = GetWorldSpaceViewDir(OUT.worldPos);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                OUT.waveHeight = waveHeight;
                return OUT;
            }

            float3 ScreenSpaceReflection(float3 worldPos, float3 reflectDir, float2 screenUV)
            {
                #if defined(_SSR_ON)
                    float3 startPos = worldPos;
                    float3 stepDir = reflectDir * _SSRStepSize;
                    int maxSteps = min((int)_SSRSteps, 32); // Limit to 32 for GPU compatibility
                    [unroll(32)]
                    for (int i = 0; i < 32; i++)
                    {
                        if (i >= maxSteps) break;
                        startPos += stepDir;
                        float4 projPos = TransformWorldToHClip(startPos);
                        float2 sampleUV = projPos.xy / projPos.w * 0.5 + 0.5;
                        #if UNITY_UV_STARTS_AT_TOP
                            sampleUV.y = 1 - sampleUV.y;
                        #endif
                        if (sampleUV.x < 0 || sampleUV.x > 1 || sampleUV.y < 0 || sampleUV.y > 1) break;
                        float sceneDepth = LinearEyeDepth(SampleSceneDepth(sampleUV), _ZBufferParams);
                        float rayDepth = LinearEyeDepth(projPos.z, _ZBufferParams);
                        if (rayDepth > sceneDepth && rayDepth < sceneDepth + _SSRThickness)
                            return SampleSceneColor(sampleUV);
                    }
                #endif
                return SAMPLE_TEXTURECUBE(_CubeMap, sampler_CubeMap, reflectDir).rgb;
            }

            float3 SubsurfaceScattering(float3 viewDir, float3 lightDir, float3 normal, float3 lightColor)
            {
                #if defined(_SSS_ON)
                    float3 H = normalize(lightDir + normal * _SSSDistortion);
                    float VdotH = pow(saturate(dot(viewDir, -H)), _SSSPower);
                    return _SSSColor.rgb * VdotH * _SSSStrength * lightColor;
                #else
                    return float3(0, 0, 0);
                #endif
            }

            // Beer-Lambert absorption for realistic underwater color
            float3 ApplyAbsorption(float3 backgroundColor, float depth, float3 viewDir, float3 lightDir)
            {
                // Exponential absorption based on depth
                float absorptionFactor = exp(-depth * _AbsorptionStrength / _ClarityDistance);

                // Color shift - water absorbs red first, then green
                float3 absorptionTint = lerp(_AbsorptionColor.rgb, float3(1,1,1), absorptionFactor);

                // Apply underwater fog
                float fogFactor = 1.0 - exp(-depth * _UnderwaterFogDensity);
                float3 underwaterFog = _DeepColor.rgb * 0.5;

                // Combine absorption and fog
                float3 result = backgroundColor * absorptionTint;
                result = lerp(result, underwaterFog, fogFactor);

                return result;
            }

            float3 SampleCaustics(float2 worldXZ, float depth, float3 worldNormal)
            {
                #if defined(_CAUSTICS_ON)
                    float time = _Time.y * _CausticsSpeed;

                    // Distort caustics UV based on wave normal for shimmer effect
                    float2 normalDistort = worldNormal.xz * _CausticsDistortion;

                    // Layer 1 - main caustics
                    float2 uv1 = worldXZ * _CausticsScale + normalDistort + float2(time * 0.4, time * 0.25);
                    // Layer 2 - offset and scaled differently
                    float2 uv2 = worldXZ * _CausticsScale * 1.4 - normalDistort * 0.7 - float2(time * 0.3, time * 0.35);
                    // Layer 3 - fine detail
                    float2 uv3 = worldXZ * _CausticsScale * 2.1 + normalDistort * 0.3 + float2(time * 0.15, -time * 0.2);

                    // Sample with RGB split for chromatic effect (like real caustics)
                    float2 rgbOffset = float2(_CausticsSplit, _CausticsSplit * 0.5);

                    float c1r = SAMPLE_TEXTURE2D(_CausticsTexture, sampler_CausticsTexture, uv1 + rgbOffset).r;
                    float c1g = SAMPLE_TEXTURE2D(_CausticsTexture, sampler_CausticsTexture, uv1).r;
                    float c1b = SAMPLE_TEXTURE2D(_CausticsTexture, sampler_CausticsTexture, uv1 - rgbOffset).r;

                    float c2r = SAMPLE_TEXTURE2D(_CausticsTexture, sampler_CausticsTexture, uv2 + rgbOffset * 0.7).r;
                    float c2g = SAMPLE_TEXTURE2D(_CausticsTexture, sampler_CausticsTexture, uv2).r;
                    float c2b = SAMPLE_TEXTURE2D(_CausticsTexture, sampler_CausticsTexture, uv2 - rgbOffset * 0.7).r;

                    float c3 = SAMPLE_TEXTURE2D(_CausticsTexture, sampler_CausticsTexture, uv3).r;

                    // Combine layers with min for sharp caustic lines
                    float3 caustics;
                    caustics.r = min(c1r, c2r) + c3 * 0.3;
                    caustics.g = min(c1g, c2g) + c3 * 0.3;
                    caustics.b = min(c1b, c2b) + c3 * 0.3;

                    // Enhance contrast
                    caustics = pow(caustics, 1.8) * 2.5;

                    // Depth fade - stronger in shallow water, disappears in deep
                    float shallowBoost = saturate(1.0 - depth / (_CausticsDepth * 0.3));
                    float depthFade = saturate(1.0 - depth / _CausticsDepth);
                    depthFade = depthFade * depthFade; // Quadratic falloff
                    depthFade *= (1.0 + shallowBoost * 2.0); // Extra bright in shallows

                    return caustics * depthFade * _CausticsStrength;
                #else
                    return float3(0, 0, 0);
                #endif
            }

            // Underwater light rays (God Rays) - visible when looking up from underwater
            float3 CalculateUnderwaterRays(float2 worldXZ, float depth, float3 viewDir, float3 lightDir, float3 lightColor)
            {
                float time = _Time.y * _UnderwaterRaysSpeed;

                // Rays are more visible when looking toward light source
                float viewToLight = saturate(dot(viewDir, lightDir) * 0.5 + 0.5);

                // Create volumetric ray pattern using layered noise
                float2 rayUV1 = worldXZ * _UnderwaterRaysScale + float2(time * 0.1, 0);
                float2 rayUV2 = worldXZ * _UnderwaterRaysScale * 1.7 + float2(time * 0.15, time * 0.05);
                float2 rayUV3 = worldXZ * _UnderwaterRaysScale * 0.5 + float2(-time * 0.08, time * 0.02);

                // Directional rays (streaks going down from surface)
                float ray1 = sin(rayUV1.x * 8.0 + rayUV1.y * 2.0 + time) * 0.5 + 0.5;
                float ray2 = sin(rayUV2.x * 12.0 + rayUV2.y * 3.0 - time * 0.7) * 0.5 + 0.5;
                float ray3 = sin(rayUV3.x * 5.0 + rayUV3.y * 1.5 + time * 1.3) * 0.5 + 0.5;

                // Combine with contrast
                float rays = ray1 * ray2 + ray3 * 0.3;
                rays = pow(saturate(rays), 2.0);

                // Fade based on depth - stronger near surface, fades in deep water
                float depthFade = exp(-depth / _UnderwaterRaysFalloff);

                // Boost when looking up toward light
                float lookUpBoost = pow(viewToLight, 2.0);

                // Apply light color and strength
                float3 rayColor = lightColor * rays * depthFade * lookUpBoost * _UnderwaterRaysStrength;

                return rayColor;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y;
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float3 viewDir = normalize(IN.viewDir);

                float2 flowVector = (SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, IN.worldPos.xz * _FlowMap_ST.xy).rg * 2 - 1) * _FlowStrength;
                float flowTime = time * _FlowSpeed;
                float3 uvwA = FlowUVW(IN.worldPos.xz, flowVector, flowTime, 0);
                float3 uvwB = FlowUVW(IN.worldPos.xz, flowVector, flowTime, 0.5);

                float2 normalUV1A = uvwA.xy * _NormalScale1 + time * _NormalSpeed1.xy;
                float2 normalUV1B = uvwB.xy * _NormalScale1 + time * _NormalSpeed1.xy;
                float2 normalUV2A = uvwA.xy * _NormalScale2 + time * _NormalSpeed2.xy;
                float2 normalUV2B = uvwB.xy * _NormalScale2 + time * _NormalSpeed2.xy;

                float3 normal1A = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap1, sampler_NormalMap1, normalUV1A), _NormalStrength);
                float3 normal1B = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap1, sampler_NormalMap1, normalUV1B), _NormalStrength);
                float3 normal2A = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap2, sampler_NormalMap2, normalUV2A), _NormalStrength * 0.5);
                float3 normal2B = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap2, sampler_NormalMap2, normalUV2B), _NormalStrength * 0.5);

                float3 normalTS1 = normal1A * uvwA.z + normal1B * uvwB.z;
                float3 normalTS2 = normal2A * uvwA.z + normal2B * uvwB.z;
                float3 normalTS = normalize(normalTS1 + normalTS2);

                float3x3 TBN = float3x3(IN.worldTangent, IN.worldBitangent, IN.worldNormal);
                float3 worldNormal = normalize(mul(normalTS, TBN));

                // Add procedural surface ripples (constant micro-movement)
                float3 rippleNormal = CalculateSurfaceRipples(IN.worldPos.xz, time);
                worldNormal = normalize(worldNormal + float3(rippleNormal.x, 0, rippleNormal.z));

                float depth = SampleSceneDepth(screenUV);
                float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                float surfaceDepth = LinearEyeDepth(IN.positionCS.z, _ZBufferParams);
                float depthDifference = linearDepth - surfaceDepth;
                float depthFactor = saturate(depthDifference / _DepthMaxDistance);

                float4 waterColor = lerp(_ShallowColor, _DeepColor, pow(depthFactor, _DepthStrength));
                float horizonFactor = pow(1 - saturate(dot(viewDir, float3(0, 1, 0))), 3);
                waterColor = lerp(waterColor, _HorizonColor, horizonFactor * 0.5);

                // Refraction with chromatic aberration
                // Note: Requires Opaque Texture enabled in URP settings
                float2 refractionOffset = worldNormal.xz * _RefractionStrength * saturate(depthDifference);
                float3 refractionR = SampleSceneColor(screenUV + refractionOffset * (1 + _ChromaticAberration));
                float3 refractionG = SampleSceneColor(screenUV + refractionOffset);
                float3 refractionB = SampleSceneColor(screenUV + refractionOffset * (1 - _ChromaticAberration));
                float3 sampledColor = float3(refractionR.r, refractionG.g, refractionB.b);

                // Check if Opaque Texture is available (invalid samples are often very dark or saturated)
                float sampledValid = step(0.01, dot(sampledColor, float3(0.299, 0.587, 0.114)));

                // If sample looks invalid, use water color directly
                // Otherwise blend with water color to maintain water appearance
                float3 refractionColor = lerp(waterColor.rgb, lerp(waterColor.rgb, sampledColor, 0.5), sampledValid);

                // Apply Beer-Lambert absorption for transparency
                Light mainLightForAbsorption = GetMainLight();
                refractionColor = ApplyAbsorption(refractionColor, depthDifference, viewDir, mainLightForAbsorption.direction);

                // Enhanced caustics with wave distortion
                float3 caustics = SampleCaustics(IN.worldPos.xz, depthDifference, worldNormal);
                refractionColor += caustics * saturate(1.0 - depthFactor * 0.5); // Caustics visible through clear water

                // Underwater light rays (God Rays) - adds volumetric light effect
                float3 underwaterRays = CalculateUnderwaterRays(IN.worldPos.xz, depthDifference, viewDir, mainLightForAbsorption.direction, mainLightForAbsorption.color);
                refractionColor += underwaterRays;

                float fresnel = _FresnelBias + (1 - _FresnelBias) * pow(1 - saturate(dot(viewDir, worldNormal)), _FresnelPower);
                float3 reflectDir = reflect(-viewDir, worldNormal);
                float3 reflectionColor = ScreenSpaceReflection(IN.worldPos, reflectDir, screenUV);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.worldPos));
                float3 lightDir = mainLight.direction;
                float3 rawLightColor = mainLight.color * mainLight.shadowAttenuation;

                // Strongly normalize light color to prevent over-saturation
                // Water should maintain its color regardless of light color
                float lightLuminance = dot(rawLightColor, float3(0.299, 0.587, 0.114));
                float3 normalizedLightColor = rawLightColor / max(lightLuminance, 0.001);
                // Only 30% of light hue affects water, rest is white
                float3 lightColor = lerp(float3(1,1,1), normalizedLightColor, 0.3) * min(lightLuminance, 1.2);

                float3 sss = SubsurfaceScattering(viewDir, lightDir, worldNormal, lightColor);

                // === PHYSICS-BASED WATER SPECULAR (Genshin-style) ===
                // Uses wave normals directly for realistic light reflection
                float3 specularColor = CalculateWaterSpecular(
                    worldNormal,
                    viewDir,
                    lightDir,
                    lightColor,
                    IN.worldTangent,
                    IN.worldBitangent,
                    IN.worldPos.xz,
                    IN.waveHeight,
                    time
                );

                // Shore foam - multi-layer for natural look
                float shoreFoam = 0;
                if (depthDifference < _FoamDistance)
                {
                    float foamTime = time * _FoamSpeed;
                    // Multiple layers at different scales
                    float2 foamUV1 = IN.worldPos.xz * _FoamNoiseScale + foamTime * float2(0.1, 0.05);
                    float2 foamUV2 = IN.worldPos.xz * _FoamNoiseScale * 2.3 + foamTime * float2(-0.08, 0.12);
                    float2 foamUV3 = IN.worldPos.xz * _FoamNoiseScale * 0.5 + foamTime * float2(0.03, -0.06);

                    float foamNoise1 = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV1).r;
                    float foamNoise2 = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV2).r;
                    float foamNoise3 = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV3).r;

                    // Blend noise layers
                    float foamNoise = foamNoise1 * 0.5 + foamNoise2 * 0.3 + foamNoise3 * 0.2;

                    float foamFactor = 1 - saturate(depthDifference / _FoamDistance);
                    foamFactor = pow(foamFactor, 0.7); // Softer falloff
                    shoreFoam = saturate((foamNoise - (1 - foamFactor)) * _FoamSharpness);
                }

                // Wave crest foam - follows wave motion naturally
                float waveFoam = 0;
                #if defined(_WAVE_FOAM_ON)
                    float waveHeightNorm = saturate(IN.waveHeight * 2 + 0.5);

                    // Soft threshold with smooth transition
                    float foamMask = smoothstep(_WaveFoamThreshold - _WaveFoamSoftness, _WaveFoamThreshold + _WaveFoamSoftness, waveHeightNorm);

                    if (foamMask > 0.001)
                    {
                        float foamTime = time * _FoamSpeed * 0.5;

                        // UV that follows wave motion for more natural appearance
                        float2 waveMotion = float2(sin(time * 0.3), cos(time * 0.2)) * 0.5;
                        float2 foamUV1 = IN.worldPos.xz * _FoamNoiseScale * 0.3 + waveMotion + foamTime * float2(0.1, 0.08);
                        float2 foamUV2 = IN.worldPos.xz * _FoamNoiseScale * 0.7 - waveMotion * 0.5 + foamTime * float2(-0.05, 0.1);
                        float2 foamUV3 = IN.worldPos.xz * _FoamNoiseScale * 1.2 + foamTime * float2(0.08, -0.03);

                        float foamNoise1 = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV1).r;
                        float foamNoise2 = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV2).r;
                        float foamNoise3 = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV3).r;

                        // Complex noise blend for organic look
                        float foamNoise = foamNoise1 * foamNoise2 * 2.0 + foamNoise3 * 0.3;
                        foamNoise = saturate(foamNoise);

                        waveFoam = foamNoise * foamMask;
                    }
                #endif

                float totalFoam = saturate(shoreFoam + waveFoam * 0.8);

                // Enhanced transparency blending
                // Shallow water shows more of the refracted background (transparency)
                // Deep water shows more of the water color
                float transparencyFactor = saturate(depthDifference / _TransparencyDepth);
                transparencyFactor = pow(transparencyFactor, 0.6); // Softer transition

                // Blend between clear (refracted) and tinted water based on depth
                float3 finalColor = lerp(refractionColor, waterColor.rgb, transparencyFactor * waterColor.a);

                // Apply reflection based on fresnel (view angle)
                finalColor = lerp(finalColor, reflectionColor, fresnel * _ReflectionStrength);

                // Add subsurface scattering (light through water) - reduced
                finalColor += sss * 0.5;

                // Add specular highlights - these should be bright but localized
                finalColor += specularColor;

                // Apply foam on top
                finalColor = lerp(finalColor, _FoamColor.rgb, totalFoam);

                // Unity fog
                finalColor = MixFog(finalColor, IN.fogFactor);

                // Alpha: more transparent in shallow, opaque in deep, plus foam
                float alpha = saturate(transparencyFactor * 0.7 + fresnel * 0.3 + totalFoam);
                alpha = max(alpha, 0.1); // Minimum visibility

                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }

    // ==================== Built-in Pipeline SubShader ====================
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        GrabPass { "_GrabTexture" }

        Pass
        {
            Name "WaterProBuiltIn"
            Tags { "LightMode" = "ForwardBase" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #pragma shader_feature_local _SSS_ON
            #pragma shader_feature_local _WAVE_FOAM_ON

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _BumpMap;
            sampler2D _NormalMap1;
            sampler2D _NormalMap2;
            sampler2D _FlowMap;
            sampler2D _FoamTexture;
            sampler2D _GrabTexture;
            sampler2D _CameraDepthTexture;
            samplerCUBE _CubeMap;

            float4 _ShallowColor;
            float4 _DeepColor;
            float4 _HorizonColor;
            float _DepthMaxDistance;
            float _DepthStrength;

            float4 _AbsorptionColor;
            float _AbsorptionStrength;
            float _ClarityDistance;
            float _UnderwaterFogDensity;
            float _TransparencyDepth;

            float _OceanScale;
            float _WaveSpeed;
            float _WaveHeight;
            float4 _WindDirection;
            float _Choppiness;
            float4 _WaveA;
            float4 _WaveB;
            float4 _WaveC;
            float4 _WaveD;
            float4 _WaveE;
            float4 _WaveF;
            float4 _WaveG;
            float4 _WaveH;
            float4 _WaveI;
            float4 _WaveJ;
            float4 _WaveK;
            float4 _WaveL;
            float _MicroWaveScale;
            float _MicroWaveStrength;

            float _NormalStrength;
            float _NormalScale1;
            float _NormalScale2;
            float4 _NormalSpeed1;
            float4 _NormalSpeed2;
            float _RippleScale1;
            float _RippleScale2;
            float _RippleScale3;
            float _RippleStrength;
            float _RippleSpeed;

            float4 _FlowMap_ST;
            float _FlowStrength;
            float _FlowSpeed;

            float _ReflectionStrength;
            float _RefractionStrength;
            float _FresnelPower;
            float _FresnelBias;

            float4 _SSSColor;
            float _SSSStrength;
            float _SSSDistortion;
            float _SSSPower;

            float4 _FoamColor;
            float _FoamDistance;
            float _FoamNoiseScale;
            float _FoamSpeed;
            float _FoamSharpness;
            float _WaveFoamThreshold;

            sampler2D _CausticsTexture;
            float _CausticsStrength;
            float _CausticsScale;
            float _CausticsSpeed;
            float _CausticsDepth;
            float _CausticsDistortion;
            float _CausticsSplit;
            float _UnderwaterRaysStrength;
            float _UnderwaterRaysScale;
            float _UnderwaterRaysSpeed;
            float _UnderwaterRaysFalloff;

            float4 _SpecularColor;
            float _Smoothness;
            float _SpecularIntensity;
            float _SunDiskSize;
            float _SunDiskIntensity;
            float _SunDiskSharpness;
            float _AnisotropyStrength;
            float _AnisotropyDirection;
            float _MicroFacetScale;
            float _MicroFacetIntensity;
            float _MicroFacetThreshold;
            float _WaveFoamSoftness;

            // Simple noise for sparkles
            float SimpleNoiseBuiltin(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            float SmoothNoiseBuiltin(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                f = f * f * (3.0 - 2.0 * f);
                float a = SimpleNoiseBuiltin(i);
                float b = SimpleNoiseBuiltin(i + float2(1, 0));
                float c = SimpleNoiseBuiltin(i + float2(0, 1));
                float d = SimpleNoiseBuiltin(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // GGX Normal Distribution
            float GGX_D_Builtin(float NdotH, float roughness)
            {
                float a2 = roughness * roughness;
                float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
                return a2 / (UNITY_PI * d * d + 0.0001);
            }

            // Schlick Fresnel
            float3 FresnelSchlickBuiltin(float cosTheta, float3 F0)
            {
                return F0 + (1.0 - F0) * pow(saturate(1.0 - cosTheta), 5.0);
            }

            // Smith Geometry
            float SmithG_Builtin(float NdotV, float NdotL, float roughness)
            {
                float k = roughness * roughness * 0.5;
                float g1 = NdotV / (NdotV * (1.0 - k) + k + 0.0001);
                float g2 = NdotL / (NdotL * (1.0 - k) + k + 0.0001);
                return g1 * g2;
            }

            // Sun disk reflection
            float SunDiskBuiltin(float3 reflectDir, float3 lightDir)
            {
                float cosAngle = dot(reflectDir, lightDir);
                float disk = saturate((cosAngle - (1.0 - _SunDiskSize)) / _SunDiskSize);
                return pow(disk, _SunDiskSharpness);
            }

            // Rotate UV (Built-in)
            float2 RotateUVBuiltin(float2 uv, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
            }

            // Procedural surface ripples (Built-in) with rotated waves to prevent moire
            float3 CalculateSurfaceRipplesBuiltin(float2 worldXZ, float time)
            {
                float rippleTime = time * _RippleSpeed;

                // Multiple rotated UV layers
                float2 uv1 = RotateUVBuiltin(worldXZ * _RippleScale1, 0.0);
                float2 uv2 = RotateUVBuiltin(worldXZ * _RippleScale1, 1.047);
                float2 uv3 = RotateUVBuiltin(worldXZ * _RippleScale1, 2.094);

                float2 uv4 = RotateUVBuiltin(worldXZ * _RippleScale2, 0.524);
                float2 uv5 = RotateUVBuiltin(worldXZ * _RippleScale2, 1.571);
                float2 uv6 = RotateUVBuiltin(worldXZ * _RippleScale2, 2.618);

                float2 uv7 = RotateUVBuiltin(worldXZ * _RippleScale3, 0.785);
                float2 uv8 = RotateUVBuiltin(worldXZ * _RippleScale3, 2.356);

                float n1 = SmoothNoiseBuiltin(worldXZ * 0.3 + rippleTime * 0.05);
                float n2 = SmoothNoiseBuiltin(worldXZ * 0.7 - rippleTime * 0.07);

                float w1 = sin(uv1.x * 4.0 + rippleTime * 1.1 + n1 * 3.0);
                float w2 = sin(uv2.x * 4.0 + rippleTime * 0.9 + n1 * 2.5);
                float w3 = sin(uv3.x * 4.0 + rippleTime * 1.3 + n2 * 2.0);

                float w4 = sin(uv4.x * 6.0 + rippleTime * 1.5 + n2 * 4.0);
                float w5 = sin(uv5.x * 6.0 - rippleTime * 1.2 + n1 * 3.5);
                float w6 = sin(uv6.x * 6.0 + rippleTime * 1.7 + n2 * 3.0);

                float w7 = sin(uv7.x * 10.0 + rippleTime * 2.0 + n1 * 5.0);
                float w8 = sin(uv8.x * 10.0 - rippleTime * 1.8 + n2 * 4.5);

                float layer1 = (w1 + w2 + w3) / 3.0;
                float layer2 = (w4 + w5 + w6) / 3.0;
                float layer3 = (w7 + w8) / 2.0;

                float directNoise = (SmoothNoiseBuiltin(worldXZ * _RippleScale2 + rippleTime) - 0.5) * 2.0;

                float dx = layer1 * 0.5 * cos(0.0) + layer1 * 0.5 * cos(1.047) + layer1 * 0.5 * cos(2.094)
                         + layer2 * 0.35 * cos(0.524) + layer2 * 0.35 * cos(1.571)
                         + layer3 * 0.2 * cos(0.785) + directNoise * 0.2;

                float dz = layer1 * 0.5 * sin(0.0) + layer1 * 0.5 * sin(1.047) + layer1 * 0.5 * sin(2.094)
                         + layer2 * 0.35 * sin(0.524) + layer2 * 0.35 * sin(1.571)
                         + layer3 * 0.2 * sin(0.785) + directNoise * 0.15;

                float3 rippleNormal = float3(0, 1, 0);
                rippleNormal.x = dx * _RippleStrength * 0.12;
                rippleNormal.z = dz * _RippleStrength * 0.12;
                return normalize(rippleNormal);
            }

            // Micro-facet glitter (wave-based, returns intensity only)
            float MicroFacetGlitterBuiltin(float3 normal, float3 viewDir, float3 lightDir, float waveHeight)
            {
                float waveFactor = saturate(abs(waveHeight) * 3.0);
                float3 H = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(normal, H));
                float glitter = smoothstep(_MicroFacetThreshold, 1.0, NdotH);
                glitter *= (1.0 + waveFactor * 0.5);
                return glitter * _MicroFacetIntensity;
            }

            // Physics-based water specular (Built-in version)
            // Anisotropic GGX for Built-in
            float AnisotropicGGXBuiltin(float NdotH, float TdotH, float BdotH, float roughnessT, float roughnessB)
            {
                float d = (TdotH * TdotH) / (roughnessT * roughnessT + 0.0001)
                        + (BdotH * BdotH) / (roughnessB * roughnessB + 0.0001)
                        + NdotH * NdotH;
                return 1.0 / (UNITY_PI * roughnessT * roughnessB * d * d + 0.0001);
            }

            float3 CalculateWaterSpecularBuiltin(float3 worldNormal, float3 viewDir, float3 lightDir, float3 lightColor, float3 tangent, float3 bitangent, float2 worldXZ, float waveHeight, float time)
            {
                float3 H = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(worldNormal, H));
                float NdotV = saturate(dot(worldNormal, viewDir));
                float NdotL = saturate(dot(worldNormal, lightDir));
                float VdotH = saturate(dot(viewDir, H));
                float TdotH = dot(tangent, H);
                float BdotH = dot(bitangent, H);

                float roughness = max(1.0 - _Smoothness, 0.08);

                // GGX Specular
                float D = GGX_D_Builtin(NdotH, roughness);
                float G = SmithG_Builtin(NdotV, NdotL, roughness);
                float3 F0 = float3(0.02, 0.02, 0.02);
                float3 F = FresnelSchlickBuiltin(VdotH, F0);
                float3 specBRDF = (D * G * F) / (4.0 * NdotV * NdotL + 0.0001);
                float3 baseSpec = specBRDF * NdotL * _SpecularIntensity;

                // Anisotropic highlights - stretch along wave direction
                float anisoT = roughness * (1.0 + _AnisotropyStrength * 3.0);
                float anisoB = roughness * max(0.05, 1.0 - _AnisotropyStrength * 0.8);
                float anisoD = AnisotropicGGXBuiltin(NdotH, TdotH, BdotH, anisoT, anisoB);
                float3 anisoSpec = anisoD * G * F * NdotL * _SpecularIntensity;

                // Blend isotropic and anisotropic
                float3 blendedSpec = lerp(baseSpec, anisoSpec, _AnisotropyStrength);

                // Sun disk (with light color)
                float3 reflectDir = reflect(-viewDir, worldNormal);
                float sunDisk = SunDiskBuiltin(reflectDir, lightDir);
                float peakBoost = saturate(waveHeight * 2.0 + 0.3);
                float3 sunReflection = sunDisk * _SunDiskIntensity * peakBoost * lightColor;

                // Micro-facet glitter (with light color)
                float glitterIntensity = MicroFacetGlitterBuiltin(worldNormal, viewDir, lightDir, waveHeight);
                float3 glitter = glitterIntensity * lightColor;

                return (blendedSpec * lightColor + sunReflection + glitter) * _SpecularColor.rgb;
            }

            // Underwater light rays (God Rays) - Built-in version
            float3 CalculateUnderwaterRaysBuiltin(float2 worldXZ, float depth, float3 viewDir, float3 lightDir, float3 lightColor)
            {
                float time = _Time.y * _UnderwaterRaysSpeed;
                float viewToLight = saturate(dot(viewDir, lightDir) * 0.5 + 0.5);

                float2 rayUV1 = worldXZ * _UnderwaterRaysScale + float2(time * 0.1, 0);
                float2 rayUV2 = worldXZ * _UnderwaterRaysScale * 1.7 + float2(time * 0.15, time * 0.05);
                float2 rayUV3 = worldXZ * _UnderwaterRaysScale * 0.5 + float2(-time * 0.08, time * 0.02);

                float ray1 = sin(rayUV1.x * 8.0 + rayUV1.y * 2.0 + time) * 0.5 + 0.5;
                float ray2 = sin(rayUV2.x * 12.0 + rayUV2.y * 3.0 - time * 0.7) * 0.5 + 0.5;
                float ray3 = sin(rayUV3.x * 5.0 + rayUV3.y * 1.5 + time * 1.3) * 0.5 + 0.5;

                float rays = ray1 * ray2 + ray3 * 0.3;
                rays = pow(saturate(rays), 2.0);

                float depthFade = exp(-depth / _UnderwaterRaysFalloff);
                float lookUpBoost = pow(viewToLight, 2.0);

                return lightColor * rays * depthFade * lookUpBoost * _UnderwaterRaysStrength;
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
                float4 screenPos : TEXCOORD5;
                float3 viewDir : TEXCOORD6;
                float waveHeight : TEXCOORD7;
                UNITY_FOG_COORDS(8)
            };

            float3 GerstnerWave(float4 wave, float3 p, inout float3 tangent, inout float3 binormal, float time)
            {
                float steepness = wave.z;
                float wavelength = wave.w;
                float k = 2.0 * UNITY_PI / wavelength;
                float c = sqrt(9.8 / k);
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, p.xz) - c * time);
                float a = steepness / k;

                tangent += float3(-d.x * d.x * steepness * sin(f), d.x * steepness * cos(f), -d.x * d.y * steepness * sin(f));
                binormal += float3(-d.x * d.y * steepness * sin(f), d.y * steepness * cos(f), -d.y * d.y * steepness * sin(f));

                return float3(d.x * a * cos(f), a * sin(f), d.y * a * cos(f));
            }

            float3 ApplyWaves(float3 positionOS, inout float3 normal, out float waveHeight)
            {
                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);
                float time = _Time.y * _WaveSpeed;
                float3 scaledPos = positionOS * _OceanScale;

                float3 offset = float3(0, 0, 0);
                offset += GerstnerWave(_WaveA, scaledPos, tangent, binormal, time * 0.8);
                offset += GerstnerWave(_WaveB, scaledPos, tangent, binormal, time * 0.9);
                offset += GerstnerWave(_WaveC, scaledPos, tangent, binormal, time);
                offset += GerstnerWave(_WaveD, scaledPos, tangent, binormal, time * 1.1);
                offset += GerstnerWave(_WaveE, scaledPos, tangent, binormal, time * 1.2);
                offset += GerstnerWave(_WaveF, scaledPos, tangent, binormal, time * 1.4);
                offset += GerstnerWave(_WaveG, scaledPos, tangent, binormal, time * 1.6);
                offset += GerstnerWave(_WaveH, scaledPos, tangent, binormal, time * 1.8);
                offset += GerstnerWave(_WaveI, scaledPos, tangent, binormal, time * 2.0);
                offset += GerstnerWave(_WaveJ, scaledPos, tangent, binormal, time * 2.2);
                offset += GerstnerWave(_WaveK, scaledPos, tangent, binormal, time * 2.5);
                offset += GerstnerWave(_WaveL, scaledPos, tangent, binormal, time * 2.8);

                offset.y *= _WaveHeight;
                offset.xz *= _Choppiness;

                normal = normalize(cross(binormal, tangent));
                waveHeight = offset.y;
                return positionOS + offset;
            }

            float3 FlowUVW(float2 uv, float2 flowVector, float time, float phaseOffset)
            {
                float progress = frac(time + phaseOffset);
                float3 uvw;
                uvw.xy = uv - flowVector * progress;
                uvw.z = 1 - abs(1 - 2 * progress);
                return uvw;
            }

            v2f vert(appdata v)
            {
                v2f o;

                float3 waveNormal;
                float waveHeight;
                float3 posOS = ApplyWaves(v.vertex.xyz, waveNormal, waveHeight);

                o.worldPos = mul(unity_ObjectToWorld, float4(posOS, 1)).xyz;
                o.pos = UnityWorldToClipPos(o.worldPos);
                o.uv = v.uv;
                o.screenPos = ComputeGrabScreenPos(o.pos);
                o.worldNormal = UnityObjectToWorldNormal(waveNormal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * v.tangent.w;
                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldPos);
                o.waveHeight = waveHeight;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float time = _Time.y;
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float3 viewDir = normalize(i.viewDir);

                // Flow
                float2 flowVector = (tex2D(_FlowMap, i.worldPos.xz * _FlowMap_ST.xy).rg * 2 - 1) * _FlowStrength;
                float flowTime = time * _FlowSpeed;
                float3 uvwA = FlowUVW(i.worldPos.xz, flowVector, flowTime, 0);
                float3 uvwB = FlowUVW(i.worldPos.xz, flowVector, flowTime, 0.5);

                // Normal maps
                float2 normalUV1A = uvwA.xy * _NormalScale1 + time * _NormalSpeed1.xy;
                float2 normalUV1B = uvwB.xy * _NormalScale1 + time * _NormalSpeed1.xy;
                float2 normalUV2A = uvwA.xy * _NormalScale2 + time * _NormalSpeed2.xy;
                float2 normalUV2B = uvwB.xy * _NormalScale2 + time * _NormalSpeed2.xy;

                float3 normal1A = UnpackScaleNormal(tex2D(_NormalMap1, normalUV1A), _NormalStrength);
                float3 normal1B = UnpackScaleNormal(tex2D(_NormalMap1, normalUV1B), _NormalStrength);
                float3 normal2A = UnpackScaleNormal(tex2D(_NormalMap2, normalUV2A), _NormalStrength * 0.5);
                float3 normal2B = UnpackScaleNormal(tex2D(_NormalMap2, normalUV2B), _NormalStrength * 0.5);

                float3 normalTS1 = normal1A * uvwA.z + normal1B * uvwB.z;
                float3 normalTS2 = normal2A * uvwA.z + normal2B * uvwB.z;
                float3 normalTS = normalize(normalTS1 + normalTS2);

                float3x3 TBN = float3x3(i.worldTangent, i.worldBitangent, i.worldNormal);
                float3 worldNormal = normalize(mul(normalTS, TBN));

                // Add procedural surface ripples
                float3 rippleNormal = CalculateSurfaceRipplesBuiltin(i.worldPos.xz, time);
                worldNormal = normalize(worldNormal + float3(rippleNormal.x, 0, rippleNormal.z));

                // Depth
                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV);
                float linearDepth = LinearEyeDepth(depth);
                float surfaceDepth = i.screenPos.w;
                float depthDifference = linearDepth - surfaceDepth;
                float depthFactor = saturate(depthDifference / _DepthMaxDistance);

                // Water color
                float4 waterColor = lerp(_ShallowColor, _DeepColor, pow(depthFactor, _DepthStrength));
                float horizonFactor = pow(1 - saturate(dot(viewDir, float3(0, 1, 0))), 3);
                waterColor = lerp(waterColor, _HorizonColor, horizonFactor * 0.5);

                // Refraction with depth-based scaling
                float2 refractionOffset = worldNormal.xz * _RefractionStrength * saturate(depthDifference);
                float3 refractionColor = tex2D(_GrabTexture, screenUV + refractionOffset).rgb;

                // Beer-Lambert absorption
                float absorptionFactor = exp(-depthDifference * _AbsorptionStrength / _ClarityDistance);
                float3 absorptionTint = lerp(_AbsorptionColor.rgb, float3(1,1,1), absorptionFactor);
                float fogFactor = 1.0 - exp(-depthDifference * _UnderwaterFogDensity);
                refractionColor = refractionColor * absorptionTint;
                refractionColor = lerp(refractionColor, _DeepColor.rgb * 0.5, fogFactor);

                // Caustics
                float causticsTime = _Time.y * _CausticsSpeed;
                float2 causticsDistort = worldNormal.xz * _CausticsDistortion;
                float2 causticsUV1 = i.worldPos.xz * _CausticsScale + causticsDistort + causticsTime * float2(0.4, 0.25);
                float2 causticsUV2 = i.worldPos.xz * _CausticsScale * 1.4 - causticsDistort * 0.7 - causticsTime * float2(0.3, 0.35);
                float c1 = tex2D(_CausticsTexture, causticsUV1).r;
                float c2 = tex2D(_CausticsTexture, causticsUV2).r;
                float causticsIntensity = min(c1, c2);
                causticsIntensity = pow(causticsIntensity, 1.8) * 2.5;
                float causticsDepthFade = saturate(1.0 - depthDifference / _CausticsDepth);
                causticsDepthFade *= causticsDepthFade;
                float3 caustics = causticsIntensity * causticsDepthFade * _CausticsStrength;
                refractionColor += caustics;

                // Underwater light rays (God Rays)
                float3 lightDirForRays = normalize(_WorldSpaceLightPos0.xyz);
                float3 lightColorForRays = _LightColor0.rgb;
                float3 underwaterRays = CalculateUnderwaterRaysBuiltin(i.worldPos.xz, depthDifference, viewDir, lightDirForRays, lightColorForRays);
                refractionColor += underwaterRays;

                // Fresnel
                float fresnel = _FresnelBias + (1 - _FresnelBias) * pow(1 - saturate(dot(viewDir, worldNormal)), _FresnelPower);

                // Reflection (cubemap)
                float3 reflectDir = reflect(-viewDir, worldNormal);
                float3 reflectionColor = texCUBE(_CubeMap, reflectDir).rgb;

                // Lighting
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 rawLightColor = _LightColor0.rgb;

                // Strongly normalize light color to prevent over-saturation
                float lightLuminance = dot(rawLightColor, float3(0.299, 0.587, 0.114));
                float3 normalizedLightColor = rawLightColor / max(lightLuminance, 0.001);
                float3 lightColor = lerp(float3(1,1,1), normalizedLightColor, 0.3) * min(lightLuminance, 1.2);

                // SSS
                float3 sss = float3(0, 0, 0);
                #if defined(_SSS_ON)
                    float3 H = normalize(lightDir + worldNormal * _SSSDistortion);
                    float VdotH = pow(saturate(dot(viewDir, -H)), _SSSPower);
                    sss = _SSSColor.rgb * VdotH * _SSSStrength * lightColor;
                #endif

                // === PHYSICS-BASED WATER SPECULAR (Genshin-style) ===
                float3 specularColor = CalculateWaterSpecularBuiltin(
                    worldNormal,
                    viewDir,
                    lightDir,
                    lightColor,
                    i.worldTangent,
                    i.worldBitangent,
                    i.worldPos.xz,
                    i.waveHeight,
                    time
                );

                // Shore foam - multi-layer
                float shoreFoam = 0;
                if (depthDifference < _FoamDistance)
                {
                    float foamTime = time * _FoamSpeed;
                    float2 foamUV1 = i.worldPos.xz * _FoamNoiseScale + foamTime * float2(0.1, 0.05);
                    float2 foamUV2 = i.worldPos.xz * _FoamNoiseScale * 2.3 + foamTime * float2(-0.08, 0.12);
                    float foamNoise1 = tex2D(_FoamTexture, foamUV1).r;
                    float foamNoise2 = tex2D(_FoamTexture, foamUV2).r;
                    float foamNoise = foamNoise1 * 0.6 + foamNoise2 * 0.4;
                    float foamFactor = 1 - saturate(depthDifference / _FoamDistance);
                    foamFactor = pow(foamFactor, 0.7);
                    shoreFoam = saturate((foamNoise - (1 - foamFactor)) * _FoamSharpness);
                }

                // Wave crest foam
                float waveFoam = 0;
                #if defined(_WAVE_FOAM_ON)
                    float waveHeightNorm = saturate(i.waveHeight * 2 + 0.5);
                    float foamMask = smoothstep(_WaveFoamThreshold - _WaveFoamSoftness, _WaveFoamThreshold + _WaveFoamSoftness, waveHeightNorm);
                    if (foamMask > 0.001)
                    {
                        float foamTime = time * _FoamSpeed * 0.5;
                        float2 waveMotion = float2(sin(time * 0.3), cos(time * 0.2)) * 0.5;
                        float2 foamUV1 = i.worldPos.xz * _FoamNoiseScale * 0.3 + waveMotion + foamTime * float2(0.1, 0.08);
                        float2 foamUV2 = i.worldPos.xz * _FoamNoiseScale * 0.7 - waveMotion * 0.5;
                        float foamNoise1 = tex2D(_FoamTexture, foamUV1).r;
                        float foamNoise2 = tex2D(_FoamTexture, foamUV2).r;
                        float foamNoise = foamNoise1 * foamNoise2 * 2.0;
                        waveFoam = saturate(foamNoise) * foamMask;
                    }
                #endif

                float totalFoam = saturate(shoreFoam + waveFoam * 0.8);

                // Enhanced transparency blending
                float transparencyFactor = saturate(depthDifference / _TransparencyDepth);
                transparencyFactor = pow(transparencyFactor, 0.6);

                // Blend between clear (refracted) and tinted water based on depth
                float3 finalColor = lerp(refractionColor, waterColor.rgb, transparencyFactor * waterColor.a);

                finalColor = lerp(finalColor, reflectionColor, fresnel * _ReflectionStrength);
                finalColor += sss * 0.5;
                finalColor += specularColor;
                finalColor = lerp(finalColor, _FoamColor.rgb, totalFoam);

                UNITY_APPLY_FOG(i.fogCoord, finalColor);

                // Alpha: more transparent in shallow, opaque in deep
                float alpha = saturate(transparencyFactor * 0.7 + fresnel * 0.3 + totalFoam);
                alpha = max(alpha, 0.1);
                return float4(finalColor, alpha);
            }
            ENDCG
        }
    }

    // ==================== HDRP ====================
    // NOTE: For HDRP, use Unity's official Water System instead.
    // It provides FFT-based waves, built-in buoyancy, and proper HDRP integration.
    // See: https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@14.0/manual/WaterSystem.html
    //
    // This shader supports URP and Built-in pipelines only.

    FallBack "Synaptic/Water"
}
