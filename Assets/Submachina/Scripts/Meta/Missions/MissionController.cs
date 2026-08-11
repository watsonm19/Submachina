using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;
using Submachina.Core;

namespace Submachina.Meta
{
    /**
     * Scene orchestrator for a mission run (one per mission scene, plain scene
     * object — no singleton).
     *
     * On Start it reads MissionContext.Current (or builds a debug spec when the
     * scene is played directly), then:
     *   - applies the environment: currentStrength → CurrentManager speed boost
     *   - spawns the objective at targetDepth (cargo pod / hostile / survey sites)
     *   - tracks objective completion and the sub's deepest depth
     *   - completes the mission when the objective is done AND the sub returns
     *     above extractionY: banks cargo + reward to the profile, records stats,
     *     and returns to the hub
     *   - fails the mission on sub death: unbanked cargo is lost, stats recorded
     *
     * All majors are exposed as UnityEvents for feedback/UI wiring, and an OnGUI
     * debug overlay makes the flow testable before any real mission UI exists.
     */
    public class MissionController : MonoBehaviour
    {
        // =====================
        // Scene References
        // =====================

        [FoldoutGroup("Scene")]
        [Tooltip("The scene's CurrentManager — receives the mission's current-strength boost. Found automatically when left empty.")]
        [SerializeField] private CurrentManager currentManager;

        [FoldoutGroup("Scene")]
        [Tooltip("Y the sub must rise above (with the objective done) to extract. Slightly below the surface.")]
        [SerializeField] private float extractionY = -5f;

        // =====================
        // Objective Prefabs
        // =====================

        [FoldoutGroup("Objectives")]
        [Tooltip("Retrieval: the cargo pod (MissionCargo + SonarTarget).")]
        [SerializeField] private MissionCargo cargoPodPrefab;

        [FoldoutGroup("Objectives")]
        [Tooltip("Neutralize: the hostile creature to kill (needs a Health component).")]
        [SerializeField] private GameObject hostilePrefab;

        [FoldoutGroup("Objectives")]
        [Tooltip("Research: one survey site (ResearchTarget + SonarTarget).")]
        [SerializeField] private ResearchTarget researchTargetPrefab;

        [FoldoutGroup("Objectives")]
        [Tooltip("Horizontal scatter around x=0 for spawned objectives.")]
        [SerializeField, Min(0f)] private float spawnSpreadX = 25f;

        // =====================
        // Debug Fallback
        // =====================

        [FoldoutGroup("Debug")]
        [Tooltip("Spec used when the scene is played directly (no hub launch).")]
        [SerializeField] private MissionType debugType = MissionType.Retrieval;

        [FoldoutGroup("Debug")]
        [SerializeField, Min(20f)] private float debugTargetDepth = 90f;

        [FoldoutGroup("Debug")]
        [SerializeField] private bool showDebugGUI = true;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")] public UnityEvent onMissionStarted;
        [FoldoutGroup("Events")] public UnityEvent onObjectiveComplete;
        [FoldoutGroup("Events")] public UnityEvent onMissionComplete;
        [FoldoutGroup("Events")] public UnityEvent onMissionFailed;

