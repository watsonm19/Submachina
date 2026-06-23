using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using TMPro;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * The "assign this controller" overlay shown while the game is frozen for a join.
     *
     * LocalPlayerManager calls Open(...) with the free player slots and the device(s)
     * that triggered the join. The overlay lists each free slot as a button; the
     * joining player picks one with their own device (D-pad / stick / arrows) and
     * confirms (south button / Enter / click), or cancels (east button / Esc).
     *
     * The UI is built from code on first use so the component is drop-in — just add
     * it to the scene and reference it from LocalPlayerManager. Colours and font size
     * are exposed for restyling, and UnityEvents fire at each step so Feel/MMF juice
     * can be wired in the inspector.
     *
     * All timing uses unscaled time because the game is paused (Time.timeScale = 0)
     * while this overlay is open.
     */
    public class PlayerJoinOverlay : MonoBehaviour
    {
        /** A free slot the joining player can pick. Index maps back to LocalPlayerManager's slot list. */
        public struct SlotInfo
        {
            public int Index;
            public string Label;
            public Color Color;
        }

        // =====================
        // Style
        // =====================

        [FoldoutGroup("Style")]
        [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.78f);
        [FoldoutGroup("Style")]
        [SerializeField] private Color titleColor = new Color(0.9f, 0.95f, 1f);
        [FoldoutGroup("Style")]
        [SerializeField] private Color slotNormalColor = new Color(0.18f, 0.22f, 0.28f, 0.95f);
        [FoldoutGroup("Style")]
        [SerializeField] private Color slotHighlightColor = new Color(0.30f, 0.75f, 0.95f, 1f);
        [FoldoutGroup("Style")]
        [SerializeField] private int titleFontSize = 42;
        [FoldoutGroup("Style")]
        [SerializeField] private int slotFontSize = 30;

        // =====================
        // Events (hook Feel/MMF here)
        // =====================

        [FoldoutGroup("Events")]
        public UnityEvent onOpened = new();
        [FoldoutGroup("Events")]
        public UnityEvent<int> onHighlightChanged = new();
        [FoldoutGroup("Events")]
        public UnityEvent<int> onConfirmed = new();
        [FoldoutGroup("Events")]
        public UnityEvent onCancelled = new();
        [FoldoutGroup("Events")]
        public UnityEvent onClosed = new();

        // =====================
        // State
        // =====================

        private GameObject _canvasRoot;
        private TextMeshProUGUI _subtitle;
        private Transform _slotRow;

        private readonly List<SlotInfo> _slots = new();
        private readonly List<Image> _slotImages = new();
        private readonly List<InputDevice> _devices = new();

        private Action<int> _onConfirm;
        private Action _onCancel;
        private int _highlight;
        private float _stickCooldown;

        /** True while the overlay is shown and accepting input. */
        public bool IsOpen { get; private set; }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Shows the overlay for a join. 'slots' are the currently free players,
         * 'devices' are the controller(s) that triggered the join (used to drive
         * selection), and the callbacks fire on the player's choice.
         */
        public void Open(IReadOnlyList<SlotInfo> slots, IReadOnlyList<InputDevice> devices,
                         Action<int> onConfirm, Action onCancel)
        {
            EnsureBuilt();

            // Cache the request
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            _slots.Clear();
            for (int i = 0; i < slots.Count; i++) _slots.Add(slots[i]);

            _devices.Clear();
            if (devices != null)
                for (int i = 0; i < devices.Count; i++) _devices.Add(devices[i]);

            // Build a button per free slot and show
            BuildSlotButtons();
            _subtitle.text = $"{DescribeDevices()} — choose a player";
            _highlight = 0;
            ApplyHighlight();

            _canvasRoot.SetActive(true);
            IsOpen = true;
            onOpened.Invoke();
        }

        /** Hides the overlay and clears the active request. */
        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
            _onConfirm = null;
            _onCancel = null;
            onClosed.Invoke();
        }

        // -------------------------------------------------------
        // Input loop (unscaled — runs while the game is frozen)
        // -------------------------------------------------------

        private void Update()
        {
            if (!IsOpen || _slots.Count == 0) return;

            // Cancel takes priority so a player can always back out
            if (ReadCancel())
            {
                var cb = _onCancel;
                Close();
                cb?.Invoke();
                onCancelled.Invoke();
                return;
            }

            // Move the highlight with the joining device
            int dir = ReadNavigation();
            if (dir != 0)
            {
                _highlight = (_highlight + dir + _slots.Count) % _slots.Count;
                ApplyHighlight();
                onHighlightChanged.Invoke(_slots[_highlight].Index);
            }

            // Confirm the highlighted slot
            if (ReadConfirm())
                Confirm(_highlight);
        }

        // -------------------------------------------------------
        // Selection
        // -------------------------------------------------------

        /** Commits the slot at the given list position: fires events and closes. */
        private void Confirm(int listIndex)
        {
            if (listIndex < 0 || listIndex >= _slots.Count) return;

            int slotIndex = _slots[listIndex].Index;
            var cb = _onConfirm;
            Close();
            cb?.Invoke(slotIndex);
            onConfirmed.Invoke(slotIndex);
        }

        /** Repaints button colours so the current highlight stands out. */
        private void ApplyHighlight()
        {
            for (int i = 0; i < _slotImages.Count; i++)
                _slotImages[i].color = i == _highlight ? slotHighlightColor : slotNormalColor;
        }

        // -------------------------------------------------------
        // Device reading (gamepad + keyboard)
        // -------------------------------------------------------

        /** Returns -1 (left), +1 (right) or 0 from the joining device(s) this frame. */
        private int ReadNavigation()
        {
            // Analog-stick repeat is rate-limited; decrement the cooldown on unscaled time
            if (_stickCooldown > 0f) _stickCooldown -= Time.unscaledDeltaTime;

            int dir = 0;
            float stick = 0f;

            for (int i = 0; i < _devices.Count; i++)
            {
                switch (_devices[i])
                {
                    // Edge-triggered D-pad steps respond instantly; remember stick for repeat handling
                    case Gamepad gp:
                        if (gp.dpad.left.wasPressedThisFrame) dir = -1;
                        if (gp.dpad.right.wasPressedThisFrame) dir = 1;
                        stick = Mathf.Abs(gp.leftStick.x.ReadValue()) > Mathf.Abs(stick)
                            ? gp.leftStick.x.ReadValue() : stick;
                        break;

                    // Arrow keys / A,D step instantly
                    case Keyboard kb:
                        if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) dir = -1;
                        if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) dir = 1;
                        break;
                }
            }

            // Fall back to held stick with a repeat cooldown when no edge step happened
            if (dir == 0 && _stickCooldown <= 0f && Mathf.Abs(stick) >= 0.5f)
            {
                dir = stick < 0f ? -1 : 1;
                _stickCooldown = 0.22f;
            }

            return dir;
        }

        /** True when the joining device confirms (south button / start / Enter / Space). */
        private bool ReadConfirm()
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                switch (_devices[i])
                {
                    case Gamepad gp:
                        if (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame) return true;
                        break;
                    case Keyboard kb:
                        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                            || kb.spaceKey.wasPressedThisFrame) return true;
                        break;
                }
            }
            return false;
        }

        /** True when the joining device cancels (east button / Esc). */
        private bool ReadCancel()
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                switch (_devices[i])
                {
                    case Gamepad gp:
                        if (gp.buttonEast.wasPressedThisFrame) return true;
                        break;
                    case Keyboard kb:
                        if (kb.escapeKey.wasPressedThisFrame) return true;
                        break;
                }
            }
            return false;
        }

        /** Human-readable summary of the joining devices for the subtitle. */
        private string DescribeDevices()
        {
            if (_devices.Count == 0) return "New controller";
            for (int i = 0; i < _devices.Count; i++)
                if (_devices[i] is Gamepad) return _devices[i].displayName;
            return "Keyboard";
        }

        // -------------------------------------------------------
        // UI construction (code-built so the component is drop-in)
        // -------------------------------------------------------

        /** Builds the canvas hierarchy once, lazily, on first Open. */
        private void EnsureBuilt()
        {
            if (_canvasRoot != null) return;
            EnsureEventSystem();

            // Root canvas — drawn on top of everything
            _canvasRoot = new GameObject("PlayerJoinOverlay_Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasRoot.transform.SetParent(transform, false);
            var canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = _canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Full-screen dimmed backdrop
            var backdrop = CreateRect("Backdrop", _canvasRoot.transform);
            Stretch(backdrop);
            var backdropImg = backdrop.gameObject.AddComponent<Image>();
            backdropImg.color = backdropColor;

            // Centred vertical column holding title, subtitle, slot row, hint
            var column = CreateRect("Column", backdrop);
            column.anchorMin = column.anchorMax = new Vector2(0.5f, 0.5f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(1200, 420);
            var vlg = column.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = 24;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Title
            CreateText("Title", column, "New controller detected", titleFontSize, titleColor, FontStyles.Bold);

            // Subtitle (device + instruction) — kept as a field so we can update it per-open
            _subtitle = CreateText("Subtitle", column, "", slotFontSize,
                new Color(0.7f, 0.8f, 0.9f), FontStyles.Normal);

            // Horizontal row of slot buttons
            var row = CreateRect("SlotRow", column);
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 24;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().minHeight = 120;
            _slotRow = row;

            // Hint line
            CreateText("Hint", column, "◀ ▶  select     (A)/Enter  confirm     (B)/Esc  cancel",
                22, new Color(0.55f, 0.62f, 0.7f), FontStyles.Italic);

            _canvasRoot.SetActive(false);
        }

        /** Rebuilds the slot buttons to match the current free-slot list. */
        private void BuildSlotButtons()
        {
            // Clear any buttons from a previous open
            for (int i = _slotRow.childCount - 1; i >= 0; i--)
                Destroy(_slotRow.GetChild(i).gameObject);
            _slotImages.Clear();

            // One button per free slot
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];

                var btnRect = CreateRect($"Slot_{slot.Index}", _slotRow);
                btnRect.sizeDelta = new Vector2(260, 120);
                var le = btnRect.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 260;
                le.preferredHeight = 120;

                var img = btnRect.gameObject.AddComponent<Image>();
                img.color = slotNormalColor;
                _slotImages.Add(img);

                // Click commits this slot (mouse path); capture the list index locally
                var button = btnRect.gameObject.AddComponent<Button>();
                int listIndex = i;
                button.onClick.AddListener(() => Confirm(listIndex));

                // Colour accent strip using the slot's player colour
                var accent = CreateRect("Accent", btnRect);
                accent.anchorMin = new Vector2(0f, 0f);
                accent.anchorMax = new Vector2(1f, 0f);
                accent.pivot = new Vector2(0.5f, 0f);
                accent.sizeDelta = new Vector2(0, 10);
                accent.gameObject.AddComponent<Image>().color = slot.Color;

                // Label
                CreateText("Label", btnRect, slot.Label, slotFontSize, Color.white, FontStyles.Bold);
            }
        }

        /** Creates an EventSystem with the Input System UI module if the scene lacks one (for mouse clicks). */
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform, false);
        }

        // -------------------------------------------------------
        // Small uGUI builders
        // -------------------------------------------------------

        /** Creates a child GameObject with a RectTransform. */
        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /** Stretches a RectTransform to fill its parent. */
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /** Creates a centred TextMeshPro label as a child. */
        private static TextMeshProUGUI CreateText(string name, Transform parent, string text,
            int fontSize, Color color, FontStyles style)
        {
            var rect = CreateRect(name, parent);
            var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
