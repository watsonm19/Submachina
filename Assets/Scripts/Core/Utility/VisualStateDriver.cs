using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Sirenix.OdinInspector;

namespace Utility
{
    /**
     * General-purpose visual state manager. Define named states (e.g. "activated",
     * "hovered", "damaged") with optional color and scale overrides, then enable/disable
     * them at runtime by index or name. Uses a stack-based priority model — the most
     * recently enabled state wins. When all states are disabled, reverts to the original
     * appearance captured at Awake.
     *
     * Supports both SpriteRenderer and UI Graphic (Image, RawImage, TMP, etc.).
     * Auto-detects the primary target on the same GameObject if not manually assigned.
     * Additional sprites/graphics on other GameObjects can be added to mirror the
     * same visual states — each captures its own original color/scale for proper revert.
     */
    [AddComponentMenu("Utility/Visual State Driver")]
    public class VisualStateDriver : MonoBehaviour
    {
        // ── Nested types ──────────────────────────────────────────────

        [Serializable]
        public class VisualStateOverride
        {
            [LabelWidth(50)]
            public string name;

            [HorizontalGroup("Color", Width = 0.15f), ToggleLeft, LabelWidth(14)]
            public bool overrideColor;

            [HorizontalGroup("Color"), EnableIf("overrideColor"), ColorUsage(true, true), HideLabel]
            public Color color = Color.white;

            [Range(0.01f, 5f)]
            public float scaleMultiplier = 1f;

            [HorizontalGroup("Tween", Width = 0.15f), ToggleLeft, LabelWidth(14)]
            public bool useTween;

            [HorizontalGroup("Tween"), EnableIf("useTween"), LabelWidth(55)]
            [Range(0.01f, 5f)]
            public float tweenDuration = 0.2f;

            [HorizontalGroup("Tween"), EnableIf("useTween"), LabelWidth(35), LabelText("Ease")]
            public Ease tweenEase = Ease.OutQuad;
        }

        // ── Serialized fields ─────────────────────────────────────────

        [TitleGroup("Target")]
        [InfoBox("Leave both empty to auto-detect a SpriteRenderer or Graphic on this GameObject.")]
        [Tooltip("Explicit SpriteRenderer target. Takes priority over Graphic if both are assigned.")]
        [SerializeField] SpriteRenderer targetSprite;

        [TitleGroup("Target")]
        [Tooltip("Explicit UI Graphic target (Image, RawImage, TMP, etc.).")]
        [SerializeField] Graphic targetGraphic;

        [TitleGroup("Target")]
        [Tooltip("Additional SpriteRenderers that mirror the same visual state.")]
        [SerializeField] List<SpriteRenderer> additionalSprites = new();

        [TitleGroup("Target")]
        [Tooltip("Additional UI Graphics that mirror the same visual state.")]
        [SerializeField] List<Graphic> additionalGraphics = new();

        [TitleGroup("States")]
        [ListDrawerSettings(ShowIndexLabels = true, DraggableItems = true, OnEndListElementGUI = "DrawStateElementControls")]
        [SerializeField] List<VisualStateOverride> states = new();

        [TitleGroup("States")]
        [Tooltip("Optional resting appearance. When all states are disabled, transitions here using its tween settings. If unset, snaps to the captured original instantly.")]
        [SerializeField] VisualStateOverride defaultState;

        // ── Runtime state ─────────────────────────────────────────────

        Color _originalColor;
        Vector3 _originalScale;
        bool _tearingDown;
        readonly List<int> _activeStack = new();

        // Tweens for primary target
        Tween _colorTween;
        Tween _scaleTween;

        // Tweens and captured originals for additional targets
        readonly List<Tween> _additionalColorTweens = new();
        readonly List<Color> _additionalOriginalColors = new();
        readonly List<Vector3> _additionalOriginalScales = new();

        // ── Debug display ─────────────────────────────────────────────

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        List<string> ActiveStateNames
        {
            get
            {
                var names = new List<string>();
                foreach (int idx in _activeStack)
                {
                    if (idx >= 0 && idx < states.Count)
                        names.Add(states[idx].name);
                }
                return names;
            }
        }

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        string ResolvedWinner
        {
            get
            {
                if (_activeStack.Count == 0) return "(default)";
                int top = _activeStack[_activeStack.Count - 1];
                return top >= 0 && top < states.Count ? states[top].name : "(invalid)";
            }
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            ResolveTarget();
            CaptureOriginals();
        }

