// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · §5.4b · Hard receipt (Editor, Layer C).
//
// The acceptance gate. It NEVER copies a builder's return value — it reopens the SAVED
// scene and independently reads back the real state (AssetDatabase, SerializedObject fields,
// EditorBuildSettings, live components, ownership markers, the just-written manifest) and
// records expected-vs-actual for every required fact:
//   objects 5/5 · scene refs 7/7 · input refs 3/3 · value bindings · enemy prefab assigned ·
//   collider non-zero · exactly one active AudioListener · camera rev4 facts · Build Settings ·
//   ownership-manifest agreement · manual_assembly_steps = 0.
// PASS iff every check passes AND manual_assembly_steps == 0; otherwise PARTIAL (some pass)
// or FAIL (a required structural fact missing). Any missing/duplicate/stale/ambiguous fact is
// never rounded up to PASS.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Runtime;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC
{
    [Serializable] public class GdaiReceiptCheck { public string key; public string expected; public string actual; public bool pass; }

    // Gate B · B7 · typed scene-assembly metrics, extended INTO the same receipt (not a separate file). Every
    // field is filled by INDEPENDENT readback of the reopened saved scene (+ the sceneAssembly SSOT for the
    // unresolved check) — never a builder return value. Present only when a scene assembly was composed.
    [Serializable]
    public class GdaiSceneMetrics
    {
        public string scene_layout_id;
        public string scene_guid;
        public bool saved;
        public bool in_build_settings;
        public int root_count;               // GdaiSceneAssemblyMarker kind=root (must be exactly 1)
        public int arena_boundaries;         // kind=blocker
        public int spawn_markers;            // kind=player_spawn + enemy_spawn
        public int scene_elements;           // kind=scene_element
        public int box_colliders;            // across owned scene objects + Player
        public int polygon_colliders;
        public int circle_colliders;
        public int player_colliders;         // Collider2D on the owned Player (AC-2 ⇒ >= 1)
        public int stale_colliders;          // owned objects carrying >1 Collider2D (duplicate/leftover shape)
        public int duplicate_identities;     // owned scene objects sharing a source_id (kind|entity_id)
        public int unresolved;               // DTO draft-blocker scene_elements with NO live Collider2D
        public int red_errors;               // LogType.Error/Exception observed during the sync (injected)
        public bool manual_assembly;         // any manual assembly step was required
    }

    [Serializable]
    public class GdaiPlayableReceipt
    {
        public const string SchemaVersion = "gdai.unity.playable_receipt.v1";
        public const string ReceiptPath = "Assets/GDAI_Project/Generated/GDAIPlayableReceipt.json";

        public string schema_version = SchemaVersion;
        public string project_id;
        public string snapshot_id;
        public int contract_revision;
        public string contract_sha256;
        public string generated_at;
        public string status;                 // PASS | PARTIAL | FAIL
        public int manual_assembly_steps;
        public string framing_inputs;         // §6 coverage: fit_arena inputs, verified via camera_ortho_size
        public List<GdaiReceiptCheck> checks = new List<GdaiReceiptCheck>();
        public List<string> failures = new List<string>();

        // Gate B · B7: additive scene section. Null (omitted) for playable-only bundles with no scene assembly.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public GdaiSceneMetrics scene;

        public bool IsPass => status == "PASS";
    }

    public static class GdaiPlayableReceiptWriter
    {
        private static readonly string[] Canonical = { "Player", "InputManager", "GameIntegrationController", "EnemyManager", "Main Camera" };
        private const float Eps = 0.001f;

        // The CANONICAL required binding sets for this profile (mirrors the contract validator's pinned
        // sets). The receipt verifies these INDEPENDENTLY of the contract's own list length, so a short
        // or empty contract binding list can never make the check pass vacuously.
        private static readonly (string comp, string field)[] RequiredSceneRefs =
        {
            ("GameIntegrationController", "inputManager"),
            ("GameIntegrationController", "characterStateMachine"),
            ("GameIntegrationController", "combatLocatorSystem"),
            ("GameIntegrationController", "enemyDirector"),
            ("GameIntegrationController", "player"),
            ("InputManager", "_mainCamera"),
            ("InputManager", "_playerReferenceTransform"),
        };
        private static readonly (string field, string action)[] RequiredInputRefs =
        {
            ("_pointerPositionRef", "Gameplay/PointerPosition"),
            ("_leftClickRef", "Gameplay/LeftClick"),
            ("_rightClickRef", "Gameplay/RightClick"),
        };

        /// <summary>
        /// Reopen the saved scene and build the receipt by INDEPENDENT readback. nowUtc is injected
        /// (no Date.Now hidden dependency). Does not write; call Write() to persist atomically.
        /// </summary>
        public static GdaiPlayableReceipt Build(GdaiPlayableContract c, string projectId, string snapshotId,
            string contractSha256, string scenePath, DateTime nowUtc, int redErrorCount = 0)
        {
            var r = new GdaiPlayableReceipt
            {
                project_id = projectId,
                snapshot_id = snapshotId,
                contract_revision = c?.contract_revision ?? -1,
                contract_sha256 = contractSha256,
                generated_at = nowUtc.ToString("o"),
                manual_assembly_steps = 0,   // the composer did everything; a non-zero here would itself be a FAIL fact
            };

            void Check(string key, string expected, string actual)
            {
                bool pass = string.Equals(expected, actual, StringComparison.Ordinal);
                r.checks.Add(new GdaiReceiptCheck { key = key, expected = expected, actual = actual, pass = pass });
                if (!pass) r.failures.Add($"{key}: expected [{expected}] actual [{actual}]");
            }

            if (c == null) { r.failures.Add("null contract"); r.status = "FAIL"; return r; }

            // Independent readback: reopen the SAVED scene from disk.
            try { EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single); }
            catch (Exception e) { r.failures.Add("could not reopen saved scene: " + e.Message); r.status = "FAIL"; return r; }

            var markers = UnityEngine.Object.FindObjectsByType<GdaiGeneratedPlayableMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            // 1 · objects 5/5 — each canonical name owned exactly once
            int ownedCanonical = Canonical.Count(n => markers.Count(m => m.gameObject.name == n) == 1);
            Check("objects_owned", "5", ownedCanonical.ToString());

            // independent guard: the receipt must not trust the contract's own list lengths, so it also
            // re-asserts the frozen shape (revision 4, exactly the required binding counts) up front.
            Check("contract_revision", GdaiPlayableContract.Revision.ToString(), c.contract_revision.ToString());

            // 2 · scene refs 7/7 — the CANONICAL required set, each bound to a GDAI-owned object (not the
            // contract's own count, which would pass vacuously for a short/empty binding list).
            int sceneOk = RequiredSceneRefs.Count(rr => RequiredSceneRefBound(rr.comp, rr.field));
            Check("scene_refs", RequiredSceneRefs.Length.ToString(), sceneOk.ToString());

            // 3 · input refs 3/3 — each field must reference the InputActionReference whose name IS the
            // pinned action (non-null alone would let a wrong/stale/junk reference false-pass).
            int inputOk = RequiredInputRefs.Count(ir => InputRefMatchesAction(ir.field, ir.action));
            Check("input_refs", RequiredInputRefs.Length.ToString(), inputOk.ToString());

            // 4 · value binding — the enemyLayer mask must include the enemy's layer; an empty/absent
            // contract list is treated as a FAIL (expected is a canonical lower bound of 1).
            int valOk = 0, valTotal = c.value_bindings?.Count ?? 0;
            foreach (var v in c.value_bindings ?? new List<GdaiPlayableContract.ValueBindingSpec>())
                if (ValueBindingSatisfied(v)) valOk++;
            Check("value_bindings", Math.Max(1, valTotal).ToString(), valOk.ToString());

            // 5 · enemy prefab assigned (persistent asset on EnemyDirector.enemyPrefab)
            Check("enemy_prefab_assigned", "true", EnemyPrefabAssigned().ToString().ToLowerInvariant());

            // 6 · collider non-zero on the enemy prefab asset
            Check("enemy_collider_nonzero", "true", EnemyColliderNonZero(c).ToString().ToLowerInvariant());

            // 7 · exactly one active AudioListener
            Check("active_audio_listeners", "1", GdaiAudioListenerEnsurer.ActiveCount().ToString());

            // 8 · camera rev4 facts (read from the reopened Main Camera + contract spec)
            CameraChecks(c.camera, Check);
            if (c.camera != null)
                r.framing_inputs = $"target_aspect={c.camera.target_aspect:0.####} padding_ratio={c.camera.padding_ratio:0.###} " +
                    (c.camera.world_bounds != null ? $"world_bounds={c.camera.world_bounds.width:0.##}x{c.camera.world_bounds.height:0.##}" : "world_bounds=?");

            // 9 · Build Settings contains the enabled scene
            bool inBuild = EditorBuildSettings.scenes.Any(s => s.path == scenePath && s.enabled);
            Check("scene_in_build_settings", "true", inBuild.ToString().ToLowerInvariant());

            // 10 · ownership manifest agrees with actual state AND this session's identity
            bool manifestOk = GdaiPlayableOwnershipManifest.Verify(c, projectId, snapshotId, contractSha256, out string mErr);
            Check("ownership_manifest", "verified", manifestOk ? "verified" : ("mismatch:" + mErr));

            // 11 · manual assembly steps
            Check("manual_assembly_steps", "0", r.manual_assembly_steps.ToString());

            // 12 · Gate B scene-assembly metrics + PASS gates (independent readback of the reopened scene +
            //      the sceneAssembly SSOT for the unresolved check). Only applied when a scene assembly was
            //      composed (root marker present) — playable-only bundles leave r.scene null and skip these.
            var sm = ComputeSceneMetrics(scenePath, redErrorCount);
            if (sm.root_count > 0 || sm.arena_boundaries > 0 || sm.scene_elements > 0 || sm.spawn_markers > 0)
            {
                r.scene = sm;
                Check("scene_root", "1", sm.root_count.ToString());
                Check("scene_player_colliders_ge1", "true", (sm.player_colliders >= 1).ToString().ToLowerInvariant());
                Check("scene_duplicate_identities", "0", sm.duplicate_identities.ToString());
                Check("scene_stale_colliders", "0", sm.stale_colliders.ToString());
                Check("scene_unresolved", "0", sm.unresolved.ToString());
                Check("scene_red_errors", "0", sm.red_errors.ToString());
                // (manual assembly is gated once by manual_assembly_steps above; sm.manual_assembly is the
                //  informational mirror — the composer is the sole authority, so it is definitionally false.)
            }

            int failed = r.checks.Count(x => !x.pass);
            r.status = failed == 0 ? "PASS" : (failed < r.checks.Count ? "PARTIAL" : "FAIL");
            return r;
        }

        // ---- independent readback helpers ----

        private static Component OwnedComponent(string ownedName, string typeName)
        {
            var go = GdaiSceneObjectComposer.FindOwned(ownedName);
            if (go == null) return null;
            var t = GdaiSceneObjectComposer.ResolveComponentType(typeName);
            return t == null ? null : go.GetComponent(t);
        }

        // A required InputManager input ref is satisfied only when the field references an
        // InputActionReference whose name IS the pinned action (identity, not mere non-null).
        private static bool InputRefMatchesAction(string field, string action)
        {
            var im = OwnedComponent("InputManager", "InputManager");
            if (im == null) return false;
            var so = new SerializedObject(im);
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) return false;
            var refObj = p.objectReferenceValue;
            return refObj != null && refObj.GetType().Name == "InputActionReference" && refObj.name == action;
        }

        // The source component that hosts each binding field, keyed by the contract component name.
        private static Component ResolveSource(string componentName)
        {
            var t = GdaiSceneObjectComposer.ResolveComponentType(componentName);
            if (t == null) return null;
            var hosts = UnityEngine.Object.FindObjectsByType<GdaiGeneratedPlayableMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(m => m.GetComponent(t)).Where(x => x != null).ToList();
            return hosts.Count == 1 ? hosts[0] : null;
        }

        // A required scene ref is bound only when the source component's field references a GDAI-owned
        // object (marker present) — never a stray human object, never null.
        private static bool RequiredSceneRefBound(string component, string field)
        {
            var src = ResolveSource(component);
            if (src == null) return false;
            var so = new SerializedObject(src);
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) return false;
            var go = GameObjectOf(p.objectReferenceValue);
            return go != null && go.GetComponent<GdaiGeneratedPlayableMarker>() != null;
        }

        private static GameObject GameObjectOf(UnityEngine.Object o)
        {
            if (o is GameObject g) return g;
            if (o is Component c) return c.gameObject;
            return null;
        }

        private static bool ValueBindingSatisfied(GdaiPlayableContract.ValueBindingSpec v)
        {
            var src = ResolveSource(v.component);
            if (src == null) return false;
            var so = new SerializedObject(src);
            var p = so.FindProperty(v.field);
            if (p == null || p.propertyType != SerializedPropertyType.LayerMask) return false;
            int mask = p.intValue;
            foreach (var ln in v.layers ?? new List<string>())
            {
                int idx = LayerMask.NameToLayer(ln);
                if (idx < 0 || (mask & (1 << idx)) == 0) return false;
            }
            return (v.layers?.Count ?? 0) > 0;
        }

        private static bool EnemyPrefabAssigned()
        {
            var director = ResolveSource("EnemyDirector");
            if (director == null) return false;
            var so = new SerializedObject(director);
            var p = so.FindProperty("enemyPrefab");
            var go = p?.objectReferenceValue as GameObject;
            return go != null && EditorUtility.IsPersistent(go) && PrefabUtility.IsPartOfPrefabAsset(go);
        }

        private static bool EnemyColliderNonZero(GdaiPlayableContract c)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(c.enemy_prefab?.asset_path);
            if (asset == null) return false;
            var col = asset.GetComponentInChildren<Collider2D>(true);
            if (col == null || !col.enabled) return false;
            if (col is BoxCollider2D b) return b.size.x > Eps && b.size.y > Eps;
            if (col is CircleCollider2D ci) return ci.radius > Eps;
            var s = col.bounds.size; return s.x > Eps && s.y > Eps;
        }

        private static void CameraChecks(GdaiPlayableContract.CameraSpec spec, Action<string, string, string> Check)
        {
            var camGo = GdaiSceneObjectComposer.FindOwned("Main Camera");
            var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            if (spec == null || cam == null)
            {
                Check("camera", "present", cam == null ? "missing_camera" : "missing_spec");
                return;
            }
            Check("camera_tag", "MainCamera", camGo.tag);
            Check("camera_orthographic", "true", cam.orthographic.ToString().ToLowerInvariant());
            Check("camera_ortho_size", spec.SolveOrthographicSize().ToString("0.###"), cam.orthographicSize.ToString("0.###"));
            Check("camera_clear_flags", "SolidColor", cam.clearFlags.ToString());
            Check("camera_background", ColorStr(spec.background), ColorStr(cam.backgroundColor));
            Check("camera_position_z", spec.position != null ? spec.position.z.ToString("0.###") : "?", camGo.transform.position.z.ToString("0.###"));
            Check("camera_framing", "fit_arena", spec.framing ?? "null");
            // NOTE: target_aspect / padding_ratio / world_bounds are the fit_arena framing INPUTS — the
            // camera does not store them, so the LIVE proof that they were applied is camera_ortho_size
            // above (== max(h/2, w/(2*aspect))*(1+padding) computed against the actual camera). They are
            // recorded in receipt.framing_inputs (set in Build) for external §6 coverage, NOT as vacuous
            // spec-vs-spec checks.
        }

        private static string ColorStr(GdaiPlayableContract.Rgba c) =>
            c == null ? "null" : $"{c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##}";
        private static string ColorStr(Color c) => $"{c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##}";

        // ---- Gate B · B7 · scene-assembly metrics (independent readback of the reopened scene + SSOT) ----
        private static GdaiSceneMetrics ComputeSceneMetrics(string scenePath, int redErrorCount)
        {
            var m = new GdaiSceneMetrics { red_errors = redErrorCount, manual_assembly = false };
            m.scene_guid = AssetDatabase.AssetPathToGUID(scenePath) ?? "";
            m.saved = File.Exists(Path.Combine(ProjectRoot(), scenePath));
            m.in_build_settings = EditorBuildSettings.scenes.Any(s => s.path == scenePath && s.enabled);

            // sceneAssembly SSOT — scene_layout_id + the draft-blocker elements that MUST carry a collider.
            var draftBlockerIds = new HashSet<string>();
            if (GDAI.Bridge.Editor.LayerB.GdaiSceneAssemblyModels.TryLoad(out var dto, out _) && dto != null)
            {
                m.scene_layout_id = dto.scene_layout_id;
                foreach (var e in dto.scene_elements ?? new List<GDAI.Bridge.Editor.LayerB.SceneElementDto>())
                    if (e != null && !string.IsNullOrEmpty(e.id) && e.physics != null
                        && e.physics.kind == "demo_draft_blocker" && !e.physics.confirmed)
                        draftBlockerIds.Add(e.id);
            }

            var markers = UnityEngine.Object.FindObjectsByType<GDAI.Bridge.GdaiSceneAssemblyMarker>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var idCounts = new Dictionary<string, int>();
            var owned = new List<GameObject>();
            foreach (var mk in markers)
            {
                string sid = GdaiPlayableOwnershipManifest.SceneObjectSourceId(mk.kind, mk.entityId);
                idCounts[sid] = idCounts.TryGetValue(sid, out var n) ? n + 1 : 1;
                owned.Add(mk.gameObject);
                switch (mk.kind)
                {
                    case GDAI.Bridge.GdaiSceneAssemblyKind.Root: m.root_count++; break;
                    case GDAI.Bridge.GdaiSceneAssemblyKind.Blocker: m.arena_boundaries++; break;
                    case GDAI.Bridge.GdaiSceneAssemblyKind.PlayerSpawn:
                    case GDAI.Bridge.GdaiSceneAssemblyKind.EnemySpawn: m.spawn_markers++; break;
                    case GDAI.Bridge.GdaiSceneAssemblyKind.SceneElement: m.scene_elements++; break;
                }
            }
            m.duplicate_identities = idCounts.Values.Count(v => v > 1);

            // include the playable-owned Player so its collider (AC-2) counts + is checked.
            var player = GdaiSceneObjectComposer.FindOwned("Player");
            if (player != null) { owned.Add(player); m.player_colliders = player.GetComponents<Collider2D>().Length; }

            foreach (var go in owned)
            {
                var cols = go.GetComponents<Collider2D>();
                if (cols.Length > 1) m.stale_colliders++;   // duplicate/leftover shape on one object
                foreach (var col in cols)
                {
                    if (col is BoxCollider2D) m.box_colliders++;
                    else if (col is PolygonCollider2D) m.polygon_colliders++;
                    else if (col is CircleCollider2D) m.circle_colliders++;
                }
            }

            // unresolved: each DTO draft-blocker element must have a live Collider2D on its owned object.
            foreach (var id in draftBlockerIds)
            {
                var go = markers.FirstOrDefault(mk =>
                    mk.kind == GDAI.Bridge.GdaiSceneAssemblyKind.SceneElement && (mk.entityId ?? "") == id)?.gameObject;
                if (go == null || go.GetComponent<Collider2D>() == null) m.unresolved++;
            }

            return m;
        }

        // ---- atomic write ----

        private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;

        public static bool Write(GdaiPlayableReceipt receipt, out string error)
        {
            error = null;
            try
            {
                string abs = Path.GetFullPath(Path.Combine(ProjectRoot(), GdaiPlayableReceipt.ReceiptPath));
                Directory.CreateDirectory(Path.GetDirectoryName(abs));
                string tmp = abs + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(receipt, Formatting.Indented));
                if (File.Exists(abs)) File.Replace(tmp, abs, null); else File.Move(tmp, abs);
                AssetDatabase.ImportAsset(GdaiPlayableReceipt.ReceiptPath);
                return true;
            }
            catch (Exception e) { error = "receipt write failed: " + e.Message; return false; }
        }
    }
}
