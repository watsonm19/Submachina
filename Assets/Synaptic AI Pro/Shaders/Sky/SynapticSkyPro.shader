Shader "Synaptic/SkyPro"
{
    Properties
    {
        [Header(Atmosphere)]
        _AtmosphereColor ("Atmosphere Color", Color) = (0.4, 0.7, 1.0, 1)
        _HorizonColor ("Horizon Color", Color) = (0.8, 0.85, 0.9, 1)
        _GroundColor ("Ground Color", Color) = (0.3, 0.3, 0.35, 1)
        _AtmosphereThickness ("Atmosphere Thickness", Range(0, 5)) = 1
        _MieScattering ("Mie Scattering", Range(0, 1)) = 0.1
        _RayleighScattering ("Rayleigh Scattering", Range(0, 2)) = 1

        [Header(Sun)]
        _SunColor ("Sun Color", Color) = (1, 0.95, 0.8, 1)
        _SunSize ("Sun Size", Range(0.001, 0.1)) = 0.02
        _SunIntensity ("Sun Intensity", Range(0, 10)) = 5
        _SunGlowSize ("Sun Glow Size", Range(0, 1)) = 0.3
        _SunGlowIntensity ("Sun Glow Intensity", Range(0, 5)) = 1

        [Header(Clouds Volumetric)]
        [Toggle(_CLOUDS_ON)] _CloudsEnabled ("Enable Clouds", Float) = 1
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.6, 0.65, 0.7, 1)
        _CloudDensity ("Cloud Density", Range(0, 2)) = 1
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.5
        _CloudHeight ("Cloud Height", Float) = 2000
        _CloudThickness ("Cloud Thickness", Float) = 500
        _CloudSpeed ("Cloud Speed", Float) = 0.01
        _CloudDirection ("Cloud Direction", Vector) = (1, 0, 0, 0)

        [Header(Cloud Detail)]
        _CloudScale ("Cloud Scale", Float) = 0.0001
        _CloudDetailScale ("Detail Scale", Float) = 0.0005
        _CloudDetailStrength ("Detail Strength", Range(0, 1)) = 0.5
        _CloudEdgeSoftness ("Edge Softness", Range(0, 1)) = 0.3

        [Header(Cloud Lighting)]
        _CloudAmbient ("Cloud Ambient", Range(0, 1)) = 0.3
        _CloudSunPenetration ("Sun Penetration", Range(0, 1)) = 0.3
        _CloudSilverLining ("Silver Lining", Range(0, 1)) = 0.5
        _CloudSilverLiningSpread ("Silver Spread", Range(0.5, 5)) = 2

        [Header(Stars)]
        [Toggle(_STARS_ON)] _StarsEnabled ("Enable Stars", Float) = 1
        _StarsDensity ("Stars Density", Range(0, 1)) = 0.5
        _StarsIntensity ("Stars Intensity", Range(0, 3)) = 1
        _StarsTwinkleSpeed ("Twinkle Speed", Float) = 2

        [Header(Night Sky)]
        _NightColor ("Night Sky Color", Color) = (0.02, 0.02, 0.05, 1)
        _NightTransition ("Night Transition", Range(0, 1)) = 0.3

        [Header(Performance)]
        _CloudSteps ("Cloud Ray Steps", Range(4, 64)) = 16
        _CloudLightSteps ("Light Ray Steps", Range(2, 16)) = 4
    }

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SkyPro"

            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _CLOUDS_ON
            #pragma shader_feature_local _STARS_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                // Atmosphere
                float4 _AtmosphereColor;
                float4 _HorizonColor;
                float4 _GroundColor;
                float _AtmosphereThickness;
                float _MieScattering;
                float _RayleighScattering;

                // Sun
                float4 _SunColor;
                float _SunSize;
                float _SunIntensity;
                float _SunGlowSize;
                float _SunGlowIntensity;

                // Clouds
                float4 _CloudColor;
                float4 _CloudShadowColor;
                float _CloudDensity;
                float _CloudCoverage;
                float _CloudHeight;
                float _CloudThickness;
                float _CloudSpeed;
                float4 _CloudDirection;
                float _CloudScale;
                float _CloudDetailScale;
                float _CloudDetailStrength;
                float _CloudEdgeSoftness;
                float _CloudAmbient;
                float _CloudSunPenetration;
                float _CloudSilverLining;
                float _CloudSilverLiningSpread;

                // Stars
                float _StarsDensity;
                float _StarsIntensity;
                float _StarsTwinkleSpeed;

                // Night
                float4 _NightColor;
                float _NightTransition;

                // Performance
                float _CloudSteps;
                float _CloudLightSteps;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDir : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            // Noise functions
            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                return lerp(
                    lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                         lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                         lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z
                );
            }

            float FBM(float3 p, int octaves)
            {
                float value = 0;
                float amplitude = 0.5;
                float frequency = 1;

                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * Noise3D(p * frequency);
                    frequency *= 2;
                    amplitude *= 0.5;
                }

                return value;
            }

            // Cloud density at a point
            float CloudDensity(float3 p)
            {
                float time = _Time.y * _CloudSpeed;
                float3 windOffset = _CloudDirection.xyz * time;

                // Main cloud shape
                float mainNoise = FBM((p + windOffset) * _CloudScale, 4);

                // Detail noise
                float detailNoise = FBM((p + windOffset * 2) * _CloudDetailScale, 3);
                mainNoise = lerp(mainNoise, mainNoise * detailNoise, _CloudDetailStrength);

                // Coverage threshold
                float density = mainNoise - (1 - _CloudCoverage);
                density = saturate(density / _CloudEdgeSoftness);

                return density * _CloudDensity;
            }

            // Ray-sphere intersection
            float2 RaySphereIntersect(float3 rayOrigin, float3 rayDir, float3 sphereCenter, float sphereRadius)
            {
                float3 oc = rayOrigin - sphereCenter;
                float b = dot(oc, rayDir);
                float c = dot(oc, oc) - sphereRadius * sphereRadius;
                float h = b * b - c;

                if (h < 0) return float2(-1, -1);

                h = sqrt(h);
                return float2(-b - h, -b + h);
            }

            // Rayleigh phase function
            float RayleighPhase(float cosTheta)
            {
                return 0.75 * (1.0 + cosTheta * cosTheta);
            }

            // Mie phase function (Henyey-Greenstein)
            float MiePhase(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1 - g2) / (4 * PI * pow(1 + g2 - 2 * g * cosTheta, 1.5));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.viewDir = normalize(TransformObjectToWorld(IN.positionOS.xyz));
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 viewDir = normalize(IN.viewDir);

                // Get sun direction
                Light mainLight = GetMainLight();
                float3 sunDir = mainLight.direction;
                float sunDot = dot(viewDir, sunDir);

                // Day/night factor based on sun height
                float sunHeight = sunDir.y;
                float dayFactor = saturate((sunHeight + _NightTransition) / (2 * _NightTransition));

                // ==================== ATMOSPHERE ====================

                // Vertical gradient
                float upDot = viewDir.y;
                float horizonFactor = 1 - abs(upDot);
                horizonFactor = pow(horizonFactor, 3);

                // Rayleigh scattering (blue sky)
                float rayleigh = RayleighPhase(sunDot) * _RayleighScattering;

                // Mie scattering (sun glow)
                float mie = MiePhase(sunDot, 0.76) * _MieScattering;

                // Sky color
                float3 skyColor = _AtmosphereColor.rgb * _AtmosphereThickness;
                skyColor = lerp(skyColor, _HorizonColor.rgb, horizonFactor);

                // Apply scattering
                float3 scatterColor = skyColor * rayleigh + _SunColor.rgb * mie;

                // Ground color (below horizon)
                float groundMask = saturate(-upDot * 10);
                scatterColor = lerp(scatterColor, _GroundColor.rgb, groundMask);

                // ==================== SUN ====================

                float sunMask = saturate((sunDot - (1 - _SunSize)) / _SunSize);
                sunMask = pow(sunMask, 0.5);

                // Sun glow
                float sunGlow = pow(saturate(sunDot), 8 / _SunGlowSize) * _SunGlowIntensity;

                float3 sunColor = _SunColor.rgb * _SunIntensity * (sunMask + sunGlow);

                // ==================== STARS ====================

                float3 starsColor = float3(0, 0, 0);
                #if defined(_STARS_ON)
                    if (dayFactor < 0.8)
                    {
                        // Star field
                        float3 starDir = viewDir * 1000;
                        float starNoise = Hash(floor(starDir));

                        if (starNoise > 1 - _StarsDensity * 0.01)
                        {
                            float twinkle = sin(_Time.y * _StarsTwinkleSpeed + starNoise * 100) * 0.3 + 0.7;
                            float starBrightness = pow(starNoise, 20) * _StarsIntensity * twinkle;
                            starsColor = float3(1, 1, 1) * starBrightness * (1 - dayFactor);
                        }
                    }
                #endif

                // ==================== CLOUDS ====================

                float3 cloudColor = float3(0, 0, 0);
                float cloudAlpha = 0;

                #if defined(_CLOUDS_ON)
                    // Ray march through cloud layer
                    float3 rayOrigin = float3(0, 0, 0);
                    float3 rayDir = viewDir;

                    // Cloud layer bounds
                    float cloudBottom = _CloudHeight;
                    float cloudTop = _CloudHeight + _CloudThickness;

                    // Calculate entry/exit points
                    float2 tBottom = RaySphereIntersect(rayOrigin, rayDir, float3(0, -6371000, 0), 6371000 + cloudBottom);
                    float2 tTop = RaySphereIntersect(rayOrigin, rayDir, float3(0, -6371000, 0), 6371000 + cloudTop);

                    float tStart = max(tBottom.x, 0);
                    float tEnd = tTop.x > 0 ? tTop.x : tTop.y;

                    if (tEnd > tStart && viewDir.y > -0.1)
                    {
                        float stepSize = (tEnd - tStart) / _CloudSteps;
                        float transmittance = 1;
                        float3 lightEnergy = float3(0, 0, 0);

                        for (int i = 0; i < (int)_CloudSteps; i++)
                        {
                            float t = tStart + (i + 0.5) * stepSize;
                            float3 samplePos = rayOrigin + rayDir * t;

                            float density = CloudDensity(samplePos);

                            if (density > 0.001)
                            {
                                // Light march toward sun
                                float lightTransmittance = 1;
                                float lightStepSize = _CloudThickness / _CloudLightSteps;

                                for (int j = 0; j < (int)_CloudLightSteps; j++)
                                {
                                    float3 lightSamplePos = samplePos + sunDir * (j + 0.5) * lightStepSize;
                                    float lightDensity = CloudDensity(lightSamplePos);
                                    lightTransmittance *= exp(-lightDensity * lightStepSize * 0.01);
                                }

                                // Beer's law
                                float extinctionCoeff = density * stepSize * 0.01;
                                float sampleTransmittance = exp(-extinctionCoeff);

                                // Lighting
                                float3 ambient = _CloudColor.rgb * _CloudAmbient;
                                float3 directLight = _SunColor.rgb * lightTransmittance * _CloudSunPenetration;

                                // Silver lining (forward scattering)
                                float silverLining = pow(saturate(sunDot), _CloudSilverLiningSpread) * _CloudSilverLining;
                                directLight += _SunColor.rgb * silverLining * lightTransmittance;

                                // Shadow color blend
                                float3 cloudSample = lerp(_CloudShadowColor.rgb, _CloudColor.rgb, lightTransmittance);
                                cloudSample *= (ambient + directLight);

                                lightEnergy += cloudSample * transmittance * (1 - sampleTransmittance);
                                transmittance *= sampleTransmittance;

                                if (transmittance < 0.01) break;
                            }
                        }

                        cloudColor = lightEnergy;
                        cloudAlpha = 1 - transmittance;
                    }
                #endif

                // ==================== COMBINE ====================

                // Night sky
                float3 nightSky = lerp(_NightColor.rgb, scatterColor, dayFactor);
                nightSky += starsColor;

                // Add sun
                float3 finalColor = nightSky + sunColor * dayFactor;

                // Blend clouds
                finalColor = lerp(finalColor, cloudColor, cloudAlpha);

                // Tone mapping (simple Reinhard)
                finalColor = finalColor / (finalColor + 1);

                return float4(finalColor, 1);
            }
            ENDHLSL
        }
    }

    // ==================== Built-in Pipeline SubShader ====================
    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "SkyProBuiltIn"
            Cull Off
            ZWrite Off

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _CLOUDS_ON
            #pragma shader_feature_local _STARS_ON

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            float4 _AtmosphereColor;
            float4 _HorizonColor;
            float4 _GroundColor;
            float _AtmosphereThickness;
            float4 _SunColor;
            float _SunSize;
            float _SunIntensity;
            float _SunGlowSize;
            float _SunGlowIntensity;
            float4 _NightColor;
            float _NightTransition;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.viewDir = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 viewDir = normalize(i.viewDir);
                float3 sunDir = normalize(_WorldSpaceLightPos0.xyz);
                float sunDot = dot(viewDir, sunDir);
                float sunHeight = sunDir.y;
                float dayFactor = saturate((sunHeight + _NightTransition) / (2 * _NightTransition));

                float upDot = viewDir.y;
                float horizonFactor = 1 - abs(upDot);
                horizonFactor = pow(horizonFactor, 3);

                float3 skyColor = _AtmosphereColor.rgb * _AtmosphereThickness;
                skyColor = lerp(skyColor, _HorizonColor.rgb, horizonFactor);

                float groundMask = saturate(-upDot * 10);
                skyColor = lerp(skyColor, _GroundColor.rgb, groundMask);

                float sunMask = saturate((sunDot - (1 - _SunSize)) / _SunSize);
                sunMask = pow(sunMask, 0.5);
                float sunGlow = pow(saturate(sunDot), 8 / _SunGlowSize) * _SunGlowIntensity;
                float3 sunColor = _SunColor.rgb * _SunIntensity * (sunMask + sunGlow);

                float3 nightSky = lerp(_NightColor.rgb, skyColor, dayFactor);
                float3 finalColor = nightSky + sunColor * dayFactor;
                finalColor = finalColor / (finalColor + 1);

                return float4(finalColor, 1);
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
            "RenderType" = "Background"
            "Queue" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "HDRenderPipeline"
        }

        Pass
        {
            Name "SkyProHDRP"
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _AtmosphereColor;
                float4 _HorizonColor;
                float4 _GroundColor;
                float _AtmosphereThickness;
                float4 _SunColor;
                float _SunSize;
                float _SunIntensity;
                float _SunGlowSize;
                float _SunGlowIntensity;
                float4 _NightColor;
                float _NightTransition;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 viewDir : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.viewDir = normalize(TransformObjectToWorld(IN.positionOS.xyz));
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 viewDir = normalize(IN.viewDir);
                float3 sunDir = float3(0.3, 0.8, 0.5);
                float sunDot = dot(viewDir, sunDir);
                float dayFactor = saturate((sunDir.y + _NightTransition) / (2 * _NightTransition));

                float upDot = viewDir.y;
                float horizonFactor = pow(1 - abs(upDot), 3);

                float3 skyColor = lerp(_AtmosphereColor.rgb * _AtmosphereThickness, _HorizonColor.rgb, horizonFactor);
                skyColor = lerp(skyColor, _GroundColor.rgb, saturate(-upDot * 10));

                float sunMask = pow(saturate((sunDot - (1 - _SunSize)) / _SunSize), 0.5);
                float sunGlow = pow(saturate(sunDot), 8 / _SunGlowSize) * _SunGlowIntensity;
                float3 sunColor = _SunColor.rgb * _SunIntensity * (sunMask + sunGlow);

                float3 finalColor = lerp(_NightColor.rgb, skyColor, dayFactor) + sunColor * dayFactor;
                return float4(finalColor / (finalColor + 1), 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
