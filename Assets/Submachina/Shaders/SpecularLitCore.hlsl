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

// Perceptual luminance of the albedo at an explicit mip, used as the height field by mode 9.
// Sampling a coarse LOD is how the blur is done: the hardware's mip chain is an already-
// filtered box blur, far cheaper than widening the tap kernel.
// REQUIRES MIPMAPS on the texture. Sprites frequently ship with "Generate Mip Maps" OFF, in
// which case every LOD returns the base level and the blur silently does nothing.
half SampleAlbedoHeight(float2 uv, float lod)
{
    half3 rgb = (half3)SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, lod).rgb;
    return dot(rgb, half3(0.299h, 0.587h, 0.114h));
}

// One central-difference gradient of the height field, at a given tap radius and mip.
// Returns the SLOPE, i.e. the negated gradient — a normal tilts away from rising height.
half2 AlbedoHeightGradient(float2 uv, float2 e, float lod)
{
    half hL = SampleAlbedoHeight(uv - float2(e.x, 0), lod);
    half hR = SampleAlbedoHeight(uv + float2(e.x, 0), lod);
    half hD = SampleAlbedoHeight(uv - float2(0, e.y), lod);
    half hU = SampleAlbedoHeight(uv + float2(0, e.y), lod);
    return half2(hL - hR, hD - hU);
}

// Surface normal derived from the ALBEDO treated as a height map — the trick Laigter and
// Material Maker use when there's no authored normal map. Central-difference the luminance
// over a texel-space neighbourhood to get the height gradient, then tilt the normal by it:
// n = normalize(-dh/du, -dh/dv, 1), which is exactly the tangent-space normal convention.
//
// Cost: four taps of a texture the shader is ALREADY sampling, one or two texels from the
// fetch it already did, so they land on the same cache lines. Cheap in practice — the thing
// to watch is overdraw (this is a transparent-queue shader), not the taps themselves.
//
// The radius is in TEXELS, not screen pixels, so the derived relief is stable under camera
// zoom and matches what an offline baker would produce from the same texture.
//
// INHERENT LIMITATION (true of every tool that does this, not of this implementation):
// albedo is not height. Dark PAINT on a flat surface becomes a dent and light paint becomes
// a bump, because the technique cannot separate pigment from shape. It reads well on
// textures whose value variation IS the relief — rock, bark, rubble, corrosion — and badly
// on flat-lit graphic art or anything with strong albedo-only markings.
half3 AlbedoHeightNormal(float2 uv)
{
    // Tap offset in UV. _HeightTexel.xy is (1/width, 1/height) of the source texture, written
    // by the driver (SpecularController fills it from the sprite). It is NOT Unity's magic
    // _MainTex_TexelSize: that name makes the 2D SRP Batcher drop the whole material.
    //
    // Floored at one screen pixel's worth of UV. That covers both failure modes: an unset
    // _HeightTexel (falls back to pixel-sized taps, which is right at 1:1 density), and heavy
    // MAGNIFICATION, where texel-sized taps would land inside one texel and flatten the relief.
    float2 px = (abs(ddx(uv)) + abs(ddy(uv))) * 0.5;
    float2 e0 = max(_HeightTexel.xy, px) * max(_HeightRadius, 0.01h);

    // BROAD pass — the fix for a gritty, over-detailed result. Sample a coarse mip with a
    // proportionally wider radius (both double per mip level) so the kernel keeps covering
    // the same texture area: raising the blur REMOVES fine structure rather than just
    // softening it in place, which is what separates form from noise.
    float lod = _HeightBlur;
    half2 g = AlbedoHeightGradient(uv, e0 * exp2(lod), lod);

    // FINE pass — the crisp LOD-0 gradient mixed back over the broad form.
    // 0 = pure smooth form, 1 = all of the texture's detail. Skipped entirely when there's
    // no blur to mix against, saving four taps (uniform branch, so the whole draw agrees).
    if (lod > 0.001h && _HeightDetail > 0.001h)
        g = lerp(g, AlbedoHeightGradient(uv, e0, 0.0), _HeightDetail);

    // A negative _HeightStrength inverts the relief — dark reads as high instead of low.
    g *= _HeightStrength;

    // Soft-knee compression on the slope magnitude. Hard albedo edges — outlines, paint
    // boundaries, the "detail that shouldn't be there" — produce extreme gradients that read
    // as cliffs, while gentle shading (the part that IS shape) produces small ones. Dividing
    // by (1 + |g|k) pulls the extremes toward a ceiling and leaves the gentle end near-linear,
    // so it suppresses exactly the wrong detail instead of flattening everything.
    if (_HeightCompress > 0.0h)
        g /= 1.0h + length(g) * _HeightCompress;

    return normalize(half3(g, 1.0h));
}

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

