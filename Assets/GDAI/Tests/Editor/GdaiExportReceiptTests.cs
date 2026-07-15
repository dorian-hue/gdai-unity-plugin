using System;
using System.IO;
using GDAI.Bridge.Editor.LayerA;
using NUnit.Framework;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiExportReceiptTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "gdai-receipt-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        private static GdaiExportReceiptData Valid() => new GdaiExportReceiptData
        {
            project_id = "18bedbf4-3993-422b-97ce-e5eb910bb55c",
            snapshot_id = "abcdef1234567890",
            snapshot_revision = "coherent",
            plugin_version = "0.1.0-alpha.8.5",
            generated_file_count = 12,
            binary_asset_count = 3,
            preserved_meta_count = 7,
            backup_path = ".gdai/generated_backups/x",
            scene_path = "Assets/Scenes/SampleScene.unity",
            scene_elements_count = 4,
            collider_count = 5,
            started_at = "2026-07-12T10:00:00Z",
            completed_at = "2026-07-12T10:01:00Z",
            result = "PASS",
        };

        [Test]
        public void Write_CreatesReceiptUnderGdaiReceipts_WithExpectedName()
        {
            var utc = new DateTime(2026, 7, 12, 10, 1, 2, DateTimeKind.Utc);
            string path = GdaiExportReceipt.Write(_root, Valid(), utc);
            StringAssert.Contains(Path.Combine(".gdai", "receipts"), path);
            Assert.AreEqual("20260712T100102Z-abcdef12-export-completion.json", Path.GetFileName(path));
            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void Write_RoundTripsAllFields()
        {
            string path = GdaiExportReceipt.Write(_root, Valid(), DateTime.UtcNow);
            var round = UnityEngine.JsonUtility.FromJson<GdaiExportReceiptData>(File.ReadAllText(path));
            Assert.AreEqual("gdai.export_completion.v1", round.schema_version);
            Assert.AreEqual("18bedbf4-3993-422b-97ce-e5eb910bb55c", round.project_id);
            Assert.AreEqual(5, round.collider_count);
            Assert.AreEqual("PASS", round.result);
        }

        [Test]
        public void Write_RefusesNonPassResult_FailureWritesNoSuccessReceipt()
        {
            var r = Valid(); r.result = "FAIL";
            Assert.Throws<InvalidOperationException>(() => GdaiExportReceipt.Write(_root, r, DateTime.UtcNow));
        }

        [Test]
        public void ReceiptModel_HasNoSecretBearingFields()
        {
            foreach (var f in typeof(GdaiExportReceiptData).GetFields())
                foreach (var banned in new[] { "token", "authorization", "secret", "service_role", "jwt" })
                    StringAssert.DoesNotContain(banned, f.Name.ToLowerInvariant());
        }
    }
}
