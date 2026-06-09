using System;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Core.Rendering
{
    /**
     * Drives the Submachina/UnderwaterDistortion fullscreen shader by pushing all of
     * its parameters as GLOBAL shader uniforms each frame.
     *
     * The actual render injection is done by URP's built-in "Full Screen Pass Renderer
     * Feature" (added to Renderer2D.asset, pointed at a material using that shader); this
     * component never touches the render pass. It owns two things:
     *   1. The ambient flow settings (the gentle whole-screen undulation).
     *   2. A fixed pool of active ripples, fed by DistortionRippleBus.Emit().
     *
     * Runs in edit mode ([ExecuteAlways]) so the effect is visible without entering Play
     * and so EditorCapture stills are deterministic via the manual-time override.
     */
    [ExecuteAlways]
    public class UnderwaterDistortionController : MonoBehaviour
    {
        // Must match UD_MAX_RIPPLES / UD_MAX_WAKES in UnderwaterDistortion.shader.
        private const int MaxRipples = 16;
        private const int MaxWakes = 8;

        /** The single live controller, so emitters/tools can find it without a scene ref. */
        public static UnderwaterDistortionController Instance { get; private set; }

        // ─── Master ──────────────────────────────────────────────────────────
        [TitleGroup("Master")]
        [Tooltip("Master on/off for the whole effect. Off leaves the scene undistorted.")]
        [ToggleLeft]
        public bool globalEnable = true;

        [TitleGroup("Master")]
        [Tooltip("Camera used to project ripple world positions to the screen. Empty = Camera.main.")]
        public Camera targetCamera;

        // ─── Ambient flow ────────────────────────────────────────────────────
        [TitleGroup("Ambient Flow")]
        [Tooltip("Peak UV displacement of the whole-screen undulation. Small values (0.002–0.02) read as subtle water; crank it up to verify in a still.")]
        [Range(0f, 0.1f)]
        public float ambientAmplitude = 0.006f;

        [TitleGroup("Ambient Flow")]
        [Tooltip("Spatial frequency of the ambient waves — higher = more, tighter ripples across the screen.")]
        [Range(0.1f, 8f)]
        public float ambientScale = 2.2f;

        [TitleGroup("Ambient Flow")]
        [Tooltip("How fast the ambient undulation scrolls/animates.")]
        [Range(0f, 40f)]
        public float ambientSpeed = 0.6f; 

        // ─── Noise texture (optional) ────────────────────────────────────────
        [TitleGroup("Noise Texture", "Optional")]
        [InfoBox("Blend a scrolling tiling noise into the ambient flow for a more organic look. Assign the texture to _UD_NoiseTex on the material.")]
        [Tooltip("0 = pure procedural sine flow, 1 = pure noise-texture flow. Blends between them.")]
        [Range(0f, 1f)]
        public float noiseBlend = 0f;

        [TitleGroup("Noise Texture")]
        [Tooltip("Tiling scale of the noise texture sample.")]
        [Range(0.1f, 8f)]
        public float noiseScale = 1.5f;

        [TitleGroup("Noise Texture")]
        [Tooltip("Scroll speed of the noise texture.")]
        [Range(0f, 4f)]
        public float noiseSpeed = 0.4f;

        // ─── Refraction (light bending) ──────────────────────────────────────
        [TitleGroup("Refraction")]
        [Tooltip("How much light splits into RGB along the distortion — the 'bent light' look. 0 = off.")]
        [Range(0f, 8f)]
        public float chromaticAberration = 2.5f;

        // ─── God rays ────────────────────────────────────────────────────────
        [TitleGroup("God Rays", "Light shafts angling down from the surface.")]
        [Tooltip("Overall brightness of the god-ray shafts. 0 = off.")]
        [Range(0f, 2f)]
        public float godRayIntensity = 0.35f;

        [TitleGroup("God Rays")]
        [Tooltip("How many shafts across the screen (noise frequency across the ray direction).")]
        [Range(0.5f, 20f)]
        public float godRayDensity = 4f;

        [TitleGroup("God Rays")]
        [Tooltip("Shaft contrast/thinness — higher = sharper, more separated beams.")]
        [Range(1f, 16f)]
        public float godRaySharpness = 7f;

        [TitleGroup("God Rays")]
        [Tooltip("How fast the shafts drift/shimmer.")]
        [Range(0f, 1f)]
        public float godRaySpeed = 0.05f;

        [TitleGroup("God Rays")]
        [Tooltip("Horizontal lean of the shafts. 0 = straight down; +/- angles them.")]
        [Range(-1f, 1f)]
        public float godRayAngle = 0.15f;

        [TitleGroup("God Rays")]
        [Tooltip("Color of the shafts (HDR — push past white for a bloom-friendly glow).")]
        [ColorUsage(false, true)]
        public Color godRayTint = new Color(0.55f, 0.8f, 1f);

        // ─── Caustics ────────────────────────────────────────────────────────
        [TitleGroup("Caustics", "Animated light webs that sparkle across surfaces.")]
        [Tooltip("Overall brightness of the caustics. 0 = off.")]
        [Range(0f, 2f)]
        public float causticIntensity = 0.45f;

        [TitleGroup("Caustics")]
        [Tooltip("Tiling scale of the caustic cells — higher = smaller, denser cells.")]
        [Range(0.01f, 14f)]
        public float causticScale = 3.5f;

        [TitleGroup("Caustics")]
        [Tooltip("Animation speed of the caustic shimmer.")]
        [Range(0f, 1f)]
        public float causticSpeed = 0.09f;

        [TitleGroup("Caustics")]
        [Tooltip("Contrast of the cell highlights — higher = thinner, brighter veins.")]
        [Range(0.5f, 20f)]
        public float causticSharpness = 1.6f;

        [TitleGroup("Caustics")]
        [Tooltip("Animated domain warp — makes the caustic web MORPH/pulse in place instead of sliding flatly. The main 'alive' control.")]
        [Range(0f, 1f)]
        public float causticWarp = 0.18f;

        [TitleGroup("Caustics")]
        [Tooltip("How much the water distortion bends the caustics, tying them to the same wobble as the scene.")]
        [Range(0f, 6f)]
        public float causticDistort = 1.5f;

        [TitleGroup("Caustics")]
        [Tooltip("How strongly caustics cling to lit surfaces vs. open water (higher = mostly on objects).")]
        [Range(0f, 20f)]
        public float causticSurfaceMask = 1.2f;

        [TitleGroup("Caustics")]
        [Tooltip("Baseline caustic visibility in open (dark) water.")]
        [Range(0f, 1f)]
        public float causticOpenWater = 0.12f;

        [TitleGroup("Caustics")]
        [Tooltip("Color of the caustics (HDR).")]
        [ColorUsage(false, true)]
        public Color causticTint = new Color(0.65f, 0.95f, 1f);

        // ─── Deep tint ───────────────────────────────────────────────────────
        [TitleGroup("Deep Tint", "Optional color grade; leave strength 0 if you grade via a Volume.")]
        [Tooltip("Multiplicative deep-water color.")]
        public Color deepTint = new Color(0.45f, 0.75f, 0.85f);

        [TitleGroup("Deep Tint")]
        [Tooltip("How strongly to apply the deep tint. 0 = off.")]
        [Range(0f, 1f)]
        public float deepTintStrength = 0f;

        // ─── Wake shape ──────────────────────────────────────────────────────
        [TitleGroup("Wake Shape", "Turbulence trails from propulsion (see PropulsionWakeEmitter).")]
        [Tooltip("Length of the turbulence plume along travel, in viewport units.")]
        [Range(0.02f, 0.5f)]
        public float wakeHalfLength = 0.12f;

        [TitleGroup("Wake Shape")]
        [Tooltip("Turbulence frequency inside the wake — higher = finer churn.")]
        [Range(2f, 40f)]
        public float wakeFrequency = 14f;

        // ─── Wake defaults (test button + emitter fallback) ──────────────────
        [TitleGroup("Wake Defaults")]
        [Tooltip("Peak displacement strength of a test wake.")]
        [Range(0f, 0.2f)]
        public float wakeDefaultStrength = 0.05f;

        [TitleGroup("Wake Defaults")]
        [Tooltip("Seconds until a test wake fully fades.")]
        [Range(0.1f, 4f)]
        public float wakeDefaultLifetime = 0.9f;

        // ─── Ripple shape ────────────────────────────────────────────────────
        [TitleGroup("Ripple Shape")]
        [Tooltip("How fast a ripple ring expands outward, in viewport units per second (1 ≈ full screen height).")]
        [Range(0.05f, 2f)]
        public float ringExpansionSpeed = 0.4f;

        [TitleGroup("Ripple Shape")]
        [Tooltip("Width of the displaced ring band in viewport units. Larger = softer, fatter ring.")]
        [Range(0.01f, 0.5f)]
        public float ringFalloff = 0.08f;

        // ─── Ripple defaults (used by the test buttons) ──────────────────────
        [TitleGroup("Ripple Defaults", "Parameters used by the test buttons below; gameplay emitters pass their own.")]
        [Tooltip("Peak displacement amplitude of a test ripple.")]
        [Range(0f, 0.2f)]
        public float defaultStrength = 0.04f;

        [TitleGroup("Ripple Defaults")]
        [Tooltip("Number of wave cycles packed into the ring.")]
        [Range(1f, 30f)]
        public float defaultFrequency = 10f;

        [TitleGroup("Ripple Defaults")]
        [Tooltip("Oscillation (phase) speed of a test ripple — higher feels faster/jolting.")]
        [Range(0f, 30f)]
        public float defaultSpeed = 12f;

        [TitleGroup("Ripple Defaults")]
        [Tooltip("Seconds until a test ripple fully fades out.")]
        [Range(0.1f, 6f)]
        public float defaultLifetime = 2f;

        // ─── Edit-mode capture ───────────────────────────────────────────────
        [TitleGroup("Edit-Mode Capture", "Freeze the animation clock so EditorCapture produces a deterministic, repeatable still.")]
        [Tooltip("When on (in edit mode), the effect uses Manual Time instead of the live editor clock.")]
        [ToggleLeft]
        public bool manualTimeOverride = false;

        [TitleGroup("Edit-Mode Capture")]
        [ShowIf(nameof(manualTimeOverride))]
        [Tooltip("The frozen clock value. Emit a ripple at a low value, then raise this to watch the ring expand in successive captures.")]
        public float manualTime = 0f;

        // ─── Debug readout ───────────────────────────────────────────────────
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        public int LiveRippleCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _ripples.Length; i++) if (_ripples[i].active) n++;
                return n;
            }
        }

        // ── Internal state ──────────────────────────────────────────────────
        // Fixed-size ripple pool; the GPU arrays are always uploaded at full length so
        // Unity never caches a shorter array length (a known SetGlobalVectorArray pitfall).
        private Ripple[] _ripples = new Ripple[MaxRipples];
        private readonly Vector4[] _rippleA = new Vector4[MaxRipples];
        private readonly Vector4[] _rippleB = new Vector4[MaxRipples];

        // Wake pool (turbulence trails), packed the same way the ripple pool is.
        private Wake[] _wakes = new Wake[MaxWakes];
        private readonly Vector4[] _wakeA = new Vector4[MaxWakes];
        private readonly Vector4[] _wakeB = new Vector4[MaxWakes];

        // Cached shader property ids (per project convention).
        private static readonly int _idTime          = Shader.PropertyToID("_UD_Time");
        private static readonly int _idFlowParams    = Shader.PropertyToID("_UD_FlowParams");
        private static readonly int _idFlowParams2   = Shader.PropertyToID("_UD_FlowParams2");
        private static readonly int _idAspect        = Shader.PropertyToID("_UD_Aspect");
        private static readonly int _idChromatic     = Shader.PropertyToID("_UD_Chromatic");
        private static readonly int _idGodRayParams  = Shader.PropertyToID("_UD_GodRayParams");
        private static readonly int _idGodRayDir     = Shader.PropertyToID("_UD_GodRayDir");
        private static readonly int _idGodRayTint    = Shader.PropertyToID("_UD_GodRayTint");
        private static readonly int _idCausticParams = Shader.PropertyToID("_UD_CausticParams");
        private static readonly int _idCausticParams2 = Shader.PropertyToID("_UD_CausticParams2");
        private static readonly int _idCausticTint   = Shader.PropertyToID("_UD_CausticTint");
        private static readonly int _idCausticMask   = Shader.PropertyToID("_UD_CausticMask");
        private static readonly int _idDeepTint      = Shader.PropertyToID("_UD_DeepTint");
        private static readonly int _idRippleA       = Shader.PropertyToID("_UD_RippleA");
        private static readonly int _idRippleB       = Shader.PropertyToID("_UD_RippleB");
        private static readonly int _idRippleCount   = Shader.PropertyToID("_UD_RippleCount");
        private static readonly int _idWakeA         = Shader.PropertyToID("_UD_WakeA");
        private static readonly int _idWakeB         = Shader.PropertyToID("_UD_WakeB");
        private static readonly int _idWakeCount     = Shader.PropertyToID("_UD_WakeCount");

        /** A single live ripple in the pool. */
        private struct Ripple
        {
            public bool active;
            public Vector3 worldPos;
            public float startTime;
            public float strength;
            public float frequency;
            public float speed;
            public float lifetime;
        }

        /** A single live wake (turbulence trail) in the pool. */
        private struct Wake
        {
            public bool active;
            public Vector3 worldPos;
            public Vector3 worldDir;   // travel direction in world space
            public float startTime;
            public float strength;
            public float halfLength;
            public float frequency;
            public float lifetime;
        }

        /**
         * The animation clock. Play mode uses real time; edit mode uses the frozen
         * manual value when override is on, else the live editor clock. Driving both
         * the ambient flow and ripple ages from one source keeps everything in sync.
         */
        private float CurrentTime
        {
            get
            {
                if (Application.isPlaying) return Time.time;
#if UNITY_EDITOR
                if (manualTimeOverride) return manualTime;
                return (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
                return Time.time;
#endif
            }
        }

        // ─── Lifecycle ───────────────────────────────────────────────────────

        /** Claim the singleton slot and start listening for ripple requests. */
        private void OnEnable()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("[UnderwaterDistortion] Multiple controllers active; the newest one wins.", this);

            Instance = this;
            DistortionRippleBus.OnRipple += Enqueue;
            DistortionWakeBus.OnWake += EnqueueWake;
        }

        /** Release the singleton slot and stop listening. */
        private void OnDisable()
        {
            DistortionRippleBus.OnRipple -= Enqueue;
            DistortionWakeBus.OnWake -= EnqueueWake;
            if (Instance == this) Instance = null;
        }

        /** Push the latest parameters to the GPU every frame (edit and play). */
        private void Update()
        {
            UploadToGpu();
        }

        // ─── Emission ────────────────────────────────────────────────────────

        /**
         * Place an incoming ripple request into the pool. Uses the first free slot,
         * or steals the oldest ripple when the pool is full so the newest event always
         * shows. Stamped with the current clock so its envelope ages correctly.
         */
        private void Enqueue(RippleRequest r)
        {
            int slot = FindFreeOrOldestSlot();

            _ripples[slot] = new Ripple
            {
                active = true,
                worldPos = r.worldPos,
                startTime = CurrentTime,
                strength = r.strength,
                frequency = r.frequency,
                speed = r.speed,
                lifetime = Mathf.Max(0.01f, r.lifetime)
            };
        }

        /** Return a free pool slot, or the index of the oldest ripple if all are busy. */
        private int FindFreeOrOldestSlot()
        {
            int oldest = 0;
            float oldestStart = float.MaxValue;

            for (int i = 0; i < _ripples.Length; i++)
            {
                if (!_ripples[i].active) return i;
                if (_ripples[i].startTime < oldestStart)
                {
                    oldestStart = _ripples[i].startTime;
                    oldest = i;
                }
            }

            return oldest;
        }

        /**
         * Place an incoming wake request into the wake pool (first free slot, else steal
         * the oldest). A zero/near-zero direction is dropped — a wake needs a travel axis.
         */
        private void EnqueueWake(WakeRequest r)
        {
            if (r.worldDir.sqrMagnitude < 1e-6f) return;

            int slot = FindFreeOrOldestWakeSlot();

            _wakes[slot] = new Wake
            {
                active = true,
                worldPos = r.worldPos,
                worldDir = r.worldDir.normalized,
                startTime = CurrentTime,
                strength = r.strength,
                halfLength = r.length > 0f ? r.length : wakeHalfLength,
                frequency = r.frequency > 0f ? r.frequency : wakeFrequency,
                lifetime = Mathf.Max(0.01f, r.lifetime)
            };
        }

        /** Return a free wake slot, or the index of the oldest wake if all are busy. */
        private int FindFreeOrOldestWakeSlot()
        {
            int oldest = 0;
            float oldestStart = float.MaxValue;

            for (int i = 0; i < _wakes.Length; i++)
            {
                if (!_wakes[i].active) return i;
                if (_wakes[i].startTime < oldestStart)
                {
                    oldestStart = _wakes[i].startTime;
                    oldest = i;
                }
            }

            return oldest;
        }

        // ─── GPU upload ──────────────────────────────────────────────────────

        /**
         * Recompute every uniform and push it to the global shader state. Public so the
         * effect can be refreshed on demand (e.g. right before an EditorCapture) without
         * waiting for the next editor tick.
         */
        public void UploadToGpu()
        {
            float time = CurrentTime;

            // Scalar / vector parameters.
            Shader.SetGlobalFloat(_idTime, time);
            Shader.SetGlobalVector(_idFlowParams,
                new Vector4(ambientAmplitude, ambientScale, ambientSpeed, globalEnable ? 1f : 0f));
            Shader.SetGlobalVector(_idFlowParams2,
                new Vector4(noiseBlend, noiseScale, noiseSpeed, 0f));

            // Aspect correction keeps ripple rings circular on wide screens.
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            float aspect = cam != null ? cam.aspect : 1.7777f;
            Shader.SetGlobalVector(_idAspect, new Vector4(aspect, 1f, 0f, 0f));

            // Refraction + artificial-light parameters (Color implicitly converts to Vector4).
            Shader.SetGlobalFloat(_idChromatic, chromaticAberration);
            Vector2 grDir = new Vector2(godRayAngle, -1f).normalized;
            Shader.SetGlobalVector(_idGodRayParams, new Vector4(godRayIntensity, godRayDensity, godRaySharpness, godRaySpeed));
            Shader.SetGlobalVector(_idGodRayDir, new Vector4(grDir.x, grDir.y, 0f, 0f));
            Shader.SetGlobalVector(_idGodRayTint, godRayTint);
            Shader.SetGlobalVector(_idCausticParams, new Vector4(causticIntensity, causticScale, causticSpeed, causticSharpness));
            Shader.SetGlobalVector(_idCausticParams2, new Vector4(causticWarp, causticDistort, 0f, 0f));
            Shader.SetGlobalVector(_idCausticTint, causticTint);
            Shader.SetGlobalVector(_idCausticMask, new Vector4(causticSurfaceMask, causticOpenWater, 0f, 0f));
            Shader.SetGlobalVector(_idDeepTint, new Vector4(deepTint.r, deepTint.g, deepTint.b, deepTintStrength));

            // Pack live ripples to the front of the arrays, expire finished ones, zero the rest.
            int live = 0;
            for (int i = 0; i < _ripples.Length; i++)
            {
                ref Ripple rp = ref _ripples[i];
                if (!rp.active) continue;

                float t = time - rp.startTime;
                if (t < 0f || t > rp.lifetime) { rp.active = false; continue; }   // expired (or stale clock)

                // Project the world origin to viewport UV; cull if behind the camera.
                float u = Mathf.Clamp01(t / rp.lifetime);
                Vector3 vp = cam != null ? cam.WorldToViewportPoint(rp.worldPos) : new Vector3(0.5f, 0.5f, 1f);
                float visible = vp.z > 0f ? 1f : 0f;

                // Envelope: quick ramp-in (avoids a pop) then linear fade-out over the lifetime.
                float amplitude = rp.strength * (1f - u) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.1f)) * visible;
                float radius = ringExpansionSpeed * t;

                _rippleA[live] = new Vector4(vp.x, vp.y, radius, amplitude);
                _rippleB[live] = new Vector4(rp.frequency, ringFalloff, t * rp.speed, 1f);
                live++;
            }

            // Zero out the unused tail so the shader's active flag (w) reliably gates them.
            for (int i = live; i < MaxRipples; i++)
            {
                _rippleA[i] = Vector4.zero;
                _rippleB[i] = Vector4.zero;
            }

            Shader.SetGlobalVectorArray(_idRippleA, _rippleA);
            Shader.SetGlobalVectorArray(_idRippleB, _rippleB);
            Shader.SetGlobalInt(_idRippleCount, live);

            // Pack live wakes: viewport center + aspect-corrected travel direction.
            int wlive = 0;
            for (int i = 0; i < _wakes.Length; i++)
            {
                ref Wake wk = ref _wakes[i];
                if (!wk.active) continue;

                float t = time - wk.startTime;
                if (t < 0f || t > wk.lifetime) { wk.active = false; continue; }

                float u = Mathf.Clamp01(t / wk.lifetime);
                Vector3 vp0 = cam != null ? cam.WorldToViewportPoint(wk.worldPos) : new Vector3(0.5f, 0.5f, 1f);
                Vector3 vp1 = cam != null ? cam.WorldToViewportPoint(wk.worldPos + wk.worldDir) : new Vector3(0.6f, 0.5f, 1f);
                float visible = vp0.z > 0f ? 1f : 0f;

                // Travel direction in aspect-corrected viewport space (matches the shader's frame).
                Vector2 dirUv = new Vector2((vp1.x - vp0.x) * aspect, vp1.y - vp0.y);
                if (dirUv.sqrMagnitude < 1e-8f) dirUv = Vector2.right;
                dirUv.Normalize();

                // Envelope: quick ramp-in then fade; the plume grows a little as it dissipates.
                float strength = wk.strength * (1f - u) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.12f)) * visible;
                float halfLen = wk.halfLength * (1f + u * 0.6f);

                _wakeA[wlive] = new Vector4(vp0.x, vp0.y, strength, halfLen);
                _wakeB[wlive] = new Vector4(dirUv.x, dirUv.y, wk.frequency, 1f);
                wlive++;
            }

            for (int i = wlive; i < MaxWakes; i++)
            {
                _wakeA[i] = Vector4.zero;
                _wakeB[i] = Vector4.zero;
            }

            Shader.SetGlobalVectorArray(_idWakeA, _wakeA);
            Shader.SetGlobalVectorArray(_idWakeB, _wakeB);
            Shader.SetGlobalInt(_idWakeCount, wlive);
        }

        // ─── Test controls ───────────────────────────────────────────────────

        /** Emit a test ripple at the world origin using the default parameters. */
        [TitleGroup("Test Controls")]
        [Button("Emit Test Ripple At Origin")]
        public void EmitTestRippleAtOrigin()
        {
            DistortionRippleBus.Emit(Vector3.zero, defaultStrength, defaultFrequency, defaultSpeed, defaultLifetime);
            UploadToGpu();
        }

        /** Emit a test ripple at the center of the target camera's view. */
        [Button("Emit Test Ripple At Camera Center")]
        public void EmitTestRippleAtCameraCenter()
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            Vector3 center = cam != null
                ? cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, Mathf.Abs(cam.transform.position.z)))
                : Vector3.zero;

            DistortionRippleBus.Emit(center, defaultStrength, defaultFrequency, defaultSpeed, defaultLifetime);
            UploadToGpu();
        }

        /**
         * Emit a test wake across the middle of the view (moving right), so the turbulence
         * plume is easy to see. Emit at a low manual time, then raise it to watch it dissipate.
         */
        [Button("Emit Test Wake (Across View)")]
        public void EmitTestWake()
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            float zd = cam != null ? Mathf.Abs(cam.transform.position.z) : 10f;
            Vector3 pos = cam != null ? cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, zd)) : Vector3.zero;
            Vector3 dir = cam != null ? cam.transform.right : Vector3.right;

            DistortionWakeBus.Emit(pos, dir, wakeDefaultStrength, wakeHalfLength, wakeFrequency, wakeDefaultLifetime);
            UploadToGpu();
        }

        /** Clear every active ripple immediately. */
        [Button("Clear Ripples")]
        public void ClearRipples()
        {
            for (int i = 0; i < _ripples.Length; i++) _ripples[i].active = false;
            UploadToGpu();
        }

        /** Clear every active wake immediately. */
        [Button("Clear Wakes")]
        public void ClearWakes()
        {
            for (int i = 0; i < _wakes.Length; i++) _wakes[i].active = false;
            UploadToGpu();
        }
    }
}
