using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Generic per-instance driver for the SpriteLitSpecular material (shader
     * Submachina/2D/SpriteLitSpecular) using a MaterialPropertyBlock. Drop it on any
     * shiny sprite (metal, gems, ice, wet rock…) that should glint when a `SpecularLight2D`
     * sweeps over it.
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
         * own `_NormalMap` (bespoke relief); the rest are procedural patterns generated in the
         * shader from the sprite's UV — instant glint for generic sprites with no authored map.
         */
        public enum NormalSource { SpriteNormalMap = 0, Dome = 1, Bevel = 2, Ripples = 3, Radial = 4, Facets = 5, NormalTexture = 6 }

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

        // Cached shader property ids (avoids string hashing every write)
        private static readonly int SpecColorID = Shader.PropertyToID("_SpecColor");
        private static readonly int SpecPowerID = Shader.PropertyToID("_SpecPower");
        private static readonly int SpecIntensityID = Shader.PropertyToID("_SpecIntensity");
        private static readonly int SpecLightDirID = Shader.PropertyToID("_SpecLightDir");
        private static readonly int LightResponseID = Shader.PropertyToID("_LightResponse");
        private static readonly int SpecBoostID = Shader.PropertyToID("_SpecBoost");
        private static readonly int SpecReplaceID = Shader.PropertyToID("_SpecReplace");
        private static readonly int SpecClampID = Shader.PropertyToID("_SpecClamp");
        private static readonly int SpecAlbedoTintID = Shader.PropertyToID("_SpecAlbedoTint");
        private static readonly int SpecScreenID = Shader.PropertyToID("_SpecScreen");
        private static readonly int SpecViewBiasID = Shader.PropertyToID("_SpecViewBias");
        private static readonly int DiffNormalStrengthID = Shader.PropertyToID("_DiffNormalStrength");
        private static readonly int ShimmerAmpID = Shader.PropertyToID("_ShimmerAmp");
        private static readonly int ShimmerSpeedID = Shader.PropertyToID("_ShimmerSpeed");
        private static readonly int ShimmerPhaseID = Shader.PropertyToID("_ShimmerPhase");
        private static readonly int ShimmerWaveID = Shader.PropertyToID("_ShimmerWave");
        private static readonly int ShimmerModeID = Shader.PropertyToID("_ShimmerMode");
        private static readonly int DirWobbleID = Shader.PropertyToID("_DirWobble");
        private static readonly int NormalModeID = Shader.PropertyToID("_NormalMode");
        private static readonly int NormalStrengthID = Shader.PropertyToID("_NormalStrength");
        private static readonly int NormalFreqID = Shader.PropertyToID("_NormalFreq");
        private static readonly int NormalUVRectID = Shader.PropertyToID("_NormalUVRect");
        private static readonly int NormalTexID = Shader.PropertyToID("_NormalTex");
        private static readonly int NormalTexSTID = Shader.PropertyToID("_NormalTexST");
        private static readonly int SortingLayerBitID = Shader.PropertyToID("_SortingLayerBit");

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
                 "generated in-shader from the sprite UV (no texture needed). Dome = round bulge, Bevel = rim only, " +
                 "Ripples = wavy bands, Radial = concentric rings, Facets = sparkly cells.")]
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

        [FoldoutGroup("Surface Normal"), HideIf(nameof(IsBaselineNormalMode))]
        [Tooltip("Wave count (Ripples/Radial) or cell count (Facets). Ignored by Dome/Bevel.")]
        [SerializeField, Min(0.01f)] private float normalFrequency = 8f;

        // Strength/frequency only matter for the procedural patterns (not the two texture modes).
        private bool IsBaselineNormalMode =>
            normalSource == NormalSource.SpriteNormalMap || normalSource == NormalSource.NormalTexture;

        // =====================
        // Transient flare (one-shot pulses)
        // =====================

        [FoldoutGroup("Transient Flare")]
        [Tooltip("How fast a Pulse() flash decays back to zero (units/sec).")]
        [SerializeField, Min(0f)] private float pulseDecay = 4f;

        // =====================
        // Internals
        // =====================

        private SpriteRenderer[] _renderers;
        private MaterialPropertyBlock _mpb;
        private float _phase;            // per-instance offset so a field of sprites doesn't shimmer in unison

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
         * shader, so this does NOT need to run per frame.
         */
        private void ApplyBaseline()
        {
            if (_renderers == null) return;

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

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(SpecPowerID, specPower);
                _mpb.SetFloat(SpecViewBiasID, specViewBias);
                _mpb.SetFloat(SpecIntensityID, baseIntensity);
                _mpb.SetVector(SpecLightDirID, dir);
                _mpb.SetFloat(LightResponseID, illuminationResponse);
                _mpb.SetFloat(ShimmerAmpID, shimmerAmp);
                _mpb.SetFloat(ShimmerSpeedID, modSpeed);
                _mpb.SetFloat(ShimmerPhaseID, shimmerPhase);
                _mpb.SetFloat(ShimmerWaveID, (float)(int)modWaveform);
                _mpb.SetFloat(ShimmerModeID, (float)(int)modTarget);
                _mpb.SetVector(DirWobbleID, dirWobble);
                _mpb.SetFloat(SpecBoostID, 0f);
                // Albedo balance: additive vs energy-conserving replace, plus optional glint ceiling
                _mpb.SetFloat(SpecReplaceID, specReplace);
                _mpb.SetFloat(SpecClampID, specClamp);
                // Texture-through-the-glint controls: albedo-tinted spec + screen-softened additive
                _mpb.SetFloat(SpecAlbedoTintID, specAlbedoTint);
                _mpb.SetFloat(SpecScreenID, specScreen);
                // Procedural-normal params (the UV rect is per-sprite so patterns stay centered on atlases)
                _mpb.SetFloat(NormalModeID, normalMode);
                _mpb.SetFloat(NormalStrengthID, normalStrength);
                _mpb.SetFloat(DiffNormalStrengthID, diffuseNormalStrength);
                _mpb.SetFloat(NormalFreqID, normalFrequency);
                _mpb.SetVector(NormalUVRectID, SpriteUVRect(r));
                // Inline normal-map override: bind the texture + its tiling/offset so the shader
                // (mode 6) samples it, scaled/positioned to fit the sprite. If none is assigned the
                // shader falls back to the material's flat "bump" default.
                if (normalSource == NormalSource.NormalTexture)
                {
                    if (normalTexture != null) _mpb.SetTexture(NormalTexID, normalTexture);
                    _mpb.SetVector(NormalTexSTID, new Vector4(
                        normalTextureTiling.x, normalTextureTiling.y, normalTextureOffset.x, normalTextureOffset.y));
                }
                if (overrideColor) _mpb.SetColor(SpecColorID, specColor);
                // Sorting layer bit so the shader only responds to lights targeting this layer
                _mpb.SetFloat(SortingLayerBitID, (float)SpecularLight2DManager.SortingLayerBit(r.sortingLayerID));
                r.SetPropertyBlock(_mpb);
            }

            _lastBoost = 0f;
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
            }
        }

        // -------------------------------------------------------
        // Editor convenience
        // -------------------------------------------------------

        /** Lazily cache the renderers + property block (Awake, edit-mode ApplyTint/OnValidate). */
        private void EnsureInitialized()
        {
            if (_renderers == null) _renderers = GetComponentsInChildren<SpriteRenderer>(true);
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