// ---------------------------------------------------------------------------------
// FORM SHAPE — a broad procedural 3D form (dome / bevel / pillow / cylinder / slope /
// silhouette-inflate) composited UNDER the detail normal, so a flat sprite reads as a
// raised solid while the detail relief (sprite map, override texture, albedo-height)
// still supplies the surface texture. Form and detail are combined with Reoriented
// Normal Mapping — the detail bumps are rotated onto the form's surface, which keeps
// both readable instead of one washing the other out the way adding/averaging does.
// ---------------------------------------------------------------------------------

// Reoriented Normal Mapping (Barré-Brisebois & Hill). Treats `nForm` as the base
// surface and re-expresses `nDetail` in its tangent frame. Flat detail returns the
// form exactly; flat form returns the detail exactly.
half3 BlendNormalsRNM(half3 nForm, half3 nDetail)
{
    half3 t = nForm + half3(0.0h, 0.0h, 1.0h);
    half3 u = nDetail * half3(-1.0h, -1.0h, 1.0h);
    return normalize(t * dot(t, u) / max(t.z, 1e-3h) - u);
}

// Slope magnitude of the form's height profile at normalized distance d (0 = shape
// centre, 1 = its outer edge). Two dials morph the whole family of solids:
//   _ShapeRim     — where the slope starts. 0 = curvature from the very centre (dome);
//                   0.6+ = flat plateau with a narrow shoulder (bevel / pillow edge).
//   _ShapeProfile — slope curve outside the rim. 0 = constant slope (linear bevel /
//                   cone), ~1 = parabolic dome, 2+ = slope packed at the outer edge
//                   (the round "inflated cushion" shoulder).
// _ShapeHeight scales the result — the overall steepness/depth of the form.
half FormProfileSlope(half d)
{
    half rim = saturate(_ShapeRim);
    half t = saturate((d - rim) / max(1.0h - rim, 1e-3h));
    half s = _ShapeHeight * pow(max(t, 1e-4h), max(_ShapeProfile, 0.0h));
    // Past the footprint (d > 1, reachable when _ShapeExtent shrinks the shape inside
    // the rect) the surface eases back to flat instead of freezing at maximum tilt.
    return s * (1.0h - smoothstep(1.0h, 1.15h, d));
}

