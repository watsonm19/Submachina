// URP 2D-lit shader for GENERATED MESHES — spline fills (SplineFillMeshBuilder),
// procedural creature bodies (RadialMeshRenderer / ChainStripRenderer), and any
// other MeshRenderer that follows the TEXCOORD1 contract below. Formerly named
// SplineFillLitSpecular; also absorbs the ProcCreature2D feature set (outline,
// emission + rim boost, flash), so one mesh shader serves the whole family.
//
// This is the MeshRenderer sibling of Submachina/2D/SpriteLitSpecular: the same
// normal-mapped 2D diffuse lighting + the full specular glint pipeline (shared via
// SpecularLitCore.hlsl), but with the fill textures as FIRST-CLASS material slots —
// the mesh carries tiling UVs (local or world space, baked by the builder), so the
// seamless albedo/normal/specmask trio is just assigned on the material and repeats
// across the shape. No sprite Secondary Textures, no fill/edge submesh split.
//
// THE TEXCOORD1 EDGE CONTRACT (baked by every generated-mesh builder):
//   xy = outward direction at the nearest silhouette point (local space, unit)
//   z  = NORMALIZED edge distance: 0 at the silhouette, 1 at the band's inner
//        edge / body core (drives the edge band + Form Shape)
//   w  = WORLD-UNIT edge distance: 0 at the silhouette (drives the constant-width
//        outline + rim emission, taper-independent)
// Older BAKED spline mesh assets predate w (it reads 0) — the outline defaults
// off, so they render unchanged; re-bake before enabling outline on them.
//
// The baked band drives three composable edge effects:
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
Shader "Submachina/2D/Mesh2DLitSpecular"
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

        [Header(Outline Emission and Flash)]
        // Merged from ProcCreature2D (single source of truth in SpecularLitCore.hlsl).
        // Outline: constant-world-width band keyed off TEXCOORD1.w (the world-unit edge
        // distance every generated mesh bakes) — width 0 = off. Emission: flat HDR glow
        // plus a rim-concentrated boost. Flash: override toward a solid color (creature
        // code drives _FlashAmount per frame via MPB). All defaults are neutral.
        _OutlineColor("Outline Color (A = strength)", Color) = (0, 0, 0, 1)
        _OutlineWidth("Outline Width (world units, 0 = off)", Float) = 0
        _OutlineSoftness("Outline Softness", Range(0, 1)) = 0.25
        [HDR] _EmissionColor("Emission (HDR)", Color) = (0, 0, 0, 1)
        _RimEmission("Rim Emission Boost", Float) = 0
        _RimWidth("Rim Width (world units)", Float) = 0.15
        _FlashColor("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount("Flash Amount", Range(0, 1)) = 0

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
        // 9 = AlbedoHeight — derive the relief from the fill albedo treated as a height map
        // (the Laigter / Material Maker trick), for texture sets with no authored normal.
        _NormalMode("Normal Mode (0=texture)", Float) = 0
        // Mode 9: tap distance in TEXELS (1 = finest detail, larger reads broader forms) and
        // the gradient -> slope gain (NEGATIVE inverts, so dark reads as high).
        _HeightRadius("Albedo Height Radius (texels)", Range(0.25, 8)) = 1
        _HeightStrength("Albedo Height Strength (+/- inverts)", Range(-40, 40)) = 8
        // Blur = mip the broad gradient is read from (the cure for gritty results; NEEDS
        // MIPMAPS). Detail mixes the crisp LOD-0 gradient back over it. Compress soft-knees
        // the slope so hard albedo edges stop reading as cliffs.
        _HeightBlur("Albedo Height Blur (mip)", Range(0, 6)) = 1
        _HeightDetail("Albedo Height Detail Mix", Range(0, 1)) = 0.5
        _HeightCompress("Albedo Height Slope Compress", Range(0, 8)) = 2
        _NormalStrength("Normal Strength", Float) = 1
        _DiffNormalStrength("Diffuse Normal Strength", Float) = 1
        // Feed the 2D LIGHT BUFFER from the same normal the specular uses, not just _NormalMap.
        // This is what makes the procedural / AlbedoHeight modes read as real relief under
        // ordinary Light2Ds. Off by default — it changes the look of existing procedural setups.
        [MaterialToggle] _DiffFromMode("Diffuse Uses Normal Mode", Float) = 0
        _NormalEmboss("Relief Emboss (fake additive relief)", Float) = 0
        // How high each specular light sits above the plane for the emboss. LOW = grazing =
        // maximum relief contrast (reads as shadow); high = overhead = flat. 0.5 = legacy.
        _EmbossElevation("Emboss Light Elevation", Range(0.05, 2)) = 0.5
        _NormalFreq("Normal Frequency", Float) = 8
        // xy = UV of the sprite rect origin, zw = UV size — identity on meshes.
        _NormalUVRect("Normal UV Rect", Vector) = (0, 0, 1, 1)
        _NormalTex("Normal Override (straight RGB)", 2D) = "bump" {}
        _NormalTexST("Normal Override Tiling/Offset", Vector) = (1, 1, 0, 0)

        [Header(Form Shape)]
        // A broad procedural 3D form composited UNDER the detail normal via Reoriented
        // Normal Mapping. On this shader ANY non-zero mode uses the baked EDGE BAND as the
        // distance field: the whole piece domes/bevels up from its own outline — whatever
        // shape the spline is — while the tiled detail normal map rides on top. Rim +
        // Profile morph the family (rim 0/profile ~1 = dome, rim 0.6/profile ~0.2 = bevel,
        // rim 0.45/profile ~2 = pillow); Extent spans the band like Edge Effect Width.
        // Unlike Edge Bevel above (an additive rim nudge), this is a full form compose the
        // detail relief genuinely curves around. Feeds specular AND the 2D light buffer.
        _ShapeMode("Form Shape (0=off, else edge-band form)", Float) = 0
        _ShapeHeight("Form Height (slope gain)", Range(0, 8)) = 1.5
        _ShapeRim("Form Rim Start (0=dome .. plateau)", Range(0, 0.95)) = 0
        _ShapeProfile("Form Profile (0=linear .. round shoulder)", Range(0, 4)) = 1
        _ShapeRect("Form Rectangularity (unused here)", Range(0, 1)) = 0
        _ShapeExtent("Form Extent (fraction of band)", Range(0.1, 4)) = 1
        _ShapeAngle("Form Angle (unused here)", Float) = 0
        _ShapeDetail("Form Detail Blend (detail riding the form)", Range(0, 1)) = 1
        _ShapeBlur("Form Silhouette Blur (unused here)", Range(0, 6)) = 3

        [Header(Ambient Relief)]
        // Ungated relief: URP 2D can only shade normals from POSITIONAL lights (a Global
        // Light2D is a flat multiply) and _NormalEmboss is falloff/cone gated, so relief
        // vanishes wherever no specular light reaches. See SpriteLitSpecular / the core
        // include for the full story. All three default to OFF.
        //   A) fill   — a fixed virtual "sun" shading the relief everywhere. Keep the
        //               direction the same across materials so the scene shares one sun.
        //   B) slope  — steeper pixels darker; direction-independent depth floor.
        //   C) cavity — the height field's Laplacian: dark pits, bright ridges, no baked map.
        //               Gain is per WORLD unit, so it does NOT drift with camera zoom or with
        //               a tiling override (material or SplineFillOverride). Negative flips it.
        _AmbientFill("Ambient Fill Strength", Range(0, 3)) = 0
        _AmbientDir("Ambient Fill Dir (xyz)", Vector) = (-0.45, 0.6, 0.5, 0)
        _SlopeAO("Slope Shading Exponent (0 = off)", Range(0, 4)) = 0
        _CavityAmount("Cavity Occlusion (pits)", Range(0, 2)) = 0
        _CavityRidge("Cavity Ridge Highlight", Range(0, 2)) = 0
        _CavityScale("Cavity Curvature Gain (+/-)", Range(-20, 20)) = 1
        _CavitySpec("Cavity Occludes Specular", Range(0, 1)) = 0
        //   D) grooves — the LIGHT-FOLLOWING cavity: net darkening in grooves running ACROSS
        //      a specular light's direction, none along it, so shading sweeps with the beam.
        //      Complements Relief Emboss (contrast) with the net energy loss it cancels out.
        //      Same per-world-unit curvature units as Cavity Gain, so the two are comparable.
        //      Lit Fade hands crevices from the ambient cavity to these where light lands.
        _DirCavity("Directional Groove Strength", Range(0, 2)) = 0
        _DirCavityScale("Directional Groove Gain (+/-)", Range(-20, 20)) = 1
        _CavityLitFade("Cavity Fade Under Direct Light", Range(0, 1)) = 0

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
                // Everything shared with the sprite shader lives in one include so the
                // six CBUFFERs across the family can never drift apart.
                #include "SpecularLitProperties.hlsl"
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
                // uvEff is the coordinate the normal varies over — uvNrm here, so the fill's
                // tiling (material, or a SplineFillOverride per-object override) is folded in
                // and the curvature terms don't need re-tuning when a piece is retiled.
                float2 uvEff;
                // Normal-map UV vs ALBEDO UV: mode 9 differentiates the albedo, so it must ride
                // the fill UV (input.uv) rather than uvNrm — otherwise a de-synced _NormalMap_ST
                // would have it reading a different part of the texture than the one on screen.
                half3 nCurv = ComputeSurfaceNormal(uvNrm, input.uv, input.worldPos.xy, uvEff);
                if (_NormalMapOnce > 0.5h && (any(uvNrm < 0.0) || any(uvNrm > 1.0)))
                    nCurv = half3(0.0h, 0.0h, 1.0h);
                // nCurv is kept UNBEVELLED for the curvature terms: the bevel is a wide smooth
                // ramp across the whole band, so differentiating it would paint a false cavity
                // ring around the rim — which _EdgeDarken is already treating.
                // Form Shape first (the edge band as a distance field — the whole piece domes
                // from its outline, detail relief curving around the form via RNM), then the
                // legacy additive edge bevel on top; both are excluded from the curvature.
                half3 nShaped = _ShapeMode > 0.5h
                    ? ComposeFormNormal(ComputeFormNormalEdge((half2)input.edge.xy, input.edge.z), nCurv)
                    : nCurv;
                half3 nBase = normalize(half3(nShaped.xy + (half2)input.edge.xy * (_EdgeBevel * w), nShaped.z));

                // Edge darken BEFORE the specular add (the Photoshop "inner glow, black,
                // multiply" look) — bevel glints still fire on the darkened rim, which is
                // what sells the rim as shadowed 3D form instead of a painted border.
                c.rgb *= lerp(half3(1.0h, 1.0h, 1.0h), _EdgeColor.rgb, w * _EdgeDarken);

                // Outline band (merged ProcCreature feature): constant world width via the
                // baked world-unit edge distance. Before the specular for the same reason
                // as the darken — glints riding the band read as shaded form.
                c = ApplyEdgeOutline(c, (half)input.edge.w);

                // Spec mask on its own UV; Stamp Once reads the configurable background
                // colour outside the window (match the stamp's border: white = neutral
                // spec elsewhere, black = dull rock everywhere except the glowy spot).
                half3 specMask = (half3)SAMPLE_TEXTURE2D(_SpecMask, sampler_SpecMask, uvSpec).rgb;
                if (_SpecMaskOnce > 0.5h && (any(uvSpec < 0.0) || any(uvSpec > 1.0)))
                    specMask = _SpecMaskOnceBg.rgb;

                // Full glint pipeline shared with the sprite shader.
                half3 texel = (half3)SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb * input.color.rgb;
                c = ApplySpecularMasked(c, nBase, nCurv, texel, specMask, uvEff, input.worldPos.xy);

                // Emission (+ rim boost) and flash AFTER the specular — self-lit glow the
                // relief terms must not darken, and a full flash overrides glints too.
                c = ApplyEmissionFlash(c, (half)input.edge.w);

                // Alpha fade LAST, over everything (lit base + glints + glow), so the
                // whole surface melts toward the background at the rim.
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
                half4  color    : COLOR;
                float3 worldPos : TEXCOORD4; // needed by the world-space normal modes
                float4 edge     : TEXCOORD5; // xy = outward dir, z = edge distance 0..1
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

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
                // Everything shared with the sprite shader lives in one include so the
                // six CBUFFERs across the family can never drift apart.
                #include "SpecularLitProperties.hlsl"
            CBUFFER_END

            // Shared core, for ComputeSurfaceNormal when _DiffFromMode is on.
            #include "SpecularLitCore.hlsl"

            Varyings NormalsRenderingVertex(Attributes input)
            {
                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color;
                o.edge = input.edgeData;
                o.worldPos = TransformObjectToWorld(input.positionOS.xyz);
                // Keep the light-buffer normals sampling in lockstep with the lit pass's tiling.
                o.uv = o.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                // Normals output with the relief deepened by _DiffNormalStrength, PLUS the edge
                // bevel: near the rim the normal is tilted outward before it lands in the 2D
                // light buffer, so every Light2D shades the edge as a rounded form.
                const half4 mainTex = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half dd = (half)saturate(input.edge.z / max(_EdgeWidth, 1e-4));
                half w = pow(saturate(1.0h - dd), _EdgeFalloff);
                // Same relative normal-map UV + stamp-once window as the lit pass, so the
                // diffuse light buffer and the specular read identical relief.
                float2 uvNrm = input.uv * _NormalMap_ST.xy + _NormalMap_ST.zw;

                // _DiffFromMode on = light the buffer from the same ComputeSurfaceNormal the
                // specular uses, so procedural / albedo-height relief reaches the diffuse
                // lighting instead of only glinting. Off = the stock _NormalMap path.
                half3 normalTS;
                if (_DiffFromMode > 0.5h)
                {
                    float2 uvEff;
                    normalTS = ComputeSurfaceNormal(uvNrm, input.uv, input.worldPos.xy, uvEff);
                }
                else
                {
                    normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvNrm));
                }
                if (_NormalMapOnce > 0.5h && (any(uvNrm < 0.0) || any(uvNrm > 1.0)))
                    normalTS = half3(0.0h, 0.0h, 1.0h);

                // Detail at diffuse depth first, then the Form Shape composed under it (the
                // edge-band dome shades as real raised depth under every Light2D), then the
                // legacy additive edge bevel on top — same order as the lit pass.
                normalTS = ScaleRelief(normalTS, _DiffNormalStrength);
                if (_ShapeMode > 0.5h)
                    normalTS = BlendNormalsRNM(ComputeFormNormalEdge((half2)input.edge.xy, input.edge.z), normalTS);
                normalTS = normalize(half3(normalTS.xy + (half2)input.edge.xy * (_EdgeBevel * w), normalTS.z));
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
                // Everything shared with the sprite shader lives in one include so the
                // six CBUFFERs across the family can never drift apart.
                #include "SpecularLitProperties.hlsl"
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
