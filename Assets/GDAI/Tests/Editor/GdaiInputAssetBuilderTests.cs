// AUTO-0Q-P2 · P2-D tests · Deterministic input asset generation.
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerB;
using NUnit.Framework;
using UnityEditor;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiInputAssetBuilderTests
    {
        private GdaiPlayableContract _contract;
        private string _path;

        private static string FixtureJson()
        {
            var g = AssetDatabase.FindAssets("PlayableContract.rev3.projectslash-2d874a40");
            return File.ReadAllText(AssetDatabase.GUIDToAssetPath(g[0]));
        }

        [SetUp]
        public void SetUp()
        {
            _contract = GdaiPlayableContract.Parse(FixtureJson()).Contract;
            Assert.IsNotNull(_contract);
            _path = _contract.input.asset_path;
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadMainAssetAtPath(_path) != null) AssetDatabase.DeleteAsset(_path);
        }

        [Test]
        public void Json_UsesContractActionIdsAndBindings()
        {
            var json = GdaiInputAssetBuilder.BuildJson(_contract);
            foreach (var a in _contract.input.actions)
            {
                StringAssert.Contains("\"id\": \"" + a.id + "\"", json);
                StringAssert.Contains("\"path\": \"" + a.binding + "\"", json);
                StringAssert.Contains("\"name\": \"" + a.name + "\"", json);
            }
            StringAssert.Contains("\"name\": \"Gameplay\"", json);
            // exactly the three mouse/pointer bindings, no extras
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(json, "<Pointer>/position").Count);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(json, "<Mouse>/leftButton").Count);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(json, "<Mouse>/rightButton").Count);
        }

        [Test]
        public void Json_IsDeterministic()
        {
            Assert.AreEqual(GdaiInputAssetBuilder.BuildJson(_contract), GdaiInputAssetBuilder.BuildJson(_contract));
        }

        [Test]
        public void EnsureAsset_CreatesImportableAsset()
        {
            var r = GdaiInputAssetBuilder.EnsureAsset(_contract);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsTrue(r.Created);
            Assert.IsTrue(File.Exists(_path));
            Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(_path), "asset imported");
        }

        [Test]
        public void EnsureAsset_SecondRun_StableGuid_NoRecreate()
        {
            GdaiInputAssetBuilder.EnsureAsset(_contract);
            string guid1 = AssetDatabase.AssetPathToGUID(_path);
            var r2 = GdaiInputAssetBuilder.EnsureAsset(_contract);
            Assert.IsTrue(r2.Ok);
            Assert.IsFalse(r2.Created, "second run reuses");
            Assert.AreEqual(guid1, AssetDatabase.AssetPathToGUID(_path), "input asset GUID stable across sync");
        }
    }
}
