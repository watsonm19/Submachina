using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives the specular response of nearby ore from a sub light (e.g. the turret
     * spotlight). Lives on the Light2D's GameObject.
     *
     * Pattern: the LIGHT pushes to ore, not the other way around. There are only a
     * few lights but potentially hundreds of ore, so a few periodic OverlapCircle
     * queries is far cheaper than every ore scanning for lights each frame. Out-of-range
     * ore needs no cleanup — OreSpecularController fades its own illumination once we
     * stop refreshing it.
     *
     * "Only glint when the ore is actually in the beam": illumination = distance falloff
     * × a hard cone gate. The cone is centered on the light's local +Y (which is exactly
     * how URP orients a 2D point-light spotlight via transform.rotation), so the gate
     * tracks the real beam as you rotate the light. The numeric URP angle→width mapping
     * is fuzzy, so use the Scene gizmo (select the light) to match Cone Half Angle to the
     * visible beam, or press "Copy Angles From Light2D".
     */
    [RequireComponent(typeof(Light2D))]
    public class SubLightOreIlluminator : MonoBehaviour
    {
        [Tooltip("Layers the ore colliders live on (e.g. the Collision layer rocks sit on).")]
        [SerializeField] private LayerMask oreLayers = ~0;

        [Tooltip("Use the Light2D's outer radius as the reach instead of the explicit range below.")]
        [SerializeField] private bool useLightRadius = true;

        [HideIf(nameof(useLightRadius))]
        [Tooltip("Reach of the effect in world units when not using the light radius.")]
        [SerializeField, Min(0f)] private float range = 6f;

        [Tooltip("Proximity falloff: X = normalized distance 0..1, Y = illumination 0..1.")]
        [SerializeField] private AnimationCurve falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Tooltip("Overall strength multiplier for the pushed illumination (0..1+).")]
        [SerializeField, Min(0f)] private float strength = 1f;

        [Tooltip("Also scale illumination by the light's current intensity (so dimming the light dims the glints).")]
        [SerializeField] private bool scaleByLightIntensity = false;

        // =====================
        // Cone gate (the beam)
        // =====================

        [BoxGroup("Cone"), Tooltip("Only illuminate ore inside the beam arc. Turn off for an omni-directional light.")]
        [SerializeField] private bool useCone = true;

        [BoxGroup("Cone"), ShowIf(nameof(useCone))]
        [Tooltip("Half-angle of the beam in degrees, measured from the aim axis. 45 = a 90°-wide pie slice.")]
        [SerializeField, Range(0f, 180f)] private float coneHalfAngle = 45f;

        [BoxGroup("Cone"), ShowIf(nameof(useCone))]
        [Tooltip("Soft fade past the edge of the cone, in degrees (0 = hard cut-off).")]
        [SerializeField, Range(0f, 45f)] private float coneEdgeSoftness = 8f;

        [BoxGroup("Cone"), ShowIf(nameof(useCone))]
        [Tooltip("Beam axis in the light's local space. +Y matches URP's 2D spotlight direction; leave it unless your light is built differently.")]
        [SerializeField] private Vector2 aimAxisLocal = Vector2.up;

        [BoxGroup("Cone"), ShowIf(nameof(useCone))]
        [Button("Copy Angles From Light2D"), Tooltip("Sets the cone half-angle from the Light2D's outer angle, and the soft edge from the inner/outer gap.")]
        private void CopyAnglesFromLight()
        {
            var lt = GetComponent<Light2D>();
            if (lt == null) return;
            coneHalfAngle = lt.pointLightOuterAngle * 0.5f;
            coneEdgeSoftness = Mathf.Clamp((lt.pointLightOuterAngle - lt.pointLightInnerAngle) * 0.5f, 0f, 45f);
        }

        // =====================
        // Performance
        // =====================

        [BoxGroup("Performance"), Tooltip("Seconds between illumination updates. Larger = cheaper, slightly less responsive.")]
        [SerializeField, Min(0f)] private float tickInterval = 0.08f;

        [BoxGroup("Performance"), Tooltip("Max ore colliders considered per tick.")]
        [SerializeField, Min(1)] private int maxOrePerTick = 32;

        private Light2D _light;
        private Collider2D[] _hits;
        private float _timer;
        private readonly List<OreSpecularController> _ctrls = new List<OreSpecularController>(); // reused buffer (no per-tick alloc)

        private void Awake()
        {
            _light = GetComponent<Light2D>();
            _hits = new Collider2D[maxOrePerTick];
            // Stagger ticks across many illuminators so they don't all fire on the same frame
            _timer = (GetInstanceID() & 0xFF) / 255f * tickInterval;
        }

        /**
         * Throttled tick: find ore in reach, gate each by the cone, and push the surviving
         * illumination plus the direction back toward this light (so the glint aims at it).
         */
        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = tickInterval;

            float reach = Reach();
            if (reach <= 0f) return;

            Vector2 lightPos = transform.position;
            float lightScale = scaleByLightIntensity && _light != null ? Mathf.Max(0f, _light.intensity) : 1f;
            Vector2 aimWorld = ((Vector2)transform.TransformDirection(aimAxisLocal)).normalized;

            int n = Physics2D.OverlapCircleNonAlloc(lightPos, reach, _hits, oreLayers);
            for (int i = 0; i < n; i++)
            {
                // Controllers may sit on the rock root OR on individual nugget children
                // (e.g. only the "shiny" nugget). The collider is on the root, so search
                // the whole hierarchy and drive each controller against its OWN position.
                _hits[i].GetComponentsInChildren(_ctrls);
                for (int j = 0; j < _ctrls.Count; j++)
                {
                    var ctrl = _ctrls[j];

                    Vector2 toOre = (Vector2)ctrl.transform.position - lightPos;
                    float dist = toOre.magnitude;
                    if (dist < 1e-4f) continue;
                    Vector2 dirToOre = toOre / dist;

                    // Distance falloff
                    float illum = falloff.Evaluate(Mathf.Clamp01(dist / reach));

                    // Hard cone gate: angle between the beam axis and the ore. Outside the
                    // cone (+ soft edge) → 0, so a nearby-but-not-aimed light does nothing.
                    if (useCone)
                    {
                        float ang = Vector2.Angle(aimWorld, dirToOre); // 0..180
                        float cone = 1f - Smooth01(coneHalfAngle, coneHalfAngle + coneEdgeSoftness, ang);
                        if (cone <= 0f) continue;
                        illum *= cone;
                    }

                    illum *= strength * lightScale;
                    if (illum <= 0f) continue;

                    ctrl.SetIllumination(illum, -toOre); // dir from ore back toward this light
                }
            }
        }

        /** Effective reach: the light's outer radius, or the explicit range. */
        private float Reach()
        {
            if (useLightRadius)
            {
                var lt = _light != null ? _light : GetComponent<Light2D>();
                return lt != null ? lt.pointLightOuterRadius : range;
            }
            return range;
        }

        /** Hermite smoothstep returning 0 below edge0, 1 above edge1. */
        private static float Smooth01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(edge1 - edge0, 1e-5f));
            return t * t * (3f - 2f * t);
        }

        /** Rotate a 2D vector by degrees (CCW). */
        private static Vector3 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector3(v.x * c - v.y * s, v.x * s + v.y * c, 0f);
        }

        /** Visualize the reach circle and the beam cone in the Scene view. */
        private void OnDrawGizmosSelected()
        {
            Vector3 pos = transform.position;
            float reach = Reach();

            // Reach circle
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(pos, reach);

            if (!useCone) return;

            Vector2 aim = ((Vector2)transform.TransformDirection(aimAxisLocal)).normalized;

            // Solid cone edges + arc (the hard gate)
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.95f);
            DrawConeBoundary(pos, aim, coneHalfAngle, reach);

            // Faint outer edge where the soft falloff reaches zero
            if (coneEdgeSoftness > 0f)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.3f);
                DrawConeBoundary(pos, aim, coneHalfAngle + coneEdgeSoftness, reach);
            }

            // Center aim line
            Gizmos.color = new Color(1f, 1f, 0.6f, 0.6f);
            Gizmos.DrawLine(pos, pos + (Vector3)aim * reach);
        }

        /** Draws the two edge rays + connecting arc for a cone of the given half-angle. */
        private static void DrawConeBoundary(Vector3 pos, Vector2 aim, float halfAngle, float reach)
        {
            Vector3 left = pos + Rotate(aim, halfAngle) * reach;
            Vector3 right = pos + Rotate(aim, -halfAngle) * reach;
            Gizmos.DrawLine(pos, left);
            Gizmos.DrawLine(pos, right);

            const int seg = 24;
            Vector3 prev = right;
            for (int i = 1; i <= seg; i++)
            {
                float a = Mathf.Lerp(-halfAngle, halfAngle, i / (float)seg);
                Vector3 p = pos + Rotate(aim, a) * reach;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
