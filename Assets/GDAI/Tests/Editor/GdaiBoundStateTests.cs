using System.Collections.Generic;
using GDAI.Bridge.Editor.LayerA;
using NUnit.Framework;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiBoundStateTests
    {
        private const string Bound = "18bedbf4-3993-422b-97ce-e5eb910bb55c";

        [Test]
        public void BoundProjectPresentInCatalog_ResolvesExactIndex()
        {
            var catalog = new List<string> { "aaaa1111-0000-0000-0000-000000000000", Bound, "bbbb2222-0000-0000-0000-000000000000" };
            Assert.IsTrue(GdaiProjectBinding.TryResolveCatalogIndex(Bound, catalog, out int idx));
            Assert.AreEqual(1, idx, "must resolve the EXACT bound project, not any other");
        }

        [Test]
        public void BoundProjectAbsent_NoFallbackToFirstProject()
        {
            var catalog = new List<string> { "aaaa1111-0000-0000-0000-000000000000", "bbbb2222-0000-0000-0000-000000000000" };
            Assert.IsFalse(GdaiProjectBinding.TryResolveCatalogIndex(Bound, catalog, out int idx),
                "absent bound project must fail closed (PAIRED_BINDING_UNAUTHORIZED)");
            Assert.AreEqual(-1, idx, "index must NOT default to project[0]");
        }

        [Test]
        public void EmptyCatalog_FailsClosed()
        {
            Assert.IsFalse(GdaiProjectBinding.TryResolveCatalogIndex(Bound, new List<string>(), out _));
        }

        [Test]
        public void NullBoundId_FailsClosed()
        {
            Assert.IsFalse(GdaiProjectBinding.TryResolveCatalogIndex(null, new List<string> { Bound }, out _));
        }
    }
}
