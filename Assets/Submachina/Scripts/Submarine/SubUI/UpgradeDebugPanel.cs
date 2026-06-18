using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Runtime debug panel for granting/removing any upgrade in the catalog.
     *
     * Built with UI Toolkit — a UIDocument is created at runtime so no
     * manual UXML/USS asset setup is required. All styling is inline.
     *
     * Toggle with a configurable key (default: Tab). Shows a scrollable
     * grid of compact cards — one per upgrade — with the current level.
     * Click to grant one level. Shift+Click removes the upgrade entirely.
     *
     * Cards are color-coded:
     *   Green  — active (has at least one level)
     *   Yellow — maxed out
     *   Grey   — not yet granted
     *   Red    — prerequisites unmet
     *
     * Does NOT pause the game — apply upgrades while playing.
     */
    [RequireComponent(typeof(UIDocument))]
    public class UpgradeDebugPanel : SubmarineObserver
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Catalog")]
        [Tooltip("Pool containing ALL upgrades in the game (the 'master catalog'). " +
                 "Every entry is shown as a card in the debug panel.")]
        [SerializeField, Required] private UpgradeDraftPool catalog;

        [FoldoutGroup("Toggle")]
        [Tooltip("Key to toggle the debug panel on/off.")]
        [SerializeField] private Key toggleKey = Key.Tab;

        // =====================
        // Colors
        // =====================

        private static readonly Color PanelBg     = new(0.04f, 0.06f, 0.10f, 0.95f);
        private static readonly Color CardGrey    = new(0.18f, 0.20f, 0.24f);
        private static readonly Color CardGreen   = new(0.14f, 0.34f, 0.20f);
        private static readonly Color CardYellow  = new(0.38f, 0.33f, 0.10f);
        private static readonly Color CardRed     = new(0.32f, 0.14f, 0.14f);
        private static readonly Color CardHover   = new(0.28f, 0.42f, 0.55f);
        private static readonly Color HeaderGold  = new(1f, 0.85f, 0.3f);
        private static readonly Color TextLight   = new(0.78f, 0.84f, 0.90f);
        private static readonly Color TextGreen   = new(0.50f, 0.78f, 0.52f);
        private static readonly Color TextYellow  = new(1f, 0.84f, 0.31f);
        private static readonly Color TextRed     = new(0.94f, 0.33f, 0.31f);

        // =====================
        // State
        // =====================

        private UIDocument _doc;
        private VisualElement _root;
        private VisualElement _panel;
        private VisualElement _grid;
        private bool _visible;
        private readonly List<CardEntry> _cards = new();

        private struct CardEntry
        {
            public UpgradeDef def;
            public VisualElement card;
            public Label nameLabel;
            public Label levelLabel;
        }

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            _doc = GetComponent<UIDocument>();

            if (_doc.panelSettings == null)
                _doc.panelSettings = GetOrCreatePanelSettings();
        }

        /**
         * By Start(), all Awake and OnEnable calls have completed —
         * UIDocument has initialized its panel and rootVisualElement is ready.
         */
        private void Start()
        {
            BuildUI();
            _panel.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame)
                Toggle();

            if (_visible)
                RefreshCards();
        }

        // -------------------------------------------------------
        // Toggle
        // -------------------------------------------------------

        private void Toggle()
        {
            if (_panel == null) return;

            _visible = !_visible;
            _panel.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (_visible) RebuildCards();
        }

        // -------------------------------------------------------
        // UI Construction
        // -------------------------------------------------------

        private void BuildUI()
        {
            _root = _doc.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;

            // Main panel — anchored to the right side of the screen
            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.right = 8;
            _panel.style.top = 12;
            _panel.style.bottom = 12;
            _panel.style.width = new Length(38, LengthUnit.Percent);
            _panel.style.backgroundColor = PanelBg;
            _panel.style.borderTopLeftRadius = 8;
            _panel.style.borderTopRightRadius = 8;
            _panel.style.borderBottomLeftRadius = 8;
            _panel.style.borderBottomRightRadius = 8;
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;
            _panel.style.paddingLeft = 10;
            _panel.style.paddingRight = 10;
            _root.Add(_panel);

            // Header
            var header = new Label("UPGRADE DEBUG");
            header.style.fontSize = 20;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = HeaderGold;
            header.style.marginBottom = 2;
            _panel.Add(header);

            var hint = new Label("Tab to close  |  Click = grant  |  Shift+Click = remove");
            hint.style.fontSize = 11;
            hint.style.color = new Color(0.5f, 0.5f, 0.5f);
            hint.style.marginBottom = 8;
            _panel.Add(hint);

            // Scrollable grid area
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _panel.Add(scroll);

            // Grid container using flex-wrap
            _grid = new VisualElement();
            _grid.style.flexDirection = FlexDirection.Row;
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.style.alignContent = Align.FlexStart;
            scroll.Add(_grid);
        }

        // -------------------------------------------------------
        // Card Management
        // -------------------------------------------------------

        private void RebuildCards()
        {
            _grid.Clear();
            _cards.Clear();

            if (catalog == null || Sub?.Upgrades == null) return;

            for (int i = 0; i < catalog.upgrades.Count; i++)
            {
                var def = catalog.upgrades[i];
                if (def == null) continue;
                _cards.Add(CreateCard(def));
            }

            RefreshCards();
        }

        private CardEntry CreateCard(UpgradeDef def)
        {
            var card = new VisualElement();
            card.style.width = 160;
            card.style.height = 64;
            card.style.marginRight = 6;
            card.style.marginBottom = 6;
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
            card.style.backgroundColor = CardGrey;
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.justifyContent = Justify.Center;
            card.style.alignItems = Align.Center;
            card.pickingMode = PickingMode.Position;
            card.style.cursor = new UnityEngine.UIElements.Cursor();

            // Hover effect
            var capturedDef = def;
            Color normalColor = CardGrey;
            card.RegisterCallback<MouseEnterEvent>(_ =>
            {
                normalColor = card.resolvedStyle.backgroundColor;
                card.style.backgroundColor = CardHover;
            });
            card.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                card.style.backgroundColor = normalColor;
            });

            // Click handler
            card.RegisterCallback<ClickEvent>(evt =>
            {
                OnCardClicked(capturedDef);
                evt.StopPropagation();
            });

            // Upgrade name
            var nameLabel = new Label(def.upgradeName);
            nameLabel.style.fontSize = 13;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color = TextLight;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.pickingMode = PickingMode.Ignore;
            card.Add(nameLabel);

            // Level indicator
            var levelLabel = new Label("");
            levelLabel.style.fontSize = 11;
            levelLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            levelLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            levelLabel.style.marginTop = 2;
            levelLabel.pickingMode = PickingMode.Ignore;
            card.Add(levelLabel);

            _grid.Add(card);

            return new CardEntry { def = def, card = card, nameLabel = nameLabel, levelLabel = levelLabel };
        }

        /**
         * Updates every card's color and label to reflect current state.
         * Runs each frame while the panel is visible.
         */
        private void RefreshCards()
        {
            if (Sub?.Upgrades == null) return;

            for (int i = 0; i < _cards.Count; i++)
            {
                var entry = _cards[i];
                if (entry.def == null) continue;

                int level = Sub.Upgrades.GetLevel(entry.def);
                int max = entry.def.maxLevel;
                bool meetsPrereqs = Sub.Upgrades.MeetsPrerequisites(entry.def);

                // Background color
                Color bg;
                if (!meetsPrereqs && level == 0)       bg = CardRed;
                else if (level >= max)                  bg = CardYellow;
                else if (level > 0)                     bg = CardGreen;
                else                                    bg = CardGrey;
                entry.card.style.backgroundColor = bg;

                // Name color
                Color nameCol;
                if (level >= max)                       nameCol = TextYellow;
                else if (level > 0)                     nameCol = TextGreen;
                else if (!meetsPrereqs)                 nameCol = TextRed;
                else                                    nameCol = TextLight;
                entry.nameLabel.style.color = nameCol;

                // Level text
                if (max > 1)
                    entry.levelLabel.text = $"{level} / {max}";
                else
                    entry.levelLabel.text = level > 0 ? "ACTIVE" : "";
            }
        }

        // -------------------------------------------------------
        // Interaction
        // -------------------------------------------------------

        private void OnCardClicked(UpgradeDef def)
        {
            if (Sub?.Upgrades == null) return;

            var kb = Keyboard.current;
            bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

            if (shift)
            {
                if (Sub.Upgrades.GetLevel(def) > 0)
                {
                    Sub.Upgrades.Remove(def);
                    Debug.Log($"[UpgradeDebug] Removed '{def.upgradeName}'");
                }
            }
            else
            {
                bool granted = Sub.Upgrades.Grant(def);
                if (granted)
                    Debug.Log($"[UpgradeDebug] Granted '{def.upgradeName}' → level {Sub.Upgrades.GetLevel(def)}");
                else
                    Debug.Log($"[UpgradeDebug] Cannot grant '{def.upgradeName}' (maxed or prerequisites unmet)");
            }
        }

        // -------------------------------------------------------
        // Panel Settings
        // -------------------------------------------------------

        /**
         * Creates a PanelSettings in memory at runtime. The setup wizard
         * assigns a persistent asset in editor; this is the play-mode fallback.
         */
        private static PanelSettings GetOrCreatePanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 200;
            return settings;
        }
    }
}
