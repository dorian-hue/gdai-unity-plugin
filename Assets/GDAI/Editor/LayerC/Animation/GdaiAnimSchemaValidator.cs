// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · minimal JSON Schema (draft-07 subset) validator.
//
// Validates an INCOMING package JObject against the byte-identical canonical schema file
// (Contracts/gdai.animation.materialization_package.v1.schema.json) using the same construct
// set as the Flowcraft validate.ts — so both repos enforce the ONE schema authority. The
// schema file SHA is pinned; drift is a deterministic test failure.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    public static class GdaiAnimSchemaValidator
    {
        public static List<string> Validate(JToken value, JObject schema, string path = "$")
        {
            var errs = new List<string>();
            var t = schema["type"];
            if (t != null)
            {
                var types = t.Type == JTokenType.Array ? ((JArray)t).Select(x => (string)x).ToList() : new List<string> { (string)t };
                if (!types.Any(ty => TypeOk(ty, value))) { errs.Add(path + ": expected type " + t + ", got " + value.Type); return errs; }
            }
            if (schema["const"] != null && !JToken.DeepEquals(value, schema["const"])) errs.Add(path + ": const mismatch");
            if (schema["enum"] is JArray en && !en.Any(e => JToken.DeepEquals(e, value))) errs.Add(path + ": not in enum");
            if (value.Type == JTokenType.String)
            {
                string s = (string)value;
                if (schema["pattern"] != null && !Regex.IsMatch(s, (string)schema["pattern"])) errs.Add(path + ": pattern failed");
                if (schema["minLength"] != null && s.Length < (int)schema["minLength"]) errs.Add(path + ": minLength");
            }
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            {
                double d = (double)value;
                if (schema["minimum"] != null && d < (double)schema["minimum"]) errs.Add(path + ": minimum");
                if (schema["exclusiveMinimum"] != null && d <= (double)schema["exclusiveMinimum"]) errs.Add(path + ": exclusiveMinimum");
            }
            if (value.Type == JTokenType.Array)
            {
                var arr = (JArray)value;
                if (schema["minItems"] != null && arr.Count < (int)schema["minItems"]) errs.Add(path + ": minItems");
                if (schema["items"] is JObject items) for (int i = 0; i < arr.Count; i++) errs.AddRange(Validate(arr[i], items, path + "[" + i + "]"));
            }
            if (value.Type == JTokenType.Object)
            {
                var obj = (JObject)value;
                if (schema["required"] is JArray req) foreach (var r in req) if (obj[(string)r] == null) errs.Add(path + ": missing required '" + (string)r + "'");
                var props = schema["properties"] as JObject ?? new JObject();
                if (schema["additionalProperties"] != null && schema["additionalProperties"].Type == JTokenType.Boolean && !(bool)schema["additionalProperties"])
                    foreach (var p in obj.Properties()) if (props[p.Name] == null) errs.Add(path + ": additional property '" + p.Name + "' not allowed");
                foreach (var p in props.Properties()) if (obj[p.Name] != null) errs.AddRange(Validate(obj[p.Name], (JObject)p.Value, path + "." + p.Name));
            }
            return errs;
        }

        private static bool TypeOk(string ty, JToken v)
        {
            switch (ty)
            {
                case "object": return v.Type == JTokenType.Object;
                case "array": return v.Type == JTokenType.Array;
                case "string": return v.Type == JTokenType.String;
                case "number": return v.Type == JTokenType.Integer || v.Type == JTokenType.Float;
                case "integer": return v.Type == JTokenType.Integer;
                case "boolean": return v.Type == JTokenType.Boolean;
                case "null": return v.Type == JTokenType.Null;
                default: return false;
            }
        }

        /// <summary>Locate the mirrored canonical schema file inside the installed package.</summary>
        public static string SchemaPath()
        {
            var pi = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/ai.gamedevs.plugin/package.json");
            string rel = "Assets/GDAI/Editor/LayerC/Animation/Contracts/gdai.animation.materialization_package.v1.schema.json";
            return pi != null ? Path.Combine(pi.resolvedPath, rel)
                : Path.GetFullPath(Path.Combine(Directory.GetParent(UnityEngine.Application.dataPath).FullName, rel));
        }

        public static JObject LoadSchema() => JObject.Parse(File.ReadAllText(SchemaPath()));
        public static string SchemaFileSha256() => GdaiAnimJson.Sha256Hex(File.ReadAllBytes(SchemaPath()));
    }
}
