// AUTO-0Q-P2 · P2-A tests · Contract consumer parses the REAL rev4 fixture
// (verbatim Contract Gate emission for snapshot 2d874a40) and fails closed on
// every mutilated variant. No hand-rewritten test contract.
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using NUnit.Framework;
using UnityEditor;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiPlayableContractTests
    {
        private static string FixturePath()
        {
            var guids = AssetDatabase.FindAssets("PlayableContract.rev4.projectslash-2d874a40");
            Assert.IsNotEmpty(guids, "rev4 fixture asset must be present");
            return AssetDatabase.GUIDToAssetPath(guids[0]);
        }

        private static string FixtureJson() => File.ReadAllText(FixturePath());

        [Test]
        public void RealRev3Fixture_ParsesAndValidates()
        {
            var r = GdaiPlayableContract.Parse(FixtureJson());
            Assert.IsTrue(r.Ok, r.Summary);
            var c = r.Contract;
            Assert.AreEqual(GdaiPlayableContract.Schema, c.schema_version);
            Assert.AreEqual(GdaiPlayableContract.Profile, c.profile_id);
            Assert.AreEqual("Assets/Scenes/Main.unity", c.canonical_scene.path);
            // pinned deterministic action IDs from the Contract Gate
            Assert.AreEqual("6d202212-57a1-5009-a2c8-df62fc84cf7d", c.input.actions.First(a => a.name == "PointerPosition").id);
            Assert.AreEqual("5c7eabed-23af-5537-b8d1-761382b6c8ca", c.input.actions.First(a => a.name == "LeftClick").id);
            Assert.AreEqual("10a8be8d-97ca-57a1-8dfb-4ffe5f52419a", c.input.actions.First(a => a.name == "RightClick").id);
            // five canonical scene objects
            var names = c.scene_objects.Select(o => o.name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "EnemyManager", "GameIntegrationController", "InputManager", "Main Camera", "Player" }, names);
            // player co-location + enemy collider + value binding
            CollectionAssert.AreEquivalent(new[] { "SpriteRenderer", "Rigidbody2D" }, c.player.required_components);
            CollectionAssert.AreEquivalent(new[] { "CharacterStateMachine", "CombatLocatorSystem" }, c.player.host_components);
            Assert.Contains("BoxCollider2D", c.enemy_prefab.required_runtime_components);
            Assert.AreEqual(7, c.scene_bindings.Count);
            Assert.AreEqual("Default", c.value_bindings.First(v => v.field == "enemyLayer").layers[0]);
        }

        private static void AssertFails(string json, string why)
        {
            var r = GdaiPlayableContract.Parse(json);
            Assert.IsFalse(r.Ok, "expected fail-closed: " + why + " — got OK");
        }

        [Test] public void FailClosed_EmptyOrGarbage()
        {
            AssertFails("", "empty");
            AssertFails("not json", "garbage");
            AssertFails("{}", "empty object");
        }

        [Test] public void FailClosed_WrongSchemaOrProfile()
        {
            AssertFails(FixtureJson().Replace("gdai.unity.playable_contract.v1", "gdai.unity.playable_contract.v2"), "schema drift");
            AssertFails(FixtureJson().Replace("unity.pointer_action_demo.v1", "unity.other_profile.v1"), "profile drift");
        }

        [Test] public void FailClosed_MissingSceneObject()
        {
            // remove GameIntegrationController from scene_objects only (keep bindings) → undeclared host
            var json = FixtureJson().Replace("{\n      \"name\": \"GameIntegrationController\",\n      \"components\": [\n        \"GameIntegrationController\"\n      ]\n    },", "");
            AssertFails(json, "missing GIC scene object");
        }

        [Test] public void FailClosed_PathTraversal()
        {
            AssertFails(FixtureJson().Replace(
                "Assets/GDAI_Project/Generated/Input/GDAI_DefaultControls.inputactions",
                "Assets/GDAI_Project/Generated/../../Evil/GDAI_DefaultControls.inputactions"), "input traversal");
            AssertFails(FixtureJson().Replace(
                "Assets/GDAI_Project/Generated/Prefabs/GDAI_DefaultEnemy.prefab",
                "Assets/Other/GDAI_DefaultEnemy.prefab"), "prefab outside owned root");
        }

        [Test] public void FailClosed_InputMapMutations()
        {
            AssertFails(FixtureJson().Replace("\"field\": \"_rightClickRef\"", "\"field\": \"_pointerPositionRef\""), "duplicated field");
            AssertFails(FixtureJson().Replace("Gameplay/LeftClick\"\n      }", "Gameplay/RightClick\"\n      }"), "mis-mapped action");
        }

        [Test] public void FailClosed_EnemyColliderRemoved()
        {
            AssertFails(FixtureJson().Replace("\"BoxCollider2D\",\n", ""), "collider removed");
        }

        [Test] public void FailClosed_ValueBindingRemoved()
        {
            AssertFails(FixtureJson().Replace("\"enemyLayer\"", "\"otherField\""), "enemyLayer binding gone");
        }

        [Test] public void FailClosed_NilOrSharedEntity()
        {
            AssertFails(FixtureJson().Replace("8f7f846c-84f4-4c78-985c-9b129b3ccdf6", "00000000-0000-0000-0000-000000000000"), "nil player entity");
            AssertFails(FixtureJson().Replace("1ce221b0-fb97-4b8b-b7db-cf4ba21888a9", "8f7f846c-84f4-4c78-985c-9b129b3ccdf6"), "player==enemy entity");
        }
    }
}