        void OnDisable()
        {
            _tearingDown = true;
            _colorTween?.Kill();
            _scaleTween?.Kill();
            foreach (var t in _additionalColorTweens) t?.Kill();
        }

        void OnEnable()
        {
            _tearingDown = false;
        }

        void OnValidate()
        {
            ResolveTarget();
        }

        // ── Public API ────────────────────────────────────────────────

        /** Enables a state by list index, pushing it to the top of the priority stack. */
        public void EnableState(int index)
        {
            if (!ValidateIndex(index)) return;

            // Move to top if already active, otherwise push
            _activeStack.Remove(index);
            _activeStack.Add(index);
            ApplyCurrentState();
        }

        /** Enables a state by name. Logs a warning if the name is not found. */
        public void EnableState(string stateName)
        {
            int index = FindIndexByName(stateName);
            if (index < 0) return;
            EnableState(index);
        }

        /** Disables a state by list index, removing it from the priority stack. */
        public void DisableState(int index)
        {
            if (!ValidateIndex(index)) return;

            _activeStack.Remove(index);
            ApplyCurrentState();
        }

        /** Disables a state by name. Logs a warning if the name is not found. */
        public void DisableState(string stateName)
        {
            int index = FindIndexByName(stateName);
            if (index < 0) return;
            DisableState(index);
        }

        /** Clears all active states, reverting to the original appearance. */
        public void DisableAllStates()
        {
            _activeStack.Clear();
            ApplyCurrentState();
        }

        /** Toggles a state on or off by index. */
        public void ToggleState(int index)
        {
            if (!ValidateIndex(index)) return;

            if (_activeStack.Contains(index))
                DisableState(index);
            else
                EnableState(index);
        }

        /** Toggles a state on or off by name. */
        public void ToggleState(string stateName)
        {
            int index = FindIndexByName(stateName);
            if (index < 0) return;
            ToggleState(index);
        }

        /** Returns true if the state at the given index is currently active. */
        public bool IsStateEnabled(int index) => _activeStack.Contains(index);

        /** Returns true if the named state is currently active. */
        public bool IsStateEnabled(string stateName)
        {
            int index = FindIndexByName(stateName);
            return index >= 0 && _activeStack.Contains(index);
        }

        // ── Testing buttons (Odin) ───────────────────────────────────

        [TitleGroup("Testing")]
        [HorizontalGroup("Testing/Row1")]
        [Button("Enable State")]
        void TestEnable(int index) => EnableState(index);

        [HorizontalGroup("Testing/Row1")]
        [Button("Disable State")]
        void TestDisable(int index) => DisableState(index);

        [TitleGroup("Testing")]
        [Button("Disable All States")]
        void TestDisableAll() => DisableAllStates();

        // ── Per-element inspector controls (play mode only) ───────────

#if UNITY_EDITOR
        /** Draws an active indicator and enable/disable buttons after each state element in the list. */
        void DrawStateElementControls(int index)
        {
            if (!Application.isPlaying) return;

            bool isActive = _activeStack.Contains(index);

            // Status indicator — green when enabled, dim when off.
            var prevColor = GUI.color;
            GUI.color = isActive ? new Color(0.3f, 1f, 0.4f) : new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label(isActive ? "● ON" : "○ OFF", GUILayout.Width(40));
            GUI.color = prevColor;

            // Per-element invoke/disable buttons.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Enable", GUILayout.Width(55)))
                EnableState(index);
            if (GUILayout.Button("Disable", GUILayout.Width(55)))
                DisableState(index);
            GUILayout.EndHorizontal();
        }
#endif

        // ── Internals ─────────────────────────────────────────────────

        /**
         * Auto-detects a SpriteRenderer or Graphic on this GameObject if neither
         * target field has been manually assigned. SpriteRenderer takes priority.
         */
        void ResolveTarget()
        {
            if (targetSprite == null && targetGraphic == null)
            {
                targetSprite = GetComponent<SpriteRenderer>();
                if (targetSprite == null)
                    targetGraphic = GetComponent<Graphic>();
            }
        }

