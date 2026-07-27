// URP 2D-lit shader for GENERATED SPLINE-FILL MESHES (see Core.Rendering.SplineFillMeshBuilder).
//
// This is the MeshRenderer sibling of Submachina/2D/SpriteLitSpecular: the same
// normal-mapped 2D diffuse lighting + the full specular glint pipeline (shared via
// SpecularLitCore.hlsl), but with the fill textures as FIRST-CLASS material slots —
// the mesh carries tiling UVs (local or world space, baked by the builder), so the
// seamless albedo/normal/specmask trio is just assigned on the material and repeats
// across the shape. No sprite Secondary Textures, no fill/edge submesh split.
//
// On top of that, the mesh bakes an EDGE BAND into TEXCOORD1:
//   xy = outward direction at the nearest outline point (object space, unit)
//   z  = edge distance: 0 at the outline, 1 at the inner edge of the band (and interior)
// which drives three composable edge effects:
//   - Edge Darken : multiply toward _EdgeColor near the rim (Photoshop "inner glow,
//                   black, multiply" look). Applied BEFORE the specular add, so bevel
//                   glints still fire on the darkened rim — reads as shadowed 3D form.
//   - Edge Bevel  : bends the surface normal outward near the rim, for BOTH the 2D
//                   light buffer (NormalsRendering pass) and the specular — the edge
//                   genuinely rounds under Light2Ds and SpecularLight2Ds.
//   - Alpha Fade  : fades the rim toward transparent so the shape melts into the
//                   background. Applied last, over everything.
//
// Differences from the sprite shader: no sprite instance properties / flip / skinning
// (MeshRenderer has none of those); vertex color × _Color is the tint path instead of
// unity_SpriteColor. All three passes share an identical UnityPerMaterial CBUFFER
// layout or the SRP Batcher breaks — the spec + edge properties are declared in every pass.
Shader "Submachina/2D/SplineFillLitSpecular"
{
    Properties
    {
        _MainTex("Fill Albedo (tiling)", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        // The normal map and spec mask each have their OWN Tiling/Offset (the inspector
        // fields under their slots), applied RELATIVE to the fill UV — the default (1,0)
        // follows the fill exactly, so only touch them to de-sync one map (e.g. place a
        // reused spec-mask graphic). Pair with the Stamp Once toggles below to window a
        // non-tiling stamp (one glowy spot) instead of letting it repeat.
        _NormalMap("Fill Normal Map (tiling, straight RGB)", 2D) = "bump" {}
        // Tiling specular gate (RGB tints + scales all specular). White default leaves
        // the fill fully responsive to _SpecColor.
        _SpecMask("Fill Specular Mask (tiling, RGB)", 2D) = "white" {}
        // Stamp Once: sample the map only inside its single 0..1 UV window — outside it
        // the normal map falls back to FLAT and the spec mask to WHITE (neutral), so a
        // decal-sized graphic lands once at a chosen spot instead of tiling across.
        [MaterialToggle] _NormalMapOnce("Normal Map Stamp Once (no tiling)", Float) = 0
        [MaterialToggle] _SpecMaskOnce("Spec Mask Stamp Once (no tiling)", Float) = 0
        // What the spec mask reads as OUTSIDE the stamp window — match the stamp graphic's
        // border so the seam disappears: white = neutral/full spec elsewhere, black = dull
        // everywhere except the stamp (the classic one-glowy-spot setup), grey = dimmed.
        _SpecMaskOnceBg("Spec Mask Stamp Background", Color) = (1, 1, 1, 1)
        _Color("Tint", Color) = (1,1,1,1)
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        [Header(Edge Band)]
        // The band's WIDTH in world units is baked into the mesh by SplineFillMeshBuilder;
        // these shape what happens across it (d = 0 at the outline .. 1 at the band's inner edge).
        _EdgeColor("Edge Shade Color", Color) = (0, 0, 0, 1)
        _EdgeDarken("Edge Darken (multiply inner glow)", Range(0, 1)) = 0.8
        // <=1 shapes the gradient WITHIN the mesh's baked band; >1 spills past it as a
        // flat(ish) darkening plateau over the interior (there's no distance data beyond
        // the band — for a genuinely deeper gradient raise the builder's Edge Band Width).
        _EdgeWidth("Edge Effect Width (fraction of band)", Range(0.01, 8)) = 1
        _EdgeFalloff("Edge Falloff Exponent", Range(0.25, 8)) = 2
        _EdgeAlphaFade("Edge Alpha Fade (melt into background)", Range(0, 1)) = 0
        _EdgeBevel("Edge Bevel (normal rounding)", Range(0, 4)) = 0

        [Header(Metallic Specular)]
        [HDR] _SpecColor("Specular Color (HDR)", Color) = (1.3, 1.4, 1.6, 1)
        _SpecPower("Specular Tightness", Range(1, 200)) = 64
        _SpecIntensity("Resting Specular Intensity", Float) = 0
        // Baseline glint direction in tangent/UV space (z = toward viewer). Only used for
        // the resting/shimmer/boost glint when no light is reacting.
        _SpecLightDir("Baseline Glint Dir", Vector) = (-0.45, 0.6, 0.66, 0)
        _LightResponse("Light Response", Float) = 1
        _SpecBoost("Specular Boost (mining/pulse)", Float) = 0
        _SpecReplace("Specular Replace (0 add..1 replace)", Range(0, 1)) = 0
        _SpecClamp("Specular Clamp (0 = off)", Float) = 0
        _SpecAlbedoTint("Specular Albedo Tint", Range(0, 1)) = 0
        _SpecScreen("Specular Screen Blend", Range(0, 1)) = 0
        _SpecViewBias("Specular View Bias (omnidirectionality)", Range(0, 10)) = 0.66

        [Header(Glow Zone)]
        // Threshold-gated glow regions driven by the spec mask's brightness — see
        // SpriteLitSpecular for the full story. Threshold > 1 (default) = off.
        _GlowThreshold("Glow Mask Threshold (>1 = off)", Range(0, 2)) = 2
        _GlowKnee("Glow Threshold Knee (soft edge)", Range(0.001, 0.5)) = 0.15
        _GlowViewBias("Glow View Bias (omnidirectionality)", Range(0, 10)) = 4
        _GlowPower("Glow Tightness", Range(1, 200)) = 8
        _GlowGain("Glow Gain (HDR multiplier)", Float) = 2

        [Header(Animation)]
        _ShimmerAmp("Intensity Mod Amplitude", Float) = 0
        _ShimmerSpeed("Intensity Mod Speed", Float) = 1
        _ShimmerPhase("Intensity Mod Phase", Float) = 0
        _ShimmerWave("Intensity Mod Waveform", Float) = 0
        _ShimmerMode("Intensity Mod Mode", Float) = 0
        _DirWobble("Direction Wobble (amp, speed, wave, phase)", Vector) = (0, 1, 0, 0)

        [Header(Surface Normal)]
        // 0 = _NormalMap (the tiling fill map — the usual choice here); 1..5 = procedural
        // UV patterns (of limited use on tiling UVs); 6 = _NormalTex override;
        // 7 = World Facets, 8 = World Ripples (world-space, always seam-free).
        _NormalMode("Normal Mode (0=texture)", Float) = 0
        _NormalStrength("Normal Strength", Float) = 1
        _DiffNormalStrength("Diffuse Normal Strength", Float) = 1
        _NormalEmboss("Relief Emboss (fake additive relief)", Float) = 0
        _NormalFreq("Normal Frequency", Float) = 8
        // xy = UV of the sprite rect origin, zw = UV size — identity on meshes.
        _NormalUVRect("Normal UV Rect", Vector) = (0, 0, 1, 1)
        _NormalTex("Normal Override (straight RGB)", 2D) = "bump" {}
        _NormalTexST("Normal Override Tiling/Offset", Vector) = (1, 1, 0, 0)

        // Sorting layer bit (set per-instance by SpecularController). Default = all layers.
        _SortingLayerBit("Sorting Layer Bit", Float) = 16777215
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

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
                float4 edgeData : TEXCOORD1; // xy = outward dir, z = edge distance 0..1
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4  color    : COLOR;
                float3 worldPos : TEXCOORD4; // world position for real-light specular
                float4 edge     : TEXCOORD5; // xy = outward dir, z = edge distance 0..1
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;   // fill tiling/offset (material inspector or per-object override)
                float4 _NormalMap_ST; // normal-map tiling/offset RELATIVE to the fill UV ((1,0) = follow fill)
                float4 _SpecMask_ST;  // spec-mask tiling/offset RELATIVE to the fill UV ((1,0) = follow fill)
                half _NormalMapOnce;  // 1 = stamp once (flat normal outside the 0..1 window)
                half _SpecMaskOnce;   // 1 = stamp once (mask reads _SpecMaskOnceBg outside the window)
                half4 _SpecMaskOnceBg;
                half4 _EdgeColor;
                half _EdgeDarken;
                half _EdgeWidth;
                half _EdgeFalloff;
                half _EdgeAlphaFade;
                half _EdgeBevel;
                half4 _SpecColor;
                half4 _SpecLightDir;
                half _SpecPower;
                half _SpecIntensity;
                half _LightResponse;
                half _SpecBoost;
                half _SpecReplace;
                half _SpecClamp;
                half _SpecAlbedoTint;
                half _SpecScreen;
                half _SpecViewBias;
                half _GlowThreshold;
                half _GlowKnee;
                half _GlowViewBias;
                half _GlowPower;
                half _GlowGain;
                half _ShimmerAmp;
                half _ShimmerSpeed;
                half _ShimmerPhase;
                half _ShimmerWave;
                half _ShimmerMode;
                half4 _DirWobble;
                half _NormalMode;
                half _NormalStrength;
                half _DiffNormalStrength;
                half _NormalEmboss;
                half _NormalFreq;
                float4 _NormalUVRect;
                float4 _NormalTexST;
                float _SortingLayerBit;
            CBUFFER_END

            // Shared specular core (global lights, surface normals, ApplySpecular).
            #include "SpecularLitCore.hlsl"

            // Edge weight from the baked band distance: 1 at the outline fading to 0 at
            // _EdgeWidth × the band width, shaped by the falloff exponent. Also outputs the
            // rescaled 0..1 distance dd (for the alpha fade, which wants the inverse ramp).
            half EdgeWeight(float d, out half dd)
            {
                dd = (half)saturate(d / max(_EdgeWidth, 1e-4));
                return pow(saturate(1.0h - dd), _EdgeFalloff);
            }

            Varyings LitVertex(Attributes input)
            {
                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color;
                o.worldPos = TransformObjectToWorld(input.positionOS.xyz);
                o.edge = input.edgeData;
                // Fill tiling/offset on top of the mesh's baked planar UVs — drives ALL the
                // fill textures (albedo/normal/specmask sample the same interpolated uv).
                o.uv = o.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                // Base = the stock normal-mapped 2D diffuse lit color.
                half4 c = CommonLitFragment(input, input.color);

                // Edge band weight (1 at the outline → 0 inside).
                half dd;
                half w = EdgeWeight(input.edge.z, dd);

                // Per-map UVs, RELATIVE to the fill UV (input.uv already carries
                // _MainTex_ST): the (1,0) defaults follow the fill exactly; a per-object
                // override can rescale/slide one map to fit a reused graphic.
                float2 uvNrm  = input.uv * _NormalMap_ST.xy + _NormalMap_ST.zw;
                float2 uvSpec = input.uv * _SpecMask_ST.xy  + _SpecMask_ST.zw;

                // Surface normal + edge bevel: tilt the normal outward near the rim so the
                // edge rounds under the specular lights (the diffuse side of the same bevel
                // happens in the NormalsRendering pass). Stamp Once: outside the map's
                // single 0..1 window the surface is treated as flat.
                half3 nBase = ComputeSurfaceNormal(uvNrm, input.worldPos.xy);
                if (_NormalMapOnce > 0.5h && (any(uvNrm < 0.0) || any(uvNrm > 1.0)))
                    nBase = half3(0.0h, 0.0h, 1.0h);
                nBase = normalize(half3(nBase.xy + (half2)input.edge.xy * (_EdgeBevel * w), nBase.z));

                // Edge darken BEFORE the specular add (the Photoshop "inner glow, black,
                // multiply" look) — bevel glints still fire on the darkened rim, which is
                // what sells the rim as shadowed 3D form instead of a painted border.
                c.rgb *= lerp(half3(1.0h, 1.0h, 1.0h), _EdgeColor.rgb, w * _EdgeDarken);

                // Spec mask on its own UV; Stamp Once reads the configurable background
                // colour outside the window (match the stamp's border: white = neutral
                // spec elsewhere, black = dull rock everywhere except the glowy spot).
                half3 specMask = (half3)SAMPLE_TEXTURE2D(_SpecMask, sampler_SpecMask, uvSpec).rgb;
                if (_SpecMaskOnce > 0.5h && (any(uvSpec < 0.0) || any(uvSpec > 1.0)))
                    specMask = _SpecMaskOnceBg.rgb;

                // Full glint pipeline shared with the sprite shader.
                half3 texel = (half3)SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb * input.color.rgb;
                c = ApplySpecularMasked(c, nBase, texel, specMask, input.worldPos.xy);

                // Alpha fade LAST, over everything (lit base + glints), so the whole
                // surface melts toward the background at the rim.
                c.a *= lerp(1.0h, dd, _EdgeAlphaFade);
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            #pragma multi_compile_instancing

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color    : COLOR;
                float4 edgeData : TEXCOORD1; // xy = outward dir, z = edge distance 0..1
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4  color : COLOR;
                float4 edge  : TEXCOORD5; // xy = outward dir, z = edge distance 0..1
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START( UnityPerMaterial )
                half4 _Color;
                float4 _MainTex_ST;   // fill tiling/offset (material inspector or per-object override)
                float4 _NormalMap_ST; // normal-map tiling/offset RELATIVE to the fill UV ((1,0) = follow fill)
                float4 _SpecMask_ST;  // spec-mask tiling/offset RELATIVE to the fill UV ((1,0) = follow fill)
                half _NormalMapOnce;  // 1 = stamp once (flat normal outside the 0..1 window)
                half _SpecMaskOnce;   // 1 = stamp once (mask reads _SpecMaskOnceBg outside the window)
                half4 _SpecMaskOnceBg;
                half4 _EdgeColor;
                half _EdgeDarken;
                half _EdgeWidth;
                half _EdgeFalloff;
                half _EdgeAlphaFade;
                half _EdgeBevel;
                half4 _SpecColor;
                half4 _SpecLightDir;
                half _SpecPower;
                half _SpecIntensity;
                half _LightResponse;
                half _SpecBoost;
                half _SpecReplace;
                half _SpecClamp;
                half _SpecAlbedoTint;
                half _SpecScreen;
                half _SpecViewBias;
                half _GlowThreshold;
                half _GlowKnee;
                half _GlowViewBias;
                half _GlowPower;
                half _GlowGain;
                half _ShimmerAmp;
                half _ShimmerSpeed;
                half _ShimmerPhase;
                half _ShimmerWave;
                half _ShimmerMode;
                half4 _DirWobble;
                half _NormalMode;
                half _NormalStrength;
                half _DiffNormalStrength;
                half _NormalEmboss;
                half _NormalFreq;
                float4 _NormalUVRect;
                float4 _NormalTexST;
                float _SortingLayerBit;
            CBUFFER_END

            Varyings NormalsRenderingVertex(Attributes input)
            {
                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color;
                o.edge = input.edgeData;
                // Keep the light-buffer normals sampling in lockstep with the lit pass's tiling.
                o.uv = o.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                // Stock normals output with the relief deepened by _DiffNormalStrength, PLUS
                // the edge bevel: near the rim the normal is tilted outward before it lands
                // in the 2D light buffer, so every Light2D shades the edge as a rounded form.
                const half4 mainTex = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half dd = (half)saturate(input.edge.z / max(_EdgeWidth, 1e-4));
                half w = pow(saturate(1.0h - dd), _EdgeFalloff);
                // Same relative normal-map UV + stamp-once window as the lit pass, so the
                // diffuse light buffer and the specular read identical relief.
                float2 uvNrm = input.uv * _NormalMap_ST.xy + _NormalMap_ST.zw;
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvNrm));
                if (_NormalMapOnce > 0.5h && (any(uvNrm < 0.0) || any(uvNrm > 1.0)))
                    normalTS = half3(0.0h, 0.0h, 1.0h);
                normalTS = normalize(half3(normalTS.xy * _DiffNormalStrength + (half2)input.edge.xy * (_EdgeBevel * w), normalTS.z));
                return NormalsRenderingShared(mainTex, normalTS, input.tangentWS.xyz, input.bitangentWS.xyz, input.normalWS.xyz);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;   // fill tiling/offset (material inspector or per-object override)
                float4 _NormalMap_ST; // normal-map tiling/offset RELATIVE to the fill UV ((1,0) = follow fill)
                float4 _SpecMask_ST;  // spec-mask tiling/offset RELATIVE to the fill UV ((1,0) = follow fill)
                half _NormalMapOnce;  // 1 = stamp once (flat normal outside the 0..1 window)
                half _SpecMaskOnce;   // 1 = stamp once (mask reads _SpecMaskOnceBg outside the window)
                half4 _SpecMaskOnceBg;
                half4 _EdgeColor;
                half _EdgeDarken;
                half _EdgeWidth;
                half _EdgeFalloff;
                half _EdgeAlphaFade;
                half _EdgeBevel;
                half4 _SpecColor;
                half4 _SpecLightDir;
                half _SpecPower;
                half _SpecIntensity;
                half _LightResponse;
                half _SpecBoost;
                half _SpecReplace;
                half _SpecClamp;
                half _SpecAlbedoTint;
                half _SpecScreen;
                half _SpecViewBias;
                half _GlowThreshold;
                half _GlowKnee;
                half _GlowViewBias;
                half _GlowPower;
                half _GlowGain;
                half _ShimmerAmp;
                half _ShimmerSpeed;
                half _ShimmerPhase;
                half _ShimmerWave;
                half _ShimmerMode;
                half4 _DirWobble;
                half _NormalMode;
                half _NormalStrength;
                half _DiffNormalStrength;
                half _NormalEmboss;
                half _NormalFreq;
                float4 _NormalUVRect;
                float4 _NormalTexST;
                float _SortingLayerBit;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color;
                o.uv = o.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                return CommonUnlitFragment(input, input.color);
            }
            ENDHLSL
        }
    }
}
