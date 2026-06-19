using UnityEngine;
using UnityEngine.Events;
using TMPro;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Object-pooled floating-text spawner for world-space number popups.
     *
     * Extends SubmarineObserver — auto-resolves its submarine from the hierarchy
     * and wires itself to that sub's survival systems. No manual Unity Event
     * wiring needed: drop this anywhere inside the submarine hierarchy (e.g. the
     * player canvas) and it listens to the holistic O2/HP events.
     *
     * It subscribes to the single source of truth for each resource:
     *   - O2System.onAirGained  → "+air"  popups (pickups + manual/intake pumps)
     *   - O2System.onAirSpent   → "-air"  popups (Cavitation Burst cost)
     *   - O2System.onAirDecayed → low-key periodic passive-loss popups
     *   - Health.onHealed       → "+HP"   popups
     *   - Health.onDamaged      → "-HP"   popups
     *
     * Each of these is a "channel" with its own FloatingTextStyle (colour, size,
     * format, optional prefab) and its own little ring-buffer pool, so every
     * popup type can look and feel distinct — e.g. O2 numbers inside a bubble
     * prefab, HP numbers as plain text in a different colour.
     */
    public class FloatingTextPool : SubmarineObserver
    {
        // =====================
        // O2 Channels
        // =====================

        [FoldoutGroup("O2/Gained")]
        [HideLabel, Title("Air Gained", "Pickups and successful pumps", TitleAlignments.Left)]
        [SerializeField] private FloatingTextStyle o2Gained = new()
        {
            color = new Color(0.4f, 0.9f, 1f, 1f),
            numberFormat = "+{0:F0}",
            poolSize = 8
        };

        [FoldoutGroup("O2/Lost")]
        [HideLabel, Title("Air Lost", "Discrete spends like Cavitation Burst", TitleAlignments.Left)]
        [SerializeField] private FloatingTextStyle o2Lost = new()
        {
            color = new Color(1f, 0.55f, 0.3f, 1f),
            numberFormat = "-{0:F0}",
            poolSize = 6
        };

        [FoldoutGroup("O2/Passive Decay")]
        [HideLabel, Title("Passive Decay", "Periodic breathing loss — quieter & smaller", TitleAlignments.Left)]
        [SerializeField] private FloatingTextStyle o2Decay = new()
        {
            color = new Color(0.8f, 0.7f, 0.55f, 0.85f),
            numberFormat = "-{0:F0}",
            scale = 0.65f,
            floatSpeed = 0.8f,
            poolSize = 4
        };

        // =====================
        // HP Channels
        // =====================

        [FoldoutGroup("HP/Gained")]
        [HideLabel, Title("HP Gained", "Healing", TitleAlignments.Left)]
        [SerializeField] private FloatingTextStyle hpGained = new()
        {
            color = new Color(0.4f, 1f, 0.45f, 1f),
            numberFormat = "+{0:F0}",
            poolSize = 4
        };

        [FoldoutGroup("HP/Lost")]
        [HideLabel, Title("HP Lost", "Damage taken", TitleAlignments.Left)]
        [SerializeField] private FloatingTextStyle hpLost = new()
        {
            color = new Color(1f, 0.25f, 0.2f, 1f),
            numberFormat = "-{0:F0}",
            poolSize = 6
        };

        // =====================
        // Spawn Position
        // =====================

        [FoldoutGroup("Spawn Position")]
        [Tooltip("World-space anchor where popups spawn. If unassigned, defaults to the submarine's transform.")]
        [SerializeField] private Transform spawnAnchor;

        [FoldoutGroup("Spawn Position")]
        [Tooltip("Random offset radius so rapid popups don't stack exactly on top of each other.")]
        [SerializeField, Min(0f)] private float jitterRadius = 0.3f;

        // =====================
        // Sorting (bare popups only)
        // =====================

        [FoldoutGroup("Sorting")]
        [Tooltip("Sorting layer for code-generated (no-prefab) text. Leave empty for 'Default'. " +
                 "Prefab popups use whatever their prefab authors.")]
        [SerializeField] private string sortingLayerName = "";

        [FoldoutGroup("Sorting")]
        [Tooltip("Sorting order within the layer for code-generated text. High = drawn on top.")]
        [SerializeField] private int sortingOrder = 100;

        // =====================
        // State
        // =====================

        private Channel _o2GainedCh, _o2LostCh, _o2DecayCh, _hpGainedCh, _hpLostCh;

        // Cached listener delegates so we can cleanly unsubscribe in OnDestroy
        private UnityAction<float> _onAirGained, _onAirSpent, _onAirDecayed;
        private UnityAction<int>   _onHealed, _onDamaged;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();

            // Build one ring-buffer pool per channel
            _o2GainedCh = new Channel(o2Gained, this);
            _o2LostCh   = new Channel(o2Lost, this);
            _o2DecayCh  = new Channel(o2Decay, this);
            _hpGainedCh = new Channel(hpGained, this);
            _hpLostCh   = new Channel(hpLost, this);
        }

        private void Start()
        {
            if (Sub == null) return;

            // ── O2: subscribe to the holistic air events on the O2System ──
            if (Sub.O2 != null)
            {
                _onAirGained  = amount => _o2GainedCh.Show(amount, SpawnPosition());
                _onAirSpent   = amount => _o2LostCh.Show(amount, SpawnPosition());
                _onAirDecayed = amount => _o2DecayCh.Show(amount, SpawnPosition());

                Sub.O2.onAirGained.AddListener(_onAirGained);
                Sub.O2.onAirSpent.AddListener(_onAirSpent);
                Sub.O2.onAirDecayed.AddListener(_onAirDecayed);
            }

            // ── HP: subscribe to the Health component on the sub root ──
            if (Sub.Health != null)
            {
                _onHealed  = amount => _hpGainedCh.Show(amount, SpawnPosition());
                _onDamaged = amount => _hpLostCh.Show(amount, SpawnPosition());

                Sub.Health.onHealed.AddListener(_onHealed);
                Sub.Health.onDamaged.AddListener(_onDamaged);
            }
        }

        private void OnDestroy()
        {
            // Mirror every subscription made in Start so swapped-out subs don't leak
            if (Sub?.O2 != null)
            {
                if (_onAirGained != null)  Sub.O2.onAirGained.RemoveListener(_onAirGained);
                if (_onAirSpent != null)   Sub.O2.onAirSpent.RemoveListener(_onAirSpent);
                if (_onAirDecayed != null) Sub.O2.onAirDecayed.RemoveListener(_onAirDecayed);
            }

            if (Sub?.Health != null)
            {
                if (_onHealed != null)  Sub.Health.onHealed.RemoveListener(_onHealed);
                if (_onDamaged != null) Sub.Health.onDamaged.RemoveListener(_onDamaged);
            }
        }

        // -------------------------------------------------------
        // Spawn Position
        // -------------------------------------------------------

        /** Resolves the spawn point (anchor → sub → self) and applies jitter. */
        private Vector3 SpawnPosition()
        {
            Vector3 pos = spawnAnchor != null
                ? spawnAnchor.position
                : (Sub != null ? Sub.transform.position : transform.position);

            return pos + (Vector3)(Random.insideUnitCircle * jitterRadius);
        }

        // -------------------------------------------------------
        // Channel — one styled, pooled popup type
        // -------------------------------------------------------

        /**
         * Owns a fixed ring buffer of FloatingText instances for a single style.
         * Show() grabs the next slot (recycling the oldest when saturated),
         * positions it, and plays it with the channel's styled look.
         */
        private class Channel
        {
            private readonly FloatingTextStyle _style;
            private readonly FloatingText[] _pool;
            private int _next;

            public Channel(FloatingTextStyle style, FloatingTextPool owner)
            {
                _style = style;
                int size = Mathf.Max(1, style.poolSize);
                _pool = new FloatingText[size];
                for (int i = 0; i < size; i++)
                    _pool[i] = owner.CreateInstance(style, i);
            }

            /** Spawns the next popup for this channel at the given world position. */
            public void Show(float amount, Vector3 position)
            {
                FloatingText ft = _pool[_next];
                _next = (_next + 1) % _pool.Length;

                ft.transform.position = position;
                ft.Play(string.Format(_style.numberFormat, amount),
                        _style.color, _style.floatSpeed, _style.lifetime, _style.scale);
            }
        }

        // -------------------------------------------------------
        // Instance Creation
        // -------------------------------------------------------

        /**
         * Builds one pooled FloatingText for a style. Two paths:
         *   - prefab assigned → instantiate it (e.g. a bubble-wrapped TMP) and
         *     ensure it carries a FloatingText to animate.
         *   - prefab null     → generate a bare world-space TextMeshPro using the
         *     style's font size and this pool's sorting settings.
         */
        private FloatingText CreateInstance(FloatingTextStyle style, int index)
        {
            // ── Prefab path: let the prefab define its own art (font, bubble) ──
            if (style.prefab != null)
            {
                GameObject go = Instantiate(style.prefab, transform);
                go.name = $"{style.prefab.name}_{index}";

                // FloatingText requires a TextMeshPro; the prefab is expected to
                // have both, but add a FloatingText defensively if it's missing.
                FloatingText ft = go.GetComponent<FloatingText>();
                if (ft == null) ft = go.AddComponent<FloatingText>();

                go.SetActive(false);
                return ft;
            }

            // ── Bare path: original code-generated world-space TextMeshPro ──
            var bare = new GameObject($"FloatingText_{index}");
            bare.transform.SetParent(transform);

            var tmp = bare.AddComponent<TextMeshPro>();
            tmp.fontSize = style.fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            // Sorting — ensure text draws on top of game sprites
            var renderer = bare.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (!string.IsNullOrEmpty(sortingLayerName))
                    renderer.sortingLayerName = sortingLayerName;
                renderer.sortingOrder = sortingOrder;
            }

            bare.AddComponent<FloatingText>();
            bare.SetActive(false);

            return bare.GetComponent<FloatingText>();
        }
    }
}
