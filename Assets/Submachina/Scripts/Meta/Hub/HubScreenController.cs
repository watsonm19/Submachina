using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;
using Submachina.Core;

namespace Submachina.Meta
{
    /**
     * The hub ("Drydock") screen: outfitting shop, loadout picker and mission
     * board, all driven off ProfileService with no submarine instance present.
     *
     * Builds its own screen-space overlay Canvas at runtime — no manual UI
     * hierarchy setup required, just drop this on an empty GameObject in the
     * Hub scene and assign the catalog + resource type list. This mirrors
     * UpgradeDraftUI's "runtime-built placeholder UI" approach: function over
     * beauty, replace with hand-authored UI once the layout settles.
     *
     * Each panel is rebuilt from scratch (old rows destroyed, new ones made)
     * whenever its underlying data changes, rather than trying to diff/patch
     * existing rows — simple, and cheap enough for hub-visit frequency.
     */
    public class HubScreenController : MonoBehaviour
    {
        // =====================
        // Data
        // =====================

        [FoldoutGroup("Data")]
        [Tooltip("Shop stock, loadout slots and their prices.")]
        [SerializeField, Required] private UpgradeCatalog catalog;

        [FoldoutGroup("Data")]
        [Tooltip("Every resource type the game can spawn — drives the wallet display and mission generation/forecast lookups.")]
        [SerializeField] private List<ResourceType> resourceTypes = new();

        [FoldoutGroup("Data")]
        [Tooltip("Scene MissionContext.Launch loads. Informational only — MissionContext owns the real scene name constant; kept here for future flexibility.")]
        [SerializeField] private string missionSceneName = "Mission_Descent";

        // =====================
        // Style
        // =====================

        [FoldoutGroup("Style")] [SerializeField] private Color backgroundColor = new(0.04f, 0.06f, 0.09f, 1f);
        [FoldoutGroup("Style")] [SerializeField] private Color panelColor = new(0.09f, 0.13f, 0.18f, 0.95f);
        [FoldoutGroup("Style")] [SerializeField] private Color rowColor = new(0.14f, 0.19f, 0.25f, 1f);
        [FoldoutGroup("Style")] [SerializeField] private Color buttonColor = new(0.18f, 0.28f, 0.38f, 1f);
        [FoldoutGroup("Style")] [SerializeField] private Color headerColor = new(1f, 0.85f, 0.3f, 1f);
        [FoldoutGroup("Style")] [SerializeField] private Color textColor = new(0.85f, 0.95f, 1f, 1f);
        [FoldoutGroup("Style")] [SerializeField] private Color dimColor = new(0.45f, 0.5f, 0.55f, 1f);
        [FoldoutGroup("Style")] [SerializeField] private Color affordColor = new(0.55f, 0.9f, 0.6f, 1f);
        [FoldoutGroup("Style")] [SerializeField] private Color unaffordColor = new(0.95f, 0.4f, 0.4f, 1f);
        [FoldoutGroup("Style")] [SerializeField] private Color pickedColor = new(0.3f, 0.6f, 0.4f, 1f);

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")] public UnityEvent<UpgradeDef> onUpgradePurchased;
        [FoldoutGroup("Events")] public UnityEvent onLoadoutChanged;
        [FoldoutGroup("Events")] public UnityEvent<MissionSpec> onMissionLaunched;
        [FoldoutGroup("Events")] public UnityEvent onSaveReset;

        // =====================
        // Tabs
        // =====================

        private enum Tab { Outfitting, Loadout, Missions }

        // =====================
        // Runtime state
        // =====================

        private TextMeshProUGUI[] _walletTexts;
        private TextMeshProUGUI _statsText;
        private TextMeshProUGUI _debriefText;

        private Button _tabOutfitting, _tabLoadout, _tabMissions;
        private GameObject _outfittingPanel, _loadoutPanel, _missionsPanel;
        private RectTransform _outfittingContent, _loadoutContent, _missionsContent;

        private bool _missionsBuilt;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Start()
        {
            BuildUI();
            RefreshOutfittingPanel();
            RefreshLoadoutPanel();
            ShowTab(Tab.Outfitting);
        }

        // =========================================================
        // Canvas construction
        // =========================================================

