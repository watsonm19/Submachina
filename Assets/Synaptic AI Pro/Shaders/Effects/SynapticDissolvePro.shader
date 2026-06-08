Shader "Synaptic/DissolvePro"
{
    Properties
    {
        [Header(Base)]
        _MainTex ("Albedo", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1

        [Header(Dissolve)]
        _DissolveAmount ("Dissolve Amount", Range(0, 1)) = 0
        _DissolveNoise ("Dissolve Noise", 2D) = "white" {}
        _DissolveScale ("Noise Scale", Float) = 1
        _DissolveDirection ("Direction (XYZ)", Vector) = (0, 1, 0, 0)
        [Toggle(_DIRECTIONAL_DISSOLVE)] _DirectionalDissolve ("Directional", Float) = 0
        _DirectionalBias ("Directional Bias", Range(0, 1)) = 0.5

        [Header(Edge Effect)]
        _EdgeColor1 ("Edge Color 1", Color) = (1, 0.5, 0, 1)
        _EdgeColor2 ("Edge Color 2", Color) = (1, 0, 0, 1)
        _EdgeWidth ("Edge Width", Range(0, 0.5)) = 0.1
        _EdgeIntensity ("Edge Intensity", Range(0, 10)) = 3
        [Toggle(_EDGE_GLOW)] _EdgeGlow ("Edge Glow", Float) = 1

        [Header(Particles)]
        [Toggle(_PARTICLES_ON)] _ParticlesEnabled ("Enable Particles", Float) = 1
        _ParticleColor ("Particle Color", Color) = (1, 0.7, 0.3, 1)
        _ParticleSize ("Particle Size", Range(0, 0.1)) = 0.02
        _ParticleSpeed ("Particle Speed", Float) = 2
        _ParticleDensity ("Particle Density", Range(0, 1)) = 0.5

        [Header(Vertex Displacement)]
        [Toggle(_VERTEX_DISPLACEMENT)] _VertexDisplacement ("Vertex Displacement", Float) = 0
        _DisplacementStrength ("Displacement Strength", Float) = 0.5
        _DisplacementDirection ("Displacement Dir", Vector) = (0, 1, 0, 0)
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

        Pass
        {
            Name "DissolveForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #pragma shader_feature_local _DIRECTIONAL_DISSOLVE
            #pragma shader_feature_local _EDGE_GLOW
            #pragma shader_feature_local _PARTICLES_ON
            #pragma shader_feature_local _VERTEX_DISPLACEMENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            TEXTURE2D(_BumpMap);
            TEXTURE2D(_DissolveNoise);
            SAMPLER(sampler_MainTex);
            SAMPLER(sampler_BumpMap);
            SAMPLER(sampler_DissolveNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float4 _BumpMap_ST;
                float _BumpScale;

                float _DissolveAmount;
                float4 _DissolveNoise_ST;
                float _DissolveScale;
                float4 _DissolveDirection;
                float _DirectionalBias;

                float4 _EdgeColor1;
                float4 _EdgeColor2;
                float _EdgeWidth;
                float _EdgeIntensity;

                float4 _ParticleColor;
                float _ParticleSize;
                float _ParticleSpeed;
                float _ParticleDensity;

                float _DisplacementStrength;
                float4 _DisplacementDirection;
            CBUFFER_END

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
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                float fogFactor : TEXCOORD6;
                float3 objectPos : TEXCOORD7;
            };

            // Hash for particles
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 posOS = IN.positionOS.xyz;
                OUT.objectPos = posOS;

                // Sample noise for vertex displacement
                #if defined(_VERTEX_DISPLACEMENT)
                    float2 noiseUV = IN.uv * _DissolveScale;
                    float noise = SAMPLE_TEXTURE2D_LOD(_DissolveNoise, sampler_DissolveNoise, noiseUV, 0).r;

                    float dissolveEdge = 1 - _DissolveAmount;
                    float edgeFactor = saturate((noise - dissolveEdge) / _EdgeWidth);

                    if (edgeFactor > 0)
                    {
                        float3 dispDir = normalize(_DisplacementDirection.xyz);
                        posOS += dispDir * edgeFactor * _DisplacementStrength;
                    }
                #endif

                OUT.worldPos = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);

                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.worldTangent = TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.worldBitangent = cross(OUT.worldNormal, OUT.worldTangent) * IN.tangentOS.w;

                OUT.shadowCoord = TransformWorldToShadowCoord(OUT.worldPos);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // Sample dissolve noise
                float2 noiseUV = IN.uv * _DissolveScale;
                float noise = SAMPLE_TEXTURE2D(_DissolveNoise, sampler_DissolveNoise, noiseUV).r;

                // Directional dissolve
                #if defined(_DIRECTIONAL_DISSOLVE)
                    float3 dir = normalize(_DissolveDirection.xyz);
                    float dirFactor = dot(normalize(IN.objectPos), dir) * 0.5 + 0.5;
                    noise = lerp(noise, dirFactor, _DirectionalBias);
                #endif

                // Dissolve threshold
                float dissolveThreshold = 1 - _DissolveAmount;
                clip(noise - dissolveThreshold);

                // Edge detection
                float edge = 1 - saturate((noise - dissolveThreshold) / _EdgeWidth);

                // Sample textures
                float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _BaseColor;
                float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);

                // Transform normal
                float3x3 TBN = float3x3(IN.worldTangent, IN.worldBitangent, IN.worldNormal);
                float3 worldNormal = normalize(mul(normalTS, TBN));

                // Lighting
                Light mainLight = GetMainLight(IN.shadowCoord);
                float3 lightDir = mainLight.direction;
                float3 lightColor = mainLight.color * mainLight.shadowAttenuation;
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos);

                float NdotL = saturate(dot(worldNormal, lightDir));
                float3 diffuse = albedo.rgb * lightColor * NdotL;

                // Specular
                float3 halfDir = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(worldNormal, halfDir));
                float spec = pow(NdotH, _Smoothness * 128);
                float3 specular = spec * lightColor * _Metallic;

                // Ambient
                float3 ambient = albedo.rgb * 0.1;

                float3 finalColor = diffuse + specular + ambient;

                // Edge glow
                #if defined(_EDGE_GLOW)
                    float3 edgeColor = lerp(_EdgeColor2.rgb, _EdgeColor1.rgb, edge);
                    float edgeGlow = edge * _EdgeIntensity;
                    finalColor += edgeColor * edgeGlow;
                #endif

                // Particles
                #if defined(_PARTICLES_ON)
                    float particleHash = Hash(IN.uv * 100 + _Time.y * _ParticleSpeed);
                    if (particleHash > 1 - _ParticleDensity && edge > 0.5)
                    {
                        float particleBrightness = particleHash * edge * 2;
                        finalColor += _ParticleColor.rgb * particleBrightness;
                    }
                #endif

                // Fog
                finalColor = MixFog(finalColor, IN.fogFactor);

                return float4(finalColor, albedo.a);
            }
            ENDHLSL
        }

        // Shadow caster
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

            #pragma shader_feature_local _DIRECTIONAL_DISSOLVE

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

            TEXTURE2D(_DissolveNoise);
            SAMPLER(sampler_DissolveNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _DissolveAmount;
                float _DissolveScale;
                float4 _DissolveDirection;
                float _DirectionalBias;
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
                float3 objectPos : TEXCOORD1;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(IN.normalOS);

                worldPos = ApplyShadowBiasCustom(worldPos, worldNormal, _LightDirection);
                OUT.positionCS = TransformWorldToHClip(worldPos);
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.objectPos = IN.positionOS.xyz;

                return OUT;
            }

            float4 ShadowFrag(Varyings IN) : SV_Target
            {
                float noise = SAMPLE_TEXTURE2D(_DissolveNoise, sampler_DissolveNoise, IN.uv * _DissolveScale).r;

                #if defined(_DIRECTIONAL_DISSOLVE)
                    float3 dir = normalize(_DissolveDirection.xyz);
                    float dirFactor = dot(normalize(IN.objectPos), dir) * 0.5 + 0.5;
                    noise = lerp(noise, dirFactor, _DirectionalBias);
                #endif

                clip(noise - (1 - _DissolveAmount));
                return 0;
            }
            ENDHLSL
        }
    }

    // ==================== Built-in Pipeline SubShader ====================
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "DissolveBuiltIn"
            Tags { "LightMode" = "ForwardBase" }
            Cull Back

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            #pragma shader_feature_local _DIRECTIONAL_DISSOLVE
            #pragma shader_feature_local _EDGE_GLOW

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            sampler2D _DissolveNoise;
            float4 _MainTex_ST;
            float4 _BaseColor;
            float _DissolveAmount;
            float _DissolveScale;
            float4 _DissolveDirection;
            float _DirectionalBias;
            float4 _EdgeColor1;
            float4 _EdgeColor2;
            float _EdgeWidth;
            float _EdgeIntensity;

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
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 objectPos : TEXCOORD3;
                SHADOW_COORDS(4)
                UNITY_FOG_COORDS(5)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.objectPos = v.vertex.xyz;
                TRANSFER_SHADOW(o)
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 noiseUV = i.uv * _DissolveScale;
                float noise = tex2D(_DissolveNoise, noiseUV).r;

                #if defined(_DIRECTIONAL_DISSOLVE)
                    float3 dir = normalize(_DissolveDirection.xyz);
                    float dirFactor = dot(normalize(i.objectPos), dir) * 0.5 + 0.5;
                    noise = lerp(noise, dirFactor, _DirectionalBias);
                #endif

                float dissolveThreshold = 1 - _DissolveAmount;
                clip(noise - dissolveThreshold);

                float edge = 1 - saturate((noise - dissolveThreshold) / _EdgeWidth);

                float4 albedo = tex2D(_MainTex, i.uv) * _BaseColor;
                float3 normal = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float shadow = SHADOW_ATTENUATION(i);

                float NdotL = saturate(dot(normal, lightDir));
                float3 diffuse = albedo.rgb * _LightColor0.rgb * NdotL * shadow;
                float3 ambient = albedo.rgb * 0.1;
                float3 finalColor = diffuse + ambient;

                #if defined(_EDGE_GLOW)
                    float3 edgeColor = lerp(_EdgeColor2.rgb, _EdgeColor1.rgb, edge);
                    finalColor += edgeColor * edge * _EdgeIntensity;
                #endif

                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                return float4(finalColor, albedo.a);
            }
            ENDCG
        }
    }

    // ==================== HDRP SubShader ====================
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.high-definition" }
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "DissolveHDRP"
            Tags { "LightMode" = "Forward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _DIRECTIONAL_DISSOLVE
            #pragma shader_feature_local _EDGE_GLOW

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            TEXTURE2D(_DissolveNoise);
            SAMPLER(sampler_MainTex);
            SAMPLER(sampler_DissolveNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _DissolveAmount;
                float _DissolveScale;
                float4 _DissolveDirection;
                float _DirectionalBias;
                float4 _EdgeColor1;
                float4 _EdgeColor2;
                float _EdgeWidth;
                float _EdgeIntensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 worldNormal : TEXCOORD1; float3 objectPos : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.objectPos = IN.positionOS.xyz;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float noise = SAMPLE_TEXTURE2D(_DissolveNoise, sampler_DissolveNoise, IN.uv * _DissolveScale).r;
                #if defined(_DIRECTIONAL_DISSOLVE)
                    float dirFactor = dot(normalize(IN.objectPos), normalize(_DissolveDirection.xyz)) * 0.5 + 0.5;
                    noise = lerp(noise, dirFactor, _DirectionalBias);
                #endif
                clip(noise - (1 - _DissolveAmount));

                float edge = 1 - saturate((noise - (1 - _DissolveAmount)) / _EdgeWidth);
                float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _BaseColor;
                float3 finalColor = albedo.rgb;
                #if defined(_EDGE_GLOW)
                    finalColor += lerp(_EdgeColor2.rgb, _EdgeColor1.rgb, edge) * edge * _EdgeIntensity;
                #endif
                return float4(finalColor, albedo.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