        // =====================
        // Public State
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public MissionSpec Spec { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public bool ObjectiveComplete { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public bool MissionOver { get; private set; }

        // =====================
        // Internals
        // =====================

        private Submarine _sub;
        private bool _initialized;
        private float _deepestDepth;
        private int _researchRemaining;
        private const float ReturnToHubDelay = 3f;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        /**
         * Scene subs often ship INACTIVE (the local-multiplayer drop-in flow
         * activates them on join), so Submarine.All can be empty at Start.
         * Start only resolves the spec and — when no join manager owns the
         * scene — wakes the sub itself; the real mission init defers until a
         * submarine has actually registered (see Update).
         */
        private void Start()
        {
            Spec = MissionContext.Current ?? BuildDebugSpec();
            ActivateSubmarineIfUnmanaged();
        }

        private void Update()
        {
            // Defer full setup until a submarine registers (join flow / activation)
            if (!_initialized) { TryInitialize(); if (!_initialized) return; }
            if (MissionOver) return;

            // Track the run's depth record for the profile
            _deepestDepth = Mathf.Max(_deepestDepth, -_sub.transform.position.y);

            // Extraction: objective done + back near the surface
            if (ObjectiveComplete && _sub.transform.position.y >= extractionY)
                CompleteMission();
        }

        /**
         * When the scene has a LocalPlayerManager, players activate their subs
         * through the drop-in join flow — leave it alone. Otherwise (single
         * player mission launch) wake the first sub found, active or not.
         */
        private void ActivateSubmarineIfUnmanaged()
        {
            if (Submarine.All.Count > 0) return;

            // Only an ACTIVE join manager owns the scene — a disabled one means
            // the drop-in flow is intentionally off, so we wake the sub ourselves
            var joinManager = FindFirstObjectByType<LocalPlayerManager>();
            if (joinManager != null && joinManager.isActiveAndEnabled) return;

            var subs = FindObjectsByType<Submarine>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (subs.Length > 0) subs[0].gameObject.SetActive(true);
            else Debug.LogError("[MissionController] No submarine (active or inactive) in scene.");
        }

        /** One-time mission setup, run as soon as a submarine has registered. */
        private void TryInitialize()
        {
            if (Submarine.All.Count == 0) return;
            _sub = Submarine.All[0];
            _initialized = true;

            // Fail the run when the sub dies — unbanked cargo goes down with it
            _sub.Health?.onDeath.AddListener(OnSubDeath);

            EnsureCameraTracking();
            ApplyEnvironment();
            SpawnObjective();
            onMissionStarted?.Invoke();
        }

        /**
         * With the join flow off, nothing drives the camera: the join manager
         * normally Register()s players with MultiTargetCamera2D on join — and in
         * scenes where that camera rig lives ON the (now disabled) manager
         * object, the single-target CameraFollow on the camera is left disabled
         * too. So: an ACTIVE LocalPlayerManager owns the camera — don't touch
         * anything. Otherwise prefer a live MultiTargetCamera2D (register every
         * sub), and fall back to waking the dormant CameraFollow, retargeted at
         * the mission sub.
         */
        private void EnsureCameraTracking()
        {
            var joinManager = FindFirstObjectByType<LocalPlayerManager>();
            if (joinManager != null && joinManager.isActiveAndEnabled) return;

            // A multi-target rig that is actually running (not one buried on the
            // disabled manager object) takes priority
            var multiCam = FindFirstObjectByType<MultiTargetCamera2D>();
            if (multiCam != null && multiCam.isActiveAndEnabled)
            {
                foreach (var sub in Submarine.All) multiCam.Register(sub.transform);
                multiCam.SnapToTargets();
                return;
            }

            // Single-player fallback: the follow cam ships disabled in scenes
            // authored for the join flow — wake it and aim it at the sub
            var follow = FindFirstObjectByType<CameraFollow>();
            if (follow != null)
            {
                follow.SetTarget(_sub.transform);
                follow.enabled = true;
                follow.SnapToTarget();
            }
            else
            {
                Debug.LogWarning("[MissionController] No operational camera driver found (MultiTargetCamera2D inactive, no CameraFollow).");
            }
        }

        // -------------------------------------------------------
        // Setup
        // -------------------------------------------------------

        /** Mission scene played directly — synthesize a spec so testing works. */
        private MissionSpec BuildDebugSpec()
        {
            Debug.Log("[MissionController] No MissionContext — using debug spec.");
            return new MissionSpec
            {
                type = debugType,
                targetDepth = debugTargetDepth,
                title = "Debug Mission",
                researchTargetCount = 3,
            };
        }

        /** Pushes the spec's environment knobs into the scene systems. */
        private void ApplyEnvironment()
        {
            if (currentManager == null) currentManager = FindFirstObjectByType<CurrentManager>();
            if (currentManager != null && Spec.currentStrength > 0f)
                currentManager.AddSpeedBoost(Spec.currentStrength);

            // O2 richness: rich water extracts easier — ambient decay slows; thin
            // water drains faster. Applied through the stat table so it stacks
            // cleanly with upgrades. Example: richness 1.25 → decay ×0.8.
            if (_sub?.Upgrades != null && Spec.o2Richness > 0f && !Mathf.Approximately(Spec.o2Richness, 1f))
                _sub.Upgrades.Stats.Add(SubStats.BaseDecayRate, 0f, 1f / Spec.o2Richness - 1f);
        }

        /** Instantiates the objective for the spec's mission type at target depth. */
        private void SpawnObjective()
        {
            switch (Spec.type)
            {
                case MissionType.Retrieval:
                    if (!SpawnGuard(cargoPodPrefab, "cargo pod")) return;
                    var pod = Instantiate(cargoPodPrefab, SpawnPoint(0), Quaternion.identity);
                    pod.onRetrieved.AddListener(SetObjectiveComplete);
                    break;

                case MissionType.Neutralize:
                    if (!SpawnGuard(hostilePrefab, "hostile")) return;
                    var hostile = Instantiate(hostilePrefab, SpawnPoint(0), Quaternion.identity);
                    var health = hostile.GetComponentInChildren<Health>();
                    if (health != null) health.onDeath.AddListener(SetObjectiveComplete);
                    else { Debug.LogError("[MissionController] Hostile prefab has no Health — completing objective immediately."); SetObjectiveComplete(); }
                    break;

                case MissionType.Research:
                    if (!SpawnGuard(researchTargetPrefab, "research target")) return;
                    _researchRemaining = Mathf.Max(1, Spec.researchTargetCount);
                    for (int i = 0; i < _researchRemaining; i++)
                    {
                        var site = Instantiate(researchTargetPrefab, SpawnPoint(i), Quaternion.identity);
                        site.onScanned.AddListener(OnSiteScanned);
                    }
                    break;
            }
        }

        /** Objective positions scatter horizontally and stagger slightly in depth. */
        private Vector3 SpawnPoint(int index)
        {
            var rng = new System.Random(Spec.seed + index);
            float x = ((float)rng.NextDouble() * 2f - 1f) * spawnSpreadX;
            float depthJitter = index * 12f + (float)rng.NextDouble() * 8f;
            return new Vector3(x, -(Spec.targetDepth + depthJitter), 0f);
        }

        private bool SpawnGuard(Object prefab, string label)
        {
            if (prefab != null) return true;
            Debug.LogError($"[MissionController] No {label} prefab assigned — objective cannot spawn.");
            return false;
        }

        // -------------------------------------------------------
        // Objective & outcome
        // -------------------------------------------------------

        private void OnSiteScanned()
        {
            _researchRemaining--;
            if (_researchRemaining <= 0) SetObjectiveComplete();
        }

        private void SetObjectiveComplete()
        {
            if (ObjectiveComplete || MissionOver) return;
            ObjectiveComplete = true;
            onObjectiveComplete?.Invoke();
        }

        /** Extraction reached: bank everything, record the win, head home. */
        private void CompleteMission()
        {
            if (MissionOver) return;
            MissionOver = true;

            // Bank whatever was actually mined and hauled home, then empty the hold —
            // there is no completion gift; the world's resources ARE the reward
            ProfileService.BankCargo(_sub.Cargo);
            _sub.Cargo?.Clear();

            ProfileService.RecordMission(success: true, _deepestDepth);
            onMissionComplete?.Invoke();
            Invoke(nameof(ReturnToHubSuccess), ReturnToHubDelay);
        }

        /** Sub died: cargo is lost with the wreck; permanents persist. */
        private void OnSubDeath()
        {
            if (MissionOver) return;
            MissionOver = true;

            ProfileService.RecordMission(success: false, _deepestDepth);
            onMissionFailed?.Invoke();
            Invoke(nameof(ReturnToHubFailure), ReturnToHubDelay);
        }

        private void ReturnToHubSuccess() => ReturnToHub(true);
        private void ReturnToHubFailure() => ReturnToHub(false);

        /** Loads the hub when it's available; otherwise stays for scene testing. */
        private void ReturnToHub(bool success)
        {
            if (Application.CanStreamedLevelBeLoaded(MissionContext.HubSceneName))
                MissionContext.ReturnToHub(success);
            else
                Debug.LogWarning("[MissionController] Hub scene not in build settings — staying in scene.");
        }

        // -------------------------------------------------------
        // Debug overlay
        // -------------------------------------------------------

        /** Minimal on-screen mission state until real mission UI exists. */
        private void OnGUI()
        {
            if (!showDebugGUI || Spec == null) return;

            GUILayout.BeginArea(new Rect(10, 270, 340, 130), GUI.skin.box);
            GUILayout.Label($"MISSION: {Spec.title} ({Spec.type})");
            GUILayout.Label($"Objective @ {Spec.targetDepth:0} m — {(ObjectiveComplete ? "COMPLETE — return to surface!" : "in progress")}");
            if (Spec.type == MissionType.Research && !ObjectiveComplete)
                GUILayout.Label($"Sites remaining: {_researchRemaining}");
            if (MissionOver) GUILayout.Label("MISSION OVER");
            GUILayout.EndArea();
        }
    }
}