// FORM NORMAL for rect-based sprites: `p` is the rect-local 0..1 coordinate (atlas
// remap already applied), `uv` the raw texture UV (silhouette mode only). Modes:
//   1 Shape      — round↔rectangular footprint (_ShapeRect): the dome/bevel/pillow family
//   2 Cylinder   — curved across ONE axis (aimed by _ShapeAngle): pipes, ridges, hulls
//   3 Slope      — a ramp along one axis: wedges, tilted panels
//   4 Silhouette — mip-blurred ALPHA as the height field, so the sprite puffs up from
//                  its own outline whatever its shape ("inflate"). Needs mipmaps.
half3 ComputeFormNormal(float2 p, float2 uv)
{
    // Silhouette inflate: central-difference the blurred alpha, exactly like the
    // AlbedoHeight relief but on coverage instead of luminance. _ShapeBlur mip levels
    // of blur turn the hard outline into a wide ramp — the rounded inflated shoulder.
    if (_ShapeMode > 3.5h)
    {
        float2 px = (abs(ddx(uv)) + abs(ddy(uv))) * 0.5;
        float lod = _ShapeBlur;
        float2 e = max(_HeightTexel.xy, px) * exp2(lod);
        half aL = (half)SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv - float2(e.x, 0), lod).a;
        half aR = (half)SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv + float2(e.x, 0), lod).a;
        half aD = (half)SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv - float2(0, e.y), lod).a;
        half aU = (half)SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv + float2(0, e.y), lod).a;
        half2 g = half2(aL - aR, aD - aU) * _ShapeHeight;
        return normalize(half3(g, 1.0h));
    }

    // Shape frame: -1..1 local coords, rotated so cylinders/slopes can aim anywhere
    // (and the rectangular footprint can sit at an angle).
    float2 q = (p - 0.5) * 2.0;
    half sA = 0.0h, cA = 1.0h;
    sincos(_ShapeAngle, sA, cA);
    float2 qr = float2(q.x * cA + q.y * sA, -q.x * sA + q.y * cA);

    half d = 0.0h;                   // normalized distance through the shape (0 centre .. 1 edge)
    float2 ur = float2(1.0, 0.0);    // downhill (outward) direction in the shape frame
    if (_ShapeMode < 1.5h)
    {
        // Shape: blend the round distance field toward the rectangular (chebyshev) one.
        // The two gradients blend the same way so the downhill direction stays coherent.
        float r = length(qr);
        float m = max(abs(qr.x), abs(qr.y));
        float2 gRound = qr / max(r, 1e-4);
        float2 gRect = abs(qr.x) >= abs(qr.y) ? float2(sign(qr.x), 0) : float2(0, sign(qr.y));
        d = (half)lerp(r, m, _ShapeRect);
        ur = normalize(lerp(gRound, gRect, _ShapeRect) + float2(1e-5, 0));
    }
    else if (_ShapeMode < 2.5h)
    {
        // Cylinder: distance measured across the rotated X axis only.
        d = (half)abs(qr.x);
        ur = float2(qr.x >= 0.0 ? 1.0 : -1.0, 0.0);
    }
    else
    {
        // Slope: one continuous ramp along the rotated X axis (rim/profile still shape it —
        // rim 0 + profile 0 is a uniform tilt, raising profile curls the ramp).
        d = (half)saturate(qr.x * 0.5 + 0.5);
        ur = float2(1.0, 0.0);
    }

    // _ShapeExtent rescales the footprint: >1 pulls the form's edge INSIDE the rect
    // (sprites with transparent padding), <1 spreads it past the rect (a gentler cap).
    half s = FormProfileSlope(d * (half)_ShapeExtent);

    // Rotate the downhill direction back into UV space; the normal tilts outward.
    float2 u = float2(ur.x * cA - ur.y * sA, ur.x * sA + ur.y * cA);
    return normalize(half3((half2)u * s, 1.0h));
}

// FORM NORMAL for edge-banded meshes (spline fills): the baked edge distance IS the
// distance field, so the same rim/profile morphs dome an ARBITRARY outline — the whole
// piece reads as one raised slab/cushion with the tiled detail map riding on top.
// _ShapeExtent spans the band exactly like _EdgeWidth does (1 = the full baked band).
half3 ComputeFormNormalEdge(half2 outwardDir, float edgeDist)
{
    half t = 1.0h - (half)saturate(edgeDist / max(_ShapeExtent, 1e-3h));
    half s = FormProfileSlope(t);
    return normalize(half3(outwardDir * s, 1.0h));
}

// Compose the form UNDER the detail normal. _ShapeDetail dials how much of the detail
// relief survives on the form (1 = full detail riding the shape, 0 = the bare form).
half3 ComposeFormNormal(half3 nForm, half3 nDetail)
{
    return BlendNormalsRNM(nForm, ScaleRelief(nDetail, _ShapeDetail));
}

