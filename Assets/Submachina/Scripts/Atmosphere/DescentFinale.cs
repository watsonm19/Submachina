using System.Collections;
using Core.Modulation;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.Events;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Submachina.Core
{
    /**
     * The descent's final beat: a hard blackout and silence. Plays feedbacks and an
     * explosion event, forces the darkness parameter to its ceiling for the rest of
     * the scene, and fades the light target's multiplier all the way to 0 so it goes
     * dark even below whatever floor the environmental route would normally hold it to.
     *
     * One-shot — TriggerFinale() is ignored on repeat calls.
     */
    public class DescentFinale : MonoBehaviour
    {
        [Header("Director")]
        [Tooltip("Auto-resolved via EnvironmentDirector.FindFor() when left empty.")]
        [SerializeField] private EnvironmentDirector director;

        [Tooltip("Optional. When set, a Max-blend modifier forces this parameter to its authored ceiling, held forever.")]
        [SerializeField] private DirectorParameterDef darknessParameter;

        [Header("Light")]
        [Tooltip("Optional. Multiplier is lerped to 0 over blackoutFadeSeconds so the light dies completely.")]
        [SerializeField] private ModulatedFloatTarget lightTarget;

        [Tooltip("Duration (seconds) of the light-to-black fade.")]
        [SerializeField] private float blackoutFadeSeconds = 0.6f;

        [Header("Timing")]
        [Tooltip("Delay before the bang and blackout fire, for lining up with an animation or camera cue.")]
        [SerializeField] private float bangDelaySeconds = 0f;

        [Header("Feel")]
        [Tooltip("Played the instant the bang triggers (after bangDelaySeconds).")]
        [SerializeField] private MMF_Player[] feedbacks;

        [Header("Events")]
        [Tooltip("Raised at the bang — wire the explosion one-shot and ambience stop here.")]
        public UnityEvent onBang;

        [Tooltip("Raised once the light multiplier has reached 0.")]
        public UnityEvent onBlackoutComplete;

        private bool _triggered;

        private void OnEnable()
        {
            if (director == null) director = EnvironmentDirector.FindFor(this);
        }

        // ------------------------------------------------------------------ public API

        /** Fires the finale sequence once: bang + permanent darkness ceiling, then fades the light to black. */
#if ODIN_INSPECTOR
        [Button("Trigger Finale (test)")]
#endif
        public void TriggerFinale()
        {
            if (_triggered) return;
            _triggered = true;

            StartCoroutine(FinaleRoutine());
        }

        // ------------------------------------------------------------------ sequence

        /**
         * Waits bangDelaySeconds, then plays feedbacks and forces darkness to its ceiling
         * (held forever via an infinite-hold Max modifier), then fades the light target's
         * Multiplier to 0 over blackoutFadeSeconds.
         */
        private IEnumerator FinaleRoutine()
        {
            if (bangDelaySeconds > 0f) yield return new WaitForSeconds(bangDelaySeconds);

            // Bang: feedbacks + scene-wired explosion/ambience-stop, plus a permanent darkness ceiling.
            if (feedbacks != null)
                for (int i = 0; i < feedbacks.Length; i++)
                    if (feedbacks[i] != null) feedbacks[i].PlayFeedbacks();

            onBang?.Invoke();

            if (director != null && darknessParameter != null)
            {
                director.AddModifier(darknessParameter, ParameterBlendMode.Max, darknessParameter.maxValue,
                    0.25f, -1f, 0f, 100, this);
            }

            // Fade the light's Multiplier to 0 so it goes fully dark, ignoring any route floor.
            if (lightTarget != null)
            {
                float startMultiplier = lightTarget.Multiplier;
                float t = 0f;
                while (t < blackoutFadeSeconds)
                {
                    t += Time.deltaTime;
                    lightTarget.Multiplier = Mathf.Lerp(startMultiplier, 0f, Mathf.Clamp01(t / blackoutFadeSeconds));
                    yield return null;
                }
                lightTarget.Multiplier = 0f;
            }

            onBlackoutComplete?.Invoke();
        }
    }
}
