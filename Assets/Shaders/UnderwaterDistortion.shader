// Fullscreen underwater distortion for the URP 2D Renderer.
//
// Designed to be driven by URP's built-in "Full Screen Pass Renderer Feature":
// the feature binds the rendered scene as _BlitTexture and runs this pass over a
// fullscreen triangle. Most tunables arrive as GLOBAL uniforms set from C#
// (UnderwaterDistortionController); the one exception is the ambient noise texture,
// which is a normal material property so it can be assigned in the inspector.
//
// The fragment shader builds a UV displacement from two sources and re-samples
// the scene at the warped coordinate:
//   1. Ambient flow  — a gentle whole-screen undulation (procedural sine layers
//                      blended with an optional scrolling noise texture).
//   2. Ripple flow   — expanding radial waves emitted at world points, packed
//                      into two Vector4 arrays by the controller each frame.
Shader "Submachina/UnderwaterDistortion"
{
    Properties
    {
        // Tiling noise sampled for the textured ambient mode (Noise Blend > 0). Any
        // seamless grayscale noise works (e.g. Feel's MMPerlinNoise/MMCloudsNoise).
        // Default "gray" = 0.5 -> a neutral zero offset, so an unassigned slot is safe.
        [NoScaleOffset] _UD_NoiseTex ("Ambient Noise (tiling)", 2D) = "gray" {}
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
            float2 _UD_Aspect;          // (camera.aspect, 1) — keeps ripple rings circular

            // Tiling noise driving the textured ambient mode (declared as a material
            // property above so it shows in the inspector). Default "gray" = neutral.
            TEXTURE2D(_UD_NoiseTex);
            SAMPLER(sampler_UD_NoiseTex);

            // Ripple pool, packed into two arrays. A length of 16 matches the controller's
            // UD_MAX_RIPPLES; _UD_RippleCount marks how many leading slots are live.
            #define UD_MAX_RIPPLES 16
            float4 _UD_RippleA[UD_MAX_RIPPLES];   // xy = centerUV, z = currentRadius, w = currentAmplitude
            float4 _UD_RippleB[UD_MAX_RIPPLES];   // x = waveFrequency, y = ringFalloff, z = phase, w = active(0/1)
            int    _UD_RippleCount;

            /**
             * Whole-screen ambient undulation as a UV offset.
             *
             * Two crossed sine layers give an organic, non-repeating wobble; an optional
             * tiling noise texture (scrolling on two axes, remapped to a signed offset)
             * is blended in for a more chaotic look. noiseBlend 0 = pure procedural.
             */
            float2 AmbientFlow(float2 uv)
            {
                // Procedural: layer a base sine with a faster, smaller harmonic per axis.
                float t = _UD_Time * _UD_FlowParams.z;
                float s = _UD_FlowParams.y;
                float2 proc;
                proc.x = sin(uv.y * s * UD_TAU + t)        + 0.5 * sin(uv.y * s * 13.0 - t * 1.3);
                proc.y = cos(uv.x * s * UD_TAU - t * 0.9)  + 0.5 * cos(uv.x * s * 11.0 + t * 1.1);

                // Textured: sample the tiling noise at two differently-scrolled UVs so even a
                // grayscale map gives a 2D (non-diagonal) warp. Each sample feeds one axis;
                // the +0.5 shift decorrelates them. Remap [0,1] -> signed [-1,1] offset.
                float  ns = _UD_FlowParams2.y;
                float  nt = _UD_Time * _UD_FlowParams2.z;
                float  nx = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, uv * ns + float2( 0.10,  0.13) * nt).r;
                float  ny = SAMPLE_TEXTURE2D(_UD_NoiseTex, sampler_UD_NoiseTex, uv * ns + float2(-0.13,  0.07) * nt + 0.5).r;
                float2 tex = float2(nx, ny) * 2.0 - 1.0;

                // Blend the two sources, then scale by the master ambient amplitude.
                return lerp(proc, tex, saturate(_UD_FlowParams2.x)) * _UD_FlowParams.x;
            }

            /**
             * Sum the radial displacement contributed by every active ripple.
             *
             * Each ripple is a ring centered on its expanding radius: a Gaussian
             * envelope localizes the wave to that ring, a sine gives the oscillation,
             * and the offset pushes outward along the radial direction. The delta is
             * aspect-corrected so rings stay circular on non-square screens.
             */
            float2 RippleFlow(float2 uv)
            {
                float2 disp = 0;

                [loop]
                for (int i = 0; i < _UD_RippleCount; i++)
                {
                    float4 a = _UD_RippleA[i];
                    float4 b = _UD_RippleB[i];
                    if (b.w < 0.5) continue;            // skip zeroed/inactive slots

                    // Distance from the ripple center, corrected for screen aspect.
                    float2 d    = (uv - a.xy) * _UD_Aspect;
                    float  dist = length(d);

                    // Localize to the expanding ring, oscillate along it, push outward.
                    float ring = exp(-pow((dist - a.z) / max(b.y, 1e-4), 2.0));
                    float wave = sin(dist * b.x * UD_TAU - b.z);
                    float2 dir = d / max(dist, 1e-4);
                    disp += dir * wave * ring * a.w;
                }

                return disp;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord.xy;

                // Fade displacement to zero near the borders so warped UVs never sample
                // outside the frame (which would smear/clamp the edge pixels).
                float2 edge = smoothstep(0.0, 0.06, uv) * smoothstep(0.0, 0.06, 1.0 - uv);
                float  fade = edge.x * edge.y;

                // Combine both flow sources, gated by the master enable and the edge fade.
                float2 offset = (AmbientFlow(uv) + RippleFlow(uv)) * fade * _UD_FlowParams.w;

                // Re-sample the scene at the warped coordinate (clamped, so out-of-range
                // UVs hold the border rather than wrapping).
                float2 warped = saturate(uv + offset);
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, warped, _BlitMipLevel);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
