using Sirenix.OdinInspector;
using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * Owns and animates one ProcChain anchored to a Transform — the reusable
     * "spine" component behind fish bodies, eels, tentacles, and trailing fins.
     *
     * Each LateUpdate (after physics/AI have moved the anchor) it:
     *   1. Estimates the anchor's velocity and facing.
     *   2. Nudges chain points with the configured drivers — traveling swim wave,
     *      perlin idle sway, and a constant force (droop/buoyancy).
     *   3. Runs the follow-the-leader constraint pass to restore segment lengths.
     *
     * Renderers (ChainStripRenderer, ChainSpriteRenderer) read the solved points
     * after this runs — see the DefaultExecutionOrder pairing.
     */
    [DefaultExecutionOrder(50)]
    public class ChainSimulator : MonoBehaviour, IProcPointSource
    {
        // =====================
        // Chain
        // =====================

        [FoldoutGroup("Chain")]
        [Tooltip("Number of points in the chain. Segments = points - 1. 8-16 is plenty for most creatures.")]
        [SerializeField, Range(2, 64)] private int pointCount = 12;

        [FoldoutGroup("Chain")]
        [Tooltip("World-space distance between consecutive points at transform scale 1 — hierarchy scale multiplies this, so scaling the creature resizes the chain. Total chain length = segmentLength × (points - 1).")]
        [SerializeField, Min(0.01f)] private float segmentLength = 0.25f;

        [FoldoutGroup("Chain")]
        [Tooltip("Maximum bend per joint in degrees. Small = stiff rod, large = floppy rope. 25-40 reads as 'muscular fish', 60+ as 'soft tentacle'.")]
        [SerializeField, Range(1f, 180f)] private float maxBendDegrees = 35f;

        [FoldoutGroup("Chain")]
        [Tooltip("How strongly each segment eases back toward straight (per second). 0 = pure trailing rope; higher values spring the body straight when it stops turning.")]
        [SerializeField, Range(0f, 20f)] private float straightenSpeed = 3f;

        // =====================
        // Anchor
        // =====================

        [FoldoutGroup("Anchor")]
        [Tooltip("Transform the chain head is pinned to. Defaults to this transform.")]
        [SerializeField] private Transform anchor;

        [FoldoutGroup("Anchor")]
        [Tooltip("Local-space offset from the anchor where the head sits (e.g. the rim of a jellyfish bell).")]
        [SerializeField] private Vector2 anchorOffset = Vector2.zero;

        [FoldoutGroup("Anchor")]
        [EnableIf(nameof(HasExplicitAnchor))]
        [Tooltip("When an explicit Anchor is assigned, also offset the head by this GameObject's own position — " +
                 "so the head can be placed by moving this object in the scene view (Anchor Offset still adds on top). " +
                 "Without this, assigning an anchor makes this object's own transform irrelevant. " +
                 "No effect when Anchor is empty: the head already follows this transform then.")]
        [SerializeField] private bool applyLocalPositionOffset = false;

        // The toggle above only means something when the head is pinned to some OTHER transform.
        private bool HasExplicitAnchor => anchor != null && anchor != transform;

        [FoldoutGroup("Anchor")]
        [Tooltip("Head facing used for the first segment's bend limit. Velocity: face travel direction (swimmers). " +
                 "ReverseVelocity: face away from travel, so the body streams out ahead of the anchor (jet-propelled " +
                 "squid/jellyfish, or anything dragged backwards). TransformRight: follow the anchor's rotation " +
                 "(mounted tentacles). None: free-hanging.")]
        [SerializeField] private FacingMode facing = FacingMode.Velocity;

        // Appended-only: Unity serializes enums by index, so new modes go on the end to avoid re-mapping prefabs.
        public enum FacingMode { Velocity, TransformRight, TransformUp, None, ReverseVelocity }

        // =====================
        // Swim Wave
        // =====================

        [FoldoutGroup("Swim Wave")]
        [Tooltip("Base sideways wave amplitude (world units) applied even when stationary. Keeps idle creatures subtly alive.")]
        [SerializeField, Min(0f)] private float idleWaveAmplitude = 0.02f;

        [FoldoutGroup("Swim Wave")]
        [Tooltip("Extra wave amplitude added per unit of anchor speed — faster swimming = bigger undulation. 0 disables speed coupling.")]
        [SerializeField, Min(0f)] private float waveAmplitudePerSpeed = 0.02f;

        [FoldoutGroup("Swim Wave")]
        [Tooltip("Hard cap on total wave amplitude (world units) so burst speeds don't turn the body into a sine explosion.")]
        [SerializeField, Min(0f)] private float maxWaveAmplitude = 0.35f;

        [FoldoutGroup("Swim Wave")]
        [Tooltip("Wave cycles per second. 1-3 for lazy swimmers, 4-8 for frantic darting.")]
        [SerializeField, Min(0f)] private float waveFrequency = 2.5f;

        [FoldoutGroup("Swim Wave")]
        [Tooltip("World-space length of one full wave along the body. Shorter than the chain = visible S-curve; longer = whole-body sway.")]
        [SerializeField, Min(0.05f)] private float waveLength = 2f;

        [FoldoutGroup("Swim Wave")]
        [Tooltip("Amplitude ramp along the chain (x: 0 = head, 1 = tail). Default ramps up toward the tail like a real fish tail-fin.")]
        [SerializeField] private AnimationCurve waveRamp = AnimationCurve.Linear(0f, 0.1f, 1f, 1f);

        // =====================
        // Noise Sway
        // =====================

        [FoldoutGroup("Noise Sway")]
        [Tooltip("Perlin-noise sideways drift amplitude (world units) — organic non-repeating wobble layered over the swim wave. Great for tentacle idle.")]
        [SerializeField, Min(0f)] private float swayAmplitude = 0.05f;

        [FoldoutGroup("Noise Sway")]
        [Tooltip("How fast the noise sway evolves. 0.2-0.5 = slow oceanic drift, 1+ = agitated.")]
        [SerializeField, Min(0f)] private float swayFrequency = 0.4f;

        // =====================
        // Constant Force
        // =====================

        [FoldoutGroup("Constant Force")]
        [Tooltip("Constant world-space drift applied to every point (units/sec). Down = tentacle droop, up = buoyant trailing. Zero for neutral swimmers.")]
        [SerializeField] private Vector2 constantForce = Vector2.zero;

        [FoldoutGroup("Constant Force")]
        [Tooltip("Force ramp along the chain (x: 0 = head, 1 = tail) — lets the tip droop more than the root.")]
        [SerializeField] private AnimationCurve forceRamp = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // =====================
        // Hit Reactions
        // =====================

        [FoldoutGroup("Hit Reactions")]
        [Title("Limp (ragdoll)")]
        [Tooltip("Per-joint bend limit while fully limp, replacing Max Bend Degrees. " +
                 "High values (90-180) let the body fold anywhere, so it hangs and wobbles " +
                 "like rope instead of holding a creature silhouette.")]
        [SerializeField, Range(1f, 180f)] private float limpBendDegrees = 120f;

        [FoldoutGroup("Hit Reactions")]
        [Tooltip("Straighten speed while fully limp, replacing Straighten Speed. " +
                 "0 = pure trailing rope with no spring back toward straight — the core of the " +
                 "ragdoll look. Raise slightly if the body folds up more than you like.")]
        [SerializeField, Range(0f, 20f)] private float limpStraightenSpeed = 0f;

        [FoldoutGroup("Hit Reactions")]
        [Tooltip("Swim-wave amplitude scale while fully limp. 0 stops the undulation entirely, " +
                 "which is what keeps a knocked-back creature from looking like it deliberately " +
                 "turned and swam off. 1 leaves the wave running.")]
        [SerializeField, Range(0f, 1f)] private float limpWaveMultiplier = 0f;

        [FoldoutGroup("Hit Reactions")]
        [Tooltip("Perlin sway scale while fully limp. Above 1 the body keeps drifting and " +
                 "wobbling loosely after the wave cuts out — the 'flopping' part of the ragdoll.")]
        [SerializeField, Min(0f)] private float limpSwayMultiplier = 2.5f;

        [FoldoutGroup("Hit Reactions")]
        [Tooltip("Seconds spent easing back to normal after EITHER reaction window expires — " +
                 "the shared recovery knob for limp and frozen pose alike.\n\n" +
                 "The bend limit and straightening tighten across this window, so the creature " +
                 "visibly gathers itself instead of snapping rigid in one frame. Coming out of a " +
                 "frozen pose it also eases the head back to its travel facing, since a held " +
                 "pose can drift far from where the creature ended up heading.")]
        [SerializeField, Min(0f)] private float limpRecoverDuration = 0.35f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float DebugSpeed => Application.isPlaying ? Speed : 0f;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float DebugLimpWeight => LimpWeight;

        // =====================
        // Public state
        // =====================

        /** The solved chain — renderers and creature brains read points from here. World space. */
        public ProcChain Chain { get; private set; }

        /** Number of points in the chain. */
        public int PointCount => pointCount;

        /** World-space distance between consecutive points, hierarchy scale included. */
        public float SegmentLength => segmentLength * HierarchyScale;

        /**
         * Uniform scale factor from the transform hierarchy. All world-unit dimensions
         * (segment length, wave/sway amplitudes, droop) are authored at scale 1 and
         * multiplied by this, so scaling the creature's transform — or any parent —
         * resizes the whole simulation. Flip scales (negative axes) are size-neutral;
         * non-uniform scales average the two axes, since a world-space chain has no
         * stable local axis to stretch along.
         */
        public float HierarchyScale
        {
            get
            {
                Vector3 s = transform.lossyScale;
                return (Mathf.Abs(s.x) + Mathf.Abs(s.y)) * 0.5f;
            }
        }

        /** Smoothed anchor velocity estimated from position deltas (world units/sec). */
        public Vector2 AnchorVelocity { get; private set; }

        /** Smoothed anchor speed — drivers and renderers can key intensity off this. */
        public float Speed { get; private set; }

        /** Direction the head currently faces (unit vector). */
        public Vector2 Facing { get; private set; } = Vector2.right;

        /** Runtime gain on wave frequency — creature brains push this up for excitement states (strike, panic). */
        public float WaveFrequencyMultiplier { get; set; } = 1f;

        /** Runtime gain on wave amplitude — pairs with the frequency multiplier for agitated swimming. */
        public float WaveAmplitudeMultiplier { get; set; } = 1f;

        /** 0 = normal, 1 = fully ragdolled. Eases back to 0 after a Limp() window expires. */
        public float LimpWeight { get; private set; }

        /** True while any limp is still in effect, including the recovery ease-out. */
        public bool IsLimp => LimpWeight > 0.001f;

        /** True while FreezePose() is holding the shape rigid. */
        public bool IsPoseFrozen { get; private set; }

        private Vector2 _lastAnchorPos;
        private float _wavePhase;
        private float _noiseSeed;

        // Hit-reaction windows. While the limp window is open the weight pins at 1 and the
        // head stops imposing a facing; after it expires the weight eases out to 0.
        private float _limpEndTime;
        private float _freezeEndTime;
        private bool _limpWindowOpen;

        // Eases 1 → 0 after a frozen pose releases. Loosens the solve exactly like LimpWeight
        // does (but without touching the drivers, so the reaction keeps its stiff character)
        // and eases the head back to its travel facing.
        private float _freezeRecoverWeight;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Per-instance noise seed so a cluster of identical creatures doesn't sway in unison.
            _noiseSeed = Random.value * 1000f;
            EnsureChain();
        }

        private void OnEnable()
        {
            EnsureChain();
            SnapToAnchor();
        }

        private void OnValidate()
        {
            // Domain reload is disabled in this project — rebuild the chain when
            // inspector counts change so stale arrays never survive a tweak.
            if (Chain != null && (Chain.Count != pointCount || !Application.isPlaying))
                Chain = null;
            EnsureChain();
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // ---- Anchor tracking: position, velocity estimate, facing ----
            Vector2 anchorPos = AnchorWorldPosition();
            Vector2 anchorDelta = anchorPos - _lastAnchorPos;
            Vector2 rawVel = anchorDelta / dt;
            _lastAnchorPos = anchorPos;

            // Exponential smoothing keeps physics jitter out of the animation drivers.
            AnchorVelocity = Vector2.Lerp(AnchorVelocity, rawVel, 1f - Mathf.Exp(-10f * dt));
            Speed = AnchorVelocity.magnitude;

            // ---- Hit reactions: advance the limp envelope and frozen-pose window ----
            UpdateReactionWindows(dt);

            // Frozen pose: hold the solved shape rigid and simply carry it along with the
            // anchor. Skipping the translate would leave the body behind while the creature
            // flies off, so "disabled simulation" still has to track the anchor.
            if (IsPoseFrozen)
            {
                TranslateChain(anchorDelta);
                return;
            }

            // Facing is held steady through the limp window — otherwise a knockback's reversed
            // velocity would be recorded as the new heading, which is the exact "it turned
            // around and swam off" artifact the limp exists to remove.
            if (!_limpWindowOpen) UpdateFacing(dt);

            // ---- Drivers: nudge points, then let the constraint pass shape it ----
            // Hierarchy scale resizes every world-unit dimension so scaled creatures keep their proportions.
            float scale = HierarchyScale;
            ApplyDrivers(dt, scale);

            // Both reactions relax the solve toward a floppy rope — looser joints, no
            // straightening — and tighten back over the shared recovery window. Only the limp
            // scales the drivers, so a frozen pose keeps its stiff character on the way out.
            float loosen = Mathf.Max(LimpWeight, _freezeRecoverWeight);
            float bendDegrees = Mathf.Lerp(maxBendDegrees, limpBendDegrees, loosen);
            float straightenRate = Mathf.Lerp(straightenSpeed, limpStraightenSpeed, loosen);

            // Straighten fraction converted from per-second rate to this step (framerate independent).
            float straighten = 1f - Mathf.Exp(-straightenRate * dt);

            // While the limp window is open the head imposes no facing at all — Solve falls back
            // to the chain's own current direction, so the body keeps the orientation it had and
            // just wobbles. Facing returns during the recovery ease-out, while the bend limit is
            // still loose, so realigning reads as gathering itself rather than a snap.
            Vector2 solveFacing = _limpWindowOpen || facing == FacingMode.None ? Vector2.zero : Facing;

            Chain.SegmentLength = segmentLength * scale;
            Chain.Solve(anchorPos, solveFacing, bendDegrees * Mathf.Deg2Rad, straighten);
        }

        /**
         * Advances the two hit-reaction windows.
         *
         * Limp snaps to full strength the instant it is triggered (a hit reaction should be
         * immediate), then eases back to 0 across limpRecoverDuration once the window expires.
         * The frozen pose is a plain on/off window with no easing.
         */
        private void UpdateReactionWindows(float dt)
        {
            bool freezeWindowOpen = Time.time < _freezeEndTime;

            // Falling edge: hand the frozen pose off to the shared recovery ease, so the solve
            // resumes loose and tightens back rather than snapping to whatever the restored
            // facing demands in a single frame.
            if (IsPoseFrozen && !freezeWindowOpen) _freezeRecoverWeight = 1f;
            IsPoseFrozen = freezeWindowOpen;

            if (!IsPoseFrozen && _freezeRecoverWeight > 0f)
            {
                _freezeRecoverWeight = limpRecoverDuration > 0f
                    ? Mathf.MoveTowards(_freezeRecoverWeight, 0f, dt / limpRecoverDuration)
                    : 0f;
            }

            _limpWindowOpen = Time.time < _limpEndTime;
            if (_limpWindowOpen)
            {
                LimpWeight = 1f;
            }
            else if (LimpWeight > 0f)
            {
                LimpWeight = limpRecoverDuration > 0f
                    ? Mathf.MoveTowards(LimpWeight, 0f, dt / limpRecoverDuration)
                    : 0f;
            }
        }

        /**
         * Adopts the resolved facing, easing into it while recovering from a frozen pose.
         *
         * A freeze holds the body completely static while the creature keeps moving, so by the
         * time it releases, the travel direction can point somewhere quite different from where
         * the body is aimed. Adopting that in one frame is what reads as popping into a new
         * pose, so the recovery blends there across limpRecoverDuration instead. The limp path
         * doesn't need this — it keeps solving throughout, so its pose never diverges as far.
         */
        private void UpdateFacing(float dt)
        {
            Vector2 resolved = ResolveFacing();

            // Ease rate derived from the shared recovery window, so one knob tunes the handoff.
            // The 3x lands the blend most of the way by the time the window closes.
            if (_freezeRecoverWeight > 0f && limpRecoverDuration > 0f)
            {
                float t = 1f - Mathf.Exp(-(3f / limpRecoverDuration) * dt);
                Vector2 eased = Vector2.Lerp(Facing, resolved, t);
                Facing = eased.sqrMagnitude > 1e-6f ? eased.normalized : resolved;
                return;
            }

            Facing = resolved;
        }

        /** Rigidly shifts every chain point, preserving the pose. Used while the pose is frozen. */
        private void TranslateChain(Vector2 delta)
        {
            if (Chain == null) return;

            Vector2[] points = Chain.Points;
            for (int i = 0; i < points.Length; i++)
                points[i] += delta;
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /** World position of chain point i (0 = head). */
        public Vector2 GetPoint(int i) => Chain.Points[i];

        /** Smoothed world-space tangent at point i (head → tail direction). */
        public Vector2 GetTangent(int i) => Chain.TangentAt(i);

        /** World-space left-perpendicular at point i — the ribbon width axis. */
        public Vector2 GetNormal(int i) => Chain.NormalAt(i);

        /** IProcPointSource: rest pose = the chain laid straight behind the anchor. */
        public void PrepareEditorPreview() => SnapToAnchor();

        /**
         * Goes limp (ragdoll) for a duration — the body stops imposing a facing, joints
         * loosen, the swim wave cuts out and the noise sway takes over, so it hangs and
         * wobbles instead of looking like it deliberately turned and swam off.
         *
         * Designed for hit reactions: 0.3-0.5s reads well for a knockback. Takes a plain
         * float, so it wires straight to any UnityEvent (HitReceiver.onHit, Health.onDamaged)
         * with the duration typed into the Inspector. Repeat calls extend the window
         * rather than cutting it short, so a flurry of hits keeps the body loose.
         *
         * Tune the character of it with the Hit Reactions settings; recovery eases out
         * over limpRecoverDuration after the window closes.
         */
        public void Limp(float duration)
        {
            _limpEndTime = Mathf.Max(_limpEndTime, Time.time + Mathf.Max(0f, duration));
            LimpWeight = 1f;
            _limpWindowOpen = true;
        }

        /** Closes the limp window early. The weight still eases out rather than snapping back. */
        public void EndLimp()
        {
            _limpEndTime = 0f;
            _limpWindowOpen = false;
        }

        /**
         * Freezes the chain's pose for a duration — the stiffer alternative to Limp().
         * The solved shape is held rigid and simply carried along with the anchor, so the
         * creature reads as briefly stunned-stiff rather than floppy. No drivers run.
         *
         * Same wiring story as Limp(): a plain float parameter for UnityEvent use.
         * Repeat calls extend the window.
         */
        public void FreezePose(float duration)
        {
            _freezeEndTime = Mathf.Max(_freezeEndTime, Time.time + Mathf.Max(0f, duration));
            IsPoseFrozen = true;
        }

        /**
         * Releases a frozen pose early. Simulation resumes from wherever the chain sits and
         * eases back through the same recovery as a naturally expiring freeze.
         */
        public void EndFreeze()
        {
            if (IsPoseFrozen) _freezeRecoverWeight = 1f;
            _freezeEndTime = 0f;
            IsPoseFrozen = false;
        }

        /**
         * Instantly lays the chain straight behind the anchor. Call after teleporting
         * the creature or re-enabling from culling so the body doesn't whip across the screen.
         */
        [FoldoutGroup("Debug")]
        [Button("Snap To Anchor")]
        public void SnapToAnchor()
        {
            // Lazily (re)build the chain so edit-mode renderers can preview without Awake having run.
            if (Chain == null || Chain.Count != pointCount)
                Chain = new ProcChain(pointCount, segmentLength);

            // A teleport / cull-restore invalidates any in-flight hit reaction, recovery
            // included — the chain is being laid out fresh, so there is nothing to ease from.
            EndLimp();
            EndFreeze();
            LimpWeight = 0f;
            _freezeRecoverWeight = 0f;

            Vector2 anchorPos = AnchorWorldPosition();
            _lastAnchorPos = anchorPos;
            AnchorVelocity = Vector2.zero;
            Speed = 0f;
            Facing = ResolveFacing();
            Chain.SegmentLength = SegmentLength;
            Chain.Teleport(anchorPos, facing == FacingMode.None ? Vector2.down : -Facing);
        }

        // -------------------------------------------------------
        // Internals
        // -------------------------------------------------------

        private void EnsureChain()
        {
            if (Chain == null || Chain.Count != pointCount)
                SnapToAnchor();
        }

        private Vector2 AnchorWorldPosition()
        {
            Transform a = anchor != null ? anchor : transform;

            // Explicit anchor + toggle: pin at this object's own world position, with Anchor
            // Offset added in the anchor's space — the head is placeable by moving this
            // GameObject in the scene view while still tracking the anchor's motion.
            if (applyLocalPositionOffset && HasExplicitAnchor)
                return (Vector2)transform.position + (Vector2)a.TransformVector(anchorOffset);

            return a.TransformPoint(anchorOffset);
        }

        private Vector2 ResolveFacing()
        {
            Transform a = anchor != null ? anchor : transform;
            switch (facing)
            {
                case FacingMode.TransformRight: return a.right;
                case FacingMode.TransformUp: return a.up;
                case FacingMode.None: return Facing;
                default:
                    // Velocity facing holds the last heading below a small speed floor
                    // so a paused creature doesn't spin from jitter. Below the floor we keep
                    // the stored facing, which is already sign-corrected from a previous frame.
                    if (Speed <= 0.15f) return Facing;

                    // ReverseVelocity flips the heading: the head aims backwards, so the chain
                    // (which lays out opposite the facing) trails out ahead of the travel direction.
                    Vector2 heading = AnchorVelocity / Speed;
                    return facing == FacingMode.ReverseVelocity ? -heading : heading;
            }
        }

        /**
         * Applies the traveling swim wave, perlin sway, and constant force as raw
         * point displacements. Displacements are applied along each point's current
         * normal (sideways), which the following constraint pass converts into
         * clean S-curves — e.g. a 0.1 amplitude at waveLength 2 over a 3-unit chain
         * yields about one and a half visible body waves.
         *
         * 'scale' is the hierarchy scale factor: amplitudes and the droop force are
         * lengths, so they scale with the creature to keep the motion proportional.
         */
        private void ApplyDrivers(float dt, float scale)
        {
            _wavePhase += waveFrequency * WaveFrequencyMultiplier * Mathf.PI * 2f * dt;

            // Speed-coupled amplitude: idle base + per-speed gain, capped.
            // The limp scale is applied after the cap — a ragdolled body stops driving
            // its swim wave, which is what stops it reading as deliberate swimming.
            float amp = Mathf.Min(
                (idleWaveAmplitude + Speed * waveAmplitudePerSpeed) * WaveAmplitudeMultiplier,
                maxWaveAmplitude) * Mathf.Lerp(1f, limpWaveMultiplier, LimpWeight) * scale;

            // Sway goes the other way — loose wobble takes over as the wave drops out.
            float swayAmp = swayAmplitude * Mathf.Lerp(1f, limpSwayMultiplier, LimpWeight) * scale;
            float noiseT = Time.time * swayFrequency + _noiseSeed;

            var points = Chain.Points;
            for (int i = 1; i < points.Length; i++)
            {
                float along01 = i / (float)(points.Length - 1);
                Vector2 normal = Chain.NormalAt(i);

                // Traveling wave: phase marches backward along the body so crests flow head → tail.
                // Wave spatial term stays unscaled: hierarchy scale would multiply distAlong AND
                // waveLength equally, so the number of body waves is scale-invariant either way.
                float wave = 0f;
                if (amp > 0f)
                {
                    float distAlong = i * segmentLength;
                    wave = Mathf.Sin(_wavePhase - distAlong * (Mathf.PI * 2f / waveLength))
                           * amp * waveRamp.Evaluate(along01);
                }

                // Perlin sway: -1..1 noise, unique per point and per instance.
                float sway = 0f;
                if (swayAmp > 0f)
                    sway = (Mathf.PerlinNoise(noiseT, _noiseSeed + i * 0.35f) - 0.5f) * 2f * swayAmp;

                // Sideways drivers displace along the normal; constant force is plain world drift.
                points[i] += normal * ((wave + sway) * dt * 60f * 0.1f)
                             + constantForce * (forceRamp.Evaluate(along01) * dt * scale);
            }
        }

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Tooltip("Duration used by the Test Limp / Test Freeze buttons below.")]
        [SerializeField, Min(0f)] private float debugReactionDuration = 0.4f;

        /** Fires a limp so the ragdoll look can be judged without landing a real hit. */
        [FoldoutGroup("Debug")]
        [Button("Test Limp"), GUIColor(1f, 0.8f, 0.6f)]
        private void DebugLimp()
        {
            if (!Application.isPlaying) { Debug.Log("[ChainSimulator] Play mode only."); return; }
            Limp(debugReactionDuration);
        }

        /** Fires a frozen pose — the stiff alternative, for side-by-side comparison. */
        [FoldoutGroup("Debug")]
        [Button("Test Freeze Pose"), GUIColor(0.6f, 0.8f, 1f)]
        private void DebugFreeze()
        {
            if (!Application.isPlaying) { Debug.Log("[ChainSimulator] Play mode only."); return; }
            FreezePose(debugReactionDuration);
        }
#endif

        private void OnDrawGizmosSelected()
        {
            // Rest-pose preview in edit mode; live chain in play mode.
            Gizmos.color = new Color(0.3f, 1f, 0.8f, 0.8f);
            if (Application.isPlaying && Chain != null)
            {
                for (int i = 1; i < Chain.Count; i++)
                    Gizmos.DrawLine(Chain.Points[i - 1], Chain.Points[i]);
            }
            else
            {
                Transform a = anchor != null ? anchor : transform;
                Vector2 head = AnchorWorldPosition(); // includes the local-position offset toggle
                Vector2 back = -(Vector2)a.right;
                float seg = SegmentLength; // scaled, so the preview matches the runtime chain size
                for (int i = 0; i < pointCount - 1; i++)
                    Gizmos.DrawLine(head + back * (seg * i), head + back * (seg * (i + 1)));
            }
        }
    }
}
