// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · §5.5 · Playable composer CTA (Editor, Layer C).
//
// The one user-triggered "compose the playable scene" call, wired ONLY through pure API
// seams (never EditorApplication.ExecuteMenuItem). Order is fixed:
//   compose → build input asset → build+bind enemy prefab → apply bindings (scene+value) →
//   bind input refs → configure camera → ensure one AudioListener → save scene →
//   Build Settings → write ownership manifest (atomic, last) → build+write HARD RECEIPT
//   (independent readback) → mark the operation Completed ONLY if the receipt is PASS.
// The receipt — not any builder's return value — is the gate: a failed manifest or receipt
// write, or a non-PASS receipt, leaves the operation Failed, never Completed.
// Structural seam failures (scene/compose/input-asset/enemy-prefab) stop early; binding
// partials are left for the receipt to catch by independent readback.
// =====================================================================================
using System;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerB;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GDAI.Bridge.Editor.LayerC
{
    public static class GdaiPlayableComposerCta
    {
        public class Result
        {
            public bool Completed;
            public string OperationId;
            public GdaiPlayableReceipt Receipt;
            public string Error;
            public System.Collections.Generic.List<string> Log = new System.Collections.Generic.List<string>();
        }

        private static string Root() => Directory.GetParent(Application.dataPath).FullName;

        public enum ImportedContractOutcome { Composed, NotPresent, Failed, DeferredToReload }

        // A composed scene needs the generated MonoBehaviours (CharacterStateMachine, …) as compiled
        // TYPES. On a FRESH sync those .cs arrive in this import and only exist after the imminent
        // domain reload, so a synchronous compose here cannot resolve them. The window CTA therefore
        // DEFERS: it drops this marker and the [InitializeOnLoad] resume hook composes automatically
        // once the code has compiled — one user click, zero manual steps.
        [Serializable] private class PendingCompose { public string project_id; public string snapshot_id; public string scene_path; public string at; }
        private static string PendingComposePath(string root) => Path.Combine(root, "Library", "GDAI", "pending-compose.json");

        private const string ProbeType = "CharacterStateMachine"; // a generated player-assembly MonoBehaviour
        private static bool GeneratedTypesCompiled() => GdaiSceneObjectComposer.ResolveComponentType(ProbeType) != null;

        /// <summary>
        /// Window-CTA entry after a bundle import. If the contract is absent → NotPresent (legacy path).
        /// If the generated code is already compiled → compose now (re-sync / TREE-C reopen). Otherwise the
        /// code is compiling on the imminent reload → persist a pending-compose marker and return
        /// DeferredToReload; the resume hook finishes automatically. This is the ONE seam the visible
        /// window CTA uses; a fresh project composes with a single click and no manual step.
        /// </summary>
        public static ImportedContractOutcome RunOrDeferFromImportedContract(string projectId, string snapshotId,
            string scenePath, DateTime nowUtc, out Result result, out string detail)
        {
            result = null; detail = null;
            string root = Root();
            string cpath = Path.Combine(root, "Assets", "GDAI_Generated", GdaiPlayableContract.BundleFileName);
            if (!File.Exists(cpath)) { ClearPendingCompose(root); detail = "no playable contract in bundle (pre-rev4 snapshot)"; return ImportedContractOutcome.NotPresent; }

            if (!GeneratedTypesCompiled())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PendingComposePath(root)));
                File.WriteAllText(PendingComposePath(root), JsonConvert.SerializeObject(
                    new PendingCompose { project_id = projectId, snapshot_id = snapshotId, scene_path = scenePath, at = nowUtc.ToString("o") },
                    Formatting.Indented));
                detail = "generated code importing; the playable scene composes automatically after Unity finishes compiling";
                return ImportedContractOutcome.DeferredToReload;
            }
            var outcome = RunFromImportedContract(projectId, snapshotId, scenePath, nowUtc, out result, out detail);
            ClearPendingCompose(root);
            return outcome;
        }

        /// <summary>
        /// Called by the resume hook after every domain reload. If a compose was deferred and the
        /// generated code is now compiled, run it exactly once (delete the marker first so a failed
        /// or repeated reload never re-runs it). Returns false when there is nothing pending.
        /// </summary>
        public static bool TryResumePendingCompose(DateTime nowUtc, out ImportedContractOutcome outcome, out Result result, out string detail)
        {
            outcome = ImportedContractOutcome.NotPresent; result = null; detail = null;
            string root = Root();
            string pp = PendingComposePath(root);
            if (!File.Exists(pp)) return false;
            PendingCompose pc;
            try { pc = JsonConvert.DeserializeObject<PendingCompose>(File.ReadAllText(pp)); }
            catch { ClearPendingCompose(root); return false; }
            if (pc == null || string.IsNullOrEmpty(pc.project_id)) { ClearPendingCompose(root); return false; }
            if (!GeneratedTypesCompiled()) { detail = "generated code not compiled yet — will retry on the next reload"; return false; }
            ClearPendingCompose(root); // run once
            outcome = RunFromImportedContract(pc.project_id, pc.snapshot_id,
                string.IsNullOrEmpty(pc.scene_path) ? "Assets/Scenes/Main.unity" : pc.scene_path, nowUtc, out result, out detail);
            // T4 0H1 Phase 3: fresh-sync animation closure. The pending marker was cleared above (run once),
            // so a re-entry cannot double-consume. When the DEFERRED compose succeeded (the fresh-sync path
            // that produced the shipped candidate), run the animation consumer on the SAME snapshot identity
            // so the animation materializes on the far side of the reload — one click, zero manual steps.
            if (outcome == ImportedContractOutcome.Composed)
            {
                var anim = Animation.GdaiAnimationBundleConsumer.Consume(pc.snapshot_id, Animation.GdaiAnimationBundleConsumer.DefaultRunClass);
                if (anim.outcome == Animation.GdaiAnimConsumeOutcome.Failed) detail += " · animation consume FAILED: " + anim.code;
                else if (anim.outcome == Animation.GdaiAnimConsumeOutcome.Consumed) detail += " · animation materialized (" + anim.receiptStatus + ")";
            }
            return true;
        }

        private static void ClearPendingCompose(string root)
        {
            try { var p = PendingComposePath(root); if (File.Exists(p)) File.Delete(p); } catch { }
        }

        /// <summary>
        /// Window-CTA seam: compose from the contract the bundle import just wrote (if any).
        /// Reads Assets/GDAI_Generated/GDAI_PlayableContract.json, parses fail-closed, pins its
        /// sha256, and runs the full composer. NotPresent = pre-rev4 snapshot (caller falls back
        /// to the legacy minimal scene prep); Failed = contract present but invalid or the
        /// composition/receipt did not PASS (caller must fail the sync, never continue silently).
        /// </summary>
        public static ImportedContractOutcome RunFromImportedContract(string projectId, string snapshotId,
            string scenePath, DateTime nowUtc, out Result result, out string detail)
        {
            result = null; detail = null;
            string path = Path.Combine(Root(), "Assets", "GDAI_Generated", GdaiPlayableContract.BundleFileName);
            if (!File.Exists(path)) { detail = "no playable contract in bundle (pre-rev4 snapshot)"; return ImportedContractOutcome.NotPresent; }
            byte[] bytes = File.ReadAllBytes(path);
            var parse = GdaiPlayableContract.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            if (!parse.Ok) { detail = "contract invalid: " + parse.Summary; return ImportedContractOutcome.Failed; }
            string sha = GdaiPlayableResume.Sha256Hex(bytes);
            result = Run(parse.Contract, projectId, snapshotId, sha, scenePath, nowUtc);
            detail = result.Completed ? ("receipt " + result.Receipt.status) : result.Error;
            return result.Completed ? ImportedContractOutcome.Composed : ImportedContractOutcome.Failed;
        }

        /// <summary>
        /// Run the full static composition. Sprites are resolved from the imported-asset registry by the
        /// contract's entity ids when present (null is fine — the enemy collider still gets a non-zero
        /// box); explicit overrides are for tests. nowUtc is injected (no hidden Date.Now).
        /// </summary>
        public static Result Run(GdaiPlayableContract c, string projectId, string snapshotId, string contractSha256,
            string scenePath, DateTime nowUtc, Sprite playerSpriteOverride = null, Sprite enemySpriteOverride = null)
        {
            var res = new Result();
            if (c == null) { res.Error = "null contract"; return res; }
            string root = Root();

            var op = GdaiPlayableOperation.Create(root, projectId, snapshotId,
                c.schema_version, c.contract_revision, contractSha256, scenePath, nowUtc);
            res.OperationId = op.operation_id;

            // Gate B · B7: count red console errors (Error/Exception) raised WHILE composing, so the receipt can
            // fail-closed on a scene that composed "successfully" per return values but logged a real error. The
            // count is snapshotted before the receipt's own readback (below) so the receipt does not count itself.
            int redErrors = 0;
            Application.LogCallback redErrorCounter = (cond, st, type) =>
            { if (type == LogType.Error || type == LogType.Exception) redErrors++; };
            Application.logMessageReceived += redErrorCounter;

            try
            {
                // 1 · canonical scene + Build Settings
                var scene = GdaiCanonicalScene.EnsureSavedAndInBuild(scenePath);
                if (!scene.Ok) return Fail(res, op, root, nowUtc, "canonical scene: " + scene.Error);
                op.Advance(root, GdaiPlayablePhase.CanonicalSceneReady, nowUtc);

                // 2 · compose the five owned objects
                var compose = GdaiSceneObjectComposer.Compose(c, c.profile_id, snapshotId);
                if (!compose.Ok) return Fail(res, op, root, nowUtc, "compose: " + compose.Summary);
                op.Advance(root, GdaiPlayablePhase.SceneObjectsComposed, nowUtc);

                // 2b · Gate B: consume GDAI_SceneAssembly.json INSIDE this canonical scene (single authority).
                //      Arena boundary colliders + scene element colliders + spawn markers now live in the
                //      scene the composer owns and saves, instead of being placed pre-reset and wiped.
                var sceneAsm = GDAI.Bridge.Editor.LayerB.GdaiSceneAssemblyComposerCore.Compose();
                res.Log.Add("scene assembly: " + sceneAsm.summary);
                if (!sceneAsm.ok) return Fail(res, op, root, nowUtc, "scene assembly: " + sceneAsm.summary);

                // 3 · deterministic input asset. The InputActionReference sub-assets are read later by
                // path-scoped LoadAllAssetsAtPath (which forces a synchronous load) — so a single import
                // here is enough and we avoid the double-import that duplicates the reference sub-assets.
                var input = GdaiInputAssetBuilder.EnsureAsset(c);
                if (!input.Ok) return Fail(res, op, root, nowUtc, "input asset: " + input.Error);
                op.Advance(root, GdaiPlayablePhase.InputAssetReady, nowUtc);

                // 4 · enemy prefab (sprite from registry when available) + bind to director
                var enemySprite = enemySpriteOverride ?? ResolveSprite(c.enemy_prefab?.sprite_entity_id);
                var enemy = GdaiEnemyPrefabBuilder.Build(c.enemy_prefab, enemySprite, c.profile_id, snapshotId);
                if (!enemy.Ok) return Fail(res, op, root, nowUtc, "enemy prefab: " + enemy.Summary);
                if (!GdaiEnemyPrefabBuilder.BindToDirector(enemy.PrefabPath, c.enemy_prefab, out string bderr))
                    return Fail(res, op, root, nowUtc, "enemy director bind: " + bderr);
                op.Advance(root, GdaiPlayablePhase.EnemyPrefabReady, nowUtc);

                // 5 · player sprite (best-effort; visibility is proven in PlayMode, not the static receipt)
                var playerSprite = playerSpriteOverride ?? ResolveSprite(c.player?.sprite_entity_id);
                if (playerSprite != null) AssignPlayerSprite(playerSprite, res);

                // 6 · bindings: 7 scene refs + 3 input refs + value bindings (all contract-driven), then camera
                var bind = GdaiPlayableBindingApplier.Apply(c);
                res.Log.Add("bindings: " + bind.Summary);
                var camGo = GdaiSceneObjectComposer.FindOwned("Main Camera");
                var cam = GdaiCameraConfigurer.Apply(camGo, c.camera);
                res.Log.Add("camera: " + (cam.Ok ? ("ortho " + cam.OrthographicSize.ToString("0.###")) : ("ERR " + cam.Error)));
                op.Advance(root, GdaiPlayablePhase.BindingsApplied, nowUtc);

                // 7 · exactly one active AudioListener (non-destructive policy)
                var audio = GdaiAudioListenerEnsurer.Ensure();
                res.Log.Add("audio: " + audio.Message);
                op.Advance(root, GdaiPlayablePhase.SelfChecksPassed, nowUtc);

                // 8 · save the scene, re-affirm Build Settings
                var active = SceneManager.GetActiveScene();
                if (!EditorSceneManager.SaveScene(active, scenePath))
                    return Fail(res, op, root, nowUtc, "scene save failed");
                GdaiCanonicalScene.EnsureSavedAndInBuild(scenePath);
                op.Advance(root, GdaiPlayablePhase.SceneSaved, nowUtc);

                // 9 · ownership manifest — LAST, atomic, before the receipt reads it
                if (!GdaiPlayableOwnershipManifest.Write(c, projectId, snapshotId, contractSha256, scenePath, out string merr))
                    return Fail(res, op, root, nowUtc, "ownership manifest: " + merr);

                // 10 · HARD RECEIPT — independent readback (reopens the saved scene). Pass the red-error count
                //      captured during composition (snapshotted here, before the receipt's own readback).
                var receipt = GdaiPlayableReceiptWriter.Build(c, projectId, snapshotId, contractSha256, scenePath, nowUtc, redErrors);
                if (!GdaiPlayableReceiptWriter.Write(receipt, out string rerr))
                    return Fail(res, op, root, nowUtc, "receipt write: " + rerr);
                res.Receipt = receipt;
                op.Advance(root, GdaiPlayablePhase.ReceiptWritten, nowUtc);

                // 11 · gate: only a PASS receipt completes the operation
                if (!receipt.IsPass)
                {
                    op.Fail(root, nowUtc, "receipt " + receipt.status + ": " + string.Join("; ", receipt.failures.Take(6)));
                    res.Error = "receipt not PASS (" + receipt.status + ")";
                    return res;
                }
                op.Advance(root, GdaiPlayablePhase.Complete, nowUtc);
                res.Completed = true;
                return res;
            }
            catch (Exception e) { return Fail(res, op, root, nowUtc, "cta exception: " + e.Message); }
            finally { Application.logMessageReceived -= redErrorCounter; }
        }

        private static Result Fail(Result res, GdaiPlayableOperation op, string root, DateTime nowUtc, string reason)
        {
            res.Error = reason;
            op.Fail(root, nowUtc, reason);
            return res;
        }

        private static Sprite ResolveSprite(string entityId)
        {
            if (string.IsNullOrEmpty(entityId)) return null;
            return GdaiImportedAssetRegistry.TryGetSpriteForEntity(entityId, out var s, out _) ? s : null;
        }

        private static void AssignPlayerSprite(Sprite sprite, Result res)
        {
            var player = GdaiSceneObjectComposer.FindOwned("Player");
            var sr = player != null ? player.GetComponent<SpriteRenderer>() : null;
            if (sr == null) { res.Log.Add("player sprite: no SpriteRenderer"); return; }
            Undo.RecordObject(sr, "GDAI assign player sprite");
            sr.sprite = sprite;
            sr.color = Color.white;
            // Gate B AC-2: re-derive the Player collider from the now-assigned sprite bounds (deterministic).
            GdaiSceneObjectComposer.EnsurePlayerPhysics(player, sprite);
            res.Log.Add("player sprite assigned: " + sprite.name);
        }
    }
}
