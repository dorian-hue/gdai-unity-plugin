// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · P2-D2 · Camera fit_arena configurer (Editor, Layer C).
//
// Applies the contract's camera framing to the composer-created Main Camera. The
// producer contract declares the FACTS + policy (projection=orthographic,
// framing=fit_arena, arena-derived world bounds, profile target_aspect + padding);
// the plugin SOLVES the orthographic size that fits the arena on both axes so the
// 2D scene is visible with no horizontal clipping. No hardcoded size / project
// values here — every input comes from the contract.
// =====================================================================================
using GDAI.Bridge.Editor.LayerA;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC
{
    public static class GdaiCameraConfigurer
    {
        public class Result { public bool Ok; public string Error; public float OrthographicSize; }

        /// <summary>Configure the Main Camera GameObject from the contract's camera block.</summary>
        public static Result Apply(GameObject cameraObject, GdaiPlayableContract.CameraSpec spec)
        {
            var r = new Result();
            if (cameraObject == null) { r.Error = "camera object missing"; return r; }
            if (spec == null) { r.Error = "camera spec missing"; return r; }

            var cam = cameraObject.GetComponent<Camera>();
            if (cam == null) cam = Undo.AddComponent<Camera>(cameraObject);

            Undo.RecordObject(cam, "GDAI configure camera");
            cam.orthographic = spec.projection == "orthographic";
            cam.orthographicSize = spec.SolveOrthographicSize();
            cam.clearFlags = spec.clear_flags == "SolidColor" ? CameraClearFlags.SolidColor : cam.clearFlags;
            if (spec.background != null)
                cam.backgroundColor = new Color(spec.background.r, spec.background.g, spec.background.b, spec.background.a);

            Undo.RecordObject(cameraObject.transform, "GDAI camera transform");
            if (spec.position != null)
                cameraObject.transform.position = new Vector3(spec.position.x, spec.position.y, spec.position.z);

            if (!string.IsNullOrEmpty(spec.tag) && TagExists(spec.tag))
                cameraObject.tag = spec.tag;
            else if (!string.IsNullOrEmpty(spec.tag))
            {
                r.Error = "required camera tag '" + spec.tag + "' not defined";
                return r;
            }

            r.OrthographicSize = cam.orthographicSize;
            r.Ok = cam.orthographic && cam.orthographicSize > 0f
                && (string.IsNullOrEmpty(spec.tag) || cameraObject.CompareTag(spec.tag));
            if (!r.Ok) r.Error = "camera did not reach the required orthographic/tag state";
            return r;
        }

        private static bool TagExists(string tag)
        {
            try { return System.Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, tag) >= 0; }
            catch { return tag == "MainCamera" || tag == "Player" || tag == "Untagged"; }
        }
    }
}
