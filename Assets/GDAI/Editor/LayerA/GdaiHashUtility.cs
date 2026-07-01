using System;
using System.Security.Cryptography;
using System.Text;

// =====================================================================================
// GDAI Unity Plugin · SHA-256 helper for production bundle-proxy file integrity checks.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    internal static class GdaiHashUtility
    {
        /// <summary>Lowercase hex SHA-256 of the UTF-8 bytes of <paramref name="content"/>.</summary>
        public static string ComputeSha256Hex(string content)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool Matches(string content, string expectedHex)
        {
            if (string.IsNullOrEmpty(expectedHex)) return false;
            return string.Equals(ComputeSha256Hex(content), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
