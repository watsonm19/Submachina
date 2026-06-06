using UnityEngine;
using UnityEngine.SceneManagement;
using Sirenix.OdinInspector;

namespace Utility
{
    /**
     * Simple utility script that can reload a scene when called via Unity Events.
     * Can reload the current scene or a specific referenced scene.
     */
    public class SceneReloader : MonoBehaviour
    {
        [TitleGroup("Scene Settings")]
        [Tooltip("The scene to reload. If null, will reload the current active scene")]
        [SerializeField] private Object sceneAsset;

        /**
         * Reloads the referenced scene, or the current scene if no scene is referenced.
         * This method can be called via Unity Events.
         */
        [Button("Reload Scene", ButtonSizes.Medium)]
        [TitleGroup("Debug")]
        public void ReloadScene()
        {
            // If a scene asset is assigned, reload that specific scene
            if (sceneAsset != null)
            {
                string sceneName = sceneAsset.name;
                if (string.IsNullOrEmpty(sceneName))
                {
                    Debug.LogError("SceneReloader: Scene asset name is empty!", this);
                    return;
                }

                SceneManager.LoadScene(sceneName);
            }
            // Otherwise, reload the currently active scene
            else
            {
                Scene currentScene = SceneManager.GetActiveScene();
                SceneManager.LoadScene(currentScene.name);
            }
        }
    }
}

