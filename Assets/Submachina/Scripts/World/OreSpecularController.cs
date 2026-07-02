using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Ore-specific `SpecularController`: everything the generic base does (per-instance
     * look, idle shimmer, light reactivity, one-shot `Pulse`) PLUS a sustained "mining
     * glow" that flares the ore while the laser is cutting it.
     *
     * The base owns the wake/sleep + `_SpecBoost` write; this subclass just contributes
     * the mining term through the two extension hooks (`ComposeBoost` / `IsIdle`) and wakes
     * the component when the glow turns on. So idle ore is still zero-cost — only the 1–2
     * rocks currently being mined or flashing tick.
     *
     * Use this on ore; use the plain `SpecularController` for any other shiny sprite.
     */
    public class OreSpecularController : SpecularController
    {
        [FoldoutGroup("Mining Glow")]
        [Tooltip("Extra specular added at full mining glow (SetMiningGlow(1)). Lets a MiningResource flare the ore as the laser mines it.")]
        [SerializeField, Min(0f)] private float miningGlowResponse = 3f;

        private float _miningGlow; // 0..1, set by a MiningResource while the laser mines this ore

        /**
         * Sets a sustained mining glow (0..1) that ramps the specular up while the laser is
         * mining this ore. Wakes the component so the boost is applied; when the glow (and
         * any pulse) return to zero the base puts itself back to sleep.
         */
        public void SetMiningGlow(float glow01)
        {
            _miningGlow = Mathf.Clamp01(glow01);
            Wake();
        }

        /** Add the mining glow on top of the base pulse. */
        protected override float ComposeBoost() => base.ComposeBoost() + _miningGlow * miningGlowResponse;

        /** Stay awake while the glow is active, not just while a pulse decays. */
        protected override bool IsIdle() => base.IsIdle() && _miningGlow <= 0f;
    }
}
