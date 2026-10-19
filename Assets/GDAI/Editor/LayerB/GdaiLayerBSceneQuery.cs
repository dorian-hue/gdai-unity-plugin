using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · Layer B/B2 · shared scene query.
// Finds Components of a runtime Type in the OPEN scene(s), including inactive objects,
// excluding Project-browser assets/prefabs. Uses Resources.FindObjectsOfTypeAll(Type)
// instead of the Unity 6 obsolete Object.FindObjectsByType(Type, FindObjectsInactive,
// FindObjectsSortMode) overload (CS0618). Reflection-friendly (accepts System.Type),
// no compile-time dependency on generated classes.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    internal static class GdaiLayerBSceneQuery
    {
        public static List<Component> FindSceneComponents(Type type)
        {
            var result = new List<Component>();
            if (type == null) return result;

            foreach (var obj in Resources.FindObjectsOfTypeAll(type))
            {
                if (obj is Component c &&
                    c.gameObject.scene.IsValid() &&
                    !EditorUtility.IsPersistent(c))
                {
                    result.Add(c);
                }
            }

            return result;
        }
    }
}
