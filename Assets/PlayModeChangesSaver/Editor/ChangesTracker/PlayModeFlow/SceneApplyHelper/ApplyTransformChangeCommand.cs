using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static PlayModeChangesSaver.Editor.ChangesTracker.PlayModeFlow.SceneApplyProcessor;


namespace PlayModeChangesSaver.Editor.ChangesTracker.PlayModeFlow.SceneApplyHelper
{
    public class ApplyTransformChangeCommand : IApplyCommand
    {
        private readonly ChangesStore.TransformChange _change;
        private Scene _scene;

        public ApplyTransformChangeCommand(Scene scene, ChangesStore.TransformChange change)
        {
            _scene = scene;
            _change = change;
        }

        public void Execute()
        {
            var go =
                SceneAndPathUtilities.FindGameObjectByGuidOrPath(_scene, _change.globalObjectId, _change.objectPath);
            if (!go)
            {
                return;
            }

            var t = go.transform;
            var rt = t as RectTransform;

            Undo.RecordObject(t, "Apply Play Mode Transform Changes");

            if (HasModifiedProperties())
            {
                ApplyModifiedProperties(t, rt);
            }
            else
            {
                ApplyFullSnapshot(t, rt);
            }

            MarkDirty(go);
        }

        private bool HasModifiedProperties()
        {
            return _change.modifiedProperties is { Count: > 0 };
        }

        private void ApplyModifiedProperties(Transform t, RectTransform rt)
        {
            foreach (var prop in _change.modifiedProperties)
            {
                ApplyPropertyToTransform(t, rt, _change, prop);
            }
        }

        private void ApplyFullSnapshot(Transform t, RectTransform rt)
        {
            t.SetLocalPositionAndRotation(_change.position, _change.rotation);
            t.localScale = _change.scale;

            ApplyRectTransformSnapshot(rt);
        }

        private void ApplyRectTransformSnapshot(RectTransform rt)
        {
            if (!rt || !_change.isRectTransform)
            {
                return;
            }

            rt.anchoredPosition = _change.anchoredPosition;
            rt.anchoredPosition3D = _change.anchoredPosition3D;
            rt.anchorMin = _change.anchorMin;
            rt.anchorMax = _change.anchorMax;
            rt.pivot = _change.pivot;
            rt.sizeDelta = _change.sizeDelta;
            rt.offsetMin = _change.offsetMin;
            rt.offsetMax = _change.offsetMax;
        }

        private void MarkDirty(Object target)
        {
            EditorUtility.SetDirty(target);
            if (_scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(_scene);
            }
        }
    }
}