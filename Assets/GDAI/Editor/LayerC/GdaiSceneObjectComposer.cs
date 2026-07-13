// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · P2-C · Scene-object graph composer (Editor, Layer C).
//
// Creates the exact five canonical host objects the rev3 contract declares —
// Player, InputManager, GameIntegrationController, EnemyManager, Main Camera —
// BEFORE any binder runs (binders bind existing objects, they never create them).
// Every created object is stamped with GdaiGeneratedPlayableMarker so a later Sync
// re-owns it and, conversely, a same-named UNMARKED human object is never adopted.
// Components are added by the exact type NAME the contract lists (resolved via
// TypeCache over the generated + engine assemblies) — never inferred from a class
// name, never guessed. The player's host components (CharacterStateMachine,
// CombatLocatorSystem) are co-located on the single Player object so movement and
// the dash sweep act on the visible sprite.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Runtime;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC
{
    public static class GdaiSceneObjectComposer
    {
        public class Result
        {
            public bool Ok;
            public readonly List<string> Created = new List<string>();
            public readonly List<string> Errors = new List<string>();
            public string Summary => Errors.Count == 0
                ? "created/verified " + Created.Count + " objects"
                : string.Join("; ", Errors);
        }

        // Engine components the contract can name that are not generated MonoBehaviours.
        private static readonly Dictionary<string, Type> EngineTypes = new Dictionary<string, Type>
        {
            { "Camera", typeof(Camera) },
            { "SpriteRenderer", typeof(SpriteRenderer) },
            { "Rigidbody2D", typeof(Rigidbody2D) },
            { "BoxCollider2D", typeof(BoxCollider2D) },
            { "CircleCollider2D", typeof(CircleCollider2D) },
        };

        /// <summary>Resolve a component type by its exact contract name across generated + engine assemblies.</summary>
        public static Type ResolveComponentType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (EngineTypes.TryGetValue(name, out var et)) return et;
            // generated MonoBehaviours live in the project's default assemblies; match by simple name,
            // require it to be a Component, and require an unambiguous single match.
            var matches = TypeCache.GetTypesDerivedFrom<Component>()
                .Where(t => t.Name == name && !t.IsAbstract)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        /// <summary>Create/verify the five canonical objects. Existing GDAI-marked objects are reused.</summary>
        public static Result Compose(GdaiPlayableContract contract, string profileId, string snapshotId)
        {
            var r = new Result();
            if (contract == null) { r.Errors.Add("null contract"); return r; }

            // Player position mirrors LayerC's historical canonical spawn.
            var positions = new Dictionary<string, Vector3>
            {
                { contract.player.object_name, new Vector3(-1.5f, 0f, 0f) },
                { "Main Camera", new Vector3(0f, 0f, -10f) },
            };

            foreach (var spec in contract.scene_objects)
            {
                var go = FindOwnedOrCreate(spec.name, positions.TryGetValue(spec.name, out var p) ? p : Vector3.zero,
                    profileId, snapshotId, RoleOf(contract, spec.name), out bool created);
                if (go == null)
                {
                    r.Errors.Add("object '" + spec.name + "' exists but is not GDAI-owned (unmarked human object) — refusing to adopt");
                    continue;
                }
                if (created) r.Created.Add(spec.name);

                // tag (builtin tags like Player must exist; unknown tag is a hard error, not silent)
                var tag = spec.name == contract.player.object_name ? contract.player.tag : null;
                if (!string.IsNullOrEmpty(tag))
                {
                    if (!TagExists(tag)) { r.Errors.Add("required tag '" + tag + "' is not defined in this project"); continue; }
                    go.tag = tag;
                }

                foreach (var compName in spec.components)
                {
                    var t = ResolveComponentType(compName);
                    if (t == null) { r.Errors.Add("component type '" + compName + "' for '" + spec.name + "' unresolved/ambiguous"); continue; }
                    if (go.GetComponent(t) == null) Undo.AddComponent(go, t);
                }
            }

            if (r.Errors.Count > 0) return r;

            // verify each declared object now exists with its declared components
            foreach (var spec in contract.scene_objects)
            {
                var go = FindOwned(spec.name);
                if (go == null) { r.Errors.Add("post-verify: object '" + spec.name + "' missing"); continue; }
                foreach (var compName in spec.components)
                {
                    var t = ResolveComponentType(compName);
                    if (t == null || go.GetComponent(t) == null)
                        r.Errors.Add("post-verify: '" + spec.name + "' missing component '" + compName + "'");
                }
            }
            r.Ok = r.Errors.Count == 0;
            return r;
        }

        private static string RoleOf(GdaiPlayableContract c, string objName)
        {
            if (objName == c.player.object_name) return "player";
            if (objName == "Main Camera") return "camera";
            if (objName == "GameIntegrationController") return "controller";
            if (objName == "EnemyManager") return "manager";
            return "host";
        }

        private static bool TagExists(string tag)
        {
            try { return UnityEditorInternal.InternalEditorUtility.tags.Contains(tag); }
            catch { return tag == "Player" || tag == "MainCamera" || tag == "Untagged"; }
        }

        /// <summary>An object of this name that carries our marker, or null.</summary>
        public static GameObject FindOwned(string name)
        {
            foreach (var m in UnityEngine.Object.FindObjectsByType<GdaiGeneratedPlayableMarker>(FindObjectsSortMode.None))
                if (m.gameObject.name == name) return m.gameObject;
            return null;
        }

        private static GameObject FindOwnedOrCreate(string name, Vector3 pos, string profileId, string snapshotId, string role, out bool created)
        {
            created = false;
            var owned = FindOwned(name);
            if (owned != null) return owned;
            // refuse to adopt a same-named object that lacks our marker
            var stray = GameObject.Find(name);
            if (stray != null && stray.GetComponent<GdaiGeneratedPlayableMarker>() == null) return null;

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "GDAI create " + name);
            go.transform.position = pos;
            var marker = Undo.AddComponent<GdaiGeneratedPlayableMarker>(go);
            marker.profileId = profileId;
            marker.snapshotId = snapshotId;
            marker.ownedRole = role;
            created = true;
            return go;
        }
    }
}
