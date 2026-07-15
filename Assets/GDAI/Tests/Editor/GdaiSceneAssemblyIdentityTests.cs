// T4 0J Gate B · Req1 · owned_scene_assembly UNIQUE STABLE SEMANTIC identity. SceneObjectSourceId is the
// single identity primitive used by the ownership manifest Write/Verify and the receipt's duplicate check;
// it derives identity from the sceneAssembly SSOT (kind + entity_id), never the Unity object name. These
// unit tests pin its uniqueness/stability/collision properties without needing the generated host types.
using System.Linq;
using GDAI.Bridge.Editor.LayerC;
using NUnit.Framework;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiSceneAssemblyIdentityTests
    {
        private static string Id(string kind, string entity) =>
            GdaiPlayableOwnershipManifest.SceneObjectSourceId(kind, entity);

        [Test]
        public void SourceId_IsKindPipeEntity()
        {
            Assert.AreEqual("blocker|arena_left", Id("blocker", "arena_left"));
            Assert.AreEqual("scene_element|e1", Id("scene_element", "e1"));
            Assert.AreEqual("player_spawn|abc-123", Id("player_spawn", "abc-123"));
        }

        [Test]
        public void SourceId_EmptyEntity_IsUniqueByKind()
        {
            // root and arena_bounds carry no entity — kind alone must keep them distinct.
            Assert.AreEqual("root|", Id("root", null));
            Assert.AreEqual("arena_bounds|", Id("arena_bounds", ""));
            Assert.AreNotEqual(Id("root", null), Id("arena_bounds", null));
        }

        [Test]
        public void SourceId_FourArenaBoundaries_AreDistinct()
        {
            var ids = new[] { "arena_left", "arena_right", "arena_top", "arena_bottom" }
                .Select(b => Id("blocker", b)).ToList();
            Assert.AreEqual(4, ids.Distinct().Count(), "all four arena boundaries have distinct identities");
        }

        [Test]
        public void SourceId_DistinctAcrossKinds_ForSameEntity()
        {
            // an entity that appears both as a spawn and a placement must not alias.
            Assert.AreNotEqual(Id("player_spawn", "e9"), Id("scene_element", "e9"));
        }

        [Test]
        public void SourceId_SameKindEntity_CollidesForDuplicateDetection()
        {
            // two objects sharing (kind, entity) share an identity → the receipt flags them as a duplicate.
            Assert.AreEqual(Id("scene_element", "e1"), Id("scene_element", "e1"));
        }

        [Test]
        public void SourceId_Stable_NotDerivedFromName()
        {
            // identity is a pure function of (kind, entity) — no name input, so a name-derivation change
            // cannot alter it. Same inputs → byte-identical id across calls.
            Assert.AreEqual(Id("scene_element", "obstacle-42"), Id("scene_element", "obstacle-42"));
        }

        [Test]
        public void SourceId_NullKind_Handled()
        {
            Assert.AreEqual("|", Id(null, null));
        }
    }
}
