using System;
using UnityEngine;

/**
 * Constrains a [SerializeField] MonoBehaviour field so it only accepts objects that implement
 * a specific interface. The companion RequireInterfaceDrawer auto-resolves the correct component
 * when a wrong component or bare GameObject is dropped onto the field in the Inspector.
 *
 * Usage:
 *   [SerializeField, RequireInterface(typeof(ISequencer))]
 *   MonoBehaviour sequencerObject;
 *
 *   ISequencer Sequencer => sequencerObject as ISequencer;
 */
[AttributeUsage(AttributeTargets.Field)]
public class RequireInterfaceAttribute : PropertyAttribute
{
    public readonly Type RequiredType;

    public RequireInterfaceAttribute(Type requiredType)
    {
        RequiredType = requiredType;
    }
}
