#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core.EditorTools
{
    /** Overall silhouette family for a generated terrain object. */
    public enum SilhouetteShape
    {
        Blob,       // wobbly circle — the classic nugget
        Boulder,    // superellipse (squarish rounded mass)
        Slab,       // wide flattened superellipse (sediment slab / ledge chunk)
        Shard,      // angular polygon with flat facet edges
        Cluster     // several blobs smooth-merged into one lumpy mass
    }

    /** Which crystal system(s) to grow on the rock. */
    public enum CrystalStyle
    {
        None,
        Prisms,          // elongated quartz-like spikes fanning out of the rock
        Druse,           // amethyst-bed patches of tightly packed faceted terminations
        PrismsAndDruse
    }

    /**
     * One paint layer composited over the base rock albedo (sand, dirt, moss, rust…).
     * Uses a noise mask (optionally biased toward crevices or peaks) to place either an
     * external texture (tiled) or a flat tint. Layers apply in list order, top of list first.
     */
    [Serializable]
    public class PaintLayer
    {
        [Tooltip("Label only — helps keep the list readable.")]
        public string name = "Layer";

        [Tooltip("Untick to skip this layer without deleting it.")]
        public bool enabled = true;

        [Tooltip("Optional external texture (e.g. a downloaded/AI-generated sand or dirt PNG). " +
                 "Tiled across the object and multiplied by the tint. Leave empty to paint the flat tint alone.")]
        public Texture2D texture;

        [Min(0.05f), Tooltip("How many times the texture repeats across the sprite. 1 = stretched once, 4 = tiled 4x.")]
        public float textureTiling = 2f;

        [Tooltip("Layer colour. Multiplies the texture when one is assigned. Keep it grey-ish if the runtime tint should stay readable.")]
        public Color tint = new Color(0.75f, 0.7f, 0.6f);

        [Range(0f, 1f), Tooltip("How much of the rock this layer covers overall. 0 = nothing, 1 = everywhere.")]
        public float coverage = 0.45f;

        [Min(0.25f), Tooltip("Frequency of the noise mask that decides WHERE the layer sits. Low = large continuous patches, high = speckled.")]
        public float maskFrequency = 4f;

        [Range(0f, 1f), Tooltip("Softness of the patch edges. 0 = hard cutout, 1 = long soft blend.")]
        public float maskSoftness = 0.3f;

        [Range(-1f, 1f), Tooltip("Bias placement by surface relief: -1 = collects in cracks/valleys (sediment, dirt), " +
                                 "+1 = clings to ridges/peaks (dust, scraped highlights), 0 = ignores relief.")]
        public float heightBias = 0f;

        [Range(0f, 1f), Tooltip("Blend strength of the layer where its mask is solid.")]
        public float opacity = 1f;

        [Range(0f, 1f), Tooltip("How much the texture's brightness also bumps the normal map (adds tactile grain). 0 = colour only.")]
        public float bumpInfluence = 0.25f;
    }

    /** How a decal's colour is combined with the rock underneath it. */
    public enum DecalBlend
    {
        Normal,     // straight alpha-over (paint the decal on top)
        Multiply,   // darken (stains, dirt, shadowed growth)
        Screen,     // lighten (bleached spots, pale encrustation)
        Overlay,    // contrast-preserving mix (weathering that keeps rock detail)
        SoftLight,  // gentle tonal push (subtle discolouration)
        Additive    // add light (bright mineral / glow-ish deposits)
    }

    /** Where a decal layer's stamps are placed on the object. */
    public enum DecalPlacement
    {
        Explicit,       // one stamp at an exact position you set
        ScatterOnRock,  // N stamps scattered across the rock face (spread apart)
        AroundRim       // N stamps rooted along the rim (ledges, overhangs, edge growth)
    }

    /** How a decal contributes to the normal-map relief (so it catches the torch light). */
    public enum DecalReliefMode
    {
        None,       // colour only — flat, no bump
        Raise,      // the whole decal bulges out of the surface (protrusions, coral, barnacles)
        Carve,      // the decal presses INTO the surface (pits, holes, hydrothermal vents)
        Emboss      // use the decal's own brightness as height (fossils, engraved detail, cracks)
    }

    /**
     * One decal layer: a texture STAMPED a few times onto the rock (as opposed to a PaintLayer,
     * which tiles a material everywhere). This is the construct for one-off features — a
     * protrusion, an overhang ledge, a fossil, a coral clump, a vent — where you want direct
     * control over placement, size, rotation, how it blends, and how much it stands proud of
     * (or sinks into) the surface so URP 2D lighting reads it as real relief.
     *
     * The texture is expected to carry an ALPHA silhouette (our generated decals are chroma-keyed
     * to alpha). Stamps apply in list order over the rock + paint layers, beneath the crystals.
     */
    [Serializable]
    public class DecalLayer
    {
        [Tooltip("Label only — helps keep the list readable.")]
        public string name = "Decal";

        [Tooltip("Untick to skip this layer without deleting it.")]
        public bool enabled = true;

        [Required, Tooltip("The decal image (RGBA — its alpha is the stamp silhouette). Use the chroma-keyed decals from the generator.")]
        public Texture2D texture;

        // =====================
        // Placement
        // =====================

        [Title("Placement")]
        [Tooltip("Explicit = one stamp at an exact spot. ScatterOnRock = N stamps spread over the face. AroundRim = N stamps along the edge (overhangs/ledges).")]
        public DecalPlacement placement = DecalPlacement.ScatterOnRock;

        [ShowIf("@placement != DecalPlacement.Explicit"), PropertyRange(1, 12)]
        [Tooltip("How many stamps this layer places.")]
        public int count = 3;

        [ShowIf("@placement == DecalPlacement.Explicit")]
        [Tooltip("Stamp centre in normalized object space: (0,0) = middle, (±1,±1) = texture corners. The rock spans roughly ±Base Radius.")]
        public Vector2 position = Vector2.zero;

        [ShowIf("@placement != DecalPlacement.Explicit"), Range(0f, 1f)]
        [Tooltip("Keeps stamps from piling up: each tries several candidate spots and takes the one farthest from those already placed. 0 = fully random.")]
        public float separation = 0.6f;

        [MinMaxSlider(0.1f, 1.6f, true)]
        [Tooltip("Stamp size as a fraction of the object's width (1 = as wide as the rock). Sampled per stamp.")]
        public Vector2 scaleRange = new Vector2(0.35f, 0.55f);

        [Tooltip("Distribution for Scale: X = uniform roll, Y = position in the range (0 = min, 1 = max).")]
        public AnimationCurve scaleCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [MinMaxSlider(-180f, 180f, true)]
        [Tooltip("Random rotation range in degrees, sampled per stamp. Set both to 0 to keep decals upright.")]
        public Vector2 rotationRange = new Vector2(-180f, 180f);

        [ShowIf("@placement == DecalPlacement.AroundRim")]
        [Tooltip("Rotate each stamp so its 'up' points outward from the centre — for ledges/overhangs that should hang off the rim.")]
        public bool alignToRim = true;

        [ShowIf("@placement == DecalPlacement.AroundRim"), MinMaxSlider(0f, 0.6f, true)]
        [Tooltip("How far in from the rim each stamp roots (fraction of the rim distance). 0 = right on the edge.")]
        public Vector2 rimInsetRange = new Vector2(0f, 0.12f);

        [Tooltip("Nudges this layer's random stream so two layers using the same placement don't land identically.")]
        public int seedOffset = 0;

        // =====================
        // Appearance
        // =====================

        [Title("Appearance")]
        [Tooltip("How the decal colour combines with the rock underneath.")]
        public DecalBlend blendMode = DecalBlend.Normal;

        [Tooltip("Multiplies the decal colour. White = untouched. Tint stains toward a hue or knocks brightness down.")]
        public Color tint = Color.white;

        [Range(0f, 1f), Tooltip("Overall stamp strength.")]
        public float opacity = 1f;

        [Range(0f, 1f), Tooltip("Alpha threshold where the stamp's edge sits — raise to bite in tighter and drop faint halo pixels from the chroma key.")]
        public float alphaCutoff = 0.35f;

        [Range(0.01f, 0.5f), Tooltip("Softness of that edge. Low = crisp cutout, high = feathered blend into the rock.")]
        public float alphaSoftness = 0.12f;

        [Tooltip("On: the stamp is clipped to the existing rock silhouette (won't spill into open water).")]
        public bool clipToSilhouette = true;

        [HideIf(nameof(clipToSilhouette)), Tooltip("Only when Clip To Silhouette is off: the stamp EXTENDS the silhouette — its alpha adds to the object so it can stick out past the rim (true protrusions).")]
        public bool extendSilhouette = false;

        // =====================
        // Relief (lighting)
        // =====================

        [Title("Relief")]
        [Tooltip("How the decal bumps the normal map so the torch lights it. Raise = bulges out, Carve = sinks in, Emboss = brightness becomes height, None = flat colour.")]
        public DecalReliefMode reliefMode = DecalReliefMode.Raise;

        [ShowIf("@reliefMode != DecalReliefMode.None"), Range(0f, 1.5f)]
        [Tooltip("How strongly the decal stands proud of / sinks into the surface in the normal map.")]
        public float reliefStrength = 0.5f;

        [Tooltip("Give the decal its own specular-mask value (e.g. wet coral glints, dull fossil doesn't). Off = inherits the rock's spec level.")]
        public bool applySpec = false;

        [ShowIf(nameof(applySpec)), Range(0f, 1f), Tooltip("Specular-mask value baked where this decal sits.")]
        public float specValue = 0.5f;

        [ShowIf(nameof(applySpec))]
        [Tooltip("Glint colour baked into the spec mask where this decal sits (the mask's RGB tints the specular). White = uncoloured.")]
        public Color specColor = Color.white;
    }

    /**
     * Crystal growth settings — prismatic quartz-like spikes and/or druse (amethyst-bed)
     * patches. Crystals carry their own colour, raised faceted relief in the normal map,
     * and a high specular-mask value so they glint far more than the host rock.
     *
     * Every min/max range is paired with a DISTRIBUTION CURVE: X = a uniform roll (0..1),
     * Y = where in the range it lands (0 = min, 1 = max). Linear = uniform spread;
     * an ease-in curve = mostly small with rare big ones (e.g. 90% short / 10% long);
     * a stepped curve = distinct tiers (e.g. two rim rings at different insets).
     */
    [Serializable]
    public class CrystalSettings
    {
        [Tooltip("Prisms = elongated spikes fanning out of the rock (quartz points). " +
                 "Druse = patches of tightly packed faceted cells (amethyst geode bed).")]
        public CrystalStyle style = CrystalStyle.None;

        // =====================
        // Shared look
        // =====================

        [ShowIf("@style != CrystalStyle.None")]
        [Tooltip("Crystal body colour (baked into the albedo — unlike the grey rock this is NOT meant to be tinted at runtime, " +
                 "see 'Separate Crystal Layer' in Output).")]
        public Color color = new Color(0.72f, 0.5f, 0.95f);

        [ShowIf("@style != CrystalStyle.None"), Range(0f, 0.5f)]
        [Tooltip("Per-crystal random colour variation (value/hue jitter) so a cluster doesn't look uniform.")]
        public float colorVariation = 0.15f;

        [ShowIf("@style != CrystalStyle.None"), Range(0f, 1f)]
        [Tooltip("How much brighter the crystal gets toward its tip/termination — sells translucency.")]
        public float tipBrightness = 0.7f;

        [ShowIf("@style != CrystalStyle.None"), Range(0f, 1f)]
        [Tooltip("Brightness split between crystal facets. Higher = harder, more chiselled read.")]
        public float facetContrast = 0.55f;

        [ShowIf("@style != CrystalStyle.None"), MinMaxSlider(0f, 3f, true)]
        [Tooltip("How proud of the rock the crystals stand in the normal map (relief height multiplier). " +
                 "Sampled per crystal/patch from this range — mix short stubs with tall spikes.")]
        public Vector2 heightScaleRange = new Vector2(0.8f, 1.2f);

        [ShowIf("@style != CrystalStyle.None")]
        [Tooltip("Distribution for Height Scale: X = uniform roll, Y = position in the range (0 = min, 1 = max).")]
        public AnimationCurve heightScaleCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [ShowIf("@style != CrystalStyle.None"), Range(0f, 1f)]
        [Tooltip("Specular-mask value baked where crystals are (1 = full glint). The rock body uses the much lower 'Rock Spec Level'.")]
        public float specMaskValue = 1f;

        [ShowIf("@style != CrystalStyle.None")]
        [Tooltip("Crystals glint in their OWN (per-crystal jittered) colour — amethyst flashes purple. " +
                 "Off = use the flat Spec Color below instead.")]
        public bool specUseCrystalColor = true;

        [ShowIf("@!specUseCrystalColor && style != CrystalStyle.None")]
        [Tooltip("Flat glint colour baked into the spec mask where crystals are. White = uncoloured " +
                 "(whatever the material/SpecularController _SpecColor gives).")]
        public Color specColor = Color.white;

        [ShowIf("@style != CrystalStyle.None"), Range(0f, 1f)]
        [Tooltip("Keeps prism clusters / druse patches from stacking on top of each other: each placement tries several " +
                 "candidate spots and takes the one farthest from the clusters already placed. 0 = fully random " +
                 "(may stack), 1 = strongly spread apart; mid values still allow a little overlap.")]
        public float clusterSeparation = 0.6f;

        // =====================
        // Prisms (quartz spikes)
        // =====================

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [PropertyRange(1, 16), Tooltip("How many prism clusters to grow on the rock.")]
        public int clusterCount = 2;

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [MinMaxSlider(1, 16, true), Tooltip("Crystals per cluster, sampled per cluster — varied bundle sizes read more natural.")]
        public Vector2 crystalsPerCluster = new Vector2(3, 7);

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [Tooltip("Distribution for Crystals Per Cluster: X = uniform roll, Y = position in the range.")]
        public AnimationCurve crystalsPerClusterCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [MinMaxSlider(0.05f, 0.7f, true), Tooltip("Crystal length range (normalized: 0.5 = half the texture).")]
        public Vector2 lengthRange = new Vector2(0.15f, 0.3f);

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [Tooltip("Distribution for Length: X = uniform roll, Y = position in the range. Ease-in = mostly short, rare long spears.")]
        public AnimationCurve lengthCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [MinMaxSlider(0.01f, 0.2f, true), Tooltip("Crystal width range (normalized).")]
        public Vector2 widthRange = new Vector2(0.035f, 0.08f);

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [Tooltip("Distribution for Width: X = uniform roll, Y = position in the range (e.g. steep late rise = 90% thin, 10% fat).")]
        public AnimationCurve widthCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [Range(0f, 1f), Tooltip("Fan spread of a cluster: 0 = parallel needles, 1 = wide starburst.")]
        public float spread = 0.4f;

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [Range(0.05f, 0.9f), Tooltip("Fraction of each crystal's length that tapers to the point.")]
        public float tipFraction = 0.35f;

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [Range(0.02f, 0.6f), Tooltip("Fraction of each crystal's length blended into the rock at its base — fades the crystal's " +
                                     "colour/height smoothly out of the surface instead of a hard edge.")]
        public float rootBlend = 0.25f;

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [Tooltip("On: clusters root along the rock's rim and point outward (spikes sticking out of the mass). " +
                 "Off: clusters grow anywhere, pointing in random directions.")]
        public bool growFromRim = true;

        [BoxGroup("Prisms"), ShowIf("@growFromRim && (style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse)")]
        [MinMaxSlider(0f, 1f, true), Tooltip("How far in from the rim each cluster roots, as a fraction of the rim distance: " +
                                             "0 = right at the edge, 1 = at the centre. Sampled per cluster.")]
        public Vector2 rimInsetRange = new Vector2(0.1f, 0.2f);

        [BoxGroup("Prisms"), ShowIf("@growFromRim && (style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse)")]
        [Tooltip("Distribution for Rim Inset: X = uniform roll, Y = position in the range. A two-step curve over a wide range " +
                 "creates TWO distinct rings (outer rim + inner rim); more steps = more rings.")]
        public AnimationCurve rimInsetCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [BoxGroup("Prisms"), ShowIf("@style == CrystalStyle.Prisms || style == CrystalStyle.PrismsAndDruse")]
        [Range(0f, 0.5f), Tooltip("Faint noisy streaks along each crystal's length (internal fractures / milkiness).")]
        public float streakStrength = 0.15f;

        // =====================
        // Druse (amethyst bed)
        // =====================

        [BoxGroup("Druse"), ShowIf("@style == CrystalStyle.Druse || style == CrystalStyle.PrismsAndDruse")]
        [PropertyRange(1, 8), Tooltip("How many druse patches to place on the rock face.")]
        public int drusePatchCount = 2;

        [BoxGroup("Druse"), ShowIf("@style == CrystalStyle.Druse || style == CrystalStyle.PrismsAndDruse")]
        [MinMaxSlider(0.04f, 0.4f, true), Tooltip("Patch radius range (normalized). Sampled per patch.")]
        public Vector2 drusePatchRadius = new Vector2(0.1f, 0.18f);

        [BoxGroup("Druse"), ShowIf("@style == CrystalStyle.Druse || style == CrystalStyle.PrismsAndDruse")]
        [Tooltip("Distribution for Patch Radius: X = uniform roll, Y = position in the range.")]
        public AnimationCurve drusePatchRadiusCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [BoxGroup("Druse"), ShowIf("@style == CrystalStyle.Druse || style == CrystalStyle.PrismsAndDruse")]
        [PropertyRange(4f, 40f), Tooltip("Cell density of the crystal bed — how many little terminations per texture width. " +
                                         "Low = chunky cubes, high = fine sugar-crystal sparkle.")]
        public float druseDensity = 16f;
    }

    /**
     * Every parameter of one terrain-object generation run. This is the unit presets save
     * and the window edits — keep it a plain serializable data class (no logic) so it can
     * round-trip through EditorJsonUtility for preset save/load and preview cloning.
     */
    [Serializable]
    public class TerrainObjectSettings
    {
        // =====================
        // Output
        // =====================

        [TabGroup("Tabs", "Output"), FolderPath, Tooltip("Folder the PNG sets are written to (created if missing).")]
        public string outputFolder = "Assets/Submachina/Art/Terrain";

        [TabGroup("Tabs", "Output"), Tooltip("Base file name: outputs land as {Name}_{i}_albedo.png / _normal.png / _mask.png.")]
        public string baseName = "Rock";

        [TabGroup("Tabs", "Output"), Tooltip("Base seed. Each variant offsets from this so results are reproducible.")]
        public int seed = 12345;

        [TabGroup("Tabs", "Output"), PropertyRange(1, 24), Tooltip("How many distinct variants to bake.")]
        public int variantCount = 5;

        [TabGroup("Tabs", "Output"), PropertyRange(64, 1024), Tooltip("Texture size in pixels (square). Crystals benefit from 512.")]
        public int resolution = 256;

        [TabGroup("Tabs", "Output"), Min(1f), Tooltip("Sprite pixels-per-unit. 256 gives a ~1 world-unit object at default scale.")]
        public float pixelsPerUnit = 256f;

        [TabGroup("Tabs", "Output"), Tooltip("Also bake a {Name}_{i}_mask.png (R = per-pixel specular multiplier) and attach it as the " +
                                             "sprite's '_SpecMask' Secondary Texture — lets crystals glint while the rock stays dull.")]
        public bool bakeSpecMask = true;

        [TabGroup("Tabs", "Output"), ShowIf(nameof(bakeSpecMask)), Range(0f, 1f)]
        [Tooltip("Specular-mask level for the plain rock body (crystals/specks bake much higher). " +
                 "~0.2 keeps rock subdued; 1 matches the old un-masked behaviour.")]
        public float rockSpecLevel = 0.25f;

        [TabGroup("Tabs", "Output"), ShowIf(nameof(bakeSpecMask))]
        [Tooltip("Glint COLOUR baked into the spec mask for the rock body and its mineral specks — the mask's RGB " +
                 "tints the specular per pixel (e.g. warm gold for pyrite specks). White = uncoloured, i.e. whatever " +
                 "the material/SpecularController _SpecColor gives.")]
        public Color rockSpecColor = Color.white;

        [TabGroup("Tabs", "Output")]
        [Tooltip("On: crystals bake into their OWN sprite pair ({Name}_{i}_crystal_albedo/_normal[/_mask]) instead of the rock texture. " +
                 "Stack it on a child SpriteRenderer so the rock can be runtime-tinted (heat lerp) while crystals keep their true colour.")]
        public bool separateCrystalLayer = false;

        [TabGroup("Tabs", "Output")]
        [Tooltip("Bake a tint straight into the rock albedo (instead of relying on SpriteRenderer.color at runtime). " +
                 "Applies to combined-mode crystals too; a separate crystal layer stays untinted. " +
                 "The Lit preview stops applying its own scene tint while this is on (it would double up).")]
        public bool bakeTint = false;

        [TabGroup("Tabs", "Output"), ShowIf(nameof(bakeTint))]
        [Tooltip("The tint multiplied into the final albedo when Bake Tint is on. Its ALPHA is the tint opacity: " +
                 "1 = fully tinted, 0.5 = halfway between the untinted albedo and the full tint, 0 = no effect.")]
        public Color tintColor = new Color(0.42f, 0.52f, 0.78f);

        // =====================
        // Shape
        // =====================

        [TabGroup("Tabs", "Shape"), Tooltip("Silhouette family. Blob = wobbly circle, Boulder = squarish mass, Slab = wide flat chunk, " +
                                            "Shard = angular polygon, Cluster = several blobs merged into one lumpy mass.")]
        public SilhouetteShape shape = SilhouetteShape.Blob;

        [TabGroup("Tabs", "Shape"), PropertyRange(0.2f, 0.49f), Tooltip("Object radius in normalized space (0.5 = touches texture edge). Leave margin for crystals that stick out.")]
        public float baseRadius = 0.38f;

        [TabGroup("Tabs", "Shape"), PropertyRange(0.35f, 2.5f), Tooltip("Width/height stretch. >1 = wide and squat, <1 = tall and narrow.")]
        public float aspect = 1f;

        [TabGroup("Tabs", "Shape"), Tooltip("Randomly rotate each variant's whole silhouette so a batch doesn't share one orientation.")]
        public bool randomizeRotation = true;

        [TabGroup("Tabs", "Shape"), ShowIf("@shape == SilhouetteShape.Boulder || shape == SilhouetteShape.Slab")]
        [PropertyRange(1.5f, 8f), Tooltip("Superellipse exponent: 2 = ellipse, higher = squarer with rounded corners.")]
        public float squareness = 3.5f;

        [TabGroup("Tabs", "Shape"), ShowIf(nameof(shape), SilhouetteShape.Shard)]
        [PropertyRange(3, 12), Tooltip("Corner count of the shard polygon.")]
        public int shardVertices = 6;

        [TabGroup("Tabs", "Shape"), ShowIf(nameof(shape), SilhouetteShape.Shard)]
        [Range(0f, 1f), Tooltip("Random radius variance per corner — 0 = regular polygon, 1 = jagged broken shard.")]
        public float shardIrregularity = 0.45f;

        [TabGroup("Tabs", "Shape"), ShowIf(nameof(shape), SilhouetteShape.Shard)]
        [Range(0f, 1f), Tooltip("Corner rounding: 0 = razor-flat facet edges, 1 = fully softened back to a blob.")]
        public float shardRounding = 0.15f;

        [TabGroup("Tabs", "Shape"), ShowIf(nameof(shape), SilhouetteShape.Cluster)]
        [PropertyRange(2, 6), Tooltip("How many lobes merge into the cluster mass.")]
        public int lobeCount = 3;

        [TabGroup("Tabs", "Shape"), ShowIf(nameof(shape), SilhouetteShape.Cluster)]
        [Range(0f, 1f), Tooltip("How far lobes scatter from the centre. 0 = concentric (near-blob), 1 = loose bunch-of-grapes.")]
        public float lobeSpread = 0.55f;

        [TabGroup("Tabs", "Shape"), ShowIf(nameof(shape), SilhouetteShape.Cluster)]
        [Range(0f, 1f), Tooltip("Random size variance between lobes.")]
        public float lobeSizeVariance = 0.4f;

        [TabGroup("Tabs", "Shape"), ShowIf(nameof(shape), SilhouetteShape.Cluster)]
        [PropertyRange(0.01f, 0.3f), Tooltip("Blend radius where lobes merge — low = distinct pebbles touching, high = one melted mass.")]
        public float lobeSmoothness = 0.12f;

        [TabGroup("Tabs", "Shape"), PropertyRange(0f, 0.2f), Tooltip("How much the silhouette wobbles in/out — higher = chunkier, more irregular rim.")]
        public float edgeNoiseAmount = 0.08f;

        [TabGroup("Tabs", "Shape"), PropertyRange(1f, 12f), Tooltip("Frequency of the rim wobble — higher = more, smaller lumps.")]
        public float edgeNoiseFrequency = 3f;

        [TabGroup("Tabs", "Shape"), PropertyRange(0f, 0.35f), Tooltip("Domain warp: smears the whole silhouette (and its coordinate space) for gnarled, " +
                                                                      "organic outlines. 0 = off. Great on Shard/Boulder to break up clean edges.")]
        public float warpAmount = 0f;

        [TabGroup("Tabs", "Shape"), PropertyRange(0.5f, 8f), ShowIf("@warpAmount > 0f"), Tooltip("Frequency of the domain warp.")]
        public float warpFrequency = 2.5f;

        // =====================
        // Surface (albedo detail)
        // =====================

        [TabGroup("Tabs", "Surface"), Tooltip("Base colour before shading. Keep it neutral grey (~0.8) if the runtime SpriteRenderer tint " +
                                              "should drive the final colour (ore heat-lerp etc.); use real colour for standalone props.")]
        public Color baseColor = new Color(0.8f, 0.8f, 0.8f);

        [TabGroup("Tabs", "Surface"), PropertyRange(0f, 1f), Tooltip("Contrast of the rocky surface noise. Higher reads more polished/metallic.")]
        public float surfaceContrast = 0.5f;

        [TabGroup("Tabs", "Surface"), PropertyRange(1f, 16f), Tooltip("Frequency of the rocky surface noise.")]
        public float surfaceFrequency = 6f;

        [TabGroup("Tabs", "Surface"), PropertyRange(0f, 1f), Tooltip("Strength of dark cracks/facets carved by cellular noise.")]
        public float crackStrength = 0.45f;

        [TabGroup("Tabs", "Surface"), PropertyRange(1f, 16f), Tooltip("Frequency of the crack/facet pattern.")]
        public float crackFrequency = 5f;

        [TabGroup("Tabs", "Surface"), PropertyRange(0f, 1f), Tooltip("Rim ambient-occlusion: darkens edges so the object reads as a rounded mass even unlit.")]
        public float aoStrength = 0.45f;

        [TabGroup("Tabs", "Surface"), PropertyRange(0f, 0.08f), Tooltip("Chance per pixel of a bright mineral speck (metallic sparkle).")]
        public float speckChance = 0.018f;

        [TabGroup("Tabs", "Surface"), PropertyRange(0f, 1f), Tooltip("How bright each mineral speck is.")]
        public float speckBrightness = 0.85f;

        [TabGroup("Tabs", "Surface"), Tooltip("Specks also bake a high value into the specular mask so they glint under the torch.")]
        public bool specksAreShiny = true;

        [TabGroup("Tabs", "Surface"), Range(0f, 4f), Tooltip("Sedimentary strata bands: 0 = off. Darkens/bumps parallel layers across the object " +
                                                             "(very Factorio). Above ~1 the bands cut deep grooves and darken toward black. " +
                                                             "Combine with Slab shape for ledge chunks.")]
        public float strataStrength = 0f;

        [TabGroup("Tabs", "Surface"), ShowIf("@strataStrength > 0f"), PropertyRange(1f, 12f), Tooltip("Number of strata bands across the object.")]
        public float strataFrequency = 4f;

        [TabGroup("Tabs", "Surface"), ShowIf("@strataStrength > 0f"), Range(-90f, 90f), Tooltip("Band angle in degrees (0 = horizontal layers).")]
        public float strataAngle = 12f;

        [TabGroup("Tabs", "Surface"), ShowIf("@strataStrength > 0f"), Range(0f, 1f), Tooltip("How much noise bends the bands (0 = ruler-straight).")]
        public float strataWarp = 0.4f;

        // =====================
        // Normal map (lighting relief)
        // =====================

        [TabGroup("Tabs", "Normal"), PropertyRange(0f, 2f), Tooltip("Height of the overall raised mass — makes the object look physically rounded under light.")]
        public float domeStrength = 1.5f;

        [TabGroup("Tabs", "Normal"), PropertyRange(0.15f, 3f), Tooltip("Profile of that mass: ~0.3 = flat plateau with steep sides (slab), " +
                                                                       "0.5 = rounded boulder, 1+ = soft mound peaking at centre.")]
        public float domeProfile = 0.5f;

        [TabGroup("Tabs", "Normal"), PropertyRange(0f, 1f), Tooltip("Smooth medium-frequency lobes in the normal — a few clean facets give a coherent metallic sheen.")]
        public float facetStrength = 0.35f;

        [TabGroup("Tabs", "Normal"), PropertyRange(1f, 8f), Tooltip("Frequency of the smooth facets. Low = a few big lobes; high = busier.")]
        public float facetFrequency = 3f;

        [TabGroup("Tabs", "Normal"), PropertyRange(0f, 1f), Tooltip("Faint high-frequency crag detail in the normal. Keep low for a polished look.")]
        public float detailStrength = 0.15f;

        [TabGroup("Tabs", "Normal"), PropertyRange(0.5f, 10f), Tooltip("Overall normal steepness. Higher = tighter, shinier highlights.")]
        public float normalStrength = 5f;

        // =====================
        // Paint layers
        // =====================

        [TabGroup("Tabs", "Layers")]
        [ListDrawerSettings(ShowFoldout = true), Tooltip("Optional material layers painted over the rock (sand, dirt, rust, moss…). " +
                                                         "Applied top-to-bottom; each uses a noise mask optionally biased into crevices or onto ridges.")]
        public List<PaintLayer> paintLayers = new List<PaintLayer>();

        // =====================
        // Decals (stamped one-off features)
        // =====================

        [TabGroup("Tabs", "Decals")]
        [ListDrawerSettings(ShowFoldout = true), Tooltip("One-off stamped features (protrusions, overhangs, fossils, coral, vents…). " +
                                                         "Unlike paint layers these are placed a few times with explicit scale/rotation/blend/relief control. " +
                                                         "Applied over the rock + paint layers, beneath the crystals.")]
        public List<DecalLayer> decalLayers = new List<DecalLayer>();

        // =====================
        // Crystals
        // =====================

        [TabGroup("Tabs", "Crystals"), HideLabel, InlineProperty]
        public CrystalSettings crystals = new CrystalSettings();
    }
}
#endif
