using System;
using PlayModeChangesSaver.Editor.ChangesTracker.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static PlayModeChangesSaver.Editor.ChangesTracker.PlayModeFlow.SceneApplyProcessor;


namespace PlayModeChangesSaver.Editor.ChangesTracker.PlayModeFlow.SceneApplyHelper
{
    public class ApplyComponentChangeCommand : IApplyCommand
    {
        private readonly CompChangesStore.ComponentChange _change;
        private Scene _scene;

        public ApplyComponentChangeCommand(Scene scene, CompChangesStore.ComponentChange change)
        {
            _scene = scene;
            _change = change;
        }

        public void Execute()
        {
            if (!TryGetTargetComponent(out var comp))
            {
                return;
            }

            var so = new SerializedObject(comp);
            Undo.RecordObject(comp, "Apply Play Mode Component Changes");

            ApplySerializedProperties(so);
            so.ApplyModifiedProperties();

            ApplyMaterialChangesIfNeeded(comp);

            EditorUtility.SetDirty(comp);
            if (_scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(_scene);
            }
        }

        private bool TryGetTargetComponent(out Component component)
        {
            component = null;

            var go = SceneAndPathUtilities.FindGameObjectByGuidOrPath(_scene, _change.globalObjectId,
                _change.objectPath);
            if (!go)
            {
                return false;
            }

            var type = Type.GetType(_change.componentType);
            if (type == null)
            {
                return false;
            }

            var allComps = go.GetComponents(type);
            if (_change.componentIndex < 0 || _change.componentIndex >= allComps.Length)
            {
                return false;
            }

            component = allComps[_change.componentIndex];
            if (!component)
            {
                return false;
            }

            return true;
        }

        private void ApplySerializedProperties(SerializedObject so)
        {
            for (var i = 0; i < _change.propertyPaths.Count; i++)
            {
                var path = _change.propertyPaths[i];
                var value = _change.serializedValues[i];
                var typeName = _change.valueTypes[i];

                var prop = so.FindProperty(path);
                if (prop == null)
                {
                    continue;
                }

                ComponentPropertySerializer.ApplyPropertyValue(prop, typeName, value);
            }
        }

        private void ApplyMaterialChangesIfNeeded(Component comp)
        {
            if (!_change.includeMaterialChanges)
            {
                return;
            }

            if (comp is Renderer renderer)
            {
                ApplyMaterials(renderer, _change.materialGuids);
            }
        }
    }
}