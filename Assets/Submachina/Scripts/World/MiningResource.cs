using System.Collections.Generic;
using Core.Rendering;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * A mining resource node that requires a sustained laser beam to collect.
     *
     * The node does NOT collect on player touch — the MiningLaser script on the
     * submarine drives mining progress by calling SetMiningProgress each frame
     * while the beam is on target. When progress reaches 1.0, MiningLaser calls
     * Collect() directly. Moving the beam off-target calls SetMiningProgress(0).
     *
     * Visual feedback scales with mining progress, chosen once at Awake by what the
     * node (and its children) actually have — works for a single sprite OR a
     * composite of children:
     *   1. OreSpecularController(s) present  → drive their "mining glow" (the
     *      controller stays the single MaterialPropertyBlock writer).
     *   2. Else renderers using the OreLit material (any "_SpecIntensity" shader)
     *      → push _SpecIntensity up directly via a MaterialPropertyBlock.
     *   3. Else (plain sprites)              → classic bleach-toward-white tint.
     *
     * Setup:
     *   - Attach to the resource prefab alongside a CircleCollider2D.
     *   - Set the prefab's layer to "Resource" so MiningLaser's raycast can hit it.
     *   - Resources are awarded through the Submarine passed to Collect() by
     *     MiningLaser — no references need wiring at spawn time.
     */
    [RequireComponent(typeof(Collider2D))]
    public class MiningResource : MonoBehaviour
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Settings")]
        [Tooltip("Resource units awarded on successful collection.")]
        [SerializeField, Min(0f)] private float resourceValue = 10f;

        [FoldoutGroup("Settings")]
        [Tooltip("Probability of dropping one scrap on collection. " +
                 "Example: 0.20 = 20% chance, roughly 1 scrap per 5 nodes mined.")]
        [SerializeField, Range(0f, 1f)] private float scrapDropChance = 0.20f;

        [FoldoutGroup("Settings")]
        [Tooltip("Prefab to spawn in the world on a successful scrap drop roll. " +
                 "Assign the ScrapPickup prefab here — it will be collected by the " +
                 "submarine's PickupRangeDetector when the player comes within range.")]
        [SerializeField] private GameObject scrapPickupPrefab;

        [FoldoutGroup("Settings")]
        [Tooltip("When on, forces the collider to be a trigger so the sub cannot bump into it")]
        [SerializeField] private bool isTrigger = true;

        // =====================
        // Glow
        // =====================

        [FoldoutGroup("Glow")]
        [Tooltip("Extra specular 'glow power' (_SpecIntensity) added at full mining progress, for renderers " +
                 "using the OreLit material directly. Renderers driven by an OreSpecularController use that " +
                 "component's own Mining Glow Response instead.")]
        [SerializeField, Min(0f)] private float glowBoost = 3f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float MiningProgress => _currentProgress;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private GlowMode FeedbackMode => _mode;

        // =====================
        // State
        // =====================

        private enum GlowMode { Fallback, OreLitDirect, Controller }
        private GlowMode _mode;

        // Controller mode
        private OreSpecularController[] _controllers;

        // OreLit-direct mode
        private SpriteRenderer[] _glowRenderers;
        private float[] _glowBaseIntensity;
        private MaterialPropertyBlock _mpb;

        // Fallback (tint) mode
        private SpriteRenderer[] _fallbackRenderers;
        private Color[] _fallbackBaseColors;

        private float _currentProgress;
        private static readonly int SpecIntensityID = Shader.PropertyToID("_SpecIntensity");

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        /**
         * Trigger setup + picks the feedback path once, based on what this node has.
         * Priority: OreSpecularController → OreLit material → plain-sprite tint.
         */
        private void Awake()
        {
            // Trigger collider allows MiningLaser raycasts to detect this node
            if(isTrigger) GetComponent<Collider2D>().isTrigger = true;

            // 1) Controllers anywhere (self or children) own the glint — drive through them
            //    so they remain the single MaterialPropertyBlock writer.
            _controllers = GetComponentsInChildren<OreSpecularController>(true);
            if (_controllers.Length > 0) { _mode = GlowMode.Controller; return; }

            // 2) Otherwise, any renderer whose material exposes _SpecIntensity (OreLit) —
            //    push the glow up directly via a property block.
            var renderers = GetComponentsInChildren<SpriteRenderer>(true);
            var glow = new List<SpriteRenderer>();
            foreach (var r in renderers)
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(SpecIntensityID)) glow.Add(r);

            if (glow.Count > 0)
            {
                _mode = GlowMode.OreLitDirect;
                _glowRenderers = glow.ToArray();
                _glowBaseIntensity = new float[_glowRenderers.Length];
                for (int i = 0; i < _glowRenderers.Length; i++)
                    _glowBaseIntensity[i] = _glowRenderers[i].sharedMaterial.GetFloat(SpecIntensityID);
                _mpb = new MaterialPropertyBlock();
                return;
            }

            // 3) Plain sprites — fall back to the classic bleach-toward-white tint,
            //    across every renderer so it works single OR multi-child.
            _mode = GlowMode.Fallback;
            _fallbackRenderers = renderers;
            _fallbackBaseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                _fallbackBaseColors[i] = renderers[i].color;
        }

        // -------------------------------------------------------
        // Mining API
        // -------------------------------------------------------

        /**
         * Called by MiningLaser each frame the beam is on this node (and with 0 when
         * the beam leaves). Progress is 0..1; at 1.0 the node is ready to be collected.
         * Drives whichever feedback path was selected at Awake.
         */
        public void SetMiningProgress(float progress)
        {
            _currentProgress = Mathf.Clamp01(progress);

            switch (_mode)
            {
                case GlowMode.Controller:
                    // Each controller scales by its own Mining Glow Response
                    for (int i = 0; i < _controllers.Length; i++)
                        _controllers[i].SetMiningGlow(_currentProgress);
                    break;

                case GlowMode.OreLitDirect:
                    // Glow power = material base + progress * boost, per renderer
                    for (int i = 0; i < _glowRenderers.Length; i++)
                    {
                        var r = _glowRenderers[i];
                        r.GetPropertyBlock(_mpb);
                        _mpb.SetFloat(SpecIntensityID, _glowBaseIntensity[i] + _currentProgress * glowBoost);
                        r.SetPropertyBlock(_mpb);
                    }
                    break;

                case GlowMode.Fallback:
                    // Classic: bleach each sprite toward white as mining progresses
                    for (int i = 0; i < _fallbackRenderers.Length; i++)
                        _fallbackRenderers[i].color = Color.Lerp(_fallbackBaseColors[i], Color.white, _currentProgress);
                    break;
            }
        }

        /**
         * Awards resources to the collecting submarine, rolls for a scrap drop, then destroys this node.
         * Called by MiningLaser when the beam has been held on target
         * for the full mining duration.
         *
         * Scrap roll: Random.value produces a uniform 0-1 value each call.
         * Example: scrapDropChance=0.20 → ~1 scrap dropped per 5 nodes mined on average.
         */
        public void Collect(Submarine sub)
        {
            if (sub?.Resources != null)
                sub.Resources.AddResources(resourceValue);
            else
                Debug.LogWarning("[MiningResource] No Submarine ResourceManager available.");

            // Roll for a scrap pickup drop — spawns a physical world object that the
            // player must come within pickup range to collect (bank-full check happens there)
            if (scrapPickupPrefab != null && Random.value < scrapDropChance)
            {
                GameObject scrap = Instantiate(scrapPickupPrefab, transform.position, Quaternion.identity);
                ShadowCaster2DRefresher.RefreshHierarchy(scrap); // URP 2D casters don't rebuild their mesh on clone — force it
            }

            Destroy(gameObject);
        }
    }
}
