using Sirenix.OdinInspector;
using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * Pins a child Transform (an eye, a fin, a beak, a bioluminescent spot, a
     * particle emitter) to a point ON a RadialMeshRenderer body, so it rides that
     * body's squash/stretch/rim-wobble instead of floating rigidly over it.
     *
     * The anchor stores a REST position — where the art sits on the undeformed
     * silhouette — and every LateUpdate pushes that point through the exact same
     * deformation the mesh vertices use (RadialMeshRenderer.DeformLocalPoint), so
     * the two can never disagree.
     *
     * Two ways to author the rest position:
     *   Rest Point — a raw local offset. Drop the art where you want it in the
     *                scene and hit "Capture From Transform". Best for eyes and
     *                anything sitting inside the body.
     *   Polar      — an angle + a fraction of the silhouette radius at that angle.
     *                Radius-relative, so re-baking a different silhouette sprite
     *                keeps fins/spines glued to the same part of the outline.
     *
     * Optional extras, all weighted 0-1 so they can be dialed in per creature:
     *   Inherit Squash   — the art scales with the local deformation (an eye that
     *                      squishes with the mantle rather than sliding rigidly).
     *   Orient To Surface — the art's +Y aims along the deformed surface normal
     *                      (fins fan out correctly as the body flattens).
     *
     * Companion to RadialMeshRenderer's Deform Pivot: the pivot chooses what stays
     * PUT during a squash, the anchors keep everything else attached to what moves.
     */
    [ExecuteAlways]
    [DefaultExecutionOrder(65)] // after RadialMeshRenderer (60) has resolved the frame's deformation
    public class RadialMeshAnchor : MonoBehaviour
    {
        /** How the rest position on the body is authored. */
        public enum AnchorSpace
        {
            /** A raw local-space offset from the body's origin — capture it from wherever you dragged the art. */
            RestPoint,
            /** An angle plus a fraction of the silhouette radius there — stays glued to the outline across re-bakes. */
            Polar
        }

        // =====================
        // Body
        // =====================

        [FoldoutGroup("Body")]
        [Tooltip("Deformable body this anchor rides. Auto-resolves from the parent hierarchy if empty.")]
        [SerializeField] private RadialMeshRenderer body;

        // =====================
        // Placement
        // =====================

        [FoldoutGroup("Placement")]
        [Tooltip("Rest Point: a raw local offset (capture it from the scene). Polar: an angle + fraction of the silhouette radius, which survives silhouette re-bakes.")]
        [SerializeField, EnumToggleButtons] private AnchorSpace space = AnchorSpace.RestPoint;

        [FoldoutGroup("Placement")]
        [Tooltip("Undeformed position on the body, in the body's local space. Author it by dragging this object into place (with the body at rest) and pressing Capture From Transform.")]
        [SerializeField, ShowIf(nameof(IsRestPoint))] private Vector2 restPoint;

        [FoldoutGroup("Placement")]
        [Tooltip("Angle around the body (degrees CCW from +X) the anchor sits at.")]
        [SerializeField, Range(-180f, 180f), HideIf(nameof(IsRestPoint))] private float angleDegrees;

        [FoldoutGroup("Placement")]
        [Tooltip("Distance out along that angle as a fraction of the rest silhouette radius: 0 = body center, 1 = right on the outline, >1 = floating off the surface.")]
        [SerializeField, Range(0f, 1.5f), HideIf(nameof(IsRestPoint))] private float radialFraction = 1f;

        [FoldoutGroup("Placement")]
        [Tooltip("Local Z the anchor holds — the sorting depth of the art, untouched by the 2D deformation.")]
        [SerializeField] private float restDepth;

        // =====================
        // Follow
        // =====================

        [FoldoutGroup("Follow")]
        [Tooltip("How much of the body's deformation the position follows. 1 = welded to the surface, 0 = holds the rest pose, mid = a softer partial follow.")]
        [SerializeField, Range(0f, 1f)] private float followWeight = 1f;

        [FoldoutGroup("Follow")]
        [Tooltip("How much of the rim ripple reaches this anchor. The body already scales the wobble by how far out the anchor sits, so this is a per-anchor trim on top (0 = ignore ripple entirely).")]
        [SerializeField, Range(0f, 1f)] private float wobbleWeight = 1f;

        [FoldoutGroup("Follow")]
        [Tooltip("Scales the art by the body's local squash: 0 = rigid art sliding over the surface (right for a hard eyeball), 1 = the art squashes exactly as much as the body does (right for soft skin markings).")]
        [SerializeField, Range(0f, 1f)] private float inheritSquash;

        [FoldoutGroup("Follow")]
        [Tooltip("Rotates the art so its +Y points along the DEFORMED surface normal — fins/spines fan correctly as the body flattens. 0 = keep the captured rest rotation.")]
        [SerializeField, Range(0f, 1f)] private float orientToSurface;

        [FoldoutGroup("Follow")]
        [Tooltip("Extra Z rotation (degrees) added after the surface alignment — hand-tuning for art that isn't authored pointing up.")]
        [SerializeField, Range(-180f, 180f)] private float rotationOffset;

        // =====================
        // Captured rest pose
        // =====================

        [FoldoutGroup("Rest Pose")]
        [Tooltip("Body-relative Z rotation the art rests at — blended away as Orient To Surface rises.")]
        [SerializeField] private float restRotation;

        [FoldoutGroup("Rest Pose")]
        [Tooltip("Local scale the art rests at. Inherit Squash multiplies this rather than overwriting it, so the authored size survives.")]
        [SerializeField] private Vector3 restScale = Vector3.one;

        [FoldoutGroup("Rest Pose")]
        [Tooltip("Draw the rest point, the live deformed point, and the link back to the body pivot while selected.")]
        [SerializeField] private bool drawGizmos = true;

        private bool IsRestPoint => space == AnchorSpace.RestPoint;

        /** The undeformed local point on the body this anchor represents. */
        public Vector2 RestLocalPoint => ResolveRestPoint();

        /** Where that point currently sits after the body's live deformation. */
        public Vector2 DeformedLocalPoint => body != null ? body.DeformLocalPoint(ResolveRestPoint(), wobbleWeight) : ResolveRestPoint();

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Reset()
        {
            // Adding the component in the editor captures wherever the artist already
            // dragged the art — the common case is "this is exactly where I want it".
            ResolveBody();
            CaptureFromTransform();
        }

        private void OnValidate() => ResolveBody();

        private void OnEnable() => ResolveBody();

        /**
         * Finds the body to ride when none is assigned. Anchored art is just as often
         * a SIBLING of the mesh (eye and mantle both parented under a creature's
         * "Visual" root) as a child of it, so this widens the search one ancestor at a
         * time — nearest relative wins — instead of only looking straight up.
         */
        private void ResolveBody()
        {
            if (body != null) return;

            body = GetComponentInParent<RadialMeshRenderer>();
            if (body != null) return;

            for (Transform p = transform.parent; p != null; p = p.parent)
            {
                body = p.GetComponentInChildren<RadialMeshRenderer>(true);
                if (body != null) return;
            }
        }

        private void LateUpdate()
        {
            Apply();
        }

        // -------------------------------------------------------
        // Placement
        // -------------------------------------------------------

        /** Resolves the authored rest position, converting polar coordinates against the body's rest silhouette. */
        private Vector2 ResolveRestPoint()
        {
            if (IsRestPoint || body == null) return restPoint;

            // Polar: walk out along the angle by a fraction of the silhouette's own
            // radius there — e.g. 0.9 keeps a fin just inside the outline whatever
            // shape gets baked in later.
            float rad = angleDegrees * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            return dir * (body.GetRestRadiusAtAngle(angleDegrees) * radialFraction);
        }

        /**
         * Drives the transform from the body's live deformation: position always,
         * rotation only when surface alignment or an offset asks for it, scale only
         * when squash inheritance is dialed in. Every write is skipped when it would
         * be a no-op, so an at-rest anchor never dirties the scene in edit mode.
         */
        private void Apply()
        {
            if (body == null) return;

            Vector2 rest = ResolveRestPoint();
            Vector2 deformed = Vector2.Lerp(rest, body.DeformLocalPoint(rest, wobbleWeight), followWeight);

            // Position: body-local → world, keeping the authored sorting depth.
            Vector3 targetWorld = body.transform.TransformPoint(new Vector3(deformed.x, deformed.y, restDepth));
            if ((transform.position - targetWorld).sqrMagnitude > 1e-10f) transform.position = targetWorld;

            // Rotation: rest pose blended toward the deformed surface normal (+Y = out).
            if (orientToSurface > 0f || !Mathf.Approximately(rotationOffset, 0f))
            {
                float z = restRotation;
                if (orientToSurface > 0f)
                {
                    Vector2 outward = rest.sqrMagnitude > 0.000001f ? rest.normalized : Vector2.up;
                    Vector2 normal = body.DeformNormal(outward);
                    float surfaceZ = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg - 90f;
                    z = Mathf.LerpAngle(restRotation, surfaceZ, orientToSurface);
                }

                Quaternion targetRot = body.transform.rotation * Quaternion.Euler(0f, 0f, z + rotationOffset);
                if (Quaternion.Angle(transform.rotation, targetRot) > 0.001f) transform.rotation = targetRot;
            }

            // Scale: multiply the authored size by (a share of) the body's squash.
            if (inheritSquash > 0f)
            {
                Vector2 ds = body.DeformScale;
                Vector3 targetScale = new Vector3(
                    restScale.x * Mathf.Lerp(1f, ds.x, inheritSquash),
                    restScale.y * Mathf.Lerp(1f, ds.y, inheritSquash),
                    restScale.z);
                if ((transform.localScale - targetScale).sqrMagnitude > 1e-10f) transform.localScale = targetScale;
            }
        }

        // -------------------------------------------------------
        // Authoring
        // -------------------------------------------------------

        /**
         * Reads the current transform back into the rest pose — position, depth,
         * rotation and scale. Author with the body AT REST (edit mode, or a paused
         * neutral frame); capturing mid-squash bakes that squash into the rest pose.
         */
        [FoldoutGroup("Placement")]
        [Button("Capture From Transform", ButtonSizes.Medium)]
        public void CaptureFromTransform()
        {
            ResolveBody();
            if (body == null)
            {
                Debug.LogWarning($"[RadialMeshAnchor] {name}: no RadialMeshRenderer found on this object, its parents, or their children — nothing to anchor to.", this);
                return;
            }

            Vector3 local = body.transform.InverseTransformPoint(transform.position);
            restDepth = local.z;

            Vector2 p = new Vector2(local.x, local.y);
            if (IsRestPoint)
            {
                restPoint = p;
            }
            else
            {
                angleDegrees = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
                float restRadius = body.GetRestRadiusAtAngle(angleDegrees);
                radialFraction = restRadius > 0.0001f ? p.magnitude / restRadius : 1f;
            }

            restRotation = (Quaternion.Inverse(body.transform.rotation) * transform.rotation).eulerAngles.z;
            restScale = transform.localScale;

            if (Application.isPlaying && (body.Squash - Vector2.one).sqrMagnitude > 0.0004f)
                Debug.LogWarning($"[RadialMeshAnchor] {name}: captured while the body was squashed ({body.Squash}) — that deformation is now baked into the rest pose.", this);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /** Snaps the transform onto the stored rest pose — the visual confirmation of what was captured. */
        [FoldoutGroup("Placement")]
        [Button("Snap To Rest Pose")]
        public void SnapToRestPose()
        {
            if (body == null) return;
            Vector2 rest = ResolveRestPoint();
            transform.position = body.transform.TransformPoint(new Vector3(rest.x, rest.y, restDepth));
            transform.rotation = body.transform.rotation * Quaternion.Euler(0f, 0f, restRotation + rotationOffset);
            transform.localScale = restScale;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || body == null) return;

            Gizmos.matrix = body.transform.localToWorldMatrix;
            Vector2 rest = ResolveRestPoint();
            Vector2 live = Vector2.Lerp(rest, body.DeformLocalPoint(rest, wobbleWeight), followWeight);

            // Rest position + the tether back to the pivot everything deforms around.
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.6f);
            Gizmos.DrawLine(body.DeformPivot, rest);
            Gizmos.DrawWireSphere(rest, 0.05f);

            // Where the anchor actually is this frame.
            Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.9f);
            Gizmos.DrawSphere(live, 0.05f);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
