using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Player-side component that issues mode commands to the companion submarine.
     *
     * Attach to the PLAYER submarine root. Reads three input actions (Mine / Guard /
     * Collect) and calls CompanionAI.SetCommand() on the companion. The companion
     * is auto-discovered at Start and periodically re-checked so it works even if
     * the companion spawns after this component.
     *
     * Input layout:
     *   Mine    — D-pad Up   / keyboard 1
     *   Guard   — D-pad Down / keyboard 2
     *   Collect — D-pad Left or Right / keyboard 3
     *
     * Bind these actions in your Input Asset and assign the references here.
     * Wire OnCommandIssued to a HUD text or icon to display the current mode.
     *
     * Setup:
     *   1. Add to the player submarine root (it is an InputSubmarineComponent —
     *      it participates in the per-player input device routing automatically).
     *   2. Create three Button actions in your Input Asset and assign them.
     *   3. Optionally wire OnCommandIssued to a UI element.
     */
    public class CompanionCommandSystem : InputSubmarineComponent
    {
        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Button action — D-pad Up / keyboard 1. Issues the Mine command.")]
        [SerializeField] private InputActionReference commandMineRef;

        [FoldoutGroup("Input")]
        [Tooltip("Button action — D-pad Down / keyboard 2. Issues the Guard command.")]
        [SerializeField] private InputActionReference commandGuardRef;

        [FoldoutGroup("Input")]
        [Tooltip("Button action — D-pad Left or Right / keyboard 3. Issues the Collect command.")]
        [SerializeField] private InputActionReference commandCollectRef;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever a new command is issued. " +
                 "Wire to a TMP_Text component to show the current mode in the HUD.")]
        public UnityEvent<CompanionCommand> OnCommandIssued;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string ActiveCommand => _companion != null ? _companion.CurrentCommand.ToString() : "No Companion";

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool CompanionLinked => _companion != null;

        // =====================
        // State
        // =====================

        private CompanionAI _companion;
        private InputAction _mineAction;
        private InputAction _guardAction;
        private InputAction _collectAction;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();

            // Register all three actions for per-player device routing
            _mineAction    = RegisterAction(commandMineRef);
            _guardAction   = RegisterAction(commandGuardRef);
            _collectAction = RegisterAction(commandCollectRef);
        }

        private void Start()
        {
            FindCompanion();
        }

        private void Update()
        {
            // Lazy companion discovery — handles companions that spawn after the player
            if (_companion == null) FindCompanion();
            if (_companion == null) return;

            // Direct bindings: each button maps to one command, pressed-this-frame only
            if (_mineAction?.WasPressedThisFrame()    == true) Issue(CompanionCommand.Mine);
            if (_guardAction?.WasPressedThisFrame()   == true) Issue(CompanionCommand.Guard);
            if (_collectAction?.WasPressedThisFrame() == true) Issue(CompanionCommand.Collect);
        }

        protected override void OnActionsRebound()
        {
            // Re-resolve each action after the device has been reassigned
            _mineAction    = ResolveAction(commandMineRef);
            _guardAction   = ResolveAction(commandGuardRef);
            _collectAction = ResolveAction(commandCollectRef);
        }

        // -------------------------------------------------------
        // Command dispatch
        // -------------------------------------------------------

        /** Sends a command to the companion and notifies HUD listeners. */
        private void Issue(CompanionCommand command)
        {
            _companion.SetCommand(command);
            OnCommandIssued?.Invoke(command);
        }

        // -------------------------------------------------------
        // Companion discovery
        // -------------------------------------------------------

        /**
         * Searches Submarine.All for a submarine with a CompanionAI component.
         * Safe to call repeatedly — exits early once the companion is found.
         */
        private void FindCompanion()
        {
            foreach (Submarine sub in Submarine.All)
            {
                if (sub == Sub) continue;
                CompanionAI ai = sub.GetComponentInChildren<CompanionAI>();
                if (ai == null) continue;
                _companion = ai;
                break;
            }
        }
    }
}
