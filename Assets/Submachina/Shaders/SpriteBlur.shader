// URP 2D Sprite-Lit with a per-sprite blur baked into the material.
//
// Base is a verbatim copy of "Universal Render Pipeline/2D/Sprite-Lit-Default" (so the
// sprite still receives normal 2D lighting + shadows). The only change: the diffuse
// sample in the lit/unlit fragments is replaced with a multi-tap blur.
//
// Two blur modes (_BlurMode):
//   0 Gaussian — gaussian-weighted golden-angle spiral over a disc. Soft, even,
//     direction-free: the classic "out of focus" look.
//   1 Motion   — uniform taps along a line at _BlurAngle: the Photoshop Motion Blur
//     streak. Same cost per tap; great for speed lines / drifting silt.
//
// Why this approach (vs. post processing / render features):
//   - Isolation is by construction — only sprites using this material pay for or show
//     the blur. No layer juggling, no extra render targets, no fullscreen passes.
//   - ALPHA is blurred along with the colour, so edges dissolve softly and the sprite
//     stays properly see-through. Overall transparency still comes from the
//     SpriteRenderer colour alpha as usual (blurry glass = blur + tint alpha < 1).
//   - Cost scales only with blurred pixels on screen: _BlurSamples taps each (default 16).
//
// Under-sampling artifacts (the "textured"/cellular look when radius outruns taps) are
// fought on two fronts:
//   - _BlurNoise rotates (gaussian) / shifts (motion) the tap pattern by per-pixel
//     interleaved gradient noise, dissolving the structured pattern into fine static
//     grain. Nearly free; leave at 1 unless the grain itself bothers you.
//   - _BlurAutoMip widens each tap's footprint (higher mip) to match the gap between
//     taps, so sparse taps still overlap. REQUIRES mipmaps on the texture; composes
//     with the manual _BlurMip bias.
//
// Other blur details:
//   - Tap weights (gaussian mode) are gaussian in radius (sigma = radius/2), normalised.
//   - RGB is accumulated PREMULTIPLIED by alpha, then un-premultiplied — transparent
//     texels (usually black) then contribute only transparency, never dark halos.
//
// Caveats:
//   - The blur cannot paint OUTSIDE the sprite's mesh. For soft edges that grow outward,
//     set the sprite's Mesh Type to Full Rect and keep transparent padding in the art.
//   - On tightly packed atlases the taps can bleed neighbouring sprites in — give
//     blurred sprites atlas padding or keep them un-atlased.
//   - All three passes must share an identical UnityPerMaterial CBUFFER layout or the
//     SRP Batcher breaks — the blur properties are declared in every pass.
//
// Per-instance control (many sprites, one material, different blur amounts) goes through
// Core.Rendering.SpriteBlur, which writes these properties via MaterialPropertyBlock.
Shader "Submachina/2D/SpriteBlur"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        [Header(Blur)]
        // 0 = Gaussian disc (out-of-focus); 1 = Motion streak along _BlurAngle (Photoshop-style).
        [Enum(Gaussian, 0, Motion, 1)] _BlurMode("Blur Mode", Float) = 0
        // Blur radius in TEXELS of _MainTex. 0 = fully sharp (early-outs to a single tap).
        // In Motion mode this is the HALF-length of the streak (it extends both ways).
        _BlurRadius("Blur Radius (texels)", Range(0, 64)) = 8
        // Taps per pixel. 16 is smooth for radii up to ~12 texels; for bigger radii prefer
        // Auto Mip / _BlurMip over piling on taps.
        _BlurSamples("Blur Samples", Range(4, 48)) = 16
        // Motion mode only: streak direction in degrees (0 = horizontal, 90 = vertical).
        _BlurAngle("Motion Angle (degrees)", Range(0, 180)) = 0
        // Per-pixel noise rotation/shift of the tap pattern — dissolves the structured
        // "cellular" under-sampling artifact into fine grain. 1 = full de-banding.
        _BlurNoise("Pattern Noise (de-banding)", Range(0, 1)) = 1
        // Extra mip levels to sample from (needs mipmaps on the texture). Each +1 halves
        // the effective source resolution — the cheap way to make large radii creamy.
        _BlurMip("Blur Mip Bias (needs mipmaps)", Range(0, 6)) = 0
        // Auto-raise the mip so tap footprints cover the gaps between taps (needs mipmaps).
        // Kills the cellular look at any radius/sample combo, at some extra softness.
        [Toggle] _BlurAutoMip("Auto Mip (needs mipmaps)", Float) = 0

        // Legacy properties. They're here so that materials using this shader can gracefully fallback to the legacy sprite shader.
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
                half4 color        : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            // No _MainTex_TexelSize here on purpose — _TexelSize/_ST properties disable the
            // 2D SRP Batcher, so the blur queries the texture size with GetDimensions instead.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _BlurMode;
                half _BlurRadius;
                half _BlurSamples;
                half _BlurAngle;
                half _BlurNoise;
                half _BlurMip;
                half _BlurAutoMip;
            CBUFFER_END

            /**
             * Blurred sample of _MainTex at `uv` — gaussian spiral disc or directional
             * motion streak depending on _BlurMode. Premultiplied-alpha accumulation and
             * per-pixel noise de-banding throughout (see file header). `pixelPos` is the
             * fragment's screen position (positionCS.xy), used to seed the noise.
             */
            half4 BlurredMainSample(float2 uv, float2 pixelPos)
            {
                // Sharp fast path — behaves exactly like the stock shader at radius 0.
                if (_BlurRadius <= 0.01) return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // Interleaved gradient noise (0..1): a stable screen-space dither that turns
                // the structured tap pattern into fine grain. Scaled by the artist knob.
                float noise = frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715)))) * _BlurNoise;

                // Texel size straight from the texture (kept out of the CBUFFER — see note above).
                float texW, texH;
                _MainTex.GetDimensions(texW, texH);
                float2 texel = 1.0 / float2(texW, texH);

                int taps = max((int)_BlurSamples, 1);

                // Accumulate premultiplied colour so transparent texels can't smear dark halos.
                half4 premul = 0;
                half weightSum = 0;

                if (_BlurMode < 0.5)
                {
                    // --- Gaussian disc: golden-angle spiral, gaussian radial weights. ---
                    // sigma = radius/2 puts the disc edge at 2 sigma (~5% weight).
                    const float GOLDEN_ANGLE = 2.3999632;
                    float sigma = _BlurRadius * 0.5;
                    float invTwoSigmaSq = 1.0 / (2.0 * sigma * sigma);

                    // Auto-mip: ring spacing ~ radius/sqrt(taps); read the mip whose texel
                    // footprint matches it so sparse taps still overlap (no cell pattern).
                    float mip = _BlurMip;
                    if (_BlurAutoMip > 0.5) mip += max(0.0, log2(_BlurRadius / sqrt((float)taps)));

                    // Per-pixel rotation of the whole spiral — the main de-banding trick.
                    float baseAngle = noise * 6.2831853;

                    for (int i = 0; i < taps; i++)
                    {
                        // sqrt spreads taps evenly over the disc's AREA; golden angle avoids banding.
                        float r = _BlurRadius * sqrt((i + 0.5) / taps);
                        float angle = baseAngle + i * GOLDEN_ANGLE;
                        float2 offset = float2(cos(angle), sin(angle)) * r * texel;

                        half w = exp(-(r * r) * invTwoSigmaSq);
                        half4 s = SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, uv + offset, mip);
                        premul += half4(s.rgb * s.a, s.a) * w;
                        weightSum += w;
                    }
                }
                else
                {
                    // --- Motion streak: uniform taps along a line through the pixel, both
                    // directions — the Photoshop Motion Blur look (box filter, no falloff). ---
                    float rad = radians(_BlurAngle);
                    float2 dir = float2(cos(rad), sin(rad)) * texel;

                    // Auto-mip: tap spacing along the streak is 2*radius/taps.
                    float mip = _BlurMip;
                    if (_BlurAutoMip > 0.5) mip += max(0.0, log2(2.0 * _BlurRadius / taps));

                    // Per-pixel shift of the tap comb along the streak hides its banding.
                    float jitter = noise - 0.5 * _BlurNoise;

                    for (int i = 0; i < taps; i++)
                    {
                        // t spans -1..1 along the streak, e.g. 8 taps: -0.94, -0.69 ... 0.94.
                        float t = ((i + 0.5 + jitter) / taps) * 2.0 - 1.0;
                        float2 offset = dir * (t * _BlurRadius);

                        half4 s = SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, uv + offset, mip);
                        premul += half4(s.rgb * s.a, s.a);
                        weightSum += 1.0;
                    }
                }

                // Un-premultiply back to straight alpha for the standard sprite blend mode.
                half alpha = premul.a / weightSum;
                half3 rgb = premul.a > 1e-5 ? premul.rgb / premul.a : half3(0, 0, 0);
                return half4(rgb, alpha);
            }

            Varyings LitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;

                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                // Inlined CommonLitFragment with the blurred diffuse in place of the stock sample.
                const half4 main = input.color * BlurredMainSample(input.uv, input.positionCS.xy);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                SurfaceData2D surfaceData;
                InputData2D inputData;

                InitializeSurfaceData(main.rgb, main.a, mask, normalTS, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);

