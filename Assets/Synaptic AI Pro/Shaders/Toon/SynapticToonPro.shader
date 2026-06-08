Shader "Synaptic/ToonPro"
{
    Properties
    {
        [Header(Base)]
        _MainTex ("Albedo", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Shadow)]
        _ShadowColor ("Shadow Color", Color) = (0.6, 0.5, 0.6, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _ShadowSmoothness ("Shadow Smoothness", Range(0, 0.5)) = 0.1
        _ShadowRampTex ("Shadow Ramp", 2D) = "white" {}
        [Toggle(_USE_RAMP)] _UseRamp ("Use Ramp Texture", Float) = 0

        [Header(Dual Color SSS)]
        [Toggle(_DUAL_SSS_ON)] _DualSSSEnabled ("Enable Dual SSS", Float) = 0
        _SSSLightColor ("SSS Light Side Color", Color) = (1, 0.9, 0.7, 1)
        _SSSShadowColor ("SSS Shadow Side Color", Color) = (0.8, 0.4, 0.4, 1)
        _SSSStrength ("SSS Strength", Range(0, 2)) = 0.5
        _SSSDistortion ("SSS Distortion", Range(0, 1)) = 0.5
        _SSSPower ("SSS Power", Range(1, 10)) = 3

        [Header(Face Shadow)]
        [Toggle(_FACE_SHADOW_ON)] _FaceShadowEnabled ("Enable Face Shadow", Float) = 0
        _FaceShadowMap ("Face Shadow Map (SDF)", 2D) = "white" {}
        _FaceShadowOffset ("Face Shadow Offset", Range(-1, 1)) = 0
        _FaceShadowSmoothness ("Face Shadow Smoothness", Range(0, 0.5)) = 0.1
        _FaceForward ("Face Forward Direction", Vector) = (0, 0, 1, 0)
        _FaceRight ("Face Right Direction", Vector) = (1, 0, 0, 0)

        [Header(Specular)]
        [Toggle(_SPECULAR_ON)] _SpecularEnabled ("Enable Specular", Float) = 1
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _SpecularSmoothness ("Specular Smoothness", Range(0, 1)) = 0.8
        _SpecularIntensity ("Specular Intensity", Range(0, 5)) = 1
        _SpecularSize ("Specular Size", Range(0.01, 1)) = 0.3
        [Toggle(_ANISO_SPECULAR)] _AnisoSpecular ("Anisotropic Specular", Float) = 0
        _AnisoShift ("Aniso Shift", Range(-1, 1)) = 0

        [Header(Rim Light)]
        [Toggle(_RIM_ON)] _RimEnabled ("Enable Rim", Float) = 1
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 10)) = 3
        _RimIntensity ("Rim Intensity", Range(0, 3)) = 1
        [KeywordEnum(Standard, Fresnel, Depth, Directional, View)] _RimType ("Rim Type", Float) = 0
        _RimThreshold ("Rim Light Threshold", Range(0, 1)) = 0.5
        _RimDepthFade ("Rim Depth Fade", Range(0, 10)) = 1
        _RimLightDirection ("Rim Light Direction", Vector) = (0, 1, 0, 0)

        [Header(Outline)]
        [Toggle(_OUTLINE_ON)] _OutlineEnabled ("Enable Outline", Float) = 1
        _OutlineColor ("Outline Color", Color) = (0.2, 0.2, 0.2, 1)
        _OutlineWidth ("Outline Width", Range(0, 10)) = 1
        _OutlineZOffset ("Outline Z Offset", Range(0, 1)) = 0.0001
        [Toggle(_OUTLINE_COLORED)] _OutlineColored ("Colored Outline", Float) = 0
        _OutlineColorMix ("Outline Color Mix", Range(0, 1)) = 0.5
        [Toggle(_OUTLINE_DISTANCE_FADE)] _OutlineDistanceFade ("Distance Fade", Float) = 1
        _OutlineFadeStart ("Fade Start Distance", Float) = 5
        _OutlineFadeEnd ("Fade End Distance", Float) = 20

        [Header(Emission)]
        [Toggle(_EMISSION_ON)] _EmissionEnabled ("Enable Emission", Float) = 0
        _EmissionMap ("Emission Map", 2D) = "black" {}
        _EmissionColor ("Emission Color", Color) = (1, 1, 1, 1)
        _EmissionIntensity ("Emission Intensity", Range(0, 10)) = 1
        [Toggle(_EMISSION_PULSE)] _EmissionPulse ("Pulse Emission", Float) = 0
        _EmissionPulseSpeed ("Pulse Speed", Float) = 2

        [Header(Normal Map)]
        [Toggle(_NORMAL_MAP_ON)] _NormalMapEnabled ("Enable Normal Map", Float) = 0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1

        [Header(Matcap)]
        [Toggle(_MATCAP_ON)] _MatcapEnabled ("Enable Matcap", Float) = 0
        _MatcapTex ("Matcap Texture", 2D) = "white" {}
        _MatcapIntensity ("Matcap Intensity", Range(0, 2)) = 1
        _MatcapBlendMode ("Blend Mode (0=Add, 1=Multiply)", Range(0, 1)) = 0

        [Header(Detail)]
        _DetailMask ("Detail Mask (R)", 2D) = "white" {}
        _DetailAlbedo ("Detail Albedo", 2D) = "grey" {}
        _DetailNormal ("Detail Normal", 2D) = "bump" {}
        _DetailScale ("Detail Scale", Float) = 1
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.5

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Toggle] _ZWrite ("Z Write", Float) = 1
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Main pass
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #pragma shader_feature_local _USE_RAMP
            #pragma shader_feature_local _FACE_SHADOW_ON
            #pragma shader_feature_local _DUAL_SSS_ON
            #pragma shader_feature_local _SPECULAR_ON
            #pragma shader_feature_local _ANISO_SPECULAR
            #pragma shader_feature_local _RIM_ON
            #pragma shader_feature_local _RIMTYPE_STANDARD _RIMTYPE_FRESNEL _RIMTYPE_DEPTH _RIMTYPE_DIRECTIONAL _RIMTYPE_VIEW
            #pragma shader_feature_local _EMISSION_ON
            #pragma shader_feature_local _EMISSION_PULSE
            #pragma shader_feature_local _NORMAL_MAP_ON
            #pragma shader_feature_local _MATCAP_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            TEXTURE2D(_ShadowRampTex);
            TEXTURE2D(_FaceShadowMap);
            TEXTURE2D(_BumpMap);
            TEXTURE2D(_EmissionMap);
            TEXTURE2D(_MatcapTex);
            TEXTURE2D(_DetailMask);
            TEXTURE2D(_DetailAlbedo);
            TEXTURE2D(_DetailNormal);

            SAMPLER(sampler_MainTex);
            SAMPLER(sampler_ShadowRampTex);
            SAMPLER(sampler_FaceShadowMap);
            SAMPLER(sampler_BumpMap);
            SAMPLER(sampler_EmissionMap);
            SAMPLER(sampler_MatcapTex);
            SAMPLER(sampler_DetailMask);
            SAMPLER(sampler_DetailAlbedo);
            SAMPLER(sampler_DetailNormal);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSmoothness;

                float4 _FaceShadowMap_ST;
                float _FaceShadowOffset;
                float _FaceShadowSmoothness;
                float4 _FaceForward;
                float4 _FaceRight;

                float4 _SSSLightColor;
                float4 _SSSShadowColor;
                float _SSSStrength;
                float _SSSDistortion;
                float _SSSPower;

                float4 _SpecularColor;
                float _SpecularSmoothness;
                float _SpecularIntensity;
                float _SpecularSize;
                float _AnisoShift;

                float4 _RimColor;
                float _RimPower;
                float _RimIntensity;
                float _RimThreshold;
                float _RimDepthFade;
                float4 _RimLightDirection;

                float4 _EmissionMap_ST;
                float4 _EmissionColor;
                float _EmissionIntensity;
                float _EmissionPulseSpeed;

                float4 _BumpMap_ST;
                float _BumpScale;

                float _MatcapIntensity;
                float _MatcapBlendMode;

                float _DetailScale;
                float _DetailStrength;

                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
                float3 viewDir : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
                float fogFactor : TEXCOORD7;
                float4 vertexColor : TEXCOORD8;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);

                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.worldTangent = TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.worldBitangent = cross(OUT.worldNormal, OUT.worldTangent) * IN.tangentOS.w;

                OUT.viewDir = GetWorldSpaceViewDir(OUT.worldPos);
                OUT.shadowCoord = TransformWorldToShadowCoord(OUT.worldPos);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                OUT.vertexColor = IN.color;

                return OUT;
            }

            // Genshin-style face shadow using SDF
            float GetFaceShadow(float2 uv, float3 lightDir, float3 faceForward, float3 faceRight)
            {
                #if defined(_FACE_SHADOW_ON)
                    // Project light onto face plane
                    float2 lightDirXZ = normalize(float2(dot(lightDir, faceRight), dot(lightDir, faceForward)));

                    // Flip UV for left side
                    float2 sdfUV = uv;
                    if (lightDirXZ.x < 0)
                    {
                        sdfUV.x = 1 - sdfUV.x;
                        lightDirXZ.x = -lightDirXZ.x;
                    }

                    // Sample SDF
                    float sdf = SAMPLE_TEXTURE2D(_FaceShadowMap, sampler_FaceShadowMap, sdfUV).r;

                    // Calculate threshold based on light angle
                    float threshold = 1 - (lightDirXZ.y * 0.5 + 0.5) + _FaceShadowOffset;

                    return smoothstep(threshold - _FaceShadowSmoothness, threshold + _FaceShadowSmoothness, sdf);
                #else
                    return 1;
                #endif
            }

            // Stylized specular
            float StylizedSpecular(float3 normal, float3 lightDir, float3 viewDir, float3 tangent, float smoothness, float size)
            {
                float3 halfDir = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(normal, halfDir));

                #if defined(_ANISO_SPECULAR)
                    // Anisotropic specular
                    float3 binormal = cross(normal, tangent);
                    float TdotH = dot(tangent, halfDir);
                    float BdotH = dot(binormal, halfDir);
                    float aniso = sqrt(1 - TdotH * TdotH) * sqrt(1 - BdotH * BdotH);
                    NdotH = lerp(NdotH, aniso, 0.5);
                #endif

                float specPower = exp2(smoothness * 10 + 1);
                float spec = pow(NdotH, specPower);

                // Stylized hard edge
                return smoothstep(size - 0.01, size + 0.01, spec);
            }

            // Dual Color SSS (Genshin Style)
            float3 DualColorSSS(float3 normal, float3 viewDir, float3 lightDir, float shadowFactor,
                               float3 lightSSSColor, float3 shadowSSSColor, float strength, float distortion, float power)
            {
                #if defined(_DUAL_SSS_ON)
                    float3 sssH = normalize(lightDir + normal * distortion);
                    float VdotH = pow(saturate(dot(viewDir, -sssH)), power);

                    // Interpolate between light SSS and shadow SSS based on shadow factor
                    float3 sssColor = lerp(shadowSSSColor, lightSSSColor, shadowFactor);
                    return sssColor * VdotH * strength;
                #else
                    return float3(0, 0, 0);
                #endif
            }

            // Advanced Rim Light with 5 types
            float3 RimLight(float3 normal, float3 viewDir, float3 lightDir, float3 rimColor, float power, float intensity,
                           float threshold, float depthFade, float3 customLightDir, float depth)
            {
                float rim = 0;

                #if defined(_RIMTYPE_FRESNEL)
                    // Fresnel rim - stronger at grazing angles
                    float fresnel = pow(1 - saturate(dot(viewDir, normal)), power);
                    rim = fresnel;

                #elif defined(_RIMTYPE_DEPTH)
                    // Depth-based rim - fades with depth
                    float baseRim = 1 - saturate(dot(viewDir, normal));
                    float depthFactor = saturate(depth * depthFade);
                    rim = pow(baseRim, power) * (1 - depthFactor);

                #elif defined(_RIMTYPE_DIRECTIONAL)
                    // Directional rim - follows custom light direction
                    float baseRim = 1 - saturate(dot(viewDir, normal));
                    float NdotL = dot(normal, normalize(customLightDir)) * 0.5 + 0.5;
                    rim = pow(baseRim, power) * smoothstep(threshold - 0.1, threshold + 0.1, NdotL);

                #elif defined(_RIMTYPE_VIEW)
                    // View-dependent rim - based on view angle to light
                    float baseRim = 1 - saturate(dot(viewDir, normal));
                    float VdotL = dot(viewDir, lightDir) * 0.5 + 0.5;
                    rim = pow(baseRim, power) * (1 - VdotL);

                #else // _RIMTYPE_STANDARD
                    // Standard rim
                    rim = pow(1 - saturate(dot(viewDir, normal)), power);
                #endif

                return rimColor * rim * intensity;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // Sample textures
                float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);

                float3 normal = normalize(IN.worldNormal);
                float3 viewDir = normalize(IN.viewDir);

                // Normal map
                #if defined(_NORMAL_MAP_ON)
                    float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv * _BumpMap_ST.xy), _BumpScale);
                    float3x3 TBN = float3x3(IN.worldTangent, IN.worldBitangent, IN.worldNormal);
                    normal = normalize(mul(normalTS, TBN));
                #endif

                // Lighting
                Light mainLight = GetMainLight(IN.shadowCoord);
                float3 lightDir = mainLight.direction;
                float3 lightColor = mainLight.color;
                float shadow = mainLight.shadowAttenuation;

                // Basic NdotL
                float NdotL = dot(normal, lightDir);
                float halfLambert = NdotL * 0.5 + 0.5;

                // Shadow calculation
                float shadowFactor;
                #if defined(_USE_RAMP)
                    shadowFactor = SAMPLE_TEXTURE2D(_ShadowRampTex, sampler_ShadowRampTex, float2(halfLambert * shadow, 0.5)).r;
                #else
                    shadowFactor = smoothstep(_ShadowThreshold - _ShadowSmoothness, _ShadowThreshold + _ShadowSmoothness, halfLambert * shadow);
                #endif

                // Face shadow override
                float faceShadow = GetFaceShadow(IN.uv, lightDir, _FaceForward.xyz, _FaceRight.xyz);
                shadowFactor = min(shadowFactor, faceShadow);

                // Apply shadow color
                float3 diffuse = lerp(albedo.rgb * _ShadowColor.rgb, albedo.rgb, shadowFactor);
                diffuse *= lightColor;

                // Specular
                float3 specular = float3(0, 0, 0);
                #if defined(_SPECULAR_ON)
                    float spec = StylizedSpecular(normal, lightDir, viewDir, IN.worldTangent, _SpecularSmoothness, 1 - _SpecularSize);
                    specular = _SpecularColor.rgb * spec * _SpecularIntensity * shadowFactor;
                #endif

                // Dual Color SSS
                float3 sss = DualColorSSS(normal, viewDir, lightDir, shadowFactor,
                                         _SSSLightColor.rgb, _SSSShadowColor.rgb,
                                         _SSSStrength, _SSSDistortion, _SSSPower);

                // Rim light
                float3 rim = float3(0, 0, 0);
                #if defined(_RIM_ON)
                    float depth = IN.positionCS.w * 0.01; // Approximate depth
                    rim = RimLight(normal, viewDir, lightDir, _RimColor.rgb, _RimPower, _RimIntensity,
                                  _RimThreshold, _RimDepthFade, _RimLightDirection.xyz, depth);
                #endif

                // Matcap
                float3 matcap = float3(0, 0, 0);
                #if defined(_MATCAP_ON)
                    float3 viewNormal = mul((float3x3)UNITY_MATRIX_V, normal);
                    float2 matcapUV = viewNormal.xy * 0.5 + 0.5;
                    matcap = SAMPLE_TEXTURE2D(_MatcapTex, sampler_MatcapTex, matcapUV).rgb * _MatcapIntensity;
                #endif

                // Emission
                float3 emission = float3(0, 0, 0);
                #if defined(_EMISSION_ON)
                    emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb * _EmissionIntensity;
                    #if defined(_EMISSION_PULSE)
                        emission *= (sin(_Time.y * _EmissionPulseSpeed) * 0.5 + 0.5) + 0.5;
                    #endif
                #endif

                // Additional lights
                float3 additionalLights = float3(0, 0, 0);
                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();
                    for (uint i = 0; i < pixelLightCount; i++)
                    {
                        Light light = GetAdditionalLight(i, IN.worldPos);
                        float addNdotL = saturate(dot(normal, light.direction));
                        float addShadow = smoothstep(_ShadowThreshold - _ShadowSmoothness, _ShadowThreshold + _ShadowSmoothness, addNdotL * 0.5 + 0.5);
                        additionalLights += albedo.rgb * light.color * light.distanceAttenuation * addShadow * 0.5;
                    }
                #endif

                // Combine
                float3 finalColor = diffuse + specular + sss + rim + additionalLights + emission;

                // Matcap blend
                #if defined(_MATCAP_ON)
                    finalColor = lerp(finalColor + matcap, finalColor * matcap, _MatcapBlendMode);
                #endif

                // Fog
                finalColor = MixFog(finalColor, IN.fogFactor);

                return float4(finalColor, albedo.a);
            }
            ENDHLSL
        }

        // Outline pass
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag

            #pragma shader_feature_local _OUTLINE_ON
            #pragma shader_feature_local _OUTLINE_COLORED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _OutlineColor;
                float _OutlineWidth;
                float _OutlineZOffset;
                float _OutlineColorMix;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;

                #if defined(_OUTLINE_ON)
                    // Use vertex color alpha for outline width variation
                    float outlineWidth = _OutlineWidth * 0.001 * IN.color.a;

                    // Smooth normal from tangent for better outline
                    float3 smoothNormal = IN.tangentOS.xyz;
                    if (length(smoothNormal) < 0.01)
                    {
                        smoothNormal = IN.normalOS;
                    }

                    float3 posOS = IN.positionOS.xyz + smoothNormal * outlineWidth;
                    float4 posCS = TransformObjectToHClip(posOS);

                    // Z offset to prevent z-fighting
                    posCS.z += _OutlineZOffset * posCS.w;

                    OUT.positionCS = posCS;
                #else
                    OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                #endif

                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            float4 OutlineFrag(Varyings IN) : SV_Target
            {
                #if defined(_OUTLINE_ON)
                    float4 outlineColor = _OutlineColor;

                    #if defined(_OUTLINE_COLORED)
                        float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _BaseColor;
                        outlineColor = lerp(_OutlineColor, albedo * _OutlineColor, _OutlineColorMix);
                    #endif

                    return outlineColor;
                #else
                    discard;
                    return 0;
                #endif
            }
            ENDHLSL
        }

        // Shadow caster pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float3 _LightDirection;

            // ApplyShadowBias inline (URP 14+ compatible)
            float3 ApplyShadowBiasCustom(float3 positionWS, float3 normalWS, float3 lightDirection)
            {
                float invNdotL = 1.0 - saturate(dot(lightDirection, normalWS));
                float scale = invNdotL * 0.001;
                positionWS = lightDirection * 0.001 + positionWS;
                positionWS = normalWS * scale.xxx + positionWS;
                return positionWS;
            }

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(IN.normalOS);

                worldPos = ApplyShadowBiasCustom(worldPos, worldNormal, _LightDirection);
                float4 posCS = TransformWorldToHClip(worldPos);

                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = posCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);

                return OUT;
            }

            float4 ShadowFrag(Varyings IN) : SV_Target
            {
                float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // Depth pass
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            float4 DepthFrag(Varyings IN) : SV_Target
            {
                float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    // ==================== Built-in Pipeline SubShader ====================
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // Main pass
        Pass
        {
            Name "ToonForwardBuiltIn"
            Tags { "LightMode" = "ForwardBase" }

            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            #pragma shader_feature_local _USE_RAMP
            #pragma shader_feature_local _FACE_SHADOW_ON
            #pragma shader_feature_local _SPECULAR_ON
            #pragma shader_feature_local _RIM_ON
            #pragma shader_feature_local _EMISSION_ON
            #pragma shader_feature_local _NORMAL_MAP_ON

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            sampler2D _ShadowRampTex;
            sampler2D _FaceShadowMap;
            sampler2D _BumpMap;
            sampler2D _EmissionMap;

            float4 _MainTex_ST;
            float4 _BaseColor;
            float4 _ShadowColor;
            float _ShadowThreshold;
            float _ShadowSmoothness;

            float _FaceShadowOffset;
            float _FaceShadowSmoothness;
            float4 _FaceForward;
            float4 _FaceRight;

            float4 _SpecularColor;
            float _SpecularSmoothness;
            float _SpecularIntensity;
            float _SpecularSize;

            float4 _RimColor;
            float _RimPower;
            float _RimIntensity;
            float _RimThreshold;

            float4 _EmissionColor;
            float _EmissionIntensity;

            float4 _BumpMap_ST;
            float _BumpScale;
            float _Cutoff;

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
                float3 viewDir : TEXCOORD5;
                SHADOW_COORDS(6)
                UNITY_FOG_COORDS(7)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * v.tangent.w;
                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldPos);
                TRANSFER_SHADOW(o)
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float GetFaceShadow(float2 uv, float3 lightDir, float3 faceForward, float3 faceRight)
            {
                #if defined(_FACE_SHADOW_ON)
                    float2 lightDirXZ = normalize(float2(dot(lightDir, faceRight), dot(lightDir, faceForward)));
                    float2 sdfUV = uv;
                    if (lightDirXZ.x < 0) { sdfUV.x = 1 - sdfUV.x; lightDirXZ.x = -lightDirXZ.x; }
                    float sdf = tex2D(_FaceShadowMap, sdfUV).r;
                    float threshold = 1 - (lightDirXZ.y * 0.5 + 0.5) + _FaceShadowOffset;
                    return smoothstep(threshold - _FaceShadowSmoothness, threshold + _FaceShadowSmoothness, sdf);
                #else
                    return 1;
                #endif
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 albedo = tex2D(_MainTex, i.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);

                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(i.viewDir);

                #if defined(_NORMAL_MAP_ON)
                    float3 normalTS = UnpackScaleNormal(tex2D(_BumpMap, i.uv * _BumpMap_ST.xy), _BumpScale);
                    float3x3 TBN = float3x3(i.worldTangent, i.worldBitangent, i.worldNormal);
                    normal = normalize(mul(normalTS, TBN));
                #endif

                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 lightColor = _LightColor0.rgb;
                float shadow = SHADOW_ATTENUATION(i);

                float NdotL = dot(normal, lightDir);
                float halfLambert = NdotL * 0.5 + 0.5;

                float shadowFactor;
                #if defined(_USE_RAMP)
                    shadowFactor = tex2D(_ShadowRampTex, float2(halfLambert * shadow, 0.5)).r;
                #else
                    shadowFactor = smoothstep(_ShadowThreshold - _ShadowSmoothness, _ShadowThreshold + _ShadowSmoothness, halfLambert * shadow);
                #endif

                float faceShadow = GetFaceShadow(i.uv, lightDir, _FaceForward.xyz, _FaceRight.xyz);
                shadowFactor = min(shadowFactor, faceShadow);

                float3 diffuse = lerp(albedo.rgb * _ShadowColor.rgb, albedo.rgb, shadowFactor) * lightColor;

                float3 specular = float3(0, 0, 0);
                #if defined(_SPECULAR_ON)
                    float3 halfDir = normalize(lightDir + viewDir);
                    float NdotH = saturate(dot(normal, halfDir));
                    float spec = pow(NdotH, exp2(_SpecularSmoothness * 10 + 1));
                    spec = smoothstep(1 - _SpecularSize - 0.01, 1 - _SpecularSize + 0.01, spec);
                    specular = _SpecularColor.rgb * spec * _SpecularIntensity * shadowFactor;
                #endif

                float3 rim = float3(0, 0, 0);
                #if defined(_RIM_ON)
                    float rimFactor = 1 - saturate(dot(viewDir, normal));
                    rimFactor = pow(rimFactor, _RimPower);
                    rim = _RimColor.rgb * rimFactor * _RimIntensity;
                #endif

                float3 emission = float3(0, 0, 0);
                #if defined(_EMISSION_ON)
                    emission = tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb * _EmissionIntensity;
                #endif

                float3 finalColor = diffuse + specular + rim + emission;
                UNITY_APPLY_FOG(i.fogCoord, finalColor);

                return float4(finalColor, albedo.a);
            }
            ENDCG
        }

        // Outline pass
        Pass
        {
            Name "OutlineBuiltIn"
            Tags { "LightMode" = "Always" }

            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _OUTLINE_ON

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _BaseColor;
            float4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineZOffset;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                #if defined(_OUTLINE_ON)
                    float outlineWidth = _OutlineWidth * 0.001 * v.color.a;
                    float3 posOS = v.vertex.xyz + v.normal * outlineWidth;
                    o.pos = UnityObjectToClipPos(float4(posOS, 1));
                    o.pos.z += _OutlineZOffset * o.pos.w;
                #else
                    o.pos = UnityObjectToClipPos(v.vertex);
                #endif
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                #if defined(_OUTLINE_ON)
                    return _OutlineColor;
                #else
                    discard;
                    return 0;
                #endif
            }
            ENDCG
        }

        // Shadow caster
        Pass
        {
            Name "ShadowCasterBuiltIn"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _BaseColor;
            float _Cutoff;

            struct v2f
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float alpha = tex2D(_MainTex, i.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    // ==================== HDRP SubShader ====================
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.high-definition" }
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "HDRenderPipeline"
        }

        Pass
        {
            Name "ToonForwardHDRP"
            Tags { "LightMode" = "Forward" }

            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _USE_RAMP
            #pragma shader_feature_local _SPECULAR_ON
            #pragma shader_feature_local _RIM_ON
            #pragma shader_feature_local _EMISSION_ON

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            TEXTURE2D(_ShadowRampTex);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_MainTex);
            SAMPLER(sampler_ShadowRampTex);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSmoothness;
                float4 _SpecularColor;
                float _SpecularSmoothness;
                float _SpecularIntensity;
                float _SpecularSize;
                float4 _RimColor;
                float _RimPower;
                float _RimIntensity;
                float4 _EmissionColor;
                float _EmissionIntensity;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 viewDir : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                OUT.uv = IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDir = GetWorldSpaceNormalizeViewDir(OUT.worldPos);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);

                float3 normal = normalize(IN.worldNormal);
                float3 viewDir = normalize(IN.viewDir);
                float3 lightDir = float3(0.5, 0.8, 0.2); // Simplified

                float NdotL = dot(normal, lightDir);
                float halfLambert = NdotL * 0.5 + 0.5;

                float shadowFactor;
                #if defined(_USE_RAMP)
                    shadowFactor = SAMPLE_TEXTURE2D(_ShadowRampTex, sampler_ShadowRampTex, float2(halfLambert, 0.5)).r;
                #else
                    shadowFactor = smoothstep(_ShadowThreshold - _ShadowSmoothness, _ShadowThreshold + _ShadowSmoothness, halfLambert);
                #endif

                float3 diffuse = lerp(albedo.rgb * _ShadowColor.rgb, albedo.rgb, shadowFactor);

                float3 specular = float3(0, 0, 0);
                #if defined(_SPECULAR_ON)
                    float3 halfDir = normalize(lightDir + viewDir);
                    float NdotH = saturate(dot(normal, halfDir));
                    float spec = pow(NdotH, exp2(_SpecularSmoothness * 10 + 1));
                    spec = smoothstep(1 - _SpecularSize - 0.01, 1 - _SpecularSize + 0.01, spec);
                    specular = _SpecularColor.rgb * spec * _SpecularIntensity * shadowFactor;
                #endif

                float3 rim = float3(0, 0, 0);
                #if defined(_RIM_ON)
                    float rimFactor = pow(1 - saturate(dot(viewDir, normal)), _RimPower);
                    rim = _RimColor.rgb * rimFactor * _RimIntensity;
                #endif

                float3 emission = float3(0, 0, 0);
                #if defined(_EMISSION_ON)
                    emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb * _EmissionIntensity;
                #endif

                return float4(diffuse + specular + rim + emission, albedo.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
