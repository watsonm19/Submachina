using System.Collections;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Flashes the sprite white for a brief moment whenever this entity takes damage.
     *
     * Subscribes to Health.onHealthChanged and triggers the flash whenever HP
     * decreases. The flash captures whatever color the sprite currently is
     * (including EnemyController's wind-up/attack tints) and restores it
     * afterward, so the two systems don't fight each other.
     *
     * Setup:
     *   Add to any GameObject that has both a Health and a SpriteRenderer.
     *   No Inspector wiring needed — it auto-connects to Health in Start.
     */
    public class HitFlash : MonoBehaviour
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Flash")]
        [Tooltip("Color to flash when damage is taken. White is the standard hit confirm.")]
        [SerializeField] private Color flashColor = Color.white;

        [FoldoutGroup("Flash")]
        [Tooltip("How long the flash color holds before restoring. Short enough to feel snappy.")]
        [SerializeField, Min(0.01f)] private float flashDuration = 0.1f;

        // =====================
        // State
        // =====================

        private SpriteRenderer _spriteRenderer;
        private Health _health;
        private Coroutine _flashCoroutine;
        private int _lastHP;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _health = GetComponent<Health>();
        }

        private void Start()
        {
            if (_health == null) return;

            _lastHP = _health.CurrentHP;
            _health.onHealthChanged.AddListener(OnHealthChanged);
        }

        private void OnDestroy()
        {
            if (_health != null)
                _health.onHealthChanged.RemoveListener(OnHealthChanged);
        }

        // -------------------------------------------------------
        // Flash
        // -------------------------------------------------------

        private void OnHealthChanged(int current, int max)
        {
            // Only flash on damage, not on heals
            if (current >= _lastHP) { _lastHP = current; return; }
            _lastHP = current;

            // Stop any in-progress flash before starting a new one
            // so rapid hits don't leave the sprite stuck on flash color
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(FlashRoutine());
        }

        /**
         * Captures the sprite's current color (may be a tint from EnemyController),
         * snaps to flashColor, then restores the original after flashDuration.
         */
        private IEnumerator FlashRoutine()
        {
            Color colorBeforeFlash = _spriteRenderer.color;
            _spriteRenderer.color = flashColor;

            yield return new WaitForSeconds(flashDuration);

            _spriteRenderer.color = colorBeforeFlash;
            _flashCoroutine = null;
        }
    }
}
