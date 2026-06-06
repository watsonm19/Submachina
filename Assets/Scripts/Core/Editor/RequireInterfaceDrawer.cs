using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

/**
 * Property drawer for RequireInterfaceAttribute.
 * Ensures a MonoBehaviour field only ever holds a component implementing the required interface.
 *
 * WHY single-type-param OdinAttributeDrawer<TAttribute>:
 *   The two-type-param version OdinAttributeDrawer<TAttribute, MonoBehaviour> has a sealed
 *   CanDrawAttributeProperty that checks type compatibility against the property's runtime
 *   value type. Once a concrete subtype (e.g. SynthGenerator) is stored, Odin creates a
 *   polymorphic alias entry whose TypeOfValue is SynthGenerator. Odin's internal check
 *   SynthGenerator.IsAssignableFrom(MonoBehaviour) returns false, and the drawer is silently
 *   dropped from the chain — DrawPropertyLayout stops being called entirely.
 *   The single-type-param version has no such type filter.
 *
 * WHY Property.BaseValueEntry (not Property.ValueEntry):
 *   Property.ValueEntry returns the alias entry for the current runtime type, which changes
 *   as different components are assigned. Property.BaseValueEntry always returns the entry
 *   for the declared type (MonoBehaviour), which is stable and correct for reading/writing
 *   the actual serialized field.
 *
 * Correction paths:
 *   1. Update() + correction pass each draw: catches direct serialized-field writes from
 *      Unity's ObjectField that bypass SmartValue and ApplyChanges entirely.
 *   2. EndChangeCheck inline: standard drag/pick path where GUI.changed fires.
 *   3. OnValueChanged backstop: fires after any change that does go through ApplyChanges.
 */
[DrawerPriority(DrawerPriorityLevel.AttributePriority)]
public class RequireInterfaceDrawer : OdinAttributeDrawer<RequireInterfaceAttribute>
{
    bool _correcting;

    // Cached result of the Layout-pass correction. Unity IMGUI requires that Layout and Repaint
    // draw exactly the same controls. Update() can shift the value between events if the user
    // clears the field, so we run Update() + correction only during Layout and cache here.
    MonoBehaviour _layoutValue;

    // BaseValueEntry is the declared-type (MonoBehaviour) entry — stable across runtime type
    // changes. OnValueChanged and Update() live on the concrete PropertyValueEntry class, not
    // on the IPropertyValueEntry interface, so we cast to access them.
    PropertyValueEntry BaseEntry => Property.BaseValueEntry as PropertyValueEntry;

    /**
     * Only draw on MonoBehaviour-typed properties — not on the List<MonoBehaviour> field itself.
     * Odin automatically propagates this PropertyAttribute to each list element, so gating here
     * lets the drawer handle elements while the list falls through to Odin's default list drawer
     * (preserving [ListDrawerSettings], reordering, add/remove buttons, etc.).
     */
    protected override bool CanDrawAttributeProperty(InspectorProperty property)
    {
        if (property.BaseValueEntry == null) return false;
        return typeof(MonoBehaviour).IsAssignableFrom(property.BaseValueEntry.BaseValueType);
    }

    /** Seed the cached layout value and subscribe to value-commit events. */
    protected override void Initialize()
    {
        var entry = BaseEntry;
        if (entry != null)
        {
            _layoutValue = Resolve(entry.WeakSmartValue as MonoBehaviour);
            entry.OnValueChanged += _ => OnValueCommitted();
        }
    }

    /**
     * Fires after Odin commits a value through ApplyChanges().
     * Corrects the value if it doesn't implement the required interface.
     */
    void OnValueCommitted()
    {
        if (_correcting) return;
        _correcting = true;

        var entry = BaseEntry;
        if (entry != null)
        {
            var corrected = Resolve(entry.WeakSmartValue as MonoBehaviour);
            if (!ReferenceEquals(corrected, entry.WeakSmartValue))
                entry.WeakSmartValue = corrected;
        }

        _correcting = false;
    }

    /**
     * Core resolution logic used by all correction paths.
     * Returns the value unchanged if it already implements the required interface.
     * Searches the same GameObject via GetComponent for an implementing component.
     * Returns null with a warning if nothing on the GameObject qualifies.
     *
     * Example: field holds AudioSource, GO also has SynthGenerator (: ISoundGenerator)
     *          → silently returns SynthGenerator.
     */
    MonoBehaviour Resolve(MonoBehaviour value)
    {
        if (value == null) return null;

        var requiredType = Attribute.RequiredType;

        if (requiredType.IsAssignableFrom(value.GetType())) return value;

        var resolved = value.GetComponent(requiredType) as MonoBehaviour;
        if (resolved != null) return resolved;

        Debug.LogWarning(
            $"[RequireInterface] '{value.gameObject.name}' has no {requiredType.Name} component — assignment rejected.",
            value);
        return null;
    }

    protected override void DrawPropertyLayout(GUIContent label)
    {
        var requiredType = Attribute.RequiredType;
        var entry = BaseEntry;

        // Update() and the correction pass run only during Layout.
        // Unity IMGUI requires Layout and Repaint to draw identical controls. Calling Update()
        // on every event can shift the value between Layout and Repaint (e.g. user clears the
        // field), causing the warning box to appear on one event but not the other — producing
        // the "Getting control N's position in a group with only N controls" error.
        // We cache the corrected value from Layout and reuse it for all subsequent events.
        if (Event.current.type == EventType.Layout)
        {
            entry?.Update();

            var current = entry?.WeakSmartValue as MonoBehaviour;
            var corrected = Resolve(current);
            if (!ReferenceEquals(corrected, current) && entry != null)
                entry.WeakSmartValue = corrected;

            _layoutValue = corrected;
        }

        // Label annotated with required interface name for discoverability.
        var displayLabel = label != null
            ? new GUIContent(label.text, $"[{requiredType.Name}]  {label.tooltip}".TrimEnd())
            : new GUIContent(string.Empty, $"[{requiredType.Name}]");

        // Draw the object field. EndChangeCheck handles the standard drag/pick path.
        EditorGUI.BeginChangeCheck();
        var newValue = (MonoBehaviour)SirenixEditorFields.UnityObjectField(
            displayLabel, _layoutValue, typeof(MonoBehaviour), allowSceneObjects: true);

        if (EditorGUI.EndChangeCheck() && entry != null)
            entry.WeakSmartValue = Resolve(newValue);

        // Warning when unset — driven by the Layout-pass value so it matches control count.
        if (_layoutValue == null)
            SirenixEditorGUI.WarningMessageBox($"Assign a component implementing {requiredType.Name}.");
    }
}