// BASE surface normal (strength NOT applied — callers scale relief via ScaleRelief so
// the specular depth and the emboss term can use the same map at different depths):
// either the sprite's normal map (mode 0), a procedural pattern generated from the
// sprite's local UV (modes 1..5), or a WORLD-SPACE procedural pattern (modes 7..8).
// Procedural normals only tilt in XY across the flat quad — enough to make lights
// glint with no authored texture. _NormalUVRect remaps atlas UVs to 0..1 so the
// UV-space patterns stay centered.
// `uvEff` reports the coordinate the normal actually VARIES OVER (post atlas-remap, post
// per-map tiling/offset, or world position for the world modes). The curvature terms need it
// to know how many UV units a step of one world unit covers — that ratio is what a tiling
// override changes, and without it the cavity gain drifts every time the fill is retiled.
half3 ComputeSurfaceNormal(float2 uv, float2 uvAlbedo, float2 wpos, out float2 uvEff)
{
    uvEff = uv; // every branch below overwrites this; seeded so no path can leave it unset
    // Mode 0: the sprite's own normal map. UnpackNormal decodes BOTH encodings:
    // Unity-imported "Normal map" textures (BC5/DXT5nm channel-packed, z rebuilt
    // from xy) and our straight-RGB baked maps (opaque alpha -> plain xy*2-1).
    // Same decoder as the NormalsRendering pass, so diffuse and specular agree.
    if (_NormalMode < 0.5h)
    {
        uvEff = uv;
        half3 n0 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv));
        return normalize(n0);
    }

    // Mode 9: derive the relief from the ALBEDO as if it were a height map. Tested BEFORE
    // the world modes below, whose `> 6.5` test would otherwise swallow it. Uses the albedo's
    // own UV (not the normal map's), since that's the texture being differentiated.
    if (_NormalMode > 8.5h)
    {
        uvEff = uvAlbedo;
        return AlbedoHeightNormal(uvAlbedo);
    }

    // Modes 7/8: WORLD-SPACE patterns — hashed/waved from the fragment's world
    // position instead of sprite UV, so they are continuous across stitched geometry
    // (SpriteShape fill + rotated edge sprites) with zero seams or per-segment resets.
    // Here _NormalFreq means repeats/cells PER WORLD UNIT.
    if (_NormalMode > 6.5h)
    {
        uvEff = wpos; // world patterns vary per world unit, so world position IS their UV
        float wfreq = max(_NormalFreq, 1e-4);
        float2 gw = float2(0.0, 0.0);
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
        uvEff = tp; // includes the override's own tiling, so curvature tracks it too
        half3 n6 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, tp));
        return normalize(n6);
    }

    uvEff = p; // procedural patterns vary over the rect-remapped local UV
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

// Everything the curvature terms (isotropic cavity + directional grooves) need, gathered
// ONCE per fragment. Derivatives must be taken outside the light loop — gradient
// instructions in its divergent control flow (all those `continue`s) are undefined.
struct CurvatureBasis
{
    float2 gnx, gny;   // screen-space gradients of the normal's x / y
    float2 duvx, duvy; // screen-space gradients of the normal's own UV — the J_uv columns
    float2 jw0, jw1;   // rows of J_world^-1: turns a world direction into a screen direction
    half   valid;      // 0 kills every curvature term (Facets modes, degenerate quad)
};

// Builds the basis. `nCurv` is the normal BEFORE any caller-side bending (the spline fill's
// edge bevel), because a bevel is a wide smooth ramp that would otherwise register as real
// curvature and paint a false cavity ring around every rim — on top of the _EdgeDarken
// already treating that area. `uvEff` is what ComputeSurfaceNormal reports the normal varies
// over, so per-object tiling/offset overrides are accounted for automatically.
CurvatureBasis BuildCurvatureBasis(half3 nCurv, float2 uvEff, float2 wpos)
{
    CurvatureBasis b;
    b.gnx = float2(ddx(nCurv.x), ddy(nCurv.x));
    b.gny = float2(ddx(nCurv.y), ddy(nCurv.y));
    b.duvx = ddx(uvEff);
    b.duvy = ddy(uvEff);

    // J_world = [ d(wpos)/d(screenX) | d(wpos)/d(screenY) ]; we want its inverse, which maps
    // a world direction back to the screen direction that travels along it.
    float2 dwx = ddx(wpos), dwy = ddy(wpos);
    float det = dwx.x * dwy.y - dwy.x * dwx.y;
    float inv = abs(det) > 1e-12 ? 1.0 / det : 0.0; // degenerate quad -> terms drop out
    b.jw0 = float2(dwy.y, -dwy.x) * inv;
    b.jw1 = float2(-dwx.y, dwx.x) * inv;

    // The Facets patterns (modes 5 and 7) are piecewise constant per hashed cell: their
    // derivative is 0 inside a cell and a spike at the boundary, so curvature would draw
    // cell OUTLINES rather than relief. Masked, not branched around — see the note above.
    bool facets = (_NormalMode > 4.5h && _NormalMode < 5.5h) || (_NormalMode > 6.5h && _NormalMode < 7.5h);
    b.valid = (facets || inv == 0.0) ? 0.0h : 1.0h;
    return b;
}

