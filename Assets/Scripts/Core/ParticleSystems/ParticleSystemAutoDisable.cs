using UnityEngine;
using Sirenix.OdinInspector;

namespace Core.ParticleSystems
{
    /**
     * Disables this GameObject once its particle system (and all child systems)
     * have finished playing — the signal an MMF object pool uses to recycle it.
     *
     * Why not use the ParticleSystem's built-in "Stop Action = Disable"?
     * Stop Action disables each system on the object it lives on. On a multi-emitter
     * effect that means every child emitter disables ITS OWN GameObject when it ends.
     * Re-activating the root (what a pool does on reuse) does NOT bring those children
     * back, because their activeSelf was individually set false — so the effect plays
     * once and is then permanently dead. This component instead leaves every system's
     * Stop Action at None and disables ONLY the root, and only when the whole hierarchy
     * is done, so a pooled instance restarts cleanly every reuse.
     *
     * Setup: put this on the root of a pooled particle prefab whose root ParticleSystem
     * is the one MMF plays. Set Stop Action = None on every system in the prefab.
     */
    [RequireComponent(typeof(ParticleSystem))]
    public class ParticleSystemAutoDisable : MonoBehaviour
    {
        // =====================
        // State
        // =====================

        private ParticleSystem _particles;

        // Guards the startup frame: OnEnable runs before MMF calls Play(), so the
        // system isn't alive yet. We only start watching for "finished" after we've
        // first observed it actually playing.
        private bool _hasStarted;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _particles = GetComponent<ParticleSystem>();
        }

        /** Reset the startup guard each time the pool re-activates this instance. */
        private void OnEnable()
        {
            _hasStarted = false;
        }

        /**
         * Watches the effect's lifetime. Waits until the system reports alive (Play
         * has kicked in), then disables the GameObject the moment it — together with
         * all child emitters — goes fully dead, handing the instance back to the pool.
         */
        private void LateUpdate()
        {
            if (_particles == null) return;

            // Phase 1: wait for Play() to take effect so we don't disable on the first frame
            if (!_hasStarted)
            {
                if (_particles.IsAlive(true)) _hasStarted = true;
                return;
            }

            // Phase 2: once it has played out completely, return to the pool
            if (!_particles.IsAlive(true)) gameObject.SetActive(false);
        }

        // -------------------------------------------------------
        // Debug
        // -------------------------------------------------------

        [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
        private bool IsAliveWithChildren => Application.isPlaying && _particles != null && _particles.IsAlive(true);
    }
}
