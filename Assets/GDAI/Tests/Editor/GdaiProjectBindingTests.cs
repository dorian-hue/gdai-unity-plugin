using System;
using System.IO;
using GDAI.Bridge.Editor.LayerA;
using NUnit.Framework;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiProjectBindingTests
    {
        private const string Plugin = "0.1.0-alpha.8.5";
        private const string UnityVer = "6000.5.1f1";
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "gdai-binding-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "ProjectSettings"));
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        private static string ValidJson(string mutateField = null, string mutateValue = null)
        {
            var b = new GdaiProjectBindingData
            {
                schema_version = "gdai.unity_export.v1",
                project_id = "18bedbf4-3993-422b-97ce-e5eb910bb55c",
                project_display_name = "Project Slash",
                target_engine = "unity",
                unity_version = UnityVer,
                render_pipeline = "built_in",
                plugin_version = "v0.1.0-alpha.8.5",
                template_revision = "de7b10347494bade13d7eb0162489fadd1aabb61",
                template_tree = "1eb220dfae06f5e8779e3cfb496dec37686d7492",
                template_allowlist_sha256 = new string('0', 64),
                generated_owned_paths = new[] { "Assets/GDAI_Generated" },
                created_by = "unity-project-export",
            };
            var json = UnityEngine.JsonUtility.ToJson(b);
            if (mutateField != null)
            {
                var marker = "\"" + mutateField + "\":\"";
                int i = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
                int j = json.IndexOf('"', i);
                json = json.Substring(0, i) + mutateValue + json.Substring(j);
            }
            return json;
        }

        private void WriteBinding(string json) =>
            File.WriteAllText(Path.Combine(_root, "ProjectSettings", "GDAIProjectBinding.json"), json);

        private bool Load(out GdaiProjectBindingData d, out string err)
            => GdaiProjectBinding.TryLoad(_root, Plugin, UnityVer, out d, out err, out _);

        [Test]
        public void ValidBinding_Loads()
        {
            WriteBinding(ValidJson());
            Assert.IsTrue(Load(out var d, out var err), err);
            Assert.AreEqual("18bedbf4-3993-422b-97ce-e5eb910bb55c", d.project_id);
        }

        [Test]
        public void MissingBinding_FailsClosed_Absent()
        {
            Assert.IsFalse(GdaiProjectBinding.TryLoad(_root, Plugin, UnityVer, out _, out var err, out var exists));
            Assert.IsFalse(exists);
            Assert.AreEqual("BINDING_ABSENT", err);
        }

        [Test]
        public void MalformedJson_FailsClosed()
        {
            WriteBinding("{not json!!");
            Assert.IsFalse(Load(out _, out var err));
            StringAssert.StartsWith("BINDING_", err);
        }

        [TestCase("project_id", "not-a-uuid", "BINDING_BAD_PROJECT_UUID")]
        [TestCase("schema_version", "gdai.unity_export.v999", "BINDING_WRONG_SCHEMA")]
        [TestCase("target_engine", "godot", "BINDING_WRONG_ENGINE")]
        [TestCase("unity_version", "2022.3.0f1", "BINDING_WRONG_UNITY_VERSION")]
        [TestCase("render_pipeline", "urp", "BINDING_WRONG_PIPELINE")]
        [TestCase("plugin_version", "v0.1.0-alpha.8.4", "BINDING_PLUGIN_VERSION_MISMATCH")]
        public void WrongField_FailsClosed(string field, string value, string expectedPrefix)
        {
            WriteBinding(ValidJson(field, value));
            Assert.IsFalse(Load(out _, out var err));
            StringAssert.StartsWith(expectedPrefix, err);
        }

        [Test]
        public void SelfOwningGeneratedPaths_FailClosed()
        {
            var b = UnityEngine.JsonUtility.FromJson<GdaiProjectBindingData>(ValidJson());
            b.generated_owned_paths = new[] { "ProjectSettings" };
            Assert.AreEqual("BINDING_SELF_OWNED: ProjectSettings",
                GdaiProjectBinding.Validate(b, Plugin, UnityVer));
        }

        [Test]
        public void TraversalAndAbsolutePaths_FailClosed()
        {
            var b = UnityEngine.JsonUtility.FromJson<GdaiProjectBindingData>(ValidJson());
            b.generated_owned_paths = new[] { "Assets/../secrets" };
            StringAssert.StartsWith("BINDING_GENERATED_ROOT_TRAVERSAL", GdaiProjectBinding.Validate(b, Plugin, UnityVer));
            b.generated_owned_paths = new[] { "/etc" };
            StringAssert.StartsWith("BINDING_GENERATED_ROOT_ABSOLUTE", GdaiProjectBinding.Validate(b, Plugin, UnityVer));
            b.generated_owned_paths = new[] { "Assets/SomewhereElse" };
            StringAssert.StartsWith("BINDING_GENERATED_ROOT_NOT_APPROVED", GdaiProjectBinding.Validate(b, Plugin, UnityVer));
        }
    }
}
