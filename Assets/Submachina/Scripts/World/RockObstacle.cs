using UnityEngine;
using UnityEngine.Events;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Marks a rock obstacle as breakable by the MiningLaser.
     *
     * MiningLaser detects this component on a hit collider and accumulates
     * a hold-beam timer. When the timer reaches the laser's rockBreakDuration,
     * it calls Break() here, which fires events, plays feedbacks, and destroys
     * the GameObject.
     *
     * The sprite tints toward damagedColor as progress increases, giving the
     * player visual feedback that the rock is taking damage. The color resets
     * if the beam moves away before the rock breaks.
     *
     * Setup:
     *   1. Add this component to the RockObstacle prefab (layer: Collision).
     *   2. Set MiningLaser's Obstacle Layer mask to the Collision layer.
     *   3. Set Break From Layers to the Submarine layer.
     *   4. Wire breakFeedbacks to particle/sound effects for destruction juice.
     */
    [RequireComponent(typeof(Collider2D))]
    public class RockObstacle : MonoBehaviour
    {
        // =====================
        // Collision Breaking
        // =====================

        [FoldoutGroup("Collision")]
        [Tooltip("Minimum impact speed (m/s) for a collision to break this rock. " +
                 "Mirrors CollisionDamage's minImpactSpeed — set them to similar values so " +
                 "impacts that hurt the sub also break the rock.")]
        [SerializeField, Min(0f)] private float minBreakSpeed = 4f;

        [FoldoutGroup("Collision")]
        [Tooltip("Only break from collisions with objects on these layers. " +
                 "Set to the Submarine layer so rocks don't break each other or from stray spawns.")]
        [SerializeField] private LayerMask breakFromLayers = ~0;

        // =====================
        // Visual
        // =====================

        [FoldoutGroup("Visual")]
        [Tooltip("Color the sprite lerps toward as beam hold-time increases. " +
                 "E.g. a warm orange-red gives a 'heating up' look before the rock shatters.")]
        [SerializeField] private Color damagedColor = new Color(1f, 0.45f, 0.2f, 1f);

        // =====================
        // Feel
        // =====================

        [FoldoutGroup("Feel")]
        [Tooltip("Feedbacks played at the rock's position when it fully breaks. " +
                 "Hook up a particle burst, camera shake, and a crunch sound here.")]
        [SerializeField] private MMF_Player breakFeedbacks;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired just before the rock is destroyed. Use to spawn debris, update counters, etc.")]
        public UnityEvent onBreak;

        // =====================
        // Internals
        // =====================

        private SpriteRenderer _sprite;
        private Color _originalColor;

        private void Awake()
        {
            _sprite = GetComponent<SpriteRenderer>();
            if (_sprite != null) _originalColor = _sprite.color;
        }

        // -------------------------------------------------------
        // Collision Breaking
        // -------------------------------------------------------

        /**
         * Breaks the rock if the impact speed exceeds the threshold and the
         * colliding object is on an allowed layer.
         *
         * Both this and CollisionDamage (on the submarine) fire independently
         * on the same impact — the sub takes damage AND the rock breaks.
         * Tuning tip: set minBreakSpeed close to CollisionDamage's minImpactSpeed
         * so any hit that hurts the sub also clears the rock.
         */
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if ((breakFromLayers & (1 << collision.gameObject.layer)) == 0) return;
            if (collision.relativeVelocity.magnitude < minBreakSpeed) return;
            Break();
        }

        // -------------------------------------------------------
        // Progress API (called by MiningLaser each frame)
        // -------------------------------------------------------

        /**
         * Updates the rock's visual to reflect current mining progress.
         * progress 0 = undamaged, 1 = about to break.
         * The sprite lerps toward damagedColor as heat builds up.
         */
        public void SetProgress(float progress)
        {
            if (_sprite != null)
                _sprite.color = Color.Lerp(_originalColor, damagedColor, progress);
        }

        /**
         * Restores the rock's undamaged appearance when the beam moves away
         * before the rock breaks — so partial damage isn't persistent.
         */
        public void ResetProgress()
        {
            if (_sprite != null)
                _sprite.color = _originalColor;
        }

        /**
         * Called by MiningLaser when the hold timer completes.
         * Fires events and feedbacks, then destroys this GameObject.
         */
        public void Break()
        {
            onBreak?.Invoke();
            breakFeedbacks?.PlayFeedbacks(transform.position);
            Destroy(gameObject);
        }
    }
}