// Second derivative of the height field along a unit WORLD direction L (the Hessian's L-L
// component). POSITIVE inside any trough running ACROSS L, ~0 for one running ALONG it —
// the anisotropy of raking light — and positive in a pit, negative on a ridge, for any L.
//
// The normal's xy is the height gradient in UV SPACE (d(h)/d(u) = -n.x), while L is in world
// space, so the two have to be reconciled. Moving one world unit along L is a screen step of
// Ls = J_world^-1 L, which is a UV step of q = J_uv Ls. Writing the bilinear form in screen
// gradients, one factor of J_uv cancels and it collapses to:
//     d2h/dL2 = -( q . (A_screen Ls) )      where A_screen rows are grad_screen(n.x), (n.y)
// Verified: a valley h = u^2 (n.x = -2u) returns +2 along L = (1,0) and 0 along L = (0,1).
//
// Because q carries the UV-per-world ratio, the result is correct under per-object TILING
// (it scales as tiling^2, as the real curvature does) and invariant to camera ZOOM, sprite
// scale, and the D3D-vs-GL ddy sign convention. Offset, being a translation, cancels in
// every derivative and so never mattered.
half HeightCurvature(CurvatureBasis b, float2 Lworld)
{
    float2 Ls = float2(dot(b.jw0, Lworld), dot(b.jw1, Lworld)); // world dir -> screen dir
    float2 q  = b.duvx * Ls.x + b.duvy * Ls.y;                  // UV travelled per world unit
    return (half)(-(q.x * dot(b.gnx, Ls) + q.y * dot(b.gny, Ls))) * b.valid;
}

// Direction-INDEPENDENT cavity: the height field's Laplacian, i.e. the curvature summed over
// two orthogonal world axes. This is the same quantity a baker writes to a cavity/AO map,
// computed live. Returned pre-split as (pitDepth, ridgeHeight), both 0..1, so the dark and
// bright halves weight independently. A NEGATIVE _CavityScale swaps them — the fix for a
// normal map whose X/green channel is inverted relative to Unity's convention.
// Still per-2x2-pixel-quad, so slightly blocky under heavy magnification.
half2 ComputeCavity(CurvatureBasis b)
{
    half lap = HeightCurvature(b, float2(1, 0)) + HeightCurvature(b, float2(0, 1));
    half curv = (half)clamp(lap * _CavityScale, -1.0, 1.0); // clamped so one steep texel can't blow it out
    return half2(saturate(curv), saturate(-curv)); // curvature is positive in pits
}

