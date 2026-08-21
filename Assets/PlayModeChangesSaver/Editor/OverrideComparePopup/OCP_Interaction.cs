using System;
using System.Collections.Generic;
using System.Globalization;
using PlayModeChangesSaver.Editor.ChangesTracker;
using PlayModeChangesSaver.Editor.ChangesTracker.App;
using PlayModeChangesSaver.Editor.ChangesTracker.Serialization;
using PlayModeChangesSaver.Editor.ChangesTracker.SnapShotHelper;
using UnityEditor;
using UnityEngine;

namespace PlayModeChangesSaver.Editor.OverrideComparePopup
{
    /// <summary>
    ///     Handles user interactions: drag-drop, apply, revert buttons.
    /// </summary>
    internal class OcpInteraction
    {
        private const float DragHeaderHeight = 20f;

        private readonly Component _liveComponent;
        private readonly Component _snapshotComponent;
        private Vector2 _dragLastMousePos = Vector2.zero;
        private bool _isDragging;

        public OcpInteraction(Component liveComponent, Component snapshotComponent)
        {
            _liveComponent = liveComponent;
            _snapshotComponent = snapshotComponent;
        }

        /// <summary>
        ///     Handles drag-and-drop functionality for moving the popup window.
        /// </summary>
        public void HandleDragAndDrop(Rect rect, EditorWindow editorWindow)
        {
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            Rect dragHeaderRect = new(rect.x, rect.y, rect.width, DragHeaderHeight);

            if (Event.current.type == EventType.MouseDown && dragHeaderRect.Contains(Event.current.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                _isDragging = true;
                _dragLastMousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDrag && _isDragging && GUIUtility.hotControl == controlId)
            {
                var currentScreenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                var delta = currentScreenPos - _dragLastMousePos;

                var newRect = editorWindow.position;
                newRect.position += delta;
                editorWindow.position = newRect;

                _dragLastMousePos = currentScreenPos;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                _isDragging = false;
                GUIUtility.hotControl = 0;
                Event.current.Use();
            }

            if (Event.current.type == EventType.Repaint)
            {
                GUI.Box(dragHeaderRect, GUIContent.none, EditorStyles.toolbar);
            }
        }

        /// <summary>
        ///     Reverts all changes made in Play Mode back to the original snapshot state.
        /// </summary>
        public void RevertToOriginal(bool openedFromBrowser = false)
        {
            if (!_snapshotComponent)
            {
                return;
            }

            var goid = GlobalObjectId.GetGlobalObjectIdSlow(_liveComponent.gameObject).ToString();
            var scenePath = GetScenePathForGo(_liveComponent.gameObject);
            var objectPath = OcpUtilities.GetGameObjectPath(_liveComponent.gameObject.transform);

            if (_liveComponent is Transform or RectTransform)
            {
                RevertTransformToOriginal(_liveComponent.gameObject, scenePath, objectPath, goid);
            }
            else
            {
                SerializedObject sourceSo = new(_snapshotComponent);
                SerializedObject targetSo = new(_liveComponent);

                var sourceProp = sourceSo.GetIterator();
                var enterChildren = true;

                while (sourceProp.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (sourceProp.name == "m_Script")
                    {
                        continue;
                    }

                    var targetProp = targetSo.FindProperty(sourceProp.propertyPath);
                    if (targetProp != null && targetProp.propertyType == sourceProp.propertyType)
                    {
                        targetSo.CopyFromSerializedProperty(sourceProp);
                    }
                }

                targetSo.ApplyModifiedProperties();

                RestoreSnapshotState(goid);
            }

            ResetBaselinesIfPlaying();
            RemoveFromStore();

            if (openedFromBrowser)
            {
                RefreshBrowserIfOpen();
            }
        }

        private void RestoreSnapshotState(string goid)
        {
            if (_liveComponent is Transform or RectTransform)
            {
                RestoreNameSnapshot();
            }
            else if (_liveComponent is Renderer renderer)
            {
                RestoreRendererMaterials(renderer, goid);
            }
        }

        private void RestoreNameSnapshot()
        {
            var nameSnapshot = NameSH.GetNameSnapshot(_liveComponent.gameObject);
            if (nameSnapshot != null && !string.IsNullOrEmpty(nameSnapshot.objectName))
            {
                _liveComponent.gameObject.name = nameSnapshot.objectName;
            }
        }

        private void RestoreRendererMaterials(Renderer renderer, string goid)
        {
            if (!renderer)
            {
                return;
            }

            var snapshotRenderer = _snapshotComponent as Renderer;
            var materialGuids = ResolveOriginalMaterialGuids(goid, snapshotRenderer);

            Debug.Log(
                $"[OCP] RestoreRendererMaterials | goid={goid} | comp={renderer.GetType().Name} | guidCount={materialGuids.Count}");

            if (materialGuids is { Count: > 0 })
            {
                ApplyMaterials(renderer, materialGuids);
            }
        }

        private List<string> ResolveOriginalMaterialGuids(string goid, Renderer snapshotRenderer)
        {
            // Prefer the snapshot renderer data (already built from original store).
            var fromSnapshot = MaterialChangeHandler.GetRendererMaterialGuids(snapshotRenderer);
            if (fromSnapshot is { Count: > 0 })
            {
                return fromSnapshot;
            }

            // Fallback: query the original store by GOID (not path) to honor identity even if path changed.
            var store = CompOriginalStore.LoadExisting();
            if (!store)
            {
                return new List<string>();
            }

            var type = _liveComponent.GetType();
            var allOfType = _liveComponent.gameObject.GetComponents(type);
            var compIndex = Array.IndexOf(allOfType, _liveComponent);
            var compType = type.AssemblyQualifiedName;

            var entry = store.entries.Find(e =>
                !string.IsNullOrEmpty(e.globalObjectId) &&
                e.globalObjectId == goid &&
                e.componentType == compType &&
                e.componentIndex == compIndex);

            if (entry != null && entry.materialGuids is { Count: > 0 })
            {
                return new List<string>(entry.materialGuids);
            }

            return new List<string>();
        }

        private void ResetBaselinesIfPlaying()
        {
            if (!Application.isPlaying || !_liveComponent)
            {
                return;
            }

            if (_liveComponent is Transform or RectTransform)
            {
                ChangesTrackerCore.ResetTransformBaseline(_liveComponent.gameObject);
            }
            else
            {
                ChangesTrackerCore.ResetComponentBaseline(_liveComponent);
            }
        }

        /// <summary>
        ///     Reverts all changes made in Play Mode back to the saved store values.
        /// </summary>
        public void RevertToSaved(bool openedFromBrowser = false)
        {
            if (!_liveComponent)
            {
                return;
            }

            var go = _liveComponent.gameObject;
            var scenePath = GetScenePathForGo(go);
            var objectPath = OcpUtilities.GetGameObjectPath(go.transform);
            var goid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();

            if (_liveComponent is Transform or RectTransform)
            {
                RevertTransformToSaved(go, scenePath, objectPath, goid);
            }
            else
            {
                RevertComponentToSaved(go, scenePath, objectPath, goid);
            }

            if (openedFromBrowser)
            {
                RefreshBrowserIfOpen();
            }
        }

        private void RevertTransformToSaved(GameObject go, string scenePath, string objectPath, string goid)
        {
            var tStore = ChangesStore.LoadExisting();
            if (!tStore)
            {
                return;
            }

            var index = tStore.changes.FindIndex(c =>
                (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(goid) && c.globalObjectId == goid) ||
                (c.scenePath == scenePath && c.objectPath == objectPath));
            if (index < 0)
            {
                return;
            }

            var storedChange = tStore.changes[index];
            var t = go.transform;

            ApplyTransformValues(t, storedChange);
            ApplyRectTransformValues(t as RectTransform, storedChange);

            if (Application.isPlaying)
            {
                ChangesTrackerCore.ResetTransformBaseline(go);
            }
        }

        private void RevertTransformToOriginal(GameObject go, string scenePath, string objectPath, string goid)
        {
            var transform = go.transform;
            var originalStore = OriginalStore.LoadExisting();
            var originalEntry = originalStore?.entries.Find(e =>
                (!string.IsNullOrEmpty(e.globalObjectId) && !string.IsNullOrEmpty(goid) && e.globalObjectId == goid) ||
                (e.scenePath == scenePath && e.objectPath == objectPath));

            Snapshot snapshot;
            if (originalEntry != null)
            {
                snapshot = new Snapshot
                {
                    position = originalEntry.position,
                    rotation = originalEntry.rotation,
                    scale = originalEntry.scale,
                    isRectTransform = originalEntry.isRectTransform,
                    anchoredPosition = originalEntry.anchoredPosition,
                    anchoredPosition3D = originalEntry.anchoredPosition3D,
                    anchorMin = originalEntry.anchorMin,
                    anchorMax = originalEntry.anchorMax,
                    pivot = originalEntry.pivot,
                    sizeDelta = originalEntry.sizeDelta,
                    offsetMin = originalEntry.offsetMin,
                    offsetMax = originalEntry.offsetMax
                };
            }
            else
            {
                snapshot = ChangesTrackerCore.GetSnapshot(go);
            }

            if (snapshot == null)
            {
                return;
            }

            Undo.RecordObject(transform, "Revert Transform to Original");
            Undo.RecordObject(go, "Revert Transform to Original");

            transform.SetLocalPositionAndRotation(snapshot.position, snapshot.rotation);
            transform.localScale = snapshot.scale;

            if (snapshot.isRectTransform && transform is RectTransform rt)
            {
                rt.anchoredPosition = snapshot.anchoredPosition;
                rt.anchoredPosition3D = snapshot.anchoredPosition3D;
                rt.anchorMin = snapshot.anchorMin;
                rt.anchorMax = snapshot.anchorMax;
                rt.pivot = snapshot.pivot;
                rt.sizeDelta = snapshot.sizeDelta;
                rt.offsetMin = snapshot.offsetMin;
                rt.offsetMax = snapshot.offsetMax;
            }

            EditorUtility.SetDirty(transform);
            EditorUtility.SetDirty(go);

            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            }

            if (Application.isPlaying)
            {
                ChangesTrackerCore.ResetTransformBaseline(go);
            }
        }

