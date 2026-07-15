// =====================================================================================
// GDAI Unity Plugin · T4 0J C4/C5 · TEST-ONLY editor seams (dormant by default).
//
// These exist ONLY so a repo-external headless A4 harness can drive the REAL window operation without
// (a) writing test values into the user's real production EditorPrefs, and (b) getting stuck on an
// interactive DisplayDialog it cannot answer. Neither seam changes production behavior:
//   · SessionActive is false unless a harness explicitly sets Session AND the editor is in batchmode.
//   · ConfirmationPolicy.TestOverride is null unless a harness sets it AND the editor is in batchmode.
// Interactive users, and even a normal batchmode build, never see any behavior change.
//
// Hard constraints honored (see operator authorization C4/C5):
//   · No real token is read/copied/backed-up/logged: the session token is a harness-supplied non-secret.
//   · No wildcard confirmation approval: only the two enumerated GdaiConfirmationKind values are approvable,
//     and only through an explicitly-installed override bound to the exact A4 operation identity.
//   · No environment-variable product bypass; no interactive-mode auto-approval; no implicit approval of
//     future dialogs (a new dialog is subject to the policy only if it is routed through Confirm(kind,…)).
// =====================================================================================
using System;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("GDAI.Editor.Tests")]

namespace GDAI.Bridge.Editor.LayerA
{
    /// <summary>A harness-injected TEST_ONLY session. Never persisted; re-injected after each domain reload.</summary>
    internal sealed class GdaiTestSession
    {
        public string functionsBase;
        public string projectId;
        public string token;        // non-secret harness token; NEVER a real user token
        public string operationId;  // the exact A4 operation identity (binds confirmation approval)
    }

    internal static class GdaiEditorTestHooks
    {
        // Set only by the repo-external A4 harness (via reflection), only in batchmode, only for one run.
        internal static GdaiTestSession Session;

        /// <summary>True only when a harness has injected a session AND we are headless. Guards every seam.</summary>
        internal static bool SessionActive => Session != null && Application.isBatchMode;

        internal static void ClearSession() { Session = null; }
    }

    /// <summary>The exact, enumerated set of confirmations a headless A4 run may auto-approve. Allowlist.</summary>
    internal enum GdaiConfirmationKind
    {
        ReplaceGeneratedRoot,
        ReplaceEnemyPrefab,
    }

    /// <summary>
    /// Central confirmation policy. Production/interactive path ALWAYS shows the real EditorUtility dialog.
    /// A TEST_ONLY override may auto-decide ONLY in batchmode and ONLY for an enumerated kind. Callers route
    /// their existing confirms through Confirm(kind,…); the dialog text/behavior is otherwise unchanged.
    /// </summary>
    internal static class GdaiEditorConfirmationPolicy
    {
        // Installed only by the A4 harness (reflection), only in batchmode. A closure that returns true only
        // for the exact operation identity + allowed kinds; anything else returns false.
        internal static Func<GdaiConfirmationKind, bool> TestOverride;

        internal static void Clear() { TestOverride = null; }

        /// <summary>
        /// Pure decision seam (testable without showing a dialog): returns the test decision, or null when
        /// no test decision applies (⇒ caller must show the real dialog). Auto-decide requires an installed
        /// override AND batchmode; interactive mode always returns null.
        /// </summary>
        internal static bool? TryTestDecision(GdaiConfirmationKind kind, bool isBatchMode)
        {
            if (TestOverride != null && isBatchMode) return TestOverride(kind);
            return null;
        }

        /// <summary>Production: real dialog. TEST_ONLY (batchmode + installed override): enumerated auto-decision.</summary>
        internal static bool Confirm(GdaiConfirmationKind kind, string title, string message, string ok, string cancel)
        {
            var d = TryTestDecision(kind, Application.isBatchMode);
            if (d.HasValue)
            {
                Debug.Log($"[GDAI][ConfirmationPolicy] TEST_ONLY override · kind={kind} · decision={d.Value}");
                return d.Value;
            }
            return UnityEditor.EditorUtility.DisplayDialog(title, message, ok, cancel);
        }
    }
}
