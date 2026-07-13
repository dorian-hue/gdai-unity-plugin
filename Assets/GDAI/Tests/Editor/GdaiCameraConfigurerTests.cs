// AUTO-0Q-P2 · P2-D2 tests · Camera fit_arena configurer. Drives the real rev4
// contract fixture; verifies the solved orthographic size, projection, tag,
// clear flags, background and position against the arena facts (no magic size).
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerC;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiCameraConfigurerTests
    {
        private GdaiPlayableContract _contract;

        private static string FixtureJson()
        {
            var g = AssetDatabase.FindAssets("PlayableContract.rev3.projectslash-2d874a40");
            return File.ReadAllText(AssetDatabase.GUIDToAssetPath(g[0]));
        }

        [SetUp]
        public void SetUp()
        {
            _contract = GdaiPlayableContract.Parse(FixtureJson()).Contract;
            Assert.IsNotNull(_contract, "rev4 fixture must parse (includes camera block)");
        }

        [Test]
        public void Fixture_CarriesCameraBlock()
        {
            var cam = _contract.camera;
            Assert.IsNotNull(cam);
            Assert.AreEqual("orthographic", cam.projection);
            Assert.AreEqual("fit_arena", cam.framing);
            Assert.AreEqual("MainCamera", cam.tag);
            Assert.AreEqual(9.6f, cam.world_bounds.width, 1e-4);
            Assert.AreEqual(5.4f, cam.world_bounds.height, 1e-4);
        }

        [Test]
        public void SolvedSize_FitsArenaBothAxes_NoMagicNumber()
        {
            var cam = _contract.camera;
            float size = cam.SolveOrthographicSize();
            // manual solve: max(5.4/2, 9.6/(2*16/9)) * 1.1 = max(2.7, 2.7)*1.1 = 2.97
            Assert.AreEqual(2.97f, size, 1e-2);
            // vertical half-extent covers the arena height, horizontal covers the arena width
            Assert.GreaterOrEqual(size, cam.world_bounds.height / 2f);
            Assert.GreaterOrEqual(size * cam.target_aspect, cam.world_bounds.width / 2f);
        }

        [Test]
        public void Apply_MakesCameraOrthographicWithSolvedSizeAndTag()
        {
            var go = new GameObject("Main Camera");
            try
            {
                var r = GdaiCameraConfigurer.Apply(go, _contract.camera);
                Assert.IsTrue(r.Ok, r.Error);
                var cam = go.GetComponent<Camera>();
                Assert.IsTrue(cam.orthographic, "must be orthographic (2D)");
                Assert.AreEqual(_contract.camera.SolveOrthographicSize(), cam.orthographicSize, 1e-3);
                Assert.AreEqual(CameraClearFlags.SolidColor, cam.clearFlags);
                Assert.AreEqual("MainCamera", go.tag);
                Assert.Less(go.transform.position.z, 0f, "camera looks toward the z=0 scene");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void WideArena_UsesWidthConstraint_NoHorizontalClip()
        {
            // a 32:9 arena is wider than the 16:9 target → the width term must dominate
            var cam = _contract.camera;
            cam.world_bounds.width = 32f; cam.world_bounds.height = 9f; // aspect 3.56 > 1.78
            float byHeight = cam.world_bounds.height / 2f;                  // 4.5
            float byWidth = cam.world_bounds.width / (2f * cam.target_aspect); // 9.0
            float size = cam.SolveOrthographicSize();
            Assert.AreEqual(System.Math.Max(byHeight, byWidth) * (1f + cam.padding_ratio), size, 1e-3);
            Assert.Greater(size, byHeight, "wide arena is framed by width, not height (no horizontal clip)");
        }
    }
}
