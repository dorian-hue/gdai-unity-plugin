// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · TEST_ONLY golden-fixture verifier (Option B).
//
// Operator ruling (0F-FIXTURE-CONTRACT-ERRATUM): the CHECKED-IN BINARY golden PNG is the
// artifact authority; DECODED RGBA comparison is the semantic authority. No PNG/deflate
// encoder exists here and no compressed-byte equivalence is claimed — the original zlib
// framing is provenance only. Proves: file exists → exact byte size → exact SHA-256 →
// valid PNG decode → RGBA → exact dimensions → EVERY pixel matches the canonical pixel
// function → grid/cell geometry agrees with the normalized package. Fail-closed FIXTURE_*.
// =====================================================================================
using System;
using System.IO;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    public static class GdaiAnimationFixture
    {
        /// <summary>Canonical pixel function (top-left origin). Gutter → transparent; else per-cell color.</summary>
        public static Color32 CanonicalPixel(int x, int yTop, int cellW, int cellH)
        {
            if (x % cellW == 0 || yTop % cellH == 0) return new Color32(0, 0, 0, 0);
            int c = x / cellW, r = yTop / cellH;
            return new Color32((byte)((40 + c * 30) & 255), (byte)((40 + r * 20) & 255), 120, 255);
        }

        /// <summary>
        /// Verify a checked-in golden sheet against its authoritative coordinates + the package grid.
        /// Returns true or sets a single FIXTURE_* code (first violation, fail-closed).
        /// </summary>
        public static bool Verify(string absolutePngPath, long expectedBytes, string expectedSha256,
            int expectedWidth, int expectedHeight, GdaiAnimationPackage package, out string error)
        {
            error = null;
            if (!File.Exists(absolutePngPath)) { error = "FIXTURE_FILE_HASH_DRIFT:absent:" + absolutePngPath; return false; }
            byte[] bytes = File.ReadAllBytes(absolutePngPath);
            if (bytes.LongLength != expectedBytes) { error = "FIXTURE_FILE_SIZE_DRIFT:" + bytes.LongLength + "!=" + expectedBytes; return false; }
            string sha = GdaiAnimJson.Sha256Hex(bytes);
            if (!string.Equals(sha, expectedSha256, StringComparison.Ordinal)) { error = "FIXTURE_FILE_HASH_DRIFT:" + sha; return false; }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(bytes, false)) { error = "FIXTURE_PNG_DECODE_FAILED:LoadImage"; return false; }
                if (tex.width != expectedWidth || tex.height != expectedHeight)
                { error = "FIXTURE_DIMENSION_MISMATCH:" + tex.width + "x" + tex.height + "!=" + expectedWidth + "x" + expectedHeight; return false; }

                // grid/cell geometry must agree with the normalized package (0F erratum requirement)
                if (package != null)
                {
                    if (package.columns * package.cell_width != expectedWidth || package.rows * package.cell_height != expectedHeight)
                    { error = "FIXTURE_DIMENSION_MISMATCH:package-grid:" + package.columns + "x" + package.rows + "@" + package.cell_width; return false; }
                }
                int cw = package?.cell_width ?? 0, ch = package?.cell_height ?? 0;
                if (cw <= 0 || ch <= 0) { error = "FIXTURE_DIMENSION_MISMATCH:package-cell-size"; return false; }

                // EVERY decoded pixel vs the canonical function. Texture rows are bottom-up; PNG top-down.
                var px = tex.GetPixels32();
                int W = tex.width, H = tex.height;
                for (int yTop = 0; yTop < H; yTop++)
                {
                    int texRow = H - 1 - yTop;
                    for (int x = 0; x < W; x++)
                    {
                        Color32 got = px[texRow * W + x];
                        Color32 want = CanonicalPixel(x, yTop, cw, ch);
                        if (got.r != want.r || got.g != want.g || got.b != want.b || got.a != want.a)
                        {
                            error = "FIXTURE_PIXEL_SEMANTICS_DRIFT:(" + x + "," + yTop + "):got(" + got.r + "," + got.g + "," + got.b + "," + got.a +
                                    ")want(" + want.r + "," + want.g + "," + want.b + "," + want.a + ")";
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception e) { error = "FIXTURE_PNG_DECODE_FAILED:" + e.Message; return false; }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }
    }
}
