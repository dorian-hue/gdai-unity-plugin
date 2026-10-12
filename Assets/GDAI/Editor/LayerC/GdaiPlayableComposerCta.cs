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
        private static GdaiSourceBoundPlayablePlan.Readiness GeneratedReadiness(out string error)
        {
            error=null;var path=Path.Combine(Root(), "Assets", "GDAI_Generated", GdaiPlayableContract.BundleFileName);
            if(File.Exists(path)) {
                var parsed=GdaiPlayableContract.Parse(File.ReadAllText(path));
                if(!parsed.Ok){error=parsed.Summary;return GdaiSourceBoundPlayablePlan.Readiness.Terminal;}
                if(parsed.Contract.profile_id==GdaiSourceBoundPlayablePlan.Profile) {
                    var readiness=GdaiSourceBoundPlayablePlan.AssessReadiness(parsed.Contract,out error);
                    if(readiness!=GdaiSourceBoundPlayablePlan.Readiness.Ready)return readiness;
                    try {
                        GdaiSourceBoundPlayableComposer.AdmitTransientComposition(parsed.Contract);
                    }catch(Exception ex){error="compiled source admission: "+ex.Message;return GdaiSourceBoundPlayablePlan.Readiness.Terminal;}
                    return GdaiSourceBoundPlayablePlan.Readiness.Ready;
                }
            }
            return GdaiSceneObjectComposer.ResolveComponentType(ProbeType)!=null?GdaiSourceBoundPlayablePlan.Readiness.Ready:GdaiSourceBoundPlayablePlan.Readiness.Transient;
        }
        public static GdaiSourceBoundPlayablePlan.Readiness InspectImportedContractReadiness(out bool present,out string error)
        {
            present=File.Exists(Path.Combine(Root(),"Assets","GDAI_Generated",GdaiPlayableContract.BundleFileName));
            return present?GeneratedReadiness(out error):NoImportedContract(out error);
        }
        private static GdaiSourceBoundPlayablePlan.Readiness NoImportedContract(out string error) {error=null;return GdaiSourceBoundPlayablePlan.Readiness.Ready;}
        private static ImportedContractOutcome TerminalFailure(string root,string project,string snapshot,string scene,DateTime now,string error,out Result result,out string detail)
        {
            ClearPendingCompose(root);GdaiSyncFinalization.Clear(root);
            var path=Path.Combine(root,"Assets","GDAI_Generated",GdaiPlayableContract.BundleFileName);
            var sha=File.Exists(path)?GdaiPlayableResume.Sha256Hex(File.ReadAllBytes(path)):null;
            var op=GdaiPlayableOperation.Create(root,project,snapshot,GdaiPlayableContract.Schema,4,sha,scene,now);op.Fail(root,now,error);
            result=new Result {Error=error,OperationId=op.operation_id};detail="terminal generated identity failure: "+error;return ImportedContractOutcome.Failed;
        }

        /// <summary>
        /// B-1: true when RunOrDeferFromImportedContract would defer — the imported bundle
        /// carries the rev4 contract but its generated types have not compiled yet. The
        /// window CTA checks this BEFORE running the Layer-B seams so no scene work lands
        /// on pre-reload state that the imminent domain reload would discard (MNT-10).
        /// Same two facts the defer branch itself tests; keep them in lockstep.
        /// </summary>
        public static bool WouldDeferFromImportedContract()
        {
            var readiness=InspectImportedContractReadiness(out var present,out _);
            return present&&readiness==GdaiSourceBoundPlayablePlan.Readiness.Transient;
        }

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

            var readiness=GeneratedReadiness(out var readinessError);
            if(readiness==GdaiSourceBoundPlayablePlan.Readiness.Terminal)return TerminalFailure(root,projectId,snapshotId,scenePath,nowUtc,readinessError,out result,out detail);
            if (readiness==GdaiSourceBoundPlayablePlan.Readiness.Transient)
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
        /// The shared post-import normal route for the source-bound profile. Both the visible
        /// Complete Sync window and the domain-reload resume hook call this before any legacy
        /// canonical-scene or Layer-B mutation. False means the imported contract is an ordinary
        /// legacy profile and the caller must retain its existing route unchanged.
        /// </summary>
        public static bool TryRunSourceBoundNormalRoute(string projectId,string snapshotId,string scenePath,
            DateTime nowUtc,out ImportedContractOutcome outcome,out Result result,out string detail)
        {
            outcome=ImportedContractOutcome.NotPresent;result=null;detail=null;
            var path=Path.Combine(Root(),"Assets","GDAI_Generated",GdaiPlayableContract.BundleFileName);
            if(!File.Exists(path))return false;
            var parsed=GdaiPlayableContract.Parse(File.ReadAllText(path));
            // A contract that changed or became malformed after the caller's admission may
            // never fall through into scene-mutating legacy behavior.
            if(!parsed.Ok) {outcome=RunOrDeferFromImportedContract(projectId,snapshotId,scenePath,nowUtc,out result,out detail);return true;}
            if(parsed.Contract.profile_id!=GdaiSourceBoundPlayablePlan.Profile)return false;
            outcome=RunOrDeferFromImportedContract(projectId,snapshotId,scenePath,nowUtc,out result,out detail);
            return true;
        }

        /// <summary>Source-bound completion saves only the owned active canonical scene.
        /// Loaded dirty or never-saved user scenes remain in memory and are never sent through
        /// SaveOpenScenes. Legacy finalization keeps its existing behavior.</summary>
        public static GdaiSyncFinalization.FinalizeResult FinalizeSourceBoundNormalRoute(string projectRoot,GdaiSyncFinalizationContext ctx,DateTime nowUtc)
        {
            var r=new GdaiSyncFinalization.FinalizeResult();
            if(ctx==null){r.FailPhase="FINALIZE_CONTEXT_MISSING";return r;}
            try {
                AssetDatabase.SaveAssets();var active=SceneManager.GetActiveScene();
                if(!active.IsValid()||string.IsNullOrEmpty(active.path)||!EditorSceneManager.SaveScene(active)){r.FailPhase="SCENE_SAVE";return r;}
                int elementCount=0,colliderCount=0;foreach(var col in UnityEngine.Object.FindObjectsByType<PolygonCollider2D>(FindObjectsSortMode.None)){colliderCount++;_=col;}
                var sceneRoot=GameObject.Find("GDAI_SceneAssembly");if(sceneRoot!=null)elementCount=sceneRoot.transform.childCount;
                var receipt=new GdaiExportReceiptData {project_id=ctx.project_id,snapshot_id=ctx.snapshot_id,snapshot_revision=ctx.snapshot_revision,plugin_version=ctx.plugin_version,generated_file_count=ctx.generated_file_count,binary_asset_count=ctx.binary_asset_count,preserved_meta_count=ctx.preserved_meta_count,backup_path=ctx.backup_path,scene_path=active.path,scene_elements_count=elementCount,collider_count=colliderCount,started_at=ctx.started_at,completed_at=nowUtc.ToString("o"),result="PASS"};
                r.ReceiptPath=GdaiExportReceipt.Write(projectRoot,receipt,nowUtc);r.SceneElementsCount=elementCount;r.ColliderCount=colliderCount;r.Ok=true;return r;
            }catch(Exception e){r.FailPhase="FINALIZE_EXCEPTION: "+e.Message;return r;}
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
            var readiness=GeneratedReadiness(out var readinessError);
            if(readiness==GdaiSourceBoundPlayablePlan.Readiness.Terminal){outcome=TerminalFailure(root,pc.project_id,pc.snapshot_id,pc.scene_path,nowUtc,readinessError,out result,out detail);return true;}
            if(readiness==GdaiSourceBoundPlayablePlan.Readiness.Transient) {detail=readinessError??"generated code not compiled yet";return false;}
            ClearPendingCompose(root); // run once
            string scenePath = string.IsNullOrEmpty(pc.scene_path) ? "Assets/Scenes/Main.unity" : pc.scene_path;
            // Source-bound composition owns its complete additive scene/asset lifecycle. It must
            // run before the legacy canonical and Layer-B path so its admission and rollback see
            // the real pre-sync state. Ordinary contracts retain the established legacy ordering.
            var sourceBoundHandled=TryRunSourceBoundNormalRoute(pc.project_id,pc.snapshot_id,scenePath,nowUtc,out outcome,out result,out detail);
            if(!sourceBoundHandled)
            {
                var canonical = GdaiCanonicalScene.EnsureSavedAndInBuild(scenePath);
                if (!canonical.Ok)
                {
                    GdaiSyncFinalization.Clear(root); // never finalize a failed resume
                    outcome = ImportedContractOutcome.Failed;
                    detail = "canonical scene: " + canonical.Error;
                    return true;
                }
                try
                {
                    GdaiSceneBackgroundBinder.TryPlaceFromPointer(out string bgMsg);
                    Debug.Log("[GDAI][ResumeCompose] scene background: " + bgMsg);
                    GdaiSceneAssemblyElements.PlaceSceneElementsMenu();
                    var spawn = GdaiSceneAssemblySpawnMarkers.PlaceSpawnMarkers();
                    Debug.Log("[GDAI][ResumeCompose] spawn markers: " + spawn);
                    GdaiSceneAssemblyArenaBounds.ShowArenaBoundsMenu();
                    GdaiSceneAssemblyDemoObstacle.CreateDemoObstacleMenu();
                    GdaiSceneAssemblyEdgeBlockers.CreateEdgeBlockersMenu();
                }
                catch (Exception e)
                {
                    GdaiSyncFinalization.Clear(root);
                    outcome = ImportedContractOutcome.Failed;
                    detail = "layer-b seams: " + e.Message;
                    return true;
                }
                outcome = RunFromImportedContract(pc.project_id, pc.snapshot_id, scenePath, nowUtc, out result, out detail);
            }

            // B-1 finalization — exactly once, only after a composed scene, only with the
            // context persisted for THIS operation (TryTake is the read+delete latch and
            // refuses a project/snapshot mismatch).
            if (outcome == ImportedContractOutcome.Composed)
            {
                if (GdaiSyncFinalization.TryTake(root, pc.project_id, pc.snapshot_id, out var fctx))
                {
                    var fin = sourceBoundHandled?FinalizeSourceBoundNormalRoute(root,fctx,nowUtc):GdaiSyncFinalization.FinalizeSync(root, fctx, nowUtc);
                    if (fin.Ok)
                    {
                        detail = detail + " · finalized: " + fin.SceneElementsCount + " scene element(s), receipt " + Path.GetFileName(fin.ReceiptPath);
                    }
                    else
                    {
                        detail = detail + " · FINALIZE FAILED: " + fin.FailPhase + " (no receipt written)";
                        Debug.LogError("[GDAI][ResumeCompose] finalize failed: " + fin.FailPhase);
                    }
                }
                else
                {
                    // No valid persisted context (pre-B-1 defer, or consumed already). Fail
                    // closed on the receipt — never invent import stats — but keep the scene:
                    // save what the composer built so the objects survive the next reload.
                    if(sourceBoundHandled) {
                        var active=SceneManager.GetActiveScene();if(active.IsValid()&&!string.IsNullOrEmpty(active.path))EditorSceneManager.SaveScene(active);
                    } else UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                    Debug.LogWarning("[GDAI][ResumeCompose] no pending finalization context — scene saved, no completion receipt written");
                }
            }
            else
            {
                GdaiSyncFinalization.Clear(root); // failed compose: drop the stale context, no receipt
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
            if(c.profile_id==GdaiSourceBoundPlayablePlan.Profile)
                return GdaiSourceBoundPlayableComposer.Run(c,projectId,snapshotId,contractSha256,scenePath,nowUtc);

            var op = GdaiPlayableOperation.Create(root, projectId, snapshotId,
                c.schema_version, c.contract_revision, contractSha256, scenePath, nowUtc);
            res.OperationId = op.operation_id;

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

                // 10 · HARD RECEIPT — independent readback (reopens the saved scene)
                var receipt = GdaiPlayableReceiptWriter.Build(c, projectId, snapshotId, contractSha256, scenePath, nowUtc);
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
            res.Log.Add("player sprite assigned: " + sprite.name);
        }
    }
}
