Shader "Synaptic/EyePro"
{
    Properties
    {
        [Header(Iris)]
        _IrisColor ("Iris Color", Color) = (0.3, 0.5, 0.8, 1)
        _IrisTex ("Iris Texture", 2D) = "white" {}
        _IrisSize ("Iris Size", Range(0.1, 1)) = 0.5
        _IrisDepth ("Iris Depth (Parallax)", Range(0, 0.5)) = 0.15

        [Header(Pupil)]
        _PupilColor ("Pupil Color", Color) = (0.05, 0.05, 0.1, 1)
        _PupilSize ("Pupil Size", Range(0.05, 0.5)) = 0.2
        _PupilDilation ("Pupil Dilation", Range(0.5, 2)) = 1.0
        _PupilSharpness ("Pupil Sharpness", Range(1, 20)) = 10

        [Header(Sclera)]
        _ScleraColor ("Sclera Color", Color) = (1, 0.98, 0.95, 1)
        _ScleraTex ("Sclera Texture", 2D) = "white" {}

        [Header(Cornea Reflection)]
        _ReflectionStrength ("Reflection Strength", Range(0, 2)) = 1.0
        _ReflectionSmoothness ("Reflection Smoothness", Range(0, 1)) = 0.95
        _Cubemap ("Environment Map", CUBE) = "" {}

        [Header(Highlight)]
        _Highlight1Pos ("Highlight 1 Position", Vector) = (0.2, 0.3, 0, 0)
        _Highlight1Size ("Highlight 1 Size", Range(0, 0.3)) = 0.08
        _Highlight1Strength ("Highlight 1 Strength", Range(0, 2)) = 1.5

        _Highlight2Pos ("Highlight 2 Position", Vector) = (-0.15, -0.2, 0, 0)
        _Highlight2Size ("Highlight 2 Size", Range(0, 0.2)) = 0.04
        _Highlight2Strength ("Highlight 2 Strength", Range(0, 2)) = 0.8

        [Header(Limbal Ring)]
        _LimbalColor ("Limbal Ring Color", Color) = (0.1, 0.15, 0.2, 1)
        _LimbalWidth ("Limbal Ring Width", Range(0, 0.3)) = 0.1
        _LimbalStrength ("Limbal Ring Strength", Range(0, 2)) = 1.0

        [Header(Caustics)]
        _CausticsStrength ("Caustics Strength", Range(0, 1)) = 0.3
        _CausticsScale ("Caustics Scale", Range(1, 20)) = 8
        _CausticsSpeed ("Caustics Speed", Range(0, 2)) = 0.5

        [Header(Subsurface)]
        _SSSColor ("SSS Color", Color) = (1, 0.3, 0.2, 1)
        _SSSStrength ("SSS Strength", Range(0, 1)) = 0.2

        [Header(Rim Light)]
        _RimColor ("Rim Color", Color) = (0.8, 0.9, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 10)) = 4
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.5

        [Header(Shadow)]
        _ShadowColor ("Shadow Color", Color) = (0.3, 0.3, 0.4, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _IrisColor;
        float4 _IrisTex_ST;
        float _IrisSize;
        float _IrisDepth;

        float4 _PupilColor;
        float _PupilSize;
        float _PupilDilation;
        float _PupilSharpness;

        float4 _ScleraColor;

        float _ReflectionStrength;
        float _ReflectionSmoothness;

        float4 _Highlight1Pos;
        float _Highlight1Size;
        float _Highlight1Strength;

        float4 _Highlight2Pos;
        float _Highlight2Size;
        float _Highlight2Strength;

        float4 _LimbalColor;
        float _LimbalWidth;
        float _LimbalStrength;

        float _CausticsStrength;
        float _CausticsScale;
        float _CausticsSpeed;

        float4 _SSSColor;
        float _SSSStrength;

        float4 _RimColor;
        float _RimPower;
        float _RimStrength;

        float4 _ShadowColor;
        float _ShadowThreshold;
    CBUFFER_END

    TEXTURE2D(_IrisTex);
    SAMPLER(sampler_IrisTex);
    TEXTURE2D(_ScleraTex);
    SAMPLER(sampler_ScleraTex);
    TEXTURECUBE(_Cubemap);
    SAMPLER(sampler_Cubemap);

    // Simple noise for caustics
    float noise2D(float2 p)
    {
        return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
    }

    float smoothNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);

        float a = noise2D(i);
        float b = noise2D(i + float2(1, 0));
        float c = noise2D(i + float2(0, 1));
        float d = noise2D(i + float2(1, 1));

        return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    }

    float caustics(float2 uv, float time)
    {
        float c1 = smoothNoise(uv * _CausticsScale + time * 0.5);
        float c2 = smoothNoise(uv * _CausticsScale * 1.3 - time * 0.3);
        return pow(c1 * c2, 2) * 4.0;
    }

    ENDHLSL

    // ==================== URP SubShader ====================
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float3 viewDirTS : TEXCOORD5;
                float fogFactor : TEXCOORD6;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.bitangentWS = normalInputs.bitangentWS;
                output.uv = input.uv;

                // Tangent space view direction for parallax
                float3 viewDirWS = GetWorldSpaceViewDir(posInputs.positionWS);
                float3x3 TBN = float3x3(normalInputs.tangentWS, normalInputs.bitangentWS, normalInputs.normalWS);
                output.viewDirTS = mul(TBN, viewDirWS);

                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Centered UV
                float2 centeredUV = input.uv - 0.5;
                float dist = length(centeredUV);

                // Parallax offset for iris depth
                float3 viewDirTS = normalize(input.viewDirTS);
                float2 parallaxOffset = viewDirTS.xy / viewDirTS.z * _IrisDepth;
                float2 irisUV = centeredUV + parallaxOffset;
                float irisDist = length(irisUV);

                // Pupil (with dilation)
                float pupilRadius = _PupilSize * _PupilDilation;
                float pupilMask = 1.0 - saturate(pow(irisDist / pupilRadius, _PupilSharpness));

                // Iris mask
                float irisMask = saturate(1.0 - smoothstep(_IrisSize - 0.05, _IrisSize + 0.05, irisDist));
                irisMask *= (1.0 - pupilMask);

                // Limbal ring
                float limbalDist = abs(irisDist - _IrisSize);
                float limbalMask = (1.0 - smoothstep(0, _LimbalWidth, limbalDist)) * irisMask;

                // Sample iris texture
                float2 irisTexUV = (irisUV / _IrisSize) * 0.5 + 0.5;
                half4 irisTex = SAMPLE_TEXTURE2D(_IrisTex, sampler_IrisTex, irisTexUV);

                // Sample sclera
                half4 scleraTex = SAMPLE_TEXTURE2D(_ScleraTex, sampler_ScleraTex, input.uv);

                // Build eye color
                float3 eyeColor = _ScleraColor.rgb * scleraTex.rgb;
                eyeColor = lerp(eyeColor, _IrisColor.rgb * irisTex.rgb, irisMask);
                eyeColor = lerp(eyeColor, _LimbalColor.rgb, limbalMask * _LimbalStrength);
                eyeColor = lerp(eyeColor, _PupilColor.rgb, pupilMask);

                // Caustics inside iris
                float time = _Time.y * _CausticsSpeed;
                float causticsValue = caustics(irisUV * 2.0, time);
                eyeColor += causticsValue * _CausticsStrength * irisMask * (1.0 - pupilMask);

                // Normalize vectors
                float3 N = normalize(input.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Get main light
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 L = mainLight.direction;

                // Toon shading
                float NdotL = dot(N, L);
                float halfLambert = NdotL * 0.5 + 0.5;
                float shadow = step(_ShadowThreshold, halfLambert) * mainLight.shadowAttenuation;
                float3 diffuse = lerp(_ShadowColor.rgb, float3(1,1,1), shadow);

                // Environment reflection (cornea)
                float3 reflectDir = reflect(-V, N);
                float3 envReflection = SAMPLE_TEXTURECUBE_LOD(_Cubemap, sampler_Cubemap, reflectDir, (1.0 - _ReflectionSmoothness) * 6.0).rgb;
                float fresnel = pow(1.0 - saturate(dot(N, V)), 3.0);
                float3 reflection = envReflection * _ReflectionStrength * fresnel;

                // Highlights (anime style)
                float2 hl1UV = centeredUV - _Highlight1Pos.xy;
                float hl1 = 1.0 - saturate(length(hl1UV) / _Highlight1Size);
                hl1 = pow(hl1, 2.0) * _Highlight1Strength;

                float2 hl2UV = centeredUV - _Highlight2Pos.xy;
                float hl2 = 1.0 - saturate(length(hl2UV) / _Highlight2Size);
                hl2 = pow(hl2, 2.0) * _Highlight2Strength;

                float3 highlights = float3(1,1,1) * (hl1 + hl2);

                // Subsurface scattering
                float sss = pow(saturate(dot(-V, L)), 3.0) * _SSSStrength;
                float3 sssColor = _SSSColor.rgb * sss;

                // Rim light
                float NdotV = saturate(dot(N, V));
                float rim = pow(1.0 - NdotV, _RimPower) * _RimStrength;
                float3 rimColor = _RimColor.rgb * rim;

                // Combine
                float3 finalColor = eyeColor * diffuse * mainLight.color;
                finalColor += reflection;
                finalColor += highlights;
                finalColor += sssColor;
                finalColor += rimColor;

                finalColor = MixFog(finalColor, input.fogFactor);

                return half4(finalColor, 1);
            }
            ENDHLSL
        }

        // Shadow Caster
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow

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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vertShadow(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                posWS = ApplyShadowBiasCustom(posWS, normalWS, _LightDirection);
                output.positionCS = TransformWorldToHClip(posWS);
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            half4 fragShadow(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Depth Only
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vertDepth(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 fragDepth(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    // ==================== Built-in SubShader ====================
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _IrisTex;
            float4 _IrisTex_ST;
            sampler2D _ScleraTex;
            samplerCUBE _Cubemap;

            fixed4 _IrisColor;
            float _IrisSize;
            float _IrisDepth;

            fixed4 _PupilColor;
            float _PupilSize;
            float _PupilDilation;
            float _PupilSharpness;

            fixed4 _ScleraColor;

            float _ReflectionStrength;
            float _ReflectionSmoothness;

            float4 _Highlight1Pos;
            float _Highlight1Size;
            float _Highlight1Strength;

            float4 _Highlight2Pos;
            float _Highlight2Size;
            float _Highlight2Strength;

            fixed4 _LimbalColor;
            float _LimbalWidth;
            float _LimbalStrength;

            float _CausticsStrength;
            float _CausticsScale;
            float _CausticsSpeed;

            fixed4 _SSSColor;
            float _SSSStrength;

            fixed4 _RimColor;
            float _RimPower;
            float _RimStrength;

            fixed4 _ShadowColor;
            float _ShadowThreshold;

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
                float3 viewDirTS : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                SHADOW_COORDS(5)
            };

            float noise2D(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float smoothNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = noise2D(i);
                float b = noise2D(i + float2(1, 0));
                float c = noise2D(i + float2(0, 1));
                float d = noise2D(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float caustics(float2 uv, float time)
            {
                float c1 = smoothNoise(uv * _CausticsScale + time * 0.5);
                float c2 = smoothNoise(uv * _CausticsScale * 1.3 - time * 0.3);
                return pow(c1 * c2, 2) * 4.0;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);

                float3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                float3 worldBitangent = cross(o.worldNormal, worldTangent) * v.tangent.w;
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - o.worldPos);
                float3x3 TBN = float3x3(worldTangent, worldBitangent, o.worldNormal);
                o.viewDirTS = mul(TBN, viewDirWS);

                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 centeredUV = i.uv - 0.5;
                float dist = length(centeredUV);

                float3 viewDirTS = normalize(i.viewDirTS);
                float2 parallaxOffset = viewDirTS.xy / viewDirTS.z * _IrisDepth;
                float2 irisUV = centeredUV + parallaxOffset;
                float irisDist = length(irisUV);

                float pupilRadius = _PupilSize * _PupilDilation;
                float pupilMask = 1.0 - saturate(pow(irisDist / pupilRadius, _PupilSharpness));

                float irisMask = saturate(1.0 - smoothstep(_IrisSize - 0.05, _IrisSize + 0.05, irisDist));
                irisMask *= (1.0 - pupilMask);

                float limbalDist = abs(irisDist - _IrisSize);
                float limbalMask = (1.0 - smoothstep(0, _LimbalWidth, limbalDist)) * irisMask;

                float2 irisTexUV = (irisUV / _IrisSize) * 0.5 + 0.5;
                fixed4 irisTex = tex2D(_IrisTex, irisTexUV);
                fixed4 scleraTex = tex2D(_ScleraTex, i.uv);

                float3 eyeColor = _ScleraColor.rgb * scleraTex.rgb;
                eyeColor = lerp(eyeColor, _IrisColor.rgb * irisTex.rgb, irisMask);
                eyeColor = lerp(eyeColor, _LimbalColor.rgb, limbalMask * _LimbalStrength);
                eyeColor = lerp(eyeColor, _PupilColor.rgb, pupilMask);

                float time = _Time.y * _CausticsSpeed;
                float causticsValue = caustics(irisUV * 2.0, time);
                eyeColor += causticsValue * _CausticsStrength * irisMask * (1.0 - pupilMask);

                float3 N = normalize(i.worldNormal);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);

                float atten = SHADOW_ATTENUATION(i);
                float NdotL = dot(N, L);
                float halfLambert = NdotL * 0.5 + 0.5;
                float shadow = step(_ShadowThreshold, halfLambert) * atten;
                float3 diffuse = lerp(_ShadowColor.rgb, float3(1,1,1), shadow);

                float3 reflectDir = reflect(-V, N);
                float3 envReflection = texCUBElod(_Cubemap, float4(reflectDir, (1.0 - _ReflectionSmoothness) * 6.0)).rgb;
                float fresnel = pow(1.0 - saturate(dot(N, V)), 3.0);
                float3 reflection = envReflection * _ReflectionStrength * fresnel;

                float2 hl1UV = centeredUV - _Highlight1Pos.xy;
                float hl1 = 1.0 - saturate(length(hl1UV) / _Highlight1Size);
                hl1 = pow(hl1, 2.0) * _Highlight1Strength;

                float2 hl2UV = centeredUV - _Highlight2Pos.xy;
                float hl2 = 1.0 - saturate(length(hl2UV) / _Highlight2Size);
                hl2 = pow(hl2, 2.0) * _Highlight2Strength;

                float3 highlights = float3(1,1,1) * (hl1 + hl2);

                float sss = pow(saturate(dot(-V, L)), 3.0) * _SSSStrength;
                float3 sssColor = _SSSColor.rgb * sss;

                float NdotV = saturate(dot(N, V));
                float rim = pow(1.0 - NdotV, _RimPower) * _RimStrength;
                float3 rimColor = _RimColor.rgb * rim;

                float3 finalColor = eyeColor * diffuse * _LightColor0.rgb;
                finalColor += reflection;
                finalColor += highlights;
                finalColor += sssColor;
                finalColor += rimColor;

                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                return fixed4(finalColor, 1);
            }
            ENDCG
        }

        // Shadow Caster
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i);
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
            "RenderPipeline" = "HDRenderPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _IrisColor;
                float4 _IrisTex_ST;
                float _IrisSize;
                float _IrisDepth;
                float4 _PupilColor;
                float _PupilSize;
                float _PupilDilation;
                float _PupilSharpness;
                float4 _ScleraColor;
                float _ReflectionStrength;
                float _ReflectionSmoothness;
                float4 _Highlight1Pos;
                float _Highlight1Size;
                float _Highlight1Strength;
                float4 _Highlight2Pos;
                float _Highlight2Size;
                float _Highlight2Strength;
                float4 _LimbalColor;
                float _LimbalWidth;
                float _LimbalStrength;
                float _CausticsStrength;
                float _CausticsScale;
                float _CausticsSpeed;
                float4 _SSSColor;
                float _SSSStrength;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _ShadowColor;
                float _ShadowThreshold;
            CBUFFER_END

            TEXTURE2D(_IrisTex);
            SAMPLER(sampler_IrisTex);
            TEXTURE2D(_ScleraTex);
            SAMPLER(sampler_ScleraTex);
            TEXTURECUBE(_Cubemap);
            SAMPLER(sampler_Cubemap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewDirTS : TEXCOORD3;
            };

            float noise2D(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float smoothNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = noise2D(i);
                float b = noise2D(i + float2(1, 0));
                float c = noise2D(i + float2(0, 1));
                float d = noise2D(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float causticsFunc(float2 uv, float time)
            {
                float c1 = smoothNoise(uv * _CausticsScale + time * 0.5);
                float c2 = smoothNoise(uv * _CausticsScale * 1.3 - time * 0.3);
                return pow(c1 * c2, 2) * 4.0;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;

                float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                float3 bitangentWS = cross(output.normalWS, tangentWS) * input.tangentOS.w;
                float3 viewDirWS = GetWorldSpaceViewDir(output.positionWS);
                float3x3 TBN = float3x3(tangentWS, bitangentWS, output.normalWS);
                output.viewDirTS = mul(TBN, viewDirWS);

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 centeredUV = input.uv - 0.5;
                float dist = length(centeredUV);

                float3 viewDirTS = normalize(input.viewDirTS);
                float2 parallaxOffset = viewDirTS.xy / viewDirTS.z * _IrisDepth;
                float2 irisUV = centeredUV + parallaxOffset;
                float irisDist = length(irisUV);

                float pupilRadius = _PupilSize * _PupilDilation;
                float pupilMask = 1.0 - saturate(pow(irisDist / pupilRadius, _PupilSharpness));

                float irisMask = saturate(1.0 - smoothstep(_IrisSize - 0.05, _IrisSize + 0.05, irisDist));
                irisMask *= (1.0 - pupilMask);

                float limbalDist = abs(irisDist - _IrisSize);
                float limbalMask = (1.0 - smoothstep(0, _LimbalWidth, limbalDist)) * irisMask;

                float2 irisTexUV = (irisUV / _IrisSize) * 0.5 + 0.5;
                float4 irisTex = SAMPLE_TEXTURE2D(_IrisTex, sampler_IrisTex, irisTexUV);
                float4 scleraTex = SAMPLE_TEXTURE2D(_ScleraTex, sampler_ScleraTex, input.uv);

                float3 eyeColor = _ScleraColor.rgb * scleraTex.rgb;
                eyeColor = lerp(eyeColor, _IrisColor.rgb * irisTex.rgb, irisMask);
                eyeColor = lerp(eyeColor, _LimbalColor.rgb, limbalMask * _LimbalStrength);
                eyeColor = lerp(eyeColor, _PupilColor.rgb, pupilMask);

                float time = _Time.y * _CausticsSpeed;
                float causticsValue = causticsFunc(irisUV * 2.0, time);
                eyeColor += causticsValue * _CausticsStrength * irisMask * (1.0 - pupilMask);

                float3 N = normalize(input.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 L = normalize(float3(0.5, 1, 0.3));

                float NdotL = dot(N, L);
                float halfLambert = NdotL * 0.5 + 0.5;
                float shadow = step(_ShadowThreshold, halfLambert);
                float3 diffuse = lerp(_ShadowColor.rgb, float3(1,1,1), shadow);

                float3 reflectDir = reflect(-V, N);
                float3 envReflection = SAMPLE_TEXTURECUBE_LOD(_Cubemap, sampler_Cubemap, reflectDir, (1.0 - _ReflectionSmoothness) * 6.0).rgb;
                float fresnel = pow(1.0 - saturate(dot(N, V)), 3.0);
                float3 reflection = envReflection * _ReflectionStrength * fresnel;

                float2 hl1UV = centeredUV - _Highlight1Pos.xy;
                float hl1 = 1.0 - saturate(length(hl1UV) / _Highlight1Size);
                hl1 = pow(hl1, 2.0) * _Highlight1Strength;

                float2 hl2UV = centeredUV - _Highlight2Pos.xy;
                float hl2 = 1.0 - saturate(length(hl2UV) / _Highlight2Size);
                hl2 = pow(hl2, 2.0) * _Highlight2Strength;

                float3 highlights = float3(1,1,1) * (hl1 + hl2);

                float sss = pow(saturate(dot(-V, L)), 3.0) * _SSSStrength;
                float3 sssColor = _SSSColor.rgb * sss;

                float NdotV = saturate(dot(N, V));
                float rim = pow(1.0 - NdotV, _RimPower) * _RimStrength;
                float3 rimColor = _RimColor.rgb * rim;

                float3 finalColor = eyeColor * diffuse + reflection + highlights + sssColor + rimColor;

                return float4(finalColor, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Diffuse"
    CustomEditor "SynapticEyeProEditor"
}
