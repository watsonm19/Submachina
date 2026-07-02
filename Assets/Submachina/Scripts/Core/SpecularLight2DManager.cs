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

        private static readonly int CountID = Shader.PropertyToID("_SpecLightCount");
        private static readonly int LightAID = Shader.PropertyToID("_SpecLightA");
        private static readonly int LightBID = Shader.PropertyToID("_SpecLightB");

        private static readonly List<SpecularLight2D> Lights = new List<SpecularLight2D>();
        private static SpecularLight2DManager _instance;

        // Reused per-frame scratch (fixed length = the shader's array length).
        private static readonly Vector4[] PackedA = new Vector4[MaxLights];
        private static readonly Vector4[] PackedB = new Vector4[MaxLights];

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

        /** Lazily spawn a hidden, scene-persistent manager on first registration. */
        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("SpecularLight2DManager") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SpecularLight2DManager>();
        }

        /**
         * Pack up to MaxLights active lights into the global arrays. Skips null/disabled
         * lights and any that report no usable reach, then publishes the count so the
         * shader only iterates real entries.
         */
        private void LateUpdate()
        {
            int written = 0;
            for (int i = 0; i < Lights.Count && written < MaxLights; i++)
            {
                var light = Lights[i];
                if (light == null || !light.isActiveAndEnabled) continue;
                if (!light.TryPack(out Vector4 a, out Vector4 b)) continue;

                PackedA[written] = a;
                PackedB[written] = b;
                written++;
            }

            Shader.SetGlobalVectorArray(LightAID, PackedA);
            Shader.SetGlobalVectorArray(LightBID, PackedB);
            Shader.SetGlobalFloat(CountID, written);
        }
    }
}
