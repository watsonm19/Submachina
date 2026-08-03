using Core.Modulation;
using UnityEngine;
using UnityEngine.Events;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Submachina.Core
{
    /**
     * Occasionally darts a creepy silhouette sprite straight across the camera view —
     * a "something just moved in the dark" beat. Gated by a semantic parameter (e.g.
     * Threat or Darkness) so skitters ramp up as the descent gets scarier.
     *
     * Uses a single pooled child SpriteRenderer rather than instantiating per skitter,
     * so repeated attempts never allocate. Update-driven: one component handles both
     * the idle countdown between attempts and the active cross-screen movement.
     */
    public class SkitterSpawner : MonoBehaviour
    {
        private enum State { Idle, Moving }

        [Header("Director Gate")]
        [Tooltip("Auto-resolved via EnvironmentDirector.FindFor() when left empty.")]
        [SerializeField] private EnvironmentDirector director;

        [Tooltip("Semantic parameter checked against minParameterValue before each attempt. Left null, the gate is always open.")]
        [SerializeField] private DirectorParameterDef parameter;

        [Tooltip("Minimum director value required for an attempt to be allowed to spawn.")]
        [SerializeField] private float minParameterValue = 0.5f;

        [Header("Timing")]
        [Tooltip("Random range (seconds) between skitter attempts.")]
        [SerializeField] private Vector2 intervalRange = new Vector2(15f, 40f);

        [Range(0f, 1f)]
        [Tooltip("Chance an eligible attempt actually spawns a skitter.")]
        [SerializeField] private float probability = 0.7f;

        [Header("Visuals")]
        [Tooltip("Random silhouette sprite pool.")]
        [SerializeField] private Sprite[] sprites;

        [Tooltip("Tint applied to the sprite — a dark, mostly-opaque silhouette by default.")]
        [SerializeField] private Color tint = new Color(0.05f, 0.05f, 0.05f, 0.9f);

        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder = 50;

        [Header("Motion")]
        [Tooltip("Random world-units/second speed range for the crossing.")]
        [SerializeField] private Vector2 speedRange = new Vector2(8f, 18f);

        [Tooltip("Random uniform scale range applied to the sprite each spawn.")]
        [SerializeField] private Vector2 scaleRange = new Vector2(0.5f, 1.5f);

        [Tooltip("Random viewport-Y range (0 = bottom, 1 = top) the crossing travels along.")]
        [SerializeField] private Vector2 viewportYRange = new Vector2(0.15f, 0.85f);

        [Header("Events")]
        [Tooltip("Raised the instant a skitter spawns — wire whoosh/scuttle sounds in the scene.")]
        public UnityEvent onSkitter;

        // Pooled sprite — created lazily on first spawn, reused for every skitter thereafter.
        private GameObject _pooled;
        private Transform _pooledTransform;
        private SpriteRenderer _pooledRenderer;

        // Attempt / movement state.
        private State _state = State.Idle;
        private float _timer;
        private Vector3 _moveDir;
        private float _moveSpeed;
        private float _targetX;
        private bool _movingRight;

        private void OnEnable()
        {
            if (director == null) director = EnvironmentDirector.FindFor(this);
            ScheduleNextAttempt();
        }

        private void OnDisable()
        {
            if (_pooled != null) _pooled.SetActive(false);
            _state = State.Idle;
        }

        private void Update()
        {
            if (_state == State.Idle)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f) AttemptSkitter();
                return;
            }

            UpdateMovement(Time.deltaTime);
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Forces a skitter to spawn right now, bypassing the gate/probability roll — handy for previewing placement and motion.</summary>
#if ODIN_INSPECTOR
        [Button("Try Skitter Now (test)")]
#endif
        public void TrySkitterNow()
        {
            SpawnSkitter();
        }

        // ------------------------------------------------------------------ attempt / spawn

        private void ScheduleNextAttempt()
        {
            _timer = Random.Range(intervalRange.x, intervalRange.y);
            _state = State.Idle;
        }

        /**
         * Rolls whether this attempt spawns a skitter: the semantic parameter (if assigned)
         * must be at or above minParameterValue, and a probability roll must pass. Either way
         * the next attempt is rescheduled.
         */
        private void AttemptSkitter()
        {
            bool parameterOk = parameter == null || director == null || director.GetValue(parameter) >= minParameterValue;
            bool roll = Random.value <= probability;

            if (parameterOk && roll) SpawnSkitter();
            ScheduleNextAttempt();
        }

        /**
         * Places the pooled sprite just off one horizontal screen edge at a random viewport
         * height, then starts it moving straight across to beyond the opposite edge.
         * Example: movingRight true → starts at viewport x=-0.1, ends past x=1.1.
         */
        private void SpawnSkitter()
        {
            Camera cam = Camera.main;
            if (cam == null || sprites == null || sprites.Length == 0) return;

            EnsurePooled();

            bool movingRight = Random.value < 0.5f;
            float startViewportX = movingRight ? -0.1f : 1.1f;
            float endViewportX = movingRight ? 1.1f : -0.1f;
            float viewportY = Random.Range(viewportYRange.x, viewportYRange.y);

            // Convert viewport points to world space at the camera's distance from the z=0 gameplay plane.
            float worldZ = Mathf.Abs(cam.transform.position.z);
            Vector3 startWorld = cam.ViewportToWorldPoint(new Vector3(startViewportX, viewportY, worldZ));
            Vector3 endWorld = cam.ViewportToWorldPoint(new Vector3(endViewportX, viewportY, worldZ));
            startWorld.z = 0f;
            endWorld.z = 0f;

            _pooledRenderer.sprite = sprites[Random.Range(0, sprites.Length)];
            _pooledRenderer.flipX = !movingRight; // sprites are assumed to face right by default
            _pooledTransform.position = startWorld;

            float scale = Random.Range(scaleRange.x, scaleRange.y);
            _pooledTransform.localScale = new Vector3(scale, scale, 1f);
            _pooled.SetActive(true);

            _moveDir = (endWorld - startWorld).normalized;
            _moveSpeed = Random.Range(speedRange.x, speedRange.y);
            _targetX = endWorld.x;
            _movingRight = movingRight;
            _state = State.Moving;

            onSkitter?.Invoke();
        }

        /** Advances the pooled sprite along its crossing and deactivates it once it passes the far edge. */
        private void UpdateMovement(float dt)
        {
            _pooledTransform.position += _moveDir * (_moveSpeed * dt);

            bool arrived = _movingRight ? _pooledTransform.position.x >= _targetX : _pooledTransform.position.x <= _targetX;
            if (!arrived) return;

            _pooled.SetActive(false);
            ScheduleNextAttempt();
        }

        /// <summary>Lazily creates the single reused sprite object, parented under this spawner.</summary>
        private void EnsurePooled()
        {
            if (_pooled != null) return;

            _pooled = new GameObject("SkitterSprite");
            _pooled.transform.SetParent(transform, false);
            _pooledRenderer = _pooled.AddComponent<SpriteRenderer>();
            _pooledRenderer.color = tint;
            _pooledRenderer.sortingLayerName = sortingLayerName;
            _pooledRenderer.sortingOrder = sortingOrder;
            _pooledTransform = _pooled.transform;
            _pooled.SetActive(false);
        }
    }
}
