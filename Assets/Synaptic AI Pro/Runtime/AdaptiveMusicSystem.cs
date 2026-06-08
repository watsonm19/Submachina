using System.Collections;
using UnityEngine;

namespace SynapticPro
{
    /// <summary>
    /// Revolutionary adaptive music system
    /// Perfect implementation of intro+loop pattern
    /// Music system generated from natural language in one shot
    /// </summary>
    public class AdaptiveMusicSystem : MonoBehaviour
    {
        [Header("Audio Settings")]
        public AudioClip musicClip;
        public float introDuration = 10f;
        public float loopStartTime = 10f;
        public float loopEndTime = -1f; // -1 means end of clip
        
        [Header("Fade Settings")]
        public float fadeInDuration = 0f;
        public float fadeOutDuration = 2f;
        public float volume = 0.8f;
        
        [Header("Runtime Info")]
        [SerializeField] private bool isPlaying = false;
        [SerializeField] private bool hasPlayedIntro = false;
        [SerializeField] private float currentTime = 0f;
        
        private AudioSource audioSource;
        private Coroutine musicCoroutine;
        
        void Awake()
        {
            // Automatically create AudioSource
            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            // Basic configuration
            audioSource.playOnAwake = false;
            audioSource.loop = false; // Manual loop control
            audioSource.volume = 0f; // For fade-in
        }
        
        void Start()
        {
            if (musicClip != null)
            {
                PlayAdaptiveMusic();
            }
        }
        
        /// <summary>
        /// Start playing adaptive music
        /// </summary>
        public void PlayAdaptiveMusic()
        {
            if (musicClip == null)
            {
                Debug.LogWarning("[AdaptiveMusic] AudioClip is not assigned!");
                return;
            }
            
            if (musicCoroutine != null)
            {
                StopCoroutine(musicCoroutine);
            }
            
            // Set default loop end time
            if (loopEndTime <= 0 || loopEndTime > musicClip.length)
            {
                loopEndTime = musicClip.length;
            }
            
            musicCoroutine = StartCoroutine(AdaptiveMusicCoroutine());
        }
        
        /// <summary>
        /// Stop music (with fade-out)
        /// </summary>
        public void StopMusic()
        {
            if (musicCoroutine != null)
            {
                StopCoroutine(musicCoroutine);
                musicCoroutine = null;
            }
            
            StartCoroutine(FadeOut());
        }
        
        /// <summary>
        /// Main loop for adaptive music
        /// </summary>
        private IEnumerator AdaptiveMusicCoroutine()
        {
            isPlaying = true;
            hasPlayedIntro = false;
            currentTime = 0f;
            
            // Set AudioClip
            audioSource.clip = musicClip;
            audioSource.time = 0f;
            audioSource.Play();

            // Fade in
            if (fadeInDuration > 0)
            {
                yield return StartCoroutine(FadeIn());
            }
            else
            {
                audioSource.volume = volume;
            }
            
            Debug.Log($"[AdaptiveMusic] Started playing: Intro({introDuration}s) -> Loop({loopStartTime}s-{loopEndTime}s)");
            
            // Main loop
            while (isPlaying)
            {
                currentTime = audioSource.time;

                // Handle intro section
                if (!hasPlayedIntro && currentTime >= introDuration)
                {
                    hasPlayedIntro = true;
                    Debug.Log("[AdaptiveMusic] Intro finished, starting loop section");
                }

                // When loop point is reached
                if (hasPlayedIntro && currentTime >= loopEndTime)
                {
                    Debug.Log($"[AdaptiveMusic] Loop point reached, jumping to {loopStartTime}s");
                    audioSource.time = loopStartTime;
                }

                // When music ends (unexpected)
                if (!audioSource.isPlaying)
                {
                    Debug.LogWarning("[AdaptiveMusic] Audio stopped unexpectedly, restarting...");
                    audioSource.time = hasPlayedIntro ? loopStartTime : 0f;
                    audioSource.Play();
                }

                yield return null; // Wait until next frame
            }
        }
        
        /// <summary>
        /// Fade-in processing
        /// </summary>
        private IEnumerator FadeIn()
        {
            float elapsedTime = 0f;
            audioSource.volume = 0f;
            
            while (elapsedTime < fadeInDuration)
            {
                elapsedTime += Time.deltaTime;
                float normalizedTime = elapsedTime / fadeInDuration;
                audioSource.volume = Mathf.Lerp(0f, volume, normalizedTime);
                yield return null;
            }
            
            audioSource.volume = volume;
        }
        
        /// <summary>
        /// Fade-out processing
        /// </summary>
        private IEnumerator FadeOut()
        {
            float startVolume = audioSource.volume;
            float elapsedTime = 0f;
            
            while (elapsedTime < fadeOutDuration)
            {
                elapsedTime += Time.deltaTime;
                float normalizedTime = elapsedTime / fadeOutDuration;
                audioSource.volume = Mathf.Lerp(startVolume, 0f, normalizedTime);
                yield return null;
            }
            
            audioSource.volume = 0f;
            audioSource.Stop();
            isPlaying = false;
        }
        
        /// <summary>
        /// Dynamically update settings (can be changed during runtime)
        /// </summary>
        public void UpdateSettings(float newIntroDuration, float newLoopStart, float newLoopEnd = -1f)
        {
            introDuration = newIntroDuration;
            loopStartTime = newLoopStart;
            
            if (newLoopEnd > 0)
            {
                loopEndTime = newLoopEnd;
            }
            else if (musicClip != null)
            {
                loopEndTime = musicClip.length;
            }
            
            Debug.Log($"[AdaptiveMusic] Settings updated: Intro({introDuration}s) -> Loop({loopStartTime}s-{loopEndTime}s)");
        }
        
        /// <summary>
        /// Display debug information
        /// </summary>
        void OnGUI()
        {
            if (!Application.isPlaying) return;
            
            GUILayout.BeginArea(new Rect(10, 10, 300, 150));
            GUILayout.Label("🎵 Adaptive Music System");
            GUILayout.Label($"Playing: {isPlaying}");
            GUILayout.Label($"Has Played Intro: {hasPlayedIntro}");
            GUILayout.Label($"Current Time: {currentTime:F2}s");
            GUILayout.Label($"Loop Range: {loopStartTime:F1}s - {loopEndTime:F1}s");
            
            if (musicClip != null)
            {
                GUILayout.Label($"Clip Length: {musicClip.length:F2}s");
            }
            
            GUILayout.EndArea();
        }
        
        void OnDestroy()
        {
            if (musicCoroutine != null)
            {
                StopCoroutine(musicCoroutine);
            }
        }
    }
}