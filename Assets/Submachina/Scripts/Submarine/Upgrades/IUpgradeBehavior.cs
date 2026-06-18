namespace Submachina.Core
{
    /**
     * Implemented by behavioral upgrade components instantiated on the submarine.
     *
     * When an UpgradeDef with a behaviorPrefab is granted, the UpgradeManager
     * instantiates the prefab as a child of the submarine and calls
     * OnUpgradeEnabled on all IUpgradeBehavior components on that prefab.
     *
     * Lifecycle:
     *   Grant    → Instantiate → OnUpgradeEnabled(sub, level)
     *   Toggle   → OnUpgradeDisabled(sub)  /  OnUpgradeEnabled(sub, level)
     *   Remove   → OnUpgradeDisabled(sub) → Destroy
     */
    public interface IUpgradeBehavior
    {
        /** Called when the upgrade is first applied or re-enabled after toggling. */
        void OnUpgradeEnabled(Submarine sub, int level);

        /** Called when the upgrade is toggled off or about to be removed. */
        void OnUpgradeDisabled(Submarine sub);
    }
}
