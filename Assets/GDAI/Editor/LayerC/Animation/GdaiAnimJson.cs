// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · RFC 8785 (JCS) canonical JSON + SHA-256.
//
// Must produce BYTE-IDENTICAL canonical output to the Flowcraft normalizer's jcs.ts for
// every value that can appear in gdai.animation.materialization_package.v1 (ASCII strings,
// 64-bit integers, simple decimals, bool, null, arrays, objects). The cross-language proof
// is the exact package_content_sha256 goldens shared with the Deno tests.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    public static class GdaiAnimJson
    {
        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        public static string Sha256HexUtf8(string s) => Sha256Hex(Encoding.UTF8.GetBytes(s));

        /// <summary>
        /// Parse JSON WITHOUT Newtonsoft's date auto-conversion: ISO timestamp strings must stay
        /// JTokenType.String or the RFC 8785 canonical bytes (and the package hash) change.
        /// ALWAYS use this — never JObject.Parse — for contract JSON.
        /// </summary>
        public static JObject ParseObject(string json)
        {
            using (var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(json))
            { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                return JObject.Load(reader);
        }

        /// <summary>RFC 8785 canonicalization of a parsed JSON tree (JToken).</summary>
        public static string Canonicalize(JToken token)
        {
            var sb = new StringBuilder();
            WriteCanonical(token, sb);
            return sb.ToString();
        }

        public static string JcsSha256Hex(JToken token) => Sha256HexUtf8(Canonicalize(token));

        /// <summary>0E-05 §1: SHA256(JCS(package with package_content_sha256 blanked)).</summary>
        public static string PackageContentSha256(JObject package)
        {
            var clone = (JObject)package.DeepClone();
            clone["package_content_sha256"] = "";
            return JcsSha256Hex(clone);
        }

        private static void WriteCanonical(JToken t, StringBuilder sb)
        {
            switch (t.Type)
            {
                case JTokenType.Object:
                    sb.Append('{');
                    bool firstP = true;
                    // RFC 8785: keys sorted by UTF-16 code units — String.CompareOrdinal.
                    foreach (var prop in ((JObject)t).Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        if (prop.Value.Type == JTokenType.Undefined) continue;
                        if (!firstP) sb.Append(',');
                        firstP = false;
                        WriteString(prop.Name, sb);
                        sb.Append(':');
                        WriteCanonical(prop.Value, sb);
                    }
                    sb.Append('}');
                    break;
                case JTokenType.Array:
                    sb.Append('[');
                    bool firstI = true;
                    foreach (var item in (JArray)t)
                    {
                        if (!firstI) sb.Append(',');
                        firstI = false;
                        WriteCanonical(item, sb);
                    }
                    sb.Append(']');
                    break;
                case JTokenType.Integer:
                    sb.Append(((long)t).ToString(CultureInfo.InvariantCulture));
                    break;
                case JTokenType.Float:
                    WriteNumber((double)t, sb);
                    break;
                case JTokenType.String:
                    WriteString((string)t, sb);
                    break;
                case JTokenType.Boolean:
                    sb.Append((bool)t ? "true" : "false");
                    break;
                case JTokenType.Null:
                    sb.Append("null");
                    break;
                default:
                    throw new InvalidOperationException("JCS_UNSUPPORTED_TYPE:" + t.Type);
            }
        }

        private static void WriteNumber(double d, StringBuilder sb)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new InvalidOperationException("JCS_NON_FINITE_NUMBER");
            if (d == 0.0) { sb.Append('0'); return; }                       // (-0 canonicalizes to 0 per ES)
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            {
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture)); // ES prints integral doubles without ".0"
                return;
            }
            // ES shortest round-trip form for the non-integral decimals this contract uses (e.g. 0.5).
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(string s, StringBuilder sb)
        {
            // Matches ECMAScript JSON.stringify escaping (RFC 8785 §3.2.2.2): ", \, control chars;
            // \b \t \n \f \r shorthand; other C0 as \u00xx lowercase; everything else raw UTF-8.
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
