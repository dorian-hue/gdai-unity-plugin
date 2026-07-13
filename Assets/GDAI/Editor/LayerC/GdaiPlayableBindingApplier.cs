// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · P2-E/§5.4 · Contract-driven binding applier (Editor, Layer C).
//
// Applies the rev4 contract's scene_bindings (7) and value_bindings onto the OWNED
// composed objects — exactly what the contract declares, resolved by component TYPE and
// owned-object identity, never by heuristics. For each binding:
//   * the source component is the single owned object carrying that generated type;
//   * the destination field's real type is read by reflection, so a Transform field gets
//     a Transform, a Camera field gets the Camera component, a LayerMask field gets a mask
//     built from the contract's layer names — no field-type guessing, no wrong-type writes;
//   * the write is verified to have "stuck" (an unassignable target leaves the ref null →
//     that binding is a failure, never a silent false-pass).
// Fail-closed: any unresolved/ambiguous source-or-target, missing field, undefined layer, or
// non-assignable target is an error and that field stays unbound. Marks the scene dirty; the
// CTA saves it. Never adopts unmarked objects (resolution is over GDAI-marked objects only).
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC
{
    public static class GdaiPlayableBindingApplier
    {
        public class Result
        {
            public bool Ok;
            public int SceneBound, SceneTotal, ValueBound, ValueTotal;
            public readonly List<string> Bound = new List<string>();
            public readonly List<string> Errors = new List<string>();
            public string Summary => Errors.Count == 0
                ? $"scene {SceneBound}/{SceneTotal}, value {ValueBound}/{ValueTotal} bound"
                : string.Join("; ", Errors);
        }

        public static Result Apply(GdaiPlayableContract c)
        {
            var r = new Result();
            if (c == null) { r.Errors.Add("null contract"); return r; }
            r.SceneTotal = c.scene_bindings?.Count ?? 0;
            r.ValueTotal = c.value_bindings?.Count ?? 0;

            foreach (var b in c.scene_bindings ?? new List<GdaiPlayableContract.SceneBindingSpec>())
                if (TryApplyScene(b, r, out string err)) r.SceneBound++; else r.Errors.Add(err);

            foreach (var v in c.value_bindings ?? new List<GdaiPlayableContract.ValueBindingSpec>())
                if (TryApplyValue(v, r, out string err)) r.ValueBound++; else r.Errors.Add(err);

            r.Ok = r.Errors.Count == 0 && r.SceneBound == r.SceneTotal && r.ValueBound == r.ValueTotal;
            return r;
        }

        // The single OWNED object carrying a component whose simple type-name is typeName.
        private static bool TryFindOwnedHost(string typeName, out GameObject go, out Type type, out string err)
        {
            go = null; err = null;
            type = GdaiSceneObjectComposer.ResolveComponentType(typeName, out var outcome);
            if (type == null) { err = $"type '{typeName}' unresolved ({outcome})"; return false; }
            GameObject found = null; int count = 0;
            foreach (var m in UnityEngine.Object.FindObjectsByType<GdaiGeneratedPlayableMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (m.GetComponent(type) != null) { found = m.gameObject; count++; }
            if (count == 0) { err = $"no owned object hosts {typeName}"; return false; }
            if (count > 1) { err = $"{count} owned objects host {typeName} — ambiguous, not binding"; return false; }
            go = found; return true;
        }

        private static bool TryApplyScene(GdaiPlayableContract.SceneBindingSpec b, Result r, out string err)
        {
            err = null;
            if (!TryFindOwnedHost(b.component, out var srcGo, out var srcType, out err)) return false;
            var src = srcGo.GetComponent(srcType);

            var field = srcType.GetField(b.field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) { err = $"{b.component}.{b.field} is not a field on {srcType.Name}"; return false; }

            if (!ResolveTargetGameObject(b, out var tgtGo, out err)) return false;
            var value = ResolveFieldValue(field.FieldType, tgtGo, out err);
            if (value == null) { if (err == null) err = $"{b.component}.{b.field}: no target of type {field.FieldType.Name} on '{tgtGo.name}'"; return false; }

            var so = new SerializedObject(src);
            var prop = so.FindProperty(b.field);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
            { err = $"{b.component}.{b.field} is not an object-reference serialized field"; return false; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
            if (prop.objectReferenceValue != value)   // an unassignable target does not stick → real failure
            { err = $"{b.component}.{b.field}: target '{value.name}' ({value.GetType().Name}) not assignable to {field.FieldType.Name}"; return false; }

            EditorSceneManager.MarkSceneDirty(srcGo.scene);
            r.Bound.Add($"{b.component}.{b.field} -> {value.name}");
            return true;
        }

        private static bool ResolveTargetGameObject(GdaiPlayableContract.SceneBindingSpec b, out GameObject go, out string err)
        {
            go = null; err = null;
            if (!string.IsNullOrEmpty(b.target_component))
                return TryFindOwnedHost(b.target_component, out go, out _, out err);
            string name = !string.IsNullOrEmpty(b.target_transform) ? b.target_transform
                        : !string.IsNullOrEmpty(b.target_object) ? b.target_object : null;
            if (name == null) { err = $"{b.component}.{b.field} declares no target"; return false; }
            go = GdaiSceneObjectComposer.FindOwned(name);
            if (go == null) { err = $"owned target '{name}' not found for {b.component}.{b.field}"; return false; }
            return true;
        }

        private static UnityEngine.Object ResolveFieldValue(Type fieldType, GameObject go, out string err)
        {
            err = null;
            if (fieldType == typeof(GameObject)) return go;
            if (fieldType == typeof(Transform)) return go.transform;
            if (typeof(Component).IsAssignableFrom(fieldType))
            {
                var comp = go.GetComponent(fieldType);
                if (comp == null) err = $"'{go.name}' has no {fieldType.Name}";
                return comp;
            }
            err = $"unsupported field type {fieldType.Name}";
            return null;
        }

        private static bool TryApplyValue(GdaiPlayableContract.ValueBindingSpec v, Result r, out string err)
        {
            err = null;
            if (!TryFindOwnedHost(v.component, out var go, out var type, out err)) return false;
            var src = go.GetComponent(type);
            var so = new SerializedObject(src);
            var prop = so.FindProperty(v.field);
            if (prop == null) { err = $"{v.component}.{v.field} is not a serialized field"; return false; }

            if (v.value_type == "LayerMask")
            {
                if (prop.propertyType != SerializedPropertyType.LayerMask)
                { err = $"{v.component}.{v.field} is not a LayerMask field"; return false; }
                int mask = 0;
                foreach (var ln in v.layers ?? new List<string>())
                {
                    int idx = LayerMask.NameToLayer(ln);
                    if (idx < 0) { err = $"value binding layer '{ln}' is not defined in this project"; return false; }
                    mask |= (1 << idx);
                }
                if (mask == 0) { err = $"{v.component}.{v.field}: empty layer mask"; return false; }
                prop.intValue = mask;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(go.scene);
                r.Bound.Add($"{v.component}.{v.field} = mask[{string.Join(",", v.layers)}]");
                return true;
            }
            err = $"unsupported value_type '{v.value_type}' for {v.component}.{v.field}";
            return false;
        }
    }
}
