using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Deals damage to enemies AND the submarine when the sub dashes into them.
     *
     * This component lives on a self-contained shield module: a trigger Collider2D
     * (the capsule shield outline) plus a SpriteRenderer for the visual. The prefab
     * relocates itself onto SubAnchors.Front via AnchorMount, so the damage zone is
     * physically the shielded *front* of the sub — ramming with the back of the hull
     * does nothing, by design.
     *
     * Because the collider and this script share a GameObject, the trigger callbacks
     * fire no matter where the module ends up in the sub hierarchy — it does NOT
     * depend on the root hull collider (the old OnCollisionEnter2D approach only
     * worked when this lived on the body that owned the Rigidbody2D).
     *
     * Gating: only acts while Sub.Physics.IsDashing (an active CavitationBurst dash)
     * is true, so ordinary drift-contact does nothing — only intentional rams count.
     * This keeps the risk/reward intentional and learnable.
     *
     * Risk/reward: the enemy takes dashDamage, the sub pays selfDamage as the cost of
     * an aggressive play. Enemy hits route through HitReceiver so phase invulnerability
     * is respected (e.g. RammingEnemy is immune while charging); self-damage only fires
     * when the hit is accepted.
     *
     * Multi-hit: a per-dash set tracks who has already been rammed this dash, so one
     * dash plows through a crowd hitting each enemy once, and multi-collider enemies
     * can't double-fire. The set clears at the start of every new dash.
     */
    [UsesFeedbacks(nameof(SubFeedbacks.DashRam))]
    [RequireComponent(typeof(Collider2D))]
    public class DashRam : SubmarineComponent
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Damage")]
        [Tooltip("Only objects on these layers trigger a dash-ram. " +
                 "Set to the Enemy layer.")]
        [SerializeField] private LayerMask enemyLayer;

        [FoldoutGroup("Damage")]
        [Tooltip("Damage dealt to the enemy on a successful ram.")]
        [SerializeField, Min(1)] private int dashDamage = 3;

        [FoldoutGroup("Damage")]
        [Tooltip("Damage the submarine takes when the ram connects. " +
                 "Set to 0 to remove the risk side of the trade-off.")]
        [SerializeField, Min(0)] private int selfDamage = 1;

        [FoldoutGroup("Damage")]
        [Tooltip("Knockback impulse applied to the enemy in the dash direction. " +
                 "Example: 6 gives a satisfying shove without sending enemies off-screen.")]
        [SerializeField, Min(0f)] private float knockbackForce = 6f;

        // =====================
        // Shield Visual
        // =====================

        [FoldoutGroup("Shield")]
        [Tooltip("SpriteRenderer for the shield. Stays visible while the module is " +
                 "installed and energizes (color shift) during an active dash. " +
                 "Its material must expose an HDR '_Color' tint (use the Submachina/SpriteHDR " +
                 "shader) — the tint is driven per-renderer via a MaterialPropertyBlock so HDR " +
                 "values bloom (SpriteRenderer.color can't, it clamps to 0-1).")]
        [SerializeField] private SpriteRenderer shieldRenderer;

        [FoldoutGroup("Shield")]
        [Tooltip("Resting shield tint — installed but inert (not dashing). Low alpha keeps " +
                 "it subtle. HDR — push values above 1 to bloom with post-processing.")]
        [SerializeField, ColorUsage(true, true)]
        private Color idleColor = new Color(0.40f, 0.70f, 1f, 0.35f);

        [FoldoutGroup("Shield")]
        [Tooltip("Color the shield snaps to the instant a dash fires, then recovers back to the " +
                 "idle color once the dash ends. HDR — bright values (>1) bloom for a glowing pop.")]
        [SerializeField, ColorUsage(true, true)]
        private Color chargedColor = new Color(1.6f, 3.5f, 5f, 0.90f);

        [FoldoutGroup("Shield")]
        [Tooltip("Seconds to fade the shield from its charged color back to idle after a dash ends. " +
                 "0 = snap instantly.")]
        [SerializeField, Min(0f)]
        private float colorRecoveryDuration = 0.4f;

        [FoldoutGroup("Shield")]
        [Tooltip("Color blend across the recovery. X = recovery progress (0 = dash just ended, " +
                 "1 = recovered), Y = blend from charged color (0) to idle color (1). " +
                 "Evaluated unclamped, so curves that overshoot push HDR brighter.")]
        [SerializeField]
        private AnimationCurve colorRecoveryCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [FoldoutGroup("Shield")]
        [Title("Hit Flash")]
        [Tooltip("Brief bright pop the shield flashes the instant a ram connects, overlaid on top " +
                 "of whatever state it's in (charged/recovering/idle). HDR — keep it brighter than " +
                 "the charged color so a landed hit reads as a punch.")]
        [SerializeField, ColorUsage(true, true)]
        private Color hitColor = new Color(6f, 6f, 7f, 1f);

        [FoldoutGroup("Shield")]
        [Tooltip("Seconds the hit flash takes to decay from the hit color back to the underlying " +
                 "shield color. Short = a sharp snap.")]
        [SerializeField, Min(0f)]
        private float hitFlashDuration = 0.12f;

        [FoldoutGroup("Shield")]
        [Tooltip("Decay blend for the hit flash. X = flash progress (0 = just landed, 1 = done), " +
                 "Y = blend from hit color (0) to the underlying color (1). Unclamped for overshoot.")]
        [SerializeField]
        private AnimationCurve hitFlashCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a dash-ram lands and the hit is accepted. " +
                 "Passes the world-space contact point for spawning effects.")]
        public UnityEvent<Vector2> onDashRam;

        [FoldoutGroup("Events")]
        [Tooltip("Fired once per target when a ram attempt hits an invulnerable target.")]
        public UnityEvent onDashRamBlocked;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a dash begins and the shield energizes. Wire to MMF juice.")]
        public UnityEvent onShieldCharged;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the dash ends and the shield returns to its idle state.")]
        public UnityEvent onShieldIdle;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool IsDashing => Sub?.Physics != null && Sub.Physics.IsDashing;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int HitThisDash => _hitThisDash.Count;

        // =====================
        // State
        // =====================

        // The shield's own trigger collider — the front damage zone.
        private Collider2D _shieldCollider;

        // Cached sub body, used to derive the ram direction from actual motion.
        private Rigidbody2D _subBody;

        // Edge-detect IsDashing so we can reset per-dash state and drive the visual.
        private bool _wasDashing;

        // Drives the post-dash color fade: when the dash ended, and whether a fade
        // is still in progress.
        private float _dashEndTime = -1f;
        private bool _recovering;

        // When the most recent ram landed, so the hit flash can decay from it.
        // Negative = no flash has fired yet.
        private float _hitFlashStart = -1f;

        // Editor-only: forces the charged look for the "Simulate Charge" button,
        // independent of an actual dash.
        private bool _debugCharged;

        // Per-renderer HDR tint, pushed through a MaterialPropertyBlock instead of
        // SpriteRenderer.color (which clamps to 0-1 and can't bloom). Reused each
        // frame so we don't allocate; ColorId targets the shader's "_Color".
        private MaterialPropertyBlock _shieldMpb;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Per-dash dedup, keyed on the resolved damage handler (HitReceiver/Health),
        // so a multi-collider enemy counts once and a crowd is each hit once per dash.
        private readonly HashSet<Object> _hitThisDash = new();

        // Targets we've already fired the "blocked" cue for this dash, so an
        // invulnerable target overlapping for many physics frames only reports once.
        private readonly HashSet<Object> _blockedThisDash = new();

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        /**
         * Caches references and forces the collider into trigger mode so the shield
         * never physically shoves the sub around — it is purely a detection volume.
         * The sub body is resolved before AnchorMount re-parents us (still under the
         * sub), so the reference stays valid after the mount.
         */
        protected override void Awake()
        {
            base.Awake();

            _shieldCollider = GetComponent<Collider2D>();
            _shieldCollider.isTrigger = true;

            _subBody = GetComponentInParent<Rigidbody2D>();
        }

        /** Start the shield in its resting idle tint once everything is wired. */
        private void Start()
        {
            SetShieldColor(idleColor);
        }

        /**
         * Drives the shield tint each frame. Detects dash edges (energize / begin
         * fade), resolves the base lifecycle color (charged → recovering → idle),
         * then overlays a brief hit flash on top so a landed ram pops above whatever
         * state the shield is in.
         *
         * Damage itself happens in OnTriggerStay2D while the dash gate is open.
         */
        private void Update()
        {
            bool dashing = IsDashing;

            // Dash edges: rising = reset per-dash tracking + energize; falling = fade.
            if (dashing != _wasDashing)
            {
                _wasDashing = dashing;
                if (dashing)
                {
                    _hitThisDash.Clear();
                    _blockedThisDash.Clear();
                    _recovering = false;
                    onShieldCharged?.Invoke();
                }
                else
                {
                    _dashEndTime = Time.time;
                    _recovering = true;
                }
            }

            // Base color from the dash lifecycle, with the hit flash layered over it.
            Color color = ApplyHitFlash(ResolveLifecycleColor(dashing));
            SetShieldColor(color);
        }

        // -------------------------------------------------------
        // Shield Visual — state resolution
        // -------------------------------------------------------

        /**
         * Resolves the shield's base color from where it is in the dash lifecycle:
         *   - charged while dashing (or while a debug charge is forced),
         *   - fading charged → idle across colorRecoveryDuration after a dash,
         *   - idle otherwise.
         * Fires onShieldIdle once, the frame the recovery fade completes.
         */
        private Color ResolveLifecycleColor(bool dashing)
        {
            if (dashing || _debugCharged) return chargedColor;
            if (!_recovering) return idleColor;

            // Post-dash fade. LerpUnclamped lets overshoot curves push HDR brighter.
            float progress = colorRecoveryDuration > 0f
                ? Mathf.Clamp01((Time.time - _dashEndTime) / colorRecoveryDuration)
                : 1f;
            Color color = Color.LerpUnclamped(chargedColor, idleColor, colorRecoveryCurve.Evaluate(progress));

            if (progress >= 1f)
            {
                _recovering = false;
                onShieldIdle?.Invoke();
            }
            return color;
        }

        /**
         * Overlays the hit flash: for hitFlashDuration after a ram lands, blends from
         * the bright hitColor back to the supplied base color via hitFlashCurve.
         * Returns the base color unchanged once the flash has elapsed.
         */
        private Color ApplyHitFlash(Color baseColor)
        {
            if (_hitFlashStart < 0f) return baseColor;

            float elapsed = Time.time - _hitFlashStart;
            if (elapsed >= hitFlashDuration) return baseColor;

            float t = hitFlashDuration > 0f ? Mathf.Clamp01(elapsed / hitFlashDuration) : 1f;
            return Color.LerpUnclamped(hitColor, baseColor, hitFlashCurve.Evaluate(t));
        }

        /** Starts a hit flash now — called when a ram connects (and by the debug button). */
        private void TriggerHitFlash() => _hitFlashStart = Time.time;

        // -------------------------------------------------------
        // Collision (trigger overlap)
        // -------------------------------------------------------

        /**
         * Fires every physics frame for each enemy overlapping the shield. Using
         * Stay (not just Enter) means enemies we're *already* overlapping when the
         * dash kicks off still get rammed, not only newly-entered ones.
         *
         * Flow:
         *   1. Gate: active dash + enemy layer.
         *   2. Resolve the damage handler (HitReceiver, else Health) and dedup it
         *      against this dash so each target is processed once.
         *   3. Route the hit — HitReceiver respects phase invulnerability; Health
         *      is the fallback for enemies without a receiver.
         *   4. Accepted → pay self-damage, fire feedback + events, mark as hit.
         *   5. Rejected (invulnerable) → fire the blocked cue once per target.
         */
        private void OnTriggerStay2D(Collider2D other)
        {
            // Gate: only ram during an active dash, and only against enemy-layer objects.
            if (Sub?.Physics == null || !Sub.Physics.IsDashing) return;
            if ((enemyLayer.value & (1 << other.gameObject.layer)) == 0) return;

            // Resolve the damage handler once — the dedup key is the handler itself,
            // so multiple child colliders on one enemy collapse to a single target.
            HitReceiver receiver = other.GetComponentInParent<HitReceiver>();
            Health health = receiver == null ? other.GetComponentInParent<Health>() : null;
            Object key = (Object)receiver ?? health;
            if (key == null) return;

            // Already rammed this dash — nothing more to do.
            if (_hitThisDash.Contains(key)) return;

            // Build the hit payload from the overlap geometry and the sub's motion.
            Vector2 contact = other.ClosestPoint(transform.position);
            Vector2 ramDir = _subBody != null && _subBody.linearVelocity.sqrMagnitude > 0.01f
                ? _subBody.linearVelocity.normalized
                : ((Vector2)(other.transform.position - transform.position)).normalized;

            var hitData = new HitData
            {
                damage         = dashDamage,
                knockbackForce = knockbackForce,
                hitDirection   = ramDir,
                hitPoint       = contact,
                source         = gameObject
            };

            // Route the hit. HitReceiver may reject it (invulnerable / on cooldown);
            // Health has no gate, so the fallback path always connects.
            bool hitAccepted = false;
            if (receiver != null)
                hitAccepted = receiver.ReceiveHit(hitData);
            else if (health != null)
            {
                health.TakeDamage(dashDamage);
                hitAccepted = true;
            }

            // Hit connected — flash the shield, pay self-damage, fire juice, and stop re-hitting it.
            if (hitAccepted)
            {
                _hitThisDash.Add(key);
                TriggerHitFlash();
                if (selfDamage > 0) Sub?.Health?.TakeDamage(selfDamage);
                onDashRam?.Invoke(contact);
                Sub?.Feedbacks?.Play(SubFeedbacks.DashRam, contact);
            }
            // Rejected (e.g. a charging RammingEnemy) — report once, but keep retrying
            // on later frames so a hit lands the instant its invulnerability drops.
            else if (_blockedThisDash.Add(key))
            {
                onDashRamBlocked?.Invoke();
            }
        }

        // -------------------------------------------------------
        // Shield Visual — low-level setter
        // -------------------------------------------------------

        /**
         * Pushes an HDR tint onto the shield via a MaterialPropertyBlock rather than
         * SpriteRenderer.color. The renderer's vertex color is a 32-bit LDR value
         * (clamped 0-1) so it can never bloom; a property block carries full float
         * precision and stays per-renderer, so multiple subs don't share a tint and
         * no material instance is leaked. Targets the shader's "_Color" (HDR) property.
         */
        private void SetShieldColor(Color color)
        {
            if (shieldRenderer == null) return;

            _shieldMpb ??= new MaterialPropertyBlock();
            shieldRenderer.GetPropertyBlock(_shieldMpb);
            _shieldMpb.SetColor(ColorId, color);
            shieldRenderer.SetPropertyBlock(_shieldMpb);
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Tooltip("Seconds the 'Simulate Charge' button holds the charged look before " +
                 "running the normal recovery fade.")]
        [SerializeField, Min(0f)] private float debugChargeHold = 0.6f;

        /** Pulses the charged visual without an actual dash, then fades like normal. */
        [FoldoutGroup("Debug")]
        [Button("Simulate Charge"), GUIColor(0.5f, 0.85f, 1f)]
        private void DebugSimulateCharge()
        {
            if (!Application.isPlaying) { Debug.Log("[DashRam] Play mode only."); return; }
            StartCoroutine(DebugChargePulse());
        }

        /** Fires the hit reaction — shield flash + onDashRam + feedback — for juice testing. */
        [FoldoutGroup("Debug")]
        [Button("Simulate Hit"), GUIColor(1f, 0.7f, 0.3f)]
        private void DebugSimulateHit()
        {
            if (!Application.isPlaying) { Debug.Log("[DashRam] Play mode only."); return; }
            TriggerHitFlash();
            onDashRam?.Invoke(transform.position);
            Sub?.Feedbacks?.Play(SubFeedbacks.DashRam, transform.position);
        }

        [FoldoutGroup("Debug")]
        [Button("Simulate Self-Damage"), GUIColor(0.6f, 0.8f, 1f)]
        private void DebugSimulateSelf()
        {
            if (!Application.isPlaying) { Debug.Log("[DashRam] Play mode only."); return; }
            Sub?.Health?.TakeDamage(selfDamage);
            Debug.Log($"[DashRam] Simulated self-damage — sub took {selfDamage} damage.");
        }

        /** Holds the charged look for debugChargeHold seconds, then triggers recovery. */
        private IEnumerator DebugChargePulse()
        {
            _debugCharged = true;
            _recovering = false;
            onShieldCharged?.Invoke();
            yield return new WaitForSeconds(debugChargeHold);
            _debugCharged = false;
            _dashEndTime = Time.time;
            _recovering = true;
        }
#endif
    }
}
