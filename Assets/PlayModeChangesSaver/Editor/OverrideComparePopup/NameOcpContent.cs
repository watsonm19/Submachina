using PlayModeChangesSaver.Editor.ChangesTracker;
using PlayModeChangesSaver.Editor.ChangesTracker.SnapShotHelper;
using UnityEditor;
using UnityEngine;

namespace PlayModeChangesSaver.Editor.OverrideComparePopup
{
    /// <summary>
    ///     Lightweight compare popup for GameObject name changes.
    ///     Shows original/saved/current names and allows revert/apply actions.
    /// </summary>
    internal class NameOcpContent : PopupWindowContent
    {
        private readonly GameObject _go;
        private string _currentName;

        private bool _hasOriginal;
        private bool _hasSaved;

        private string _originalName;
        private string _savedName;

        public NameOcpContent(GameObject gameObject)
        {
            _go = gameObject;
            LoadState();
        }

        private void LoadState()
        {
            _currentName = _go.name;

            var scenePath = ChangesTrackerCore.GetNormalizedScenePath(_go);
            var objectPath = SceneAndPathUtilities.GetGameObjectPath(_go.transform);
            var guid = GlobalObjectId.GetGlobalObjectIdSlow(_go).ToString();

            var nameOriginalStore = NameOriginalStore.LoadExisting();
            var originalEntry = nameOriginalStore?.entries.Find(e =>
                (!string.IsNullOrEmpty(e.globalObjectId) && !string.IsNullOrEmpty(guid) && e.globalObjectId == guid) ||
                (e.scenePath == scenePath && e.objectPath == objectPath));

            _originalName = originalEntry?.originalName ?? NameSH.GetNameSnapshot(_go)?.objectName;
            _hasOriginal = !string.IsNullOrEmpty(_originalName);

            var nameStore = NameChangesStore.LoadExisting();
            var savedEntry = nameStore?.changes.Find(c =>
                (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(guid) && c.globalObjectId == guid) ||
                (c.scenePath == scenePath && c.objectPath == objectPath));

            _savedName = savedEntry?.newName;
            _hasSaved = !string.IsNullOrEmpty(_savedName);
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(340f, 170f);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Name Comparison", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            DrawRow("Original", _hasOriginal ? _originalName : "(unbekannt)");
            DrawRow("Saved", _hasSaved ? _savedName : "(keine Speicherung)");
            DrawRow("Play Mode", _currentName);

            GUILayout.FlexibleSpace();
            DrawButtons();
        }

        private static void DrawRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(80f));
            EditorGUILayout.LabelField(value ?? string.Empty);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawButtons()
        {
            EditorGUILayout.BeginHorizontal();

            var currentEqualsSaved = _hasSaved && _currentName == _savedName;

            using (new EditorGUI.DisabledScope(!_hasOriginal))
            {
                if (GUILayout.Button("Revert to Original"))
                {
                    RevertToOriginal();
                }
            }

            using (new EditorGUI.DisabledScope(!_hasSaved || currentEqualsSaved))
            {
                if (GUILayout.Button("Revert to Saved"))
                {
                    RevertToSaved();
                }
            }

            using (new EditorGUI.DisabledScope(currentEqualsSaved))
            {
                if (GUILayout.Button("Apply Current"))
                {
                    ApplyCurrent();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void RevertToOriginal()
        {
            if (!_hasOriginal)
            {
                return;
            }

            Undo.RecordObject(_go, "Revert Name to Original");
            _go.name = _originalName;
            RemoveNameChangeEntry(_go);
            NameSH.SetNameSnapshot(_go, new NameSnapshot(_go));
            RefreshBrowserIfOpen();
            editorWindow.Close();
        }

        private void RevertToSaved()
        {
            if (!_hasSaved)
            {
                return;
            }

            Undo.RecordObject(_go, "Revert Name to Saved");
            _go.name = _savedName;
            NameSH.SetNameSnapshot(_go, new NameSnapshot(_go));
            RefreshBrowserIfOpen();
            editorWindow.Close();
        }

        private void ApplyCurrent()
        {
            ChangesTrackerCore.AcceptNameChanges(_go);
            RefreshBrowserIfOpen();
            editorWindow.Close();
        }

        private static void RefreshBrowserIfOpen()
        {
            if (EditorWindow.HasOpenInstances<OverridesBrowserWindow>())
            {
                OverridesBrowserWindow.Open();
            }
        }

        private static void RemoveNameChangeEntry(GameObject go)
        {
            var nameStore = NameChangesStore.LoadExisting();
            if (!nameStore)
            {
                return;
            }

            var scenePath = ChangesTrackerCore.GetNormalizedScenePath(go);
            var objectPath = SceneAndPathUtilities.GetGameObjectPath(go.transform);
            var guid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();

            for (var i = nameStore.changes.Count - 1; i >= 0; i--)
            {
                var c = nameStore.changes[i];
                if ((!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(guid) && c.globalObjectId == guid) ||
                    (c.scenePath == scenePath && c.objectPath == objectPath))
                {
                    nameStore.changes.RemoveAt(i);
                }
            }

            EditorUtility.SetDirty(nameStore);
            AssetDatabase.SaveAssets();
        }
    }
}