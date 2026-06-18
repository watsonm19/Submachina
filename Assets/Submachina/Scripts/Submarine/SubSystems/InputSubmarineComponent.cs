using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;
#if UNITY_EDITOR
using UnityEditor;
using Sirenix.Utilities.Editor;
#endif

namespace Submachina.Core
{
    /**
     * Intermediate base for any SubmarineComponent that reads player input.
     *
     * Handles the lifecycle every input component shares:
     *   1. Action resolution — call RegisterAction(ref) in Awake to resolve the
     *      per-player InputAction through SubmarineInputModule (or direct fallback).
     *   2. Enable / disable — OnEnable enables all registered actions, OnDisable
     *      disables them. Override these (calling base) to add extra work.
     *   3. Runtime rebinding — when SubmarineInputModule reassigns devices,
     *      RebindActions() re-resolves every registered action in place.
     *
     * Components that don't read input stay on SubmarineComponent directly.
     * Supports N actions per component (all current subsystems have 1, but future
     * multi-action components like fire+reload just call RegisterAction twice).
     */
    public abstract class InputSubmarineComponent : SubmarineComponent
    {
        // =====================
        // Action Registration
        // =====================

        private struct ActionBinding
        {
            public InputActionReference Reference;
            public InputAction Resolved;
        }

        private readonly List<ActionBinding> _bindings = new();

        /**
         * First registered action — covers every current subsystem (all have exactly one).
         * Multi-action components store the return value of RegisterAction in named fields.
         */
        protected InputAction PrimaryAction => _bindings.Count > 0 ? _bindings[0].Resolved : null;

        /**
         * Resolves and tracks an InputActionReference for per-player input.
         * Call in Awake after base.Awake(). The resolved action is automatically
         * enabled/disabled with the component and re-resolved on device changes.
         */
        protected InputAction RegisterAction(InputActionReference reference)
        {
            var resolved = ResolveAction(reference);
            _bindings.Add(new ActionBinding { Reference = reference, Resolved = resolved });
            return resolved;
        }

        // =====================
        // Lifecycle
        // =====================

        /** Enables all registered actions. Override (calling base) to add extra work. */
        protected virtual void OnEnable()
        {
            for (int i = 0; i < _bindings.Count; i++)
                _bindings[i].Resolved?.Enable();
        }

        /** Disables all registered actions. Override (calling base) to add extra work. */
        protected virtual void OnDisable()
        {
            for (int i = 0; i < _bindings.Count; i++)
                _bindings[i].Resolved?.Disable();
        }

        // =====================
        // Runtime Rebinding
        // =====================

        /**
         * Re-resolves all registered actions against the current SubmarineInputModule.
         * Called by SubmarineInputModule.Reassign when devices change or a new player
         * takes over this submarine.
         */
        public void RebindActions()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                var binding = _bindings[i];
                bool wasEnabled = binding.Resolved?.enabled ?? false;

                if (wasEnabled) binding.Resolved?.Disable();

                binding.Resolved = ResolveAction(binding.Reference);

                if (wasEnabled) binding.Resolved?.Enable();

                _bindings[i] = binding;
            }

            OnActionsRebound();
        }

        /** Override point for subclasses that cache the resolved action in a local field. */
        protected virtual void OnActionsRebound() { }

#if UNITY_EDITOR
        // -------------------------------------------------------
        // Editor — "INPUT COMPONENT" sub-banner with action chips
        // -------------------------------------------------------

        private static GUIStyle _inputBannerStyle;
        private static GUIStyle _inputChipStyle;

        [PropertyOrder(-9999), OnInspectorGUI]
        private void DrawInputComponentBanner()
        {
            EnsureInputStyles();

            // Collect action names from serialized InputActionReference fields
            var actionNames = CollectActionNames();
            if (actionNames.Count == 0) return;

            // Banner height: one row for the label + chips
            float chipHeight = 18f;
            float rowPad = 3f;
            float totalHeight = chipHeight + rowPad * 2f;

            Rect bgRect = EditorGUILayout.GetControlRect(false, totalHeight);
            SirenixEditorGUI.DrawSolidRect(bgRect, new Color(0.12f, 0.12f, 0.14f));

            // Left accent — green to distinguish from the amber feedback chips
            Rect accentRect = bgRect;
            accentRect.width = 4f;
            SirenixEditorGUI.DrawSolidRect(accentRect, new Color(0.30f, 0.85f, 0.55f));

            // Label
            float labelWidth = 56f;
            GUI.color = new Color(0.30f, 0.85f, 0.55f);
            GUI.Label(new Rect(bgRect.x + 8f, bgRect.y + rowPad, labelWidth, chipHeight),
                "Input", _inputBannerStyle);
            GUI.color = Color.white;

            // Action chips
            Color chipBg = new Color(0.22f, 0.22f, 0.26f);
            Color chipText = new Color(0.40f, 0.90f, 0.60f);
            var prevColor = GUI.color;

            float x = bgRect.x + 8f + labelWidth + 4f;
            float y = bgRect.y + rowPad;

            for (int i = 0; i < actionNames.Count; i++)
            {
                float w = _inputChipStyle.CalcSize(new GUIContent(actionNames[i])).x + 10f;
                Rect chipRect = new Rect(x, y, w, chipHeight);
                SirenixEditorGUI.DrawSolidRect(chipRect, chipBg);

                GUI.color = chipText;
                GUI.Label(chipRect, actionNames[i], _inputChipStyle);
                GUI.color = prevColor;

                x += w + 4f;
            }
        }

        /**
         * Reflects over the concrete class to find all InputActionReference fields
         * and extracts their action names for the banner chips.
         */
        private List<string> CollectActionNames()
        {
            var names = new List<string>();
            var type = GetType();
            var fields = type.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(InputActionReference)) continue;
                var reference = fields[i].GetValue(this) as InputActionReference;
                string name = reference?.action?.name ?? fields[i].Name;
                names.Add(name);
            }

            return names;
        }

        private static void EnsureInputStyles()
        {
            if (_inputBannerStyle != null) return;

            _inputBannerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            _inputChipStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = new Color(0.40f, 0.90f, 0.60f) }
            };
        }
#endif
    }
}
