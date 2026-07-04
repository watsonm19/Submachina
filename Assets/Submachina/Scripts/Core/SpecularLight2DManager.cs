using System.Collections.Generic;
using UnityEngine;

namespace Submachina.Core
{
    /**
     * Collects every active SpecularLight2D and pushes them into GLOBAL shader uniforms
     * once per frame, so the SpriteLitSpecular shader can compute glints per-pixel on the
     * GPU against the real lights.
     *
     * Why a single global-writer instead of per-sprite work: there are only a few lights
     * (one torch per sub) but potentially hundreds of lit sprites. Writing a small fixed
     * array of lights ONCE per frame is dramatically cheaper than any per-sprite scan, and
     * the shader loop over MAX lights is trivial GPU cost. Because the uniforms are global
     * (outside UnityPerMaterial), the SRP Batcher is unaffected.
     *
     * The manager is created automatically the first time a light registers — nothing to
     * place in the scene. It survives scene loads and packs in LateUpdate so it captures
     * the lights' final transforms for the frame.
     */
    [DefaultExecutionOrder(10000)] // after lights/subs have moved for the frame
    public class SpecularLight2DManager : MonoBehaviour
    {
        // Must match MAX_SPEC_LIGHTS in SpriteLitSpecular.shader.
        private const int MaxLights = 4;

        // Distance resolution of each falloff LUT row (128 half-float texels is smooth + tiny).
        private const int LutWidth = 128;

        private static readonly int CountID = Shader.PropertyToID("_SpecLightCount");
        private static readonly int LightAID = Shader.PropertyToID("_SpecLightA");
        private static readonly int LightBID = Shader.PropertyToID("_SpecLightB");
        private static readonly int FalloffLUTID = Shader.PropertyToID("_SpecFalloffLUT");

        private static readonly List<SpecularLight2D> Lights = new List<SpecularLight2D>();
        private static SpecularLight2DManager _instance;

        // Reused per-frame scratch (fixed length = the shader's array length).
        private static readonly Vector4[] PackedA = new Vector4[MaxLights];
        private static readonly Vector4[] PackedB = new Vector4[MaxLights];

        // Per-light falloff LUT (one ROW per light slot) + who/what each row currently holds, so we
        // only rebake a row when the light occupying that slot (or its curve) actually changes.
        private Texture2D _lut;
        private readonly SpecularLight2D[] _rowLight = new SpecularLight2D[MaxLights];
        private readonly int[] _rowVersion = new int[MaxLights];
        private readonly Color[] _rowScratch = new Color[LutWidth];

        /**
         * Zero the light count at startup so sprites never read stale/garbage globals before
         * any light has registered (e.g. a scene with lit sprites but no SpecularLight2D yet).
         */
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetGlobals()
        {
            Lights.Clear();
            _instance = null;
            Shader.SetGlobalFloat(CountID, 0f);
        }

        /** Add a light to the driven set and make sure a manager exists to tick it. */
        public static void Register(SpecularLight2D light)
        {
            if (light == null || Lights.Contains(light)) return;
            Lights.Add(light);
            EnsureInstance();
        }

        /** Remove a light; when the last one leaves, the globals fall back to zero lights. */
        public static void Unregister(SpecularLight2D light)
        {
            Lights.Remove(light);
            if (Lights.Count == 0) Shader.SetGlobalFloat(CountID, 0f);
        }

        /**
         * Lazily spawn a scene-persistent manager on first registration. Deliberately a PLAIN
         * object (no HideAndDontSave): it must die when play mode ends. HideAndDontSave managers
         * leaked into the editor, and on the next session their non-serialized _lut was gone
         * while Awake never re-ran — every LateUpdate then threw UnassignedReferenceException.
         */
        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("SpecularLight2DManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SpecularLight2DManager>();
        }

        /** Bind the LUT before the first render (and again after domain reloads, which re-run OnEnable). */
        private void OnEnable() => EnsureLut();

        /**
         * Build + globally bind the shared falloff LUT (one row per light slot) if it's missing —
         * on first enable, after a mid-play domain reload wiped the non-serialized field, or if
         * the texture was destroyed externally. Rows are (re)filled lazily in LateUpdate.
         */
        private void EnsureLut()
        {
            if (_lut != null) return; // Unity's lifetime-aware null check: destroyed counts as missing

            _lut = new Texture2D(LutWidth, MaxLights, TextureFormat.RHalf, false, true)
            {
                name = "SpecFalloffLUT",
                filterMode = FilterMode.Bilinear, // smooth over distance; rows sampled at exact centers
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Shader.SetGlobalTexture(FalloffLUTID, _lut);

            // The new texture is blank — forget row occupants so LateUpdate rebakes every slot
            for (int i = 0; i < MaxLights; i++) _rowLight[i] = null;
        }

        /** Release the LUT with the manager and clear the singleton slot. */
        private void OnDestroy()
        {
            if (_lut != null) DestroyImmediate(_lut);
            if (_instance == this) _instance = null;
        }

        /**
         * Bake one light's falloff across normalized distance 0..1 into the given LUT row. Only
         * called when that row's occupant or its curve version changed — never per steady frame.
         */
        private void BakeRow(int row, SpecularLight2D light)
        {
            for (int x = 0; x < LutWidth; x++)
            {
                float t = x / (float)(LutWidth - 1);   // 0..1 normalized distance
                _rowScratch[x] = new Color(light.SampleFalloff(t), 0f, 0f, 1f);
            }
            _lut.SetPixels(0, row, LutWidth, 1, _rowScratch);
        }

        /**
         * Pack up to MaxLights active lights into the global arrays. Skips null/disabled
         * lights and any that report no usable reach, then publishes the count so the
         * shader only iterates real entries. Also refreshes each occupied LUT row when its
         * light (or that light's falloff curve) has changed, uploading at most once per frame.
         */
        private void LateUpdate()
        {
            // Stale/duplicate managers (e.g. ones leaked across play sessions by the old
            // HideAndDontSave setup) retire themselves instead of fighting over the globals.
            if (_instance != this) { Destroy(gameObject); return; }
            EnsureLut();

            int written = 0;
            bool lutDirty = false;
            for (int i = 0; i < Lights.Count && written < MaxLights; i++)
            {
                var light = Lights[i];
                if (light == null || !light.isActiveAndEnabled) continue;
                if (!light.TryPack(out Vector4 a, out Vector4 b)) continue;

                PackedA[written] = a;
                PackedB[written] = b;

                // Rebake this slot's falloff only if a different light now sits here or its curve changed.
                if (_rowLight[written] != light || _rowVersion[written] != light.FalloffVersion)
                {
                    BakeRow(written, light);
                    _rowLight[written] = light;
                    _rowVersion[written] = light.FalloffVersion;
                    lutDirty = true;
                }

                written++;
            }

            if (lutDirty) _lut.Apply(false); // single GPU upload, only on a frame that changed a row

            Shader.SetGlobalVectorArray(LightAID, PackedA);
            Shader.SetGlobalVectorArray(LightBID, PackedB);
            Shader.SetGlobalFloat(CountID, written);
        }
    }
}