        private void ApplyTransformValues(Transform t, ChangesStore.TransformChange storedChange)
        {
            t.SetLocalPositionAndRotation(storedChange.position, storedChange.rotation);
            t.localScale = storedChange.scale;
        }

        private void ApplyRectTransformValues(RectTransform rt, ChangesStore.TransformChange storedChange)
        {
            if (storedChange.isRectTransform && rt != null)
            {
                rt.anchoredPosition = storedChange.anchoredPosition;
                rt.anchoredPosition3D = storedChange.anchoredPosition3D;
                rt.anchorMin = storedChange.anchorMin;
                rt.anchorMax = storedChange.anchorMax;
                rt.pivot = storedChange.pivot;
                rt.sizeDelta = storedChange.sizeDelta;
                rt.offsetMin = storedChange.offsetMin;
                rt.offsetMax = storedChange.offsetMax;
            }
        }

        private void RevertComponentToSaved(GameObject go, string scenePath, string objectPath, string goid)
        {
            var cStore = CompChangesStore.LoadExisting();
            if (!cStore)
            {
                return;
            }

            var index = FindComponentChangeIndex(go, scenePath, objectPath, goid, cStore);
            if (index < 0)
            {
                return;
            }

            var storedChange = cStore.changes[index];
            ApplyStoredComponentProperties(storedChange);
            ApplyStoredMaterialChanges(storedChange);
            ResetComponentBaselineIfPlaying();
        }

