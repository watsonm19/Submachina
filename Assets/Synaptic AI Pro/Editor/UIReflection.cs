// UIReflection.cs
// Editor-only reflection helper for accessing UnityEngine.UI and TMPro types
// without requiring asmdef references to Unity.ugui / Unity.TextMeshPro.
//
// Design goal:
//   Unity 6.4+ ships com.unity.ugui 2.0.0 which has NO asmdef (integrated into
//   the engine). Referencing "Unity.ugui" by name in an asmdef causes CS0234
//   cascades. By going fully through reflection we:
//     - avoid asmdef "references" entries
//     - avoid #if symbol gymnastics
//     - work whether ugui is the legacy package, the 2.0.0 integrated build,
//       or absent entirely.
//
// All Type / MemberInfo / enum lookups are memoized in static caches so the
// ~200 call sites in NexusExecutor.cs don't pay reflection cost per call.
//
// IMPORTANT: This file MUST NOT contain `using UnityEngine.UI;` or `using TMPro;`.
//            The whole point is to avoid compile-time dependencies on those
//            assemblies.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace SynapticPro
{
    public static class UIReflection
    {
        // ---------------------------------------------------------------------
        // Caches
        // ---------------------------------------------------------------------

        // Type cache. Stores nulls too, so we don't re-scan the AppDomain for
        // a missing type on every call.
        private static readonly Dictionary<string, Type> _typeCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        // (declaringTypeFullName, memberName) -> MemberInfo
        private static readonly Dictionary<string, PropertyInfo> _propCache =
            new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);

        private static readonly Dictionary<string, FieldInfo> _fieldCache =
            new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

        // (typeFullName, memberName) -> enum boxed value
        private static readonly Dictionary<string, object> _enumCache =
            new Dictionary<string, object>(StringComparer.Ordinal);

        // Warnings we've already emitted, so logs don't spam.
        private static readonly HashSet<string> _warned =
            new HashSet<string>(StringComparer.Ordinal);

        // Common short-name aliases. Callers can pass either short or fully
        // qualified names. Order: try alias first, then the raw string.
        private static readonly Dictionary<string, string> _uiAliases =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Text",                  "UnityEngine.UI.Text" },
                { "Image",                 "UnityEngine.UI.Image" },
                { "RawImage",              "UnityEngine.UI.RawImage" },
                { "Button",                "UnityEngine.UI.Button" },
                { "InputField",            "UnityEngine.UI.InputField" },
                { "Slider",                "UnityEngine.UI.Slider" },
                { "Toggle",                "UnityEngine.UI.Toggle" },
                { "ScrollRect",            "UnityEngine.UI.ScrollRect" },
                { "Scrollbar",             "UnityEngine.UI.Scrollbar" },
                { "Dropdown",              "UnityEngine.UI.Dropdown" },
                { "CanvasScaler",          "UnityEngine.UI.CanvasScaler" },
                { "GraphicRaycaster",      "UnityEngine.UI.GraphicRaycaster" },
                { "VerticalLayoutGroup",   "UnityEngine.UI.VerticalLayoutGroup" },
                { "HorizontalLayoutGroup", "UnityEngine.UI.HorizontalLayoutGroup" },
                { "GridLayoutGroup",       "UnityEngine.UI.GridLayoutGroup" },
                { "ContentSizeFitter",     "UnityEngine.UI.ContentSizeFitter" },
                { "LayoutElement",         "UnityEngine.UI.LayoutElement" },
                { "AspectRatioFitter",     "UnityEngine.UI.AspectRatioFitter" },
                { "Mask",                  "UnityEngine.UI.Mask" },
                { "RectMask2D",            "UnityEngine.UI.RectMask2D" },
                { "Outline",               "UnityEngine.UI.Outline" },
                { "Shadow",                "UnityEngine.UI.Shadow" },
                { "Selectable",            "UnityEngine.UI.Selectable" },
                { "Graphic",               "UnityEngine.UI.Graphic" },
                { "MaskableGraphic",       "UnityEngine.UI.MaskableGraphic" },
            };

        private static readonly Dictionary<string, string> _tmpAliases =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "TextMeshProUGUI", "TMPro.TextMeshProUGUI" },
                { "TextMeshPro",     "TMPro.TextMeshPro" },
                { "TMP_Text",        "TMPro.TMP_Text" },
                { "TMP_FontAsset",   "TMPro.TMP_FontAsset" },
                { "TMP_InputField",  "TMPro.TMP_InputField" },
                { "TMP_Dropdown",    "TMPro.TMP_Dropdown" },
                { "FontStyles",      "TMPro.FontStyles" },
                { "TextAlignmentOptions", "TMPro.TextAlignmentOptions" },
            };

        // Assemblies to probe explicitly first. Both legacy and integrated names
        // are listed; whichever exists wins.
        private static readonly string[] _uiAssemblyHints =
        {
            "UnityEngine.UI",
            "Unity.ugui",
            "UnityEngine.UIModule",
        };

        private static readonly string[] _tmpAssemblyHints =
        {
            "Unity.TextMeshPro",
            "Unity.ugui",
        };

        // ---------------------------------------------------------------------
        // Type resolution
        // ---------------------------------------------------------------------

        public static Type GetUIType(string shortName)
        {
            if (string.IsNullOrEmpty(shortName)) return null;
            string full = _uiAliases.TryGetValue(shortName, out var f) ? f : shortName;
            if (full.IndexOf('.') < 0) full = "UnityEngine.UI." + full;
            return ResolveType(full, _uiAssemblyHints);
        }

        public static Type GetTMPType(string shortName)
        {
            if (string.IsNullOrEmpty(shortName)) return null;
            string full = _tmpAliases.TryGetValue(shortName, out var f) ? f : shortName;
            if (full.IndexOf('.') < 0) full = "TMPro." + full;
            return ResolveType(full, _tmpAssemblyHints);
        }

        public static new Type GetType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            string[] hints = null;
            if (fullName.StartsWith("UnityEngine.UI", StringComparison.Ordinal)) hints = _uiAssemblyHints;
            else if (fullName.StartsWith("TMPro", StringComparison.Ordinal))     hints = _tmpAssemblyHints;
            return ResolveType(fullName, hints);
        }

        private static Type ResolveType(string fullName, string[] assemblyHints)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            if (_typeCache.TryGetValue(fullName, out var cached))
                return cached;

            Type found = null;

            // 1) Direct probe (might resolve if currently loaded with default asm)
            found = Type.GetType(fullName, false);

            // 2) Try assembly-qualified probes
            if (found == null && assemblyHints != null)
            {
                foreach (var asm in assemblyHints)
                {
                    found = Type.GetType(fullName + ", " + asm, false);
                    if (found != null) break;
                }
            }

            // 3) AppDomain-wide scan (handles ugui 2.0.0 integrated case)
            if (found == null)
            {
                var asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    try
                    {
                        var t = asms[i].GetType(fullName, false);
                        if (t != null) { found = t; break; }
                    }
                    catch { /* dynamic asm or load issue, ignore */ }
                }
            }

            _typeCache[fullName] = found; // cache nulls too
            if (found == null) WarnOnce("type:" + fullName, "[UIReflection] Type not found: " + fullName);
            return found;
        }

        // ---------------------------------------------------------------------
        // Component helpers
        // ---------------------------------------------------------------------

        public static Component AddComponent(GameObject go, string typeName)
        {
            if (go == null) return null;
            var t = GetType(typeName);
            if (t == null) return null;
            try { return go.AddComponent(t); }
            catch (Exception e)
            {
                WarnOnce("addcomp:" + typeName, "[UIReflection] AddComponent failed for " + typeName + ": " + e.Message);
                return null;
            }
        }

        public static Component GetComponent(GameObject go, string typeName)
        {
            if (go == null) return null;
            var t = GetType(typeName);
            if (t == null) return null;
            return go.GetComponent(t);
        }

        public static Component[] GetComponents(GameObject go, string typeName)
        {
            if (go == null) return Array.Empty<Component>();
            var t = GetType(typeName);
            if (t == null) return Array.Empty<Component>();
            return go.GetComponents(t);
        }

        public static UnityEngine.Object[] FindObjectsOfType(string typeName, bool includeInactive = false)
        {
            var t = GetType(typeName);
            if (t == null) return Array.Empty<UnityEngine.Object>();
            try
            {
                return UnityEngine.Object.FindObjectsOfType(t, includeInactive);
            }
            catch (Exception e)
            {
                WarnOnce("find:" + typeName, "[UIReflection] FindObjectsOfType failed for " + typeName + ": " + e.Message);
                return Array.Empty<UnityEngine.Object>();
            }
        }

        // ---------------------------------------------------------------------
        // Type checks
        // ---------------------------------------------------------------------

        public static bool IsInstanceOf(object obj, string typeName)
        {
            if (obj == null) return false;
            var t = GetType(typeName);
            return t != null && t.IsInstanceOfType(obj);
        }

        public static bool IsAssignableTo(Type t, string typeName)
        {
            if (t == null) return false;
            var target = GetType(typeName);
            return target != null && target.IsAssignableFrom(t);
        }

        // ---------------------------------------------------------------------
        // Property / Field access
        // ---------------------------------------------------------------------

        private static PropertyInfo GetCachedProperty(Type t, string propName)
        {
            if (t == null || string.IsNullOrEmpty(propName)) return null;
            string key = t.FullName + "::" + propName;
            if (_propCache.TryGetValue(key, out var p)) return p;
            p = t.GetProperty(propName,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            _propCache[key] = p;
            return p;
        }

        private static FieldInfo GetCachedField(Type t, string fieldName)
        {
            if (t == null || string.IsNullOrEmpty(fieldName)) return null;
            string key = t.FullName + "::" + fieldName;
            if (_fieldCache.TryGetValue(key, out var f)) return f;
            f = t.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            _fieldCache[key] = f;
            return f;
        }

        public static object GetProperty(object target, string propName)
        {
            if (target == null) return null;
            var p = GetCachedProperty(target.GetType(), propName);
            if (p == null || !p.CanRead) return null;
            try { return p.GetValue(target); }
            catch (Exception e)
            {
                WarnOnce("getprop:" + target.GetType().FullName + "." + propName,
                    "[UIReflection] GetProperty " + propName + " failed: " + e.Message);
                return null;
            }
        }

        public static T GetProperty<T>(object target, string propName)
        {
            var v = GetProperty(target, propName);
            if (v is T tv) return tv;
            return default;
        }

        public static void SetProperty(object target, string propName, object value)
        {
            if (target == null) return;
            var p = GetCachedProperty(target.GetType(), propName);
            if (p == null || !p.CanWrite) return;
            try
            {
                object coerced = CoerceValue(value, p.PropertyType);
                p.SetValue(target, coerced);
            }
            catch (Exception e)
            {
                WarnOnce("setprop:" + target.GetType().FullName + "." + propName,
                    "[UIReflection] SetProperty " + propName + " failed: " + e.Message);
            }
        }

        public static object GetField(object target, string fieldName)
        {
            if (target == null) return null;
            var f = GetCachedField(target.GetType(), fieldName);
            if (f == null) return null;
            try { return f.GetValue(target); }
            catch (Exception e)
            {
                WarnOnce("getfield:" + target.GetType().FullName + "." + fieldName,
                    "[UIReflection] GetField " + fieldName + " failed: " + e.Message);
                return null;
            }
        }

        public static void SetField(object target, string fieldName, object value)
        {
            if (target == null) return;
            var f = GetCachedField(target.GetType(), fieldName);
            if (f == null) return;
            try
            {
                object coerced = CoerceValue(value, f.FieldType);
                f.SetValue(target, coerced);
            }
            catch (Exception e)
            {
                WarnOnce("setfield:" + target.GetType().FullName + "." + fieldName,
                    "[UIReflection] SetField " + fieldName + " failed: " + e.Message);
            }
        }

        // Best-effort coercion for common cases: enum-by-name, int-to-enum, primitives.
        private static object CoerceValue(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType == null) return value;
            var vt = value.GetType();
            if (targetType.IsAssignableFrom(vt)) return value;

            if (targetType.IsEnum)
            {
                if (value is string s)
                {
                    try { return Enum.Parse(targetType, s, true); }
                    catch { return value; }
                }
                try { return Enum.ToObject(targetType, value); }
                catch { return value; }
            }

            try { return Convert.ChangeType(value, targetType); }
            catch { return value; }
        }

        // ---------------------------------------------------------------------
        // Enum lookup
        // ---------------------------------------------------------------------

        // GetEnum("UnityEngine.UI.Image+Type", "Sliced")
        // GetEnum("TMPro.FontStyles", "Bold")
        public static object GetEnum(string typeName, string memberName)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(memberName)) return null;
            string key = typeName + "::" + memberName;
            if (_enumCache.TryGetValue(key, out var v)) return v;

            // Support nested-type "+" syntax explicitly
            Type t = GetType(typeName);
            if (t == null)
            {
                // Try splitting at '+' for nested types if not found
                int plus = typeName.IndexOf('+');
                if (plus > 0)
                {
                    var outerName = typeName.Substring(0, plus);
                    var innerName = typeName.Substring(plus + 1);
                    var outer = GetType(outerName);
                    if (outer != null)
                    {
                        t = outer.GetNestedType(innerName,
                            BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }
            }

            object result = null;
            if (t != null && t.IsEnum)
            {
                try { result = Enum.Parse(t, memberName, true); }
                catch (Exception e)
                {
                    WarnOnce("enum:" + key, "[UIReflection] Enum parse failed " + key + ": " + e.Message);
                }
            }
            else if (t == null)
            {
                WarnOnce("enumtype:" + typeName, "[UIReflection] Enum type not found: " + typeName);
            }

            _enumCache[key] = result;
            return result;
        }

        // ---------------------------------------------------------------------
        // UnityEvent.AddListener (Action -> UnityAction via Delegate.CreateDelegate)
        // ---------------------------------------------------------------------

        public static void AddUnityEventListener(object owner, string eventPropName, Action callback)
        {
            if (owner == null || callback == null) return;

            // 1) Resolve event-bearing object (e.g. Button.onClick)
            object eventObj = GetProperty(owner, eventPropName);
            if (eventObj == null)
            {
                // Some UnityEvent members are fields, not properties
                eventObj = GetField(owner, eventPropName);
            }
            if (eventObj == null)
            {
                WarnOnce("uevent:" + owner.GetType().FullName + "." + eventPropName,
                    "[UIReflection] Event member not found: " + eventPropName);
                return;
            }

            // 2) Resolve UnityAction type
            Type unityActionType =
                Type.GetType("UnityEngine.Events.UnityAction, UnityEngine") ??
                Type.GetType("UnityEngine.Events.UnityAction, UnityEngine.CoreModule") ??
                ResolveType("UnityEngine.Events.UnityAction", null);

            if (unityActionType == null)
            {
                WarnOnce("uaction", "[UIReflection] UnityAction type not resolvable");
                return;
            }

            // 3) Wrap the Action as a UnityAction delegate
            Delegate uaDelegate;
            try
            {
                uaDelegate = Delegate.CreateDelegate(unityActionType, callback.Target, callback.Method);
            }
            catch (Exception e)
            {
                WarnOnce("uadel", "[UIReflection] CreateDelegate failed: " + e.Message);
                return;
            }

            // 4) Invoke AddListener(UnityAction)
            var addMethod = eventObj.GetType().GetMethod("AddListener", new[] { unityActionType });
            if (addMethod == null)
            {
                WarnOnce("uevtadd:" + eventObj.GetType().FullName,
                    "[UIReflection] AddListener(UnityAction) not found on " + eventObj.GetType().FullName);
                return;
            }

            try { addMethod.Invoke(eventObj, new object[] { uaDelegate }); }
            catch (Exception e)
            {
                WarnOnce("uevtinv:" + eventObj.GetType().FullName,
                    "[UIReflection] AddListener invoke failed: " + e.Message);
            }
        }

        // ---------------------------------------------------------------------
        // GameObject(string, params Type[]) constructor by type-name list
        // ---------------------------------------------------------------------

        public static GameObject CreateGameObjectWithTypes(string name, params string[] typeNames)
        {
            // Resolve as many types as possible up front
            var resolved = new List<Type>(typeNames != null ? typeNames.Length : 0);
            if (typeNames != null)
            {
                foreach (var tn in typeNames)
                {
                    var t = GetType(tn);
                    if (t != null) resolved.Add(t);
                    // missing components are simply skipped (warn already emitted by ResolveType)
                }
            }

            GameObject go;
            if (resolved.Count == 0)
            {
                go = new GameObject(name);
            }
            else
            {
                go = new GameObject(name, resolved.ToArray());
            }
            return go;
        }

        // ---------------------------------------------------------------------
        // Diagnostics
        // ---------------------------------------------------------------------

        private static void WarnOnce(string key, string message)
        {
            if (_warned.Add(key))
            {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// Optional: clear all caches. Useful for editor reload diagnostics.
        /// </summary>
        public static void ClearCaches()
        {
            _typeCache.Clear();
            _propCache.Clear();
            _fieldCache.Clear();
            _enumCache.Clear();
            _warned.Clear();
        }
    }
}
