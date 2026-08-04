using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Core.Audio
{
    /**
     * Central runtime mixer for ambience beds, pooled one-shots, and gated stingers.
     *
     * Ambience layers are persistent looping voices whose volume follows a combined "influence"
     * value (0..1) fed by one or more sources (SetAmbienceInfluence), smoothed with independent
     * fade-in/fade-out rates from the layer's def. One-shots are played through a small pooled set
     * of AudioSources so bursts of sound effects don't spawn/destroy GameObjects. Stingers are
     * one-shots with cooldown gating (per-def, per-category, global) and a duck envelope that
     * temporarily pulls ambience volume down while the stinger rings out.
     *
     * Not a singleton: consumers resolve a director via FindFor(), preferring one in their own
     * hierarchy so multiple directors can coexist (e.g. multiple subs, split-screen rigs).
     */
    public class AudioDirector : MonoBehaviour
    {
        /**
         * Per-layer runtime playback state. Lives here, never in the ScriptableObject definition,
         * so edit-time assets stay clean of play-mode values.
         */
        private class AmbienceVoice
        {
            public AmbienceLayerDef Def;
            public AudioSource Source;
            public readonly Dictionary<object, float> Influences = new Dictionary<object, float>();
            public float CurrentVolume;
            public bool ForcedFadeOut;
            public float ForcedFadeSeconds;
        }

        [Header("Ambience")]
        [Range(0f, 1f)] [SerializeField] private float masterAmbienceVolume = 1f;

        [Header("One-Shot Pool")]
        [SerializeField] private int oneShotPoolInitialSize = 8;
        [SerializeField] private int oneShotPoolMaxSize = 20;

        [Header("Stingers")]
        [Tooltip("Minimum seconds between ANY two stingers, regardless of def or category.")]
        [SerializeField] private float globalStingerCooldownSeconds = 8f;

        // ------------------------------------------------------------------ ambience state
        private readonly Dictionary<AmbienceLayerDef, AmbienceVoice> _ambienceVoices =
            new Dictionary<AmbienceLayerDef, AmbienceVoice>();

        // ------------------------------------------------------------------ one-shot pool state
        private readonly List<AudioSource> _oneShotPool = new List<AudioSource>();
        private Transform _oneShotPoolRoot;
        private readonly Dictionary<AudioOneShotDef, float> _oneShotLastPlayTime = new Dictionary<AudioOneShotDef, float>();
        private readonly Dictionary<AudioOneShotDef, List<int>> _oneShotShuffleBags = new Dictionary<AudioOneShotDef, List<int>>();
        private readonly Dictionary<AudioOneShotDef, int> _oneShotLastIndex = new Dictionary<AudioOneShotDef, int>();

        // ------------------------------------------------------------------ stinger state
        private readonly Dictionary<AudioStingerDef, float> _stingerLastPlayTime = new Dictionary<AudioStingerDef, float>();
        private readonly Dictionary<AudioStingerDef, List<int>> _stingerShuffleBags = new Dictionary<AudioStingerDef, List<int>>();
        private readonly Dictionary<AudioStingerDef, int> _stingerLastIndex = new Dictionary<AudioStingerDef, int>();
        private readonly Dictionary<string, float> _categoryLastPlayTime = new Dictionary<string, float>();
        private float _lastAnyStingerTime = float.NegativeInfinity;
        private float _duckMultiplier = 1f;
        private Coroutine _duckCoroutine;

        // ------------------------------------------------------------------ discovery

        /**
         * Resolves the director a component should talk to: one in its parent hierarchy first
         * (supports multiple rigs), otherwise the first director in the scene. No singletons.
         */
        public static AudioDirector FindFor(Component context)
        {
            if (context == null) return FindFirstObjectByType<AudioDirector>();
            var inParents = context.GetComponentInParent<AudioDirector>();
            return inParents != null ? inParents : FindFirstObjectByType<AudioDirector>();
        }

        // ------------------------------------------------------------------ setup

        /// <summary>Builds the pooled one-shot voice pool up to its initial serialized size.</summary>
        private void Awake()
        {
            _oneShotPoolRoot = new GameObject("One-Shot Pool").transform;
            _oneShotPoolRoot.SetParent(transform, false);
            for (int i = 0; i < oneShotPoolInitialSize; i++) _oneShotPool.Add(CreatePooledSource());
        }

        private void Update()
        {
            UpdateAmbience(Time.deltaTime);
        }

        // ------------------------------------------------------------------ ambience

        /// <summary>Sets this layer's influence from a specific source. Multiple sources combine via MAX.</summary>
        public void SetAmbienceInfluence(AmbienceLayerDef def, object sourceKey, float influence01)
        {
            if (def == null || sourceKey == null) return;
            var voice = EnsureVoice(def);
            voice.Influences[sourceKey] = Mathf.Clamp01(influence01);

            // A fresh influence cancels any pending forced fade-out and resumes playback.
            if (!voice.ForcedFadeOut) return;
            voice.ForcedFadeOut = false;
            if (!voice.Source.isPlaying) voice.Source.Play();
        }

        /// <summary>Convenience overload for single-source callers — keys the influence by the director itself.</summary>
        public void SetAmbienceInfluence(AmbienceLayerDef def, float influence01) => SetAmbienceInfluence(def, this, influence01);

        /// <summary>Fades every ambience voice to silence over fadeSeconds and stops each source once it reaches zero.</summary>
        public void StopAllAmbience(float fadeSeconds)
        {
            foreach (var voice in _ambienceVoices.Values)
            {
                voice.ForcedFadeOut = true;
                voice.ForcedFadeSeconds = fadeSeconds;
            }
        }

        /// <summary>Fades a single ambience layer to silence over fadeSeconds and stops its source once it reaches zero.</summary>
        public void StopAmbience(AmbienceLayerDef def, float fadeSeconds)
        {
            if (def == null || !_ambienceVoices.TryGetValue(def, out var voice)) return;
            voice.ForcedFadeOut = true;
            voice.ForcedFadeSeconds = fadeSeconds;
        }

        /**
         * Creates and starts a voice for a layer the first time it's referenced. The source is
         * started immediately at volume 0 so the very first influence update can fade it in
         * cleanly instead of popping in at an arbitrary volume.
         */
        private AmbienceVoice EnsureVoice(AmbienceLayerDef def)
        {
            if (_ambienceVoices.TryGetValue(def, out var existing)) return existing;

            var go = new GameObject($"Ambience - {def.name}");
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = def.clip;
            source.loop = def.loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.pitch = def.pitch;
            source.volume = 0f;
            if (def.mixerGroup != null) source.outputAudioMixerGroup = def.mixerGroup;
            if (def.randomStartPosition && def.clip != null) source.time = Random.Range(0f, def.clip.length);

            var voice = new AmbienceVoice { Def = def, Source = source };
            _ambienceVoices.Add(def, voice);
            source.Play();
            return voice;
        }

        /**
         * Advances every ambience voice one frame: combines its influence sources via MAX, maps
         * that through the layer's curve to a target volume (also scaled by master volume and the
         * stinger duck multiplier), then moves the current volume toward that target linearly at
         * a rate of 1/fadeSeconds per second. A forced fade-out (StopAmbience/StopAllAmbience)
         * always overrides the influence-driven target and stops the source once silent.
         */
        private void UpdateAmbience(float dt)
        {
            foreach (var pair in _ambienceVoices)
            {
                var voice = pair.Value;
                var def = voice.Def;

                // Combine every registered influence source for this layer via MAX.
                float combined = 0f;
                foreach (var influence in voice.Influences.Values)
                    if (influence > combined) combined = influence;

                // A forced fade-out always wins over whatever influence is currently asking for.
                float targetVolume;
                float fadeSeconds;
                if (voice.ForcedFadeOut)
                {
                    targetVolume = 0f;
                    fadeSeconds = voice.ForcedFadeSeconds;
                }
                else
                {
                    targetVolume = def.influenceCurve.Evaluate(combined) * def.baseVolume * masterAmbienceVolume * _duckMultiplier;
                    fadeSeconds = targetVolume > voice.CurrentVolume ? def.fadeInSeconds : def.fadeOutSeconds;
                }

                // fadeSeconds <= 0 means snap instantly rather than divide by zero for a rate.
                voice.CurrentVolume = fadeSeconds <= 0f
                    ? targetVolume
                    : Mathf.MoveTowards(voice.CurrentVolume, targetVolume, (1f / fadeSeconds) * dt);
                voice.Source.volume = voice.CurrentVolume;

                if (voice.ForcedFadeOut && voice.CurrentVolume <= 0.0001f && voice.Source.isPlaying) voice.Source.Stop();
            }
        }

        // ------------------------------------------------------------------ one-shots

        /// <summary>UnityEvent-wireable wrapper around PlayOneShot (UnityEvents can't bind non-void methods).</summary>
        public void TriggerOneShot(AudioOneShotDef def) => PlayOneShot(def);

        /// <summary>Plays a non-positional (2D) one-shot. Returns null when the def is empty or on cooldown.</summary>
        public AudioSource PlayOneShot(AudioOneShotDef def)
        {
            if (!TryPrepareOneShot(def, out int index)) return null;
            float volume = Random.Range(def.volumeRange.x, def.volumeRange.y);
            float pitch = Random.Range(def.pitchRange.x, def.pitchRange.y);
            return PlayClipOnPooledSource(def.clips[index], volume, pitch, def.mixerGroup, false, Vector3.zero, 0f, 0f, 0f);
        }

        /// <summary>Plays a one-shot at a world position, honoring the def's spatialization settings. Returns null when the def is empty or on cooldown.</summary>
        public AudioSource PlayOneShotAt(AudioOneShotDef def, Vector3 position)
        {
            if (!TryPrepareOneShot(def, out int index)) return null;
            float volume = Random.Range(def.volumeRange.x, def.volumeRange.y);
            float pitch = Random.Range(def.pitchRange.x, def.pitchRange.y);
            return PlayClipOnPooledSource(def.clips[index], volume, pitch, def.mixerGroup, true, position, def.spatialBlend, def.minDistance, def.maxDistance);
        }

        /// <summary>Validates a one-shot def against cooldown/emptiness, selects a clip index, and stamps the cooldown timer.</summary>
        private bool TryPrepareOneShot(AudioOneShotDef def, out int clipIndex)
        {
            clipIndex = -1;
            if (def == null || def.clips == null || def.clips.Length == 0) return false;
            if (def.cooldownSeconds > 0f && _oneShotLastPlayTime.TryGetValue(def, out var last) && Time.time - last < def.cooldownSeconds) return false;

            clipIndex = SelectClipIndex(def, def.clips, def.selectionMode, _oneShotShuffleBags, _oneShotLastIndex);
            _oneShotLastPlayTime[def] = Time.time;
            return true;
        }

        /// <summary>Finds a free pooled source or grows the pool up to its serialized cap; steals the oldest voice if exhausted.</summary>
        private AudioSource GetPooledSource()
        {
            for (int i = 0; i < _oneShotPool.Count; i++)
                if (!_oneShotPool[i].isPlaying) return _oneShotPool[i];

            if (_oneShotPool.Count < oneShotPoolMaxSize)
            {
                var created = CreatePooledSource();
                _oneShotPool.Add(created);
                return created;
            }

            // Pool exhausted at its cap — steal the oldest voice rather than dropping the new sound.
            return _oneShotPool[0];
        }

        private AudioSource CreatePooledSource()
        {
            var go = new GameObject($"OneShot Voice {_oneShotPool.Count}");
            go.transform.SetParent(_oneShotPoolRoot, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }

        /// <summary>Configures and plays a clip on a pooled source, positional or 2D as requested.</summary>
        private AudioSource PlayClipOnPooledSource(AudioClip clip, float volume, float pitch, AudioMixerGroup mixerGroup,
            bool positional, Vector3 position, float spatialBlend, float minDistance, float maxDistance)
        {
            var source = GetPooledSource();

            if (positional)
            {
                source.transform.position = position;
                source.spatialBlend = spatialBlend;
                source.minDistance = minDistance;
                source.maxDistance = maxDistance;
            }
            else
            {
                source.spatialBlend = 0f;
            }

            source.outputAudioMixerGroup = mixerGroup;
            source.pitch = pitch;
            source.volume = volume;
            source.clip = clip;
            source.Play();
            return source;
        }

        /**
         * Picks a clip index from a clip array according to the selection mode. ShuffleBag keeps a
         * per-def bag of indices (keyed by the def asset itself) that is refilled and Fisher-Yates
         * shuffled whenever it empties, and avoids repeating the immediately previous index when
         * the bag offers an alternative. Shared by one-shots and stingers via the TDef type param.
         */
        private int SelectClipIndex<TDef>(TDef defKey, AudioClip[] clips, AudioOneShotDef.SelectionMode mode,
            Dictionary<TDef, List<int>> shuffleBags, Dictionary<TDef, int> lastIndices) where TDef : Object
        {
            if (mode == AudioOneShotDef.SelectionMode.Random) return Random.Range(0, clips.Length);

            if (!shuffleBags.TryGetValue(defKey, out var bag) || bag.Count == 0)
            {
                bag = RefillShuffleBag(clips.Length);
                shuffleBags[defKey] = bag;
            }

            // Pop from the end of the bag; swap to the second-to-last entry first if that would repeat.
            int lastIndex = lastIndices.TryGetValue(defKey, out var last) ? last : -1;
            int popIndex = bag.Count - 1;
            if (bag[popIndex] == lastIndex && bag.Count > 1) popIndex--;

            int pick = bag[popIndex];
            bag.RemoveAt(popIndex);
            lastIndices[defKey] = pick;
            return pick;
        }

        private static List<int> RefillShuffleBag(int count)
        {
            var bag = new List<int>(count);
            for (int i = 0; i < count; i++) bag.Add(i);

            // Fisher-Yates shuffle.
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            return bag;
        }

        // ------------------------------------------------------------------ stingers

        /**
         * Attempts to play a stinger. Blocked (returns false, nothing plays) unless the def's own
         * cooldown, its category's cooldown, and the global stinger cooldown are all satisfied.
         * On success, plays the clip as a 2D one-shot through the pool, stamps all three cooldown
         * scopes, and (re)starts the duck envelope so ambience ducks under the stinger.
         */
        public bool PlayStinger(AudioStingerDef def)
        {
            if (def == null || def.clips == null || def.clips.Length == 0) return false;
            if (!IsStingerEligible(def)) return false;

            int index = SelectClipIndex(def, def.clips, def.selectionMode, _stingerShuffleBags, _stingerLastIndex);
            float volume = Random.Range(def.volumeRange.x, def.volumeRange.y);
            float pitch = Random.Range(def.pitchRange.x, def.pitchRange.y);
            PlayClipOnPooledSource(def.clips[index], volume, pitch, def.mixerGroup, false, Vector3.zero, 0f, 0f, 0f);

            // Stamp every cooldown scope this stinger participates in.
            float now = Time.time;
            _stingerLastPlayTime[def] = now;
            if (!string.IsNullOrEmpty(def.category)) _categoryLastPlayTime[def.category] = now;
            _lastAnyStingerTime = now;

            // Restart the duck envelope so overlapping stingers don't fight over duckMultiplier.
            if (_duckCoroutine != null) StopCoroutine(_duckCoroutine);
            _duckCoroutine = StartCoroutine(DuckEnvelope(def));
            return true;
        }

        /// <summary>UnityEvent-wireable wrapper around PlayStinger (UnityEvents can't bind bool-returning methods).</summary>
        public void TriggerStinger(AudioStingerDef def) => PlayStinger(def);

        /// <summary>Checks the def, category, and global cooldown scopes — all three must be clear.</summary>
        private bool IsStingerEligible(AudioStingerDef def)
        {
            float now = Time.time;

            if (def.cooldownSeconds > 0f && _stingerLastPlayTime.TryGetValue(def, out var lastDef) && now - lastDef < def.cooldownSeconds)
                return false;

            if (def.categoryCooldownSeconds > 0f && !string.IsNullOrEmpty(def.category) &&
                _categoryLastPlayTime.TryGetValue(def.category, out var lastCategory) && now - lastCategory < def.categoryCooldownSeconds)
                return false;

            if (globalStingerCooldownSeconds > 0f && now - _lastAnyStingerTime < globalStingerCooldownSeconds)
                return false;

            return true;
        }

        /**
         * Ramps duckMultiplier from its current value down to (1 - duckAmount) over duckAttackSeconds,
         * holds there for duckHoldSeconds, then ramps back up to 1 over duckReleaseSeconds. Restarting
         * this coroutine (a new stinger arriving mid-envelope) simply picks up from wherever the
         * multiplier currently sits, so overlapping stingers blend rather than pop.
         */
        private IEnumerator DuckEnvelope(AudioStingerDef def)
        {
            float targetDucked = 1f - def.duckAmount;

            // Attack: ramp down toward the ducked level.
            float startVolume = _duckMultiplier;
            float t = 0f;
            while (t < def.duckAttackSeconds)
            {
                t += Time.deltaTime;
                _duckMultiplier = Mathf.Lerp(startVolume, targetDucked, Mathf.Clamp01(t / def.duckAttackSeconds));
                yield return null;
            }
            _duckMultiplier = targetDucked;

            // Hold at the fully ducked level.
            if (def.duckHoldSeconds > 0f) yield return new WaitForSeconds(def.duckHoldSeconds);

            // Release: ramp back up to unity gain.
            float releaseStart = _duckMultiplier;
            t = 0f;
            while (t < def.duckReleaseSeconds)
            {
                t += Time.deltaTime;
                _duckMultiplier = Mathf.Lerp(releaseStart, 1f, Mathf.Clamp01(t / def.duckReleaseSeconds));
                yield return null;
            }
            _duckMultiplier = 1f;
            _duckCoroutine = null;
        }

        /// <summary>Seconds since the last stinger of any kind played, clamped to 99999 when none ever has.</summary>
        public float SecondsSinceAnyStinger
        {
            get
            {
                if (float.IsNegativeInfinity(_lastAnyStingerTime)) return 99999f;
                return Mathf.Min(Time.time - _lastAnyStingerTime, 99999f);
            }
        }

        /// <summary>Seconds since a stinger in this category last played, or 99999 if that category has never played.</summary>
        public float SecondsSinceCategory(string category)
        {
            if (string.IsNullOrEmpty(category) || !_categoryLastPlayTime.TryGetValue(category, out var last)) return 99999f;
            return Mathf.Min(Time.time - last, 99999f);
        }

        // ------------------------------------------------------------------ introspection (editor tooling)

        /// <summary>Read-only view of one ambience voice for editor tools and debug panels.</summary>
        public struct AmbienceSnapshot
        {
            public AmbienceLayerDef Def;
            public float Influence;
            public float Volume;
            public bool IsPlaying;
        }

        /// <summary>Fills the buffer with a snapshot of every ambience voice (callers reuse the list to stay allocation-free).</summary>
        public void GetAmbienceSnapshots(List<AmbienceSnapshot> buffer)
        {
            buffer.Clear();
            foreach (var pair in _ambienceVoices)
            {
                var voice = pair.Value;
                float combined = 0f;
                foreach (var influence in voice.Influences.Values)
                    if (influence > combined) combined = influence;
                buffer.Add(new AmbienceSnapshot
                {
                    Def = voice.Def,
                    Influence = combined,
                    Volume = voice.CurrentVolume,
                    IsPlaying = voice.Source != null && voice.Source.isPlaying
                });
            }
        }

        /// <summary>Current stinger duck multiplier applied to all ambience (1 = no ducking).</summary>
        public float DuckMultiplier => _duckMultiplier;

        /// <summary>Number of pooled one-shot voices currently playing.</summary>
        public int ActiveOneShotCount
        {
            get
            {
                int active = 0;
                for (int i = 0; i < _oneShotPool.Count; i++)
                    if (_oneShotPool[i].isPlaying) active++;
                return active;
            }
        }

        // ------------------------------------------------------------------ debugging

#if ODIN_INSPECTOR
        [ShowInInspector, ReadOnly, PropertyOrder(100), LabelText("Audio Debug Readout")]
#endif
        public string DebugReadout => BuildDebugReadout();

        /// <summary>Human-readable dump of ambience voices, pool usage, duck state, and stinger timing — used by inspectors and bug-report snapshots.</summary>
        public string BuildDebugReadout()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Duck multiplier: {_duckMultiplier:0.00}");
            sb.AppendLine($"Seconds since last stinger: {SecondsSinceAnyStinger:0.0}");

            sb.AppendLine("Ambience voices:");
            if (_ambienceVoices.Count == 0) sb.AppendLine("  (none)");
            foreach (var pair in _ambienceVoices)
            {
                var voice = pair.Value;
                float combined = 0f;
                foreach (var influence in voice.Influences.Values)
                    if (influence > combined) combined = influence;
                sb.AppendLine($"  {voice.Def.name}: influence {combined:0.00}, volume {voice.CurrentVolume:0.00}");
            }

            int activeOneShots = 0;
            for (int i = 0; i < _oneShotPool.Count; i++)
                if (_oneShotPool[i].isPlaying) activeOneShots++;
            sb.AppendLine($"Active one-shots: {activeOneShots} / {_oneShotPool.Count} pooled");

            return sb.ToString();
        }
    }
}