        /** Builds the full canvas hierarchy once: background, header, tab bar, the three panels, and the reset button. */
        private void BuildUI()
        {
            var canvasGO = new GameObject("HubCanvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

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

            var background = MakePanel(canvasGO.transform, "Background", backgroundColor);
            Stretch(background.GetComponent<RectTransform>());

            // Root vertical stack: header / tab bar / content area
            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(background.transform, false);
            Stretch(root.GetComponent<RectTransform>());

            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(28, 28, 20, 20);
            rootLayout.spacing = 14f;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            BuildHeader(root.transform);
            BuildTabBar(root.transform);
            BuildContentArea(root.transform);
            BuildResetButton(canvasGO.transform);
        }

        /** Title, resource wallet, debrief line and stats line. */
        private void BuildHeader(Transform parent)
        {
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(parent, false);

            var layout = header.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            MakeText(header.transform, "SUBMACHINA — DRYDOCK", headerColor, 32, FontStyles.Bold, TextAlignmentOptions.Left);

            // One text per resource type, colored with the type's tint
            var walletRow = new GameObject("Wallet", typeof(RectTransform));
            walletRow.transform.SetParent(header.transform, false);
            var walletLayout = walletRow.AddComponent<HorizontalLayoutGroup>();
            walletLayout.spacing = 28f;
            walletLayout.childControlWidth = true;
            walletLayout.childControlHeight = true;
            walletLayout.childForceExpandWidth = false;

            _walletTexts = new TextMeshProUGUI[resourceTypes.Count];
            for (int i = 0; i < resourceTypes.Count; i++)
            {
                var type = resourceTypes[i];
                _walletTexts[i] = MakeText(walletRow.transform, string.Empty,
                    type != null ? type.tint : textColor, 17, FontStyles.Normal, TextAlignmentOptions.Left);
            }

            _debriefText = MakeText(header.transform, string.Empty, affordColor, 16, FontStyles.Italic, TextAlignmentOptions.Left);
            _statsText = MakeText(header.transform, string.Empty, textColor, 16, FontStyles.Normal, TextAlignmentOptions.Left);

            RefreshWallet();
        }

        /** Rebuilds the wallet amounts, mission debrief line and headline stats line from current profile state. */
        private void RefreshWallet()
        {
            for (int i = 0; i < resourceTypes.Count; i++)
            {
                var type = resourceTypes[i];
                if (type == null || _walletTexts[i] == null) continue;
                _walletTexts[i].text = $"{type.displayName}: {ProfileService.GetResource(type)}";
            }

            // Debrief line only shows once a mission has actually resolved
            bool hasResult = MissionContext.HasLastResult;
            _debriefText.gameObject.SetActive(hasResult);
            if (hasResult)
            {
                _debriefText.text = MissionContext.LastMissionSucceeded
                    ? "Mission successful — cargo banked."
                    : "Sub lost. Unbanked cargo gone.";
                _debriefText.color = MissionContext.LastMissionSucceeded ? affordColor : unaffordColor;
            }

            float ratedDepth = HubStats.ComputeRatedDepth(catalog);
            int cargo = HubStats.ComputeCargoCapacity(catalog);
            float o2 = HubStats.ComputeMaxO2(catalog);
            var profile = ProfileService.Current;
            _statsText.text = $"Rated depth: {ratedDepth:0} m   Cargo: {cargo}   O2: {o2:0}   " +
                               $"Missions: {profile.missionsCompleted}/{profile.missionsCompleted + profile.missionsFailed}";
        }

        /** Three tab buttons switching between the outfitting/loadout/missions panels. */
        private void BuildTabBar(Transform parent)
        {
            var bar = new GameObject("TabBar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);

            var layout = bar.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var le = bar.AddComponent<LayoutElement>();
            le.preferredHeight = 56f;
            le.flexibleHeight = 0f;

            _tabOutfitting = MakeButton(bar.transform, "OUTFITTING", buttonColor, headerColor, 18, () => ShowTab(Tab.Outfitting));
            _tabLoadout = MakeButton(bar.transform, "LOADOUT", buttonColor, headerColor, 18, () => ShowTab(Tab.Loadout));
            _tabMissions = MakeButton(bar.transform, "MISSIONS", buttonColor, headerColor, 18, () => ShowTab(Tab.Missions));
        }

        /** Hosts the three scrollable panels stacked on top of each other; ShowTab toggles which is active. */
        private void BuildContentArea(Transform parent)
        {
            var area = new GameObject("Content", typeof(RectTransform));
            area.transform.SetParent(parent, false);
            var le = area.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;

            var (outfitPanel, outfitContent) = BuildScrollPanel(area.transform, "OutfittingPanel");
            var (loadoutPanel, loadoutContent) = BuildScrollPanel(area.transform, "LoadoutPanel");
            var (missionsPanel, missionsContent) = BuildScrollPanel(area.transform, "MissionsPanel");

            _outfittingPanel = outfitPanel; _outfittingContent = outfitContent;
            _loadoutPanel = loadoutPanel; _loadoutContent = loadoutContent;
            _missionsPanel = missionsPanel; _missionsContent = missionsContent;
        }

        /** Debug-only reset, pinned to the bottom-right corner outside the main layout flow. */
        private void BuildResetButton(Transform canvasParent)
        {
            var btn = MakeButton(canvasParent, "RESET SAVE", unaffordColor, Color.white, 14, OnResetClicked);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-16f, 16f);
            rt.sizeDelta = new Vector2(150f, 40f);
        }

        // =========================================================
        // Tab switching
        // =========================================================

        /** Activates one panel, dims the other tab buttons, and lazily generates mission offers on first visit. */
        private void ShowTab(Tab tab)
        {
            _outfittingPanel.SetActive(tab == Tab.Outfitting);
            _loadoutPanel.SetActive(tab == Tab.Loadout);
            _missionsPanel.SetActive(tab == Tab.Missions);

            SetTabHighlight(_tabOutfitting, tab == Tab.Outfitting);
            SetTabHighlight(_tabLoadout, tab == Tab.Loadout);
            SetTabHighlight(_tabMissions, tab == Tab.Missions);

            if (tab == Tab.Missions && !_missionsBuilt) RefreshMissionsPanel();
        }

        private void SetTabHighlight(Button btn, bool active)
        {
            Color c = active ? pickedColor : buttonColor;
            var colors = btn.colors;
            colors.normalColor = c;
            btn.colors = colors;
            if (btn.targetGraphic != null) btn.targetGraphic.color = c;
        }

        // =========================================================
        // Outfitting panel
        // =========================================================

        /** Rebuilds every shop row from catalog.entries. */
        private void RefreshOutfittingPanel()
        {
            ClearChildren(_outfittingContent);
            foreach (var entry in catalog.entries)
                BuildOutfittingRow(_outfittingContent, entry);
        }

        /**
         * One shop row: name + level, description, cost/gating status, and a
         * BUY button. Disabled when maxed or an unmet prerequisite blocks it;
         * the cost line colors each resource line green/red by affordability.
         */
        private void BuildOutfittingRow(Transform parent, ShopEntry entry)
        {
            if (entry?.def == null) return;
            var def = entry.def;
            int owned = ProfileService.GetUpgradeLevel(def.name);
            bool maxed = owned >= def.maxLevel;
            string unmetReq = FindUnmetPrerequisites(def);

            var row = MakePanel(parent, $"Row_{def.name}", rowColor);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(16, 16, 12, 12);
            rowLayout.spacing = 16f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            row.AddComponent<LayoutElement>().minHeight = 100f;

            // Info column: name/level, description, cost or gating reason
            var info = new GameObject("Info", typeof(RectTransform));
            info.transform.SetParent(row.transform, false);
            var infoLayout = info.AddComponent<VerticalLayoutGroup>();
            infoLayout.spacing = 4f;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            info.AddComponent<LayoutElement>().flexibleWidth = 1f;

            string levelSuffix = def.maxLevel > 1 ? $"  (Lv {owned}/{def.maxLevel})" : owned > 0 ? "  (Owned)" : string.Empty;
            MakeText(info.transform, def.upgradeName + levelSuffix, headerColor, 22, FontStyles.Bold, TextAlignmentOptions.Left);

            if (!string.IsNullOrEmpty(def.description))
                MakeText(info.transform, def.description, textColor, 15, FontStyles.Italic, TextAlignmentOptions.Left);

            var costs = UpgradeCatalog.CostForLevel(entry, owned);
            string statusLine = maxed ? "MAXED" : unmetReq != null ? $"Requires: {unmetReq}" : BuildCostLine(costs);
            MakeText(info.transform, statusLine, textColor, 15, FontStyles.Normal, TextAlignmentOptions.Left);

            // Buy button
            bool canBuy = !maxed && unmetReq == null && CanAfford(costs);
            var buyBtn = MakeButton(row.transform, maxed ? "MAXED" : "BUY", buttonColor, headerColor, 18,
                () => OnBuyClicked(entry, owned, costs));
            var buyLE = buyBtn.gameObject.AddComponent<LayoutElement>();
            buyLE.preferredWidth = 140f;
            buyLE.preferredHeight = 68f;
            buyBtn.interactable = canBuy;
        }

        /** Returns a comma-joined list of unmet prerequisite names, or null when all are satisfied. */
        private string FindUnmetPrerequisites(UpgradeDef def)
        {
            if (def.prerequisites == null) return null;

            string result = null;
            foreach (var req in def.prerequisites)
            {
                if (req == null || ProfileService.GetUpgradeLevel(req.name) > 0) continue;
                result = result == null ? req.upgradeName : $"{result}, {req.upgradeName}";
            }
            return result;
        }

        private void OnBuyClicked(ShopEntry entry, int ownedLevels, ResourceCost[] costs)
        {
            if (!ProfileService.TrySpend(costs)) return;

            ProfileService.SetUpgradeLevel(entry.def.name, ownedLevels + 1);
            onUpgradePurchased?.Invoke(entry.def);

            RefreshWallet();
            RefreshOutfittingPanel();
        }

        // =========================================================
        // Loadout panel
        // =========================================================

        /** Rebuilds one section per catalog.loadoutSlots. */
        private void RefreshLoadoutPanel()
        {
            ClearChildren(_loadoutContent);
            foreach (var slot in catalog.loadoutSlots)
                BuildLoadoutSlot(_loadoutContent, slot);
        }

        /** Slot header/description followed by a row of toggle buttons, one per competing choice. */
        private void BuildLoadoutSlot(Transform parent, LoadoutSlotDef slot)
        {
            if (slot == null) return;

            var section = MakePanel(parent, $"Slot_{slot.slotName}", rowColor);
            var sectionLayout = section.AddComponent<VerticalLayoutGroup>();
            sectionLayout.padding = new RectOffset(16, 16, 12, 12);
            sectionLayout.spacing = 8f;
            sectionLayout.childControlWidth = true;
            sectionLayout.childControlHeight = true;
            sectionLayout.childForceExpandWidth = true;

            MakeText(section.transform, $"{slot.slotName}  (pick {slot.maxPicks})", headerColor, 20, FontStyles.Bold, TextAlignmentOptions.Left);
            if (!string.IsNullOrEmpty(slot.description))
                MakeText(section.transform, slot.description, textColor, 14, FontStyles.Italic, TextAlignmentOptions.Left);

            var choicesRow = new GameObject("Choices", typeof(RectTransform));
            choicesRow.transform.SetParent(section.transform, false);
            var choicesLayout = choicesRow.AddComponent<HorizontalLayoutGroup>();
            choicesLayout.spacing = 12f;
            choicesLayout.childControlWidth = true;
            choicesLayout.childControlHeight = true;
            choicesLayout.childForceExpandWidth = false;
            choicesLayout.childForceExpandHeight = true;

            foreach (var choice in slot.choices)
            {
                if (choice != null) BuildLoadoutChoice(choicesRow.transform, slot, choice);
            }
        }

        /**
         * One loadout choice button. A choice absent from the shop's entries has
         * no purchase gate and counts as owned by default; a choice that IS a
         * shop entry must be bought in Outfitting before it can be picked here.
         */
        private void BuildLoadoutChoice(Transform parent, LoadoutSlotDef slot, UpgradeDef choice)
        {
            var shopEntry = catalog.FindEntry(choice);
            bool owned = shopEntry == null || ProfileService.GetUpgradeLevel(choice.name) > 0;
            bool picked = ProfileService.IsLoadoutChoice(slot.slotName, choice.name);

            var block = new GameObject($"Choice_{choice.name}", typeof(RectTransform));
            block.transform.SetParent(parent, false);
            var blockLayout = block.AddComponent<VerticalLayoutGroup>();
            blockLayout.spacing = 4f;
            blockLayout.childControlWidth = true;
            blockLayout.childControlHeight = true;
            blockLayout.childForceExpandWidth = true;
            block.AddComponent<LayoutElement>().preferredWidth = 220f;

            Color btnColor = picked ? pickedColor : owned ? buttonColor : dimColor;
            var btn = MakeButton(block.transform, choice.upgradeName + (picked ? "  [PICKED]" : string.Empty), btnColor, textColor, 16,
                owned ? () => OnLoadoutToggle(slot, choice) : null);
            var btnLE = btn.gameObject.AddComponent<LayoutElement>();
            btnLE.preferredHeight = 56f;
            btn.interactable = owned;

            if (!owned)
                MakeText(block.transform, "(purchase in Outfitting)", dimColor, 12, FontStyles.Italic, TextAlignmentOptions.Center);
        }

        private void OnLoadoutToggle(LoadoutSlotDef slot, UpgradeDef choice)
        {
            ProfileService.ToggleLoadoutChoice(slot.slotName, choice.name, slot.maxPicks);
            onLoadoutChanged?.Invoke();
            RefreshLoadoutPanel();
        }

        // =========================================================
        // Missions panel
        // =========================================================

        /**
         * Generates a fresh batch of offers and renders three mission cards.
         * The seed is derived from lifetime mission counts, so re-generating
         * (e.g. re-entering this tab) reproduces the same offers until a
         * mission actually resolves and the counts change.
         */
        private void RefreshMissionsPanel()
        {
            ClearChildren(_missionsContent);

            float ratedDepth = HubStats.ComputeRatedDepth(catalog);
            var profile = ProfileService.Current;
            int seed = profile.missionsCompleted + profile.missionsFailed;
            var offers = MissionGenerator.GenerateOffers(ratedDepth, resourceTypes, seed);

            var cardsRow = new GameObject("Cards", typeof(RectTransform));
            cardsRow.transform.SetParent(_missionsContent, false);
            var cardsLayout = cardsRow.AddComponent<HorizontalLayoutGroup>();
            cardsLayout.spacing = 16f;
            cardsLayout.childControlWidth = true;
            cardsLayout.childControlHeight = true;
            cardsLayout.childForceExpandWidth = true;

            foreach (var spec in offers)
                BuildMissionCard(cardsRow.transform, spec, ratedDepth);

            _missionsBuilt = true;
        }

        /** One scanner-report card: title, flavor, target depth (flagged when beyond rated depth), conditions, forecast, reward, and a LAUNCH button. */
        private void BuildMissionCard(Transform parent, MissionSpec spec, float ratedDepth)
        {
            var card = MakePanel(parent, $"Card_{spec.title}", rowColor);
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            var cardLE = card.AddComponent<LayoutElement>();
            cardLE.flexibleWidth = 1f;
            cardLE.minWidth = 340f;

            MakeText(card.transform, spec.title, headerColor, 20, FontStyles.Bold, TextAlignmentOptions.Left);
            MakeText(card.transform, spec.flavor, textColor, 13, FontStyles.Italic, TextAlignmentOptions.Left);

            bool beyondRated = spec.targetDepth > ratedDepth;
            string depthLine = $"TARGET DEPTH {spec.targetDepth:0} m" + (beyondRated ? " ⚠ BEYOND RATED DEPTH" : string.Empty);
            MakeText(card.transform, depthLine, beyondRated ? unaffordColor : textColor, 15, FontStyles.Bold, TextAlignmentOptions.Left);

            string currentWord = spec.currentStrength < 0.3f ? "calm" : spec.currentStrength < 1.0f ? "moderate" : "strong";
            MakeText(card.transform, $"Current: {currentWord}", textColor, 14, FontStyles.Normal, TextAlignmentOptions.Left);

            string o2Word = spec.o2Richness < 0.85f ? "thin" : spec.o2Richness < 1.15f ? "normal" : "rich";
            MakeText(card.transform, $"O2: {o2Word}", textColor, 14, FontStyles.Normal, TextAlignmentOptions.Left);

            // Scanner forecast: resolve display name/tint through the serialized resource list, skip anything unrecognized
            foreach (var forecast in spec.forecast)
            {
                var type = FindResourceType(forecast.resourceKey);
                if (type == null) continue;
                MakeText(card.transform, $"{type.displayName}: {forecast.Grade}", type.tint, 13, FontStyles.Normal, TextAlignmentOptions.Left);
            }

            // No completion reward line — the forecast above IS the payout:
            // those resources spawn in the level and must be mined + hauled home
            var launchBtn = MakeButton(card.transform, "LAUNCH", buttonColor, headerColor, 18, () => OnLaunchClicked(spec));
            launchBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
        }

        private void OnLaunchClicked(MissionSpec spec)
        {
            onMissionLaunched?.Invoke(spec);
            MissionContext.Launch(spec);
        }

        /** Resolves a resource key to its ResourceType asset through the serialized list, or null when unrecognized. */
        private ResourceType FindResourceType(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var type in resourceTypes)
                if (type != null && type.Key == key) return type;
            return null;
        }

