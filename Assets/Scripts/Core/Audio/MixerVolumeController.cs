using UnityEngine;
using UnityEngine.Audio;
using System.Collections;

public sealed class MixerVolumeController : MonoBehaviour
{
    [Header("Mixer")]
    [SerializeField] private AudioMixer mixer;

    [Tooltip("Name of the exposed parameter, e.g. 'AmbienceVolume'")]
    [SerializeField] private string exposedParam = "MasterVolume";

    [Header("Mapping")]
    [Tooltip("Volume at 0.0 on the slider, in dB. -80 is effectively silent.")]
    [SerializeField] private float minDb = -80f;

    [Tooltip("Volume at 1.0 on the slider, in dB. 0 is unity gain.")]
    [SerializeField] private float maxDb = 0f;

    [Header("Fading")]
    [Tooltip("Animation curve that maps time progression (0..1) to volume multiplier (0..1). Used for smooth fade transitions.")]
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Duration of fade-in and fade-out operations in seconds.")]
    [SerializeField] private float fadeDuration = 1f;

    private Coroutine _fadeCoroutine;

    /// <summary>
    /// Set volume with a normalized 0..1 value (perfect for UI sliders / MMF_Property).
    /// </summary>
    public void SetVolume01(float value01)
    {
        value01 = Mathf.Clamp01(value01);

        // Perceptual mapping: slider feels linear to humans
        // If you prefer a different taper, tweak the exponent.
        float perceptual = Mathf.Pow(value01, 1.5f);

        float db = Mathf.Lerp(minDb, maxDb, perceptual);
        mixer.SetFloat(exposedParam, db);
    }

    /// <summary>
    /// Directly set dB if you already work in dB.
    /// </summary>
    public void SetVolumeDb(float db)
    {
        db = Mathf.Clamp(db, minDb, maxDb);
        mixer.SetFloat(exposedParam, db);
    }

    /// <summary>
    /// Smoothly fade volume in from current level to maximum (1.0) over the configured fade duration.
    /// Any existing fade is cancelled and restarted immediately.
    /// </summary>
    public void FadeIn()
    {
        StartFade(1f, reverseCurve: false);
    }

    /// <summary>
    /// Smoothly fade volume out from current level to minimum (0.0) over the configured fade duration.
    /// Uses the reverse of the fade curve to create symmetric fade behavior.
    /// Any existing fade is cancelled and restarted immediately.
    /// </summary>
    public void FadeOut()
    {
        StartFade(0f, reverseCurve: true);
    }

    /// <summary>
    /// Internal helper to cancel active fade and start a new one.
    /// targetVolume01: normalized volume target (0..1).
    /// reverseCurve: if true, samples the curve from 1→0 instead of 0→1.
    /// </summary>
    private void StartFade(float targetVolume01, bool reverseCurve)
    {
        // Cancel any existing fade coroutine
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);

        // Start new fade coroutine
        _fadeCoroutine = StartCoroutine(FadeCoroutine(targetVolume01, reverseCurve));
    }

    /// <summary>
    /// Coroutine that animates volume over time using the configured animation curve.
    /// Runs for fadeDuration seconds, sampling the curve at each frame to determine volume.
    /// targetVolume01: the final normalized volume (0..1) to reach.
    /// reverseCurve: if true, samples curve from right to left (1→0) for fade-out behavior.
    /// </summary>
    private IEnumerator FadeCoroutine(float targetVolume01, bool reverseCurve)
    {
        float elapsedTime = 0f;

        // Determine the starting volume: if fading to 0, start from 1; otherwise start from 0
        float startVolume = (targetVolume01 == 0f) ? 1f : 0f;

        // Animate volume over fadeDuration
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsedTime / fadeDuration);

            // Sample curve based on direction: normal (0→1) or reversed (1→0)
            float curveTime = reverseCurve ? (1f - normalizedTime) : normalizedTime;
            float curveValue = fadeCurve.Evaluate(curveTime);

            // Apply the animated volume, lerping from start to target
            SetVolume01(Mathf.Lerp(startVolume, targetVolume01, curveValue));

            yield return null;
        }

        // Ensure we reach the exact target volume
        SetVolume01(targetVolume01);
        _fadeCoroutine = null;
    }
}