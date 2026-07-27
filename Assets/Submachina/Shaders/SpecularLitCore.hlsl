// Shared specular core for the Submachina 2D specular shaders:
//   - SpriteLitSpecular.shader      (SpriteRenderers / SpriteShapeRenderers)
//   - SplineFillLitSpecular.shader  (generated spline-fill MeshRenderers)
//
// Holds everything that is identical between them: the global specular-light
// uniforms published by SpecularLight2DManager, the procedural/texture surface
// normal, and the full Blinn-Phong glint pipeline (baseline + shimmer + real
// lights + glow zone + emboss + compose).
//
// Contract for the including pass:
//   - Include AFTER the URP 2D common includes (Lit2DCommon.hlsl) so
//     _NormalMap/sampler_NormalMap and _MainTex are already declared.
//   - Include AFTER the UnityPerMaterial CBUFFER — the functions here reference
//     the material properties (_SpecPower, _NormalMode, ...) by name, and every
//     pass must declare the identical CBUFFER layout for the SRP Batcher.
//   - The globals declared HERE live outside UnityPerMaterial, so the SRP
//     Batcher is unaffected.
#ifndef SUBMACHINA_SPECULAR_LIT_CORE_INCLUDED
#define SUBMACHINA_SPECULAR_LIT_CORE_INCLUDED

// ---- Global specular lights (packed each frame by SpecularLight2DManager) ----
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

// Per-pixel specular mask (Secondary Texture "_SpecMask" on sprites, or a material
// texture on mesh fills — RGB tint × strength). Lives outside UnityPerMaterial.
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

// Deepen/flatten a normal's relief: scaling XY relative to Z tilts it further
// off-flat (1 = as sampled, >1 = deeper, <1 = flatter, 0 = flat).
half3 ScaleRelief(half3 n, half s)
{
    return normalize(half3(n.xy * s, n.z));
}

// BASE surface normal (strength NOT applied — callers scale relief via ScaleRelief so
// the specular depth and the emboss term can use the same map at different depths):
// either the sprite's normal map (mode 0), a procedural pattern generated from the
// sprite's local UV (modes 1..5), or a WORLD-SPACE procedural pattern (modes 7..8).
// Procedural normals only tilt in XY across the flat quad — enough to make lights
// glint with no authored texture. _NormalUVRect remaps atlas UVs to 0..1 so the
// UV-space patterns stay centered.
half3 ComputeSurfaceNormal(float2 uv, float2 wpos)
{
    // Mode 0: the sprite's own normal map. UnpackNormal decodes BOTH encodings:
    // Unity-imported "Normal map" textures (BC5/DXT5nm channel-packed, z rebuilt
    // from xy) and our straight-RGB baked maps (opaque alpha -> plain xy*2-1).
    // Same decoder as the NormalsRendering pass, so diffuse and specular agree.
    if (_NormalMode < 0.5h)
    {
        half3 n0 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv));
        return normalize(n0);
    }

    // Modes 7/8: WORLD-SPACE patterns — hashed/waved from the fragment's world
    // position instead of sprite UV, so they are continuous across stitched geometry
    // (SpriteShape fill + rotated edge sprites) with zero seams or per-segment resets.
    // Here _NormalFreq means repeats/cells PER WORLD UNIT.
    if (_NormalMode > 6.5h)
    {
        float wfreq = max(_NormalFreq, 1e-4);
        float2 gw;
        if (_NormalMode < 7.5h)      // World Facets: random tilt per hashed world cell (sparkle)
            gw = Hash22(floor(wpos * wfreq)) * 2.0 - 1.0;
        else                         // World Ripples: parallel wavy bands along world X
            gw = float2(cos(wpos.x * wfreq * 6.2831853), 0.0);
        return normalize(half3((half2)gw, 1.0h));
    }

    // Remap the sprite's atlas UV to 0..1 across its own rect (used by the override
    // texture and every procedural pattern so they stay centered on atlased sprites).
    float2 p = (uv - _NormalUVRect.xy) / max(_NormalUVRect.zw, 1e-5);

    // Mode 6: explicit override texture (UnpackNormal — accepts Unity-imported
    // normal maps or straight-RGB baked ones, like mode 0), sampled in local UV.
    // Apply per-instance tiling/offset so the map can be scaled and positioned
    // to fit the sprite (tiling <1 enlarges the stamp; offset slides it around).
    if (_NormalMode > 5.5h)
    {
        float2 tp = p * _NormalTexST.xy + _NormalTexST.zw;
        half3 n6 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, tp));
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

    return normalize(half3((half2)g, 1.0h));
}

