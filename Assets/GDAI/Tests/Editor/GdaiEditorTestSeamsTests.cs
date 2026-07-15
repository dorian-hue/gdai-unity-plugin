// T4 0J C4/C5/C6 · tests for the dormant TEST_ONLY editor seams (session override + confirmation policy)
// and the Complete-Sync UI enable-gate. These prove the seams cannot approve/inject in production or
// interactive mode, only for the exact enumerated kinds bound to the exact operation identity.
using System;
using System.Reflection;
using NUnit.Framework;
using GDAI.Bridge.Editor.LayerA;

namespace GDAI.Tests.Editor
{
    public class GdaiEditorTestSeamsTests
    {
        // A representative scoped approver, exactly what the A4 harness installs: approve ONLY for the exact
        // operation identity AND only the two enumerated kinds; everything else denied.
        static Func<GdaiConfirmationKind, bool> ScopedApprover(string expectedOp) => kind =>
            GdaiEditorTestHooks.Session?.operationId == expectedOp &&
            (kind == GdaiConfirmationKind.ReplaceGeneratedRoot || kind == GdaiConfirmationKind.ReplaceEnemyPrefab);

        [SetUp]
        public void Reset() { GdaiEditorTestHooks.ClearSession(); GdaiEditorConfirmationPolicy.Clear(); }

        [TearDown]
        public void Cleanup() { GdaiEditorTestHooks.ClearSession(); GdaiEditorConfirmationPolicy.Clear(); }

        // ── C5 · confirmation policy ──
        [Test]
        public void NoOverride_FallsThroughToRealDialog()
        {
            // TryTestDecision returns null ⇒ Confirm() would call the real EditorUtility.DisplayDialog.
            Assert.IsNull(GdaiEditorConfirmationPolicy.TryTestDecision(GdaiConfirmationKind.ReplaceGeneratedRoot, isBatchMode: true));
        }

        [Test]
        public void InteractiveMode_NeverAutoApproves_EvenWithOverride()
        {
            GdaiEditorTestHooks.Session = new GdaiTestSession { operationId = "opA" };
            GdaiEditorConfirmationPolicy.TestOverride = ScopedApprover("opA");
            // isBatchMode:false ⇒ null ⇒ real dialog (no auto-approval in interactive mode).
            Assert.IsNull(GdaiEditorConfirmationPolicy.TryTestDecision(GdaiConfirmationKind.ReplaceGeneratedRoot, isBatchMode: false));
        }

        [Test]
        public void ExactOperation_AllowedKind_Approved()
        {
            GdaiEditorTestHooks.Session = new GdaiTestSession { operationId = "opA" };
            GdaiEditorConfirmationPolicy.TestOverride = ScopedApprover("opA");
            Assert.IsTrue(GdaiEditorConfirmationPolicy.TryTestDecision(GdaiConfirmationKind.ReplaceGeneratedRoot, isBatchMode: true));
            Assert.IsTrue(GdaiEditorConfirmationPolicy.TryTestDecision(GdaiConfirmationKind.ReplaceEnemyPrefab, isBatchMode: true));
        }

        [Test]
        public void WrongOperation_Denied()
        {
            GdaiEditorTestHooks.Session = new GdaiTestSession { operationId = "opB" }; // session ≠ approver's op
            GdaiEditorConfirmationPolicy.TestOverride = ScopedApprover("opA");
            Assert.IsFalse(GdaiEditorConfirmationPolicy.TryTestDecision(GdaiConfirmationKind.ReplaceGeneratedRoot, isBatchMode: true));
        }

        [Test]
        public void KindOutsideThisApprover_Denied()
        {
            // an approver that only whitelists ReplaceGeneratedRoot must deny ReplaceEnemyPrefab.
            GdaiEditorTestHooks.Session = new GdaiTestSession { operationId = "opA" };
            GdaiEditorConfirmationPolicy.TestOverride = kind =>
                GdaiEditorTestHooks.Session?.operationId == "opA" && kind == GdaiConfirmationKind.ReplaceGeneratedRoot;
            Assert.IsTrue(GdaiEditorConfirmationPolicy.TryTestDecision(GdaiConfirmationKind.ReplaceGeneratedRoot, isBatchMode: true));
            Assert.IsFalse(GdaiEditorConfirmationPolicy.TryTestDecision(GdaiConfirmationKind.ReplaceEnemyPrefab, isBatchMode: true));
        }

        [Test]
        public void ClearedOverride_FallsThroughAgain()
        {
            GdaiEditorTestHooks.Session = new GdaiTestSession { operationId = "opA" };
            GdaiEditorConfirmationPolicy.TestOverride = ScopedApprover("opA");
            GdaiEditorConfirmationPolicy.Clear();
            Assert.IsNull(GdaiEditorConfirmationPolicy.TryTestDecision(GdaiConfirmationKind.ReplaceGeneratedRoot, isBatchMode: true));
        }

        // ── C4 · session hook is inert unless explicitly injected ──
        [Test]
        public void SessionInactive_ByDefault()
        {
            Assert.IsNull(GdaiEditorTestHooks.Session);
            Assert.IsFalse(GdaiEditorTestHooks.SessionActive); // Session null ⇒ inactive regardless of batchmode
        }

        // ── C6 · Complete-Sync UI enable-gate (the operation the enabled button invokes) ──
        [Test]
        public void CompleteSyncButton_EnabledOnlyWhenPairedBoundReadyAndNotBusy()
        {
            foreach (GdaiBindingState st in Enum.GetValues(typeof(GdaiBindingState)))
            {
                bool expected = st == GdaiBindingState.PairedBoundReady;
                Assert.AreEqual(expected, GdaiCoherentBundleWindow.CompleteSyncButtonEnabled(st, busy: false), $"state={st}");
                Assert.IsFalse(GdaiCoherentBundleWindow.CompleteSyncButtonEnabled(st, busy: true), $"busy blocks state={st}");
            }
        }

        [Test]
        public void CompleteExportSyncOperation_Exists_OnTheWindow()
        {
            // operation-level seam (NOT a rendered-click proof): the enabled button invokes this private op.
            var m = typeof(GdaiCoherentBundleWindow).GetMethod("CompleteExportSync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "CompleteExportSync operation must exist as the Complete-Sync handler target");
        }
    }
}
