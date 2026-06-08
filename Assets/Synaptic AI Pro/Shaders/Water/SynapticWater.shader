Shader "Synaptic/Water"
{
    Properties
    {
        [Header(Water Color)]
        _ShallowColor ("Shallow Color", Color) = (0.3, 0.8, 0.9, 0.6)
        _DeepColor ("Deep Color", Color) = (0.1, 0.3, 0.5, 0.9)
        _DepthMaxDistance ("Depth Max Distance", Float) = 5.0

        [Header(Waves)]
        _WaveSpeed ("Wave Speed", Float) = 1.0
        _WaveStrength ("Wave Strength", Float) = 0.1
        _WaveFrequency ("Wave Frequency", Float) = 1.0
        _WaveDirectionA ("Wave Direction A", Vector) = (1, 0, 0, 0)
        _WaveDirectionB ("Wave Direction B", Vector) = (0, 0, 1, 0)

        [Header(Normal Map)]
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1.0
        _NormalSpeed ("Normal Speed", Float) = 0.5
        _NormalScale ("Normal Scale", Float) = 1.0

        [Header(Reflection and Refraction)]
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.5
        _RefractionStrength ("Refraction Strength", Range(0, 0.5)) = 0.1
        _FresnelPower ("Fresnel Power", Range(0.5, 10)) = 5.0

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDistance ("Foam Distance", Float) = 0.5
        _FoamCutoff ("Foam Cutoff", Range(0, 1)) = 0.5
        _FoamSpeed ("Foam Speed", Float) = 1.0
        _FoamScale ("Foam Scale", Float) = 10.0

        [Header(Specular)]
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.9
    }

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
            Name "WaterURP"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

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
                float4 screenPos : TEXCOORD5;
                float3 viewDir : TEXCOORD6;
                float fogFactor : TEXCOORD7;
            };

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _DepthMaxDistance;

                float _WaveSpeed;
                float _WaveStrength;
                float _WaveFrequency;
                float4 _WaveDirectionA;
                float4 _WaveDirectionB;

                float4 _NormalMap_ST;
                float _NormalStrength;
                float _NormalSpeed;
                float _NormalScale;

                float _ReflectionStrength;
                float _RefractionStrength;
                float _FresnelPower;

                float4 _FoamColor;
                float _FoamDistance;
                float _FoamCutoff;
                float _FoamSpeed;
                float _FoamScale;

                float4 _SpecularColor;
                float _Smoothness;
            CBUFFER_END

            // Gerstner wave function
            float3 GerstnerWave(float3 position, float2 direction, float steepness, float wavelength, float time)
            {
                float k = 2.0 * PI / wavelength;
                float c = sqrt(9.8 / k);
                float2 d = normalize(direction);
                float f = k * (dot(d, position.xz) - c * time);
                float a = steepness / k;

                return float3(
                    d.x * a * cos(f),
                    a * sin(f),
                    d.y * a * cos(f)
                );
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 posOS = IN.positionOS.xyz;
                float time = _Time.y * _WaveSpeed;

                // Apply Gerstner waves
                float3 wave1 = GerstnerWave(posOS, _WaveDirectionA.xy, _WaveStrength, _WaveFrequency * 10.0, time);
                float3 wave2 = GerstnerWave(posOS, _WaveDirectionB.xy, _WaveStrength * 0.5, _WaveFrequency * 7.0, time * 1.3);
                float3 wave3 = GerstnerWave(posOS, float2(0.7, 0.7), _WaveStrength * 0.3, _WaveFrequency * 5.0, time * 0.8);

                posOS += wave1 + wave2 + wave3;

                OUT.worldPos = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                OUT.uv = IN.uv;
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);

                // Calculate wave normal
                float3 tangent = float3(1, 0, 0);
                float3 bitangent = float3(0, 0, 1);

                // Approximate normal from wave derivatives
                float delta = 0.1;
                float3 p1 = posOS + GerstnerWave(posOS + float3(delta, 0, 0), _WaveDirectionA.xy, _WaveStrength, _WaveFrequency * 10.0, time);
                float3 p2 = posOS + GerstnerWave(posOS + float3(0, 0, delta), _WaveDirectionA.xy, _WaveStrength, _WaveFrequency * 10.0, time);

                tangent = normalize(p1 - posOS);
                bitangent = normalize(p2 - posOS);
                float3 waveNormal = normalize(cross(bitangent, tangent));

                OUT.worldNormal = TransformObjectToWorldNormal(waveNormal);
                OUT.worldTangent = TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.worldBitangent = cross(OUT.worldNormal, OUT.worldTangent) * IN.tangentOS.w;

                OUT.viewDir = GetWorldSpaceViewDir(OUT.worldPos);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);

                return OUT;
            }

            // Simple noise for foam
            float SimpleNoise(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y;

                // Sample normal map (dual layer for movement)
                float2 normalUV1 = IN.worldPos.xz * _NormalScale + time * _NormalSpeed * float2(0.5, 0.3);
                float2 normalUV2 = IN.worldPos.xz * _NormalScale * 0.8 - time * _NormalSpeed * float2(0.3, 0.5);

                float3 normalTex1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUV1), _NormalStrength);
                float3 normalTex2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUV2), _NormalStrength);
                float3 normalTS = normalize(normalTex1 + normalTex2);

                // Transform normal to world space
                float3x3 TBN = float3x3(IN.worldTangent, IN.worldBitangent, IN.worldNormal);
                float3 worldNormal = normalize(mul(normalTS, TBN));

                // Screen UV
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // Depth-based color
                float depth = SampleSceneDepth(screenUV);
                float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                float surfaceDepth = LinearEyeDepth(IN.positionCS.z, _ZBufferParams);
                float depthDifference = linearDepth - surfaceDepth;
                float depthFactor = saturate(depthDifference / _DepthMaxDistance);

                float4 waterColor = lerp(_ShallowColor, _DeepColor, depthFactor);

                // Refraction
                float2 refractionOffset = worldNormal.xz * _RefractionStrength;
                float2 refractionUV = screenUV + refractionOffset;
                float3 refractionColor = SampleSceneColor(refractionUV);

                // Fresnel
                float3 viewDir = normalize(IN.viewDir);
                float fresnel = pow(1.0 - saturate(dot(viewDir, worldNormal)), _FresnelPower);

                // Reflection (simple sky reflection approximation)
                float3 reflectDir = reflect(-viewDir, worldNormal);
                float3 reflectionColor = lerp(float3(0.5, 0.7, 0.9), float3(0.8, 0.9, 1.0), reflectDir.y * 0.5 + 0.5);

                // Lighting
                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;

                // Specular (GGX approximation)
                float3 halfDir = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(worldNormal, halfDir));
                float specular = pow(NdotH, _Smoothness * 256.0) * _Smoothness;
                float3 specularColor = _SpecularColor.rgb * specular * mainLight.color;

                // Foam
                float foam = 0.0;
                if (depthDifference < _FoamDistance)
                {
                    float foamNoise = SimpleNoise(IN.worldPos.xz * _FoamScale + time * _FoamSpeed);
                    float foamFactor = 1.0 - saturate(depthDifference / _FoamDistance);
                    foam = step(_FoamCutoff, foamNoise * foamFactor);
                }

                // Combine
                float3 finalColor = lerp(refractionColor, waterColor.rgb, waterColor.a);
                finalColor = lerp(finalColor, reflectionColor, fresnel * _ReflectionStrength);
                finalColor += specularColor;
                finalColor = lerp(finalColor, _FoamColor.rgb, foam);

                // Apply fog
                finalColor = MixFog(finalColor, IN.fogFactor);

                float alpha = saturate(waterColor.a + fresnel * 0.3 + foam);

                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }

    // Built-in Render Pipeline fallback
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "WaterBuiltIn"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

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
                float3 viewDir : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            sampler2D _NormalMap;
            float4 _NormalMap_ST;

            float4 _ShallowColor;
            float4 _DeepColor;
            float _WaveSpeed;
            float _WaveStrength;
            float _WaveFrequency;
            float4 _WaveDirectionA;
            float4 _WaveDirectionB;
            float _NormalStrength;
            float _NormalSpeed;
            float _NormalScale;
            float _FresnelPower;
            float4 _SpecularColor;
            float _Smoothness;
            float4 _FoamColor;

            // Gerstner wave
            float3 GerstnerWave(float3 position, float2 direction, float steepness, float wavelength, float time)
            {
                float k = 2.0 * UNITY_PI / wavelength;
                float c = sqrt(9.8 / k);
                float2 d = normalize(direction);
                float f = k * (dot(d, position.xz) - c * time);
                float a = steepness / k;

                return float3(
                    d.x * a * cos(f),
                    a * sin(f),
                    d.y * a * cos(f)
                );
            }

            v2f vert(appdata v)
            {
                v2f o;

                float3 posOS = v.vertex.xyz;
                float time = _Time.y * _WaveSpeed;

                // Apply waves
                float3 wave1 = GerstnerWave(posOS, _WaveDirectionA.xy, _WaveStrength, _WaveFrequency * 10.0, time);
                float3 wave2 = GerstnerWave(posOS, _WaveDirectionB.xy, _WaveStrength * 0.5, _WaveFrequency * 7.0, time * 1.3);
                posOS += wave1 + wave2;

                o.worldPos = mul(unity_ObjectToWorld, float4(posOS, 1.0)).xyz;
                o.pos = UnityObjectToClipPos(float4(posOS, 1.0));
                o.uv = v.uv;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldPos);

                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = _Time.y;

                // Normal map
                float2 normalUV1 = i.worldPos.xz * _NormalScale + time * _NormalSpeed * float2(0.5, 0.3);
                float2 normalUV2 = i.worldPos.xz * _NormalScale * 0.8 - time * _NormalSpeed * float2(0.3, 0.5);

                float3 normal1 = UnpackNormal(tex2D(_NormalMap, normalUV1));
                float3 normal2 = UnpackNormal(tex2D(_NormalMap, normalUV2));
                float3 worldNormal = normalize(i.worldNormal + (normal1 + normal2) * _NormalStrength);

                // Fresnel
                float fresnel = pow(1.0 - saturate(dot(i.viewDir, worldNormal)), _FresnelPower);

                // Base color
                float4 waterColor = lerp(_ShallowColor, _DeepColor, fresnel);

                // Specular
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfDir = normalize(lightDir + i.viewDir);
                float spec = pow(saturate(dot(worldNormal, halfDir)), _Smoothness * 256.0);
                float3 specular = _SpecularColor.rgb * spec * _LightColor0.rgb;

                // Reflection approximation
                float3 reflectColor = lerp(float3(0.5, 0.7, 0.9), float3(0.8, 0.9, 1.0), worldNormal.y * 0.5 + 0.5);

                // Combine
                float3 finalColor = waterColor.rgb;
                finalColor = lerp(finalColor, reflectColor, fresnel * 0.5);
                finalColor += specular;

                UNITY_APPLY_FOG(i.fogCoord, finalColor);

                return float4(finalColor, waterColor.a);
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
