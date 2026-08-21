using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Generic per-instance driver for the SpriteLitSpecular material (shader
     * Submachina/2D/SpriteLitSpecular) using a MaterialPropertyBlock. Drop it on any
     * shiny sprite (metal, gems, ice, wet rock…) that should glint when a `SpecularLight2D`
     * sweeps over it.
     *
     * Scope: this base drives ONE look across the whole renderer. It writes a single
     * renderer-level property block, which every submesh reads. That is exactly right for
     * a SpriteRenderer (one sprite, one material).
     *
     * A SpriteShapeRenderer draws TWO submeshes from two different materials — a tiling
     * fill and the stitched edge sprites — whose textures come from different places:
     * the edge sprites carry their own `_NormalMap` / `_SpecMask` Secondary Textures,
     * while the fill is a raw Texture2D with no secondary-texture channel at all, so it
     * silently falls back to the shader defaults ("bump" = flat, "white" = fully shiny).
     * Driving both from one block therefore gives them the same SETTINGS but wildly
     * different TEXTURES. Use `SpriteShapeSpecularController` instead — it subclasses this
     * and adds explicit per-submesh Fill/Edge routing on top of the shared baseline.
     *
     * Why a MaterialPropertyBlock: it overrides shader properties for THIS renderer only,
     * so every instance can have its own colour / shininess / response without cloning the
     * material asset.
     *
     * Division of labour (the important part for performance):
     *   - The LIGHT-driven glint (a light sweeping across the sprite) is computed entirely
     *     on the GPU by the shader, from global light uniforms published by
     *     `SpecularLight2DManager`. This controller does NOT touch it and does NOT run every
     *     frame for it — that's what makes a field of hundreds of sprites cheap.
     *   - This controller writes the PER-INSTANCE look ONCE (colour, tightness, resting
     *     intensity, light response, idle-shimmer params) at spawn, then stays asleep
     *     (disabled, no Update) until a transient flare happens.
     *   - Transient flares are the ONLY things that need per-frame work. The generic flare
     *     is `Pulse(amount)` (a one-shot additive flash that decays). It wakes the component,
     *     drives the shader's additive `_SpecBoost`, and puts it back to sleep when spent.
     *
     * So: idle sprites = zero CPU. Only the ones currently flashing tick.
     *
     * Subclasses add their own sustained/transient contributions by overriding
     * `ComposeBoost()` (extra additive boost) and `IsIdle()` (stay awake while active),
     * then calling `Wake()` when their contribution turns on. See `OreSpecularController`,
     * which adds a mining glow on top of this.
     */
    public class SpecularController : MonoBehaviour, ITintReceiver
    {
        /**
         * Where the specular's surface normal comes from. `SpriteNormalMap` uses the sprite's
         * own `_NormalMap` (bespoke relief); `NormalTexture` is an explicit override texture;
         * Dome..Facets are procedural patterns generated in the shader from the sprite's UV —
         * instant glint for generic sprites with no authored map. WorldFacets/WorldRipples are
         * procedural patterns keyed to WORLD position instead of UV: continuous across stitched
         * geometry (SpriteShape fill + edges), so sparkle never seams or resets per segment.
         * `AlbedoHeight` derives the relief from the sprite's own albedo treated as a height
         * map (the Laigter / Material Maker trick) — real relief that follows the actual art,
         * with no authored normal map and no bake step.
         */
        public enum NormalSource { SpriteNormalMap = 0, Dome = 1, Bevel = 2, Ripples = 3, Radial = 4, Facets = 5, NormalTexture = 6, WorldFacets = 7, WorldRipples = 8, AlbedoHeight = 9 }

        /** Waveform shape for the animated modulation (intensity and/or direction). */
        public enum ModWaveform { Sine = 0, PingPong = 1, Noise = 2 }

        /**
         * What the intensity modulation drives. Values match the shader's `_ShimmerMode`.
         *   ScaleBase        — ±fraction of the resting intensity (legacy; needs base > 0 to show).
         *   Additive         — absolute units added on top of base, so it flickers even fully unlit.
         *   ScaleLight       — scales the light-driven glint: dark stays dark until a light hits,
         *                      then the modulation rides on top of whatever is lit.
         *   ScaleBaseAndLight— scales the resting glint AND the light-driven glint together.
         */
        public enum ModTarget { ScaleBase = 0, Additive = 1, ScaleLight = 2, ScaleBaseAndLight = 3 }

        /**
         * The broad procedural 3D FORM composited UNDER the detail normal (via Reoriented
         * Normal Mapping in the shader), so the whole sprite reads as a raised solid while
         * the Normal Source keeps supplying the surface texture riding on it.
         *   Shape          — the dome/bevel/pillow family; morph with Rim/Profile/Rectangularity.
         *   Cylinder       — curved across ONE axis (aimed by Angle): pipes, ridges, hulls.
         *   Slope          — a ramp along one axis: wedges, tilted panels.
         *   SilhouetteDome — blurred ALPHA as the height field: the sprite inflates from its
         *                    own outline whatever its shape (needs mipmaps on the texture).
         * On spline-fill meshes every non-None mode instead uses the baked edge band as the
         * distance field — the whole piece domes/bevels up from its own outline.
         */
        public enum FormShape { None = 0, Shape = 1, Cylinder = 2, Slope = 3, SilhouetteDome = 4 }

        // Cached shader property ids (avoids string hashing every write).
        // The handful a per-submesh subclass needs to re-point are protected.
        protected static readonly int SpecColorID = Shader.PropertyToID("_SpecColor");
        protected static readonly int NormalMapID = Shader.PropertyToID("_NormalMap");
        protected static readonly int SpecMaskID = Shader.PropertyToID("_SpecMask");
        private static readonly int SpecPowerID = Shader.PropertyToID("_SpecPower");
        private static readonly int SpecIntensityID = Shader.PropertyToID("_SpecIntensity");
        private static readonly int SpecLightDirID = Shader.PropertyToID("_SpecLightDir");
        private static readonly int LightResponseID = Shader.PropertyToID("_LightResponse");
        protected static readonly int SpecBoostID = Shader.PropertyToID("_SpecBoost");
        private static readonly int SpecReplaceID = Shader.PropertyToID("_SpecReplace");
        private static readonly int SpecClampID = Shader.PropertyToID("_SpecClamp");
        private static readonly int SpecAlbedoTintID = Shader.PropertyToID("_SpecAlbedoTint");
        private static readonly int SpecScreenID = Shader.PropertyToID("_SpecScreen");
        private static readonly int SpecViewBiasID = Shader.PropertyToID("_SpecViewBias");
        private static readonly int GlowThresholdID = Shader.PropertyToID("_GlowThreshold");
        private static readonly int GlowKneeID = Shader.PropertyToID("_GlowKnee");
        private static readonly int GlowViewBiasID = Shader.PropertyToID("_GlowViewBias");
        private static readonly int GlowPowerID = Shader.PropertyToID("_GlowPower");
        private static readonly int GlowGainID = Shader.PropertyToID("_GlowGain");
        private static readonly int DiffNormalStrengthID = Shader.PropertyToID("_DiffNormalStrength");
        private static readonly int NormalEmbossID = Shader.PropertyToID("_NormalEmboss");
        private static readonly int EmbossElevationID = Shader.PropertyToID("_EmbossElevation");
        private static readonly int DirCavityID = Shader.PropertyToID("_DirCavity");
        private static readonly int DirCavityScaleID = Shader.PropertyToID("_DirCavityScale");
        private static readonly int CavityLitFadeID = Shader.PropertyToID("_CavityLitFade");
        private static readonly int AmbientFillID = Shader.PropertyToID("_AmbientFill");
        private static readonly int AmbientDirID = Shader.PropertyToID("_AmbientDir");
        private static readonly int SlopeAOID = Shader.PropertyToID("_SlopeAO");
        private static readonly int CavityAmountID = Shader.PropertyToID("_CavityAmount");
        private static readonly int CavityRidgeID = Shader.PropertyToID("_CavityRidge");
        private static readonly int CavityScaleID = Shader.PropertyToID("_CavityScale");
        private static readonly int CavitySpecID = Shader.PropertyToID("_CavitySpec");
        private static readonly int ShimmerAmpID = Shader.PropertyToID("_ShimmerAmp");
        private static readonly int ShimmerSpeedID = Shader.PropertyToID("_ShimmerSpeed");
        private static readonly int ShimmerPhaseID = Shader.PropertyToID("_ShimmerPhase");
        private static readonly int ShimmerWaveID = Shader.PropertyToID("_ShimmerWave");
        private static readonly int ShimmerModeID = Shader.PropertyToID("_ShimmerMode");
        private static readonly int DirWobbleID = Shader.PropertyToID("_DirWobble");
        protected static readonly int NormalModeID = Shader.PropertyToID("_NormalMode");
        protected static readonly int NormalStrengthID = Shader.PropertyToID("_NormalStrength");
        private static readonly int NormalFreqID = Shader.PropertyToID("_NormalFreq");
        private static readonly int HeightTexelID = Shader.PropertyToID("_HeightTexel");
        private static readonly int HeightRadiusID = Shader.PropertyToID("_HeightRadius");
        private static readonly int HeightStrengthID = Shader.PropertyToID("_HeightStrength");
        private static readonly int HeightBlurID = Shader.PropertyToID("_HeightBlur");
        private static readonly int HeightDetailID = Shader.PropertyToID("_HeightDetail");
        private static readonly int HeightCompressID = Shader.PropertyToID("_HeightCompress");
        private static readonly int DiffFromModeID = Shader.PropertyToID("_DiffFromMode");
        private static readonly int NormalUVRectID = Shader.PropertyToID("_NormalUVRect");
        private static readonly int NormalTexID = Shader.PropertyToID("_NormalTex");
        private static readonly int NormalTexSTID = Shader.PropertyToID("_NormalTexST");
        private static readonly int SortingLayerBitID = Shader.PropertyToID("_SortingLayerBit");
        private static readonly int ShapeModeID = Shader.PropertyToID("_ShapeMode");
        private static readonly int ShapeHeightID = Shader.PropertyToID("_ShapeHeight");
        private static readonly int ShapeRimID = Shader.PropertyToID("_ShapeRim");
        private static readonly int ShapeProfileID = Shader.PropertyToID("_ShapeProfile");
        private static readonly int ShapeRectID = Shader.PropertyToID("_ShapeRect");
        private static readonly int ShapeExtentID = Shader.PropertyToID("_ShapeExtent");
        private static readonly int ShapeAngleID = Shader.PropertyToID("_ShapeAngle");
        private static readonly int ShapeDetailID = Shader.PropertyToID("_ShapeDetail");
        private static readonly int ShapeBlurID = Shader.PropertyToID("_ShapeBlur");

        // =====================
        // Baseline (per-instance look — variations without material copies)
        // =====================

        [FoldoutGroup("Baseline")]
        [Tooltip("Override the material's specular colour for this instance. Turn off to keep the shared material's colour.")]
        [SerializeField] private bool overrideColor = true;

        [FoldoutGroup("Baseline"), ShowIf(nameof(overrideColor))]
        [ColorUsage(true, true)]
        [SerializeField] private Color specColor = new Color(1.3f, 1.4f, 1.6f, 1f);

        [FoldoutGroup("Baseline")]
        [Tooltip("Specular tightness (higher = smaller, sharper hotspot).")]
        [SerializeField, Min(1f)] private float specPower = 64f;

        [FoldoutGroup("Baseline")]
        [Tooltip("How much the glint leans toward the viewer instead of the light. 0 = fully " +
                 "directional (a facet only glints when the light is on its side); higher = the glow " +
                 "fires from most light directions (omnidirectional crystals). 0.66 = original feel.")]
        [SerializeField, Range(0f, 10f)] private float specViewBias = 0.66f;

        [FoldoutGroup("Baseline")]
        [Tooltip("Resting specular intensity even when no light is on the sprite (glints against the baseline dir below). " +
                 "Usually 0 — the glint comes from the actual lights.")]
        [SerializeField, Min(0f)] private float baseIntensity = 0f;

        [FoldoutGroup("Baseline")]
        [Tooltip("Direction of the resting/shimmer/boost glint in sprite space (x right, y up). " +
                 "The LIGHT-driven glint ignores this and uses the real light direction.")]
        [SerializeField] private Vector2 baseLightDir = new Vector2(-0.45f, 0.6f);

        [FoldoutGroup("Baseline"), Range(0f, 1f)]
        [Tooltip("Balance the glint against the albedo. 0 = additive (glint adds HDR on top and can " +
                 "wash the texture out at full strength); 1 = energy-conserving replace (albedo fades " +
                 "toward the glint colour so the texture stays visible under the highlight).")]
        [SerializeField] private float specReplace = 0f;

        [FoldoutGroup("Baseline"), Min(0f)]
        [Tooltip("Ceiling on the light-driven glint's PEAK strength, applied before the light's distance " +
                 "falloff and beam cone shape it — so a hot light never floods past this cap but the falloff " +
                 "still reads through. Does not cap the resting glint or Pulse() flares. 0 = no clamp.")]
        [SerializeField] private float specClamp = 0f;

        [FoldoutGroup("Baseline"), Range(0f, 1f)]
        [Tooltip("How much the sprite's own texture colours the glint (metallic feel). 0 = glint is " +
                 "pure Spec Color; 1 = glint fully tinted by the underlying texel, so dark cracks stay " +
                 "dark and coloured pixels flare their own colour — texture detail reads through the " +
                 "highlight instead of being washed over.")]
        [SerializeField] private float specAlbedoTint = 0f;

        [FoldoutGroup("Baseline"), Range(0f, 1f)]
        [Tooltip("Screen-style softening of the additive glint. 0 = classic HDR add (can bloom / blow " +
                 "out); 1 = the glint is tone-compressed below white and fills only the remaining " +
                 "headroom — it can never blow out (and stops feeding bloom), so the texture's " +
                 "contrast always survives under the highlight. Most visible at hot highlights.")]
        [SerializeField] private float specScreen = 0f;

        // =====================
        // Glow zone (threshold-gated bloom regions driven by the spec mask's brightness)
        // =====================

        [FoldoutGroup("Glow Zone")]
        [Tooltip("Give BRIGHT spec-mask regions (e.g. blurry blobs painted over crystals) their own " +
                 "bloomy specular treatment — omnidirectional, wide, HDR — while the rest of the sprite " +
                 "keeps the Baseline metallic look. Off = whole sprite uses Baseline (previous behaviour).")]
        [SerializeField] private bool glowZone = false;

        [FoldoutGroup("Glow Zone"), ShowIf(nameof(glowZone)), Range(0f, 1f)]
        [Tooltip("Spec-mask brightness (max of R/G/B) above which a pixel blends into the glow set. " +
                 "Paint the metallic body comfortably below this and the crystal blobs near white.")]
        [SerializeField] private float glowThreshold = 0.7f;

        [FoldoutGroup("Glow Zone"), ShowIf(nameof(glowZone)), Range(0.001f, 0.5f)]
        [Tooltip("Softness of the threshold edge (smoothstep half-width). With blurry mask blobs this " +
                 "gives a natural hot-core → metallic falloff instead of a hard cutout line.")]
        [SerializeField] private float glowKnee = 0.15f;

        [FoldoutGroup("Glow Zone"), ShowIf(nameof(glowZone)), Range(0f, 10f)]
        [Tooltip("View bias inside the glow zone. High = omnidirectional — the blobs light up from " +
                 "virtually any light angle instead of only when a facet happens to face the light.")]
        [SerializeField] private float glowViewBias = 4f;

        [FoldoutGroup("Glow Zone"), ShowIf(nameof(glowZone)), Min(1f)]
        [Tooltip("Specular tightness inside the glow zone. Low = a fat, soft lobe that fills the whole " +
                 "blob; the Baseline tightness still shapes the metallic sparkle outside the zone.")]
        [SerializeField] private float glowPower = 8f;

        [FoldoutGroup("Glow Zone"), ShowIf(nameof(glowZone)), Min(0f)]
        [Tooltip("HDR multiplier on all specular inside the glow zone — pushes the blobs past 1.0 so " +
                 "they feed bloom. The glow keeps the mask's own colour (green blobs bloom green). " +
                 "The Baseline screen/replace compose also fades out in the zone so bloom isn't capped.")]
        [SerializeField] private float glowGain = 2f;

        // =====================
        // Spec mask (per-pixel gate/tint on ALL specular — which pixels get to glint)
        // =====================

        [FoldoutGroup("Spec Mask")]
        [Tooltip("Per-instance specular mask (RGB tint × strength per pixel): bright pixels glint at " +
                 "full strength, dark pixels stay dull — e.g. a speckle graphic makes ONLY the speckles " +
                 "light up under a sweeping light ('hidden gold'). Pairs well with the Glow Zone (bright " +
                 "speckles bloom) and a Facets normal (each speckle answers a different light angle). " +
                 "Bound directly, so the sprite needs no _SpecMask Secondary Texture wiring. Sampled with " +
                 "the sprite's own UVs: standalone sprites line up 1:1, atlased sprites won't (use a " +
                 "Secondary Texture there so it atlases along). Empty = keep the sprite's Secondary " +
                 "Texture / material default. For spline-fill meshes use SplineFillOverride's slot instead.")]
        [SerializeField] private Texture2D specMaskTexture;

        // =====================
        // Animation (living, shimmering sprite) — computed in-shader from _Time, zero CPU cost
        // =====================

        [FoldoutGroup("Animation")]
        [Tooltip("Continuously modulate the specular INTENSITY for a living surface. No CPU cost — the shader does it.")]
        [SerializeField] private bool animate = false;

        [FoldoutGroup("Animation"), ShowIf(nameof(animate))]
        [Tooltip("Waveform of the intensity modulation. Sine = smooth pulse, PingPong = linear back-and-forth, " +
                 "Noise = organic random flicker (candle/electrical feel).")]
        [SerializeField] private ModWaveform modWaveform = ModWaveform.Sine;

        [FoldoutGroup("Animation"), ShowIf(nameof(animate))]
        [Tooltip("What the modulation drives:\n" +
                 "• ScaleBase — ±fraction of resting intensity (needs base > 0 to be visible).\n" +
                 "• Additive — absolute units on top of base; flickers even at base 0 / in the dark.\n" +
                 "• ScaleLight — dark stays dark; once a light hits, the modulation rides on the lit glint.\n" +
                 "• ScaleBaseAndLight — scales the resting AND light-driven glint together.")]
        [SerializeField] private ModTarget modTarget = ModTarget.ScaleBase;

        [FoldoutGroup("Animation"), ShowIf(nameof(animate))]
        [Tooltip("Modulation size. Scale modes: fraction (0.25 = ±25%). Additive: absolute intensity units.")]
        [SerializeField, Min(0f)] private float modAmplitude = 0.25f;

        [FoldoutGroup("Animation"), ShowIf(nameof(animate))]
        [SerializeField, Min(0f)] private float modSpeed = 1.5f;

        [FoldoutGroup("Animation")]
        [Tooltip("Wobble the glint DIRECTION over time so the highlight slides across the surface — a watery " +
                 "shimmer. Rotates the baseline glint dir AND the real-light glint dir, so it works both unlit " +
                 "(with base intensity) and while a light sweeps the sprite.")]
        [SerializeField] private bool animateDirection = false;

        [FoldoutGroup("Animation"), ShowIf(nameof(animateDirection))]
        [Tooltip("Waveform of the direction wobble (independent of the intensity waveform).")]
        [SerializeField] private ModWaveform dirWaveform = ModWaveform.Sine;

        [FoldoutGroup("Animation"), ShowIf(nameof(animateDirection))]
        [Tooltip("Maximum rotation of the glint direction, in degrees (swings ± this amount).")]
        [SerializeField, Range(0f, 90f)] private float dirWobbleDegrees = 10f;

        [FoldoutGroup("Animation"), ShowIf(nameof(animateDirection))]
        [SerializeField, Min(0f)] private float dirWobbleSpeed = 1f;

        // =====================
        // Light reactivity (how strongly this sprite answers the real lights)
        // =====================

        [FoldoutGroup("Light Reactivity")]
        [Tooltip("Per-instance multiplier on the GPU light-driven glint. 0 = this sprite ignores lights; " +
                 "higher = brighter response. The falloff/cone/direction all come from the light itself.")]
        [SerializeField, Min(0f)] private float illuminationResponse = 1f;

        // =====================
        // Surface normal (what shapes the glint) — bespoke map OR a procedural pattern
        // =====================

        [FoldoutGroup("Surface Normal")]
        [Tooltip("What shapes the glint. SpriteNormalMap uses the sprite's own normal map; NormalTexture uses " +
                 "an explicit texture you drop below (no import-settings wiring); the rest are procedural patterns " +
                 "generated in-shader (no texture needed). Dome = round bulge, Bevel = rim only, " +
                 "Ripples = wavy bands, Radial = concentric rings, Facets = sparkly cells — all in sprite UV space. " +
                 "WorldFacets/WorldRipples are the same patterns in WORLD space: seam-free on SpriteShape terrain " +
                 "(stitched fill + edge sprites) where the UV-space modes would jump per segment.")]
        [SerializeField] private NormalSource normalSource = NormalSource.SpriteNormalMap;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(normalSource), NormalSource.NormalTexture)]
        [Tooltip("Normal map sampled across the sprite (local 0..1 UV, so atlasing is handled). Import it as " +
                 "Default with sRGB OFF (straight RGB) — the same way the baked ore normals are imported, NOT " +
                 "as Unity's 'Normal map' texture type.")]
        [SerializeField] private Texture2D normalTexture;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(normalSource), NormalSource.NormalTexture)]
        [Tooltip("Tiling (scale) of the override normal map across the sprite. 1 = fits the sprite exactly; " +
                 "below 1 enlarges the stamp, above 1 shrinks/repeats it. X and Y scale independently to fix aspect.")]
        [SerializeField] private Vector2 normalTextureTiling = Vector2.one;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(normalSource), NormalSource.NormalTexture)]
        [Tooltip("Offset (in sprite UV, 0..1) to slide the override normal map so it lines up with the sprite.")]
        [SerializeField] private Vector2 normalTextureOffset = Vector2.zero;

        [FoldoutGroup("Surface Normal")]
        [Tooltip("Depth of the surface relief for every normal mode. For the sprite/override normal maps it " +
                 "exaggerates the baked bumps (1 = as authored, >1 = deeper, <1 = flatter); for the procedural " +
                 "patterns it controls how pronounced they are (higher = more grazing, brighter travelling glints).")]
        [SerializeField, Min(0f)] private float normalStrength = 1f;

        [FoldoutGroup("Surface Normal")]
        [Tooltip("Deepens the relief fed to the 2D LIGHTING normal buffer, so every Light2D (multiply " +
                 "included) shades this sprite with exaggerated bumps. Independent of the specular " +
                 "Normal Strength above and always sourced from the sprite's own normal map. " +
                 "1 = as authored, >1 = deeper, <1 = flatter.")]
        [SerializeField, Min(0f)] private float diffuseNormalStrength = 1f;

        [FoldoutGroup("Surface Normal")]
        [Tooltip("Feed the 2D LIGHT BUFFER from the Normal Source above instead of only from the sprite's " +
                 "_NormalMap. THIS is what makes the procedural and AlbedoHeight modes read as real depth: " +
                 "without it they drive the SPECULAR only, so the surface still looks flat under an ordinary " +
                 "Light2D and never matches what an authored normal texture gives you.\n\n" +
                 "Off by default because turning it on changes the look of anything already using a " +
                 "procedural mode (those have always been specular-only). No extra cost when off.")]
        [SerializeField] private bool diffuseUsesNormalMode = false;

        [FoldoutGroup("Surface Normal")]
        [Tooltip("Fakes the relief contrast an ADDITIVE light would give while keeping your multiply " +
                 "light: facets facing a specular light brighten past the multiply-lit level, facets " +
                 "facing away darken, flat pixels are untouched. Uses the same normal source as the " +
                 "specular (at authored depth) and the specular lights' falloff/cone, so unlit areas " +
                 "stay dark. 0 = off; useful range ~0.5-3.")]
        [SerializeField, Min(0f)] private float reliefEmboss = 0f;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(HasReliefEmboss))]
        [Tooltip("How high each specular light sits above the surface plane for the Relief Emboss above. " +
                 "LOW = grazing light = maximum relief contrast, which is what makes the result read as " +
                 "SHADOW rather than as tinting; high = overhead = flat. 0.5 is what this used to hardcode, " +
                 "so lower it to make your lights sculpt the surface harder.")]
        [SerializeField, Range(0.05f, 2f)] private float embossElevation = 0.5f;

        [FoldoutGroup("Surface Normal")]
        [Tooltip("The LIGHT-FOLLOWING cavity term: net darkening in grooves that run ACROSS a specular " +
                 "light's direction, and none in grooves running along it — so the shading sweeps as the " +
                 "beam moves. Complements Relief Emboss rather than duplicating it: the emboss is " +
                 "antisymmetric (a fine groove's bright wall exactly cancels its dark wall, netting zero " +
                 "darkening), while this is the net energy loss real raking light suffers crossing a groove. " +
                 "0 = off.")]
        [SerializeField, Range(0f, 2f)] private float directionalGrooves = 0f;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(HasDirectionalGrooves))]
        [Tooltip("Gain on the directional groove measurement. Same per-world-unit curvature units as the " +
                 "Ambient Relief cavity gain, so the two are directly comparable and neither drifts with " +
                 "camera zoom, sprite scale, or a tiling override. A negative value flips it (brightens " +
                 "grooves instead), the fix for a normal map with an inverted channel.")]
        [SerializeField, Range(-20f, 20f)] private float directionalGrooveGain = 1f;

        // Sub-controls only matter once their parent term is actually contributing.
        private bool HasReliefEmboss => reliefEmboss > 0f;
        private bool HasDirectionalGrooves => directionalGrooves > 0f;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(normalSource), NormalSource.AlbedoHeight)]
        [InfoBox("Albedo is not height — this cannot tell PIGMENT from SHAPE, so dark paint on a flat " +
                 "surface becomes a dent. It reads well on textures whose value variation IS the relief " +
                 "(rock, bark, rubble, corrosion) and badly on flat-lit graphic art. Inherent to the " +
                 "technique; Laigter and Material Maker have the same failure mode.\n\n" +
                 "Note this drives the SPECULAR relief only — like every procedural mode, URP's diffuse " +
                 "2D lighting still reads the (flat) _NormalMap. Pair it with Ambient Relief to see the " +
                 "shape without a specular light on it.")]
        [Tooltip("Central-difference tap distance in TEXELS. 1 = adjacent texel, the finest detail the " +
                 "texture can express; larger reads broader forms and suppresses per-texel noise. " +
                 "Texel-based (not screen-based), so the relief is stable under camera zoom.")]
        [SerializeField, Range(0.25f, 8f)] private float heightTapRadius = 1f;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(normalSource), NormalSource.AlbedoHeight)]
        [Tooltip("Gain converting the luminance gradient into surface slope — the main depth dial. " +
                 "NEGATIVE inverts the relief, so dark reads as HIGH instead of low (worth trying: which " +
                 "way round looks right depends entirely on how the texture was painted). Normal Strength " +
                 "above still scales the result afterwards, as it does for every mode.")]
        [SerializeField, Range(-40f, 40f)] private float heightStrength = 8f;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(normalSource), NormalSource.AlbedoHeight)]
        [InfoBox("Blur is 0 but the source texture has no mipmaps, so blur/detail can't work — the relief " +
                 "will stay gritty. Enable 'Generate Mip Maps' on the texture's import settings.",
                 InfoMessageType.Warning, nameof(HeightBlurNeedsMips))]
        [Tooltip("THE fix for a gritty, pixelly result: the mip level the broad gradient is read from. " +
                 "Each step doubles both the blur and the tap radius, so raising it REMOVES fine structure " +
                 "rather than just softening it — that's what separates form from noise.\n\n" +
                 "Requires the texture to have MIPMAPS. Sprites often ship with 'Generate Mip Maps' off, " +
                 "in which case every level returns the base image and this does nothing.")]
        [SerializeField, Range(0f, 6f)] private float heightBlur = 1f;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(normalSource), NormalSource.AlbedoHeight)]
        [Tooltip("How much of the crisp, unblurred gradient is mixed back over the broad form above. " +
                 "0 = pure smooth shape (start here if the result is too busy), 1 = all of the texture's " +
                 "detail. Costs nothing when Blur is 0, since there's nothing to mix against.")]
        [SerializeField, Range(0f, 1f)] private float heightDetail = 0.5f;

        [FoldoutGroup("Surface Normal"), ShowIf(nameof(normalSource), NormalSource.AlbedoHeight)]
        [Tooltip("Soft knee on the slope, for 'details that shouldn't be there'. Hard albedo edges — " +
                 "outlines, paint boundaries, speckles — produce extreme gradients that read as cliffs, " +
                 "while gentle shading (the part that actually IS shape) produces small ones. This pulls " +
                 "the extremes toward a ceiling and leaves the gentle end nearly untouched, so it " +
                 "suppresses the wrong detail specifically instead of flattening everything. 0 = off.")]
        [SerializeField, Range(0f, 8f)] private float heightCompress = 2f;

        // Warn when blur is asked for but the texture can't deliver it.
        private bool HeightBlurNeedsMips
        {
            get
            {
                if (normalSource != NormalSource.AlbedoHeight || heightBlur <= 0f) return false;
                var sr = GetComponentInChildren<SpriteRenderer>();
                var tex = sr != null && sr.sprite != null ? sr.sprite.texture : null;
                return tex != null && tex.mipmapCount <= 1;
            }
        }

        [FoldoutGroup("Surface Normal"), HideIf(nameof(IsBaselineNormalMode))]
        [Tooltip("Wave count (Ripples/Radial) or cell count (Facets) across the sprite. Ignored by Dome/Bevel. " +
                 "For the World modes this is cells/waves PER WORLD UNIT instead (e.g. 8 = 8 sparkle cells per metre).")]
        [SerializeField, Min(0.01f)] private float normalFrequency = 8f;

        // Frequency only matters for the procedural patterns — not the texture modes, and not
        // AlbedoHeight (which has its own texel-space radius instead).
        private bool IsBaselineNormalMode =>
            normalSource == NormalSource.SpriteNormalMap || normalSource == NormalSource.NormalTexture
            || normalSource == NormalSource.AlbedoHeight;

        // =====================
        // Form shape — a broad procedural 3D form composited UNDER the detail normal
        // =====================

        [FoldoutGroup("Form Shape")]
        [InfoBox("Composites a broad 3D FORM under the Surface Normal above (Reoriented Normal " +
                 "Mapping), so the whole sprite reads as a raised solid while the normal source " +
                 "keeps supplying the surface texture riding on it. Pairs beautifully with " +
                 "AlbedoHeight or a tiled Normal Texture: broad shape + textured detail. The form " +
                 "feeds the specular AND the 2D light buffer, so it shades as real depth under " +
                 "every Light2D. Pair with Ambient Relief to see the form with no light on it.")]
        [Tooltip("Shape = the dome/bevel/pillow family (morph with Rim/Profile/Rectangularity). " +
                 "Cylinder = curved across one axis (aim with Angle). Slope = a ramp. " +
                 "SilhouetteDome = the sprite inflates from its own alpha outline, whatever its " +
                 "shape (needs mipmaps). On spline-fill meshes every mode instead uses the baked " +
                 "edge band — the piece domes up from its own outline.")]
        [SerializeField] private FormShape formShape = FormShape.None;

        // One-click starting points in the Rim/Profile/Height morph space (all editable after).
        [FoldoutGroup("Form Shape"), ButtonGroup("Form Shape/Presets")]
        private void Dome() => ApplyFormPreset(0f, 1f, 2f);
        [ButtonGroup("Form Shape/Presets")]
        private void Pillow() => ApplyFormPreset(0.45f, 1.8f, 2.5f);
        [ButtonGroup("Form Shape/Presets")]
        private void Bevel() => ApplyFormPreset(0.65f, 0.2f, 2f);
        [ButtonGroup("Form Shape/Presets")]
        private void Cone() => ApplyFormPreset(0f, 0f, 1.5f);

        [FoldoutGroup("Form Shape"), ShowIf(nameof(HasForm)), Range(0f, 8f)]
        [Tooltip("Overall steepness/depth of the form — the master 3D-ness dial.")]
        [SerializeField] private float formHeight = 2f;

        [FoldoutGroup("Form Shape"), ShowIf(nameof(HasProfiledForm)), Range(0f, 0.95f)]
        [Tooltip("Where the slope starts. 0 = curvature from the very centre (dome); higher = a " +
                 "flat plateau with the slope pushed out to a shoulder near the edge (bevel/pillow).")]
        [SerializeField] private float formRim = 0f;

        [FoldoutGroup("Form Shape"), ShowIf(nameof(HasProfiledForm)), Range(0f, 4f)]
        [Tooltip("Slope curve outside the rim. 0 = constant slope (linear bevel / cone), ~1 = " +
                 "parabolic dome, 2+ = slope packed at the edge (round inflated-cushion shoulder).")]
        [SerializeField] private float formProfile = 1f;

        [FoldoutGroup("Form Shape"), ShowIf(nameof(IsShapeForm)), Range(0f, 1f)]
        [Tooltip("Footprint of the shape: 0 = round/elliptical, 1 = rectangular with the slope on " +
                 "all four sides (a cushion/panel look). Blend between for rounded rects.")]
        [SerializeField] private float formRectangularity = 0f;

        [FoldoutGroup("Form Shape"), ShowIf(nameof(HasProfiledForm)), Range(0.1f, 4f)]
        [Tooltip("Footprint scale. Sprites: >1 pulls the form's edge INSIDE the rect (art with " +
                 "transparent padding), <1 spreads it past the rect (a gentler cap). Spline fills: " +
                 "the fraction of the baked edge band the form spans, like Edge Effect Width.")]
        [SerializeField] private float formExtent = 1f;

        [FoldoutGroup("Form Shape"), ShowIf(nameof(HasOrientedForm)), Range(-180f, 180f)]
        [Tooltip("Rotates the shape frame (degrees) — aims the cylinder axis / slope direction, " +
                 "or tilts the rectangular footprint.")]
        [SerializeField] private float formAngle = 0f;

        [FoldoutGroup("Form Shape"), ShowIf(nameof(HasForm)), Range(0f, 1f)]
        [Tooltip("How much of the detail relief (the Surface Normal above) survives on the form. " +
                 "1 = full detail riding the shape; 0 = the bare smooth form.")]
        [SerializeField] private float formDetail = 1f;

        [FoldoutGroup("Form Shape"), ShowIf(nameof(IsSilhouetteForm))]
        [InfoBox("The sprite's texture has no mipmaps, so the silhouette can't be blurred into a " +
                 "rounded shoulder — enable 'Generate Mip Maps' in the texture's import settings.",
                 InfoMessageType.Warning, nameof(SilhouetteNeedsMips))]
        [Range(0f, 6f)]
        [Tooltip("Mip level the alpha outline is blurred to. Higher = a wider, rounder inflated " +
                 "shoulder reaching further inside the silhouette.")]
        [SerializeField] private float formSilhouetteBlur = 3f;

        // Which form dials are relevant to the current mode.
        private bool HasForm => formShape != FormShape.None;
        private bool HasProfiledForm => HasForm && formShape != FormShape.SilhouetteDome;
        private bool IsShapeForm => formShape == FormShape.Shape;
        private bool HasOrientedForm => IsShapeForm || formShape == FormShape.Cylinder || formShape == FormShape.Slope;
        private bool IsSilhouetteForm => formShape == FormShape.SilhouetteDome;

        // Warn when the silhouette inflate can't blur (same mip requirement as the height blur).
        private bool SilhouetteNeedsMips
        {
            get
            {
                if (formShape != FormShape.SilhouetteDome) return false;
                var sr = GetComponentInChildren<SpriteRenderer>();
                var tex = sr != null && sr.sprite != null ? sr.sprite.texture : null;
                return tex != null && tex.mipmapCount <= 1;
            }
        }

        /** Preset jump into the Rim/Profile/Height morph space, applied immediately. */
        private void ApplyFormPreset(float rim, float profile, float height)
        {
            formShape = FormShape.Shape;
            formRim = rim;
            formProfile = profile;
            formHeight = height;
            EnsureInitialized();
            ApplyBaseline();
        }

        // =====================
        // Ambient relief — the relief you can see with NO light on the surface
        // =====================

        [FoldoutGroup("Ambient Relief")]
        [InfoBox("URP 2D can only shade a normal map from POSITIONAL lights — a Global Light2D has no " +
                 "position, so it's a flat multiply that ignores normals — and Relief Emboss above is " +
                 "gated by each specular light's falloff/cone. Result: unlit surfaces read as completely " +
                 "flat. These three terms are ungated, so relief survives in the dark.")]
        [Tooltip("Master switch for all three ambient relief terms. Off zeroes them in the shader, " +
                 "so this is a clean A/B toggle with no other bookkeeping.")]
        [SerializeField] private bool ambientRelief = false;

        [FoldoutGroup("Ambient Relief"), ShowIf(nameof(ambientRelief))]
        [Tooltip("(A) Virtual directional fill light — a fixed 'sun' that shades the relief EVERYWHERE, " +
                 "the 2D stand-in for Unity's 3D directional light. Brightens slopes facing it and shades " +
                 "slopes turned away; flat pixels are untouched, so untextured sprites never wash out. " +
                 "0 = off; useful range ~0.2-1.")]
        [SerializeField, Range(0f, 3f)] private float ambientFill = 0f;

        [FoldoutGroup("Ambient Relief"), ShowIf(nameof(ambientRelief))]
        [Tooltip("Direction the ambient fill 'sun' comes from, in sprite/UV space. Keep this IDENTICAL " +
                 "across every material in the scene — a shared direction is what reads as one sun; " +
                 "per-object directions just read as noise.")]
        [SerializeField] private Vector2 ambientFillDir = new Vector2(-0.45f, 0.6f);

        [FoldoutGroup("Ambient Relief"), ShowIf(nameof(ambientRelief))]
        [Tooltip("How high the fill 'sun' sits above the surface plane. Low = grazing light = maximum " +
                 "relief contrast (long apparent shading); high = overhead = flatter, subtler shading.")]
        [SerializeField, Range(0.05f, 2f)] private float ambientFillElevation = 0.5f;

        [FoldoutGroup("Ambient Relief"), ShowIf(nameof(ambientRelief))]
        [Tooltip("(B) Slope shading — steeper pixels sit darker, regardless of any light direction. " +
                 "Costs one instruction. Not true occlusion (it also dims the rim of a big smooth bulge), " +
                 "but it's a cheap always-on depth floor. 0 = off; useful range ~0.5-3.")]
        [SerializeField, Range(0f, 4f)] private float slopeShading = 0f;

        [FoldoutGroup("Ambient Relief"), ShowIf(nameof(ambientRelief))]
        [Tooltip("(C) Cavity occlusion — darkens PITS, derived live from the normal map's curvature " +
                 "(no baked AO texture needed). This is the term that makes crevices read as deep. " +
                 "0 = off; useful range ~0.3-1.")]
        [SerializeField, Range(0f, 2f)] private float cavityOcclusion = 0f;

        [FoldoutGroup("Ambient Relief"), ShowIf(nameof(ambientRelief))]
        [Tooltip("The bright half of the same cavity term — brightens RIDGE CRESTS with albedo-coloured " +
                 "light. Often sells relief harder than the dark half does, so try raising both together.")]
        [SerializeField, Range(0f, 2f)] private float cavityRidge = 0f;

        [FoldoutGroup("Ambient Relief"), ShowIf(nameof(ambientRelief))]
        [Tooltip("Gain on the curvature measurement. Measured per WORLD unit, so it does NOT drift with " +
                 "camera zoom, sprite scale, or a tiling override — tune it once per normal map (raise " +
                 "until pits read, back off before they crush to black). Same units as the Directional " +
                 "Groove Gain, so the two are directly comparable. A NEGATIVE value swaps pits and ridges, " +
                 "the fix if a normal map's X/green channel is inverted.")]
        [SerializeField, Range(-20f, 20f)] private float cavityGain = 1f;

        [FoldoutGroup("Ambient Relief"), ShowIf(nameof(ambientRelief))]
        [Tooltip("How much the cavity/slope occlusion also gates the SPECULAR — grit packed down into a " +
                 "crevice shouldn't sparkle. Gating the glint sells depth about as strongly as darkening " +
                 "the albedo does. 0 = glint ignores occlusion, 1 = fully occluded.")]
        [SerializeField, Range(0f, 1f)] private float cavityOccludesSpecular = 0f;

        [FoldoutGroup("Ambient Relief"), ShowIf(nameof(ambientRelief))]
        [Tooltip("How much DIRECT light suppresses the cavity occlusion above. Physically, ambient occlusion " +
                 "belongs to AMBIENT light — direct light is supposed to cast real shadows instead, which is " +
                 "what Relief Emboss and Directional Grooves do. At 0 a spotlit crevice gets darkened twice " +
                 "by two different models; raise toward 1 to hand crevices over to the light-following terms " +
                 "wherever a beam actually lands. 0 = the terms simply stack.")]
        [SerializeField, Range(0f, 1f)] private float cavityFadeUnderLight = 0f;

        // =====================
        // Transient flare (one-shot pulses)
        // =====================

        [FoldoutGroup("Transient Flare")]
        [Tooltip("How fast a Pulse() flash decays back to zero (units/sec).")]
        [SerializeField, Min(0f)] private float pulseDecay = 4f;

        // =====================
        // Internals
        // =====================

        private Renderer[] _renderers;   // SpriteRenderers and/or SpriteShapeRenderers
        private MaterialPropertyBlock _mpb;
        private float _phase;            // per-instance offset so a field of sprites doesn't shimmer in unison

        /** The renderers this controller drives — subclasses read this for per-submesh routing. */
        protected Renderer[] Renderers => _renderers;

        /** Shared scratch property block. Always fill it completely before setting it. */
        protected MaterialPropertyBlock Mpb => _mpb;

        private float _pulse;            // additive one-shot flash, decays via pulseDecay
        private float _lastBoost = float.NaN; // cheap dirty check for the transient write

        /**
         * Caches the renderers + property block, writes the per-instance baseline once,
         * then sleeps (disabled) so idle sprites cost no per-frame CPU.
         */
        protected virtual void Awake()
        {
            EnsureInitialized();

            // Stable per-instance phase from world position (no global RNG needed)
            float h = transform.position.x * 12.9898f + transform.position.y * 78.233f;
            _phase = h - Mathf.Floor(h);

            ApplyBaseline();
            enabled = false; // nothing to tick until a transient flare wakes us
        }

        // -------------------------------------------------------
        // Public API (drivers call these)
        // -------------------------------------------------------

        /**
         * Adds a one-shot additive flash to the specular (e.g. an MMF feedback or Animator
         * event on a hit). Decays back to zero via pulseDecay. Layers on top of the light
         * reactivity (and any subclass contribution) instead of overwriting them.
         */
        public void Pulse(float amount)
        {
            _pulse += Mathf.Max(0f, amount);
            Wake();
        }

        /**
         * ITintReceiver — multiplies an external variation tint (e.g. ClusterBuilder's
         * hue/brightness rolls) into this instance's specular colour, which lives behind
         * the property block where SpriteRenderer.color can't reach. RGB only: alpha and
         * the HDR magnitude are preserved, so a 0.8-brightness tint dims the glint in
         * step with the albedo. Safe in edit mode (preview builds run before Awake).
         */
        public void ApplyTint(Color tint)
        {
            EnsureInitialized();

            // Not overriding yet? Seed from the shared material's colour so the tint has
            // a base to multiply into, then switch to a per-instance override.
            if (!overrideColor)
            {
                if (_renderers.Length > 0 && _renderers[0].sharedMaterial != null)
                    specColor = _renderers[0].sharedMaterial.GetColor(SpecColorID);
                overrideColor = true;
            }

            // Multiply RGB, keep alpha — repeat calls compound by design
            specColor = new Color(specColor.r * tint.r, specColor.g * tint.g, specColor.b * tint.b, specColor.a);
            ApplyBaseline();
        }

        // -------------------------------------------------------
        // Per-frame work — ONLY runs while a flare is active
        // -------------------------------------------------------

        /**
         * Decays any pulse, recomposes the transient boost (base pulse + subclass extras),
         * and writes it. Once everything is spent (`IsIdle()`), writes a final zero and
         * disables itself so the sprite returns to zero-cost idle.
         */
        private void Update()
        {
            if (_pulse > 0f) _pulse = Mathf.Max(0f, _pulse - pulseDecay * Time.deltaTime);

            // Compose the additive flare and push it to the shader's _SpecBoost.
            WriteBoost(ComposeBoost());

            // Nothing left to animate — go back to sleep.
            if (IsIdle()) enabled = false;
        }

        // -------------------------------------------------------
        // Subclass extension points
        // -------------------------------------------------------

        /** The additive `_SpecBoost` this frame. Base = the decaying pulse; override to add more. */
        protected virtual float ComposeBoost() => _pulse;

        /** True when there's nothing left to drive per-frame. Override to stay awake while active. */
        protected virtual bool IsIdle() => _pulse <= 0f;

        /** Wake the component so Update runs (subclasses call this when their contribution turns on). */
        protected void Wake() => enabled = true;

        // -------------------------------------------------------
        // Property-block writes
        // -------------------------------------------------------

        /**
         * Writes the per-instance baseline into every renderer's property block. Called once
         * at spawn (and on inspector edits) — the light-driven glint is added on top by the
         * shader, so this does NOT need to run per frame. Subclasses layer per-submesh
         * overrides on top via `ApplyRendererOverrides`.
         */
        private void ApplyBaseline()
        {
            if (_renderers == null) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];

                // Renderer-level block — every submesh reads this unless a subclass
                // overrides that slot with its own per-material block below.
                r.GetPropertyBlock(_mpb);
                WriteBaselineProperties(_mpb, r);
                r.SetPropertyBlock(_mpb);

                ApplyRendererOverrides(r);
            }

            _lastBoost = 0f;
        }

        /**
         * Hook for subclasses that need to re-point individual submeshes (different normal
         * map, spec mask or spec colour per material slot). Called right after the
         * renderer-level block is written, so an override always wins. No-op in the base.
         */
        protected virtual void ApplyRendererOverrides(Renderer r) { }

        /**
         * Hook for subclasses to keep their per-submesh blocks' transient `_SpecBoost` in
         * step with the renderer-level one — a per-material block REPLACES the renderer
         * block for that slot, so it would otherwise miss every Pulse() flash. No-op in the base.
         */
        protected virtual void WriteBoostOverrides(Renderer r, float boost) { }

        /**
         * Writes the FULL per-instance baseline into the given property block for one renderer.
         * Factored out so per-submesh overrides can repeat it into their per-material block —
         * a per-material-index block REPLACES the renderer-level one for that submesh, so it
         * must be written complete, not as a delta.
         */
        protected void WriteBaselineProperties(MaterialPropertyBlock mpb, Renderer r)
        {
            // Baseline glint direction (the viewer lean now comes from _SpecViewBias in the shader)
            Vector2 d = baseLightDir.sqrMagnitude > 1e-4f ? baseLightDir.normalized : Vector2.up;
            Vector4 dir = new Vector4(d.x, d.y, 0f, 0f);

            // Animation params (amplitudes collapse to 0 when their toggle is off, neutralizing
            // the shader terms). Direction wobble packs as (amp radians, speed, waveform, phase)
            // with a phase offset so intensity + direction don't move in lockstep.
            float shimmerAmp = animate ? modAmplitude : 0f;
            float shimmerPhase = _phase * 6.2831853f; // 0..2π
            float wobbleAmp = animateDirection ? dirWobbleDegrees * Mathf.Deg2Rad : 0f;
            Vector4 dirWobble = new Vector4(wobbleAmp, dirWobbleSpeed, (float)(int)dirWaveform, shimmerPhase + 2.399f);

            float normalMode = (float)(int)normalSource;

            mpb.SetFloat(SpecPowerID, specPower);
            mpb.SetFloat(SpecViewBiasID, specViewBias);
            mpb.SetFloat(SpecIntensityID, baseIntensity);
            mpb.SetVector(SpecLightDirID, dir);
            mpb.SetFloat(LightResponseID, illuminationResponse);
            mpb.SetFloat(ShimmerAmpID, shimmerAmp);
            mpb.SetFloat(ShimmerSpeedID, modSpeed);
            mpb.SetFloat(ShimmerPhaseID, shimmerPhase);
            mpb.SetFloat(ShimmerWaveID, (float)(int)modWaveform);
            mpb.SetFloat(ShimmerModeID, (float)(int)modTarget);
            mpb.SetVector(DirWobbleID, dirWobble);
            mpb.SetFloat(SpecBoostID, 0f);
            // Albedo balance: additive vs energy-conserving replace, plus optional glint ceiling
            mpb.SetFloat(SpecReplaceID, specReplace);
            mpb.SetFloat(SpecClampID, specClamp);
            // Texture-through-the-glint controls: albedo-tinted spec + screen-softened additive
            mpb.SetFloat(SpecAlbedoTintID, specAlbedoTint);
            mpb.SetFloat(SpecScreenID, specScreen);
            // Glow zone: threshold-gated bloom regions from the spec mask's brightness.
            // Toggle off = threshold 2, unreachable by a 0..1 mask, so the zone is inert.
            mpb.SetFloat(GlowThresholdID, glowZone ? glowThreshold : 2f);
            mpb.SetFloat(GlowKneeID, glowKnee);
            mpb.SetFloat(GlowViewBiasID, glowViewBias);
            mpb.SetFloat(GlowPowerID, glowPower);
            mpb.SetFloat(GlowGainID, glowGain);
            // Procedural-normal params (the UV rect is per-sprite so patterns stay centered on atlases)
            mpb.SetFloat(NormalModeID, normalMode);
            mpb.SetFloat(NormalStrengthID, normalStrength);
            mpb.SetFloat(DiffNormalStrengthID, diffuseNormalStrength);
            mpb.SetFloat(NormalEmbossID, reliefEmboss);
            mpb.SetFloat(NormalFreqID, normalFrequency);
            // Albedo-as-height relief (mode 9). Harmless to write in every mode — the shader
            // only reads these on mode 9. The texel size lets the tap radius be expressed in
            // TEXELS (so relief is stable under zoom); left at zero the shader falls back to
            // screen-pixel taps, which is why a MeshRenderer with no sprite is fine here.
            var heightTex = r is SpriteRenderer hsr && hsr.sprite != null ? hsr.sprite.texture : null;
            mpb.SetVector(HeightTexelID, heightTex != null
                ? new Vector4(1f / heightTex.width, 1f / heightTex.height, heightTex.width, heightTex.height)
                : Vector4.zero);
            mpb.SetFloat(HeightRadiusID, heightTapRadius);
            mpb.SetFloat(HeightStrengthID, heightStrength);
            mpb.SetFloat(HeightBlurID, heightBlur);
            mpb.SetFloat(HeightDetailID, heightDetail);
            mpb.SetFloat(HeightCompressID, heightCompress);
            // Whether the 2D light buffer reads the Normal Source too, or only _NormalMap.
            mpb.SetFloat(DiffFromModeID, diffuseUsesNormalMode ? 1f : 0f);
            // Form Shape: the broad procedural form composited under the detail normal.
            mpb.SetFloat(ShapeModeID, (float)(int)formShape);
            mpb.SetFloat(ShapeHeightID, formHeight);
            mpb.SetFloat(ShapeRimID, formRim);
            mpb.SetFloat(ShapeProfileID, formProfile);
            mpb.SetFloat(ShapeRectID, formRectangularity);
            mpb.SetFloat(ShapeExtentID, formExtent);
            mpb.SetFloat(ShapeAngleID, formAngle * Mathf.Deg2Rad);
            mpb.SetFloat(ShapeDetailID, formDetail);
            mpb.SetFloat(ShapeBlurID, formSilhouetteBlur);
            // Light-FOLLOWING relief: the emboss's grazing angle plus the directional groove
            // term. These live with the lights, so they're independent of the ambientRelief
            // toggle below (which only gates the light-independent terms).
            mpb.SetFloat(EmbossElevationID, embossElevation);
            mpb.SetFloat(DirCavityID, directionalGrooves);
            mpb.SetFloat(DirCavityScaleID, directionalGrooveGain);
            // Ambient relief: the three light-INDEPENDENT terms (fill sun, slope shading,
            // cavity). The master toggle off writes plain zeros, each of which is the
            // shader's natural no-op, so the terms cost nothing and change nothing.
            Vector2 fd = ambientFillDir.sqrMagnitude > 1e-4f ? ambientFillDir.normalized : Vector2.up;
            mpb.SetVector(AmbientDirID, new Vector4(fd.x, fd.y, ambientFillElevation, 0f));
            mpb.SetFloat(AmbientFillID, ambientRelief ? ambientFill : 0f);
            mpb.SetFloat(SlopeAOID, ambientRelief ? slopeShading : 0f);
            mpb.SetFloat(CavityAmountID, ambientRelief ? cavityOcclusion : 0f);
            mpb.SetFloat(CavityRidgeID, ambientRelief ? cavityRidge : 0f);
            mpb.SetFloat(CavityScaleID, cavityGain);
            mpb.SetFloat(CavitySpecID, ambientRelief ? cavityOccludesSpecular : 0f);
            mpb.SetFloat(CavityLitFadeID, ambientRelief ? cavityFadeUnderLight : 0f);
            // Sprite rect remap only applies to SpriteRenderers; a SpriteShape mesh spans
            // many sprite rects + a tiling fill, so it gets the identity rect.
            mpb.SetVector(NormalUVRectID, r is SpriteRenderer sr ? SpriteUVRect(sr) : new Vector4(0f, 0f, 1f, 1f));
            // Inline normal-map override: bind the texture + its tiling/offset so the shader
            // (mode 6) samples it, scaled/positioned to fit the sprite. If none is assigned the
            // shader falls back to the material's flat "bump" default.
            if (normalSource == NormalSource.NormalTexture)
            {
                if (normalTexture != null) mpb.SetTexture(NormalTexID, normalTexture);
                mpb.SetVector(NormalTexSTID, new Vector4(
                    normalTextureTiling.x, normalTextureTiling.y, normalTextureOffset.x, normalTextureOffset.y));
            }
            // Inline spec-mask override: gates/tints all specular per pixel without Secondary
            // Texture wiring. Left empty, the sprite's own _SpecMask (or the "white" = fully
            // shiny default) shows through.
            if (specMaskTexture != null) mpb.SetTexture(SpecMaskID, specMaskTexture);
            if (overrideColor) mpb.SetColor(SpecColorID, specColor);
            // Sorting layer bit so the shader only responds to lights targeting this layer
            mpb.SetFloat(SortingLayerBitID, (float)SpecularLight2DManager.SortingLayerBit(r.sortingLayerID));
        }

        /**
         * The specular colour this instance actually renders with: the per-instance override
         * when enabled, otherwise the colour baked into the slot's shared material. Subclasses
         * need this to scale/tint a single submesh relative to the shared baseline.
         */
        protected Color GetEffectiveSpecColor(Renderer r, int materialIndex)
        {
            if (overrideColor) return specColor;

            var mats = r != null ? r.sharedMaterials : null;
            if (mats != null && materialIndex >= 0 && materialIndex < mats.Length && mats[materialIndex] != null)
                return mats[materialIndex].GetColor(SpecColorID);

            return Color.white;
        }

        /**
         * The sprite's rect within its (possibly atlased) texture, normalized to 0..1 UV space
         * as (uMin, vMin, uSize, vSize). The shader uses this to remap atlas UVs back to 0..1
         * local coords so procedural patterns stay centered. Falls back to the full 0..1 rect.
         */
        private static Vector4 SpriteUVRect(SpriteRenderer r)
        {
            var sp = r != null ? r.sprite : null;
            if (sp == null || sp.texture == null) return new Vector4(0f, 0f, 1f, 1f);

            Rect tr = sp.textureRect;
            float w = sp.texture.width, h = sp.texture.height;
            return new Vector4(tr.x / w, tr.y / h, tr.width / w, tr.height / h);
        }

        /**
         * Writes just the additive transient boost to every renderer (preserving the baseline
         * already in each block). Skipped when the value hasn't meaningfully changed.
         */
        private void WriteBoost(float boost)
        {
            if (_renderers == null) return;
            if (Mathf.Abs(boost - _lastBoost) < 0.0001f) return;
            _lastBoost = boost;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(SpecBoostID, boost);
                r.SetPropertyBlock(_mpb);

                // Keep any per-submesh blocks a subclass owns flashing in sync.
                WriteBoostOverrides(r, boost);
            }
        }

        // -------------------------------------------------------
        // Editor convenience
        // -------------------------------------------------------

        /** Lazily cache the renderers + property block (Awake, edit-mode ApplyTint/OnValidate). */
        private void EnsureInitialized()
        {
            // Gather only the renderer types the specular shaders target: plain sprites,
            // SpriteShape terrain, and generated spline-fill meshes (MeshRenderer +
            // SplineFillLitSpecular) — skipping particles/lines/etc. in the hierarchy.
            if (_renderers == null)
            {
                var all = GetComponentsInChildren<Renderer>(true);
                var list = new List<Renderer>(all.Length);
                foreach (var r in all)
                    if (r is SpriteRenderer || r is SpriteShapeRenderer || r is MeshRenderer) list.Add(r);
                _renderers = list.ToArray();
            }
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
        }

        /** Re-apply the baseline when tweaking values in the inspector at edit time. */
        private void OnValidate()
        {
            EnsureInitialized();
            ApplyBaseline();
        }
    }
}
