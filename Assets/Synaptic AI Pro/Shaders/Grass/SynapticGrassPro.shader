Shader "Synaptic/GrassPro"
{
    Properties
    {
        [Header(Base)]
        _MainTex ("Albedo", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.2, 0.6, 0.2, 1)
        _TipColor ("Tip Color", Color) = (0.5, 0.9, 0.3, 1)
        _GradientPower ("Gradient Power", Range(0.1, 3)) = 1

        [Header(Wind Animation)]
        [Toggle(_WIND_ON)] _WindEnabled ("Enable Wind", Float) = 1
        _WindStrength ("Wind Strength", Range(0, 2)) = 0.5
        _WindSpeed ("Wind Speed", Float) = 1
        _WindFrequency ("Wind Frequency", Float) = 1
        _WindDirection ("Wind Direction", Vector) = (1, 0, 0.5, 0)
        _WindNoiseTex ("Wind Noise", 2D) = "grey" {}
        _WindNoiseScale ("Wind Noise Scale", Float) = 0.1
        _WindGustStrength ("Gust Strength", Range(0, 2)) = 0.3
        _WindGustFrequency ("Gust Frequency", Float) = 0.5

        [Header(Player Interaction)]
        [Toggle(_INTERACTION_ON)] _InteractionEnabled ("Enable Interaction", Float) = 1
        _InteractionRadius ("Interaction Radius", Float) = 2
        _InteractionStrength ("Interaction Strength", Range(0, 2)) = 1
        _InteractionFalloff ("Interaction Falloff", Range(0.1, 5)) = 2

        [Header(Subsurface Scattering)]
        [Toggle(_SSS_ON)] _SSSEnabled ("Enable SSS", Float) = 1
        _SSSColor ("SSS Color", Color) = (0.8, 1.0, 0.5, 1)
        _SSSStrength ("SSS Strength", Range(0, 2)) = 1
        _SSSDistortion ("SSS Distortion", Range(0, 1)) = 0.5
        _SSSPower ("SSS Power", Range(1, 16)) = 4

        [Header(Detail)]
        _AO ("Ambient Occlusion", Range(0, 1)) = 0.5
        _AOPower ("AO Power", Range(0.1, 3)) = 1
        _Roughness ("Roughness", Range(0, 1)) = 0.8

        [Header(Specular)]
        [Toggle(_SPECULAR_ON)] _SpecularEnabled ("Enable Specular", Float) = 1
        _SpecularColor ("Specular Color", Color) = (1, 1, 0.8, 1)
        _SpecularIntensity ("Specular Intensity", Range(0, 3)) = 0.5
        _SpecularPower ("Specular Power", Range(1, 128)) = 32

        [Header(Distance Fade)]
        [Toggle(_DISTANCE_FADE_ON)] _DistanceFadeEnabled ("Enable Distance Fade", Float) = 1
        _FadeStart ("Fade Start", Float) = 30
        _FadeEnd ("Fade End", Float) = 50
        _FadeMinScale ("Fade Min Scale", Range(0, 1)) = 0.3

        [Header(Cutout)]
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0
    }

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "GrassForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #pragma shader_feature_local _WIND_ON
            #pragma shader_feature_local _INTERACTION_ON
            #pragma shader_feature_local _SSS_ON
            #pragma shader_feature_local _SPECULAR_ON
            #pragma shader_feature_local _DISTANCE_FADE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            TEXTURE2D(_WindNoiseTex);
            SAMPLER(sampler_MainTex);
            SAMPLER(sampler_WindNoiseTex);

            // Interaction positions (set from script)
            float4 _InteractionPositions[16];
            int _InteractionCount;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _TipColor;
                float _GradientPower;

                float _WindStrength;
                float _WindSpeed;
                float _WindFrequency;
                float4 _WindDirection;
                float4 _WindNoiseTex_ST;
                float _WindNoiseScale;
                float _WindGustStrength;
                float _WindGustFrequency;

                float _InteractionRadius;
                float _InteractionStrength;
                float _InteractionFalloff;

                float4 _SSSColor;
                float _SSSStrength;
                float _SSSDistortion;
                float _SSSPower;

                float _AO;
                float _AOPower;
                float _Roughness;

                float4 _SpecularColor;
                float _SpecularIntensity;
                float _SpecularPower;

                float _FadeStart;
                float _FadeEnd;
                float _FadeMinScale;

                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // R = height, G = AO, B = unused, A = stiffness
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float4 vertexColor : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                float fogFactor : TEXCOORD5;
                float heightGradient : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Wind functions
            float3 WindAnimation(float3 worldPos, float height, float stiffness, float time)
            {
                #if defined(_WIND_ON)
                    float3 windDir = normalize(_WindDirection.xyz);

                    // Sample wind noise
                    float2 noiseUV = worldPos.xz * _WindNoiseScale + time * _WindSpeed * 0.1;
                    float noise = SAMPLE_TEXTURE2D_LOD(_WindNoiseTex, sampler_WindNoiseTex, noiseUV, 0).r;

                    // Main wind wave
                    float windPhase = dot(worldPos.xz, windDir.xz) * _WindFrequency + time * _WindSpeed;
                    float wave = sin(windPhase) * 0.5 + 0.5;

                    // Gust
                    float gustPhase = time * _WindGustFrequency;
                    float gust = sin(gustPhase) * sin(gustPhase * 2.3) * _WindGustStrength;

                    // Combined wind
                    float windFactor = (wave + gust + noise - 0.5) * _WindStrength;
                    windFactor *= height * height; // Quadratic falloff from root
                    windFactor *= (1 - stiffness); // Stiffness reduces wind effect

                    return windDir * windFactor;
                #else
                    return float3(0, 0, 0);
                #endif
            }

            // Player interaction
            float3 InteractionDisplacement(float3 worldPos, float height)
            {
                #if defined(_INTERACTION_ON)
                    float3 totalDisplacement = float3(0, 0, 0);

                    for (int i = 0; i < _InteractionCount && i < 16; i++)
                    {
                        float3 interactorPos = _InteractionPositions[i].xyz;
                        float3 toGrass = worldPos - interactorPos;
                        toGrass.y = 0;

                        float dist = length(toGrass);
                        float influence = 1 - saturate(pow(dist / _InteractionRadius, _InteractionFalloff));

                        if (influence > 0.001)
                        {
                            float3 pushDir = normalize(toGrass + float3(0.001, 0, 0.001));
                            totalDisplacement += pushDir * influence * _InteractionStrength * height;
                        }
                    }

                    return totalDisplacement;
                #else
                    return float3(0, 0, 0);
                #endif
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float height = IN.color.r; // Height along blade (0 = root, 1 = tip)
                float stiffness = IN.color.a;
                float time = _Time.y;

                float3 posOS = IN.positionOS.xyz;
                float3 worldPosBase = TransformObjectToWorld(float3(0, 0, 0));

                // Distance fade
                #if defined(_DISTANCE_FADE_ON)
                    float distToCam = distance(worldPosBase, _WorldSpaceCameraPos);
                    float fadeFactor = saturate((distToCam - _FadeStart) / (_FadeEnd - _FadeStart));
                    float scale = lerp(1, _FadeMinScale, fadeFactor);
                    posOS *= scale;
                #endif

                float3 worldPos = TransformObjectToWorld(posOS);

                // Apply wind
                float3 windOffset = WindAnimation(worldPos, height, stiffness, time);
                worldPos += windOffset;

                // Apply interaction
                float3 interactOffset = InteractionDisplacement(worldPos, height);
                worldPos += interactOffset;

                OUT.worldPos = worldPos;
                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.vertexColor = IN.color;
                OUT.shadowCoord = TransformWorldToShadowCoord(worldPos);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                OUT.heightGradient = height;

                return OUT;
            }

            // SSS function
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

            float4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // Sample texture
                float4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(texColor.a - _Cutoff);

                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos);
                float3 normal = normalize(IN.worldNormal);

                // Height-based color gradient
                float gradientFactor = pow(IN.heightGradient, _GradientPower);
                float3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, gradientFactor) * texColor.rgb;

                // Ambient occlusion from vertex color
                float ao = lerp(1, IN.vertexColor.g, _AO);
                ao = pow(ao, _AOPower);

                // Lighting
                Light mainLight = GetMainLight(IN.shadowCoord);
                float3 lightDir = mainLight.direction;
                float3 lightColor = mainLight.color;
                float shadow = mainLight.shadowAttenuation;

                // Wrapped diffuse for softer look
                float NdotL = dot(normal, lightDir);
                float wrappedNdotL = NdotL * 0.5 + 0.5;
                float3 diffuse = albedo * lightColor * wrappedNdotL * shadow;

                // SSS
                float3 sss = SubsurfaceScattering(viewDir, lightDir, normal, lightColor);
                sss *= (1 - IN.heightGradient) * shadow; // More SSS at base

                // Specular
                float3 specular = float3(0, 0, 0);
                #if defined(_SPECULAR_ON)
                    float3 halfDir = normalize(lightDir + viewDir);
                    float NdotH = saturate(dot(normal, halfDir));
                    float spec = pow(NdotH, _SpecularPower) * _SpecularIntensity;
                    specular = _SpecularColor.rgb * spec * shadow * IN.heightGradient; // More specular at tip
                #endif

                // Additional lights
                float3 additionalLights = float3(0, 0, 0);
                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();
                    for (uint i = 0; i < pixelLightCount; i++)
                    {
                        Light light = GetAdditionalLight(i, IN.worldPos);
                        float addNdotL = saturate(dot(normal, light.direction));
                        additionalLights += albedo * light.color * light.distanceAttenuation * addNdotL * 0.5;
                    }
                #endif

                // Ambient
                float3 ambient = albedo * 0.1;

                // Combine
                float3 finalColor = (diffuse + sss + specular + additionalLights + ambient) * ao;

                // Fog
                finalColor = MixFog(finalColor, IN.fogFactor);

                return float4(finalColor, texColor.a);
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
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #pragma multi_compile_instancing
            #pragma shader_feature_local _WIND_ON
            #pragma shader_feature_local _DISTANCE_FADE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float3 _LightDirection;

            float3 ApplyShadowBiasCustom(float3 positionWS, float3 normalWS, float3 lightDirection)
            {
                float invNdotL = 1.0 - saturate(dot(lightDirection, normalWS));
                float scale = invNdotL * 0.001;
                positionWS = lightDirection * 0.001 + positionWS;
                positionWS = normalWS * scale.xxx + positionWS;
                return positionWS;
            }

            TEXTURE2D(_MainTex);
            TEXTURE2D(_WindNoiseTex);
            SAMPLER(sampler_MainTex);
            SAMPLER(sampler_WindNoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _WindStrength;
                float _WindSpeed;
                float _WindFrequency;
                float4 _WindDirection;
                float _WindNoiseScale;
                float _WindGustStrength;
                float _WindGustFrequency;
                float _FadeStart;
                float _FadeEnd;
                float _FadeMinScale;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 WindAnimation(float3 worldPos, float height, float stiffness, float time)
            {
                #if defined(_WIND_ON)
                    float3 windDir = normalize(_WindDirection.xyz);
                    float windPhase = dot(worldPos.xz, windDir.xz) * _WindFrequency + time * _WindSpeed;
                    float wave = sin(windPhase) * 0.5 + 0.5;
                    float gustPhase = time * _WindGustFrequency;
                    float gust = sin(gustPhase) * sin(gustPhase * 2.3) * _WindGustStrength;
                    float windFactor = (wave + gust - 0.25) * _WindStrength;
                    windFactor *= height * height;
                    windFactor *= (1 - stiffness);
                    return windDir * windFactor;
                #else
                    return float3(0, 0, 0);
                #endif
            }

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float height = IN.color.r;
                float stiffness = IN.color.a;

                float3 posOS = IN.positionOS.xyz;

                #if defined(_DISTANCE_FADE_ON)
                    float3 worldPosBase = TransformObjectToWorld(float3(0, 0, 0));
                    float distToCam = distance(worldPosBase, _WorldSpaceCameraPos);
                    float fadeFactor = saturate((distToCam - _FadeStart) / (_FadeEnd - _FadeStart));
                    float scale = lerp(1, _FadeMinScale, fadeFactor);
                    posOS *= scale;
                #endif

                float3 worldPos = TransformObjectToWorld(posOS);
                worldPos += WindAnimation(worldPos, height, stiffness, _Time.y);

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
                float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
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
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #pragma multi_compile_instancing
            #pragma shader_feature_local _WIND_ON
            #pragma shader_feature_local _DISTANCE_FADE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _WindStrength;
                float _WindSpeed;
                float _WindFrequency;
                float4 _WindDirection;
                float _WindGustStrength;
                float _WindGustFrequency;
                float _FadeStart;
                float _FadeEnd;
                float _FadeMinScale;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float3 WindAnimation(float3 worldPos, float height, float stiffness, float time)
            {
                #if defined(_WIND_ON)
                    float3 windDir = normalize(_WindDirection.xyz);
                    float windPhase = dot(worldPos.xz, windDir.xz) * _WindFrequency + time * _WindSpeed;
                    float wave = sin(windPhase) * 0.5 + 0.5;
                    float gustPhase = time * _WindGustFrequency;
                    float gust = sin(gustPhase) * sin(gustPhase * 2.3) * _WindGustStrength;
                    float windFactor = (wave + gust - 0.25) * _WindStrength;
                    windFactor *= height * height;
                    windFactor *= (1 - stiffness);
                    return windDir * windFactor;
                #else
                    return float3(0, 0, 0);
                #endif
            }

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                float height = IN.color.r;
                float stiffness = IN.color.a;
                float3 posOS = IN.positionOS.xyz;

                #if defined(_DISTANCE_FADE_ON)
                    float3 worldPosBase = TransformObjectToWorld(float3(0, 0, 0));
                    float distToCam = distance(worldPosBase, _WorldSpaceCameraPos);
                    float fadeFactor = saturate((distToCam - _FadeStart) / (_FadeEnd - _FadeStart));
                    posOS *= lerp(1, _FadeMinScale, fadeFactor);
                #endif

                float3 worldPos = TransformObjectToWorld(posOS);
                worldPos += WindAnimation(worldPos, height, stiffness, _Time.y);

                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);

                return OUT;
            }

            float4 DepthFrag(Varyings IN) : SV_Target
            {
                float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    // ==================== Built-in Pipeline SubShader ====================
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        Pass
        {
            Name "GrassBuiltIn"
            Tags { "LightMode" = "ForwardBase" }
            Cull Off

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #pragma shader_feature_local _WIND_ON
            #pragma shader_feature_local _SSS_ON
            #pragma shader_feature_local _SPECULAR_ON

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _BaseColor;
            float4 _TipColor;
            float _GradientPower;
            float _WindStrength;
            float _WindSpeed;
            float _WindFrequency;
            float4 _WindDirection;
            float _WindGustStrength;
            float _WindGustFrequency;
            float4 _SSSColor;
            float _SSSStrength;
            float _SSSDistortion;
            float _SSSPower;
            float4 _SpecularColor;
            float _SpecularIntensity;
            float _SpecularPower;
            float _AO;
            float _AOPower;
            float _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float4 vertexColor : TEXCOORD3;
                float heightGradient : TEXCOORD4;
                SHADOW_COORDS(5)
                UNITY_FOG_COORDS(6)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 WindAnimation(float3 worldPos, float height, float stiffness, float time)
            {
                #if defined(_WIND_ON)
                    float3 windDir = normalize(_WindDirection.xyz);
                    float windPhase = dot(worldPos.xz, windDir.xz) * _WindFrequency + time * _WindSpeed;
                    float wave = sin(windPhase) * 0.5 + 0.5;
                    float gustPhase = time * _WindGustFrequency;
                    float gust = sin(gustPhase) * sin(gustPhase * 2.3) * _WindGustStrength;
                    float windFactor = (wave + gust - 0.25) * _WindStrength;
                    windFactor *= height * height * (1 - stiffness);
                    return windDir * windFactor;
                #else
                    return float3(0, 0, 0);
                #endif
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float height = v.color.r;
                float stiffness = v.color.a;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                worldPos += WindAnimation(worldPos, height, stiffness, _Time.y);

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = worldPos;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.vertexColor = v.color;
                o.heightGradient = height;
                TRANSFER_SHADOW(o)
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float4 texColor = tex2D(_MainTex, i.uv);
                clip(texColor.a - _Cutoff);

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 normal = normalize(i.worldNormal);

                float gradientFactor = pow(i.heightGradient, _GradientPower);
                float3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, gradientFactor) * texColor.rgb;

                float ao = pow(lerp(1, i.vertexColor.g, _AO), _AOPower);

                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 lightColor = _LightColor0.rgb;
                float shadow = SHADOW_ATTENUATION(i);

                float NdotL = saturate(dot(normal, lightDir));
                float3 diffuse = albedo * lightColor * NdotL * shadow;

                float3 sss = float3(0, 0, 0);
                #if defined(_SSS_ON)
                    float3 H = normalize(lightDir + normal * _SSSDistortion);
                    float VdotH = pow(saturate(dot(viewDir, -H)), _SSSPower);
                    sss = _SSSColor.rgb * VdotH * _SSSStrength * lightColor * shadow;
                #endif

                float3 specular = float3(0, 0, 0);
                #if defined(_SPECULAR_ON)
                    float3 halfDir = normalize(lightDir + viewDir);
                    float NdotH = saturate(dot(normal, halfDir));
                    float spec = pow(NdotH, _SpecularPower);
                    specular = _SpecularColor.rgb * spec * _SpecularIntensity * lightColor * shadow;
                #endif

                float3 ambient = albedo * 0.1;
                float3 finalColor = (diffuse + sss + specular + ambient) * ao;
                UNITY_APPLY_FOG(i.fogCoord, finalColor);

                return float4(finalColor, texColor.a);
            }
            ENDCG
        }
    }

    // ==================== HDRP SubShader ====================
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.high-definition" }
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "GrassHDRP"
            Tags { "LightMode" = "Forward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _WIND_ON
            #pragma shader_feature_local _SSS_ON

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _TipColor;
                float _GradientPower;
                float _WindStrength;
                float _WindSpeed;
                float _WindFrequency;
                float4 _WindDirection;
                float _WindGustStrength;
                float _WindGustFrequency;
                float4 _SSSColor;
                float _SSSStrength;
                float _SSSDistortion;
                float _SSSPower;
                float _AO;
                float _AOPower;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float4 vertexColor : TEXCOORD3;
                float heightGradient : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 WindAnimation(float3 worldPos, float height, float stiffness, float time)
            {
                #if defined(_WIND_ON)
                    float3 windDir = normalize(_WindDirection.xyz);
                    float windPhase = dot(worldPos.xz, windDir.xz) * _WindFrequency + time * _WindSpeed;
                    float wave = sin(windPhase) * 0.5 + 0.5;
                    float gust = sin(time * _WindGustFrequency) * sin(time * _WindGustFrequency * 2.3) * _WindGustStrength;
                    float windFactor = (wave + gust - 0.25) * _WindStrength * height * height * (1 - stiffness);
                    return windDir * windFactor;
                #else
                    return float3(0, 0, 0);
                #endif
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float height = IN.color.r;
                float stiffness = IN.color.a;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                worldPos += WindAnimation(worldPos, height, stiffness, _Time.y);

                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv = IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                OUT.worldPos = worldPos;
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.vertexColor = IN.color;
                OUT.heightGradient = height;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(texColor.a - _Cutoff);

                float3 viewDir = GetWorldSpaceNormalizeViewDir(IN.worldPos);
                float3 normal = normalize(IN.worldNormal);

                float gradientFactor = pow(IN.heightGradient, _GradientPower);
                float3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, gradientFactor) * texColor.rgb;
                float ao = pow(lerp(1, IN.vertexColor.g, _AO), _AOPower);

                float3 lightDir = float3(0.5, 0.8, 0.2);
                float NdotL = saturate(dot(normal, lightDir));
                float3 diffuse = albedo * NdotL;

                float3 sss = float3(0, 0, 0);
                #if defined(_SSS_ON)
                    float3 H = normalize(lightDir + normal * _SSSDistortion);
                    float VdotH = pow(saturate(dot(viewDir, -H)), _SSSPower);
                    sss = _SSSColor.rgb * VdotH * _SSSStrength;
                #endif

                float3 finalColor = (diffuse + sss + albedo * 0.1) * ao;
                return float4(finalColor, texColor.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
