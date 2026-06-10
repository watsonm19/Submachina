// Fullscreen underwater effect for the URP 2D Renderer — built for a SIDE-VIEW,
// submerged ("looking through the water") perspective rather than a top-down surface.
//
// Driven by URP's built-in "Full Screen Pass Renderer Feature": the feature binds the
// rendered scene as _BlitTexture and runs this pass over a fullscreen triangle. Most
// tunables arrive as GLOBAL uniforms set from C# (UnderwaterDistortionController); the
// sampled noise/caustic textures are material properties so they show in the inspector.
//
// The fragment shader does four things, in order:
//   1. DISTORT   — build a UV displacement from ambient flow + ripples + propulsion wakes,
//                  then re-sample the scene with a chromatic (light-bending) split.
//   2. LIGHT     — add artificial underwater light: god-ray shafts from the surface and
//                  animated caustic shimmer (luminance-masked so it lands on objects).
//   3. PARTICLES — procedural parallax "marine snow" mote layers + rising bubbles, all
//                  world-anchored so they sell the sense of travelling through the water.
//   4. TINT      — optional subtle depth tint.
Shader "Submachina/UnderwaterDistortion"
{
    Properties
    {
        // Tiling noise for the textured ambient mode and the wake/god-ray turbulence.
        // Any seamless grayscale noise works (e.g. Feel's MMPerlinNoise/MMCloudsNoise).
        [NoScaleOffset] _UD_NoiseTex   ("Ambient Noise (tiling)", 2D) = "gray" {}
        // Cell/voronoi texture for caustics (e.g. Feel's MMVoronoiNoise/MMCellNoise).
        [NoScaleOffset] _UD_CausticTex ("Caustics (cell/voronoi)", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "UnderwaterDistortion"

            HLSLPROGRAM
            // Core gives us the SRP plumbing; Blit gives us Vert/Varyings, _BlitTexture
            // and the clamp samplers used by every URP fullscreen pass.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert        // fullscreen-triangle vertex shader from Blit.hlsl
            #pragma fragment Frag

            #define UD_TAU 6.28318530718

            // ─── Global uniforms (set by UnderwaterDistortionController) ─────────
            float  _UD_Time;            // animation clock (play time, or edit-mode override)
            float4 _UD_FlowParams;      // x=ambientAmplitude y=ambientScale z=ambientSpeed w=globalEnable
            float4 _UD_FlowParams2;     // x=noiseBlend(0..1) y=noiseScale z=noiseSpeed w=unused
            float2 _UD_Aspect;          // (camera.aspect, 1) — keeps rings/cells circular

            float  _UD_Chromatic;       // light-bending RGB split, scaled by displacement

            float4 _UD_GodRayParams;    // x=intensity y=density z=sharpness w=driftSpeed
            float4 _UD_GodRayParams2;   // x=swayAmount y=shimmerAmount z=distortCouple
            float4 _UD_GodRayDir;       // xy = ray travel direction (screen space, downward-ish)
            float4 _UD_GodRayTint;      // rgb tint of the shafts

            float4 _UD_CausticParams;   // x=intensity y=scale z=speed w=sharpness
            float4 _UD_CausticParams2;  // x=warpAmount y=distortCouple
            float4 _UD_CausticTint;     // rgb tint of the caustics
            float4 _UD_CausticMask;     // x=lumaMaskStrength y=openWaterFloor

            float4 _UD_DeepTint;        // rgb deep-water tint, w=strength

            // World anchoring — pins the ambient patterns to the WORLD so they scroll past
            // as the camera travels (the "you are actually moving" cue).
            float4 _UD_WorldOffset;     // xy = camera world position in viewport-height units (aspect-corrected space)
            float4 _UD_WorldAnchor;     // x=ambientAnchor y=causticAnchor z=godRayAnchor (0=screen-locked, 1=world-locked, between=parallax)

            // Particles — marine snow (parallax mote layers) and rising bubbles.
            float4 _UD_MoteParams;      // x=intensity y=cellScale z=sizeMul w=twinkle
            float4 _UD_MoteParams2;     // x=farAnchor y=nearAnchor z=density w=godRayBoost
            float4 _UD_MoteTint;        // rgb tint of the motes
            float4 _UD_BubbleParams;    // x=intensity y=cellScale z=sizeMul w=wobble
            float4 _UD_BubbleParams2;   // x=anchorBase y=density
            float4 _UD_BubbleTint;      // rgb tint of the bubbles
            float4 _UD_ParticleDrift;   // xy=mote world drift (current/sink) z=bubble sideways drift w=bubble rise speed

            // Tiling source textures (material properties, declared above).
            TEXTURE2D(_UD_NoiseTex);    SAMPLER(sampler_UD_NoiseTex);
            TEXTURE2D(_UD_CausticTex);  SAMPLER(sampler_UD_CausticTex);

            // Ripple pool — concentric expanding rings (kept from the surface-ripple system).
            #define UD_MAX_RIPPLES 16
            float4 _UD_RippleA[UD_MAX_RIPPLES];   // xy=centerUV z=currentRadius w=currentAmplitude
            float4 _UD_RippleB[UD_MAX_RIPPLES];   // x=waveFrequency y=ringFalloff z=phase w=active
            int    _UD_RippleCount;

            // Wake pool — elongated turbulence trails behind propulsion sources.
            #define UD_MAX_WAKES 8
            float4 _UD_WakeA[UD_MAX_WAKES];       // xy=centerUV z=strength w=halfLength
            float4 _UD_WakeB[UD_MAX_WAKES];       // xy=dir(aspect-corrected unit) z=frequency w=active
            int    _UD_WakeCount;

            // ─── Distortion sources (return a UV offset) ────────────────────────

            /**
             * Whole-screen ambient undulation. Two crossed sine layers blended with an
             * optional scrolling noise texture (sampled at two offsets so a grayscale map
             * still warps in both axes). noiseBlend 0 = pure procedural.
             */
            float2 AmbientFlow(float2 uv)
            {
                // World-anchor: shift the sample coords by the camera offset so the flow
                // pattern sticks to the world and scrolls past as the camera travels.
                // (Offset arrives in aspect-corrected units, so x converts back to raw uv.)
                float2 wuv = uv + _UD_WorldOffset.xy * _UD_WorldAnchor.x * float2(1.0 / _UD_Aspect.x, 1.0);

                float t = _UD_Time * _UD_FlowParams.z;
                float s = _UD_FlowParams.y;
                float2 proc;
                proc.x = sin(wuv.y * s * UD_TAU + t)        + 0.5 * sin(wuv.y * s * 13.0 - t * 1.3);
                proc.y = cos(wuv.x * s * UD_TAU - t * 0.9)  + 0.5 * cos(wuv.x * s * 11.0 + t * 1.1);

                float  ns = _UD_FlowParams2.y;
                float  nt = _UD_Time * _UD_FlowParams2.z;
                float  nx = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, wuv * ns + float2( 0.10,  0.13) * nt).r;
                float  ny = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, wuv * ns + float2(-0.13,  0.07) * nt + 0.5).r;
                float2 tex = float2(nx, ny) * 2.0 - 1.0;

                return lerp(proc, tex, saturate(_UD_FlowParams2.x)) * _UD_FlowParams.x;
            }

            /**
             * Concentric expanding ripples (surface-style). Each is a Gaussian ring around
             * its growing radius, oscillating and pushing radially outward.
             */
            float2 RippleFlow(float2 uv)
            {
                float2 disp = 0;
                [loop]
                for (int i = 0; i < _UD_RippleCount; i++)
                {
                    float4 a = _UD_RippleA[i];
                    float4 b = _UD_RippleB[i];
                    if (b.w < 0.5) continue;

                    float2 d    = (uv - a.xy) * _UD_Aspect;
                    float  dist = length(d);
                    float  rr   = (dist - a.z) / max(b.y, 1e-4);     // signed distance from the ring
                    float  ring = exp(-rr * rr);                     // Gaussian band (x*x avoids pow(neg,2))
                    float  wave = sin(dist * b.x * UD_TAU - b.z);
                    float2 dir  = d / max(dist, 1e-4);
                    disp += dir * wave * ring * a.w;
                }
                return disp;
            }

            /**
             * Propulsion wakes — an elongated turbulence plume trailing each source along its
             * travel direction. Inside an aspect-corrected ellipse (offset to sit BEHIND the
             * source) we add high-frequency noise that scrolls backward, reading as churned,
             * light-bending water rather than a clean ring.
             */
            float2 WakeFlow(float2 uv)
            {
                float2 disp = 0;
                [loop]
                for (int i = 0; i < _UD_WakeCount; i++)
                {
                    float4 a = _UD_WakeA[i];
                    float4 b = _UD_WakeB[i];
                    if (b.w < 0.5) continue;

                    // Local frame: along = travel axis, side = perpendicular.
                    float2 dir  = b.xy;
                    float2 perp = float2(-dir.y, dir.x);
                    float2 d    = (uv - a.xy) * _UD_Aspect;
                    float  along = dot(d, dir);
                    float  side  = dot(d, perp);

                    // Elliptical envelope centered one half-length BEHIND the source so the
                    // plume trails it; tighter across (side) than along the travel axis.
                    float halfLen = max(a.w, 1e-3);
                    float al  = (along + halfLen) / halfLen;
                    float ss  = side / (halfLen * 0.35);             // normalized perpendicular distance
                    float env = exp(-al * al) * exp(-ss * ss);       // elliptical falloff (x*x avoids pow(neg,2))

                    // Backward-scrolling high-frequency turbulence.
                    float2 nUv  = uv * b.z - dir * (_UD_Time * 1.5);
                    float  n1   = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, nUv).r;
                    float  n2   = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, nUv * 1.9 + 0.37).r;
                    float2 turb = float2(n1, n2) * 2.0 - 1.0;

                    disp += turb * env * a.z;
                }
                return disp;
            }

            // ─── Artificial light (return additive intensity) ───────────────────

            // Cheap 1-D smooth value noise — used to place irregular god-ray beams. Kept 1-D
            // (and procedural) on purpose so the beams never inherit a sampled texture's
            // directional streaks, which is what made the textured version band horizontally.
            float UD_Hash1(float n) { return frac(sin(n) * 43758.5453); }
            float UD_Noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(UD_Hash1(i), UD_Hash1(i + 1.0), f);
            }

            /**
             * Procedural god-ray shafts, brightening toward the light-entry edge (the surface).
             * Built entirely from 1-D noise of the ACROSS-ray coordinate so the beams are
             * crisp vertical columns regardless of any texture. Layers of life:
             *   - SWAY: the across coordinate is offset by a slow function of depth, so beams
             *     wave from side to side as they descend;
             *   - two octaves of irregular beam placement, slowly drifting;
             *   - per-beam TWINKLE and a dappled SHIMMER whose phase is keyed to the beam
             *     coordinate, so brightness varies inside each beam but never lines up into
             *     coherent horizontal bands across beams;
             *   - coupling to the scene displacement so they bend with the water.
             */
            float GodRays(float2 uv, float2 disp)
            {
                float2 dir   = normalize(_UD_GodRayDir.xy + float2(1e-5, -1e-5));
                float2 perp  = float2(-dir.y, dir.x);

                // Bend the ray field by the water distortion, then split into along/across.
                float2 p     = uv + disp * _UD_GodRayParams2.z;
                float  across = dot(p, perp);
                float  along  = dot(p, dir);

                // World-anchor the beam pattern: sideways travel scrolls the beams past,
                // descending crawls the shimmer upward. The entry gradient below stays
                // screen-space (light always enters from the top of the view).
                float2 wo = _UD_WorldOffset.xy * _UD_WorldAnchor.z * float2(1.0 / _UD_Aspect.x, 1.0);
                across += dot(wo, perp);
                along  += dot(wo, dir);

                float density = _UD_GodRayParams.y;
                float t       = _UD_Time * _UD_GodRayParams.w;

                // Sway: wave the beams horizontally as a slow function of depth.
                across += (UD_Noise1(along * 2.0 - t * 0.6) - 0.5) * _UD_GodRayParams2.x;

                // Two octaves of irregular vertical beams, slowly drifting across. The
                // contrast stretch lifts the smooth-noise cores so beams actually read, then
                // the (gentler) sharpness exponent thins them.
                float c     = across * density;
                float beams = saturate(UD_Noise1(c + t * 0.3) * 0.65 + UD_Noise1(c * 2.3 + 11.0 - t * 0.5) * 0.45);
                beams       = smoothstep(0.30, 0.90, beams);
                float shaft = pow(beams, max(1.0, _UD_GodRayParams.z * 0.5));

                // Per-beam twinkle + lengthwise dapple. Both are PHASE-KEYED to the beam coord
                // c, so neighbouring beams flicker out of step — no screen-wide horizontal bands.
                float twinkle = 0.65 + 0.35 * sin(c * 3.1 + t * 1.7);
                float dapple  = lerp(1.0, 0.5 + 0.5 * sin(along * 9.0 + c * 5.0 - t * 3.0),
                                     saturate(_UD_GodRayParams2.y));

                float entry = saturate(dot(p, -dir));       // brighter toward the surface side
                return shaft * twinkle * dapple * entry;
            }

            /**
             * Caustic web. Two scrolling layers of the cell/voronoi texture combined with
             * min() (the classic caustic trick), then sharpened. To avoid the "flat decal
             * sliding across the screen" look, the sample coordinates are (a) bent by the
             * scene displacement `disp` so the caustics ride the same wobble as everything
             * else, and (b) pushed through an animated low-frequency DOMAIN WARP so the web
             * morphs/pulses in place instead of translating rigidly. Each layer is warped
             * slightly differently, so their min() intersection shimmers and crawls.
             */
            float Caustics(float2 uv, float2 disp)
            {
                // Aspect-correct base coords, coupled to the water distortion, then shifted
                // by the world-anchor offset so the web is pinned to the world (the camera
                // travels THROUGH the caustic field instead of carrying it along).
                float2 base = (uv + disp * _UD_CausticParams2.y) * float2(_UD_Aspect.x, 1.0)
                            + _UD_WorldOffset.xy * _UD_WorldAnchor.y;

                // Animated domain warp sampled from the flow noise — the "alive" morph.
                float2 w;
                w.x = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, base * 0.6 + _UD_Time * 0.04).r;
                w.y = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, base * 0.6 + 0.5 - _UD_Time * 0.035).r;
                w = (w * 2.0 - 1.0) * _UD_CausticParams2.x;

                float2 buv = base * _UD_CausticParams.y + w;
                float  sp  = _UD_Time * _UD_CausticParams.z;

                // Two differently-warped, differently-drifting layers -> shimmering veins.
                float c1 = SAMPLE_TEXTURE2D(_UD_CausticTex, sampler_UD_CausticTex, buv              + sp * float2( 0.10, 0.07)).r;
                float c2 = SAMPLE_TEXTURE2D(_UD_CausticTex, sampler_UD_CausticTex, buv * 1.30 + w * 0.6 - sp * float2( 0.08, 0.05)).r;
                float c  = min(c1, c2);
                return pow(saturate(c), _UD_CausticParams.w) * 2.0;
            }

            // ─── Particles (marine snow + bubbles) ──────────────────────────────

            /** 2-D cell hash → two decorrelated 0-1 randoms (cheap, no texture fetch). */
            float2 UD_Hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            /**
             * One parallax layer of "marine snow": a jittered grid where each cell may host
             * one soft mote. The jitter range keeps the mote inside its cell, so a single
             * cell lookup suffices (no neighbor scan). Per-mote size and twinkle phase come
             * from the cell hash, so the field never pulses in unison.
             */
            float UD_MoteLayer(float2 coord, float density, float sizeMul, float twinkleAmt, float t)
            {
                float2 cell = floor(coord);
                float2 f    = frac(coord);
                float2 h    = UD_Hash22(cell);
                float2 h2   = UD_Hash22(cell + 19.19);

                // Density culling: the hash decides whether this cell hosts a mote at all.
                if (h2.x > density) return 0.0;

                // Soft gaussian dot at a jittered center, size varied per mote.
                float2 center = 0.25 + 0.5 * h;
                float  size   = (0.05 + 0.09 * h2.y) * sizeMul;
                float  d      = length(f - center);
                float  spot   = exp(-(d * d) / max(size * size, 1e-6));

                // Slow per-mote twinkle, phase-randomized across the field.
                float tw = 1.0 - twinkleAmt * (0.5 + 0.5 * sin(t * (0.7 + h.x * 1.8) + h.y * UD_TAU));
                return spot * tw;
            }

            /**
             * Marine snow: UD_MOTE_LAYERS parallax depth layers. Far layers use a weak world
             * anchor (scroll slower = read as distant), are denser/smaller and dimmer; the
             * near layer overshoots the world (anchor > 1) so it reads as foreground passing
             * the camera. Both the camera offset AND the water drift scale by each layer's
             * anchor, keeping the perspective consistent (near things move and drift faster).
             */
            #define UD_MOTE_LAYERS 3
            float UD_Motes(float2 uv, float2 disp, float t)
            {
                float2 baseUv = (uv + disp * 0.5) * float2(_UD_Aspect.x, 1.0);
                float sum = 0.0;

                [unroll]
                for (int l = 0; l < UD_MOTE_LAYERS; l++)
                {
                    float fl     = l / (UD_MOTE_LAYERS - 1.0);                    // 0 = far … 1 = near
                    float anchor = lerp(_UD_MoteParams2.x, _UD_MoteParams2.y, fl);
                    float2 wuv   = baseUv + (_UD_WorldOffset.xy - _UD_ParticleDrift.xy * t) * anchor;

                    // Depth styling: far layers get a tighter grid (smaller, denser motes) and dim out.
                    float scale = _UD_MoteParams.y * lerp(1.8, 0.8, fl);
                    float size  = _UD_MoteParams.z * lerp(0.7, 1.25, fl);
                    sum += UD_MoteLayer(wuv * scale + l * 13.7, _UD_MoteParams2.z, size, _UD_MoteParams.w, t + l * 7.31)
                         * lerp(0.3, 1.0, fl);
                }
                return sum;
            }

            /**
             * One layer of bubbles: a sparse jittered grid; each occupied cell hosts a
             * rim-lit circle with a small specular glint offset up-left, reading as a glassy
             * sphere. The center wobbles side-to-side as the bubble rises.
             */
            float UD_BubbleLayer(float2 coord, float density, float sizeMul, float wobble, float t)
            {
                float2 cell = floor(coord);
                float2 f    = frac(coord);
                float2 h    = UD_Hash22(cell);
                float2 h2   = UD_Hash22(cell + 7.77);

                // Density culling — bubbles should stay sparse to read as individuals.
                if (h2.x > density) return 0.0;

                // Wobbling center + per-bubble size.
                float  size   = (0.04 + 0.08 * h2.y) * sizeMul;
                float2 center = 0.3 + 0.4 * h;
                center.x += sin(t * (1.2 + h.y * 1.6) + h.x * UD_TAU) * wobble;

                // Rim ring + specular glint (up-left) = glassy sphere.
                float d    = length(f - center);
                float rr   = (d - size) / max(size * 0.35, 1e-4);
                float rim  = exp(-rr * rr);
                float2 sp  = f - center + size * float2(0.35, -0.35);
                float s2   = max(size * 0.3, 1e-4);
                float spec = exp(-dot(sp, sp) / (s2 * s2));
                return rim * 0.6 + spec * 1.1;
            }

            /**
             * Bubbles: two parallax layers (small far fizz + larger near bubbles) rising
             * through the world-anchored space, with optional sideways current drift.
             */
            #define UD_BUBBLE_LAYERS 2
            float UD_Bubbles(float2 uv, float2 disp, float t)
            {
                float2 baseUv = (uv + disp * 0.7) * float2(_UD_Aspect.x, 1.0);   // bubbles bend with the water a bit more
                float2 drift  = float2(_UD_ParticleDrift.z, _UD_ParticleDrift.w); // sideways current, rise speed
                float sum = 0.0;

                [unroll]
                for (int l = 0; l < UD_BUBBLE_LAYERS; l++)
                {
                    float fl     = l / (UD_BUBBLE_LAYERS - 1.0);                  // 0 = far … 1 = near
                    float anchor = _UD_BubbleParams2.x * lerp(0.75, 1.2, fl);
                    float2 wuv   = baseUv + (_UD_WorldOffset.xy - drift * t) * anchor;

                    float scale = _UD_BubbleParams.y * lerp(1.6, 0.9, fl);
                    float size  = _UD_BubbleParams.z * lerp(0.75, 1.2, fl);
                    sum += UD_BubbleLayer(wuv * scale + l * 23.7, _UD_BubbleParams2.y, size, _UD_BubbleParams.w, t + l * 11.7)
                         * lerp(0.45, 1.0, fl);
                }
                return sum;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord.xy;
                float  enable = _UD_FlowParams.w;

                // Edge fade so warped UVs never sample outside the frame.
                float2 edge = smoothstep(0.0, 0.06, uv) * smoothstep(0.0, 0.06, 1.0 - uv);
                float  fade = edge.x * edge.y;

                // 1) DISTORT — combine all displacement sources.
                float2 disp = (AmbientFlow(uv) + RippleFlow(uv) + WakeFlow(uv)) * fade * enable;
                float2 warped = saturate(uv + disp);

                // Chromatic refraction: split R/B along the displacement so light bends/splits
                // most where the water is churned (edges, wakes).
                float2 ca = disp * _UD_Chromatic;
                half3 col;
                col.r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, saturate(warped + ca), _BlitMipLevel).r;
                col.g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, warped,                 _BlitMipLevel).g;
                col.b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, saturate(warped - ca), _BlitMipLevel).b;

                // 2) LIGHT — additive god rays + caustics (caustics ride on lit surfaces).
                float luma    = dot(col, half3(0.299, 0.587, 0.114));
                float rays    = GodRays(uv, disp);
                float caustic = Caustics(uv, disp);
                float caMask  = luma * _UD_CausticMask.x + _UD_CausticMask.y;
                col += _UD_GodRayTint.rgb  * rays * _UD_GodRayParams.x * enable;
                col += _UD_CausticTint.rgb * caustic * _UD_CausticParams.x * caMask * enable;

                // 3) PARTICLES — parallax marine snow (boosted where it crosses the light
                //    shafts, like dust in a sunbeam) + rising bubbles. Both additive.
                float motes   = UD_Motes(uv, disp, _UD_Time);
                float bubbles = UD_Bubbles(uv, disp, _UD_Time);
                col += _UD_MoteTint.rgb   * motes   * _UD_MoteParams.x   * (1.0 + rays * _UD_MoteParams2.w) * enable;
                col += _UD_BubbleTint.rgb * bubbles * _UD_BubbleParams.x * enable;

                // 4) TINT — optional subtle deep-water grade.
                col = lerp(col, col * _UD_DeepTint.rgb, _UD_DeepTint.w * enable);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
