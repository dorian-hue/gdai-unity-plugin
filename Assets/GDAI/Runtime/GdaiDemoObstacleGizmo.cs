using UnityEngine;

namespace GDAI.Bridge
{
    /// <summary>
    /// A3-UNITY-CONSUME-C5 · Editor-only fallback visual for a demo obstacle when no imported
    /// sprite resolves for its entity. Draws a small diamond + square so the workbench-placed
    /// object is still visible in the Scene view. No physics. OnDrawGizmos never runs in builds.
    /// Set by GdaiSceneAssemblyDemoObstacle. This is Unity-local demo-draft, NOT Flowcraft SSOT.
    /// </summary>
    public class GdaiDemoObstacleGizmo : MonoBehaviour
    {
        [Tooltip("Edge length in world units of the fallback marker.")]
        public float worldSize = 0.8f;

        public Color color = new Color(0.8f, 0.55f, 1f, 0.9f);

        private void OnDrawGizmos()
        {
            if (worldSize <= 0f) return;
            Gizmos.color = color;
            Vector3 p = transform.position;
            float h = worldSize * 0.5f;
            Vector3 up = p + Vector3.up * h, dn = p - Vector3.up * h, lf = p - Vector3.right * h, rt = p + Vector3.right * h;
            Gizmos.DrawLine(up, rt); Gizmos.DrawLine(rt, dn); Gizmos.DrawLine(dn, lf); Gizmos.DrawLine(lf, up);
            Gizmos.DrawWireCube(p, new Vector3(worldSize, worldSize, 0f));
        }
    }
}
