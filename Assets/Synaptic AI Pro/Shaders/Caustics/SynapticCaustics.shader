Shader "Synaptic/Caustics"
{
    Properties
    {
        [Header(Caustics Settings)]
        _CausticsTex ("Caustics Texture", 2D) = "white" {}
        _CausticsColor ("Caustics Color", Color) = (1, 1, 1, 1)
        _CausticsIntensity ("Intensity", Range(0, 3)) = 1.0

        [Header(Animation)]
        _Speed1 ("Layer 1 Speed", Vector) = (0.1, 0.1, 0, 0)
        _Speed2 ("Layer 2 Speed", Vector) = (-0.07, 0.05, 0, 0)
        _Scale1 ("Layer 1 Scale", Float) = 1.0
        _Scale2 ("Layer 2 Scale", Float) = 1.2

        [Header(Depth Fade)]
        _DepthFadeStart ("Depth Fade Start", Float) = 0.0
        _DepthFadeEnd ("Depth Fade End", Float) = 10.0

        [Header(Projection)]
        _ProjectionHeight ("Projection Height", Float) = 5.0
    }

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CausticsURP"

            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            TEXTURE2D(_CausticsTex);
            SAMPLER(sampler_CausticsTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _CausticsTex_ST;
                float4 _CausticsColor;
                float _CausticsIntensity;
                float4 _Speed1;
                float4 _Speed2;
                float _Scale1;
                float _Scale2;
                float _DepthFadeStart;
                float _DepthFadeEnd;
                float _ProjectionHeight;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // World position based UV for seamless projection
                float2 worldUV = IN.worldPos.xz;

                // Animated dual-layer caustics
                float time = _Time.y;
                float2 uv1 = worldUV * _Scale1 + time * _Speed1.xy;
                float2 uv2 = worldUV * _Scale2 + time * _Speed2.xy;

                // Sample both layers
                float caustics1 = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, uv1).r;
                float caustics2 = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, uv2).r;

                // Combine with min for more realistic caustics pattern
                float caustics = min(caustics1, caustics2);
                caustics = pow(caustics, 1.5) * 2.0; // Contrast boost

                // Depth-based fade (if depth texture available)
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float depth = SampleSceneDepth(screenUV);
                float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                float depthFade = saturate((linearDepth - _DepthFadeStart) / (_DepthFadeEnd - _DepthFadeStart));
                depthFade = 1.0 - depthFade;

                // Final color
                float3 color = _CausticsColor.rgb * caustics * _CausticsIntensity * depthFade;

                // Apply fog
                color = MixFog(color, IN.fogFactor);

                return float4(color, caustics * _CausticsColor.a * depthFade);
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
            "Queue" = "Transparent+100"
        }

        Pass
        {
            Name "CausticsBuiltIn"

            Blend One One
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            sampler2D _CausticsTex;
            float4 _CausticsTex_ST;
            float4 _CausticsColor;
            float _CausticsIntensity;
            float4 _Speed1;
            float4 _Speed2;
            float _Scale1;
            float _Scale2;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // World position based UV
                float2 worldUV = i.worldPos.xz;

                // Animated dual-layer
                float time = _Time.y;
                float2 uv1 = worldUV * _Scale1 + time * _Speed1.xy;
                float2 uv2 = worldUV * _Scale2 + time * _Speed2.xy;

                // Sample both layers
                float caustics1 = tex2D(_CausticsTex, uv1).r;
                float caustics2 = tex2D(_CausticsTex, uv2).r;

                // Combine
                float caustics = min(caustics1, caustics2);
                caustics = pow(caustics, 1.5) * 2.0;

                // Final color
                fixed3 color = _CausticsColor.rgb * caustics * _CausticsIntensity;

                UNITY_APPLY_FOG(i.fogCoord, color);

                return fixed4(color, caustics * _CausticsColor.a);
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
