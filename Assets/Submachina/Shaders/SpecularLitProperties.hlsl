// Shared UnityPerMaterial DECLARATIONS for the 2D specular shader family:
//   - SpriteLitSpecular.shader   (SpriteRenderers / SpriteShapeRenderers)
//   - Mesh2DLitSpecular.shader   (generated meshes: spline fills, creature bodies)
//
// #include this INSIDE every pass's CBUFFER_START(UnityPerMaterial)/CBUFFER_END,
// AFTER any shader-specific entries (tint, fill STs, edge band). Every pass of a
// shader must declare the identical CBUFFER layout or the SRP Batcher breaks —
// this include is what keeps the shared part in lockstep across all six passes
// and both shaders, and makes adding a property a one-line change.
//
// NO include guard ON PURPOSE: the file must expand exactly once per pass (each
// HLSLPROGRAM is its own compilation unit); a guard would silently empty a second
// include and corrupt the CBUFFER layout. A double include errors loudly instead.

// ---- Metallic specular (baseline glint + light response + compose) ----
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

// ---- Glow zone (threshold-gated bloom regions from the spec mask) ----
half _GlowThreshold;
half _GlowKnee;
half _GlowViewBias;
half _GlowPower;
half _GlowGain;

// ---- Animation (in-shader shimmer + direction wobble) ----
half _ShimmerAmp;
half _ShimmerSpeed;
half _ShimmerPhase;
half _ShimmerWave;
half _ShimmerMode;
half4 _DirWobble;

// ---- Surface normal (detail source + emboss + light-following grooves) ----
half _NormalMode;
half _NormalStrength;
half _DiffNormalStrength;
half _NormalEmboss;
half _EmbossElevation;
half _DirCavity;
half _DirCavityScale;
half _CavityLitFade;

// ---- Ambient relief (ungated fill light + cavity/slope AO) — SpecularLitCore.hlsl ----
half4 _AmbientDir;
half _AmbientFill;
half _SlopeAO;
half _CavityAmount;
half _CavityRidge;
half _CavityScale;
half _CavitySpec;

// ---- Albedo-as-height normal (mode 9) ----
// Deliberately NOT named _MainTex_TexelSize: the 2D SRP Batcher rejects any
// material carrying a _TexelSize property, which would silently unbatch sprites.
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

// ---- Form Shape (broad procedural form composited under the detail normal, RNM) ----
half _ShapeMode;
half _ShapeHeight;
half _ShapeRim;
half _ShapeProfile;
half _ShapeRect;
half _ShapeExtent;
half _ShapeAngle;
half _ShapeDetail;
half _ShapeBlur;

// ---- Outline / Emission / Flash (merged from ProcCreature2D) ----
// Outline + rim emission need the world-unit edge distance (TEXCOORD1.w on
// generated meshes) so they only act there; flat emission + flash work on
// every surface, sprites included. All defaults are neutral = off.
half4 _OutlineColor;
half _OutlineWidth;
half _OutlineSoftness;
half4 _EmissionColor;
half _RimEmission;
half _RimWidth;
half4 _FlashColor;
half _FlashAmount;

// ---- Sorting layer gate for the specular lights ----
float _SortingLayerBit;