        /** Captures the original color and scale so we can revert when all states are off. */
        void CaptureOriginals()
        {
            // Primary target
            if (targetSprite != null)
                _originalColor = targetSprite.color;
            else if (targetGraphic != null)
                _originalColor = targetGraphic.color;

            _originalScale = transform.localScale;

            // Additional sprite targets
            _additionalOriginalColors.Clear();
            _additionalOriginalScales.Clear();
            foreach (var sr in additionalSprites)
            {
                _additionalOriginalColors.Add(sr != null ? sr.color : Color.white);
                _additionalOriginalScales.Add(sr != null ? sr.transform.localScale : Vector3.one);
            }

            // Additional graphic targets
            foreach (var gr in additionalGraphics)
            {
                _additionalOriginalColors.Add(gr != null ? gr.color : Color.white);
                _additionalOriginalScales.Add(gr != null ? gr.transform.localScale : Vector3.one);
            }

            // Match tween list size
            _additionalColorTweens.Clear();
            for (int i = 0; i < _additionalOriginalColors.Count; i++)
                _additionalColorTweens.Add(null);
        }

        /**
         * Resolves the current visual target from the active stack and applies it.
         *
         * Priority model: the most recently enabled state (last in _activeStack) wins.
         * Color comes from the topmost state that has overrideColor enabled; if none
         * override color, the original color is used. Scale always comes from the
         * topmost state's scaleMultiplier.
         *
         * Tween settings are taken from the topmost state — if it has useTween enabled,
         * the transition animates; otherwise it's instant.
         */
        void ApplyCurrentState()
        {
            // During teardown, skip entirely — creating DOTween instances here spawns a
            // [DOTween] GameObject that leaks across scene loads.
            if (_tearingDown) return;

            // Determine tween settings and scale multiplier from top of stack
            float targetScaleMult = 1f;
            bool useTween = false;
            float tweenDuration = 0f;
            Ease tweenEase = Ease.OutQuad;
            bool hasColorOverride = false;
            Color overrideColor = Color.white;

            if (_activeStack.Count > 0)
            {
                // Top of stack determines scale and tween settings
                int topIdx = _activeStack[_activeStack.Count - 1];
                VisualStateOverride topState = states[topIdx];
                targetScaleMult = topState.scaleMultiplier;
                useTween = topState.useTween;
                tweenDuration = topState.tweenDuration;
                tweenEase = topState.tweenEase;

                // Walk from top to find the first state that overrides color
                for (int i = _activeStack.Count - 1; i >= 0; i--)
                {
                    VisualStateOverride s = states[_activeStack[i]];
                    if (s.overrideColor)
                    {
                        hasColorOverride = true;
                        overrideColor = s.color;
                        break;
                    }
                }
            }
            else if (defaultState != null)
            {
                // Stack empty — use the optional default state for tween and appearance.
                targetScaleMult = defaultState.scaleMultiplier;
                useTween = defaultState.useTween;
                tweenDuration = defaultState.tweenDuration;
                tweenEase = defaultState.tweenEase;
                if (defaultState.overrideColor)
                {
                    hasColorOverride = true;
                    overrideColor = defaultState.color;
                }
            }

            // Primary target color — use override or revert to its own original
            Color primaryColor = hasColorOverride ? overrideColor : _originalColor;
            Vector3 targetScale = _originalScale * targetScaleMult;

            // Apply primary target
            ApplyPrimaryColor(primaryColor, useTween, tweenDuration, tweenEase);
            ApplyPrimaryScale(targetScale, useTween, tweenDuration, tweenEase);

            // Apply additional targets — each reverts to its own original when no override
            ApplyAdditionalColors(hasColorOverride, overrideColor, useTween, tweenDuration, tweenEase);
            ApplyAdditionalScales(targetScaleMult, useTween, tweenDuration, tweenEase);
        }

