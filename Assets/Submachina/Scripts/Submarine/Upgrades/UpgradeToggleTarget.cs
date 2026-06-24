using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Marks a GameObject (anywhere in the submarine hierarchy) as belonging to a
     * toggleable UpgradeFeature. The UpgradeManager discovers these by walking the
     * sub's children and switches matching objects on/off when upgrades that
     * reference the same UpgradeFeature are granted, removed, or toggled.
     *
     * Many objects can share one feature, so a single upgrade can flip a whole
     * group at once. The object's authored active state is captured the first time
     * the manager touches it and restored once no active upgrade wants it on or off.
     *
     * Note: objects that start disabled never receive Awake, so the original state
     * is captured on demand by the manager (EnsureOriginalCaptured) rather than here.
     */
    public class UpgradeToggleTarget : MonoBehaviour
    {
        // =====================
        // Identity
        // =====================

        [Tooltip("The feature this object belongs to. Upgrades referencing the same " +
                 "UpgradeFeature asset will switch this object on or off.")]
        [SerializeField] private UpgradeFeature feature;

        /** The feature this target is tagged with (matched by reference). */
        public UpgradeFeature Feature => feature;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when an upgrade switches this object on.")]
        public UnityEvent onActivated;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when an upgrade switches this object off.")]
        public UnityEvent onDeactivated;

        // =====================
        // Internals
        // =====================

        private bool _originalActive;
        private bool _captured;

        // -------------------------------------------------------
        // Manager API
        // -------------------------------------------------------

        /**
         * Records the object's authored active state the first time it is touched.
         * Safe to call repeatedly — only the first call captures. Called by the
         * manager before any override so the original can later be restored.
         * (Works on disabled objects, which never run Awake.)
         */
        public void EnsureOriginalCaptured()
        {
            if (_captured) return;
            _originalActive = gameObject.activeSelf;
            _captured = true;
        }

        /**
         * Applies an upgrade-driven active state and fires the matching event.
         * Skips the SetActive call when already in the requested state to avoid
         * redundant activation churn (but still no-ops cleanly).
         */
        public void SetActiveState(bool active)
        {
            if (gameObject.activeSelf == active) return;

            gameObject.SetActive(active);
            if (active) onActivated?.Invoke();
            else onDeactivated?.Invoke();
        }

        /** Restores the authored active state captured before the first override. */
        public void RestoreOriginal()
        {
            if (!_captured) return;
            SetActiveState(_originalActive);
        }
    }
}
