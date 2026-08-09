using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.Events;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Core.Modulation
{
    /**
     * Discrete event rule watching one semantic parameter: fires actions when the value
     * crosses a threshold, with hysteresis (separate re-arm threshold), sustain duration,
     * cooldown, probability gating, and an optional one-shot mode.
     *
     * The cooldown can be a fixed wait, a random draw from a range, an authored sequence, or a
     * shuffle bag — re-picked every time the rule fires (see CooldownMode).
     *
     * Example: Dread rises above 0.75, held 1s, 25s cooldown, 60% chance
     *          → play stinger feedback + UnityEvent, re-arm only after Dread falls below 0.5.
     */
    public class DirectorRule : MonoBehaviour
    {
        public enum TriggerDirection { RisesAbove, FallsBelow }

        /**
         * How the cooldown duration is picked after each firing. Fixed is value 0 so rules
         * serialized before this field existed keep their original single-value behaviour.
         */
        public enum CooldownMode
        {
            Fixed,        // always cooldownSeconds
            RandomRange,  // new random draw from cooldownRange after every firing
            Sequence,     // walk cooldownList in order, wrapping at the end
            ShuffleBag    // draw cooldownList in random order, reshuffle once the bag empties
        }

        [Header("Source")]
        [Tooltip("Director to read from. Leave empty to auto-find (parents first, then scene).")]
        [SerializeField] private EnvironmentDirector director;

        [Tooltip("Semantic parameter this rule watches.")]
        [SerializeField] private DirectorParameterDef parameter;

        [Header("Trigger")]
        [SerializeField] private TriggerDirection direction = TriggerDirection.RisesAbove;

        [Tooltip("Firing threshold — value must cross this in the trigger direction.")]
        [SerializeField] private float triggerThreshold = 0.75f;

        [Tooltip("Hysteresis: value must return past this before the rule can fire again. " +
                 "Keep a gap from the trigger threshold to prevent oscillation spam.")]
        [SerializeField] private float resetThreshold = 0.5f;

        [Tooltip("Value must stay past the trigger threshold this long before firing. 0 = instant.")]
        [SerializeField] private float sustainSeconds = 0f;

        [Header("Pacing")]
        [Tooltip("How the wait between firings is chosen. Fixed keeps the classic single value; " +
                 "the other modes re-pick a duration every time the rule fires.")]
#if ODIN_INSPECTOR
        [EnumToggleButtons]
#endif
        [SerializeField] private CooldownMode cooldownMode = CooldownMode.Fixed;

        [Tooltip("Minimum seconds between firings of this rule. Also the fallback when a list mode has an empty list.")]
#if ODIN_INSPECTOR
        [ShowIf(nameof(ShowFixedCooldown))]
#endif
        [SerializeField] private float cooldownSeconds = 10f;

        [Tooltip("Random cooldown window — X = minimum seconds, Y = maximum. A fresh value is rolled after each firing.")]
#if ODIN_INSPECTOR
        [ShowIf(nameof(cooldownMode), CooldownMode.RandomRange)]
        [MinMaxSlider(0f, 120f, true)]
#endif
        [SerializeField] private Vector2 cooldownRange = new Vector2(5f, 15f);

        [Tooltip("Cooldown durations used one per firing — in order for Sequence, in random order for Shuffle Bag.")]
#if ODIN_INSPECTOR
        [ShowIf(nameof(UsesCooldownList))]
        [InfoBox("Add at least one duration, or the rule falls back to Cooldown Seconds.", InfoMessageType.Warning,
                 nameof(HasEmptyCooldownList))]
#endif
        [SerializeField] private float[] cooldownList = { 5f, 10f, 20f };

        [Tooltip("Shuffle Bag only: after a reshuffle, avoid starting with the same entry the previous bag ended on.")]
#if ODIN_INSPECTOR
        [ShowIf(nameof(cooldownMode), CooldownMode.ShuffleBag)]
#endif
        [SerializeField] private bool avoidRepeatOnReshuffle = true;

        [Tooltip("Chance 0..1 that an eligible crossing actually fires. Failed rolls wait for the next crossing.")]
        [Range(0f, 1f)]
        [SerializeField] private float probability = 1f;

        [Tooltip("When true the rule fires once per play session and then disarms permanently.")]
        [SerializeField] private bool oneShot;

        [Tooltip("Start armed. Disable to require an explicit Arm() call (e.g. from a scripted phase).")]
        [SerializeField] private bool armedOnStart = true;

        [Header("Actions")]
        [Tooltip("Feel feedbacks played when the rule fires.")]
        [SerializeField] private MMF_Player[] feedbacks;

        [Tooltip("Invoked when the rule fires — wire stingers, spawns, modifiers, anything.")]
        public UnityEvent onTriggered;

        [Tooltip("Invoked when the rule re-arms after the reset threshold is crossed back.")]
        public UnityEvent onRearmed;

        // --- Runtime state ---
        private bool _armed;
        private bool _spent;             // one-shot has fired
        private float _lastFireTime = float.NegativeInfinity;
        private float _sustainStart = -1f;

        // Cooldown scheduling: the duration governing the window opened by the last firing,
        // plus the cursors for the Sequence / ShuffleBag modes.
        private float _currentCooldown;
        private int _sequenceIndex;
        private int[] _bag;              // shuffled indices into cooldownList
        private int _bagIndex;
        private int _lastDrawnIndex = -1;

        public bool IsArmed => _armed;
        public bool CooldownReady => Time.time - _lastFireTime >= _currentCooldown;

        // Read-only wiring accessors for editor tooling (Director Graph window).
        public DirectorParameterDef Parameter => parameter;
        public EnvironmentDirector Director => director;
        public TriggerDirection Direction => direction;
        public float TriggerThreshold => triggerThreshold;
        public float ResetThreshold => resetThreshold;
        public float CooldownSeconds => cooldownSeconds;
        public CooldownMode Cooldown => cooldownMode;
        public float Probability => probability;
        public bool OneShot => oneShot;
        public bool IsSpent => _spent;
        public float CooldownRemaining => Mathf.Max(0f, _currentCooldown - (Time.time - _lastFireTime));

        /// <summary>Duration of the cooldown window opened by the most recent firing (0 before the first fire).</summary>
#if ODIN_INSPECTOR
        [ShowInInspector, ReadOnly, PropertyOrder(99), HideInEditorMode, LabelText("Active Cooldown")]
#endif
        public float CurrentCooldownSeconds => _currentCooldown;

        /**
         * Compact human-readable summary of the cooldown configuration for editor tooling
         * (Director Graph window). Appends the live duration once the rule has fired.
         */
        public string CooldownDescription
        {
            get
            {
                int count = cooldownList != null ? cooldownList.Length : 0;
                string live = _currentCooldown > 0f ? $" ({_currentCooldown:0.#}s)" : string.Empty;
                switch (cooldownMode)
                {
                    case CooldownMode.RandomRange: return $"{cooldownRange.x:0.#}–{cooldownRange.y:0.#}s{live}";
                    case CooldownMode.Sequence:    return count > 0 ? $"seq[{count}]{live}" : $"{cooldownSeconds:0.#}s";
                    case CooldownMode.ShuffleBag:  return count > 0 ? $"bag[{count}]{live}" : $"{cooldownSeconds:0.#}s";
                    default:                       return $"{cooldownSeconds:0.#}s";
                }
            }
        }

        // Odin conditional-display helpers for the cooldown list fields.
        private bool UsesCooldownList => cooldownMode == CooldownMode.Sequence || cooldownMode == CooldownMode.ShuffleBag;
        private bool HasEmptyCooldownList => UsesCooldownList && (cooldownList == null || cooldownList.Length == 0);
        private bool ShowFixedCooldown => cooldownMode == CooldownMode.Fixed || HasEmptyCooldownList;

        private void OnEnable()
        {
            if (director == null) director = EnvironmentDirector.FindFor(this);
            if (director == null) { Debug.LogWarning($"[DirectorRule] No EnvironmentDirector found for '{name}'.", this); enabled = false; return; }
            director.Track(parameter);
            _armed = armedOnStart;
            ResetCooldownSchedule();
        }

        // Keep authored cooldown values sane — negative waits would make the rule fire every frame.
        private void OnValidate()
        {
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            cooldownRange.x = Mathf.Max(0f, cooldownRange.x);
            cooldownRange.y = Mathf.Max(cooldownRange.x, cooldownRange.y);
            if (cooldownList != null)
                for (int i = 0; i < cooldownList.Length; i++) cooldownList[i] = Mathf.Max(0f, cooldownList[i]);
        }

        private void Update()
        {
            if (parameter == null || _spent) return;
            float value = director.GetValue(parameter);
            bool pastTrigger = direction == TriggerDirection.RisesAbove ? value >= triggerThreshold : value <= triggerThreshold;
            bool pastReset = direction == TriggerDirection.RisesAbove ? value <= resetThreshold : value >= resetThreshold;

            // Re-arm once the value has retreated past the reset threshold.
            if (!_armed)
            {
                if (!pastReset) return;
                _armed = true;
                _sustainStart = -1f;
                onRearmed?.Invoke();
                return;
            }

            // Track how long the value has been held past the trigger threshold.
            if (!pastTrigger) { _sustainStart = -1f; return; }
            if (_sustainStart < 0f) _sustainStart = Time.time;
            if (Time.time - _sustainStart < sustainSeconds) return;

            // Eligibility gates: cooldown first, then the probability roll consumes the crossing.
            if (!CooldownReady) return;
            _armed = false;
            if (Random.value > probability) return;

            Fire();
        }

        /**
         * Executes the rule's actions: Feel feedbacks then the UnityEvent. Public so
         * debug panels and scripted sequences can force-fire for testing.
         */
#if ODIN_INSPECTOR
        [Button("Fire Now (test)"), PropertyOrder(100)]
#endif
        public void Fire()
        {
            _lastFireTime = Time.time;
            _currentCooldown = DrawNextCooldown();   // this firing decides how long the next wait is
            if (oneShot) _spent = true;

            // Feel feedbacks at this rule's position (rules usually sit on rig/manager objects).
            if (feedbacks != null)
                foreach (var player in feedbacks)
                    if (player != null) player.PlayFeedbacks(transform.position);

            onTriggered?.Invoke();
        }

        /**
         * Picks the cooldown that governs the window opening right now. Fixed returns the single
         * authored value; RandomRange rolls fresh each time; Sequence walks the list in order and
         * wraps; ShuffleBag drains a shuffled copy of the list before reshuffling. List modes fall
         * back to cooldownSeconds when no durations are authored.
         */
        private float DrawNextCooldown()
        {
            int count = cooldownList != null ? cooldownList.Length : 0;
            switch (cooldownMode)
            {
                // Uniform roll between the two ends of the window (order-tolerant).
                case CooldownMode.RandomRange:
                    return Mathf.Max(0f, Random.Range(Mathf.Min(cooldownRange.x, cooldownRange.y),
                                                      Mathf.Max(cooldownRange.x, cooldownRange.y)));

                // Deterministic walk: 5, 10, 20, 5, 10, 20...
                case CooldownMode.Sequence:
                {
                    if (count == 0) return cooldownSeconds;
                    if (_sequenceIndex >= count) _sequenceIndex = 0;
                    float value = cooldownList[_sequenceIndex];
                    _sequenceIndex = (_sequenceIndex + 1) % count;
                    return Mathf.Max(0f, value);
                }

                // Every value used exactly once per bag, then the bag refills and reshuffles.
                case CooldownMode.ShuffleBag:
                {
                    if (count == 0) return cooldownSeconds;
                    if (_bag == null || _bag.Length != count || _bagIndex >= count) ShuffleCooldownBag(count);
                    _lastDrawnIndex = _bag[_bagIndex++];
                    return Mathf.Max(0f, cooldownList[_lastDrawnIndex]);
                }

                default:
                    return cooldownSeconds;
            }
        }

        /**
         * Refills the bag with every list index and Fisher-Yates shuffles it. When
         * avoidRepeatOnReshuffle is set, a leading entry that matches the previous bag's last
         * draw is swapped deeper into the bag so the same duration never runs back-to-back.
         */
        private void ShuffleCooldownBag(int count)
        {
            if (_bag == null || _bag.Length != count) _bag = new int[count];
            for (int i = 0; i < count; i++) _bag[i] = i;

            for (int i = count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int tmp = _bag[i];
                _bag[i] = _bag[j];
                _bag[j] = tmp;
            }

            // e.g. previous bag ended on index 2 and the new bag starts on index 2 → swap it with a later slot.
            if (avoidRepeatOnReshuffle && count > 1 && _bag[0] == _lastDrawnIndex)
            {
                int swap = Random.Range(1, count);
                int tmp = _bag[0];
                _bag[0] = _bag[swap];
                _bag[swap] = tmp;
            }

            _bagIndex = 0;
        }

        /**
         * Restarts the cooldown schedule: sequences begin at their first entry and shuffle bags
         * refill. Called on enable; also public so scripted phases can restart pacing cleanly.
         */
#if ODIN_INSPECTOR
        [Button("Reset Cooldown Schedule"), PropertyOrder(101)]
#endif
        public void ResetCooldownSchedule()
        {
            _currentCooldown = 0f;
            _sequenceIndex = 0;
            _bagIndex = int.MaxValue;   // forces a reshuffle on the next bag draw
            _lastDrawnIndex = -1;
        }

        /// <summary>Manually arm the rule (used by scripted phases with armedOnStart off).</summary>
        public void Arm() => _armed = true;

        /// <summary>Disarm without firing — the rule stays quiet until Arm() or the reset threshold logic re-arms it.</summary>
        public void Disarm() => _armed = false;
    }
}
