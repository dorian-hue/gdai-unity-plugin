using UnityEngine;

namespace GDAI.Bridge
{
    /// <summary>
    /// A3-UNITY-CONSUME-LONGRUN-0B · C3 · Editor-only debug visual for the arena rectangle.
    /// Draws a wire rectangle (world units) centered on its transform. No physics, no collider.
    /// OnDrawGizmos is an editor callback — never runs in player builds. Set by
    /// GdaiSceneAssemblyArenaBounds.
    /// </summary>
    public class GdaiArenaBoundsGizmo : MonoBehaviour
    {
        [Tooltip("Arena width in world units (arena.width / PPU).")]
        public float worldWidth;

        [Tooltip("Arena height in world units (arena.height / PPU).")]
        public float worldHeight;

        public Color color = new Color(1f, 0.85f, 0.2f, 0.9f);

        private void OnDrawGizmos()
        {
            if (worldWidth <= 0f || worldHeight <= 0f) return;
            Gizmos.color = color;
            Gizmos.DrawWireCube(transform.position, new Vector3(worldWidth, worldHeight, 0f));
        }
    }
}
