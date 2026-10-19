// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · §5.4b · AudioListener discipline (Editor, Layer C).
//
// A playable scene needs EXACTLY ONE active AudioListener (STOP_AUDIO_LISTENER_DUPLICATE_OR_MISSING).
// Policy — never destructive to the user:
//   * 0 active  → add one, ONLY on the GDAI-owned Main Camera (never on a human object);
//                 if the owned camera already carries a disabled one, enable it instead of adding.
//   * 1 active  → preserve it exactly; never add a second.
//   * >1 active → FAIL CLOSED and report; never auto-delete a user's AudioListener.
//   * inactive listeners (disabled component or inactive GameObject) never count toward the
//     active total and are never removed.
// "Active" == UnityEngine.AudioListener.isActiveAndEnabled (enabled component on an active object),
// which is exactly what the audio system uses.
// =====================================================================================
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC
{
    public static class GdaiAudioListenerEnsurer
    {
        public enum Outcome { AddedToOwnedCamera, EnabledOnOwnedCamera, PreservedExistingOne, FailedTooMany, FailedNoOwnedCamera }

        public class Result
        {
            public bool Ok;
            public Outcome Outcome;
            public int ActiveCount;      // AFTER the operation
            public string Message;
        }

        private const string CameraObjectName = "Main Camera";

        /// <summary>Bring the scene to exactly one active AudioListener per the non-destructive policy.</summary>
        public static Result Ensure()
        {
            var all = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int active = all.Count(l => l != null && l.isActiveAndEnabled);

            if (active > 1)
                return new Result { Ok = false, Outcome = Outcome.FailedTooMany, ActiveCount = active,
                    Message = active + " active AudioListeners — fail closed; a user listener is never auto-deleted (STOP_AUDIO_LISTENER_DUPLICATE_OR_MISSING)" };

            if (active == 1)
                return new Result { Ok = true, Outcome = Outcome.PreservedExistingOne, ActiveCount = 1,
                    Message = "exactly one active AudioListener already present — preserved untouched" };

            // 0 active: add/enable ONLY on the GDAI-owned Main Camera.
            var cam = GdaiSceneObjectComposer.FindOwned(CameraObjectName);
            if (cam == null)
                return new Result { Ok = false, Outcome = Outcome.FailedNoOwnedCamera, ActiveCount = 0,
                    Message = "no GDAI-owned '" + CameraObjectName + "' to host an AudioListener (compose first); a human Main Camera is never adopted" };

            var existing = cam.GetComponent<AudioListener>();
            if (existing == null)
            {
                Undo.AddComponent<AudioListener>(cam);
                MarkDirty(cam);
                return new Result { Ok = true, Outcome = Outcome.AddedToOwnedCamera, ActiveCount = 1,
                    Message = "added AudioListener to the owned " + CameraObjectName };
            }

            // present but disabled on the owned camera → enable in place (still our object).
            Undo.RecordObject(existing, "GDAI enable AudioListener");
            existing.enabled = true;
            MarkDirty(cam);
            return new Result { Ok = true, Outcome = Outcome.EnabledOnOwnedCamera, ActiveCount = 1,
                Message = "enabled the existing AudioListener on the owned " + CameraObjectName };
        }

        /// <summary>Read-only active count for the receipt's independent readback.</summary>
        public static int ActiveCount()
        {
            return Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(l => l != null && l.isActiveAndEnabled);
        }

        private static void MarkDirty(GameObject go) => EditorSceneManager.MarkSceneDirty(go.scene);
    }
}