// The full specular pipeline applied on top of the already-lit base color `c`:
// baseline glint + shimmer/wobble animation + the real-light Blinn-Phong loop +
// glow zone + relief emboss + the additive/screen/replace compose.
//   c        = the 2D-lit base color (CommonLitFragment result)
//   nBase    = surface normal at AUTHORED depth (ComputeSurfaceNormal, optionally
//              bent by the caller — e.g. the spline fill's edge bevel)
//   nCurv    = the same normal BEFORE any such caller-side bend, used only by the curvature
//              terms; pass nBase when the caller doesn't bend it
//   uvEff    = the coordinate the normal varies over (ComputeSurfaceNormal's out param), so
//              the curvature terms stay correct under per-object tiling overrides
//   texel    = the RAW vertex-tinted albedo texel (NOT the lit result), used by
//              the emboss relief and the albedo-tinted glint
//   specMask = the pre-sampled per-pixel specular mask (RGB tint × strength) —
//              callers with plain UVs can use the ApplySpecular wrapper below;
//              the spline-fill shader samples it itself (own UV transform + the
//              stamp-once window)
//   wpos     = fragment world position (real-light specular + world patterns)
half4 ApplySpecularMasked(half4 c, half3 nBase, half3 nCurv, half3 texel, half3 specMask,
                          float2 uvEff, float2 wpos)
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

    // --- Ambient relief: the UNGATED half of the lighting model ---
    // URP 2D shades normal maps strictly per-LIGHT and positionally (see
    // LightingUtility.hlsl: lightColor *= saturate(dot(dirToLight, normal)), which needs a
    // light POSITION — so a Global Light2D, having none, is a flat multiply that ignores
    // normals entirely). The per-light _NormalEmboss further down is gated by each light's
    // falloff/cone. Net effect: with no specular light on a surface, its relief vanishes.
    // These three terms are the fix, and none of them is light-gated:
    //   fill   = signed lambert against a fixed virtual "sun" — directional relief
    //            everywhere, the 2D stand-in for a 3D directional light.
    //   cavity = normal-field divergence — direction-INDEPENDENT depth (dark pits,
    //            bright ridge crests). Survives in total darkness.
    //   slope  = steeper pixels sit darker — a one-instruction always-on depth floor.
    // All read nBase (authored depth), so they stay in step with the emboss.

    // Virtual directional fill. Anchored the same way as the per-light emboss:
    // dot(n.xy, L.xy) + (n.z - 1) * L.z is exactly 0 for a flat normal (0,0,1), so
    // untextured pixels are never tinted or washed out. Z is clamped off zero so a
    // zeroed _AmbientDir can't produce a NaN out of normalize.
    half3 La = normalize(half3(_AmbientDir.xy, max(_AmbientDir.z, 1e-3h)));
    half fill = (dot(nBase.xy, La.xy) + (nBase.z - 1.0h) * La.z) * _AmbientFill;

    // Cavity (pits, ridges) + slope shading. pow's base is floored off zero because
    // pow(0, 0) is undefined — with _SlopeAO at 0 (the default) this still evaluates to
    // exactly 1, i.e. the term is naturally off.
    // The final occlusion multiplier is assembled AFTER the light loop (the lit-fade needs
    // the accumulated light gate), but every DERIVATIVE-based quantity is computed here,
    // outside the loop: gradient instructions inside its divergent control flow (all those
    // `continue`s) are undefined behaviour.
    CurvatureBasis curvBasis = BuildCurvatureBasis(nCurv, uvEff, wpos);
    half2 cav = ComputeCavity(curvBasis);
    half slope = pow(max(saturate(nBase.z), 1e-4h), _SlopeAO);
    half ridge = cav.y * _CavityRidge;

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
    half emboss = 0.0h;    // signed relief response (facing-light positive, facing-away negative)
    half dirGroove = 0.0h; // net groove occlusion along each light's direction (unsigned, >= 0)
    half gateSum = 0.0h;   // summed falloff*cone, used to normalise the two terms above
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

        // How much of this light lands here. Accumulated so the two relief terms below can
        // be normalised after the loop instead of stacking per light (see (c) further down).
        half gate = falloff * cone;
        gateSum += gate;

        // Relief emboss: signed lambert of the AUTHORED normal against this light,
        // anchored to the flat-normal response so untextured pixels contribute zero.
        // _EmbossElevation is how high this light sits above the surface plane — LOW is
        // grazing, which maximizes relief contrast and is what makes the result read as
        // shadow rather than as tinting. Gated by falloff/cone, so unlit areas stay untouched.
        if (_NormalEmboss > 0.0h)
        {
            half3 Ld = normalize(half3(-dir.x, -dir.y, max(_EmbossElevation, 1e-3h)));
            emboss += (dot(nBase.xy, Ld.xy) + (nBase.z - 1.0h) * Ld.z) * gate;
        }

        // Directional groove occlusion: net darkening in grooves running ACROSS this light's
        // direction, none in grooves running along it. Complements the emboss above — that
        // gives the bright-wall/dark-wall CONTRAST, this gives the net energy LOSS the
        // antisymmetric emboss cancels out to zero. Same falloff/cone gate. `dir` is used
        // unsigned (the term is quadratic in L), so light->surface vs surface->light is moot.
        if (_DirCavity > 0.0h)
            dirGroove += max(HeightCurvature(curvBasis, dir), 0.0h) * gate;
    }
    // Lit-gated modulation (modes 2/3): scale the light-driven glint so the shimmer is
    // additive to whatever illumination is present — zero light means zero shimmer.
    // (_LightResponse is already folded in per light above, under the clamp.)
    half lightMul = _ShimmerMode > 1.5h ? max(1.0h + m, 0.0h) : 1.0h;
    spec += lightSpec * lightMul;

    // --- Relief layering: normalise the per-light terms, then assemble the occlusion ---
    // Both per-light relief terms accumulate raw, so two overlapping beams used to double the
    // relief and blow out. Dividing by the summed gate, FLOORED AT 1, keeps a single light
    // bit-for-bit unchanged while making overlapping lights average instead of sum.
    half gateNorm = max(gateSum, 1.0h);
    half relief = (emboss / gateNorm) * _NormalEmboss;
    half groove = saturate((dirGroove / gateNorm) * _DirCavityScale) * _DirCavity;

    // The isotropic cavity is the AMBIENT occlusion term. Physically AO belongs to ambient
    // light while direct light should cast real shadows instead — which is what the emboss and
    // the groove term are for — so leaving both at full strength darkens a spotlit crevice
    // twice under two different models. _CavityLitFade dials how much direct light suppresses
    // the ambient one. 0 = the two simply stack (the behaviour before this term existed).
    half litFade = 1.0h - _CavityLitFade * saturate(gateSum);
    half ao = saturate(slope * (1.0h - cav.x * _CavityAmount * litFade));

    // Everything that darkens, in one multiplier: ambient cavity + slope + directional grooves.
    half occl = saturate(ao * (1.0h - groove));

    // Glow-zone HDR push: scales ALL specular (baseline + boost + light-driven) in
    // the bright-mask zone so crystal blobs climb past 1.0 and feed bloom, while
    // sub-threshold pixels keep their exact pre-glow value.
    spec *= lerp(1.0h, _GlowGain, glowW);

    // Occlusion on the GLINT: grit packed down into a crevice shouldn't sparkle, and gating
    // the specular does as much for the sense of depth as darkening the albedo. Kept on its
    // own dial so the two can be balanced independently (0 = glint ignores the occlusion,
    // 1 = fully occluded by it).
    spec *= lerp(1.0h, occl, _CavitySpec);

    // Fake "additive light" relief from the normal's signed per-light response.
    // The bright lobe ADDS albedo-coloured light (the one thing a multiply light
    // can't do — this is what makes relief pop in a dark multiply-lit scene); the
    // dark lobe multiplicatively shades whatever the scene lights left. Flat pixels
    // are untouched, unlit areas stay dark (emboss is falloff/cone gated per light).
    c.rgb += texel * max(relief, 0.0h) * c.a;     // facing the light: additive, like an additive Light2D
    c.rgb *= max(1.0h + min(relief, 0.0h), 0.0h); // facing away: multiplicative shading

    // Ambient relief, composed exactly like the emboss above — bright half ADDS
    // albedo-coloured light (the thing a multiply-only 2D light can never do), dark half
    // shades multiplicatively. The difference is that NONE of this is falloff/cone gated,
    // so it survives with no light on the surface at all, which is the whole point.
    half ambient = fill + ridge;
    c.rgb += texel * max(ambient, 0.0h) * c.a;     // sun-facing slopes + ridge crests
    c.rgb *= max(1.0h + min(ambient, 0.0h), 0.0h); // slopes turned away from the fill
    c.rgb *= occl;                                 // ambient cavity + slope + directional grooves

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
// (the sprite shader): samples _SpecMask at uv and runs the full pipeline. nCurv is
// the normal BEFORE the form-shape compose — a broad form is a wide smooth ramp that
// would register as false curvature (the same reason the spline fill excludes its
// edge bevel); pass nBase when the caller composes no form.
half4 ApplySpecular(half4 c, half3 nBase, half3 nCurv, half3 texel, float2 uv, float2 uvEff, float2 wpos)
{
    half3 specMask = (half3)SAMPLE_TEXTURE2D(_SpecMask, sampler_SpecMask, uv).rgb;
    return ApplySpecularMasked(c, nBase, nCurv, texel, specMask, uvEff, wpos);
}

#endif // SUBMACHINA_SPECULAR_LIT_CORE_INCLUDED
