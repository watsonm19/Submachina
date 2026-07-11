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
        // 0 = sprite _NormalMap; 1 = Dome, 2 = Bevel, 3 = Ripples, 4 = Radial, 5 = Facets; 6 = _NormalTex override.
        _NormalMode("Normal Mode (0=texture)", Float) = 0
        _NormalStrength("Normal Strength", Float) = 1
        // Deepens the relief fed to the 2D LIGHTING normal buffer (NormalsRendering pass), so
        // every Light2D shades this sprite with exaggerated bumps. Independent of the specular
        // _NormalStrength above. 1 = as authored, >1 = deeper, <1 = flatter.
        _DiffNormalStrength("Diffuse Normal Strength", Float) = 1
        _NormalFreq("Normal Frequency", Float) = 8
        // xy = UV of the sprite rect origin, zw = UV size (remaps to 0..1 local coords).
        _NormalUVRect("Normal UV Rect", Vector) = (0, 0, 1, 1)
        // Explicit normal map (straight RGB, sRGB off) supplied inline instead of the sprite's secondary texture.
        _NormalTex("Normal Override (straight RGB)", 2D) = "bump" {}
        // xy = tiling (scale), zw = offset for the override texture within the sprite rect (like Unity's _ST).
        _NormalTexST("Normal Override Tiling/Offset", Vector) = (1, 1, 0, 0)

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
                half _ShimmerAmp;
                half _ShimmerSpeed;
                half _ShimmerPhase;
                half _ShimmerWave;
                half _ShimmerMode;
                half4 _DirWobble;
                half _NormalMode;
                half _NormalStrength;
                half _DiffNormalStrength;
                half _NormalFreq;
                float4 _NormalUVRect;
                float4 _NormalTexST;
                float _SortingLayerBit;
            CBUFFER_END

            // ---- Global specular lights (packed each frame by SpecularLight2DManager) ----
            // Kept OUTSIDE UnityPerMaterial so the SRP Batcher is unaffected.
            #define MAX_SPEC_LIGHTS 4
            float  _SpecLightCount;
            float4 _SpecLightA[MAX_SPEC_LIGHTS]; // xy = world pos, z = outer radius, w = strength
            float4 _SpecLightB[MAX_SPEC_LIGHTS]; // xy = aim dir (world, normalized), z = cos(outerHalf), w = cos(innerHalf)
            float  _SpecLightC[MAX_SPEC_LIGHTS]; // sorting layer bitmask (which layers this light targets)

            // Per-light distance-falloff LUT (one ROW per light slot), baked by SpecularLight2DManager.
            // u = normalized distance (0 at the light, 1 at the reach); each row is that light's curve
            // (or a plain linear ramp when it uses the default). Sampling is branchless and coherent.
            TEXTURE2D(_SpecFalloffLUT);
            SAMPLER(sampler_SpecFalloffLUT);

            // Inline normal-map override (bound per-instance via MaterialPropertyBlock), so a sprite
            // can supply a normal without wiring it up as a Secondary Texture in its import settings.
            TEXTURE2D(_NormalTex);
            SAMPLER(sampler_NormalTex);

            // Per-pixel specular mask (Secondary Texture "_SpecMask", RGB tint × strength).
            // Textures live outside UnityPerMaterial, so the SRP Batcher is unaffected.
            TEXTURE2D(_SpecMask);
            SAMPLER(sampler_SpecMask);

            // Cheap 2D hash for the Facets pattern (a random value per cell).
            float2 Hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Signed -1..1 animation waveform shared by the intensity modulation and the
            // direction wobble. t = speed-scaled time + phase (radians for the sine); all
            // waveforms are normalized to the same period so speed feels consistent.
            //   mode 0 = Sine (smooth pulse), 1 = PingPong (linear triangle),
            //   mode 2 = Noise (smooth value noise — a new random level eased into per cycle).
            half Waveform(float t, half mode)
            {
                if (mode < 0.5h) return (half)sin(t);
                float u = t * 0.15915494; // radians -> cycles so triangle/noise match the sine's period
                if (mode < 1.5h) return (half)(abs(frac(u) * 2.0 - 1.0) * 2.0 - 1.0);
                // Value noise: hash a random level per cycle, smoothstep between neighbours.
                float i = floor(u);
                float f = u - i;
                float a = frac(sin(i * 12.9898) * 43758.5453);
                float b = frac(sin((i + 1.0) * 12.9898) * 43758.5453);
                return (half)(lerp(a, b, f * f * (3.0 - 2.0 * f)) * 2.0 - 1.0);
            }

            // Surface normal for the specular: either the sprite's normal map (mode 0) or a
            // procedural pattern generated from the sprite's local UV (modes 1..5). Procedural
            // normals only tilt in XY across the flat quad — enough to make lights glint with no
            // authored texture. _NormalUVRect remaps atlas UVs to 0..1 so patterns stay centered.
            half3 ComputeSurfaceNormal(float2 uv)
            {
                // Mode 0: the sprite's own normal map (straight RGB, matches our baked maps).
                // _NormalStrength deepens the relief: scaling XY relative to Z tilts the normals
                // further off-flat (1 = as authored, >1 = more pronounced, <1 = flatter).
                if (_NormalMode < 0.5h)
                {
                    half3 n0 = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv).xyz * 2.0h - 1.0h;
                    n0.xy *= _NormalStrength;
                    return normalize(n0);
                }

                // Remap the sprite's atlas UV to 0..1 across its own rect (used by the override
                // texture and every procedural pattern so they stay centered on atlased sprites).
                float2 p = (uv - _NormalUVRect.xy) / max(_NormalUVRect.zw, 1e-5);

                // Mode 6: explicit override texture (straight RGB), sampled in local UV.
                // Apply per-instance tiling/offset so the map can be scaled and positioned
                // to fit the sprite (tiling <1 enlarges the stamp; offset slides it around).
                if (_NormalMode > 5.5h)
                {
                    float2 tp = p * _NormalTexST.xy + _NormalTexST.zw;
                    half3 n6 = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, tp).xyz * 2.0h - 1.0h;
                    n6.xy *= _NormalStrength; // deepen the relief (see mode 0)
                    return normalize(n6);
                }

                float2 c = p - 0.5;
                float freq = max(_NormalFreq, 1e-4);
                float2 g = float2(0, 0); // desired normal tilt in XY (pre-strength)

                if (_NormalMode < 1.5h)          // Dome: one round highlight bulging from centre
                    g = c * 2.0;
                else if (_NormalMode < 2.5h)     // Bevel: flat centre, tilt outward only near the rim
                {
                    float d = max(abs(c.x), abs(c.y)) * 2.0;   // 0 centre .. 1 edge
                    float rim = smoothstep(0.6, 1.0, d);
                    g = normalize(c + 1e-4) * rim;
                }
                else if (_NormalMode < 3.5h)     // Ripples: parallel wavy bands along X
                    g = float2(cos(p.x * freq * 6.2831853), 0.0);
                else if (_NormalMode < 4.5h)     // Radial: concentric rings from centre
                {
                    float r = length(c);
                    g = (c / max(r, 1e-4)) * cos(r * freq * 6.2831853);
                }
                else                             // Facets: random tilt per hashed cell (sparkle)
                    g = Hash22(floor(p * freq)) * 2.0 - 1.0;

                return normalize(half3((half2)(g * _NormalStrength), 1.0h));
            }

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

                // Surface normal: the sprite's normal map, or a procedural pattern (Dome/Bevel/
                // Ripples/Radial/Facets) — see ComputeSurfaceNormal.
                half3 n = ComputeSurfaceNormal(input.uv);

                // In this flat 2D setup the viewer looks straight down +Z.
                const half3 V = half3(0, 0, 1);

                // --- Animation: intensity modulation + glint-direction wobble (both waveform-driven) ---
                // m = signed intensity modulation; wobble = signed rotation (radians) applied to the
                // glint directions below so the highlight slides/shimmers across the surface.
                half m = Waveform(_Time.y * _ShimmerSpeed + _ShimmerPhase, _ShimmerWave) * _ShimmerAmp;
                half wobble = Waveform(_Time.y * _DirWobble.y + _DirWobble.w, _DirWobble.z) * _DirWobble.x;
                half sw, cw;
                sincos(wobble, sw, cw);

                // --- Resting baseline glint (fixed virtual dir) + modulation + transient boost ---
                // Usually zero on gameplay ore (baseIntensity 0), so this collapses to the boost term.
                // The viewer lean comes from _SpecViewBias (shared with the light-driven glint below)
                // so one dial controls how omnidirectional ALL the glints are.
                half3 Lb = normalize(half3(_SpecLightDir.xy, _SpecViewBias));
                Lb.xy = half2(Lb.x * cw - Lb.y * sw, Lb.x * sw + Lb.y * cw); // direction wobble
                half3 Hb = normalize(Lb + V);
                half baseShape = pow(saturate(dot(n, Hb)), _SpecPower);

                // What the intensity modulation drives (_ShimmerMode):
                //   0 = scale base (±fraction of resting intensity — needs base > 0),
                //   1 = absolute add (base + m, so it flickers even at base 0 / unlit),
                //   2 = scale the light glint only (handled after the light loop),
                //   3 = scale base AND light together.
                half baseInt = _SpecIntensity;
                if (_ShimmerMode < 0.5h || _ShimmerMode > 2.5h) baseInt *= 1.0h + m;
                else if (_ShimmerMode < 1.5h) baseInt += m;
                half spec = baseShape * (max(baseInt, 0.0h) + _SpecBoost);

                // --- Light-driven glint: real lights, cone-gated, computed on the GPU ---
                half lightSpec = 0.0h;
                int count = (int)_SpecLightCount;
                uint spriteBits = (uint)_SortingLayerBit; // which sorting layer this sprite is on
                [loop]
                for (int i = 0; i < MAX_SPEC_LIGHTS; i++)
                {
                    if (i >= count) break;

                    // Sorting layer gate: skip lights that don't target this sprite's layer.
                    if ((spriteBits & (uint)_SpecLightC[i]) == 0u) continue;

                    float2 lightPos = _SpecLightA[i].xy;
                    half range = (half)_SpecLightA[i].z;
                    half strength = (half)_SpecLightA[i].w;

                    // Vector from the light to this fragment (world space).
                    float2 toFrag = input.worldPos.xy - lightPos;
                    half dist = (half)length(toFrag);
                    if (dist >= range || dist < 1e-4h) continue;
                    float2 dir = toFrag / dist; // light -> surface

                    // Cone gate: how aligned the surface is with the beam axis. Soft edge
                    // between the outer and inner half-angles reproduces the spotlight beam.
                    // Manual smoothstep with a guarded denominator so a hard cone (inner ==
                    // outer angle) degrades to a crisp step instead of a divide-by-zero NaN.
                    float2 aim = _SpecLightB[i].xy;
                    half cosA = (half)dot(aim, dir);
                    half cosOuter = (half)_SpecLightB[i].z;
                    half cosInner = (half)_SpecLightB[i].w;
                    half cone = saturate((cosA - cosOuter) / max(cosInner - cosOuter, 1e-4h));
                    cone = cone * cone * (3.0h - 2.0h * cone); // smoothstep shaping
                    if (cone <= 0.0h) continue;

                    // Distance falloff from this light's LUT row (linear ramp by default, or a
                    // manual curve authored on the SpecularLight2D). No mips → sample LOD 0 so the
                    // loop's control flow can't upset derivative-based mip selection.
                    half u = saturate(dist / range);
                    float v = ((float)i + 0.5) / MAX_SPEC_LIGHTS;
                    half falloff = SAMPLE_TEXTURE2D_LOD(_SpecFalloffLUT, sampler_SpecFalloffLUT, float2(u, v), 0).r;

                    // Blinn-Phong against the real light: L points from surface back to light,
                    // biased toward the viewer on Z (_SpecViewBias) so grazing beams still pop a
                    // highlight. Low bias = strictly directional facets; high bias = the glint
                    // fires from most light directions (omnidirectional glow, e.g. crystals).
                    // The direction wobble rotates only this specular dir — the cone gate and
                    // falloff above stay physical so the beam itself doesn't wander.
                    float2 sdir = float2(dir.x * cw - dir.y * sw, dir.x * sw + dir.y * cw);
                    half3 L = normalize(half3(-sdir.x, -sdir.y, _SpecViewBias));
                    half3 H = normalize(L + V);
                    half s = pow(saturate(dot(n, H)), _SpecPower);

                    // Optional ceiling on this light's PEAK glint (strength × response), applied
                    // BEFORE the falloff/cone shape it. Clamping the final value instead flattened
                    // the whole falloff gradient to the cap — a hard on/off line at the reach.
                    half g = s * strength * _LightResponse;
                    if (_SpecClamp > 0.0h) g = min(g, _SpecClamp);

                    lightSpec += g * falloff * cone;
                }
                // Lit-gated modulation (modes 2/3): scale the light-driven glint so the shimmer is
                // additive to whatever illumination is present — zero light means zero shimmer.
                // (_LightResponse is already folded in per light above, under the clamp.)
                half lightMul = _ShimmerMode > 1.5h ? max(1.0h + m, 0.0h) : 1.0h;
                spec += lightSpec * lightMul;

                // Per-pixel specular mask (RGB): tints AND scales all specular — baked crystals
                // carry their own glint colour at ~full strength, dull rock a dim grey (~0.2).
                // Composes with _SpecColor; white default leaves unmasked sprites unchanged.
                half3 specMask = (half3)SAMPLE_TEXTURE2D(_SpecMask, sampler_SpecMask, input.uv).rgb;

                // Albedo tint (metallic feel): pull the glint colour toward the sprite's own
                // vertex-tinted texel so texture detail reads through the highlight — dark cracks
                // stay dark, an amethyst texel flares purple. Uses the RAW texel (not the lit
                // result) so scene darkness doesn't double-dim the glint.
                half3 texel = (half3)SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb * input.color.rgb;
                half3 albedoTint = lerp(half3(1.0h, 1.0h, 1.0h), texel, _SpecAlbedoTint);

                // Compose the glint against the albedo, masked by sprite coverage.
                //   additive : glint added as HDR emission on top (feeds bloom, can blow out).
                //              _SpecScreen swaps it toward a Reinhard-compressed screen blend:
                //              the glint is squashed below 1 (s/(1+s)) then fills only the
                //              remaining headroom toward white — so at 1 the result can NEVER
                //              pass white (no blow-out, no bloom) and texture contrast survives.
                //   replaced : albedo faded toward the glint colour (energy-conserving, so the
                //              texture is never fully erased below a full-strength highlight)
                // _SpecReplace blends between them: 0 = pure additive, 1 = pure replace.
                half3 specColor = _SpecColor.rgb * specMask * albedoTint;
                half cover = spec * c.a;
                half3 glint = specColor * cover;
                half3 soft = glint / (1.0h + glint); // Reinhard: HDR glint compressed to <1
                half3 additive = c.rgb + lerp(glint, soft * saturate(1.0h - c.rgb), _SpecScreen);
                half3 replaced = lerp(c.rgb, specColor, saturate(cover));
                c.rgb = lerp(additive, replaced, _SpecReplace);
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
                half _ShimmerAmp;
                half _ShimmerSpeed;
                half _ShimmerPhase;
                half _ShimmerWave;
                half _ShimmerMode;
                half4 _DirWobble;
                half _NormalMode;
                half _NormalStrength;
                half _DiffNormalStrength;
                half _NormalFreq;
                float4 _NormalUVRect;
                float4 _NormalTexST;
                float _SortingLayerBit;
            CBUFFER_END

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;

                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                // Stock normals output (see Normals2DCommon.hlsl) with the relief deepened by
                // _DiffNormalStrength: scaling the tangent-space XY relative to Z tilts the
                // normals further off-flat BEFORE they land in the 2D light buffer, so every
                // Light2D (multiply included) shades this sprite with exaggerated bumps.
                const half4 mainTex = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));
                normalTS.xy *= _DiffNormalStrength;
                normalTS = normalize(normalTS);
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
                half _ShimmerAmp;
                half _ShimmerSpeed;
                half _ShimmerPhase;
                half _ShimmerWave;
                half _ShimmerMode;
                half4 _DirWobble;
                half _NormalMode;
                half _NormalStrength;
                half _DiffNormalStrength;
                half _NormalFreq;
                float4 _NormalUVRect;
                float4 _NormalTexST;
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
