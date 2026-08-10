using UnityEngine.SceneManagement;

namespace Submachina.Meta
{
    /**
     * Static hand-off between the hub and mission scenes.
     *
     * The hub stores the accepted offer here and loads the mission scene;
     * MissionController reads it on Start. Static (not DontDestroyOnLoad
     * MonoBehaviour) because it's pure data with no lifecycle — and mission
     * scenes played directly in the editor simply see null and fall back to
     * a debug spec.
     */
    public static class MissionContext
    {
        public const string HubSceneName = "Hub";
        public const string MissionSceneName = "Mission_Descent";

        /** The accepted mission offer, or null when the scene was played directly. */
        public static MissionSpec Current { get; private set; }

        /** Result of the last finished mission — read by the hub for a debrief line. */
        public static bool LastMissionSucceeded { get; private set; }
        public static bool HasLastResult { get; private set; }

        /** Hub → mission: stash the spec and load the gameplay scene. */
        public static void Launch(MissionSpec spec)
        {
            Current = spec;
            SceneManager.LoadScene(MissionSceneName);
        }

        /** Mission → hub: record the outcome and return to the hub scene. */
        public static void ReturnToHub(bool success)
        {
            LastMissionSucceeded = success;
            HasLastResult = true;
            Current = null;
            SceneManager.LoadScene(HubSceneName);
        }
    }
}
