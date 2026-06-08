Shader "Synaptic/HairPro"
{
    Properties
    {
        [Header(Base)]
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (0.3, 0.2, 0.15, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Kajiya Kay Specular)]
        _SpecularShift ("Specular Shift", Range(-1, 1)) = 0.1
        _ShiftTex ("Shift Texture", 2D) = "gray" {}

        [Header(Primary Specular)]
        _SpecularColor1 ("Primary Color", Color) = (1, 1, 1, 1)
        _SpecularWidth1 ("Primary Width", Range(0, 1)) = 0.5
        _SpecularStrength1 ("Primary Strength", Range(0, 2)) = 0.5
        _SpecularShift1 ("Primary Shift", Range(-1, 1)) = 0.1

        [Header(Secondary Specular)]
        _SpecularColor2 ("Secondary Color", Color) = (0.8, 0.6, 0.4, 1)
        _SpecularWidth2 ("Secondary Width", Range(0, 1)) = 0.3
        _SpecularStrength2 ("Secondary Strength", Range(0, 2)) = 0.3
        _SpecularShift2 ("Secondary Shift", Range(-1, 1)) = -0.1

        [Header(Anisotropic)]
        _Anisotropy ("Anisotropy", Range(0, 1)) = 0.8
        _TangentMap ("Tangent Map", 2D) = "bump" {}

        [Header(Rim Light)]
        _RimColor ("Rim Color", Color) = (1, 0.9, 0.8, 1)
        _RimPower ("Rim Power", Range(0.1, 10)) = 3
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.5

        [Header(Shadow)]
        _ShadowColor ("Shadow Color", Color) = (0.4, 0.3, 0.3, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _ShadowSoftness ("Shadow Softness", Range(0, 1)) = 0.1

        [Header(Subsurface Scattering)]
        _SSSColor ("SSS Color", Color) = (1, 0.5, 0.3, 1)
        _SSSStrength ("SSS Strength", Range(0, 2)) = 0.3
        _SSSDistortion ("SSS Distortion", Range(0, 1)) = 0.5

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.1, 0.05, 0.05, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.002

        [Header(Wind Animation)]
        _WindStrength ("Wind Strength", Range(0, 1)) = 0.1
        _WindSpeed ("Wind Speed", Range(0, 5)) = 1
        _WindDirection ("Wind Direction", Vector) = (1, 0, 0.5, 0)
    }

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

        // Main Pass
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
                float _SpecularShift;
                float4 _SpecularColor1;
                float _SpecularWidth1;
                float _SpecularStrength1;
                float _SpecularShift1;
                float4 _SpecularColor2;
                float _SpecularWidth2;
                float _SpecularStrength2;
                float _SpecularShift2;
                float _Anisotropy;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSoftness;
                float4 _SSSColor;
                float _SSSStrength;
                float _SSSDistortion;
                float4 _OutlineColor;
                float _OutlineWidth;
                float _WindStrength;
                float _WindSpeed;
                float4 _WindDirection;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShiftTex);
            SAMPLER(sampler_ShiftTex);
            TEXTURE2D(_TangentMap);
            SAMPLER(sampler_TangentMap);

            // Kajiya-Kay Specular Function
            float KajiyaKaySpecular(float3 T, float3 H, float width)
            {
                float TdotH = dot(T, H);
                float sinTH = sqrt(1.0 - TdotH * TdotH);
                float dirAtten = smoothstep(-1.0, 0.0, TdotH);
                return dirAtten * pow(sinTH, width * 100.0);
            }

            // Shift tangent by normal and shift amount
            float3 SynapticShiftTangent(float3 T, float3 N, float shift)
            {
                return normalize(T + N * shift);
            }

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
                float fogFactor : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                // Wind animation
                float3 posOS = input.positionOS.xyz;
                float windPhase = _Time.y * _WindSpeed + posOS.x * 2.0 + posOS.z * 2.0;
                float windOffset = sin(windPhase) * _WindStrength * input.uv.y;
                posOS += _WindDirection.xyz * windOffset;

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.bitangentWS = normalInputs.bitangentWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample textures
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                clip(albedo.a - _Cutoff);

                float shiftTex = SAMPLE_TEXTURE2D(_ShiftTex, sampler_ShiftTex, input.uv).r - 0.5;

                // Normalize vectors
                float3 N = normalize(input.normalWS);
                float3 T = normalize(input.tangentWS);
                float3 B = normalize(input.bitangentWS);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Get main light
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 L = mainLight.direction;
                float3 H = normalize(L + V);

                // Shift tangent for hair strands
                float shift1 = _SpecularShift + _SpecularShift1 + shiftTex;
                float shift2 = _SpecularShift + _SpecularShift2 + shiftTex;
                float3 T1 = SynapticShiftTangent(B, N, shift1);
                float3 T2 = SynapticShiftTangent(B, N, shift2);

                // Kajiya-Kay dual specular
                float spec1 = KajiyaKaySpecular(T1, H, _SpecularWidth1) * _SpecularStrength1;
                float spec2 = KajiyaKaySpecular(T2, H, _SpecularWidth2) * _SpecularStrength2;

                float3 specular = _SpecularColor1.rgb * spec1 + _SpecularColor2.rgb * spec2;
                specular *= mainLight.color * mainLight.shadowAttenuation;

                // Diffuse with toon shading
                float NdotL = dot(N, L);
                float halfLambert = NdotL * 0.5 + 0.5;
                float shadow = smoothstep(_ShadowThreshold - _ShadowSoftness, _ShadowThreshold + _ShadowSoftness, halfLambert);
                shadow *= mainLight.shadowAttenuation;

                float3 diffuse = lerp(_ShadowColor.rgb, float3(1,1,1), shadow) * albedo.rgb;

                // Subsurface Scattering
                float3 sssH = normalize(L + N * _SSSDistortion);
                float VdotH = pow(saturate(dot(V, -sssH)), 3.0);
                float3 sss = _SSSColor.rgb * VdotH * _SSSStrength * (1.0 - shadow);

                // Rim light
                float NdotV = saturate(dot(N, V));
                float rim = pow(1.0 - NdotV, _RimPower) * _RimStrength;
                float3 rimColor = _RimColor.rgb * rim * mainLight.color;

                // Additional lights
                float3 additionalLight = float3(0, 0, 0);
                #ifdef _ADDITIONAL_LIGHTS
                uint additionalLightsCount = GetAdditionalLightsCount();
                for (uint i = 0; i < additionalLightsCount; ++i)
                {
                    Light light = GetAdditionalLight(i, input.positionWS);
                    float addNdotL = saturate(dot(N, light.direction));
                    additionalLight += light.color * light.distanceAttenuation * addNdotL * 0.5;
                }
                #endif

                // Combine
                float3 finalColor = diffuse * mainLight.color + specular + sss + rimColor + additionalLight * albedo.rgb;
                finalColor = MixFog(finalColor, input.fogFactor);

                return half4(finalColor, albedo.a);
            }
            ENDHLSL
        }

        // Outline Pass
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
                float _SpecularShift;
                float4 _SpecularColor1;
                float _SpecularWidth1;
                float _SpecularStrength1;
                float _SpecularShift1;
                float4 _SpecularColor2;
                float _SpecularWidth2;
                float _SpecularStrength2;
                float _SpecularShift2;
                float _Anisotropy;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSoftness;
                float4 _SSSColor;
                float _SSSStrength;
                float _SSSDistortion;
                float4 _OutlineColor;
                float _OutlineWidth;
                float _WindStrength;
                float _WindSpeed;
                float4 _WindDirection;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

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

            Varyings vertOutline(Attributes input)
            {
                Varyings output;

                float3 normalOS = normalize(input.normalOS);
                float3 posOS = input.positionOS.xyz + normalOS * _OutlineWidth;

                output.positionCS = TransformObjectToHClip(posOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                return output;
            }

            half4 fragOutline(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(albedo.a - _Cutoff);
                return _OutlineColor;
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
            Cull Off

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

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
                float _SpecularShift;
                float4 _SpecularColor1;
                float _SpecularWidth1;
                float _SpecularStrength1;
                float _SpecularShift1;
                float4 _SpecularColor2;
                float _SpecularWidth2;
                float _SpecularStrength2;
                float _SpecularShift2;
                float _Anisotropy;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSoftness;
                float4 _SSSColor;
                float _SSSStrength;
                float _SSSDistortion;
                float4 _OutlineColor;
                float _OutlineWidth;
                float _WindStrength;
                float _WindSpeed;
                float4 _WindDirection;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

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
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                return output;
            }

            half4 fragShadow(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(albedo.a - _Cutoff);
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
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
                float _SpecularShift;
                float4 _SpecularColor1;
                float _SpecularWidth1;
                float _SpecularStrength1;
                float _SpecularShift1;
                float4 _SpecularColor2;
                float _SpecularWidth2;
                float _SpecularStrength2;
                float _SpecularShift2;
                float _Anisotropy;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSoftness;
                float4 _SSSColor;
                float _SSSStrength;
                float _SSSDistortion;
                float4 _OutlineColor;
                float _OutlineWidth;
                float _WindStrength;
                float _WindSpeed;
                float4 _WindDirection;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

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

            Varyings vertDepth(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 fragDepth(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(albedo.a - _Cutoff);
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

        // Main Pass
        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode" = "ForwardBase" }

            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _ShiftTex;
            sampler2D _TangentMap;

            fixed4 _Color;
            float _Cutoff;

            float _SpecularShift;
            fixed4 _SpecularColor1;
            float _SpecularWidth1;
            float _SpecularStrength1;
            float _SpecularShift1;

            fixed4 _SpecularColor2;
            float _SpecularWidth2;
            float _SpecularStrength2;
            float _SpecularShift2;

            fixed4 _RimColor;
            float _RimPower;
            float _RimStrength;

            fixed4 _ShadowColor;
            float _ShadowThreshold;
            float _ShadowSoftness;

            fixed4 _SSSColor;
            float _SSSStrength;
            float _SSSDistortion;

            float _WindStrength;
            float _WindSpeed;
            float4 _WindDirection;

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
                UNITY_FOG_COORDS(5)
                SHADOW_COORDS(6)
            };

            float KajiyaKaySpec(float3 T, float3 H, float width)
            {
                float TdotH = dot(T, H);
                float sinTH = sqrt(1.0 - TdotH * TdotH);
                float dirAtten = smoothstep(-1.0, 0.0, TdotH);
                return dirAtten * pow(sinTH, width * 100.0);
            }

            float3 ShiftT(float3 T, float3 N, float shift)
            {
                return normalize(T + N * shift);
            }

            v2f vert(appdata v)
            {
                v2f o;

                // Wind animation
                float3 posOS = v.vertex.xyz;
                float windPhase = _Time.y * _WindSpeed + posOS.x * 2.0 + posOS.z * 2.0;
                float windOffset = sin(windPhase) * _WindStrength * v.uv.y;
                posOS += _WindDirection.xyz * windOffset;

                o.pos = UnityObjectToClipPos(float4(posOS, 1));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, float4(posOS, 1)).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * v.tangent.w;

                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                clip(albedo.a - _Cutoff);

                float shiftTex = tex2D(_ShiftTex, i.uv).r - 0.5;

                float3 N = normalize(i.worldNormal);
                float3 T = normalize(i.worldTangent);
                float3 B = normalize(i.worldBitangent);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 H = normalize(L + V);

                // Kajiya-Kay
                float shift1 = _SpecularShift + _SpecularShift1 + shiftTex;
                float shift2 = _SpecularShift + _SpecularShift2 + shiftTex;
                float3 T1 = ShiftT(B, N, shift1);
                float3 T2 = ShiftT(B, N, shift2);

                float spec1 = KajiyaKaySpec(T1, H, _SpecularWidth1) * _SpecularStrength1;
                float spec2 = KajiyaKaySpec(T2, H, _SpecularWidth2) * _SpecularStrength2;
                float3 specular = _SpecularColor1.rgb * spec1 + _SpecularColor2.rgb * spec2;

                // Shadow
                float atten = SHADOW_ATTENUATION(i);
                float NdotL = dot(N, L);
                float halfLambert = NdotL * 0.5 + 0.5;
                float shadow = smoothstep(_ShadowThreshold - _ShadowSoftness, _ShadowThreshold + _ShadowSoftness, halfLambert);
                shadow *= atten;

                float3 diffuse = lerp(_ShadowColor.rgb, float3(1,1,1), shadow) * albedo.rgb;

                // SSS
                float3 sssH = normalize(L + N * _SSSDistortion);
                float VdotH = pow(saturate(dot(V, -sssH)), 3.0);
                float3 sss = _SSSColor.rgb * VdotH * _SSSStrength * (1.0 - shadow);

                // Rim
                float NdotV = saturate(dot(N, V));
                float rim = pow(1.0 - NdotV, _RimPower) * _RimStrength;
                float3 rimColor = _RimColor.rgb * rim;

                float3 finalColor = diffuse * _LightColor0.rgb + specular * _LightColor0.rgb * atten + sss + rimColor;

                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                return fixed4(finalColor, albedo.a);
            }
            ENDCG
        }

        // Outline Pass
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "Always" }

            Cull Front

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;
            fixed4 _OutlineColor;
            float _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 normalOS = normalize(v.normal);
                float3 posOS = v.vertex.xyz + normalOS * _OutlineWidth;
                o.pos = UnityObjectToClipPos(float4(posOS, 1));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv);
                clip(albedo.a - _Cutoff);
                return _OutlineColor;
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
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv);
                clip(albedo.a - _Cutoff);
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

            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
                float _SpecularShift;
                float4 _SpecularColor1;
                float _SpecularWidth1;
                float _SpecularStrength1;
                float _SpecularShift1;
                float4 _SpecularColor2;
                float _SpecularWidth2;
                float _SpecularStrength2;
                float _SpecularShift2;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSoftness;
                float4 _SSSColor;
                float _SSSStrength;
                float _SSSDistortion;
                float _WindStrength;
                float _WindSpeed;
                float4 _WindDirection;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShiftTex);
            SAMPLER(sampler_ShiftTex);

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
            };

            float KKSpec(float3 T, float3 H, float width)
            {
                float TdotH = dot(T, H);
                float sinTH = sqrt(1.0 - TdotH * TdotH);
                float dirAtten = smoothstep(-1.0, 0.0, TdotH);
                return dirAtten * pow(sinTH, width * 100.0);
            }

            float3 ShiftTan(float3 T, float3 N, float shift)
            {
                return normalize(T + N * shift);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 posOS = input.positionOS.xyz;
                float windPhase = _Time.y * _WindSpeed + posOS.x * 2.0 + posOS.z * 2.0;
                float windOffset = sin(windPhase) * _WindStrength * input.uv.y;
                posOS += _WindDirection.xyz * windOffset;

                output.positionWS = TransformObjectToWorld(posOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                output.bitangentWS = cross(output.normalWS, output.tangentWS) * input.tangentOS.w;
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                clip(albedo.a - _Cutoff);

                float shiftTex = SAMPLE_TEXTURE2D(_ShiftTex, sampler_ShiftTex, input.uv).r - 0.5;

                float3 N = normalize(input.normalWS);
                float3 T = normalize(input.tangentWS);
                float3 B = normalize(input.bitangentWS);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 L = normalize(float3(0.5, 1, 0.3));
                float3 H = normalize(L + V);

                float shift1 = _SpecularShift + _SpecularShift1 + shiftTex;
                float shift2 = _SpecularShift + _SpecularShift2 + shiftTex;
                float3 T1 = ShiftTan(B, N, shift1);
                float3 T2 = ShiftTan(B, N, shift2);

                float spec1 = KKSpec(T1, H, _SpecularWidth1) * _SpecularStrength1;
                float spec2 = KKSpec(T2, H, _SpecularWidth2) * _SpecularStrength2;
                float3 specular = _SpecularColor1.rgb * spec1 + _SpecularColor2.rgb * spec2;

                float NdotL = dot(N, L);
                float halfLambert = NdotL * 0.5 + 0.5;
                float shadow = smoothstep(_ShadowThreshold - _ShadowSoftness, _ShadowThreshold + _ShadowSoftness, halfLambert);

                float3 diffuse = lerp(_ShadowColor.rgb, float3(1,1,1), shadow) * albedo.rgb;

                float3 sssH = normalize(L + N * _SSSDistortion);
                float VdotH = pow(saturate(dot(V, -sssH)), 3.0);
                float3 sss = _SSSColor.rgb * VdotH * _SSSStrength * (1.0 - shadow);

                float NdotV = saturate(dot(N, V));
                float rim = pow(1.0 - NdotV, _RimPower) * _RimStrength;
                float3 rimColor = _RimColor.rgb * rim;

                float3 finalColor = diffuse + specular + sss + rimColor;

                return float4(finalColor, albedo.a);
            }
            ENDHLSL
        }
    }

    FallBack "Diffuse"
    CustomEditor "SynapticHairProEditor"
}