        private int FindComponentChangeIndex(GameObject go, string scenePath, string objectPath,
            string goid, CompChangesStore cStore)
        {
            return FindComponentIndexInStore(go, scenePath, objectPath, goid, cStore);
        }

        private int FindComponentIndexInStore(GameObject go, string scenePath, string objectPath,
            string goid, CompChangesStore cStore)
        {
            var type = _liveComponent.GetType();
            var componentType = type.AssemblyQualifiedName;
            var allOfType = go.GetComponents(type);
            var compIndex = Array.IndexOf(allOfType, _liveComponent);

            return cStore.changes.FindIndex(c =>
                ((!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(goid) && c.globalObjectId == goid) ||
                 (c.scenePath == scenePath && c.objectPath == objectPath)) &&
                c.componentType == componentType &&
                c.componentIndex == compIndex);
        }

        private void ApplyStoredComponentProperties(CompChangesStore.ComponentChange storedChange)
        {
            var targetSo = new SerializedObject(_liveComponent);

            for (var i = 0; i < storedChange.propertyPaths.Count; i++)
            {
                var propPath = storedChange.propertyPaths[i];
                var prop = targetSo.FindProperty(propPath);
                if (prop != null)
                {
                    OcpSerialization.ApplySerializedComponentValue(prop, storedChange.valueTypes[i],
                        storedChange.serializedValues[i]);
                }
            }

            targetSo.ApplyModifiedProperties();
        }

