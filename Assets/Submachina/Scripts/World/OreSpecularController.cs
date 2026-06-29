using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Per-instance driver for the OreLit specular material (shader
     * Submachina/2D/OreLitSpecular) using a MaterialPropertyBlock.
     *
     * Why a MaterialPropertyBlock: it overrides shader properties for THIS renderer
     * only, so every rock can have its own colour / shininess / dynamic response
     * without cloning the material asset (no copies to maintain, no global edits,
     * no per-instance material leaks).
     *
     * Composition model — this is the ONLY thing that writes the block, and it
     * combines every contribution each frame so they never fight each other:
     *
     *   intensity = baseIntensity * (1 + idleMod)      // baseline + idle wobble
     *             + illumination * illuminationResponse // pushed in by a nearby light
     *             + pulse                               // one-shot flashes (MMF/Animator)
     *
     * Drop it on the rock root; it drives every child nugget renderer. It works
     * standalone (baseline + idle modulation); wire a SubLightOreIlluminator to feed
     * SetIllumination() for proximity reactivity.
     */
    public class OreSpecularController : MonoBehaviour
    {
        // Cached shader property ids (avoids string hashing every frame)
        private static readonly int SpecColorID = Shader.PropertyToID("_SpecColor");
        private static readonly int SpecPowerID = Shader.PropertyToID("_SpecPower");
        private static readonly int SpecIntensityID = Shader.PropertyToID("_SpecIntensity");
        private static readonly int SpecLightDirID = Shader.PropertyToID("_SpecLightDir");

        public enum ModulationType { Sine, PingPong, Perlin }

        // =====================
        // Baseline (per-instance look — answers "variations without copies")
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
        [Tooltip("Resting specular intensity before any modulation or light reactivity.")]
        [SerializeField, Min(0f)] private float baseIntensity = 1.6f;

        [FoldoutGroup("Baseline")]
        [Tooltip("Default glint direction in sprite space (x right, y up). Used when no light is reacting.")]
        [SerializeField] private Vector2 baseLightDir = new Vector2(-0.45f, 0.6f);

        // =====================
        // Idle modulation (back-and-forth / random shimmer)
        // =====================

        [FoldoutGroup("Idle Modulation")]
        [Tooltip("Continuously wobble the specular intensity for a living, shimmering ore.")]
        [SerializeField] private bool animate = false;

        [FoldoutGroup("Idle Modulation"), ShowIf(nameof(animate))]
        [SerializeField] private ModulationType modulation = ModulationType.Sine;

        [FoldoutGroup("Idle Modulation"), ShowIf(nameof(animate))]
        [Tooltip("Wobble size as a fraction of base intensity (0.25 = ±25%).")]
        [SerializeField, Range(0f, 1f)] private float modAmplitude = 0.25f;

        [FoldoutGroup("Idle Modulation"), ShowIf(nameof(animate))]
        [SerializeField, Min(0f)] private float modSpeed = 1.5f;

        // =====================
        // Light reactivity (fed by SubLightOreIlluminator)
        // =====================

        [FoldoutGroup("Light Reactivity")]
        [Tooltip("Extra specular intensity added at full illumination (proximity = 1).")]
        [SerializeField, Min(0f)] private float illuminationResponse = 2.0f;

        [FoldoutGroup("Light Reactivity")]
        [Tooltip("Rotate the glint to face the illuminating light (so it tracks the sub).")]
        [SerializeField] private bool aimGlintAtLight = true;

        [FoldoutGroup("Light Reactivity")]
        [Tooltip("How fast illumination eases in/out. Out-of-range ore fades to 0 over ~1/this seconds.")]
        [SerializeField, Min(0.1f)] private float illumFadeSpeed = 6f;

        [FoldoutGroup("Light Reactivity")]
        [Tooltip("How fast a Pulse() flash decays back to zero (units/sec).")]
        [SerializeField, Min(0f)] private float pulseDecay = 4f;

        // =====================
        // Mining glow (driven by MiningResource)
        // =====================

        [FoldoutGroup("Mining Glow")]
        [Tooltip("Extra specular intensity added at full mining glow (SetMiningGlow(1)). " +
                 "Lets a MiningResource make the ore flare up as the laser mines it.")]
        [SerializeField, Min(0f)] private float miningGlowResponse = 3f;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired the first frame this ore becomes lit by a light (illumination rises above ~0).")]
        public UnityEvent onBecameLit;

        // =====================
        // Internals
        // =====================

        private SpriteRenderer[] _renderers;
        private MaterialPropertyBlock _mpb;
        private float _phase;            // per-instance offset so a field of ore doesn't pulse in unison

        private float _illumCurrent;     // smoothed 0..1 illumination actually applied
        private float _illumTarget;      // latest value pushed by a light
        private float _illumStamp;       // Time.time of the last push (for fade-out when out of range)
        private Vector2 _lightWorldDir = Vector2.up;
        private float _pulse;
        private float _miningGlow;       // 0..1, set by a MiningResource while the laser mines this ore
        private bool _wasLit;

        // Last-applied values for a cheap dirty check (skip MPB writes when nothing moved)
        private float _lastIntensity = float.NaN;
        private Vector4 _lastDir;

        /**
         * Caches the nugget renderers and the reusable property block.
         */
        private void Awake()
        {
            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            _mpb = new MaterialPropertyBlock();

            // Stable per-instance phase from world position (no global RNG needed)
            float h = transform.position.x * 12.9898f + transform.position.y * 78.233f;
            _phase = h - Mathf.Floor(h);
        }

        private void OnEnable() => Apply(force: true);

        // -------------------------------------------------------
        // Public API (drivers call these)
        // -------------------------------------------------------

        /**
         * Pushed by a nearby light each tick. value is 0..1 proximity/strength;
         * worldDirToLight points from this ore toward the light (used to aim the glint).
         * Not calling this lets illumination fade back to zero on its own.
         */
        public void SetIllumination(float value, Vector2 worldDirToLight)
        {
            _illumTarget = Mathf.Clamp01(value);
            _illumStamp = Time.time;
            if (aimGlintAtLight && worldDirToLight.sqrMagnitude > 1e-4f)
                _lightWorldDir = worldDirToLight.normalized;
        }

        /**
         * Adds a one-shot additive flash to the specular (e.g. an MMF feedback or
         * Animator event on a hit). Decays back to zero via pulseDecay. Because it's
         * additive, it layers on top of light reactivity instead of overwriting it.
         */
        public void Pulse(float amount) => _pulse += Mathf.Max(0f, amount);

        /**
         * Sets a sustained mining glow (0..1) that ramps the specular up while the laser
         * is mining this ore. Additive, so it layers on top of light reactivity and idle
         * modulation. Call with 0 to clear (MiningLaser does this when the beam leaves).
         */
        public void SetMiningGlow(float glow01) => _miningGlow = Mathf.Clamp01(glow01);

        // -------------------------------------------------------
        // Per-frame compose + write
        // -------------------------------------------------------

        /**
         * Eases illumination, advances idle modulation, decays any pulse, then composes
         * the final specular and writes it to every nugget (only when it actually changed).
         */
        private void Update()
        {
            // Fade illumination toward its target; when no light has pushed recently, target → 0
            if (Time.time - _illumStamp > 0.2f) _illumTarget = 0f;
            _illumCurrent = Mathf.MoveTowards(_illumCurrent, _illumTarget, illumFadeSpeed * Time.deltaTime);

            // Edge event: just became lit
            if (!_wasLit && _illumCurrent > 0.01f) { _wasLit = true; onBecameLit?.Invoke(); }
            else if (_wasLit && _illumCurrent <= 0.01f) { _wasLit = false; }

            // Decay any one-shot pulse
            if (_pulse > 0f) _pulse = Mathf.Max(0f, _pulse - pulseDecay * Time.deltaTime);

            Apply();
        }

        /**
         * Combines baseline, idle modulation, illumination and pulse into the final
         * specular state and pushes it through the MaterialPropertyBlock.
         */
        private void Apply(bool force = false)
        {
            if (_renderers == null) return;

            // Idle modulation as a fraction of base intensity
            float idle = 0f;
            if (animate)
            {
                float t = Time.time * modSpeed + _phase * 6.2831853f;
                switch (modulation)
                {
                    case ModulationType.Sine: idle = Mathf.Sin(t); break;
                    case ModulationType.PingPong: idle = Mathf.PingPong(t, 2f) - 1f; break;
                    case ModulationType.Perlin: idle = Mathf.PerlinNoise(t, _phase) * 2f - 1f; break;
                }
                idle *= modAmplitude;
            }

            // Compose: base*(1+idle) + light + mining glow + pulse (all additive)
            float intensity = baseIntensity * (1f + idle)
                            + _illumCurrent * illuminationResponse
                            + _miningGlow * miningGlowResponse
                            + _pulse;

            // Glint direction eases from baseline toward the light as illumination rises
            Vector2 dir2 = aimGlintAtLight
                ? Vector2.Lerp(baseLightDir.normalized, _lightWorldDir, _illumCurrent)
                : baseLightDir.normalized;
            Vector4 dir = new Vector4(dir2.x, dir2.y, 0.66f, 0f); // z biases the half-vector toward the viewer

            // Dirty check — avoid touching the block (and breaking batching) when idle/static
            if (!force && Mathf.Abs(intensity - _lastIntensity) < 0.001f && dir == _lastDir) return;
            _lastIntensity = intensity;
            _lastDir = dir;

            // Write the same block to every nugget
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(SpecIntensityID, intensity);
                _mpb.SetFloat(SpecPowerID, specPower);
                _mpb.SetVector(SpecLightDirID, dir);
                if (overrideColor) _mpb.SetColor(SpecColorID, specColor);
                r.SetPropertyBlock(_mpb);
            }
        }

        // -------------------------------------------------------
        // Editor convenience
        // -------------------------------------------------------

        /** Re-apply when tweaking values in the inspector at edit time. */
        private void OnValidate()
        {
            if (!Application.isPlaying && _renderers == null && this != null)
                _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            Apply(force: true);
        }
    }
}
