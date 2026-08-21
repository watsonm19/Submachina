using System;
using System.Collections.Generic;
using System.Linq;
using PlayModeChangesSaver.Editor.ChangesTracker;
using PlayModeChangesSaver.Editor.ChangesTracker.SnapShotHelper;
using PlayModeChangesSaver.Editor.OverrideComparePopup;
using UnityEditor;
using UnityEngine;

namespace PlayModeChangesSaver.Editor
{
    internal class OverridesPopUp : PopupWindowContent
    {
        private const float RowHeight = 22f;
        private const float FooterHeight = 50f;
        private readonly List<Component> _changedComponents;
        private readonly bool _hasNameDelta;
        private readonly float _headerHeight = 28f;

        private readonly GameObject _targetGo;
        private Vector2 _scroll;

        public OverridesPopUp(GameObject go)
        {
            _targetGo = go;
            _changedComponents = ChangesTrackerCore.GetChangedComponents(go);

            _hasNameDelta = ChangesTrackerCore.HasNameDelta(go);
            var showMaterialToggle =
                _changedComponents.Any(c => c is Renderer r && ChangesTrackerCore.HasMaterialDelta(r));

            if (showMaterialToggle)
            {
                _headerHeight += 18f;
            }
        }

        public override Vector2 GetWindowSize()
        {
            var rowCount = _changedComponents.Count + (_hasNameDelta ? 1 : 0);
            var count = Mathf.Max(1, rowCount);
            var listHeight = count * RowHeight;
            var totalHeight = _headerHeight + listHeight + FooterHeight + 10;
            return new Vector2(320, Mathf.Min(500, totalHeight));
        }

        public override void OnGUI(Rect rect)
        {
            // Layout
            Rect headerRect = new(rect.x, rect.y, rect.width, _headerHeight);
            DrawHeader(headerRect);

            if (_changedComponents.Count == 0 && !_hasNameDelta)
            {
                Rect helpRect = new(rect.x + 10, rect.y + _headerHeight, rect.width - 20, 40);
                GUI.Label(helpRect, "No changed components", EditorStyles.helpBox);
                return;
            }

            // List
            var listHeight = rect.height - _headerHeight - FooterHeight;
            Rect listRect = new(rect.x, rect.y + _headerHeight, rect.width, listHeight);
            DrawComponentList(listRect);

            // Footer
            Rect footerRect = new(rect.x, rect.y + _headerHeight + listHeight, rect.width, FooterHeight);
            DrawFooter(footerRect);
        }

        private void DrawHeader(Rect rect)
        {
            EditorGUI.LabelField(
                new Rect(rect.x + 6, rect.y + 6, rect.width - 12, 20),
                "Play Mode Overrides",
                EditorStyles.boldLabel
            );
        }

        private void DrawComponentList(Rect rect)
        {
            var rowCount = _changedComponents.Count + (_hasNameDelta ? 1 : 0);
            Rect viewRect = new(0, 0, rect.width - 16, rowCount * RowHeight);
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);

            var y = 0f;

            if (_hasNameDelta)
            {
                Rect nameRow = new(0, y, viewRect.width, RowHeight);
                DrawNameRow(nameRow, _targetGo);
                y += RowHeight;
            }

            foreach (var t in _changedComponents)
            {
                Rect row = new(0, y, viewRect.width, RowHeight);
                DrawRow(row, t);
                y += RowHeight;
            }

            GUI.EndScrollView();
        }