        private void ApplyStoredMaterialChanges(CompChangesStore.ComponentChange storedChange)
        {
            if (storedChange.includeMaterialChanges && _liveComponent is Renderer renderer)
            {
                ApplyMaterials(renderer, storedChange.materialGuids);
            }
        }

        private void ResetComponentBaselineIfPlaying()
        {
            if (Application.isPlaying)
            {
                ChangesTrackerCore.ResetComponentBaseline(_liveComponent);
            }
        }

        /// <summary>
        ///     Reverts all changes made in Play Mode back to the snapshot state.
        /// </summary>
        [Obsolete("Use RevertToOriginal instead")]
        public void RevertChanges(bool openedFromBrowser = false)
        {
            RevertToOriginal(openedFromBrowser);
        }

        /// <summary>
        ///     Applies the current Play Mode changes to the acceptance system.
        /// </summary>
        public void ApplyChanges(bool openedFromBrowser = false)
        {
            if (_liveComponent is Transform or RectTransform)
            {
                ChangesTrackerCore.AcceptTransformChanges(_liveComponent.gameObject);
            }
            else
            {
                ChangesTrackerCore.AcceptComponentChanges(_liveComponent);
            }

            // Force editor update to reflect the changes
            EditorUtility.SetDirty(_liveComponent);

            if (openedFromBrowser)
            {
                RefreshBrowserIfOpen();
            }
        }

        /// <summary>
        ///     Checks if there are unsaved changes compared to the store.
        ///     Returns true if the current live component has changes that are NOT yet saved in the store,
        ///     or if the current values differ from the stored values.
        /// </summary>
        public bool HasUnsavedChanges()
        {
            return ExecuteComponentQuery(
                HasUnsavedTransformChanges,
                HasUnsavedComponentChanges,
                false);
        }

        private TResult ExecuteComponentQuery<TResult>(
            Func<GameObject, string, string, TResult> transformAction,
            Func<GameObject, string, string, TResult> componentAction,
            TResult defaultValue)
        {
            if (!_liveComponent)
            {
                return defaultValue;
            }

            var go = _liveComponent.gameObject;
            var scenePath = GetScenePathForGo(go);
            var objectPath = OcpUtilities.GetGameObjectPath(go.transform);

            if (_liveComponent is Transform or RectTransform)
            {
                return transformAction(go, scenePath, objectPath);
            }

            return componentAction(go, scenePath, objectPath);
        }

        private bool HasUnsavedTransformChanges(GameObject go, string scenePath, string objectPath)
        {
            var tStore = ChangesStore.LoadExisting();
            if (!tStore)
            {
                return true;
            }

            var goid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            var index = tStore.changes.FindIndex(c =>
                (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(goid) && c.globalObjectId == goid) ||
                (c.scenePath == scenePath && c.objectPath == objectPath));
            if (index < 0)
            {
                return true;
            }

            var storedChange = tStore.changes[index];
            var t = go.transform;
            var rt = t as RectTransform;

            return TransformValuesChanged(storedChange, t, rt);
        }

