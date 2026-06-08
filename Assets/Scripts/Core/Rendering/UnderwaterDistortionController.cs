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
        // Must match UD_MAX_RIPPLES in UnderwaterDistortion.shader.
        private const int MaxRipples = 16;

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

        // Cached shader property ids (per project convention).
        private static readonly int _idTime        = Shader.PropertyToID("_UD_Time");
        private static readonly int _idFlowParams  = Shader.PropertyToID("_UD_FlowParams");
        private static readonly int _idFlowParams2 = Shader.PropertyToID("_UD_FlowParams2");
        private static readonly int _idAspect      = Shader.PropertyToID("_UD_Aspect");
        private static readonly int _idRippleA     = Shader.PropertyToID("_UD_RippleA");
        private static readonly int _idRippleB     = Shader.PropertyToID("_UD_RippleB");
        private static readonly int _idRippleCount = Shader.PropertyToID("_UD_RippleCount");

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
        }

        /** Release the singleton slot and stop listening. */
        private void OnDisable()
        {
            DistortionRippleBus.OnRipple -= Enqueue;
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

        /** Clear every active ripple immediately. */
        [Button("Clear Ripples")]
        public void ClearRipples()
        {
            for (int i = 0; i < _ripples.Length; i++) _ripples[i].active = false;
            UploadToGpu();
        }
    }
}
