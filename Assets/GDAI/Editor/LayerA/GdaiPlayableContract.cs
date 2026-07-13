// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · P2-A · Playable contract consumer (Editor, Layer A).
//
// Strict, fail-closed parser + validator for gdai.unity.playable_contract.v1 rev3
// (single profile unity.pointer_action_demo.v1). The contract is produced by
// codegen-assembly and ships in the coherent bundle as
// Assets/GDAI_Generated/GDAI_PlayableContract.json; the composer consumes THIS
// model only — no class-name guessing, no Project-Slash specifics, no fallback
// when the contract is missing, ambiguous, unknown-versioned or path-unsafe.
// Mirrors the producer-side validator dimension-for-dimension so a contract that
// passes emission cannot fail consumption for a different reason.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GDAI.Bridge.Editor.LayerA
{
    [Serializable]
    public class GdaiPlayableContract
    {
        public const string Schema = "gdai.unity.playable_contract.v1";
        public const int Revision = 4;
        public const string Profile = "unity.pointer_action_demo.v1";
        public const string OwnedPrefix = "Assets/GDAI_Project/Generated/";
        public const string BundleFileName = "GDAI_PlayableContract.json";

        public string schema_version;
        public int contract_revision;
        public string profile_id;
        public CanonicalScene canonical_scene;
        public InputSpec input;
        public PlayerSpec player;
        public EnemyPrefabSpec enemy_prefab;
        public List<SceneObjectSpec> scene_objects = new List<SceneObjectSpec>();
        public List<SceneBindingSpec> scene_bindings = new List<SceneBindingSpec>();
        public List<ValueBindingSpec> value_bindings = new List<ValueBindingSpec>();
        public CameraSpec camera;
        public List<string> self_checks = new List<string>();

        [Serializable] public class CanonicalScene { public string path; public bool add_to_build_settings; }
        [Serializable] public class ActionSpec { public string id; public string name; public string type; public string control_type; public string binding; }
        [Serializable] public class ComponentBindingSpec { public string component; public string field; public string action; }
        [Serializable] public class InputSpec
        {
            public string asset_path; public string map;
            public List<ActionSpec> actions = new List<ActionSpec>();
            public List<ComponentBindingSpec> component_bindings = new List<ComponentBindingSpec>();
        }
        [Serializable] public class PlayerSpec
        {
            public string object_name; public string object_role; public string tag;
            public string sprite_role; public string sprite_entity_id;
            public List<string> required_components = new List<string>();
            public List<string> host_components = new List<string>();
        }
        [Serializable] public class DirectorBindingSpec { public string component; public string field; public string @object; }
        [Serializable] public class EnemyPrefabSpec
        {
            public string asset_path; public string object_name; public string layer;
            public string sprite_role; public string sprite_entity_id;
            public List<string> required_runtime_components = new List<string>();
            public List<DirectorBindingSpec> director_bindings = new List<DirectorBindingSpec>();
        }
        [Serializable] public class SceneObjectSpec { public string name; public List<string> components = new List<string>(); public string tag; }
        [Serializable] public class SceneBindingSpec { public string component; public string field; public string target_component; public string target_transform; public string target_object; }
        [Serializable] public class ValueBindingSpec { public string component; public string field; public string value_type; public List<string> layers = new List<string>(); }
        [Serializable] public class Rgba { public float r; public float g; public float b; public float a; }
        [Serializable] public class Vec3 { public float x; public float y; public float z; }
        [Serializable] public class WorldBounds { public float width; public float height; }
        [Serializable] public class CameraSpec
        {
            public string object_name; public string projection; public string framing; public string tag;
            public string clear_flags; public Rgba background; public Vec3 position;
            public float padding_ratio; public float target_aspect; public WorldBounds world_bounds;

            /// <summary>The fit_arena solve: fits the arena on BOTH axes (no horizontal clipping).</summary>
            public float SolveOrthographicSize()
            {
                float byHeight = world_bounds.height / 2f;
                float byWidth = world_bounds.width / (2f * target_aspect);
                return Math.Max(byHeight, byWidth) * (1f + padding_ratio);
            }
        }

        public class Result
        {
            public GdaiPlayableContract Contract;
            public readonly List<string> Errors = new List<string>();
            public bool Ok => Contract != null && Errors.Count == 0;
            public string Summary => Errors.Count == 0 ? "OK" : string.Join("; ", Errors);
        }

        /// <summary>Parse + fully validate. Never throws; failure = null contract + exact errors.</summary>
        public static Result Parse(string json)
        {
            var r = new Result();
            if (string.IsNullOrWhiteSpace(json)) { r.Errors.Add("contract json empty"); return r; }
            GdaiPlayableContract c;
            try
            {
                c = JsonConvert.DeserializeObject<GdaiPlayableContract>(json);
            }
            catch (Exception e)
            {
                r.Errors.Add("contract json invalid: " + e.Message);
                return r;
            }
            if (c == null) { r.Errors.Add("contract json null"); return r; }
            Validate(c, r.Errors);
            if (r.Errors.Count == 0) r.Contract = c;
            return r;
        }

        // ---------------- validation (mirrors producer validator) ----------------

        private static readonly string[] RequiredActionNames = { "PointerPosition", "LeftClick", "RightClick" };
        private static readonly Dictionary<string, string> FieldAction = new Dictionary<string, string>
        {
            { "_pointerPositionRef", "Gameplay/PointerPosition" },
            { "_leftClickRef", "Gameplay/LeftClick" },
            { "_rightClickRef", "Gameplay/RightClick" },
        };
        private static readonly string[] ConcreteColliders = { "BoxCollider2D", "CircleCollider2D", "PolygonCollider2D", "CapsuleCollider2D" };
        private static readonly string[] CanonicalObjects = { "Player", "InputManager", "GameIntegrationController", "EnemyManager", "Main Camera" };

        private static bool IsEntityUuid(string s) =>
            !string.IsNullOrEmpty(s) &&
            System.Text.RegularExpressions.Regex.IsMatch(s, "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$") &&
            s != "00000000-0000-0000-0000-000000000000";

        private static bool OwnedPath(string p, string ext)
        {
            if (string.IsNullOrEmpty(p) || !p.StartsWith(OwnedPrefix, StringComparison.Ordinal) || !p.EndsWith(ext, StringComparison.Ordinal)) return false;
            if (p.Contains("..") || p.StartsWith("/") || p.Contains("\\")) return false;
            return true;
        }

        private static void Validate(GdaiPlayableContract c, List<string> e)
        {
            void Req(bool ok, string msg) { if (!ok) e.Add(msg); }

            Req(c.schema_version == Schema, "bad schema_version: " + c.schema_version);
            Req(c.contract_revision == Revision, "bad contract_revision (expected " + Revision + "): " + c.contract_revision);
            Req(c.profile_id == Profile, "bad profile_id: " + c.profile_id);

            Req(c.canonical_scene != null && (c.canonical_scene.path ?? "").StartsWith("Assets/Scenes/")
                && c.canonical_scene.path.EndsWith(".unity"), "canonical_scene.path must be Assets/Scenes/*.unity");
            Req(c.canonical_scene != null && c.canonical_scene.add_to_build_settings, "canonical_scene.add_to_build_settings must be true");

            // input actions — pinned set, unique ids/names, sane types
            var names = new HashSet<string>();
            var ids = new HashSet<string>();
            Req(c.input != null && c.input.map == "Gameplay", "input.map must be Gameplay");
            Req(c.input != null && c.input.actions.Count == 3, "expected exactly 3 input actions");
            foreach (var a in c.input?.actions ?? new List<ActionSpec>())
            {
                Req(!string.IsNullOrEmpty(a.name) && names.Add(a.name), "duplicate/empty action name: " + a.name);
                Req(!string.IsNullOrEmpty(a.id) && System.Text.RegularExpressions.Regex.IsMatch(a.id, "^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$") && ids.Add(a.id),
                    "bad/duplicate action id: " + a.id);
                Req(a.type == "Value" || a.type == "Button", "bad action type: " + a.type);
                Req(a.type != "Button" || a.control_type == "Button", "Button action needs control_type Button: " + a.name);
                Req(!string.IsNullOrEmpty(a.binding) && a.binding.StartsWith("<"), "bad binding path: " + a.binding);
            }
            Req(names.SetEquals(RequiredActionNames), "action names must be exactly {PointerPosition,LeftClick,RightClick}");

            // exact field ↔ action map, all three covered, no duplicates
            Req(c.input != null && c.input.component_bindings.Count == 3, "expected 3 input component_bindings");
            var seenFields = new HashSet<string>();
            var seenActions = new HashSet<string>();
            foreach (var cb in c.input?.component_bindings ?? new List<ComponentBindingSpec>())
            {
                Req(cb.component == "InputManager", "input binding component must be InputManager");
                Req(!string.IsNullOrEmpty(cb.field) && seenFields.Add(cb.field), "duplicate/empty input field: " + cb.field);
                Req(FieldAction.TryGetValue(cb.field ?? "", out var want) && want == cb.action,
                    "field " + cb.field + " must bind its pinned action — got " + cb.action);
                Req(seenActions.Add(cb.action), "action bound twice: " + cb.action);
            }
            Req(FieldAction.Keys.All(seenFields.Contains), "input bindings must cover all three InputManager fields");

            // owned asset paths
            Req(OwnedPath(c.input?.asset_path, ".inputactions"), "input asset_path must be an owned .inputactions");
            Req(OwnedPath(c.enemy_prefab?.asset_path, ".prefab"), "enemy prefab asset_path must be an owned .prefab");

            // player — visible + movable + co-located
            Req(c.player != null && c.player.required_components.Contains("SpriteRenderer"), "player must require SpriteRenderer");
            Req(c.player != null && c.player.required_components.Contains("Rigidbody2D"), "player must require Rigidbody2D");
            Req(c.player != null && c.player.host_components.Contains("CharacterStateMachine"), "player must host CharacterStateMachine");
            Req(c.player != null && c.player.host_components.Contains("CombatLocatorSystem"), "player must host CombatLocatorSystem");
            Req(!string.IsNullOrEmpty(c.player?.object_name), "player.object_name required");
            Req(!string.IsNullOrEmpty(c.player?.tag), "player.tag required");
            Req(IsEntityUuid(c.player?.sprite_entity_id), "player sprite_entity_id must be a real uuid");

            // enemy prefab — visible + physics + hittable
            var ec = c.enemy_prefab?.required_runtime_components ?? new List<string>();
            Req(ec.Contains("SpriteRenderer"), "enemy must require SpriteRenderer");
            Req(ec.Contains("Rigidbody2D"), "enemy must require Rigidbody2D");
            Req(ec.Any(x => ConcreteColliders.Contains(x)), "enemy must require a concrete Collider2D");
            Req(!ec.Contains("Collider2D"), "enemy must not list abstract Collider2D");
            Req(!string.IsNullOrEmpty(c.enemy_prefab?.layer), "enemy_prefab.layer required");
            Req(IsEntityUuid(c.enemy_prefab?.sprite_entity_id), "enemy sprite_entity_id must be a real uuid");
            Req(!string.Equals(c.player?.sprite_entity_id, c.enemy_prefab?.sprite_entity_id, StringComparison.OrdinalIgnoreCase),
                "player and enemy sprite_entity_id must differ");
            Req((c.enemy_prefab?.director_bindings?.Count ?? 0) >= 1, "enemy_prefab needs a director binding");
            foreach (var db in c.enemy_prefab?.director_bindings ?? new List<DirectorBindingSpec>())
                Req(db.component == "EnemyDirector" && db.field == "enemyPrefab" && !string.IsNullOrEmpty(db.@object),
                    "director binding must be EnemyDirector.enemyPrefab@<object>");

            // scene-object graph — the five canonical hosts, declared for creation
            var objNames = (c.scene_objects ?? new List<SceneObjectSpec>()).Select(o => o.name).ToList();
            Req(objNames.Count >= 5 && CanonicalObjects.All(objNames.Contains), "scene_objects must declare the five canonical objects");
            var byName = (c.scene_objects ?? new List<SceneObjectSpec>()).ToDictionary(o => o.name ?? "", o => o);
            var hosts = new HashSet<string>((c.scene_objects ?? new List<SceneObjectSpec>()).SelectMany(o => o.components));
            Req(byName.ContainsKey(c.player?.object_name ?? ""), "scene_objects must include the canonical player object");
            if (byName.TryGetValue(c.player?.object_name ?? "", out var po))
            {
                Req((c.player?.host_components ?? new List<string>()).All(h => po.components.Contains(h)), "player scene_object must carry the host components");
                Req(po.tag == c.player?.tag, "player scene_object tag must match player.tag");
            }

            // scene bindings — exact set, one target kind, declared hosts, co-location
            var bound = new HashSet<string>();
            foreach (var b in c.scene_bindings ?? new List<SceneBindingSpec>())
            {
                int kinds = (string.IsNullOrEmpty(b.target_component) ? 0 : 1) + (string.IsNullOrEmpty(b.target_transform) ? 0 : 1);
                Req(kinds == 1, "scene binding " + b.component + "." + b.field + " must target exactly one of component/transform");
                Req(hosts.Contains(b.component), "binding source component " + b.component + " has no declared host object");
                if (!string.IsNullOrEmpty(b.target_component)) Req(hosts.Contains(b.target_component), "binding target component " + b.target_component + " has no declared host object");
                if (!string.IsNullOrEmpty(b.target_transform)) Req(byName.ContainsKey(b.target_transform), "binding target transform " + b.target_transform + " is not a declared object");
                if (!string.IsNullOrEmpty(b.target_object)) Req(byName.ContainsKey(b.target_object), "binding target object " + b.target_object + " is not declared");
                if (b.component == "GameIntegrationController" && (b.field == "characterStateMachine" || b.field == "combatLocatorSystem"))
                    Req(b.target_object == c.player?.object_name, b.field + " must bind to the canonical player object");
                bound.Add(b.component + "." + b.field);
            }
            foreach (var f in new[] { "inputManager", "characterStateMachine", "combatLocatorSystem", "enemyDirector", "player" })
                Req(bound.Contains("GameIntegrationController." + f), "scene_bindings missing GameIntegrationController." + f);
            Req(bound.Contains("InputManager._mainCamera") && bound.Contains("InputManager._playerReferenceTransform"),
                "scene_bindings missing InputManager scene refs");
            Req((c.scene_bindings?.Count ?? 0) == 7, "expected exactly 7 scene bindings");
            foreach (var db in c.enemy_prefab?.director_bindings ?? new List<DirectorBindingSpec>())
                Req(byName.ContainsKey(db.@object ?? ""), "director binding object " + db.@object + " is not a declared scene object");

            // value bindings — enemyLayer must be set and include the enemy layer
            var el = (c.value_bindings ?? new List<ValueBindingSpec>())
                .FirstOrDefault(v => v.component == "CombatLocatorSystem" && v.field == "enemyLayer");
            Req(el != null, "value_bindings must set CombatLocatorSystem.enemyLayer");
            Req(el != null && el.value_type == "LayerMask" && el.layers != null && el.layers.Count > 0, "enemyLayer binding must name at least one layer");
            Req(el != null && el.layers.Contains(c.enemy_prefab?.layer ?? ""), "enemyLayer must include the enemy prefab layer");

            // camera fit_arena framing — orthographic 2D with valid arena-derived bounds
            var cam = c.camera;
            Req(cam != null, "camera block required");
            if (cam != null)
            {
                Req(cam.object_name == "Main Camera", "camera.object_name must be Main Camera");
                Req(cam.projection == "orthographic", "camera.projection must be orthographic");
                Req(cam.framing == "fit_arena", "camera.framing must be fit_arena");
                Req(cam.tag == "MainCamera", "camera.tag must be MainCamera");
                Req(cam.clear_flags == "SolidColor", "camera.clear_flags must be SolidColor");
                Req(cam.background != null, "camera.background required");
                Req(cam.position != null && cam.position.z < 0f, "camera.position.z must be negative");
                Req(cam.padding_ratio >= 0f && cam.padding_ratio < 1f, "camera.padding_ratio must be in [0,1)");
                Req(cam.target_aspect > 0f, "camera.target_aspect must be > 0");
                Req(cam.world_bounds != null && cam.world_bounds.width > 0f && cam.world_bounds.height > 0f, "camera.world_bounds must be positive");
            }
        }
    }
}