        private bool TransformValuesChanged(ChangesStore.TransformChange storedChange, Transform t, RectTransform rt)
        {
            if (RectTransformValuesChanged(storedChange, rt))
            {
                return true;
            }

            if (PositionChanged(storedChange, t))
            {
                return true;
            }

            if (RotationChanged(storedChange, t))
            {
                return true;
            }

            if (ScaleChanged(storedChange, t))
            {
                return true;
            }

            return false;
        }

        private bool RectTransformValuesChanged(ChangesStore.TransformChange storedChange, RectTransform rt)
        {
            if (!storedChange.isRectTransform || !rt)
            {
                return false;
            }

            return Vector2Changed(storedChange.anchoredPosition, rt.anchoredPosition) ||
                   Vector2Changed(storedChange.sizeDelta, rt.sizeDelta) ||
                   Vector2Changed(storedChange.anchorMin, rt.anchorMin) ||
                   Vector2Changed(storedChange.anchorMax, rt.anchorMax) ||
                   Vector2Changed(storedChange.pivot, rt.pivot);
        }

        private bool PositionChanged(ChangesStore.TransformChange storedChange, Transform t)
        {
            return Vector3Changed(storedChange.position, t.localPosition);
        }

        private bool RotationChanged(ChangesStore.TransformChange storedChange, Transform t)
        {
            return QuaternionChanged(storedChange.rotation, t.localRotation);
        }

        private bool ScaleChanged(ChangesStore.TransformChange storedChange, Transform t)
        {
            return Vector3Changed(storedChange.scale, t.localScale);
        }

        private bool Vector2Changed(Vector2 stored, Vector2 current)
        {
            return !Mathf.Approximately(stored.x, current.x) ||
                   !Mathf.Approximately(stored.y, current.y);
        }

        private bool Vector3Changed(Vector3 stored, Vector3 current)
        {
            return !Mathf.Approximately(stored.x, current.x) ||
                   !Mathf.Approximately(stored.y, current.y) ||
                   !Mathf.Approximately(stored.z, current.z);
        }

        private bool QuaternionChanged(Quaternion stored, Quaternion current)
        {
            return !Mathf.Approximately(stored.x, current.x) ||
                   !Mathf.Approximately(stored.y, current.y) ||
                   !Mathf.Approximately(stored.z, current.z) ||
                   !Mathf.Approximately(stored.w, current.w);
        }

        private bool HasUnsavedComponentChanges(GameObject go, string scenePath, string objectPath)
        {
            var cStore = CompChangesStore.LoadExisting();
            if (!cStore)
            {
                return true;
            }

            var goid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            var index = FindStoredComponentChangeIndex(go, scenePath, objectPath, goid, cStore);
            if (index < 0)
            {
                return true;
            }

            var storedChange = cStore.changes[index];
            return ComponentPropertiesDifferFromStored(storedChange);
        }

        private int FindStoredComponentChangeIndex(GameObject go, string scenePath, string objectPath,
            string goid, CompChangesStore cStore)
        {
            return FindComponentIndexInStore(go, scenePath, objectPath, goid, cStore);
        }

