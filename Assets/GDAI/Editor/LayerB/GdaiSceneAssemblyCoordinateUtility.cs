using UnityEngine;

// =====================================================================================
// A3-UNITY-CONSUME-LONGRUN-0B · C1 · Shared canvas→world coordinate converter (Editor, Layer B).
//
// ONE conversion source for validator / spawn markers / arena bounds / edge blockers, so no
// feature re-derives the math and Unity objects can't drift apart (SC-flagged system risk).
//
// Contract (demo-first, matches A3-UNITY-CONSUME-RECON-0A):
//   canvas: origin top-left, y-DOWN, px (same as web SceneCanvas / sceneAssembly).
//   world:  origin center, y-UP, units. Arena centered at world origin.
//   PPU_WORLD = 100 (matches sprite import PPU).
//     worldX = (canvasX - arenaWidth  / 2) / 100
//     worldY = -(canvasY - arenaHeight / 2) / 100   ← y flipped
//     worldZ = 0
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblyCoordinateUtility
    {
        public const float PpuWorld = 100f;

        /// <summary>Canvas point (top-left origin, y-down) → world point (center origin, y-up), z=0.</summary>
        public static Vector3 CanvasToWorld(float canvasX, float canvasY, float arenaWidth, float arenaHeight)
        {
            float worldX = (canvasX - arenaWidth * 0.5f) / PpuWorld;
            float worldY = -(canvasY - arenaHeight * 0.5f) / PpuWorld;
            return new Vector3(worldX, worldY, 0f);
        }

        /// <summary>
        /// Canvas rect (top-left corner x,y + size w,h) → world CENTER point. Y-flip affects
        /// position only, not size (see CanvasSizeToWorld).
        /// </summary>
        public static Vector3 CanvasRectCenterToWorld(float x, float y, float w, float h, float arenaWidth, float arenaHeight)
        {
            return CanvasToWorld(x + w * 0.5f, y + h * 0.5f, arenaWidth, arenaHeight);
        }

        /// <summary>Canvas size (w,h px) → world size (units). Pure scale by 1/PPU; no flip.</summary>
        public static Vector2 CanvasSizeToWorld(float w, float h)
        {
            return new Vector2(w / PpuWorld, h / PpuWorld);
        }
    }
}
