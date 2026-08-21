using PlayModeChangesSaver.Editor.ChangesTracker.SnapShotHelper;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PlayModeChangesSaver.Editor.ChangesTracker.Recording
{
    public static class NameChangeRecorder
    {
        public static void RecordNameChangeToStore(GameObject go)
        {
            var original = NameSH.GetNameSnapshot(go);
            if (original == null)
            {
                return;
            }

            var nameStore = NameChangesStore.LoadOrCreate();
            var nameOriginalStore = NameOriginalStore.LoadOrCreate();

            var firstOriginalName = GetFirstOriginalName(go, original.objectName);

            var currentPath = SceneAndPathUtilities.GetGameObjectPath(go.transform);
            var objectPath = BuildOriginalPath(currentPath, go.name, firstOriginalName);

            var nameContext = new NameChangeContext
            {
                ScenePath = ChangesTrackerCore.GetNormalizedScenePath(go),
                ObjectPath = objectPath,
                GlobalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString(),
                OriginalName = firstOriginalName,
                NewName = go.name,
                SiblingIndex = go.transform.GetSiblingIndex()
            };

            StoreOriginalNameIfNeeded(nameOriginalStore, nameContext);
            StoreNameChange(nameStore, nameContext);
            NameSH.SetNameSnapshot(go, new NameSnapshot(go));
        }

        private static string GetFirstOriginalName(GameObject go, string fallbackName)
        {
            var originalStore = NameOriginalStore.LoadExisting();
            if (!originalStore)
            {
                return fallbackName;
            }

            var goid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            var scenePath = ChangesTrackerCore.GetNormalizedScenePath(go);
            var objectPath = SceneAndPathUtilities.GetGameObjectPath(go.transform);

            var existingEntry = originalStore.entries.Find(e =>
                (!string.IsNullOrEmpty(e.globalObjectId) && !string.IsNullOrEmpty(goid) &&
                 e.globalObjectId == goid) ||
                (e.scenePath == scenePath && e.objectPath == objectPath));

            return existingEntry?.originalName ?? fallbackName;
        }

        private static string BuildOriginalPath(string currentPath, string currentName, string originalName)
        {
            if (currentName == originalName)
            {
                return currentPath;
            }

            var lastSlash = currentPath.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                return currentPath.Substring(0, lastSlash + 1) + originalName;
            }

            return originalName;
        }

        public static void RecordNameChangesForLoadedScenes()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var sceneCount = SceneManager.sceneCount;
            for (var i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    RecordNameChangesRecursive(root);
                }
            }
        }

        private static void RecordNameChangesRecursive(GameObject go)
        {
            if (!go)
            {
                return;
            }

            if (ChangesTrackerCore.HasNameDelta(go))
            {
                RecordNameChangeToStore(go);
            }

            foreach (Transform child in go.transform)
            {
                RecordNameChangesRecursive(child.gameObject);
            }
        }

        private static void StoreOriginalNameIfNeeded(NameOriginalStore store, NameChangeContext ctx)
        {
            if (NameOriginalAlreadyExists(store, ctx))
            {
                return;
            }

            var entry = new NameOriginalStore.NameOriginal
            {
                scenePath = ctx.ScenePath,
                objectPath = ctx.ObjectPath,
                globalObjectId = ctx.GlobalObjectId,
                originalName = ctx.OriginalName,
                siblingIndex = ctx.SiblingIndex
            };
            store.entries.Add(entry);
            EditorUtility.SetDirty(store);
        }

        private static bool NameOriginalAlreadyExists(NameOriginalStore store, NameChangeContext ctx)
        {
            return store.entries.Exists(e => IsMatchingNameOriginal(e, ctx));
        }

        private static bool IsMatchingNameOriginal(NameOriginalStore.NameOriginal e, NameChangeContext ctx)
        {
            if (!string.IsNullOrEmpty(e.globalObjectId) && !string.IsNullOrEmpty(ctx.GlobalObjectId))
            {
                if (e.globalObjectId == ctx.GlobalObjectId)
                {
                    return true;
                }
            }

            return e.scenePath == ctx.ScenePath && e.objectPath == ctx.ObjectPath;
        }

        private static void StoreNameChange(NameChangesStore store, NameChangeContext ctx)
        {
            var change = new NameChangesStore.NameChange
            {
                scenePath = ctx.ScenePath,
                objectPath = ctx.ObjectPath,
                globalObjectId = ctx.GlobalObjectId,
                newName = ctx.NewName,
                siblingIndex = ctx.SiblingIndex
            };

            var existingIndex = store.changes.FindIndex(c => IsMatchingNameChange(c, ctx));
            if (existingIndex >= 0)
            {
                store.changes[existingIndex] = change;
            }
            else
            {
                store.changes.Add(change);
            }

            EditorUtility.SetDirty(store);
            AssetDatabase.SaveAssets();
        }

        private static bool IsMatchingNameChange(NameChangesStore.NameChange c, NameChangeContext ctx)
        {
            if (!string.IsNullOrEmpty(c.globalObjectId) && !string.IsNullOrEmpty(ctx.GlobalObjectId))
            {
                if (c.globalObjectId == ctx.GlobalObjectId)
                {
                    return true;
                }
            }

            return c.scenePath == ctx.ScenePath && c.objectPath == ctx.ObjectPath;
        }
    }
}