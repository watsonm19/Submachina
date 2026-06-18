using UnityEngine;

namespace Submachina.Core
{
    /**
     * Runtime state for a single granted upgrade on a submarine.
     *
     * Tracks the current level, enabled state, and references to any
     * spawned GameObjects (behavioral add-ons or component swaps).
     * Managed exclusively by UpgradeManager.
     */
    public class UpgradeInstance
    {
        /** Current upgrade level (1 to UpgradeDef.maxLevel). */
        public int level;

        /** Whether this upgrade is currently active. Toggling off removes
         *  stat modifiers and deactivates behaviors/swaps without removing the upgrade. */
        public bool enabled = true;

        /** Spawned behavioral add-on GameObject (null for stat-only or swap upgrades). */
        public GameObject behaviorInstance;

        /** Spawned component swap variant GameObject (null for non-swap upgrades). */
        public GameObject swapInstance;

        /** The original GameObject that was deactivated by a component swap.
         *  Re-activated when the swap upgrade is removed or toggled off. */
        public GameObject deactivatedOriginal;
    }
}