        private bool ComponentPropertiesDifferFromStored(CompChangesStore.ComponentChange storedChange)
        {
            var liveSo = new SerializedObject(_liveComponent);
            liveSo.Update();

            for (var i = 0; i < storedChange.propertyPaths.Count; i++)
            {
                var propPath = storedChange.propertyPaths[i];
                var liveProp = liveSo.FindProperty(propPath);
                if (liveProp == null)
                {
                    continue;
                }

                var valueType = i < storedChange.valueTypes.Count ? storedChange.valueTypes[i] : string.Empty;
                var serializedValue = i < storedChange.serializedValues.Count
                    ? storedChange.serializedValues[i]
                    : string.Empty;

                if (ValueDiffersFromStored(liveProp, valueType, serializedValue))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ValueDiffersFromStored(SerializedProperty liveProp, string storedType, string storedValue)
        {
            if (string.IsNullOrEmpty(storedType))
            {
                return false;
            }

            switch (storedType)
            {
                case "Integer":
                    if (int.TryParse(storedValue, out var intVal))
                    {
                        return liveProp.intValue != intVal;
                    }

                    break;
                case "Boolean":
                    if (bool.TryParse(storedValue, out var boolVal))
                    {
                        return liveProp.boolValue != boolVal;
                    }

                    break;
                case "Float":
                    if (float.TryParse(storedValue, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var floatVal))
                    {
                        return !Mathf.Approximately(liveProp.floatValue, floatVal);
                    }

                    break;
                case "String":
                    return liveProp.stringValue != storedValue;
                case "Color":
                    if (ColorUtility.TryParseHtmlString(storedValue, out var col))
                    {
                        return liveProp.colorValue != col;
                    }

                    break;
                case "Vector2":
                {
                    var storedVec2 = OcpSerialization.DeserializeVector2(storedValue);
                    return Vector2.Distance(liveProp.vector2Value, storedVec2) > 0.0001f;
                }
                case "Vector3":
                {
                    var storedVec3 = OcpSerialization.DeserializeVector3(storedValue);
                    return Vector3.Distance(liveProp.vector3Value, storedVec3) > 0.0001f;
                }
                case "Vector4":
                    return liveProp.vector4Value != OcpSerialization.DeserializeVector4(storedValue);
                case "Quaternion":
                {
                    var storedQuat = OcpSerialization.DeserializeQuaternion(storedValue);
                    return Quaternion.Angle(liveProp.quaternionValue, storedQuat) > 0.0001f;
                }
                case "Enum":
                    if (int.TryParse(storedValue, out var enumVal))
                    {
                        return liveProp.enumValueIndex != enumVal;
                    }

                    break;
                default:
                    ComponentPropertySerializer.SerializeProperty(liveProp, out _, out var currentSerialized);
                    return currentSerialized != storedValue;
            }

            return false;
        }

        /// <summary>
        ///     Returns true if a saved entry exists for the current live component in the stores.
        /// </summary>
        public bool HasSavedEntry()
        {
            return ExecuteComponentQuery(
                (go, scenePath, objectPath) => HasSavedTransformEntry(go, scenePath, objectPath),
                HasSavedComponentEntry,
                false);
        }

        private string GetScenePathForGo(GameObject go)
        {
            var scenePath = go.scene.path;
            if (string.IsNullOrEmpty(scenePath))
            {
                scenePath = go.scene.name;
            }

            return scenePath;
        }

        private bool HasSavedTransformEntry(GameObject go, string scenePath, string objectPath)
        {
            var tStore = ChangesStore.LoadExisting();
            if (!tStore)
            {
                return false;
            }

            var goid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            var index = tStore.changes.FindIndex(c =>
                (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(goid) && c.globalObjectId == goid) ||
                (c.scenePath == scenePath && c.objectPath == objectPath));
            return index >= 0;
        }

        private bool HasSavedComponentEntry(GameObject go, string scenePath, string objectPath)
        {
            var cStore = CompChangesStore.LoadExisting();
            if (!cStore)
            {
                return false;
            }

            var goid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            var index = FindComponentIndexInStore(go, scenePath, objectPath, goid, cStore);
            return index >= 0;
        }

        private void RemoveFromStore()
        {
            if (!_liveComponent)
            {
                return;
            }

            var go = _liveComponent.gameObject;
            var scenePath = GetScenePathForGo(go);
            var objectPath = OcpUtilities.GetGameObjectPath(go.transform);
            var goid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();

            if (_liveComponent is Transform or RectTransform)
            {
                RemoveTransformFromStore(go, scenePath, objectPath, goid);
                RemoveTransformOriginalFromStore(scenePath, objectPath, goid);
            }
            else
            {
                RemoveComponentFromStore(go, scenePath, objectPath);
            }
        }

        private void RemoveTransformFromStore(GameObject go, string scenePath, string objectPath, string goid)
        {
            var tStore = ChangesStore.LoadExisting();
            if (!tStore)
            {
                return;
            }

            var normalizedScenePath = SceneAndPathUtilities.NormalizeScenePath(scenePath);
            var removedAny = false;
            for (var i = tStore.changes.Count - 1; i >= 0; i--)
            {
                var change = tStore.changes[i];
                var changeScenePath = SceneAndPathUtilities.NormalizeScenePath(change.scenePath);
                var directGuidMatch = !string.IsNullOrEmpty(change.globalObjectId) &&
                                      !string.IsNullOrEmpty(goid) &&
                                      string.Equals(change.globalObjectId, goid, StringComparison.Ordinal);
                var directPathMatch = string.Equals(changeScenePath, normalizedScenePath,
                                          StringComparison.OrdinalIgnoreCase) &&
                                      string.Equals(change.objectPath, objectPath, StringComparison.Ordinal);
                var resolvedTargetMatch = IsSameTargetGameObject(go, change.scenePath, change.globalObjectId,
                    change.objectPath);

                var matches = directGuidMatch || directPathMatch || resolvedTargetMatch;
                if (!matches)
                {
                    continue;
                }

                tStore.changes.RemoveAt(i);
                removedAny = true;
            }

            if (removedAny)
            {
                EditorUtility.SetDirty(tStore);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private void RemoveTransformOriginalFromStore(string scenePath, string objectPath, string goid)
        {
            var originalStore = OriginalStore.LoadExisting();
            if (!originalStore)
            {
                return;
            }

            var removedAny = false;
            for (var i = originalStore.entries.Count - 1; i >= 0; i--)
            {
                var entry = originalStore.entries[i];
                var matches = (!string.IsNullOrEmpty(entry.globalObjectId) && !string.IsNullOrEmpty(goid) &&
                               entry.globalObjectId == goid) ||
                              (entry.scenePath == scenePath &&
                               entry.objectPath == objectPath);
                if (!matches)
                {
                    continue;
                }

                originalStore.entries.RemoveAt(i);
                removedAny = true;
            }

            if (removedAny)
            {
                EditorUtility.SetDirty(originalStore);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static bool IsSameTargetGameObject(GameObject target, string scenePath, string globalObjectId,
            string objectPath)
        {
            if (!target)
            {
                return false;
            }

            var scene = SceneAndPathUtilities.GetSceneByPathOrName(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            var resolved = SceneAndPathUtilities.FindGameObjectByGuidOrPath(scene, globalObjectId, objectPath);
            return resolved == target;
        }

        private void RemoveComponentFromStore(GameObject go, string scenePath, string objectPath)
        {
            var cStore = CompChangesStore.LoadExisting();
            if (!cStore)
            {
                return;
            }

            var removedAny = false;
            var type = _liveComponent.GetType();
            var componentType = type.AssemblyQualifiedName;
            var allOfType = go.GetComponents(type);
            var compIndex = Array.IndexOf(allOfType, _liveComponent);

            for (var i = cStore.changes.Count - 1; i >= 0; i--)
            {
                var change = cStore.changes[i];
                var matchesComponent = change.componentType == componentType &&
                                       change.componentIndex == compIndex &&
                                       change.scenePath == scenePath &&
                                       change.objectPath == objectPath;

                if (!matchesComponent)
                {
                    continue;
                }

                cStore.changes.RemoveAt(i);
                removedAny = true;
            }

            if (removedAny)
            {
                EditorUtility.SetDirty(cStore);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static void RefreshBrowserIfOpen()
        {
            OverridesBrowserWindow.RefreshOpenInstances();
        }

        private static void ApplyMaterials(Renderer renderer, List<string> materialGuids)
        {
            if (!IsValidForMaterialApply(renderer, materialGuids))
            {
                return;
            }

            var current = renderer.sharedMaterials;
            var applied = new Material[materialGuids.Count];

            for (var i = 0; i < materialGuids.Count; i++)
            {
                applied[i] = ResolveMaterialAtIndex(i, materialGuids[i], current);
            }

            renderer.sharedMaterials = applied;
        }

        private static bool IsValidForMaterialApply(Renderer renderer, List<string> materialGuids)
        {
            if (!renderer)
            {
                return false;
            }

            if (materialGuids == null)
            {
                return false;
            }

            if (materialGuids.Count == 0)
            {
                return false;
            }

            return true;
        }

        private static Material ResolveMaterialAtIndex(int index, string guid, Material[] current)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (!mat && index < current.Length)
            {
                return current[index];
            }

            return mat;
        }
    }
}