using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a Sci-Fi Arsenal static beam prefab for 2D use.
     *
     * Replaces the stock SciFiArsenalBeamStatic script — this version does NOT
     * raycast on its own. Instead, MiningLaser calls SetBeam() each frame with
     * the beam start/end positions, whether the beam is hitting a target, and
     * the current mining progress.
     *
     * Up to four child objects are spawned from prefab references at startup:
     *   1. Beam line renderer — the core laser line with texture scrolling.
     *   2. Beam start VFX — particle glow at the emitter (turret tip).
     *   3. Beam end hit VFX — impact sparks, shown when hitting a target.
     *   4. Beam end idle VFX — tip effect shown when firing into open air (optional).
     *
     * Width pulsing keeps the beam alive visually. Mining-boost parameters
     * intensify scroll speed and pulse amplitude while actively mining, giving
     * the beam an escalating "energy transfer" feel as progress increases.
     *
     * Setup:
     *   1. Add this script to the beam GameObject (child of the turret).
     *   2. Assign the three prefab references from the Sci-Fi Arsenal beam setup.
     *   3. Remove the stock SciFiArsenalBeamStatic script.
     *   4. On MiningLaser, assign the Beam VFX reference to this component.
     */
    public class MiningBeamVFX : MonoBehaviour
    {
        // =====================
        // Prefabs
        // =====================

        [FoldoutGroup("Prefabs")]
        [Tooltip("Prefab containing the beam LineRenderer. Assigned from Sci-Fi Arsenal beam setup.")]
        [SerializeField] private GameObject beamLineRendererPrefab;

        [FoldoutGroup("Prefabs")]
        [Tooltip("Particle VFX at the beam origin (turret tip). Assigned from Sci-Fi Arsenal beam setup.")]
        [SerializeField] private GameObject beamStartPrefab;

        [FoldoutGroup("Prefabs")]
        [Tooltip("Impact VFX shown at the beam endpoint when hitting a target (sparks, glow, etc).")]
        [SerializeField] private GameObject beamEndHitPrefab;

        [FoldoutGroup("Prefabs")]
        [Tooltip("Idle VFX shown at the beam endpoint when NOT hitting a target. " +
                 "Leave empty to show nothing at the tip in open air.")]
        [SerializeField] private GameObject beamEndIdlePrefab;

        // =====================
        // Texture Scroll
        // =====================

        [FoldoutGroup("Scroll")]
        [Tooltip("Base speed the beam texture scrolls along the line. Negative reverses direction.")]
        [SerializeField] private float textureScrollSpeed = 15f;

        [FoldoutGroup("Scroll")]
        [Tooltip("Horizontal texture scale relative to beam length. " +
                 "Match to your texture's aspect ratio to avoid stretching.")]
        [SerializeField] private float textureLengthScale = 5f;

        // =====================
        // Width Pulse
        // =====================

        [FoldoutGroup("Pulse")]
        [Tooltip("Base width override for the beam. " +
                 "The prefab's original LineRenderer width is used when set to 0.")]
        [SerializeField, Min(0f)] private float baseWidth = 0f;

        [FoldoutGroup("Pulse")]
        [Tooltip("Peak width multiplier during the pulse cycle. 1.0 = no pulse.")]
        [SerializeField] private float widthMultiplier = 1.5f;

        [FoldoutGroup("Pulse")]
        [Tooltip("How fast the beam pulses between base and peak width.")]
        [SerializeField] private float pulseSpeed = 8f;

        // =====================
        // Mining Boost
        // =====================

        [FoldoutGroup("Mining Boost")]
        [Tooltip("Extra scroll speed added when the laser is actively mining. " +
                 "Scales with mining progress — e.g. at 50% progress, half of this value is added.")]
        [SerializeField] private float miningScrollBoost = 8f;

        [FoldoutGroup("Mining Boost")]
        [Tooltip("Extra pulse amplitude added when mining. " +
                 "Scales with progress so the beam swells as it nears collection.")]
        [SerializeField] private float miningPulseBoost = 0.5f;

        // =====================
        // Rendering
        // =====================

        [FoldoutGroup("Rendering")]
        [Tooltip("Sorting layer for all beam renderers. Leave empty for default.")]
        [SerializeField] private string sortingLayerName = "";

        [FoldoutGroup("Rendering")]
        [Tooltip("Sorting order for all beam renderers (line + particles).")]
        [SerializeField] private int sortingOrder = 6;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool IsActive => _isActive;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool IsHitting => _isHitting;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float CurrentMiningProgress => _miningProgress;

        // =====================
        // State
        // =====================

        private GameObject _beam;
        private GameObject _beamStart;
        private GameObject _beamEndHit;
        private GameObject _beamEndIdle;
        private LineRenderer _line;

        private float _originalWidth;
        private float _lerpValue;
        private bool _pulseExpanding = true;
        private bool _isActive;
        private bool _isHitting;
        private float _miningProgress;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            SpawnBeamParts();
            Hide();
        }

        private void Update()
        {
            if (!_isActive || _line == null) return;

            UpdateTextureScroll();
            UpdateWidthPulse();
        }

        // -------------------------------------------------------
        // Public API — called by MiningLaser each frame
        // -------------------------------------------------------

        /**
         * Positions the beam line, orients start/end VFX, and toggles the
         * impact effect based on whether the beam is hitting a target.
         *
         * miningProgress (0-1) drives the intensity of scroll and pulse boosts
         * so the beam escalates visually as the resource nears collection.
         */
        public void SetBeam(Vector2 start, Vector2 end, bool hitting, float miningProgress = 0f)
        {
            if (_line == null) return;

            _isHitting = hitting;
            _miningProgress = miningProgress;

            // Position the line renderer endpoints in world space
            _line.SetPosition(0, start);
            _line.SetPosition(1, end);

            // Scale texture to beam length so it doesn't look stretched
            float distance = Vector2.Distance(start, end);
            _line.material.mainTextureScale = new Vector2(distance / textureLengthScale, 1f);

            // Orient start/end VFX along the beam direction
            Vector2 beamDir = (end - start).normalized;
            float angle = Mathf.Atan2(beamDir.y, beamDir.x) * Mathf.Rad2Deg;

            // Emitter glow tracks the turret tip
            if (_beamStart != null)
            {
                _beamStart.transform.position = start;
                _beamStart.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            // Swap end caps — hit VFX when on target, idle VFX otherwise
            float endAngle = angle + 180f;
            if (_beamEndHit != null)
            {
                _beamEndHit.SetActive(hitting);
                if (hitting)
                {
                    _beamEndHit.transform.position = end;
                    _beamEndHit.transform.rotation = Quaternion.Euler(0f, 0f, endAngle);
                }
            }
            if (_beamEndIdle != null)
            {
                _beamEndIdle.SetActive(!hitting);
                if (!hitting)
                {
                    _beamEndIdle.transform.position = end;
                    _beamEndIdle.transform.rotation = Quaternion.Euler(0f, 0f, endAngle);
                }
            }
        }

        /**
         * Activates the beam visuals. Idempotent — safe to call every frame.
         */
        public void Show()
        {
            if (_isActive) return;
            _isActive = true;

            if (_beam != null) _beam.SetActive(true);
            if (_beamStart != null) _beamStart.SetActive(true);
            // End caps stay hidden until SetBeam picks the correct one
        }

        /**
         * Deactivates all beam visuals and resets the pulse oscillator
         * so the beam starts fresh on the next Show().
         */
        public void Hide()
        {
            _isActive = false;
            _isHitting = false;
            _miningProgress = 0f;

            if (_beam != null) _beam.SetActive(false);
            if (_beamStart != null) _beamStart.SetActive(false);
            if (_beamEndHit != null) _beamEndHit.SetActive(false);
            if (_beamEndIdle != null) _beamEndIdle.SetActive(false);

            // Reset pulse so it doesn't start mid-cycle next activation
            _lerpValue = 0f;
            _pulseExpanding = true;
        }

        // -------------------------------------------------------
        // Internals
        // -------------------------------------------------------

        /**
         * Instantiates the three beam sub-objects from their prefab references
         * and configures the LineRenderer for world-space 2D use.
         */
        private void SpawnBeamParts()
        {
            // Line renderer — the core beam visual
            if (beamLineRendererPrefab != null)
            {
                _beam = Instantiate(beamLineRendererPrefab, transform);
                _beam.transform.localPosition = Vector3.zero;
                _beam.transform.localRotation = Quaternion.identity;

                _line = _beam.GetComponent<LineRenderer>();
                _line.useWorldSpace = true;
                _line.positionCount = 2;

                // Use the inspector override if set, otherwise keep the prefab's default
                _originalWidth = baseWidth > 0f ? baseWidth : _line.startWidth;
            }

            // Start VFX — emitter glow at turret tip
            if (beamStartPrefab != null)
            {
                _beamStart = Instantiate(beamStartPrefab, transform);
            }

            // End VFX — impact sparks when hitting a target
            if (beamEndHitPrefab != null)
            {
                _beamEndHit = Instantiate(beamEndHitPrefab, transform);
            }

            // End VFX — idle tip effect when firing into open space
            if (beamEndIdlePrefab != null)
            {
                _beamEndIdle = Instantiate(beamEndIdlePrefab, transform);
            }

            ApplySortingOrder();
        }

        /**
         * Scrolls the beam texture along the line each frame.
         * Mining increases scroll speed proportional to progress for an
         * accelerating energy-transfer effect.
         */
        private void UpdateTextureScroll()
        {
            float boost = _isHitting ? miningScrollBoost * _miningProgress : 0f;
            float speed = textureScrollSpeed + boost;
            _line.material.mainTextureOffset -= new Vector2(Time.deltaTime * speed, 0f);
        }

        /**
         * Oscillates beam width between base and peak using a sine ping-pong.
         *
         * The oscillator runs 0 → 1 → 0 and maps through sin(pi * t) for
         * smooth easing at both ends. Mining widens the pulse peak proportional
         * to progress, making the beam swell as the resource nears collection.
         */
        private void UpdateWidthPulse()
        {
            // Advance the oscillator
            _lerpValue += Time.deltaTime * pulseSpeed * (_pulseExpanding ? 1f : -1f);

            if (_lerpValue >= 1f) { _pulseExpanding = false; _lerpValue = 1f; }
            else if (_lerpValue <= 0f) { _pulseExpanding = true; _lerpValue = 0f; }

            // Mining amplifies the pulse peak — beam gets fatter near collection
            float boost = _isHitting ? miningPulseBoost * _miningProgress : 0f;
            float peakWidth = _originalWidth * (widthMultiplier + boost);
            float currentWidth = Mathf.Lerp(_originalWidth, peakWidth, Mathf.Sin(_lerpValue * Mathf.PI));

            _line.startWidth = currentWidth;
            _line.endWidth = currentWidth;
        }

        /**
         * Applies the configured sorting layer and order to every renderer
         * in the beam hierarchy so it layers correctly in the 2D scene.
         */
        private void ApplySortingOrder()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (!string.IsNullOrEmpty(sortingLayerName))
                    r.sortingLayerName = sortingLayerName;
                r.sortingOrder = sortingOrder;
            }
        }
    }
}