        /** Applies color to the primary sprite or graphic target. */
        void ApplyPrimaryColor(Color color, bool tween, float duration, Ease ease)
        {
            _colorTween?.Kill();

            if (targetSprite != null)
            {
                if (tween)
                    _colorTween = targetSprite.DOColor(color, duration).SetEase(ease);
                else
                    targetSprite.color = color;
            }
            else if (targetGraphic != null)
            {
                if (tween)
                    _colorTween = targetGraphic.DOColor(color, duration).SetEase(ease);
                else
                    targetGraphic.color = color;
            }
        }

        /**
         * Applies color to all additional targets. When a state overrides color, all
         * targets get the override. Otherwise each reverts to its own captured original.
         */
        void ApplyAdditionalColors(bool hasOverride, Color overrideColor,
            bool tween, float duration, Ease ease)
        {
            int idx = 0;

            foreach (var sr in additionalSprites)
            {
                if (sr != null && idx < _additionalOriginalColors.Count)
                {
                    Color c = hasOverride ? overrideColor : _additionalOriginalColors[idx];
                    ApplyColorToTarget(idx, sr, null, c, tween, duration, ease);
                }
                idx++;
            }

            foreach (var gr in additionalGraphics)
            {
                if (gr != null && idx < _additionalOriginalColors.Count)
                {
                    Color c = hasOverride ? overrideColor : _additionalOriginalColors[idx];
                    ApplyColorToTarget(idx, null, gr, c, tween, duration, ease);
                }
                idx++;
            }
        }

        /** Applies color to a single additional target, managing its tween by index. */
        void ApplyColorToTarget(int idx, SpriteRenderer sr, Graphic gr, Color color,
            bool tween, float duration, Ease ease)
        {
            if (idx < _additionalColorTweens.Count)
                _additionalColorTweens[idx]?.Kill();

            if (sr != null)
            {
                if (tween)
                {
                    var tw = sr.DOColor(color, duration).SetEase(ease);
                    if (idx < _additionalColorTweens.Count) _additionalColorTweens[idx] = tw;
                }
                else
                    sr.color = color;
            }
            else if (gr != null)
            {
                if (tween)
                {
                    var tw = gr.DOColor(color, duration).SetEase(ease);
                    if (idx < _additionalColorTweens.Count) _additionalColorTweens[idx] = tw;
                }
                else
                    gr.color = color;
            }
        }

        /** Applies scale to the primary transform. */
        void ApplyPrimaryScale(Vector3 scale, bool tween, float duration, Ease ease)
        {
            _scaleTween?.Kill();

            if (tween)
                _scaleTween = transform.DOScale(scale, duration).SetEase(ease);
            else
                transform.localScale = scale;
        }

        /**
         * Applies scale to all additional targets. Each target is scaled relative to
         * its own captured original using the state's scaleMultiplier.
         */
        void ApplyAdditionalScales(float scaleMult, bool tween, float duration, Ease ease)
        {
            int idx = 0;

            foreach (var sr in additionalSprites)
            {
                if (sr != null && idx < _additionalOriginalScales.Count)
                {
                    Vector3 targetScale = _additionalOriginalScales[idx] * scaleMult;
                    if (tween)
                        sr.transform.DOScale(targetScale, duration).SetEase(ease);
                    else
                        sr.transform.localScale = targetScale;
                }
                idx++;
            }

            foreach (var gr in additionalGraphics)
            {
                if (gr != null && idx < _additionalOriginalScales.Count)
                {
                    Vector3 targetScale = _additionalOriginalScales[idx] * scaleMult;
                    if (tween)
                        gr.transform.DOScale(targetScale, duration).SetEase(ease);
                    else
                        gr.transform.localScale = targetScale;
                }
                idx++;
            }
        }

        /** Validates that an index is within the states list. */
        bool ValidateIndex(int index)
        {
            if (index >= 0 && index < states.Count) return true;
            Debug.LogWarning($"[VisualStateDriver] Index {index} out of range (0..{states.Count - 1})", this);
            return false;
        }

        /** Finds a state index by name. Logs a warning and returns -1 if not found. */
        int FindIndexByName(string stateName)
        {
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].name == stateName) return i;
            }

            Debug.LogWarning($"[VisualStateDriver] State '{stateName}' not found", this);
            return -1;
        }
    }
}
