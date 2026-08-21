using System;
using PlayModeChangesSaver.Editor.ChangesTracker;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PlayModeChangesSaver.Editor.OverrideComparePopup
{
    /// <summary>
    ///     Popup window for comparing original and current component/transform states side-by-side.
    ///     Delegates functionality to specialized helper classes.
    /// </summary>
    internal class OcpContent : PopupWindowContent
    {
        // Layout constants
        private const float MinWidth = 350f;
        private const float HeaderHeight = 24f;
        private const float BaseFooterHeight = 40f;
        private const float MaxWindowHeight = 400f;
        private const float MinWindowHeight = 250f;
        private readonly Component _liveComponent;
        private readonly Action _onRefreshRequest;
        private readonly bool _openedFromBrowser;
        private float _footerHeight = BaseFooterHeight;
        private OcpInteraction _interactionHelper;
        private UnityEditor.Editor _leftEditor;
        private float _leftMaxScroll;
        private UnityEditor.Editor _rightEditor;
        private float _rightMaxScroll;

        // Scroll state
        private float _scrollNormalized;

        private OcpSnapshot _snapshotHelper;

        private float _targetWindowHeight = -1f;

        public OcpContent(Component component, bool openedFromBrowser = false, Action onRefreshRequest = null)
        {
            _liveComponent = component;
            _openedFromBrowser = openedFromBrowser;
            _onRefreshRequest = onRefreshRequest;
            InitializePopup();
        }

        private void InitializePopup()
        {
            _snapshotHelper = new OcpSnapshot(_liveComponent);
            _interactionHelper = new OcpInteraction(
                _liveComponent,
                _snapshotHelper.SnapshotComponent
            );

            CreateEditors();

            var showMaterialToggle = ShouldShowMaterialToggle();
            if (showMaterialToggle)
            {
                _footerHeight += 18f;
            }
        }

        private void CreateEditors()
        {
            if (_snapshotHelper.SnapshotComponent)
            {
                _leftEditor = UnityEditor.Editor.CreateEditor(_snapshotHelper.SnapshotComponent);
                _rightEditor = UnityEditor.Editor.CreateEditor(_liveComponent);
            }
        }

        public override Vector2 GetWindowSize()
        {
            var h = _targetWindowHeight < 0 ? MinWindowHeight : _targetWindowHeight;
            return new Vector2(MinWidth * 2 + 6, h);
        }

        public override void OnGUI(Rect rect)
        {
            if (!_leftEditor || !_rightEditor)
            {
                return;
            }

            _interactionHelper.HandleDragAndDrop(rect, editorWindow);

            // Dynamic size adjustment
            var extraSpaceNeeded = Mathf.Max(_leftMaxScroll, _rightMaxScroll);

            if (Event.current.type == EventType.Layout)
            {
                var desiredHeight = Mathf.Clamp(rect.height + extraSpaceNeeded, MinWindowHeight, MaxWindowHeight);

                if (Mathf.Abs(_targetWindowHeight - desiredHeight) > 1f)
                {
                    _targetWindowHeight = desiredHeight;
                    editorWindow.ShowAsDropDown(new Rect(editorWindow.position.position, Vector2.zero),
                        GetWindowSize());
                }
            }

            // Scroll handling
            var needsScrolling = rect.height >= MaxWindowHeight - 1f && extraSpaceNeeded > 0.5f;
            HandleMouseWheel(rect, needsScrolling);

            // Layout setup
            var scrollbarWidth = needsScrolling ? 15f : 0f;
            var columnWidth = (rect.width - scrollbarWidth - 6) * 0.5f;
            var contentHeight = rect.height - _footerHeight - HeaderHeight;

            OcpUI.DrawColumnHeader(new Rect(rect.x, rect.y, columnWidth, HeaderHeight), _leftEditor.target, "Original");
            OcpUI.DrawColumnHeader(new Rect(rect.x + columnWidth + 6, rect.y, columnWidth, HeaderHeight),
                _rightEditor.target, "Play Mode");

            Rect contentRect = new(rect.x, rect.y + HeaderHeight, rect.width, contentHeight);
            GUILayout.BeginArea(contentRect);
            GUILayout.BeginHorizontal();

            var leftContext =
                new OcpUI.ColumnRenderContext(columnWidth, contentHeight, _scrollNormalized, _leftMaxScroll);
            OcpUI.DrawSynchronizedColumn(_leftEditor, ref leftContext, false);
            _leftMaxScroll = leftContext.MaxScroll;

            OcpUI.DrawSeparator(new Rect(columnWidth, 0, 2, contentHeight));

            var rightContext =
                new OcpUI.ColumnRenderContext(columnWidth, contentHeight, _scrollNormalized, _rightMaxScroll);
            OcpUI.DrawSynchronizedColumn(_rightEditor, ref rightContext, true);
            _rightMaxScroll = rightContext.MaxScroll;

            if (needsScrolling)
            {
                Rect scrollbarRect = new(rect.width - 15, 0, 15, contentHeight);
                _scrollNormalized = GUI.VerticalScrollbar(scrollbarRect, _scrollNormalized, 0.1f, 0f, 1.0f);
            }
            else
            {
                _scrollNormalized = 0f;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            DrawFooter(new Rect(rect.x, rect.y + rect.height - _footerHeight, rect.width, _footerHeight));
        }

        private void HandleMouseWheel(Rect rect, bool needsScrolling)
        {
            var isScrollWheelOverRect = needsScrolling && rect.Contains(Event.current.mousePosition) &&
                                        Event.current.type == EventType.ScrollWheel;
            if (isScrollWheelOverRect)
            {
                _scrollNormalized = Mathf.Clamp01(_scrollNormalized + Event.current.delta.y * 0.05f);
                Event.current.Use();
            }
        }

        private void DrawFooter(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();

            var hasUnsavedChanges = _interactionHelper.HasUnsavedChanges();
            var hasSavedEntry = _interactionHelper.HasSavedEntry();
            var showMaterialToggle = ShouldShowMaterialToggle();

            DrawFooterLeftPanel(rect, hasUnsavedChanges, showMaterialToggle);
            GUILayout.FlexibleSpace();
            DrawFooterActionButtons(hasUnsavedChanges, hasSavedEntry);

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawFooterLeftPanel(Rect rect, bool hasUnsavedChanges, bool showMaterialToggle)
        {
            GUILayout.BeginVertical();
            OcpUI.DrawFooter(rect, hasUnsavedChanges);
            GUILayout.Space(2);
            DrawMaterialToggle(showMaterialToggle);
            GUILayout.EndVertical();
        }

        private void DrawFooterActionButtons(bool hasUnsavedChanges, bool hasSavedEntry)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();

            DrawRevertToOriginalButton();
            GUILayout.Space(4);
            DrawRevertToSavedButton(hasSavedEntry, hasUnsavedChanges);
            GUILayout.Space(8);
            DrawApplyButton(hasUnsavedChanges);

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawMaterialToggle(bool showMaterialToggle)
        {
            if (showMaterialToggle)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Toggle(true, "Persist Material changes", GUILayout.Width(180f));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawRevertToOriginalButton()
        {
            DrawButtonWithAction("Revert to Original", 130f, false,
                () => _interactionHelper.RevertToOriginal(_openedFromBrowser));
        }

        private void DrawRevertToSavedButton(bool hasSavedEntry, bool hasUnsavedChanges)
        {
            DrawButtonWithAction("Revert to Saved", 130f, !hasSavedEntry || !hasUnsavedChanges,
                () => _interactionHelper.RevertToSaved(_openedFromBrowser));
        }

        private void DrawApplyButton(bool hasUnsavedChanges)
        {
            DrawButtonWithAction("Apply", 120f, !hasUnsavedChanges,
                () => _interactionHelper.ApplyChanges(_openedFromBrowser));
        }

        private void DrawButtonWithAction(string label, float width, bool isDisabled, Action action)
        {
            EditorGUI.BeginDisabledGroup(isDisabled);
            if (GUILayout.Button(label, GUILayout.Width(width), GUILayout.Height(28f)))
            {
                action?.Invoke();
                _onRefreshRequest?.Invoke();
                editorWindow.Close();
            }

            EditorGUI.EndDisabledGroup();
        }

        public override void OnClose()
        {
            if (_leftEditor)
            {
                Object.DestroyImmediate(_leftEditor);
            }

            if (_rightEditor)
            {
                Object.DestroyImmediate(_rightEditor);
            }

            _snapshotHelper?.Cleanup();
        }

        private bool ShouldShowMaterialToggle()
        {
            if (_liveComponent is Renderer renderer)
            {
                return ChangesTrackerCore.HasMaterialDelta(renderer);
            }

            return false;
        }
    }
}