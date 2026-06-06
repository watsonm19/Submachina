using UnityEngine;
using Sirenix.OdinInspector;

/**
 * ScriptableObject that fully describes one melee attack motion as a set of
 * AnimationCurves evaluated over normalized time (0-1). Each curve controls one
 * axis of the weapon's local transform, scaled by per-axis amplitudes.
 *
 * Examples of motions you'd create from this:
 *   "Slash Right"   - X sweeps left-to-right, Z rotation arcs, mirrorRandomly = true
 *   "Stab Forward"  - X flat, Y dips then returns, scale stretches forward
 *   "Wide Slice"    - X full range, slower duration, wider amplitude
 */
[CreateAssetMenu(fileName = "NewAttackMotion", menuName = "Combat/Attack Motion Data")]
public class AttackMotionData : ScriptableObject
{
    // =====================
    // Position Curves
    // =====================

    [FoldoutGroup("Position")]
    [Tooltip("Horizontal sweep curve over normalized time (0-1). " +
             "Example: 0 -> -1 -> 1 -> 0 for a left-right slash.")]
    public AnimationCurve positionXCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.4f, -1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f));

    [FoldoutGroup("Position")]
    [Tooltip("Vertical sweep curve over normalized time (0-1). " +
             "Example: 0 -> -0.5 -> 0 for a dip-and-return stab.")]
    public AnimationCurve positionYCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.5f, -0.3f), new Keyframe(1f, 0f));

    [FoldoutGroup("Position")]
    [Tooltip("World-unit multiplier applied to both X and Y position curves.")]
    public float positionAmplitude = 0.5f;

    // =====================
    // Rotation Curves
    // =====================

    [FoldoutGroup("Rotation")]
    [Tooltip("Pitch rotation (X-axis) curve over normalized time (0-1). " +
             "Example: 0 -> -1 -> 0 for a downward chop that returns to neutral.")]
    public AnimationCurve rotationXCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(1f, 0f));

    [FoldoutGroup("Rotation")]
    [Tooltip("Yaw rotation (Y-axis) curve over normalized time (0-1). " +
             "Example: 0 -> 1 -> 0 for a horizontal sweeping twist.")]
    public AnimationCurve rotationYCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(1f, 0f));

    [FoldoutGroup("Rotation")]
    [Tooltip("Roll rotation (Z-axis) curve over normalized time (0-1). " +
             "Example: 0 -> -1 -> 0.5 -> 0 for a slashing arc.")]
    public AnimationCurve rotationZCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.3f, -1f), new Keyframe(0.7f, 0.5f), new Keyframe(1f, 0f));

    [FoldoutGroup("Rotation")]
    [Tooltip("Degree multiplier applied to all rotation curves. " +
             "Example: 45 means a curve value of 1.0 produces 45 degrees.")]
    public float rotationAmplitude = 45f;

    // =====================
    // Scale Curve
    // =====================

    [FoldoutGroup("Scale")]
    [Tooltip("Optional stretch/squash punch curve over normalized time (0-1). " +
             "Value of 0 = no change from base scale, 1 = full amplitude added.")]
    public AnimationCurve scaleCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f));

    [FoldoutGroup("Scale")]
    [Tooltip("Maximum additional scale added at curve peak. " +
             "Example: 0.3 means the weapon stretches to 130% at curve value 1.0.")]
    public float scaleAmplitude = 0.2f;

    // =====================
    // Timing
    // =====================

    [FoldoutGroup("Timing")]
    [Tooltip("Total duration of the attack motion in seconds.")]
    public float duration = 0.35f;

    [FoldoutGroup("Timing")]
    [Tooltip("Normalized time range where the hitbox is active. " +
             "Example: (0.2, 0.7) means active from 20% to 70% of the motion.")]
    [MinMaxSlider(0f, 1f)]
    public Vector2 activeWindow = new Vector2(0.2f, 0.7f);

    // =====================
    // Randomness
    // =====================

    [FoldoutGroup("Randomness")]
    [Tooltip("Random offset range added to position amplitude each swing. " +
             "Example: 0.1 means amplitude varies by +/- 10%.")]
    [Range(0f, 0.5f)]
    public float positionJitter = 0.05f;

    [FoldoutGroup("Randomness")]
    [Tooltip("Random offset range added to rotation amplitude each swing. " +
             "Example: 0.1 means rotation varies by +/- 10%.")]
    [Range(0f, 0.5f)]
    public float rotationJitter = 0.05f;

    [FoldoutGroup("Randomness")]
    [Tooltip("Random offset range added to duration each swing. " +
             "Example: 0.1 means duration varies by +/- 10%.")]
    [Range(0f, 0.3f)]
    public float durationJitter = 0.05f;

    [FoldoutGroup("Randomness")]
    [Tooltip("When true, the X position curve is randomly flipped horizontally " +
             "to create left/right variation from a single motion asset.")]
    public bool mirrorRandomly = false;

    // =====================
    // Damage
    // =====================

    [FoldoutGroup("Damage")]
    [Tooltip("Base damage dealt by this attack motion.")]
    public int baseDamage = 1;

    [FoldoutGroup("Damage")]
    [Tooltip("Knockback impulse magnitude applied to targets on hit. " +
             "Direction is calculated from attacker toward the target.")]
    public float knockbackForce = 5f;

    // =====================
    // Runtime Helpers
    // =====================

    /**
     * Generates a randomized snapshot of this motion's parameters for one swing.
     * Called once at attack start so the entire swing is consistent — no per-frame jitter.
     *
     * Returns a struct with jittered amplitude, rotation, duration, and mirror state
     * that the controller uses to evaluate curves each frame.
     */
    public SwingInstance RollSwingInstance()
    {
        return new SwingInstance
        {
            positionAmp = positionAmplitude * (1f + Random.Range(-positionJitter, positionJitter)),
            rotationAmp = rotationAmplitude * (1f + Random.Range(-rotationJitter, rotationJitter)),
            scaleAmp = scaleAmplitude,
            swingDuration = duration * (1f + Random.Range(-durationJitter, durationJitter)),
            mirrored = mirrorRandomly && Random.value > 0.5f,
            activeWindow = activeWindow,
            baseDamage = baseDamage,
            knockbackForce = knockbackForce
        };
    }

    /**
     * Immutable snapshot of one swing's randomized parameters.
     * Created once at attack start, consumed each frame during the swing.
     */
    public struct SwingInstance
    {
        public float positionAmp;
        public float rotationAmp;
        public float scaleAmp;
        public float swingDuration;
        public bool mirrored;
        public Vector2 activeWindow;
        public int baseDamage;
        public float knockbackForce;
    }
}
