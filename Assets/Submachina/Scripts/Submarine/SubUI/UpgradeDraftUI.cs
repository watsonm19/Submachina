using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Minimal upgrade selection UI for testing.
     *
     * Listens to ResourceManager.onLevelUp and presents N upgrade choices
     * from an UpgradeDraftPool. Pauses the game while the selection is open.
     * On selection, grants the chosen upgrade and unpauses.
     *
     * Builds its own screen-space overlay Canvas at runtime — no manual
     * UI hierarchy setup required. Just add this component to the submarine
     * root and assign an UpgradeDraftPool asset.
     *
     * This is a placeholder V1 for testing; replace with a proper UI later.
     */
    public class UpgradeDraftUI : SubmarineObserver
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Draft")]
        [Tooltip("Pool of available upgrades to draft from on level-up.")]
        [SerializeField, Required] private UpgradeDraftPool draftPool;

        [FoldoutGroup("Style")]
        [Tooltip("Background color of the overlay panel.")]
        [SerializeField] private Color panelColor = new Color(0.05f, 0.08f, 0.12f, 0.92f);

        [FoldoutGroup("Style")]
        [Tooltip("Color of the upgrade choice buttons.")]
        [SerializeField] private Color buttonColor = new Color(0.15f, 0.25f, 0.35f, 1f);

        [FoldoutGroup("Style")]
        [Tooltip("Color of the button text.")]
        [SerializeField] private Color textColor = new Color(0.85f, 0.95f, 1f, 1f);

        [FoldoutGroup("Style")]
        [Tooltip("Color of the header/title text.")]
        [SerializeField] private Color headerColor = new Color(1f, 0.85f, 0.3f, 1f);

        // =====================
        // State
        // =====================

        private Canvas _canvas;
        private GameObject _panel;
        private readonly List<GameObject> _buttons = new List<GameObject>();
        private float _savedTimeScale;
        private bool _isOpen;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            BuildCanvas();
        }

        private bool _subscribed;

        private void OnEnable()
        {
            TrySubscribe();
        }

        /**
         * Fallback subscription — OnEnable may fire before ResourceManager
         * has registered with the Submarine facade (Awake order not guaranteed).
         * Start runs after all Awakes, so the facade slot is populated.
         */
        private void Start()
        {
            TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (_subscribed || Sub?.Resources == null) return;
            Sub.Resources.onLevelUp.AddListener(OnLevelUp);
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_subscribed && Sub?.Resources != null)
            {
                Sub.Resources.onLevelUp.RemoveListener(OnLevelUp);
                _subscribed = false;
            }

            if (_isOpen) Close();
        }

        // -------------------------------------------------------
        // Level-Up Handler
        // -------------------------------------------------------

        private void OnLevelUp(int newLevel)
        {
            if (Sub?.Upgrades == null || draftPool == null) return;

            var choices = draftPool.DrawChoices(Sub.Upgrades);
            if (choices.Count == 0) return;

            Open(choices, newLevel);
        }

        // -------------------------------------------------------
        // UI Construction
        // -------------------------------------------------------

        /**
         * Builds a screen-space overlay Canvas that lives as a child of
         * this GameObject. Hidden by default — shown on level-up.
         */
        private void BuildCanvas()
        {
            // Canvas root
            var canvasGO = new GameObject("UpgradeDraftCanvas");
            canvasGO.transform.SetParent(transform, false);

            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // Ensure an EventSystem exists so buttons can receive clicks
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<EventSystem>();
                esGO.AddComponent<StandaloneInputModule>();
            }

            // Full-screen panel
            _panel = CreatePanel(canvasGO.transform);
            _panel.SetActive(false);
        }

        private GameObject CreatePanel(Transform parent)
        {
            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(parent, false);

            var panelImage = panelGO.AddComponent<Image>();
            panelImage.color = panelColor;

            // Stretch to fill
            var panelRT = panelGO.GetComponent<RectTransform>();
            panelRT.anchorMin = Vector2.zero;
            panelRT.anchorMax = Vector2.one;
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            return panelGO;
        }

        // -------------------------------------------------------
        // Open / Close
        // -------------------------------------------------------

        /**
         * Shows the upgrade selection panel with the given choices.
         * Pauses the game so the player can read and decide.
         */
        private void Open(List<UpgradeDef> choices, int level)
        {
            _isOpen = true;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            // Clear old buttons
            ClearButtons();

            // Header
            var header = CreateText(_panel.transform, $"LEVEL {level} — CHOOSE AN UPGRADE",
                headerColor, 36, FontStyles.Bold);
            var headerRT = header.GetComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0.1f, 0.75f);
            headerRT.anchorMax = new Vector2(0.9f, 0.88f);
            headerRT.offsetMin = Vector2.zero;
            headerRT.offsetMax = Vector2.zero;
            _buttons.Add(header);

            // Choice buttons — evenly spaced across the middle
            float btnWidth = Mathf.Min(0.25f, 0.8f / choices.Count);
            float totalWidth = btnWidth * choices.Count + 0.02f * (choices.Count - 1);
            float startX = 0.5f - totalWidth * 0.5f;

            for (int i = 0; i < choices.Count; i++)
            {
                var def = choices[i];
                float x = startX + i * (btnWidth + 0.02f);

                var btnGO = CreateChoiceButton(_panel.transform, def, x, btnWidth);
                _buttons.Add(btnGO);
            }

            _panel.SetActive(true);
        }

        private void Close()
        {
            _panel.SetActive(false);
            ClearButtons();
            Time.timeScale = _savedTimeScale;
            _isOpen = false;
        }

        private void ClearButtons()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i] != null) Destroy(_buttons[i]);
            }
            _buttons.Clear();
        }

        // -------------------------------------------------------
        // Button Factory
        // -------------------------------------------------------

        /**
         * Creates a single upgrade choice button with name, description,
         * level indicator, and stat modifier summary.
         */
        private GameObject CreateChoiceButton(Transform parent, UpgradeDef def, float anchorX, float width)
        {
            // Button container
            var btnGO = new GameObject($"Choice_{def.upgradeName}");
            btnGO.transform.SetParent(parent, false);

            var btnImage = btnGO.AddComponent<Image>();
            btnImage.color = buttonColor;

            var btnRT = btnGO.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(anchorX, 0.2f);
            btnRT.anchorMax = new Vector2(anchorX + width, 0.7f);
            btnRT.offsetMin = Vector2.zero;
            btnRT.offsetMax = Vector2.zero;

            var btn = btnGO.AddComponent<Button>();
            var captured = def;
            btn.onClick.AddListener(() => OnChoiceSelected(captured));

            // Hover highlight
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.25f, 0.45f, 0.6f, 1f);
            colors.pressedColor = new Color(0.1f, 0.35f, 0.5f, 1f);
            btn.colors = colors;

            // Upgrade name
            int currentLevel = Sub?.Upgrades?.GetLevel(def) ?? 0;
            string levelText = def.maxLevel > 1 ? $"  [{currentLevel}/{def.maxLevel}]" : "";
            var nameText = CreateText(btnGO.transform, def.upgradeName + levelText,
                headerColor, 24, FontStyles.Bold);
            var nameRT = nameText.GetComponent<RectTransform>();
            nameRT.anchorMin = new Vector2(0.05f, 0.75f);
            nameRT.anchorMax = new Vector2(0.95f, 0.95f);
            nameRT.offsetMin = Vector2.zero;
            nameRT.offsetMax = Vector2.zero;

            // Description
            if (!string.IsNullOrEmpty(def.description))
            {
                var descText = CreateText(btnGO.transform, def.description,
                    textColor, 16, FontStyles.Italic);
                var descRT = descText.GetComponent<RectTransform>();
                descRT.anchorMin = new Vector2(0.05f, 0.45f);
                descRT.anchorMax = new Vector2(0.95f, 0.75f);
                descRT.offsetMin = Vector2.zero;
                descRT.offsetMax = Vector2.zero;
            }

            // Stat modifier summary
            if (def.statModifiers != null && def.statModifiers.Length > 0)
            {
                string summary = BuildModSummary(def);
                var modText = CreateText(btnGO.transform, summary,
                    new Color(0.6f, 0.9f, 0.7f, 1f), 14, FontStyles.Normal);
                var modRT = modText.GetComponent<RectTransform>();
                modRT.anchorMin = new Vector2(0.05f, 0.08f);
                modRT.anchorMax = new Vector2(0.95f, 0.45f);
                modRT.offsetMin = Vector2.zero;
                modRT.offsetMax = Vector2.zero;
            }

            return btnGO;
        }

        /**
         * Builds a human-readable summary of an upgrade's stat modifiers.
         * Example: "+15 MaxAirPressure\n-0.15× DashCooldown"
         */
        private string BuildModSummary(UpgradeDef def)
        {
            var lines = new System.Text.StringBuilder();
            for (int i = 0; i < def.statModifiers.Length; i++)
            {
                var mod = def.statModifiers[i];
                if (i > 0) lines.Append("\n");

                if (mod.additivePerLevel != 0f)
                {
                    string sign = mod.additivePerLevel > 0f ? "+" : "";
                    lines.Append($"{sign}{mod.additivePerLevel} {mod.stat}");
                }

                if (mod.multiplierPerLevel != 0f)
                {
                    if (mod.additivePerLevel != 0f) lines.Append("  ");
                    string sign = mod.multiplierPerLevel > 0f ? "+" : "";
                    lines.Append($"{sign}{mod.multiplierPerLevel * 100f:F0}% {mod.stat}");
                }
            }
            return lines.ToString();
        }

        // -------------------------------------------------------
        // Selection
        // -------------------------------------------------------

        private void OnChoiceSelected(UpgradeDef def)
        {
            if (Sub?.Upgrades == null) return;

            bool granted = Sub.Upgrades.Grant(def);
            if (granted)
                Debug.Log($"[UpgradeDraftUI] Granted '{def.upgradeName}' → level {Sub.Upgrades.GetLevel(def)}");
            else
                Debug.LogWarning($"[UpgradeDraftUI] Failed to grant '{def.upgradeName}'");

            Close();
        }

        // -------------------------------------------------------
        // Text Helper
        // -------------------------------------------------------

        private GameObject CreateText(Transform parent, string text, Color color,
            int fontSize, FontStyles style)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            return go;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Simulate Level Up"), GUIColor(0.6f, 1f, 0.6f)]
        private void DebugSimulateLevelUp()
        {
            if (!Application.isPlaying) { Debug.Log("[UpgradeDraftUI] Play mode only."); return; }
            OnLevelUp(Sub?.Upgrades != null ? 1 : 0);
        }
#endif
    }
}