#if defined(DEBUG_DISPLAY)
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, input.positionWS, input.positionCS, _MainTex);
                surfaceData.normalWS = input.normalWS;
#endif

                return CombinedShapeLightShared(surfaceData, inputData);
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
            // Blur properties are unused in this pass but must match the other passes' layout.
            CBUFFER_START( UnityPerMaterial )
                half4 _Color;
                half _BlurMode;
                half _BlurRadius;
                half _BlurSamples;
                half _BlurAngle;
                half _BlurNoise;
                half _BlurMip;
                half _BlurAutoMip;
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
                return CommonNormalsFragment(input, input.color);
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
                half _BlurMode;
                half _BlurRadius;
                half _BlurSamples;
                half _BlurAngle;
                half _BlurNoise;
                half _BlurMip;
                half _BlurAutoMip;
            CBUFFER_END

            /** Same two-mode blur as the Universal2D pass — see that pass for details. */
            half4 BlurredMainSample(float2 uv, float2 pixelPos)
            {
                if (_BlurRadius <= 0.01) return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                float noise = frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715)))) * _BlurNoise;

                float texW, texH;
                _MainTex.GetDimensions(texW, texH);
                float2 texel = 1.0 / float2(texW, texH);

                int taps = max((int)_BlurSamples, 1);

                half4 premul = 0;
                half weightSum = 0;

                if (_BlurMode < 0.5)
                {
                    const float GOLDEN_ANGLE = 2.3999632;
                    float sigma = _BlurRadius * 0.5;
                    float invTwoSigmaSq = 1.0 / (2.0 * sigma * sigma);

                    float mip = _BlurMip;
                    if (_BlurAutoMip > 0.5) mip += max(0.0, log2(_BlurRadius / sqrt((float)taps)));

                    float baseAngle = noise * 6.2831853;

                    for (int i = 0; i < taps; i++)
                    {
                        float r = _BlurRadius * sqrt((i + 0.5) / taps);
                        float angle = baseAngle + i * GOLDEN_ANGLE;
                        float2 offset = float2(cos(angle), sin(angle)) * r * texel;

                        half w = exp(-(r * r) * invTwoSigmaSq);
                        half4 s = SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, uv + offset, mip);
                        premul += half4(s.rgb * s.a, s.a) * w;
                        weightSum += w;
                    }
                }
                else
                {
                    float rad = radians(_BlurAngle);
                    float2 dir = float2(cos(rad), sin(rad)) * texel;

                    float mip = _BlurMip;
                    if (_BlurAutoMip > 0.5) mip += max(0.0, log2(2.0 * _BlurRadius / taps));

                    float jitter = noise - 0.5 * _BlurNoise;

                    for (int i = 0; i < taps; i++)
                    {
                        float t = ((i + 0.5 + jitter) / taps) * 2.0 - 1.0;
                        float2 offset = dir * (t * _BlurRadius);

                        half4 s = SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, uv + offset, mip);
                        premul += half4(s.rgb * s.a, s.a);
                        weightSum += 1.0;
                    }
                }

                half alpha = premul.a / weightSum;
                half3 rgb = premul.a > 1e-5 ? premul.rgb / premul.a : half3(0, 0, 0);
                return half4(rgb, alpha);
            }

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
                // Inlined CommonUnlitFragment with the blurred diffuse in place of the stock sample.
                float4 mainTex = input.color * BlurredMainSample(input.uv, input.positionCS.xy);

#if defined(DEBUG_DISPLAY)
                SurfaceData2D surfaceData;
                InputData2D inputData;
                half4 debugColor = 0;

                InitializeSurfaceData(mainTex.rgb, mainTex.a, surfaceData);
                InitializeInputData(input.uv, inputData);
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, input.positionWS, input.positionCS, _MainTex);

                if (CanDebugOverrideOutputColor(surfaceData, inputData, debugColor))
                {
                    return debugColor;
                }
#endif

                return mainTex;
            }
            ENDHLSL
        }
    }
}
