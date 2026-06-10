using System.Reflection;
using Sirenix.OdinInspector;
using UnityAtoms;
using UnityEngine;

namespace Core.UI
{
    /// <summary>
    /// Binds any Unity Atoms variable (FloatVariable, IntVariable, StringVariable, ...) to the
    /// ValueTextDisplay on this GameObject. Displays the current value on enable and live-updates
    /// whenever the variable's Changed event fires — no manual UnityEvent wiring needed.
    /// Formatting (e.g. "{0:0.0} m") is configured on the ValueTextDisplay.
    /// </summary>
    [RequireComponent(typeof(ValueTextDisplay))]
    public class AtomVariableTextBinder : MonoBehaviour
    {
        [Title("Source")]
        [Tooltip("Any Unity Atoms variable. Its value is pushed to the ValueTextDisplay on this GameObject.")]
        [SerializeField, Required] private AtomBaseVariable variable;

        // Cached references resolved on enable
        private ValueTextDisplay _display;
        private AtomEventBase _changedEvent;

        /**
         * Subscribes to the variable's Changed event and shows the current value immediately,
         * so the text is correct even before the first change fires.
         */
        private void OnEnable()
        {
            _display = GetComponent<ValueTextDisplay>();
            if (variable == null) { Debug.LogWarning($"[{name}] AtomVariableTextBinder has no variable assigned.", this); return; }

            // The typed Changed event only exists on the generic AtomVariable<...>, so we fetch it
            // via reflection through the untyped base reference. The property getter lazily creates
            // the event at runtime if none was assigned in the inspector, so it's always available.
            var changedProperty = variable.GetType().GetProperty("Changed", BindingFlags.Public | BindingFlags.Instance);
            _changedEvent = changedProperty?.GetValue(variable) as AtomEventBase;

            // Typed Raise(T) also invokes the untyped base event, so this fires on every change
            if (_changedEvent != null) _changedEvent.Register(Refresh);

            Refresh();
        }

        /** Unsubscribes from the variable's Changed event. */
        private void OnDisable()
        {
            if (_changedEvent != null) _changedEvent.Unregister(Refresh);
            _changedEvent = null;
        }

        /**
         * Reads the variable's current value and pushes it to the text display.
         * Also exposed as an editor button to preview the binding without entering play mode.
         */
        [Button("Refresh Now")]
        public void Refresh()
        {
            if (variable == null) return;

            // Resolve the display lazily so the editor button works outside play mode
            if (_display == null) _display = GetComponent<ValueTextDisplay>();
            if (_display != null) _display.SetValue(variable.BaseValue);
        }
    }
}
