Shader "Synaptic/ShieldPro"
{
    Properties
    {
        [Header(Shield Color)]
        _MainColor ("Main Color", Color) = (0.2, 0.5, 1.0, 0.3)
        _EdgeColor ("Edge Color", Color) = (0.5, 0.8, 1.0, 1.0)
        _HitColor ("Hit Color", Color) = (1.0, 0.5, 0.2, 1.0)
        _FresnelColor ("Fresnel Color", Color) = (0.3, 0.6, 1.0, 1.0)

        [Header(Pattern)]
        _PatternTex ("Pattern Texture", 2D) = "white" {}
        _PatternScale ("Pattern Scale", Float) = 1
        _PatternIntensity ("Pattern Intensity", Range(0, 2)) = 0.5
        _PatternScrollSpeed ("Pattern Scroll Speed", Vector) = (0.1, 0.1, 0, 0)

        [Header(Hex Pattern)]
        [Toggle(_HEX_PATTERN)] _HexPattern ("Hex Pattern", Float) = 1
        _HexScale ("Hex Scale", Float) = 10
        _HexEdgeWidth ("Hex Edge Width", Range(0.01, 0.5)) = 0.1
        _HexPulseSpeed ("Hex Pulse Speed", Float) = 2

        [Header(Fresnel)]
        _FresnelPower ("Fresnel Power", Range(0.5, 10)) = 3
        _FresnelIntensity ("Fresnel Intensity", Range(0, 5)) = 2

        [Header(Hit Effect)]
        [Toggle(_HIT_EFFECT)] _HitEffect ("Enable Hit Effect", Float) = 1
        _HitPosition ("Hit Position", Vector) = (0, 0, 0, 0)
        _HitRadius ("Hit Radius", Float) = 0.5
        _HitIntensity ("Hit Intensity", Range(0, 1)) = 0
        _HitRippleCount ("Ripple Count", Float) = 3
        _HitRippleSpeed ("Ripple Speed", Float) = 5

        [Header(Distortion)]
        [Toggle(_DISTORTION)] _Distortion ("Enable Distortion", Float) = 1
        _DistortionStrength ("Distortion Strength", Range(0, 0.1)) = 0.02
        _DistortionSpeed ("Distortion Speed", Float) = 1

        [Header(Animation)]
        _PulseSpeed ("Pulse Speed", Float) = 2
        _PulseIntensity ("Pulse Intensity", Range(0, 1)) = 0.2
        _WaveSpeed ("Wave Speed", Float) = 1
        _WaveFrequency ("Wave Frequency", Float) = 5
        _WaveAmplitude ("Wave Amplitude", Range(0, 0.1)) = 0.02

        [Header(Intersection)]
        [Toggle(_INTERSECTION)] _Intersection ("Intersection Glow", Float) = 1
        _IntersectionColor ("Intersection Color", Color) = (1, 1, 1, 1)
        _IntersectionWidth ("Intersection Width", Float) = 0.5
        _IntersectionIntensity ("Intersection Intensity", Range(0, 5)) = 2
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
            Name "ShieldForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _HEX_PATTERN
            #pragma shader_feature_local _HIT_EFFECT
            #pragma shader_feature_local _DISTORTION
            #pragma shader_feature_local _INTERSECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_PatternTex);
            SAMPLER(sampler_PatternTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainColor;
                float4 _EdgeColor;
                float4 _HitColor;
                float4 _FresnelColor;

                float4 _PatternTex_ST;
                float _PatternScale;
                float _PatternIntensity;
                float4 _PatternScrollSpeed;

                float _HexScale;
                float _HexEdgeWidth;
                float _HexPulseSpeed;

                float _FresnelPower;
                float _FresnelIntensity;

                float4 _HitPosition;
                float _HitRadius;
                float _HitIntensity;
                float _HitRippleCount;
                float _HitRippleSpeed;

                float _DistortionStrength;
                float _DistortionSpeed;

                float _PulseSpeed;
                float _PulseIntensity;
                float _WaveSpeed;
                float _WaveFrequency;
                float _WaveAmplitude;

                float4 _IntersectionColor;
                float _IntersectionWidth;
                float _IntersectionIntensity;
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
                float4 screenPos : TEXCOORD4;
                float3 objectPos : TEXCOORD5;
            };

            // Hexagonal pattern
            float HexPattern(float2 p)
            {
                const float2 s = float2(1, 1.7320508);
                p *= _HexScale;

                float4 hC = floor(float4(p, p - float2(0.5, 1)) / s.xyxy) + 0.5;
                float4 h = float4(p - hC.xy * s, p - (hC.zw + 0.5) * s);

                float2 nearest = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;

                float dist = length(nearest);
                float hexEdge = smoothstep(_HexEdgeWidth, _HexEdgeWidth + 0.02, abs(dist - 0.5));

                return 1 - hexEdge;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 posOS = IN.positionOS.xyz;
                OUT.objectPos = posOS;

                // Vertex wave animation
                float wave = sin(_Time.y * _WaveSpeed + posOS.y * _WaveFrequency) * _WaveAmplitude;
                posOS += IN.normalOS * wave;

                OUT.worldPos = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                OUT.uv = IN.uv;

                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDir = normalize(_WorldSpaceCameraPos - OUT.worldPos);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 normal = normalize(IN.worldNormal);
                float3 viewDir = normalize(IN.viewDir);
                float time = _Time.y;

                // Screen UV
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // Fresnel
                float fresnel = pow(1 - saturate(dot(viewDir, normal)), _FresnelPower) * _FresnelIntensity;

                // Base color with fresnel
                float4 color = lerp(_MainColor, _FresnelColor, fresnel);

                // Hex pattern
                #if defined(_HEX_PATTERN)
                    float hex = HexPattern(IN.uv);
                    float hexPulse = sin(time * _HexPulseSpeed) * 0.5 + 0.5;
                    color.rgb += _EdgeColor.rgb * hex * (0.5 + hexPulse * 0.5);
                    color.a += hex * 0.3;
                #endif

                // Pattern texture
                float2 patternUV = IN.uv * _PatternScale + time * _PatternScrollSpeed.xy;
                float pattern = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, patternUV).r;
                color.rgb += pattern * _PatternIntensity * _EdgeColor.rgb;

                // Hit effect
                #if defined(_HIT_EFFECT)
                    if (_HitIntensity > 0.001)
                    {
                        float3 hitDir = IN.worldPos - _HitPosition.xyz;
                        float hitDist = length(hitDir);

                        // Ripple waves
                        float ripple = 0;
                        for (int i = 0; i < 3; i++)
                        {
                            float ripplePhase = time * _HitRippleSpeed - i * 0.3;
                            float rippleRadius = frac(ripplePhase) * _HitRadius * 2;
                            float rippleWidth = 0.1;
                            ripple += (1 - abs(hitDist - rippleRadius) / rippleWidth) * saturate(1 - frac(ripplePhase));
                        }
                        ripple = saturate(ripple);

                        // Fade out over distance
                        float hitFade = 1 - saturate(hitDist / _HitRadius);

                        color.rgb = lerp(color.rgb, _HitColor.rgb, ripple * hitFade * _HitIntensity);
                        color.a += ripple * hitFade * _HitIntensity * 0.5;
                    }
                #endif

                // Intersection glow
                #if defined(_INTERSECTION)
                    float depth = SampleSceneDepth(screenUV);
                    float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                    float surfaceDepth = LinearEyeDepth(IN.positionCS.z, _ZBufferParams);
                    float depthDiff = abs(linearDepth - surfaceDepth);

                    if (depthDiff < _IntersectionWidth)
                    {
                        float intersectionFactor = 1 - saturate(depthDiff / _IntersectionWidth);
                        intersectionFactor = pow(intersectionFactor, 2);
                        color.rgb += _IntersectionColor.rgb * intersectionFactor * _IntersectionIntensity;
                        color.a += intersectionFactor * 0.5;
                    }
                #endif

                // Distortion
                #if defined(_DISTORTION)
                    float2 distortionOffset = normal.xy * _DistortionStrength;
                    distortionOffset *= sin(time * _DistortionSpeed + IN.uv.y * 10) * 0.5 + 0.5;
                    float3 distortedBG = SampleSceneColor(screenUV + distortionOffset);
                    color.rgb = lerp(distortedBG, color.rgb, color.a);
                #endif

                // Pulse animation
                float pulse = sin(time * _PulseSpeed) * _PulseIntensity;
                color.a += pulse;

                // Edge highlight
                color.rgb += _EdgeColor.rgb * fresnel * 0.5;

                // Clamp alpha
                color.a = saturate(color.a);

                return color;
            }
            ENDHLSL
        }
    }

    // ==================== Built-in Pipeline SubShader ====================
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "ShieldBuiltIn"
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _HEX_PATTERN

            #include "UnityCG.cginc"

            float4 _MainColor;
            float4 _EdgeColor;
            float4 _FresnelColor;
            float _FresnelPower;
            float _FresnelIntensity;
            float _HexScale;
            float _HexEdgeWidth;
            float _HexPulseSpeed;
            float _PulseSpeed;
            float _PulseIntensity;

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
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            float HexPattern(float2 p)
            {
                const float2 s = float2(1, 1.7320508);
                p *= _HexScale;
                float4 hC = floor(float4(p, p - float2(0.5, 1)) / s.xyxy) + 0.5;
                float4 h = float4(p - hC.xy * s, p - (hC.zw + 0.5) * s);
                float2 nearest = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
                float dist = length(nearest);
                return 1 - smoothstep(_HexEdgeWidth, _HexEdgeWidth + 0.02, abs(dist - 0.5));
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(i.viewDir);
                float time = _Time.y;

                float fresnel = pow(1 - saturate(dot(viewDir, normal)), _FresnelPower) * _FresnelIntensity;
                float4 color = lerp(_MainColor, _FresnelColor, fresnel);

                #if defined(_HEX_PATTERN)
                    float hex = HexPattern(i.uv);
                    float hexPulse = sin(time * _HexPulseSpeed) * 0.5 + 0.5;
                    color.rgb += _EdgeColor.rgb * hex * (0.5 + hexPulse * 0.5);
                    color.a += hex * 0.3;
                #endif

                float pulse = sin(time * _PulseSpeed) * _PulseIntensity;
                color.a += pulse;
                color.rgb += _EdgeColor.rgb * fresnel * 0.5;
                color.a = saturate(color.a);

                return color;
            }
            ENDCG
        }
    }

    // ==================== HDRP SubShader ====================
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.high-definition" }
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "ShieldHDRP"
            Tags { "LightMode" = "Forward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _HEX_PATTERN

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainColor;
                float4 _EdgeColor;
                float4 _FresnelColor;
                float _FresnelPower;
                float _FresnelIntensity;
                float _HexScale;
                float _HexEdgeWidth;
                float _HexPulseSpeed;
                float _PulseSpeed;
                float _PulseIntensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 worldNormal : TEXCOORD1; float3 viewDir : TEXCOORD2; };

            float HexPattern(float2 p, float scale, float edgeWidth)
            {
                const float2 s = float2(1, 1.7320508);
                p *= scale;
                float4 hC = floor(float4(p, p - float2(0.5, 1)) / s.xyxy) + 0.5;
                float4 h = float4(p - hC.xy * s, p - (hC.zw + 0.5) * s);
                float2 nearest = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
                return 1 - smoothstep(edgeWidth, edgeWidth + 0.02, abs(length(nearest) - 0.5));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv = IN.uv;
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDir = GetWorldSpaceNormalizeViewDir(worldPos);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 normal = normalize(IN.worldNormal);
                float3 viewDir = normalize(IN.viewDir);
                float fresnel = pow(1 - saturate(dot(viewDir, normal)), _FresnelPower) * _FresnelIntensity;
                float4 color = lerp(_MainColor, _FresnelColor, fresnel);

                #if defined(_HEX_PATTERN)
                    float hex = HexPattern(IN.uv, _HexScale, _HexEdgeWidth);
                    float hexPulse = sin(_Time.y * _HexPulseSpeed) * 0.5 + 0.5;
                    color.rgb += _EdgeColor.rgb * hex * (0.5 + hexPulse * 0.5);
                    color.a += hex * 0.3;
                #endif

                color.a += sin(_Time.y * _PulseSpeed) * _PulseIntensity;
                color.rgb += _EdgeColor.rgb * fresnel * 0.5;
                color.a = saturate(color.a);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
