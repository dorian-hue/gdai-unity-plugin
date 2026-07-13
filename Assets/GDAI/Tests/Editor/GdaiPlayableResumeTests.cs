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
        private const string CVER = "gdai.unity.playable_contract.v1/unity.pointer_action_demo.v1";

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

        private void WriteContract(string schemaProfile)
        {
            var g = UnityEditor.AssetDatabase.FindAssets("PlayableContract.rev3.projectslash-2d874a40");
            var json = File.ReadAllText(UnityEditor.AssetDatabase.GUIDToAssetPath(g[0]));
            // optionally corrupt the profile to force a revision mismatch
            if (schemaProfile == "MISMATCH") json = json.Replace("unity.pointer_action_demo.v1", "unity.other.v1");
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
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP, CVER, "Assets/Scenes/Main.unity", T);
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
        public void Refuse_ContractRevisionMismatch()
        {
            WriteBinding(PID); Session(true); WriteContract("MISMATCH");
            StartAt(GdaiPlayablePhase.CodeImportedAwaitingReload);
            Assert.AreEqual(GdaiPlayableResume.Decision.Refused, GdaiPlayableResume.ResumeAfterReload(_root, T));
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
    }
}
