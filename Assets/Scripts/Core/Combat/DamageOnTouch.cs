using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Sirenix.OdinInspector;

/**
 * Kills the player (or any target with matching tag) on contact.
 * Attach to hostile dimension variant enemies (e.g., D1_EnemyBlock).
 *
 * Provides multiple response options:
 *  - Reload current scene (default game-over behavior)
 *  - Destroy the target GameObject
 *  - Fire custom UnityEvents for juice/feedback
 */
[RequireComponent(typeof(Collider))]
public class DamageOnTouch : MonoBehaviour
{
    // =====================
    // Settings
    // =====================

    [FoldoutGroup("Detection")]
    [Tooltip("Tag of the target that takes damage when touched. Typically 'Player'.")]
    [SerializeField] private string targetTag = "Player";

    [FoldoutGroup("Response")]
    [Tooltip("What happens when contact occurs")]
    [SerializeField] private DamageResponse responseType = DamageResponse.ReloadScene;

    [FoldoutGroup("Response")]
    [ShowIf("responseType", DamageResponse.ReloadScene)]
    [Tooltip("Delay before reloading the scene, giving time for death effects to play")]
    [SerializeField, Min(0f)] private float reloadDelay = 0.5f;

    // =====================
    // Events
    // =====================

    [FoldoutGroup("Events")]
    [Tooltip("Fired when contact occurs. Wire MMF_Player for death effects, sounds, screen shake, etc.")]
    public UnityEvent onDamageDealt;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when contact occurs. Passes the damaged GameObject for more control.")]
    public UnityEvent<GameObject> onDamageDealtWithTarget;

    // =====================
    // Internal
    // =====================

    // Guard against multiple triggers in the same frame
    private bool _hasTriggered = false;

    // -------------------------------------------------------
    // Collision Detection
    // -------------------------------------------------------

    /**
     * Fires when another collider enters this trigger zone.
     * Checks target tag, then applies the configured damage response.
     *
     * Sequence:
     *  1. Guard against double-triggers
     *  2. Validate target tag
     *  3. Fire events for juice/feedback
     *  4. Apply damage response (reload scene, destroy target, etc.)
     */
    private void OnTriggerEnter(Collider other)
    {
        if (_hasTriggered) return;
        if (!other.CompareTag(targetTag)) return;

        _hasTriggered = true;

        // Fire events for death effects, sounds, screen shake, etc.
        onDamageDealt?.Invoke();
        onDamageDealtWithTarget?.Invoke(other.gameObject);

        // Apply the configured response
        ApplyDamageResponse(other.gameObject);
    }

    /**
     * Applies the selected damage response based on responseType setting.
     */
    private void ApplyDamageResponse(GameObject target)
    {
        switch (responseType)
        {
            case DamageResponse.ReloadScene:
                Invoke(nameof(ReloadCurrentScene), reloadDelay);
                break;

            case DamageResponse.DestroyTarget:
                Destroy(target);
                break;

            case DamageResponse.EventsOnly:
                // No automatic response — handle via UnityEvents
                break;
        }
    }

    /**
     * Reloads the current active scene.
     * Typically used for simple game-over → retry behavior.
     */
    private void ReloadCurrentScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // -------------------------------------------------------
    // Editor Utilities
    // -------------------------------------------------------

#if UNITY_EDITOR
    [FoldoutGroup("Debug")]
    [Button("Simulate Touch"), GUIColor(1f, 0.6f, 0.6f)]
    [Tooltip("Simulates contact with the player for testing death effects in the editor")]
    private void SimulateTouch()
    {
        _hasTriggered = false; // reset guard
        GameObject player = GameObject.FindGameObjectWithTag(targetTag);
        if (player != null)
        {
            onDamageDealt?.Invoke();
            onDamageDealtWithTarget?.Invoke(player);
            Debug.Log($"[DamageOnTouch] Simulated touch with '{player.name}'");
        }
        else
        {
            Debug.LogWarning($"[DamageOnTouch] No GameObject found with tag '{targetTag}'");
        }
    }
#endif

    // -------------------------------------------------------
    // Gizmos
    // -------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        // Draw red danger zone matching the trigger bounds
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;

        if (col is BoxCollider box)
        {
            Gizmos.DrawCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.DrawSphere(sphere.center, sphere.radius);
        }
    }
}

/**
 * Defines how the damage handler responds to contact.
 */
public enum DamageResponse
{
    ReloadScene,    // Restart the level (typical game-over)
    DestroyTarget,  // Destroy the GameObject that was touched
    EventsOnly      // Only fire UnityEvents, no automatic behavior
}