        // =========================================================
        // Reset
        // =========================================================

        private void OnResetClicked()
        {
            ProfileService.ResetProfile();
            onSaveReset?.Invoke();

            _missionsBuilt = false;
            RefreshWallet();
            RefreshOutfittingPanel();
            RefreshLoadoutPanel();
            if (_missionsPanel.activeSelf) RefreshMissionsPanel();
        }

        // =========================================================
        // Shared helpers
        // =========================================================

        /** Builds a per-resource cost line, coloring each line green when affordable and red when not. */
        private string BuildCostLine(ResourceCost[] costs)
        {
            if (costs == null || costs.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < costs.Length; i++)
            {
                var cost = costs[i];
                if (cost.type == null) continue;
                if (sb.Length > 0) sb.Append("   ");

                bool afford = ProfileService.GetResource(cost.type) >= cost.amount;
                string hex = ColorUtility.ToHtmlStringRGB(afford ? affordColor : unaffordColor);
                sb.Append($"<color=#{hex}>{cost.amount} {cost.type.displayName}</color>");
            }
            return sb.ToString();
        }

        private bool CanAfford(ResourceCost[] costs)
        {
            if (costs == null) return true;
            foreach (var cost in costs)
                if (cost.type != null && ProfileService.GetResource(cost.type) < cost.amount) return false;
            return true;
        }

