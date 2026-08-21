// URP 2D Sprite-Lit with an added HDR specular glint that tracks the REAL lights.
//
// Base is a verbatim copy of "Universal Render Pipeline/2D/Sprite-Lit-Default"
// (so the sprite still gets normal-mapped 2D diffuse lighting + shadows). The only
// change is in the Universal2D LitFragment, where we add a Blinn-Phong specular
// highlight, output as HDR so it blooms — faking a metallic/wet glint that URP 2D's
// diffuse-only model can't do.
//
// The glint is driven entirely on the GPU from a small set of GLOBAL light uniforms
// (see SpecularLight2DManager). Each fragment loops over the active lights, gates the
// glint by the light's cone (beam) + distance falloff, and computes Blinn-Phong
// against the light's ACTUAL world direction. This means:
//   - no per-sprite CPU (idle sprites leave the CPU entirely),
//   - zero lag (the hotspot follows the real light every frame),
//   - local-multiplayer ready (a fixed-size global array, MAX_SPEC_LIGHTS).
// The globals live OUTSIDE UnityPerMaterial, so the SRP Batcher is unaffected.
//
// Per-instance look/behaviour is MaterialPropertyBlock-driven, set once at spawn by a
// per-instance driver (e.g. OreSpecularController): _SpecColor / _SpecPower /
// _SpecIntensity (resting baseline glint), _LightResponse (how strongly this sprite
// answers the lights), _SpecBoost (transient flare), and the idle shimmer trio.
//
// Notes:
//  - The normal comes from the sprite's "_NormalMap" Secondary Texture (same one the
//    lighting uses), so it lines up with the surface relief and glints on facets.
//  - An optional "_SpecMask" Secondary Texture (RGB) tints AND scales ALL specular
//    (baseline + boost + light-driven) per pixel, so one sprite can mix shiny areas
//    with dull rock, and crystals can glint their own baked colour (e.g. purple
//    amethyst). Composes with _SpecColor. Defaults to white = unchanged.
//  - All three passes must share an identical UnityPerMaterial CBUFFER layout or the
//    SRP Batcher breaks — the spec properties are declared in every pass.
Shader "Submachina/2D/SpriteLitSpecular"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        // Per-pixel specular gate (RGB tints + scales all specular). Bound automatically when a
        // sprite carries a "_SpecMask" Secondary Texture; white default leaves sprites unchanged.
        _SpecMask("Specular Mask (RGB)", 2D) = "white" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        [Header(Metallic Specular)]
        [HDR] _SpecColor("Specular Color (HDR)", Color) = (1.3, 1.4, 1.6, 1)
        _SpecPower("Specular Tightness", Range(1, 200)) = 64
        _SpecIntensity("Resting Specular Intensity", Float) = 0
        // Baseline glint direction in tangent/UV space (z = toward viewer). Only used for
        // the resting/shimmer/boost glint when no light is reacting.
        _SpecLightDir("Baseline Glint Dir", Vector) = (-0.45, 0.6, 0.66, 0)
        _LightResponse("Light Response", Float) = 1
        _SpecBoost("Specular Boost (mining/pulse)", Float) = 0
        // Balance the glint against the albedo. 0 = additive (glint adds HDR on top and can
        // wash the texture out); 1 = energy-conserving replace (albedo fades toward the glint
        // colour so the texture stays visible under the highlight). Blend anywhere between.
        _SpecReplace("Specular Replace (0 add..1 replace)", Range(0, 1)) = 0
        // Ceiling on the light-driven glint's PEAK strength (strength × response, applied BEFORE
        // the distance falloff and cone shape it) so the falloff always reads through. 0 = no clamp.
        _SpecClamp("Specular Clamp (0 = off)", Float) = 0
        // How much the sprite's own texel colours the glint (metallic feel). 0 = pure _SpecColor;
        // 1 = the glint is fully tinted by the underlying texture — dark cracks stay dark, coloured
        // pixels flare their own colour — so texture detail reads through the highlight.
        _SpecAlbedoTint("Specular Albedo Tint", Range(0, 1)) = 0
        // Screen-style softening of the ADDITIVE glint: 0 = classic HDR add (can bloom/blow out),
        // 1 = the glint is Reinhard-compressed below white (s/(1+s)) and fills only the remaining
        // headroom — the result can never pass white (kills blow-out AND bloom), so the texture's
        // contrast always survives under the highlight.
        _SpecScreen("Specular Screen Blend", Range(0, 1)) = 0
        // How much the glint's light vector leans toward the viewer instead of the light
        // (the Z of L before normalize). 0 = fully directional — a facet only glints when it
        // faces the light; higher = more omnidirectional glow regardless of the light's side.
        // 0.66 preserves the shader's original feel.
        _SpecViewBias("Specular View Bias (omnidirectionality)", Range(0, 10)) = 0.66

        [Header(Glow Zone)]
        // Threshold-gated glow regions driven by the spec mask's BRIGHTNESS: pixels whose
        // _SpecMask max-component exceeds the threshold blend from the normal specular set
        // above to this Glow set — omnidirectional bias, wider lobe, HDR gain — and the
        // screen/replace compose fades back to pure additive so the glow survives to bloom.
        // Lets one sprite mix directional metallic sparkle (body) with bloomy hotspots
        // (e.g. crystal blobs painted bright in the mask). Threshold > 1 (the default)
        // disables the zone entirely — a 0..1 mask can never reach it.
        _GlowThreshold("Glow Mask Threshold (>1 = off)", Range(0, 2)) = 2
        _GlowKnee("Glow Threshold Knee (soft edge)", Range(0.001, 0.5)) = 0.15
        _GlowViewBias("Glow View Bias (omnidirectionality)", Range(0, 10)) = 4
        _GlowPower("Glow Tightness", Range(1, 200)) = 8
        _GlowGain("Glow Gain (HDR multiplier)", Float) = 2

        [Header(Animation)]
        _ShimmerAmp("Intensity Mod Amplitude", Float) = 0
        _ShimmerSpeed("Intensity Mod Speed", Float) = 1
        _ShimmerPhase("Intensity Mod Phase", Float) = 0
        // Waveform for the intensity modulation: 0 = Sine, 1 = PingPong (triangle), 2 = Noise (smooth random).
        _ShimmerWave("Intensity Mod Waveform", Float) = 0
        // What the intensity modulation drives: 0 = scale base intensity (legacy ±fraction),
        // 1 = absolute add on top of base (flickers even at base 0), 2 = scale the light-driven
        // glint only (dark stays dark until lit, then shimmers with the light), 3 = scale both.
        _ShimmerMode("Intensity Mod Mode", Float) = 0
        // Glint-direction wobble: x = angle amplitude (radians), y = speed, z = waveform, w = phase.
        // Rotates BOTH the baseline glint dir and the real-light specular dir for a watery shimmer.
        _DirWobble("Direction Wobble (amp, speed, wave, phase)", Vector) = (0, 1, 0, 0)

        [Header(Surface Normal)]
        // 0 = sprite _NormalMap; 1 = Dome, 2 = Bevel, 3 = Ripples, 4 = Radial, 5 = Facets; 6 = _NormalTex override;
        // 7 = World Facets, 8 = World Ripples (world-space patterns — seam-free on stitched
        // geometry like SpriteShape fill + edges, where sprite-local UV modes would jump per segment).
        // 9 = AlbedoHeight — derive the relief from the albedo treated as a height map (the
        // Laigter / Material Maker trick), for sprites with no authored normal.
        _NormalMode("Normal Mode (0=texture)", Float) = 0
        // Mode 9 controls. Radius is the central-difference tap distance in TEXELS (1 =
        // adjacent texel = finest detail; larger reads broader forms and suppresses noise).
        // Strength is the gradient -> slope gain; NEGATIVE inverts the relief so dark reads
        // as high. _NormalStrength still scales the result afterwards, as it does every mode.
        _HeightRadius("Albedo Height Radius (texels)", Range(0.25, 8)) = 1
        _HeightStrength("Albedo Height Strength (+/- inverts)", Range(-40, 40)) = 8
        // Blur = mip level the broad gradient is read from; the single best cure for a gritty,
        // pixelly result. NEEDS MIPMAPS on the texture — sprites often have them off, and then
        // this does nothing. Detail mixes the crisp LOD-0 gradient back over that broad form
        // (0 = pure form, 1 = all the texture's detail). Compress applies a soft knee to the
        // slope so hard albedo edges stop reading as cliffs while gentle shading survives.
        _HeightBlur("Albedo Height Blur (mip)", Range(0, 6)) = 1
        _HeightDetail("Albedo Height Detail Mix", Range(0, 1)) = 0.5
        _HeightCompress("Albedo Height Slope Compress", Range(0, 8)) = 2
        _NormalStrength("Normal Strength", Float) = 1
        // Deepens the relief fed to the 2D LIGHTING normal buffer (NormalsRendering pass), so
        // every Light2D shades this sprite with exaggerated bumps. Independent of the specular
        // _NormalStrength above. 1 = as authored, >1 = deeper, <1 = flatter.
        _DiffNormalStrength("Diffuse Normal Strength", Float) = 1
        // Feed the 2D LIGHT BUFFER from the same normal the specular uses, instead of only
        // from _NormalMap. This is what makes the procedural and AlbedoHeight modes read as
        // real relief under ordinary Light2Ds — without it they drive the specular alone and
        // the surface stays flat under normal lighting. Off by default: turning it on changes
        // the look of anything already using a procedural mode.
        [MaterialToggle] _DiffFromMode("Diffuse Uses Normal Mode", Float) = 0
        // Fake "additive light" relief: multiplies the lit color by the normal's signed response
        // to each specular light (facing-light facets brighten past the multiply-lit level,
        // facing-away darken, flat pixels untouched). 0 = off; the useful range is ~0.5-3.
        _NormalEmboss("Relief Emboss (fake additive relief)", Float) = 0
        // How high each specular light sits above the surface plane for the emboss above.
        // LOW = grazing = maximum relief contrast, which is what reads as shadow rather than
        // as tinting; high = overhead = flat. 0.5 matches the value this used to hardcode.
        _EmbossElevation("Emboss Light Elevation", Range(0.05, 2)) = 0.5
        _NormalFreq("Normal Frequency", Float) = 8
        // xy = UV of the sprite rect origin, zw = UV size (remaps to 0..1 local coords).
        _NormalUVRect("Normal UV Rect", Vector) = (0, 0, 1, 1)
        // Explicit normal map (straight RGB, sRGB off) supplied inline instead of the sprite's secondary texture.
        _NormalTex("Normal Override (straight RGB)", 2D) = "bump" {}
        // xy = tiling (scale), zw = offset for the override texture within the sprite rect (like Unity's _ST).
        _NormalTexST("Normal Override Tiling/Offset", Vector) = (1, 1, 0, 0)

        [Header(Form Shape)]
        // A broad procedural 3D form composited UNDER the detail normal above via Reoriented
        // Normal Mapping: the sprite reads as a raised solid (dome/bevel/pillow, cylinder,
        // slope, or inflated from its own silhouette) while the Normal Mode still supplies
        // the surface texture riding on it. Feeds the specular, the emboss/ambient relief,
        // AND the 2D light buffer, so the form shades as real depth under every Light2D.
        // 0 = off. Rim + Profile morph the family: rim 0/profile ~1 = dome; rim 0.6/profile
        // ~0.2 = bevel; rim 0.45/profile ~2 = pillow; rim 0/profile 0 = cone.
        _ShapeMode("Form Shape (0=off 1=shape 2=cyl 3=slope 4=silhouette)", Float) = 0
        _ShapeHeight("Form Height (slope gain)", Range(0, 8)) = 1.5
        _ShapeRim("Form Rim Start (0=dome .. plateau)", Range(0, 0.95)) = 0
        _ShapeProfile("Form Profile (0=linear .. round shoulder)", Range(0, 4)) = 1
        _ShapeRect("Form Rectangularity (0=round 1=rect)", Range(0, 1)) = 0
        _ShapeExtent("Form Extent (footprint scale)", Range(0.1, 4)) = 1
        _ShapeAngle("Form Angle (radians)", Float) = 0
        _ShapeDetail("Form Detail Blend (detail riding the form)", Range(0, 1)) = 1
        _ShapeBlur("Form Silhouette Blur (mip)", Range(0, 6)) = 3

        [Header(Ambient Relief)]
        // URP 2D shades normal maps only from POSITIONAL lights — a Global Light2D has no
        // position, so it CANNOT light a normal map (it's a flat multiply) — and the
        // _NormalEmboss above is gated by each specular light's falloff/cone. So relief
        // disappears wherever no specular light reaches. These are the ungated substitute
        // (see SpecularLitCore.hlsl for the full story); all three default to OFF.
        //
        // A) Virtual directional fill — a fixed "sun" that shades the relief everywhere.
        //    Keep the direction identical across every material or the scene reads as if
        //    each rock had its own sun. xyz = tangent/UV space (z leans toward the viewer;
        //    lower z = more grazing = more relief contrast).
        _AmbientFill("Ambient Fill Strength", Range(0, 3)) = 0
        _AmbientDir("Ambient Fill Dir (xyz)", Vector) = (-0.45, 0.6, 0.5, 0)
        // B) Slope shading — steeper pixels darker. One pow, direction-independent, not
        //    true occlusion (it dims the rim of a big smooth dome too). 0 = off, ~0.5-3 useful.
        _SlopeAO("Slope Shading Exponent (0 = off)", Range(0, 4)) = 0
        // C) Cavity from the height field's Laplacian — a live cavity map, no baked texture:
        //    pits darken, ridge crests brighten. Occlusion/Ridge weight the two halves
        //    independently; Gain scales the curvature, measured per WORLD unit, so it does
        //    NOT drift with camera zoom or with a per-object tiling override. A NEGATIVE gain
        //    flips pit/ridge — the fix for a normal map with an inverted X/green channel.
        _CavityAmount("Cavity Occlusion (pits)", Range(0, 2)) = 0
        _CavityRidge("Cavity Ridge Highlight", Range(0, 2)) = 0
        _CavityScale("Cavity Curvature Gain (+/-)", Range(-20, 20)) = 1
        // How much the cavity/slope occlusion ALSO gates the specular — grit packed into a
        // crevice shouldn't glint. 0 = glint unoccluded, 1 = fully occluded.
        _CavitySpec("Cavity Occludes Specular", Range(0, 1)) = 0
        // D) Directional grooves — the LIGHT-FOLLOWING cavity. Net darkening in grooves that
        //    run ACROSS a specular light's direction, none in grooves running along it, so the
        //    shading sweeps as the beam moves. Complements Relief Emboss: that gives the
        //    bright-wall/dark-wall contrast, this gives the net energy loss the emboss (being
        //    antisymmetric) cancels to zero. Same per-world-unit curvature units as the
        //    isotropic Cavity Gain above, so the two are directly comparable. Negative flips it.
        _DirCavity("Directional Groove Strength", Range(0, 2)) = 0
        _DirCavityScale("Directional Groove Gain (+/-)", Range(-20, 20)) = 1
        // How much DIRECT light suppresses the isotropic (ambient) cavity above. Physically AO
        // belongs to ambient light while direct light casts real shadows — the emboss and the
        // grooves are those — so at 0 a spotlit crevice gets darkened twice by two models.
        // Raise toward 1 to hand crevices over to the directional terms wherever light lands.
        _CavityLitFade("Cavity Fade Under Direct Light", Range(0, 1)) = 0

        // Sorting layer bit for this sprite (set per-instance by SpecularController). Lights whose
        // sorting layer mask doesn't overlap this bit are skipped. Default = all layers visible.
        _SortingLayerBit("Sorting Layer Bit", Float) = 16777215

        // Legacy properties so materials can gracefully fall back to the legacy sprite shader.
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
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

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4  color       : COLOR;
                float3 worldPos    : TEXCOORD4; // world position for real-light specular
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
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
                half _EmbossElevation;
                half _DirCavity;
                half _DirCavityScale;
                half _CavityLitFade;
                // Ambient relief (ungated fill light + cavity/slope AO) — SpecularLitCore.hlsl
                half4 _AmbientDir;
                half _AmbientFill;
                half _SlopeAO;
                half _CavityAmount;
                half _CavityRidge;
                half _CavityScale;
                half _CavitySpec;
                // Albedo-as-height normal (mode 9). Deliberately NOT named _MainTex_TexelSize:
                // the 2D SRP Batcher rejects any material carrying a _TexelSize/_ST property,
                // so the magic name would silently unbatch every sprite using this shader.
                float4 _HeightTexel;
                half _HeightRadius;
                half _HeightStrength;
                half _HeightBlur;
                half _HeightDetail;
                half _HeightCompress;
                half _DiffFromMode;
                half _NormalFreq;
                float4 _NormalUVRect;
                float4 _NormalTexST;
                // Form Shape: broad procedural form composited under the detail normal (RNM)
                half _ShapeMode;
                half _ShapeHeight;
                half _ShapeRim;
                half _ShapeProfile;
                half _ShapeRect;
                half _ShapeExtent;
                half _ShapeAngle;
                half _ShapeDetail;
                half _ShapeBlur;
                float _SortingLayerBit;
            CBUFFER_END

            // Shared specular core: global light uniforms, surface-normal modes, and the
            // full glint pipeline (ApplySpecular). Must come after the CBUFFER above and
            // the URP includes so it can reference the material properties and _NormalMap.
            #include "SpecularLitCore.hlsl"

            Varyings LitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                o.worldPos = TransformObjectToWorld(input.positionOS.xyz);

                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                // Base = the stock normal-mapped 2D diffuse lit color.
                half4 c = CommonLitFragment(input, input.color);

                // Surface normal at authored depth (sprite map / procedural / world pattern) +
                // the raw vertex-tinted texel (emboss relief + albedo-tinted glint use it).
                // uvEff is the coordinate the normal actually varies over (the mode's own
                // tiling/atlas remap folded in) — the curvature terms need it to stay correct
                // under per-instance tiling.
                float2 uvEff;
                // Normal UV and albedo UV are the same thing on a sprite (one texture set).
                half3 nDetail = ComputeSurfaceNormal(input.uv, input.uv, input.worldPos.xy, uvEff);
                half3 texel = (half3)SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb * input.color.rgb;

                // Form Shape: compose the broad procedural form UNDER the detail normal, so
                // the whole sprite reads as a raised solid with the detail riding on it.
                // The curvature terms keep the un-formed detail (nDetail) — a broad form is
                // a wide smooth ramp that would otherwise paint a false cavity ring.
                half3 nBase = nDetail;
                if (_ShapeMode > 0.5h)
                {
                    float2 p = (input.uv - _NormalUVRect.xy) / max(_NormalUVRect.zw, 1e-5);
                    nBase = ComposeFormNormal(ComputeFormNormal(p, input.uv), nDetail);
                }

                // Full glint pipeline (baseline + shimmer + real lights + glow zone + compose)
                // lives in SpecularLitCore.hlsl, shared with the spline-fill mesh shader.
                return ApplySpecular(c, nBase, nDetail, texel, input.uv, uvEff, input.worldPos.xy);
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

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4   color           : COLOR;
                float3  worldPos        : TEXCOORD4; // needed by the world-space normal modes
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START( UnityPerMaterial )
                half4 _Color;
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
                half _EmbossElevation;
                half _DirCavity;
                half _DirCavityScale;
                half _CavityLitFade;
                // Ambient relief (ungated fill light + cavity/slope AO) — SpecularLitCore.hlsl
                half4 _AmbientDir;
                half _AmbientFill;
                half _SlopeAO;
                half _CavityAmount;
                half _CavityRidge;
                half _CavityScale;
                half _CavitySpec;
                // Albedo-as-height normal (mode 9). Deliberately NOT named _MainTex_TexelSize:
                // the 2D SRP Batcher rejects any material carrying a _TexelSize/_ST property,
                // so the magic name would silently unbatch every sprite using this shader.
                float4 _HeightTexel;
                half _HeightRadius;
                half _HeightStrength;
                half _HeightBlur;
                half _HeightDetail;
                half _HeightCompress;
                half _DiffFromMode;
                half _NormalFreq;
                float4 _NormalUVRect;
                float4 _NormalTexST;
                // Form Shape: broad procedural form composited under the detail normal (RNM)
                half _ShapeMode;
                half _ShapeHeight;
                half _ShapeRim;
                half _ShapeProfile;
                half _ShapeRect;
                half _ShapeExtent;
                half _ShapeAngle;
                half _ShapeDetail;
                half _ShapeBlur;
                float _SortingLayerBit;
            CBUFFER_END

            // Shared core, for ComputeSurfaceNormal/ScaleRelief when _DiffFromMode is on.
            #include "SpecularLitCore.hlsl"

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                o.worldPos = TransformObjectToWorld(input.positionOS.xyz);

                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                const half4 mainTex = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Where the LIGHT BUFFER's normal comes from. _DiffFromMode on = the same
                // ComputeSurfaceNormal the specular uses, so procedural and albedo-height
                // relief is lit by every Light2D instead of only glinting — without it those
                // modes never reach the diffuse lighting and the surface reads flat.
                // Off = the stock path, the sprite's own _NormalMap.
                half3 normalTS;
                if (_DiffFromMode > 0.5h)
                {
                    float2 uvEff;
                    normalTS = ComputeSurfaceNormal(input.uv, input.uv, input.worldPos.xy, uvEff);
                }
                else
                {
                    normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));
                }

                // Deepen the relief by _DiffNormalStrength: scaling the tangent-space XY
                // relative to Z tilts the normals further off-flat BEFORE they land in the 2D
                // light buffer, so every Light2D (multiply included) shades with bigger bumps.
                normalTS = ScaleRelief(normalTS, _DiffNormalStrength);

                // Form Shape into the light buffer too (detail already at diffuse depth, so
                // the form composes at its own height): this is what makes the dome/bevel/
                // silhouette form shade as REAL raised depth under every Light2D, instead of
                // only shaping the specular.
                if (_ShapeMode > 0.5h)
                {
                    float2 p = (input.uv - _NormalUVRect.xy) / max(_NormalUVRect.zw, 1e-5);
                    normalTS = BlendNormalsRNM(ComputeFormNormal(p, input.uv), normalTS);
                }
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
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
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
                half _EmbossElevation;
                half _DirCavity;
                half _DirCavityScale;
                half _CavityLitFade;
                // Ambient relief (ungated fill light + cavity/slope AO) — SpecularLitCore.hlsl
                half4 _AmbientDir;
                half _AmbientFill;
                half _SlopeAO;
                half _CavityAmount;
                half _CavityRidge;
                half _CavityScale;
                half _CavitySpec;
                // Albedo-as-height normal (mode 9). Deliberately NOT named _MainTex_TexelSize:
                // the 2D SRP Batcher rejects any material carrying a _TexelSize/_ST property,
                // so the magic name would silently unbatch every sprite using this shader.
                float4 _HeightTexel;
                half _HeightRadius;
                half _HeightStrength;
                half _HeightBlur;
                half _HeightDetail;
                half _HeightCompress;
                half _DiffFromMode;
                half _NormalFreq;
                float4 _NormalUVRect;
                float4 _NormalTexST;
                // Form Shape: broad procedural form composited under the detail normal (RNM)
                half _ShapeMode;
                half _ShapeHeight;
                half _ShapeRim;
                half _ShapeProfile;
                half _ShapeRect;
                half _ShapeExtent;
                half _ShapeAngle;
                half _ShapeDetail;
                half _ShapeBlur;
                float _SortingLayerBit;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color *_Color * unity_SpriteColor;
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
