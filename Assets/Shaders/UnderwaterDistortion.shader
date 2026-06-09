// Fullscreen underwater effect for the URP 2D Renderer — built for a SIDE-VIEW,
// submerged ("looking through the water") perspective rather than a top-down surface.
//
// Driven by URP's built-in "Full Screen Pass Renderer Feature": the feature binds the
// rendered scene as _BlitTexture and runs this pass over a fullscreen triangle. Most
// tunables arrive as GLOBAL uniforms set from C# (UnderwaterDistortionController); the
// sampled noise/caustic textures are material properties so they show in the inspector.
//
// The fragment shader does three things, in order:
//   1. DISTORT  — build a UV displacement from ambient flow + ripples + propulsion wakes,
//                 then re-sample the scene with a chromatic (light-bending) split.
//   2. LIGHT    — add artificial underwater light: god-ray shafts from the surface and
//                 animated caustic shimmer (luminance-masked so it lands on objects).
//   3. TINT     — optional subtle depth tint.
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
                float t = _UD_Time * _UD_FlowParams.z;
                float s = _UD_FlowParams.y;
                float2 proc;
                proc.x = sin(uv.y * s * UD_TAU + t)        + 0.5 * sin(uv.y * s * 13.0 - t * 1.3);
                proc.y = cos(uv.x * s * UD_TAU - t * 0.9)  + 0.5 * cos(uv.x * s * 11.0 + t * 1.1);

                float  ns = _UD_FlowParams2.y;
                float  nt = _UD_Time * _UD_FlowParams2.z;
                float  nx = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, uv * ns + float2( 0.10,  0.13) * nt).r;
                float  ny = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, uv * ns + float2(-0.13,  0.07) * nt + 0.5).r;
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
                // Aspect-correct base coords, coupled to the water distortion.
                float2 base = (uv + disp * _UD_CausticParams2.y) * float2(_UD_Aspect.x, 1.0);

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
                float caustic = Caustics(uv, disp);
                float caMask  = luma * _UD_CausticMask.x + _UD_CausticMask.y;
                col += _UD_GodRayTint.rgb  * GodRays(uv, disp) * _UD_GodRayParams.x * enable;
                col += _UD_CausticTint.rgb * caustic * _UD_CausticParams.x * caMask * enable;

                // 3) TINT — optional subtle deep-water grade.
                col = lerp(col, col * _UD_DeepTint.rgb, _UD_DeepTint.w * enable);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