        private static void DrawNameRow(Rect rowRect, GameObject go)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(rowRect, false, false, false, false);
            }

            var labelRect = new Rect(rowRect.x + 6, rowRect.y + 3, rowRect.width - 12, 16);

            if (GUI.Button(labelRect, "GameObject Name", EditorStyles.label))
            {
                PopupWindow.Show(rowRect, new NameOcpContent(go));
            }
        }

        private void DrawRow(Rect rowRect, Component component)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(rowRect, false, false, false, false);
            }

            var content = EditorGUIUtility.ObjectContent(component, component.GetType());
            Rect labelRect = new(rowRect.x + 6, rowRect.y + 3, rowRect.width - 12, 16);

            if (GUI.Button(labelRect, content, EditorStyles.label))
            {
                PopupWindow.Show(rowRect, new OcpContent(component, false, () => RefreshContent(_targetGo)));
            }
        }

        private void DrawFooter(Rect rect)
        {
            // Background
            if (Event.current.type == EventType.Repaint)
            {
                var bgColor = EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.22f, 0.22f, 0.8f)
                    : new Color(0.8f, 0.8f, 0.8f, 0.8f);
                EditorGUI.DrawRect(rect, bgColor);
            }

            // Buttons
            const float buttonWidth = 100f;
            const float buttonHeight = 28f;
            const float spacing = 6f;
            const float totalWidth = buttonWidth * 3 + spacing * 2;
            var startX = rect.x + (rect.width - totalWidth) / 2;
            var startY = rect.y + (rect.height - buttonHeight) / 2;

            Rect revertOriginalRect = new(startX, startY, buttonWidth, buttonHeight);
            Rect revertSavedRect = new(startX + buttonWidth + spacing, startY, buttonWidth, buttonHeight);
            Rect applyRect = new(startX + (buttonWidth + spacing) * 2, startY, buttonWidth, buttonHeight);

            var hasAnySaved = HasAnySavedEntries();

            if (GUI.Button(revertOriginalRect, "Revert to Original"))
            {
                RevertToOriginal();
                RefreshBrowserIfOpen();
                editorWindow.Close();
            }

            EditorGUI.BeginDisabledGroup(!hasAnySaved);
            if (GUI.Button(revertSavedRect, "Revert to Saved"))
            {
                RevertToSaved();
                RefreshBrowserIfOpen();
                editorWindow.Close();
            }

            EditorGUI.EndDisabledGroup();

            if (GUI.Button(applyRect, "Apply All"))
            {
                ApplyAllChanges();
                RefreshBrowserIfOpen();
                editorWindow.Close();
            }
        }

        private void RevertToOriginal()
        {
            RefreshContent(_targetGo);

            var context = BuildTargetContext();
            var stores = LoadStores();

            RevertTransformFromOriginal(context, stores);
            RevertNameFromOriginal(context);

            TransformSH.ResetTransformBaseline(_targetGo);
            NameSH.SetNameSnapshot(_targetGo, new NameSnapshot(_targetGo));
            RevertComponentsFromOriginal(context, stores);
            CleanupStoresAfterOriginalRevert(context, stores);
            AssetDatabase.SaveAssets();
        }

        private void RevertToSaved()
        {
            RefreshContent(_targetGo);

            var context = BuildTargetContext();
            var (tStore, cStore, _, _) = LoadStores();

            RevertTransformFromSaved(context, tStore);
            RevertNameFromSaved(context);

            TransformSH.ResetTransformBaseline(_targetGo);
            NameSH.SetNameSnapshot(_targetGo, new NameSnapshot(_targetGo));
            RevertComponentsFromSaved(context, cStore);

            AssetDatabase.SaveAssets();
        }

        private TargetContext BuildTargetContext()
        {
            var scenePath = _targetGo.scene.path;
            if (string.IsNullOrEmpty(scenePath))
            {
                scenePath = _targetGo.scene.name;
            }

            var objectPath = OcpUtilities.GetGameObjectPath(_targetGo.transform);
            var targetGuid = GlobalObjectId.GetGlobalObjectIdSlow(_targetGo).ToString();

            var transformListed = _changedComponents.Contains(_targetGo.transform);
            var nameOnly = !transformListed && _hasNameDelta;

            return new TargetContext
            {
                ScenePath = scenePath,
                ObjectPath = objectPath,
                Guid = targetGuid,
                TransformListed = transformListed,
                NameOnly = nameOnly
            };
        }

        private static (ChangesStore tStore, CompChangesStore cStore, OriginalStore tOriginal, CompOriginalStore
            cOriginal) LoadStores()
        {
            return (
                ChangesStore.LoadExisting(),
                CompChangesStore.LoadExisting(),
                OriginalStore.LoadExisting(),
                CompOriginalStore.LoadExisting()
            );
        }

        private void RevertTransformFromOriginal(TargetContext context,
            (ChangesStore tStore, CompChangesStore cStore, OriginalStore tOriginal, CompOriginalStore cOriginal) stores)
        {
            if (!context.TransformListed)
            {
                return;
            }

            var transform = _targetGo.transform;
            var originalTransform = stores.tOriginal?.entries.Find(e =>
                (!string.IsNullOrEmpty(e.globalObjectId) && !string.IsNullOrEmpty(context.Guid) && e.globalObjectId == context.Guid) ||
                (e.scenePath == context.ScenePath &&
                 e.objectPath == context.ObjectPath));

            if (originalTransform != null)
            {
                ApplyTransformSnapshot(transform, ToSnapshot(originalTransform));
                return;
            }

            var fallbackSnapshot = ChangesTrackerCore.GetSnapshot(_targetGo);
            if (fallbackSnapshot != null)
            {
                ApplyTransformSnapshot(transform, fallbackSnapshot);
            }
        }

        private static void ApplyTransformSnapshot(Transform transform, Snapshot snapshot)
        {
            transform.SetLocalPositionAndRotation(snapshot.position, snapshot.rotation);
            transform.localScale = snapshot.scale;

            var rt = transform as RectTransform;
            if (rt != null && snapshot.isRectTransform)
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
        }

        private static Snapshot ToSnapshot(OriginalStore.TransformOriginal original)
        {
            return new Snapshot
            {
                position = original.position,
                rotation = original.rotation,
                scale = original.scale,
                isRectTransform = original.isRectTransform,
                anchoredPosition = original.anchoredPosition,
                anchoredPosition3D = original.anchoredPosition3D,
                anchorMin = original.anchorMin,
                anchorMax = original.anchorMax,
                pivot = original.pivot,
                sizeDelta = original.sizeDelta,
                offsetMin = original.offsetMin,
                offsetMax = original.offsetMax
            };
        }

        private void RevertNameFromOriginal(TargetContext context)
        {
            if (!_hasNameDelta)
            {
                return;
            }

            var nameOriginalStore = NameOriginalStore.LoadExisting();
            var originalName = nameOriginalStore?.entries.Find(e =>
                (!string.IsNullOrEmpty(e.globalObjectId) && !string.IsNullOrEmpty(context.Guid) && e.globalObjectId == context.Guid) ||
                (e.scenePath == context.ScenePath &&
                 e.objectPath == context.ObjectPath));

            if (originalName != null && !string.IsNullOrEmpty(originalName.originalName))
            {
                _targetGo.name = originalName.originalName;
                return;
            }

            var originalNameSnapshot = NameSH.GetNameSnapshot(_targetGo);
            if (originalNameSnapshot != null && !string.IsNullOrEmpty(originalNameSnapshot.objectName))
            {
                _targetGo.name = originalNameSnapshot.objectName;
            }
        }

        private void RevertComponentsFromOriginal(TargetContext context,
            (ChangesStore tStore, CompChangesStore cStore, OriginalStore tOriginal, CompOriginalStore cOriginal) stores)
        {
            foreach (var comp in _changedComponents)
            {
                if (comp is Transform)
                {
                    continue;
                }

                if (TryRevertComponentFromOriginalStore(comp, context, stores.cOriginal))
                {
                    continue;
                }

                TryRevertComponentFromSnapshot(comp);
            }
        }

        private bool TryRevertComponentFromOriginalStore(Component comp, TargetContext context,
            CompOriginalStore originalStore)
        {
            var type = comp.GetType();
            var componentType = type.AssemblyQualifiedName;
            var allOfType = _targetGo.GetComponents(type);
            var compIndex = Array.IndexOf(allOfType, comp);

            var originalEntry = originalStore?.entries.Find(c =>
                c.scenePath == context.ScenePath &&
                c.objectPath == context.ObjectPath &&
                c.componentType == componentType &&
                c.componentIndex == compIndex);

            if (originalEntry == null)
            {
                return false;
            }

            var targetSo = new SerializedObject(comp);
            for (var i = 0; i < originalEntry.propertyPaths.Count; i++)
            {
                var propPath = originalEntry.propertyPaths[i];
                var prop = targetSo.FindProperty(propPath);
                if (prop == null)
                {
                    continue;
                }

                var typeName = i < originalEntry.valueTypes.Count ? originalEntry.valueTypes[i] : string.Empty;
                var value = i < originalEntry.serializedValues.Count
                    ? originalEntry.serializedValues[i]
                    : string.Empty;
                OcpSerialization.ApplySerializedComponentValue(prop, typeName, value);
            }

            targetSo.ApplyModifiedProperties();

            if (comp is Renderer renderer && originalEntry.materialGuids is { Count: > 0 })
            {
                ApplyMaterials(renderer, originalEntry.materialGuids);
            }

            return true;
        }

        private void TryRevertComponentFromSnapshot(Component comp)
        {
            var compKey = ChangesTrackerCore.GetComponentKey(comp);
            var snapshot = ChangesTrackerCore.GetComponentSnapshot(_targetGo, compKey);
            if (snapshot == null)
            {
                return;
            }

            RevertComponent(comp, snapshot);
            if (comp is Renderer renderer && snapshot.materialGuids is { Count: > 0 })
            {
                ApplyMaterials(renderer, snapshot.materialGuids);
            }
        }

        private void CleanupStoresAfterOriginalRevert(TargetContext context,
            (ChangesStore tStore, CompChangesStore cStore, OriginalStore tOriginal, CompOriginalStore cOriginal) stores)
        {
            if ((context.TransformListed || context.NameOnly) && stores.tStore != null)
            {
                RemoveTransformEntry(stores.tStore, context);
            }

            if (_hasNameDelta)
            {
                RemoveNameEntry(context);
            }

            if (stores.cStore != null)
            {
                RemoveComponentEntries(stores.cStore, context);
                EditorUtility.SetDirty(stores.cStore);
            }
        }

        private static void RemoveTransformEntry(ChangesStore tStore, TargetContext context)
        {
            var removeIndex = tStore.changes.FindIndex(c =>
                (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(context.Guid) && c.globalObjectId == context.Guid) ||
                (c.scenePath == context.ScenePath &&
                 c.objectPath == context.ObjectPath));

            if (removeIndex >= 0)
            {
                tStore.changes.RemoveAt(removeIndex);
                EditorUtility.SetDirty(tStore);
            }
        }

        private static void RemoveNameEntry(TargetContext context)
        {
            var nameStore = NameChangesStore.LoadExisting();
            if (nameStore == null)
            {
                return;
            }

            var removeIndex = nameStore.changes.FindIndex(c =>
                (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(context.Guid) && c.globalObjectId == context.Guid) ||
                (c.scenePath == context.ScenePath &&
                 c.objectPath == context.ObjectPath));
            if (removeIndex >= 0)
            {
                nameStore.changes.RemoveAt(removeIndex);
                EditorUtility.SetDirty(nameStore);
            }
        }

        private void RemoveComponentEntries(CompChangesStore cStore, TargetContext context)
        {
            foreach (var comp in _changedComponents)
            {
                if (comp is Transform or RectTransform)
                {
                    continue;
                }

                var type = comp.GetType();
                var componentType = type.AssemblyQualifiedName;
                var allOfType = _targetGo.GetComponents(type);
                var compIndex = Array.IndexOf(allOfType, comp);

                var removeIndex = cStore.changes.FindIndex(c =>
                    c.scenePath == context.ScenePath &&
                    c.objectPath == context.ObjectPath &&
                    c.componentType == componentType &&
                    c.componentIndex == compIndex);

                if (removeIndex >= 0)
                {
                    cStore.changes.RemoveAt(removeIndex);
                }
            }
        }

        private bool HasAnySavedEntries()
        {
            var context = BuildTargetContext();

            if (HasSavedTransform(context))
            {
                return true;
            }

            if (HasSavedName(context))
            {
                return true;
            }

            return HasSavedComponent(context);
        }

        private void RevertTransformFromSaved(TargetContext context, ChangesStore tStore)
        {
            if (!context.TransformListed || tStore == null)
            {
                return;
            }

            var index = tStore.changes.FindIndex(c =>
                (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(context.Guid) && c.globalObjectId == context.Guid) ||
                (c.scenePath == context.ScenePath &&
                 c.objectPath == context.ObjectPath));
            if (index < 0)
            {
                return;
            }

            var storedChange = tStore.changes[index];
            var t = _targetGo.transform;

            t.SetLocalPositionAndRotation(storedChange.position, storedChange.rotation);
            t.localScale = storedChange.scale;

            var rt = t as RectTransform;
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

        private void RevertNameFromSaved(TargetContext context)
        {
            if (!_hasNameDelta)
            {
                return;
            }

            var nameStore = NameChangesStore.LoadExisting();
            if (nameStore == null)
            {
                return;
            }

            var index = nameStore.changes.FindIndex(c =>
                (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(context.Guid) && c.globalObjectId == context.Guid) ||
                (c.scenePath == context.ScenePath &&
                 c.objectPath == context.ObjectPath));
            if (index < 0)
            {
                return;
            }

            var storedChange = nameStore.changes[index];
            _targetGo.name = storedChange.newName;
        }

        private void RevertComponentsFromSaved(TargetContext context, CompChangesStore cStore)
        {
            if (cStore == null)
            {
                return;
            }

            foreach (var comp in _changedComponents)
            {
                if (comp is Transform or RectTransform)
                {
                    continue;
                }

                if (!TryGetComponentChange(context, cStore, comp, out var storedChange))
                {
                    continue;
                }

                var targetSo = new SerializedObject(comp);

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

                if (storedChange.includeMaterialChanges && comp is Renderer renderer)
                {
                    ApplyMaterials(renderer, storedChange.materialGuids);
                }
            }
        }

        private bool TryGetComponentChange(TargetContext context, CompChangesStore cStore, Component comp,
            out CompChangesStore.ComponentChange storedChange)
        {
            var type = comp.GetType();
            var componentType = type.AssemblyQualifiedName;
            var allOfType = _targetGo.GetComponents(type);
            var compIndex = Array.IndexOf(allOfType, comp);

            var index = cStore.changes.FindIndex(c =>
                c.componentType == componentType &&
                c.componentIndex == compIndex &&
                (
                    (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(context.Guid) && c.globalObjectId == context.Guid) ||
                    (c.scenePath == context.ScenePath &&
                     c.objectPath == context.ObjectPath)
                ));

            if (index >= 0)
            {
                storedChange = cStore.changes[index];
                return true;
            }

            storedChange = null;
            return false;
        }

        private static bool HasSavedTransform(TargetContext context)
        {
            if (!context.TransformListed)
            {
                return false;
            }

            var tStore = ChangesStore.LoadExisting();
            if (tStore == null)
            {
                return false;
            }

            var index = tStore.changes.FindIndex(c =>
                c.scenePath == context.ScenePath && c.objectPath == context.ObjectPath);
            return index >= 0;
        }

        private static bool HasSavedName(TargetContext context)
        {
            var nameStore = NameChangesStore.LoadExisting();
            if (nameStore == null)
            {
                return false;
            }

            var index = nameStore.changes.FindIndex(c =>
                (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(context.Guid) && c.globalObjectId == context.Guid) ||
                (c.scenePath == context.ScenePath &&
                 c.objectPath == context.ObjectPath));
            return index >= 0;
        }

        private bool HasSavedComponent(TargetContext context)
        {
            var cStore = CompChangesStore.LoadExisting();
            if (cStore == null)
            {
                return false;
            }

            foreach (var comp in _changedComponents)
            {
                if (comp is Transform or RectTransform)
                {
                    continue;
                }

                if (TryGetComponentChange(context, cStore, comp, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyAllChanges()
        {
            // Accept changes
            var hasTransformChange = _changedComponents.Any(comp => comp is Transform or RectTransform);

            if (hasTransformChange)
            {
                ChangesTrackerCore.AcceptTransformChanges(_targetGo);
            }

            if (_hasNameDelta)
            {
                ChangesTrackerCore.AcceptNameChanges(_targetGo);
            }


            foreach (var comp in _changedComponents)
            {
                if (comp is null or Transform)
                {
                    continue;
                }

                ChangesTrackerCore.AcceptComponentChanges(comp);
            }
        }

        private static void RevertComponent(Component comp, CompSnapshot snapshot)
        {
            SerializedObject so = new(comp);

            foreach (var kvp in snapshot.Properties)
            {
                var prop = so.FindProperty(kvp.Key);
                if (prop == null)
                {
                    continue;
                }

                try
                {
                    SetPropertyValue(prop, kvp.Value);
                }
                catch
                {
                    // ignored
                }
            }

            so.ApplyModifiedProperties();
        }

        private static void SetPropertyValue(SerializedProperty prop, object value)
        {
            if (value == null)
            {
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: prop.intValue = (int)value; break;
                case SerializedPropertyType.Boolean: prop.boolValue = (bool)value; break;
                case SerializedPropertyType.Float: prop.floatValue = (float)value; break;
                case SerializedPropertyType.String: prop.stringValue = (string)value; break;
                case SerializedPropertyType.Color: prop.colorValue = (Color)value; break;
                case SerializedPropertyType.Vector2: prop.vector2Value = (Vector2)value; break;
                case SerializedPropertyType.Vector3: prop.vector3Value = (Vector3)value; break;
                case SerializedPropertyType.Vector4: prop.vector4Value = (Vector4)value; break;
                case SerializedPropertyType.Quaternion: prop.quaternionValue = (Quaternion)value; break;
                case SerializedPropertyType.Enum: prop.enumValueIndex = (int)value; break;
            }
        }

        private static void RefreshBrowserIfOpen()
        {
            if (EditorWindow.HasOpenInstances<OverridesBrowserWindow>())
            {
                OverridesBrowserWindow.Open();
            }
        }

        private void RefreshContent(GameObject go)
        {
            _changedComponents.Clear();
            _changedComponents.AddRange(ChangesTrackerCore.GetChangedComponents(go));
        }

        private static void ApplyMaterials(Renderer renderer, List<string> materialGuids)
        {
            if (renderer == null || materialGuids == null || materialGuids.Count == 0)
            {
                return;
            }

            var current = renderer.sharedMaterials;
            var applied = new Material[materialGuids.Count];

            for (var i = 0; i < materialGuids.Count; i++)
            {
                var guid = materialGuids[i];
                if (string.IsNullOrEmpty(guid))
                {
                    applied[i] = null;
                    continue;
                }

                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null && i < current.Length)
                {
                    applied[i] = current[i];
                }
                else
                {
                    applied[i] = mat;
                }
            }

            renderer.sharedMaterials = applied;
        }

        private struct TargetContext
        {
            public string ScenePath;
            public string ObjectPath;
            public string Guid;
            public bool TransformListed;
            public bool NameOnly;
        }
    }
}