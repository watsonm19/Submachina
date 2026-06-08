using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SynapticPro
{
    /// <summary>
    /// Component that controls the adaptive music system
    /// Automatically manages intro→loop, crossfade transitions, beat synchronization, etc.
    /// </summary>
    public class AdaptiveMusicController : MonoBehaviour
    {
        [System.Serializable]
        public class MusicSegment
        {
            public string name;
            public AudioSource audioSource;
            public float startTime;
            public float endTime;
            public float loopPoint;
            public bool isLoop;
            public float fadeInDuration = 0f;
            public float fadeOutDuration = 0f;
            public List<Transition> transitions = new List<Transition>();
        }
        
        [System.Serializable]
        public class Transition
        {
            public string toSegment;
            public TransitionType type = TransitionType.Crossfade;
            public float duration = 1f;
        }
        
        public enum TransitionType
        {
            Immediate,
            Crossfade,
            OnBeat,
            OnBar
        }
        
        [Header("Music Settings")]
        public float bpm = 120f;
        public int beatsPerBar = 4;
        public bool playOnStart = true;
        
        [Header("Segments")]
        public List<MusicSegment> segments = new List<MusicSegment>();
        
        private MusicSegment currentSegment;
        private MusicSegment nextSegment;
        private float currentTime;
        private bool isTransitioning;
        private Coroutine musicCoroutine;
        
        void Start()
        {
            InitializeSegments();
            
            if (playOnStart)
            {
                PlayFromBeginning();
            }
        }
        
        void InitializeSegments()
        {
            // Automatically detect AudioSource from child objects
            if (segments.Count == 0)
            {
                AudioSource[] sources = GetComponentsInChildren<AudioSource>();
                foreach (var source in sources)
                {
                    var segment = new MusicSegment
                    {
                        name = source.gameObject.name,
                        audioSource = source,
                        isLoop = source.loop
                    };
                    
                    // Infer settings from segment name
                    if (segment.name.ToLower().Contains("intro"))
                    {
                        segment.isLoop = false;
                        segment.transitions.Add(new Transition 
                        { 
                            toSegment = "MainLoop",
                            type = TransitionType.Crossfade,
                            duration = 1.5f
                        });
                    }
                    else if (segment.name.ToLower().Contains("mainloop") || segment.name.ToLower().Contains("loop"))
                    {
                        segment.isLoop = true;
                    }
                    
                    segments.Add(segment);
                }
            }
            
            // Set end time from AudioClip length
            foreach (var segment in segments)
            {
                if (segment.audioSource && segment.audioSource.clip)
                {
                    if (segment.endTime <= 0)
                    {
                        segment.endTime = segment.audioSource.clip.length;
                    }
                }
            }
        }
        
        public void PlayFromBeginning()
        {
            if (musicCoroutine != null)
            {
                StopCoroutine(musicCoroutine);
            }
            
            musicCoroutine = StartCoroutine(MusicPlaybackCoroutine());
        }
        
        IEnumerator MusicPlaybackCoroutine()
        {
            // Find intro segment
            var introSegment = segments.Find(s => s.name.ToLower().Contains("intro"));
            if (introSegment == null && segments.Count > 0)
            {
                introSegment = segments[0];
            }
            
            if (introSegment == null)
            {
                Debug.LogWarning("No music segments found!");
                yield break;
            }
            
            // Play intro
            currentSegment = introSegment;
            PlaySegment(currentSegment);
            
            while (true)
            {
                currentTime = currentSegment.audioSource.time;
                
                // Loop when reaching loop point
                if (currentSegment.isLoop && currentSegment.loopPoint > 0 &&
                    currentTime >= currentSegment.loopPoint)
                {
                    currentSegment.audioSource.time = currentSegment.startTime;
                }
                
                // Check transitions
                if (!isTransitioning)
                {
                    // Check for automatic transition
                    if (!currentSegment.isLoop && currentTime >= currentSegment.endTime - 2f)
                    {
                        // Find transition to next segment
                        if (currentSegment.transitions.Count > 0)
                        {
                            var transition = currentSegment.transitions[0];
                            var next = segments.Find(s => s.name == transition.toSegment);
                            if (next != null)
                            {
                                StartCoroutine(TransitionToSegment(next, transition));
                            }
                        }
                    }
                }
                
                yield return null;
            }
        }
        
        void PlaySegment(MusicSegment segment)
        {
            if (segment.audioSource == null) return;
            
            segment.audioSource.time = segment.startTime;
            segment.audioSource.Play();
            
            if (segment.fadeInDuration > 0)
            {
                StartCoroutine(FadeIn(segment.audioSource, segment.fadeInDuration));
            }
            else
            {
                segment.audioSource.volume = 1f;
            }
        }
        
        IEnumerator TransitionToSegment(MusicSegment targetSegment, Transition transition)
        {
            if (isTransitioning) yield break;
            
            isTransitioning = true;
            nextSegment = targetSegment;
            
            switch (transition.type)
            {
                case TransitionType.Immediate:
                    currentSegment.audioSource.Stop();
                    currentSegment = targetSegment;
                    PlaySegment(currentSegment);
                    break;
                    
                case TransitionType.Crossfade:
                    // Start next segment
                    PlaySegment(targetSegment);
                    targetSegment.audioSource.volume = 0;

                    // Crossfade
                    float elapsed = 0;
                    while (elapsed < transition.duration)
                    {
                        elapsed += Time.deltaTime;
                        float t = elapsed / transition.duration;
                        
                        currentSegment.audioSource.volume = 1 - t;
                        targetSegment.audioSource.volume = t;
                        
                        yield return null;
                    }
                    
                    currentSegment.audioSource.Stop();
                    currentSegment = targetSegment;
                    break;
                    
                case TransitionType.OnBeat:
                case TransitionType.OnBar:
                    // Wait until end of beat/bar
                    float beatDuration = 60f / bpm;
                    float barDuration = beatDuration * beatsPerBar;
                    float waitTime = transition.type == TransitionType.OnBeat ? beatDuration : barDuration;

                    // Wait for next beat/bar
                    float timeToWait = waitTime - (currentSegment.audioSource.time % waitTime);
                    yield return new WaitForSeconds(timeToWait);

                    // Crossfade transition
                    StartCoroutine(TransitionToSegment(targetSegment, new Transition
                    {
                        toSegment = targetSegment.name,
                        type = TransitionType.Crossfade,
                        duration = transition.duration
                    }));
                    yield break;
            }
            
            isTransitioning = false;
        }
        
        IEnumerator FadeIn(AudioSource source, float duration)
        {
            float elapsed = 0;
            source.volume = 0;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                source.volume = elapsed / duration;
                yield return null;
            }
            
            source.volume = 1f;
        }
        
        IEnumerator FadeOut(AudioSource source, float duration)
        {
            float elapsed = 0;
            float startVolume = source.volume;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                source.volume = startVolume * (1 - elapsed / duration);
                yield return null;
            }
            
            source.volume = 0;
            source.Stop();
        }
        
        /// <summary>
        /// Transition to specific segment
        /// </summary>
        public void TransitionTo(string segmentName, float transitionDuration = 2f)
        {
            var targetSegment = segments.Find(s => s.name == segmentName);
            if (targetSegment != null && !isTransitioning)
            {
                StartCoroutine(TransitionToSegment(targetSegment, new Transition
                {
                    toSegment = segmentName,
                    type = TransitionType.Crossfade,
                    duration = transitionDuration
                }));
            }
        }
        
        /// <summary>
        /// Stop music
        /// </summary>
        public void StopMusic(float fadeOutDuration = 1f)
        {
            if (musicCoroutine != null)
            {
                StopCoroutine(musicCoroutine);
                musicCoroutine = null;
            }
            
            foreach (var segment in segments)
            {
                if (segment.audioSource && segment.audioSource.isPlaying)
                {
                    if (fadeOutDuration > 0)
                    {
                        StartCoroutine(FadeOut(segment.audioSource, fadeOutDuration));
                    }
                    else
                    {
                        segment.audioSource.Stop();
                    }
                }
            }
        }
    }
}