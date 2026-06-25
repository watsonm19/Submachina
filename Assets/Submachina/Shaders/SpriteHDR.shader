// Unlit, double-sided sprite shader with an HDR tint, built for bloom.
//
// Why this exists: SpriteRenderer.color is a 32-bit LDR vertex color (clamped
// 0-1), so it can never push the >1 brightness that bloom needs. Instead we
// expose an HDR _Color material property and drive it per-renderer from script
// via a MaterialPropertyBlock — full float precision, no material instancing,
// and no shared-tint bleed across multiple submarines.
//
// - Cull Off  : renders on both faces, so a sprite that flips/rotates 180°
//               (the sub facing left) stays visible.
// - Alpha blend: respects the sprite's own alpha and the tint's alpha.
// - Unlit     : glow comes purely from the HDR tint, independent of scene lights.
Shader "Submachina/SpriteHDR"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HDR] _Color ("Tint (HDR)", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "Transparent"
            "RenderType"       = "Transparent"
            "IgnoreProjector"  = "True"
            "RenderPipeline"   = "UniversalPipeline"
        }

        Pass
        {
            Name "Unlit"
            // LightMode the URP 2D Renderer uses for its transparent sprite pass.
            Tags { "LightMode" = "Universal2D" }

            // Render state lives in the Pass — on the 2D Renderer, SubShader-level
            // Blend/Cull/ZWrite isn't reliably applied, which makes the sprite draw
            // opaque (ignoring alpha). Here it's guaranteed.
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;      // SpriteRenderer vertex color (LDR)
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // SRP Batcher: all per-material constants live in one CBUFFER.
            // No _MainTex_ST — SpriteRenderer supplies final UVs, and an _ST
            // property disables SRP batching on the 2D renderer.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
                OUT.uv          = IN.uv;
                OUT.color       = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // sprite texel * renderer vertex color * HDR tint.
                // _Color carries the >1 brightness that bloom keys off of.
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return tex * IN.color * _Color;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
