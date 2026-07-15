// AUTO-0Q-P2 · P2-B(LIVE) EditMode companion · Exhaustive resume-DECISION logic.
// The live two-process driver proves the [InitializeOnLoad] hook fires after a real
// domain reload; these prove every decision branch (no-op, refuse-on-mismatch,
// resume-exact-phase, destructive-guard, terminal) deterministically.
using System;
using System.IO;
using GDAI.Bridge.Editor.LayerA;
using NUnit.Framework;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiPlayableResumeTests
    {
        private string _root;
        private static readonly DateTime T = new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc);
        private const string PID = "18bedbf4-3993-422b-97ce-e5eb910bb55c";
        private const string SNAP = "2d874a40-f10d-4d90-a3c4-5c7ffdb7cdee";
        private const string SCHEMA = "gdai.unity.playable_contract.v1";
        private const int REV = 4;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "gdai-resume-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown() { try { Directory.Delete(_root, true); } catch { } }

        private void WriteBinding(string projectId)
        {
            var p = Path.Combine(_root, GdaiProjectBinding.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, "{\"schema_version\":\"gdai.unity_export.v1\",\"project_id\":\"" + projectId + "\",\"generated_owned_paths\":[\"Assets/GDAI_Generated\"]}");
        }

        private void WriteContract(string mode)
        {
            var g = UnityEditor.AssetDatabase.FindAssets("PlayableContract.rev4.projectslash-2d874a40");
            var json = File.ReadAllText(UnityEditor.AssetDatabase.GUIDToAssetPath(g[0]));
            // "INVALID" corrupts the profile so the whole contract fails validation;
            // "OKDIFF" keeps it valid (same schema+revision) but changes the bytes → different sha256.
            if (mode == "INVALID") json = json.Replace("unity.pointer_action_demo.v1", "unity.other.v1");
            else if (mode == "OKDIFF") json = json + "\n"; // trailing whitespace: still valid JSON, new sha
            var p = Path.Combine(_root, "Assets", "GDAI_Generated", GdaiPlayableContract.BundleFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, json);
        }

        private void Session(bool present)
        {
            var p = GdaiPlayableResume.BoundSessionMarkerPath(_root);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            if (present) File.WriteAllText(p, "bound"); else if (File.Exists(p)) File.Delete(p);
        }

        private GdaiPlayableOperation StartAt(GdaiPlayablePhase phase)
        {
            // pin the identity of whatever contract WriteContract put on disk
            var id = GdaiPlayableResume.OnDiskContractIdentity(_root);
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP,
                id.Ok ? id.Schema : SCHEMA, id.Ok ? id.Revision : REV, id.Ok ? id.Sha256 : "none",
                "Assets/Scenes/Main.unity", T);
            op.Advance(_root, phase, T);
            return op;
        }

        [Test]
        public void NoOperation_NoApply()
        {
            WriteBinding(PID); Session(true); WriteContract("ok");
            Assert.AreEqual(GdaiPlayableResume.Decision.NoOperation, GdaiPlayableResume.ResumeAfterReload(_root, T));
        }

        [Test]
        public void HappyPath_ResumesExactNextPhase()
        {
            WriteBinding(PID); Session(true); WriteContract("ok");
            var op = StartAt(GdaiPlayablePhase.CodeImportedAwaitingReload);
            Assert.AreEqual(GdaiPlayableResume.Decision.Resumed, GdaiPlayableResume.ResumeAfterReload(_root, T));
            var reloaded = GdaiPlayableOperation.LoadActive(_root, out _);
            Assert.AreEqual(GdaiPlayablePhase.ResumedAfterReload, reloaded.phase);
        }

        [Test]
        public void Refuse_ProjectMismatch()
        {
            WriteBinding("00000000-0000-4000-8000-000000000000"); Session(true); WriteContract("ok");
            StartAt(GdaiPlayablePhase.CodeImportedAwaitingReload);
            Assert.AreEqual(GdaiPlayableResume.Decision.Refused, GdaiPlayableResume.ResumeAfterReload(_root, T));
        }

        [Test]
        public void Refuse_NoBoundSession()
        {
            WriteBinding(PID); Session(false); WriteContract("ok");
            StartAt(GdaiPlayablePhase.CodeImportedAwaitingReload);
            Assert.AreEqual(GdaiPlayableResume.Decision.Refused, GdaiPlayableResume.ResumeAfterReload(_root, T));
        }

        [Test]
        public void Refuse_InvalidContractOnDisk()
        {
            WriteBinding(PID); Session(true); WriteContract("INVALID");
            StartAt(GdaiPlayablePhase.CodeImportedAwaitingReload);
            Assert.AreEqual(GdaiPlayableResume.Decision.Refused, GdaiPlayableResume.ResumeAfterReload(_root, T),
                "a contract that fails validation on disk must never resume");
        }

        [Test]
        public void Refuse_ContractBytesDrift()
        {
            WriteBinding(PID); Session(true); WriteContract("ok");
            // operation pins the ORIGINAL contract identity (schema + revision + sha256)
            StartAt(GdaiPlayablePhase.CodeImportedAwaitingReload);
            // disk then holds a still-valid contract with the SAME schema+revision but different bytes
            WriteContract("OKDIFF");
            Assert.AreEqual(GdaiPlayableResume.Decision.Refused, GdaiPlayableResume.ResumeAfterReload(_root, T),
                "same schema+revision but drifted bytes (sha256 changed) must refuse — this is what schema alone cannot catch");
        }

        [Test]
        public void Refuse_NoContractOnDisk()
        {
            WriteBinding(PID); Session(true); // no contract file
            StartAt(GdaiPlayablePhase.CodeImportedAwaitingReload);
            Assert.AreEqual(GdaiPlayableResume.Decision.Refused, GdaiPlayableResume.ResumeAfterReload(_root, T));
        }

        [Test]
        public void Terminal_Aborted_NeverResumes()
        {
            WriteBinding(PID); Session(true); WriteContract("ok");
            var op = StartAt(GdaiPlayablePhase.SceneObjectsComposed);
            op.Abort(_root, T);
            Assert.AreEqual(GdaiPlayableResume.Decision.NoOperation, GdaiPlayableResume.ResumeAfterReload(_root, T),
                "aborted op is not active → treated as no operation");
        }

        [Test]
        public void DestructivePhase_NeverRepeated()
        {
            WriteBinding(PID); Session(true); WriteContract("ok");
            var op = StartAt(GdaiPlayablePhase.GeneratedRootReplaced); // committed = true
            Assert.AreEqual(GdaiPlayableResume.Decision.Resumed, GdaiPlayableResume.ResumeAfterReload(_root, T));
            var reloaded = GdaiPlayableOperation.LoadActive(_root, out _);
            // resumes PAST the committed destructive replace, never back into it
            Assert.AreEqual(GdaiPlayablePhase.CodeImportedAwaitingReload, reloaded.phase);
            Assert.IsTrue(reloaded.generated_root_committed);
        }

        [Test]
        public void Breadcrumb_RecordsDecision()
        {
            WriteBinding(PID); Session(true); WriteContract("ok");
            StartAt(GdaiPlayablePhase.CodeImportedAwaitingReload);
            GdaiPlayableResume.ResumeAfterReload(_root, T);
            var bc = File.ReadAllText(GdaiPlayableResume.BreadcrumbPath(_root));
            StringAssert.Contains("\"decision\": \"Resumed\"", bc);
            StringAssert.Contains("\"destructive_repeated\": false", bc);
        }
    
        [Test]
        public void Refuse_Rev3OperationUnderRev4Contract()
        {
            WriteBinding(PID); Session(true); WriteContract("ok"); // rev4 on disk
            // operation pinned to rev3 schema+revision (different sha too)
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP, SCHEMA, 3, "oldsha", "Assets/Scenes/Main.unity", T);
            op.Advance(_root, GdaiPlayablePhase.CodeImportedAwaitingReload, T);
            Assert.AreEqual(GdaiPlayableResume.Decision.Refused, GdaiPlayableResume.ResumeAfterReload(_root, T),
                "a rev3 operation must never resume under a rev4 on-disk contract");
        }

        [Test]
        public void Refuse_StaleOperation()
        {
            WriteBinding(PID); Session(true); WriteContract("ok");
            StartAt(GdaiPlayablePhase.CodeImportedAwaitingReload);
            // evaluate 48h later → stale
            Assert.AreEqual(GdaiPlayableResume.Decision.Refused, GdaiPlayableResume.ResumeAfterReload(_root, T.AddHours(48)));
        }

        [Test]
        public void AtomicWrite_LeavesNoTempFile()
        {
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP, SCHEMA, REV, "sha", "Assets/Scenes/Main.unity", T);
            op.Advance(_root, GdaiPlayablePhase.SceneObjectsComposed, T);
            var dir = GdaiPlayableOperation.OperationsDir(_root);
            Assert.IsEmpty(Directory.GetFiles(dir, "*.tmp"), "no half-written temp file remains");
            Assert.AreEqual(1, Directory.GetFiles(dir, "*.json").Length);
        }

    }
}