        private void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        /** A ScrollRect-backed panel: a masked viewport over a vertically-laid-out, auto-sizing content root. Returns (panel root, content transform to add rows to). */
        private (GameObject panel, RectTransform content) BuildScrollPanel(Transform parent, string name)
        {
            var panelGO = MakePanel(parent, name, panelColor);
            Stretch(panelGO.GetComponent<RectTransform>());

            var scrollGO = new GameObject("Scroll", typeof(RectTransform));
            scrollGO.transform.SetParent(panelGO.transform, false);
            Stretch(scrollGO.GetComponent<RectTransform>());
            scrollGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // near-transparent, just catches drag input

            var scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            Stretch(viewportGO.GetComponent<RectTransform>());
            viewportGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = Vector2.zero;

            var contentLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(4, 4, 4, 4);
            contentLayout.spacing = 10f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scrollRect.viewport = viewportGO.GetComponent<RectTransform>();
            scrollRect.content = contentRT;

            return (panelGO, contentRT);
        }

        private GameObject MakePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = color;
            return go;
        }

        /**
         * Builds a clickable button whose resting/hover/pressed/disabled colors
         * live entirely in its ColorBlock — Selectable's ColorTint transition
         * overwrites the target Image's color on the very next state change, so
         * setting Image.color alone (without also setting colors.normalColor)
         * gets silently discarded.
         */
        private Button MakeButton(Transform parent, string label, Color bg, Color fg, int fontSize, UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(onClick);

            var colors = btn.colors;
            colors.normalColor = bg;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.15f);
            colors.pressedColor = Color.Lerp(bg, Color.black, 0.15f);
            colors.disabledColor = dimColor;
            colors.colorMultiplier = 1f;
            btn.colors = colors;
            img.color = bg;

            var text = MakeText(go.transform, label, fg, fontSize, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            return btn;
        }

        private TextMeshProUGUI MakeText(Transform parent, string text, Color color, int fontSize, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;

            return tmp;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
