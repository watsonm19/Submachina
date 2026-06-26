using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Minimal, self-contained sonar audio: a beep when the sub pings, and an echo beep
     * as each contact returns — with volume and pitch scaled by proximity (closer reads
     * as louder and higher).
     *
     * This is the project's documented fallback to MMF for sonar sound: it drives an
     * AudioSource directly, so a sub makes sound with zero feedback-router / MMSoundManager
     * wiring. SonarSystem still fires its SonarPingEmit / SonarReturn feedback keys in
     * parallel, so richer MMF juice can be layered on later without removing this.
     *
     * Lives on the SonarSystem object (sibling of the SonarSystem component) so it ships
     * inside the sonar prefab — the only per-sub setup is assigning the beep clip.
     */
    [RequireComponent(typeof(AudioSource))]
    public class SonarReturnAudio : SubmarineComponent
    {
        // =====================
        // Clip
        // =====================

        [FoldoutGroup("Clip")]
        [Tooltip("Beep played for the outgoing ping. Also the default return echo unless overridden below.")]
        [SerializeField] private AudioClip beep;

        [FoldoutGroup("Clip")]
        [Tooltip("Play a dedicated clip for returning echoes instead of reusing the emit beep. " +
                 "Off = the original single-clip behaviour (send and return share 'beep').")]
        [SerializeField] private bool useSeparateReturnClip;

        [FoldoutGroup("Clip")]
        [ShowIf(nameof(useSeparateReturnClip))]
        [Tooltip("Clip for returning echoes when 'Use Separate Return Clip' is on. Falls back to the emit beep if empty.")]
        [SerializeField] private AudioClip returnBeep;

        [FoldoutGroup("Clip")]
        [Tooltip("When a returning contact's SonarSignature defines its own returnPingClip, play that " +
                 "instead — so different objects 'sound' different. Falls back to the return/emit clip " +
                 "when the signature has none.")]
        [SerializeField] private bool useSignatureReturnClip = true;

        // =====================
        // Outgoing Ping
        // =====================

        [FoldoutGroup("Emit")]
        [Tooltip("Play a beep when the sub emits a pulse.")]
        [SerializeField] private bool playOnEmit = true;

        [FoldoutGroup("Emit")]
        [Tooltip("Volume of the outgoing ping beep.")]
        [SerializeField, Range(0f, 1f)] private float emitVolume = 0.7f;

        [FoldoutGroup("Emit")]
        [Tooltip("Pitch of the outgoing ping beep.")]
        [SerializeField, Range(0.25f, 3f)] private float emitPitch = 1f;

        // =====================
        // Returning Echoes
        // =====================

        [FoldoutGroup("Return")]
        [Tooltip("Play a beep as each contact echoes back.")]
        [SerializeField] private bool playOnReturn = true;

        [FoldoutGroup("Return")]
        [Tooltip("Echo volume for the closest contact (proximity 1).")]
        [SerializeField, Range(0f, 1f)] private float returnVolumeNear = 1f;

        [FoldoutGroup("Return")]
        [Tooltip("Echo volume for the furthest contact (proximity 0).")]
        [SerializeField, Range(0f, 1f)] private float returnVolumeFar = 0.08f;

        [FoldoutGroup("Return")]
        [Tooltip("Shapes how proximity maps to loudness. PlayOneShot volume is LINEAR amplitude " +
                 "but the ear hears loudness logarithmically, so a flat lerp sounds nearly constant " +
                 "across the mid-range. >1 biases volume toward near contacts (sharper 'closer = louder' " +
                 "contrast); 1 = the old linear behaviour.")]
        [SerializeField, Range(1f, 4f)] private float returnVolumeContrast = 2.2f;

        [FoldoutGroup("Return")]
        [Tooltip("Echo pitch for the closest contact — higher reads as 'near'.")]
        [SerializeField, Range(0.25f, 3f)] private float returnPitchNear = 1.3f;

        [FoldoutGroup("Return")]
        [Tooltip("Echo pitch for the furthest contact.")]
        [SerializeField, Range(0.25f, 3f)] private float returnPitchFar = 0.8f;

        // =====================
        // State
        // =====================

        private AudioSource _source;
        private SonarSystem _sonar;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
        }

        /** Bind in Start so the sibling SonarSystem (registered in its own Awake) is resolvable. */
        private void Start()
        {
            _sonar = Sub != null ? Sub.Sonar : null;
            if (_sonar == null) return;
            _sonar.PingEmitted += OnPingEmitted;
            _sonar.ContactReturned += OnContactReturned;
        }

        protected override void OnDestroy()
        {
            if (_sonar != null)
            {
                _sonar.PingEmitted -= OnPingEmitted;
                _sonar.ContactReturned -= OnContactReturned;
            }
            base.OnDestroy();
        }

        // -------------------------------------------------------
        // Playback
        // -------------------------------------------------------

        /** The outgoing pulse beep — fixed volume/pitch, it's the player's own ping. */
        private void OnPingEmitted(Vector2 origin)
        {
            if (!playOnEmit || beep == null) return;
            _source.pitch = emitPitch;
            _source.PlayOneShot(beep, emitVolume);
        }

        /**
         * The return echo — louder and higher the closer the contact, so the player can
         * gauge distance by ear (the metal-detector cue). Proximity is 1 at the sub and
         * 0 at the resolved sonar range.
         */
        private void OnContactReturned(SonarContact contact)
        {
            if (!playOnReturn) return;

            // Resolve which clip "voices" this echo — proximity scaling below applies to any of them.
            AudioClip clip = ResolveReturnClip(contact);
            if (clip == null) return;

            float range = Mathf.Max(0.01f, _sonar.ResolvedRange);
            float proximity = Mathf.Clamp01(1f - contact.Distance / range);

            // Pitch reads linearly (the ear is very sensitive to it), so map it straight.
            _source.pitch = Mathf.Lerp(returnPitchFar, returnPitchNear, proximity);

            // Volume is linear amplitude but loudness is logarithmic, so a flat lerp sounds
            // near-constant — curve proximity to exaggerate the closer-is-louder contrast.
            float loudness = Mathf.Pow(proximity, returnVolumeContrast);
            _source.PlayOneShot(clip, Mathf.Lerp(returnVolumeFar, returnVolumeNear, loudness));
        }

        /**
         * Picks the clip for a returning echo, most-specific first:
         *   1. the contact's own signature clip (when enabled and present) — each archetype sounds unique;
         *   2. the dedicated return clip (when enabled and present);
         *   3. the emit beep — the original single-clip behaviour.
         */
        private AudioClip ResolveReturnClip(SonarContact contact)
        {
            if (useSignatureReturnClip && contact.Signature != null && contact.Signature.returnPingClip != null)
                return contact.Signature.returnPingClip;

            if (useSeparateReturnClip && returnBeep != null)
                return returnBeep;

            return beep;
        }
    }
}
