// DEPRECATED — the outline/emission/flash feature set moved into the shared core
// (SpecularLitCore.hlsl) and ships in Submachina/2D/Mesh2DLitSpecular, which also
// brings the full specular/normal/Form-Shape stack to creature meshes. All creature
// materials have been flipped to that shader and CreatureBuilder targets it; this
// file is kept only as a lightweight fallback if creature GPU cost ever becomes a
// problem. NOTE: it still reads TEXCOORD1.z as WORLD units, which predates the
// unified edge contract (z = normalized, w = world) — the builders now bake the
// new contract, so this shader's outline would need the one-line switch to .w
// before being revived.
//
// Procedural creature shader — flat-color / textured fill with a crisp world-space
// outline, HDR emission with a rim boost, and a hit/chromatophore flash channel.
//
// Consumes the UV1 edge data baked by ChainStripRenderer / RadialMeshRenderer
// (xy = outward dir, z = world-space distance to the silhouette edge). The outline
// is therefore constant-width in world units regardless of body taper — the same
// trick SplineFillLitSpecular uses for its edge band, minus the specular stack.
//
// Typical setups:
//   Plain + outline:   _Color tinted, white _MainTex, dark _OutlineColor.
//   Textured body:     assign _MainTex (maps head→tail on strips, planar on blobs).
//   Jellyfish glow:    translucent _Color, _EmissionColor > 0, _RimEmission ~2.
//   Squid flash:       creature code drives _FlashAmount / _EmissionColor via MPB.
Shader "Submachina/2D/ProcCreature"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Color("Fill Color", Color) = (1, 1, 1, 1)

        [Header(Outline)]
        _OutlineColor("Outline Color (A = strength)", Color) = (0, 0, 0, 1)
        _OutlineWidth("Outline Width (world units)", Float) = 0.05
        _OutlineSoftness("Outline Softness", Range(0, 1)) = 0.25

        [Header(Emission)]
        [HDR] _EmissionColor("Emission (HDR)", Color) = (0, 0, 0, 1)
        _RimEmission("Rim Emission Boost", Float) = 0
        _RimWidth("Rim Width (world units)", Float) = 0.15

        [Header(Flash)]
        _FlashColor("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount("Flash Amount", Range(0, 1)) = 0

        [HideInInspector] _ZWrite("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        // ------------------------------------------------------------------
        // 2D Renderer lit pass — creature color combined with the 2D light buffers.
        // ------------------------------------------------------------------
        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4  color    : COLOR;
                float4 edgeData : TEXCOORD1; // xy = outward dir, z = world-space edge distance
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4  color : COLOR;
                float4 edge  : TEXCOORD5;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as the SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _OutlineSoftness;
                half4 _EmissionColor;
                half _RimEmission;
                half _RimWidth;
                half4 _FlashColor;
                half _FlashAmount;
            CBUFFER_END

            Varyings LitVertex(Attributes input)
            {
                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color;
                o.edge = input.edgeData;
                o.uv = o.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                // Base = textured, vertex-tinted color combined with the 2D light buffers.
                half4 c = CommonLitFragment(input, input.color);

                // Outline: solid band where the edge distance is inside _OutlineWidth,
                // smoothed over _OutlineSoftness × width so thin tapers don't shimmer.
                half d = (half)input.edge.z;
                half soft = max(_OutlineWidth * _OutlineSoftness, 1e-4h);
                half outline = (1.0h - smoothstep(_OutlineWidth - soft, _OutlineWidth + soft, d)) * _OutlineColor.a;
                c.rgb = lerp(c.rgb, _OutlineColor.rgb, outline);

                // Emission: flat body glow plus an extra rim-concentrated boost —
                // e.g. _RimEmission 2 with a cyan _EmissionColor gives a jellyfish
                // whose edge burns ~3× brighter than its core.
                half rim = pow(saturate(1.0h - d / max(_RimWidth, 1e-3h)), 2.0h) * _RimEmission;
                c.rgb += _EmissionColor.rgb * (1.0h + rim) * c.a;

                // Flash channel (hit flash / chromatophore flicker) — overrides toward
                // a solid color while preserving the silhouette's alpha.
                c.rgb = lerp(c.rgb, _FlashColor.rgb * c.a, _FlashAmount);
                return c;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Flat normals into the 2D light buffer so Light2D normal shading stays neutral.
        // ------------------------------------------------------------------
        Pass
        {
            Tags { "LightMode" = "NormalsRendering" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsVertex
            #pragma fragment NormalsFragment

            #pragma multi_compile_instancing

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color : COLOR;
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as the SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _OutlineSoftness;
                half4 _EmissionColor;
                half _RimEmission;
                half _RimWidth;
                half4 _FlashColor;
                half _FlashAmount;
            CBUFFER_END

            Varyings NormalsVertex(Attributes input)
            {
                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color;
                o.uv = o.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            half4 NormalsFragment(Varyings input) : SV_Target
            {
                const half4 mainTex = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                const half3 normalTS = half3(0.0h, 0.0h, 1.0h);
                return NormalsRenderingShared(mainTex, normalTS, input.tangentWS.xyz, input.bitangentWS.xyz, input.normalWS.xyz);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Forward fallback (3D renderer / previews) — same look without 2D lights.
        // ------------------------------------------------------------------
        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue" = "Transparent" "RenderType" = "Transparent" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4  color    : COLOR;
                float4 edgeData : TEXCOORD1;
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4  color : COLOR;
                float4 edge  : TEXCOORD5;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY

            // NOTE: Do not ifdef the properties here as the SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _OutlineSoftness;
                half4 _EmissionColor;
                half _RimEmission;
                half _RimWidth;
                half4 _FlashColor;
                half _FlashAmount;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color;
                o.edge = input.edgeData;
                o.uv = o.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 c = CommonUnlitFragment(input, input.color);

                // Same outline/emission/flash stack as the lit pass, minus lighting.
                half d = (half)input.edge.z;
                half soft = max(_OutlineWidth * _OutlineSoftness, 1e-4h);
                half outline = (1.0h - smoothstep(_OutlineWidth - soft, _OutlineWidth + soft, d)) * _OutlineColor.a;
                c.rgb = lerp(c.rgb, _OutlineColor.rgb, outline);

                half rim = pow(saturate(1.0h - d / max(_RimWidth, 1e-3h)), 2.0h) * _RimEmission;
                c.rgb += _EmissionColor.rgb * (1.0h + rim) * c.a;

                c.rgb = lerp(c.rgb, _FlashColor.rgb * c.a, _FlashAmount);
                return c;
            }
            ENDHLSL
        }
    }
}