// The full specular pipeline applied on top of the already-lit base color `c`:
// baseline glint + shimmer/wobble animation + the real-light Blinn-Phong loop +
// glow zone + relief emboss + the additive/screen/replace compose.
//   c        = the 2D-lit base color (CommonLitFragment result)
//   nBase    = surface normal at AUTHORED depth (ComputeSurfaceNormal, optionally
//              bent by the caller — e.g. the spline fill's edge bevel)
//   texel    = the RAW vertex-tinted albedo texel (NOT the lit result), used by
//              the emboss relief and the albedo-tinted glint
//   specMask = the pre-sampled per-pixel specular mask (RGB tint × strength) —
//              callers with plain UVs can use the ApplySpecular wrapper below;
//              the spline-fill shader samples it itself (own UV transform + the
//              stamp-once window)
//   wpos     = fragment world position (real-light specular + world patterns)
half4 ApplySpecularMasked(half4 c, half3 nBase, half3 texel, half3 specMask, float2 wpos)
{
    // The specular gets its own _NormalStrength-scaled copy of the relief; the
    // emboss term below keeps the authored-depth nBase.
    half3 n = ScaleRelief(nBase, _NormalStrength);

    // --- Glow zone: threshold-gate the mask's brightness into a 0..1 weight ---
    // Below the threshold a pixel keeps the normal specular set (directional
    // metallic sparkle); above it (bright blobs marking crystals) the parameters
    // blend toward the Glow set: omnidirectional bias, wider lobe, HDR gain, and
    // (at compose time) pure additive so the glow survives to bloom. The default
    // threshold of 2 is unreachable by a 0..1 mask, so the zone is inert.
    half maskVal = max(specMask.r, max(specMask.g, specMask.b));
    half glowW = smoothstep(_GlowThreshold - _GlowKnee, _GlowThreshold + _GlowKnee, maskVal);

    // Per-pixel parameter blend — the baseline glint and light loop run ONCE with
    // these locals, so the glow zone costs two lerps, not a second Blinn-Phong pass.
    half viewBias = lerp(_SpecViewBias, _GlowViewBias, glowW);
    half specPow  = lerp(_SpecPower,    _GlowPower,    glowW);

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
    // The viewer lean comes from viewBias (the _SpecViewBias/_GlowViewBias blend, shared
    // with the light-driven glint below) so the glow zone affects ALL the glints alike.
    half3 Lb = normalize(half3(_SpecLightDir.xy, viewBias));
    Lb.xy = half2(Lb.x * cw - Lb.y * sw, Lb.x * sw + Lb.y * cw); // direction wobble
    half3 Hb = normalize(Lb + V);
    half baseShape = pow(saturate(dot(n, Hb)), specPow);

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
    half emboss = 0.0h; // signed relief response (facing-light positive, facing-away negative)
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
        float2 toFrag = wpos - lightPos;
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
        // biased toward the viewer on Z (viewBias — the per-pixel _SpecViewBias/
        // _GlowViewBias blend) so grazing beams still pop a highlight. Low bias =
        // strictly directional facets; high bias = the glint fires from most light
        // directions (omnidirectional glow, e.g. crystals). The direction wobble
        // rotates only this specular dir — the cone gate and falloff above stay
        // physical so the beam itself doesn't wander.
        float2 sdir = float2(dir.x * cw - dir.y * sw, dir.x * sw + dir.y * cw);
        half3 L = normalize(half3(-sdir.x, -sdir.y, viewBias));
        half3 H = normalize(L + V);
        half s = pow(saturate(dot(n, H)), specPow);

        // Optional ceiling on this light's PEAK glint (strength × response), applied
        // BEFORE the falloff/cone shape it. Clamping the final value instead flattened
        // the whole falloff gradient to the cap — a hard on/off line at the reach.
        half g = s * strength * _LightResponse;
        if (_SpecClamp > 0.0h) g = min(g, _SpecClamp);

        lightSpec += g * falloff * cone;

        // Relief emboss: signed lambert of the AUTHORED normal against this light,
        // anchored to the flat-normal response so untextured pixels contribute zero.
        // A grazing light vector (low Z) maximizes the relief contrast. Gated by the
        // same falloff/cone, so unlit areas stay untouched. Applied after the loop.
        if (_NormalEmboss > 0.0h)
        {
            half3 Ld = normalize(half3(-dir.x, -dir.y, 0.5h));
            emboss += (dot(nBase.xy, Ld.xy) + (nBase.z - 1.0h) * Ld.z) * falloff * cone;
        }
    }
    // Lit-gated modulation (modes 2/3): scale the light-driven glint so the shimmer is
    // additive to whatever illumination is present — zero light means zero shimmer.
    // (_LightResponse is already folded in per light above, under the clamp.)
    half lightMul = _ShimmerMode > 1.5h ? max(1.0h + m, 0.0h) : 1.0h;
    spec += lightSpec * lightMul;

    // Glow-zone HDR push: scales ALL specular (baseline + boost + light-driven) in
    // the bright-mask zone so crystal blobs climb past 1.0 and feed bloom, while
    // sub-threshold pixels keep their exact pre-glow value.
    spec *= lerp(1.0h, _GlowGain, glowW);

    // Fake "additive light" relief from the normal's signed per-light response.
    // The bright lobe ADDS albedo-coloured light (the one thing a multiply light
    // can't do — this is what makes relief pop in a dark multiply-lit scene); the
    // dark lobe multiplicatively shades whatever the scene lights left. Flat pixels
    // are untouched, unlit areas stay dark (emboss is falloff/cone gated per light).
    half relief = emboss * _NormalEmboss;
    c.rgb += texel * max(relief, 0.0h) * c.a;     // facing the light: additive, like an additive Light2D
    c.rgb *= max(1.0h + min(relief, 0.0h), 0.0h); // facing away: multiplicative shading

    // (specMask was sampled before the light loop — its RGB tints AND scales all
    // specular below: baked crystals carry their own glint colour at ~full strength,
    // dull rock a dim grey. Composes with _SpecColor; white default = unchanged.)

    // Albedo tint (metallic feel): pull the glint colour toward the sprite's own
    // vertex-tinted texel so texture detail reads through the highlight — dark
    // cracks stay dark, an amethyst texel flares purple. Uses the RAW texel (not
    // the lit result) so scene darkness doesn't double-dim the glint.
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
    // In the glow zone the compose is forced toward pure additive: the screen-blend
    // ceiling (which deliberately caps the glint below white — exactly what suppresses
    // bloom) and the replace blend both fade out with glowW, so the metallic body keeps
    // its contrast-preserving compose while the hotspots emit true HDR.
    half screen  = _SpecScreen  * (1.0h - glowW);
    half replace = _SpecReplace * (1.0h - glowW);
    half3 specColor = _SpecColor.rgb * specMask * albedoTint;
    half cover = spec * c.a;
    half3 glint = specColor * cover;
    half3 soft = glint / (1.0h + glint); // Reinhard: HDR glint compressed to <1
    half3 additive = c.rgb + lerp(glint, soft * saturate(1.0h - c.rgb), screen);
    half3 replaced = lerp(c.rgb, specColor, saturate(cover));
    c.rgb = lerp(additive, replaced, replace);
    return c;
}

// Convenience wrapper for callers whose spec mask simply shares the main UV
// (the sprite shader): samples _SpecMask at uv and runs the full pipeline.
half4 ApplySpecular(half4 c, half3 nBase, half3 texel, float2 uv, float2 wpos)
{
    half3 specMask = (half3)SAMPLE_TEXTURE2D(_SpecMask, sampler_SpecMask, uv).rgb;
    return ApplySpecularMasked(c, nBase, texel, specMask, wpos);
}

#endif // SUBMACHINA_SPECULAR_LIT_CORE_INCLUDED
