using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Submachina.Core
{
    /**
     * UnityEvent-wireable camera shake: fires an MMCameraShakeEvent that the scene's
     * MMCameraShaker (on the camera rig) picks up. Lets DirectorRules and sequence events
     * shake the camera without authoring a full MMF_Player feedback chain.
     */
    public class CameraShakeTrigger : MonoBehaviour
    {
        [Header("Shake")]
        [Tooltip("Shake length in seconds.")]
        [SerializeField] private float duration = 0.5f;

        [Tooltip("Overall shake strength.")]
        [SerializeField] private float amplitude = 1.2f;

        [Tooltip("Shake speed — higher is more frantic.")]
        [SerializeField] private float frequency = 22f;

        [Tooltip("Per-axis amplitude multipliers.")]
        [SerializeField] private Vector3 axisAmplitudes = new Vector3(1.2f, 1.2f, 0f);

        [Header("Channel")]
        [Tooltip("Int channel the MMCameraShaker listens on (default rigs use 0).")]
        [SerializeField] private int channel;

        /// <summary>Fires the configured shake — wire this to rule/finale UnityEvents.</summary>
#if ODIN_INSPECTOR
        [Button("Shake (test)")]
#endif
        public void Shake()
        {
            MMCameraShakeEvent.Trigger(duration, amplitude, frequency,
                axisAmplitudes.x, axisAmplitudes.y, axisAmplitudes.z,
                false, new MMChannelData(MMChannelModes.Int, channel, null));
        }

        /// <summary>Fires a shake with custom strength but the configured duration/frequency.</summary>
        public void ShakeWithAmplitude(float customAmplitude)
        {
            MMCameraShakeEvent.Trigger(duration, customAmplitude, frequency,
                axisAmplitudes.x, axisAmplitudes.y, axisAmplitudes.z,
                false, new MMChannelData(MMChannelModes.Int, channel, null));
        }
    }
}
