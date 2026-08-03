// Synaptic AI Pro for Unity - InstanceID/EntityId Compatibility Helper
//
// Unity 6 系で `Object.GetInstanceID()` / `EditorUtility.InstanceIDToObject()` /
// `SerializedProperty.objectReferenceInstanceIDValue` が `[Obsolete(IsError=true)]` 化され
// CS0619 (= 抑制不能なハードエラー) を出すようになった。後継 API:
//   - `GetEntityId()`            (UnityEngine.Object)
//   - `EntityIdToObject(int)`    (UnityEditor.EditorUtility)
//   - `objectReferenceEntityIdValue` (UnityEditor.SerializedProperty)
// は Unity 6.5+ でのみ利用可能。Unity 2022.3 LTS / 6.0-6.4 LTS との両対応を維持するため、
// バージョン分岐したラッパーを 1 箇所に集約する。

using UnityEditor;
using UnityEngine;

namespace SynapticPro
{
    public static class IdCompat
    {
        // Object.GetInstanceID() の代替
        public static int GetIdCompat(this Object o)
        {
#if UNITY_6000_5_OR_NEWER
            // EntityId → int の implicit cast 自体が obsolete (CS0619) になったため、
            // 明示メソッド GetHashCode() で int を取り出す。EntityId のハッシュは内部 ID
            // と一致する設計 (将来 64-bit 化されたら下位 32-bit が落ちるが、現状の
            // instance_id 用途では一意性 / 識別子としては十分機能する)。
            return o.GetEntityId().GetHashCode();
#else
            return o.GetInstanceID();
#endif
        }

        // EditorUtility.InstanceIDToObject(int) の代替
        public static Object IdToObjectCompat(int id)
        {
#if UNITY_6000_5_OR_NEWER
            return EditorUtility.EntityIdToObject(id);
#else
            return EditorUtility.InstanceIDToObject(id);
#endif
        }

        // SerializedProperty.objectReferenceInstanceIDValue の代替
        public static int GetObjectReferenceIdCompat(this SerializedProperty sp)
        {
#if UNITY_6000_5_OR_NEWER
            return sp.objectReferenceEntityIdValue.GetHashCode();
#else
            return sp.objectReferenceInstanceIDValue;
#endif
        }
    }
}
