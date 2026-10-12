using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerA
{
    [Serializable] public sealed class GdaiSourceBoundPlayablePlan
    {
        public const string Profile = "unity.source_bound_demo.v1";
        public int revision;
        public string context_sha256;
        public List<Source> sources;
        public List<Host> objects;
        public List<string> unresolved;
        public List<TypeDisposition> type_dispositions;
        [Serializable] public sealed class TypeDisposition { public string type,source_path,disposition,rationale; }
        [Serializable] public sealed class FieldObligation { public string path,type; public bool required_reference,sensitive; }
        [Serializable] public sealed class TypeObligation { public string type,source_path,kind,hosting; public bool @abstract,serializable; public List<string> bases,enum_members; public List<FieldObligation> fields; }
        [Serializable] public sealed class Source { public string path; public string sha256; }
        [Serializable] public sealed class Host
        {
            public string id, name, kind, asset_path, role, entity_id, position;
            public List<ComponentSpec> components;
        }
        [Serializable] public sealed class ComponentSpec { public string id, type, source_path; public bool native_defaults; public List<Field> fields; }
        [Serializable] public sealed class Field
        {
            public string path, disposition, rationale, target_id, target_component, reference_kind;
            private JToken _value;
            private bool _valueSpecified;
            [JsonProperty]
            public JToken value {get=>_value;set{_value=value;_valueSpecified=true;}}
            [JsonIgnore] public bool ValueSpecified => _valueSpecified;
            // Opaque sensitive fields never emit a payload. Other fields retain an explicitly
            // declared JSON null while an actually omitted value remains absent on round-trip.
            public bool ShouldSerializevalue() => value!=null||_valueSpecified&&disposition!="preserve_external";
        }
        private static readonly HashSet<string> SensitiveWords=new HashSet<string>(new[]{"secret","password","encryption","token","credential","key","iv"},StringComparer.Ordinal);
        public static bool SensitiveField(string name) {
            var separated=System.Text.RegularExpressions.Regex.Replace(name??"","([a-z0-9])([A-Z])","$1_$2");
            separated=System.Text.RegularExpressions.Regex.Replace(separated,"([A-Z]+)([A-Z][a-z])","$1_$2");
            return System.Text.RegularExpressions.Regex.Split(separated,"[^A-Za-z0-9]+").Where(word=>word.Length>0).Select(word=>word.ToLowerInvariant()).Any(SensitiveWords.Contains);
        }
        public static void RequireSafeFields(IEnumerable<Field> fields,int revision=1) {
            foreach(var field in fields??Enumerable.Empty<Field>()) {
                if(SensitiveField(field.path)) {
                    if(revision!=2||field.disposition!="preserve_external"||string.IsNullOrWhiteSpace(field.rationale)||field.ValueSpecified||field.target_id!=null||field.target_component!=null||field.reference_kind!=null)throw new InvalidOperationException("sensitive configuration requires payload-free opaque preservation");
                    continue;
                }
                if(field.disposition=="preserve_external")throw new InvalidOperationException("opaque preservation is sensitive-only");
                RequireSafeValue(field.value);
            }
        }
        private static void RequireSafeValue(JToken value) {
            if(value is JArray a){foreach(var item in a)RequireSafeValue(item);}
            else if(value is JObject o){foreach(var property in o.Properties()){if(SensitiveField(property.Name))throw new InvalidOperationException("sensitive nested configuration unsupported");RequireSafeValue(property.Value);}}
        }
        private static bool Id(string s) => s != null && System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z_][A-Za-z0-9_.-]*$");
        public static bool OwnedPath(string s, string ext) => s != null && s.StartsWith(GdaiPlayableContract.OwnedPrefix, StringComparison.Ordinal) && s.EndsWith(ext, StringComparison.Ordinal) && !s.Contains("..") && !s.Contains("\\") && System.Text.RegularExpressions.Regex.IsMatch(s,"^[A-Za-z0-9_./-]+$");
        public static string Hash(byte[] bytes) { using(var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static JToken Sorted(JToken token)
        {
            if(token is JObject ob) return new JObject(ob.Properties().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>new JProperty(p.Name,Sorted(p.Value))));
            if(token is JArray ar) return new JArray(ar.Select(Sorted));
            return token.DeepClone();
        }
        public static string ContextHash(JObject context) => Hash(Encoding.UTF8.GetBytes(Sorted(context).ToString(Formatting.None)));
        public static List<string> Validate(GdaiPlayableContract contract)
        {
            var e = new List<string>(); void Req(bool ok,string message) {if(!ok)e.Add(message);}
            var p = contract.source_bound_plan; var context=contract.source_bound_context;
            Req(contract.schema_version==GdaiPlayableContract.Schema && contract.contract_revision==4 && contract.profile_id==Profile,"unsupported source-bound envelope");
            Req(contract.canonical_scene?.path=="Assets/Scenes/Main.unity" && contract.canonical_scene.add_to_build_settings,"canonical scene invalid");
            if(p==null||p.revision!=1&&p.revision!=2||context==null) {e.Add("plan/context missing or unknown revision");return e;}
            try {foreach(var component in p.objects?.SelectMany(o=>o.components??new List<ComponentSpec>())??Enumerable.Empty<ComponentSpec>())RequireSafeFields(component.fields,p.revision);}catch {e.Add("sensitive configuration policy invalid");return e;}
            Req(!string.IsNullOrEmpty(contract.source_bound_context_json) && JToken.DeepEquals(JObject.Parse(contract.source_bound_context_json),context) && Hash(Encoding.UTF8.GetBytes(contract.source_bound_context_json))==p.context_sha256,"immutable context transport/hash mismatch");
            Req(p.unresolved!=null&&p.unresolved.Count==0,"unresolved configuration");
            var designs=context["accepted_designs"] as JArray;
            Req(designs!=null&&designs.Count>0&&designs.All(d=>d["design"] is JObject && System.Text.RegularExpressions.Regex.IsMatch((string)d["accepted_design_identity"]??"","^[a-f0-9]{64}$")),"accepted design identities missing");
            var arena=context["arena"];
            Req(arena!=null&&(string)arena["coordinate_space"]=="canvas_top_left_y_down_px"&&FinitePositive((float?)arena["width"])&&FinitePositive((float?)arena["height"]),"persisted arena/coordinate semantics missing");
            var roles=context["roles"] as JArray; var placements=context["placements"] as JArray; var assets=context["assets"] as JArray;
            Req(roles!=null&&roles.Any(r=>(string)r["role"]=="player")&&roles.Any(r=>(string)r["role"]=="enemy"),"gated roles missing");
            Req(placements!=null&&placements.Count>0&&assets!=null&&assets.Count>0,"placements/adopted assets missing");
            var paths=new HashSet<string>();
            Req(p.sources!=null&&p.sources.Count>0,"source identities missing");
            foreach(var s in p.sources??new List<Source>()) Req(s.path!=null&&System.Text.RegularExpressions.Regex.IsMatch(s.path,"^Assets/GDAI_Generated/[A-Za-z0-9_/-]+\\.cs$")&&!s.path.Contains("..")&&paths.Add(s.path)&&System.Text.RegularExpressions.Regex.IsMatch(s.sha256??"","^[a-f0-9]{64}$"),"source path/hash invalid");
            Req(p.objects!=null&&p.objects.Count>0&&p.objects.Count<=128,"object plan missing");
            var ids=new HashSet<string>(); var comps=new HashSet<string>(); var assetPaths=new HashSet<string>(); var names=new HashSet<string>();
            foreach(var o in p.objects??new List<Host>())
            {
                Req(Id(o.id)&&ids.Add(o.id)&&Id(o.name)&&names.Add(o.name),"object id/name duplicate or invalid");
                Req(new[]{"scene","prefab","config"}.Contains(o.kind),"unsupported host kind");
                if(o.kind!="scene") Req(OwnedPath(o.asset_path,o.kind=="prefab"?".prefab":".asset")&&assetPaths.Add(o.asset_path),"owned path invalid/duplicate");
                Req(o.position=="neutral"||o.position=="placement","position source unknown");
                if(!string.IsNullOrEmpty(o.role)&&o.role!="system") {
                    Req(roles!=null&&roles.Any(r=>(string)r["role"]==o.role&&(string)r["entity_id"]==o.entity_id),"gated role entity missing");
                    Req(assets!=null&&assets.Any(a=>(string)a["entity_id"]==o.entity_id),"semantic adopted asset missing");
                    Req(o.position=="placement","semantic position must be persisted");
                }
                if(o.position=="placement")Req(placements!=null&&placements.Count(a=>(string)a["entity_id"]==o.entity_id)==1,"placement identity missing/ambiguous");
                Req(o.components!=null&&o.components.Count>0&&o.components.Count<=32,"component intent required");
                if(o.kind=="config")Req(o.components?.Count==1,"config requires one ScriptableObject");
                foreach(var c in o.components??new List<ComponentSpec>()) {
                    Req(Id(c.id)&&comps.Add(c.id)&&System.Text.RegularExpressions.Regex.IsMatch(c.type??"","^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$")&&(c.source_path=="builtin" ? c.native_defaults && Builtin(c.type)!=null && o.kind!="config" : paths.Contains(c.source_path??"")),"component identity/source invalid");
                    Req(c.fields!=null,"complete field dispositions required");var fields=new HashSet<string>();
                    foreach(var f in c.fields??new List<Field>()) {
                        Req(System.Text.RegularExpressions.Regex.IsMatch(f.path??"","^[A-Za-z_][A-Za-z0-9_]*$")&&fields.Add(f.path),"field path duplicate/invalid");
                        var sensitive=SensitiveField(f.path);
                        if(sensitive)Req(p.revision==2&&f.disposition=="preserve_external"&&!string.IsNullOrWhiteSpace(f.rationale)&&!f.ValueSpecified&&f.target_id==null&&f.target_component==null&&f.reference_kind==null,"sensitive configuration requires payload-free opaque preservation");
                        else Req(new[]{"source_default","optional_null","value","reference"}.Contains(f.disposition),"unresolved field disposition");
                        Req(sensitive||f.disposition!="preserve_external","opaque preservation is sensitive-only");
                        if(f.disposition=="source_default"||f.disposition=="optional_null")Req(!string.IsNullOrWhiteSpace(f.rationale),"field rationale missing");
                        if(f.disposition=="source_default"||f.disposition=="value")Req(f.value!=null&&BoundedValue(f.value),"explicit typed value/default missing");
                        if(f.disposition=="reference")Req(new[]{"object","component","transform","prefab","asset","config"}.Contains(f.reference_kind)&&Id(f.target_id),"reference intent missing");
                    }
                }
            }
            void Reference(Host o,string kind,string id,string component)
            {
                Req(new[]{"object","component","transform","prefab","asset","config"}.Contains(kind),"unknown reference kind");
                if(kind=="asset")Req(assets!=null&&assets.Count(a=>(string)a["asset_id"]==id)==1,"adopted asset reference missing/ambiguous");
                else {
                    var target=(p.objects??new List<Host>()).Where(t=>t.id==id).ToList();Req(target.Count==1,"owned reference missing/ambiguous");
                    if(target.Count!=1)return;
                    if(kind=="component")Req(target[0].components!=null&&target[0].components.Any(t=>t.id==component),"component target missing");
                    if(kind=="prefab"||kind=="config")Req(target[0].kind==kind,"target kind mismatch");
                    if(o.kind!="scene")Req(target[0].kind!="scene","persistent assets cannot bind scene objects");
                }
            }
            void Nested(Host host,JToken value)
            {
                if(value is JArray ar) {foreach(var item in ar)Nested(host,item);}
                else if(value is JObject ob) {
                    if(ob["reference_kind"]!=null)Reference(host,(string)ob["reference_kind"],(string)ob["target_id"],(string)ob["target_component"]);
                    else foreach(var property in ob.Properties())Nested(host,property.Value);
                }
            }
            foreach(var o in p.objects??new List<Host>())foreach(var comp in o.components??new List<ComponentSpec>())foreach(var f in comp.fields??new List<Field>()) {
                if(f.disposition=="reference")Reference(o,f.reference_kind,f.target_id,f.target_component);
                if(f.disposition=="value"||f.disposition=="source_default")Nested(o,f.value);
            }
            Req((p.objects??new List<Host>()).Any(o=>o.role=="player")&&(p.objects??new List<Host>()).Any(o=>o.role=="enemy"&&o.kind=="prefab"),"player/enemy prefab intent missing");
            e.AddRange(Reconcile(contract));
            return e;
        }
        private static bool FinitePositive(float? value) => value.HasValue&&value.Value>0&&!float.IsNaN(value.Value)&&!float.IsInfinity(value.Value);
        public static GdaiPlayableContract.CameraSpec Framing(GdaiPlayableContract c)
        {
            var errors=Validate(c);if(errors.Count>0)throw new InvalidOperationException(string.Join(";",errors));
            var arena=c.source_bound_context["arena"];var width=(float)arena["width"];var height=(float)arena["height"];
            return new GdaiPlayableContract.CameraSpec {position=new GdaiPlayableContract.Vec3 {x=0,y=0,z=-10},target_aspect=width/height,padding_ratio=0,world_bounds=new GdaiPlayableContract.WorldBounds {width=width/100f,height=height/100f}};
        }
        public static List<string> Reconcile(GdaiPlayableContract c)
        {
            var e=new List<string>();var p=c.source_bound_plan;var obligations=c.source_bound_obligations;
            if(obligations==null||obligations.Count==0||p.type_dispositions==null||p.type_dispositions.Count!=obligations.Count)return new List<string>{"complete independent source/type obligations required"};
            var inventoryPaths=new HashSet<string>((p.sources??new List<Source>()).Select(s=>s.path),StringComparer.Ordinal);
            var obligationPaths=new HashSet<string>(obligations.Select(t=>t.source_path),StringComparer.Ordinal);
            foreach(var path in obligationPaths)if(!inventoryPaths.Contains(path))e.Add("obligation source identity missing: "+path);
            foreach(var t in obligations) {
                if(p.revision==2&&t.kind=="enum"&&(t.enum_members==null||t.enum_members.Count!=t.enum_members.Distinct(StringComparer.Ordinal).Count()||t.enum_members.Any(member=>!System.Text.RegularExpressions.Regex.IsMatch(member??"","^[A-Za-z_]\\w*$"))))e.Add("exact enum member inventory required: "+t.type);
                foreach(var field in t.fields??new List<FieldObligation>())if(p.revision==1&&IsStringShape(field.type))e.Add("string baseline configuration unsupported: "+t.type+"."+field.path);
                var disposition=p.type_dispositions.Where(d=>d.type==t.type&&d.source_path==t.source_path).ToList();
                string expected=t.@abstract?"abstract":t.hosting!="none"?"hosted":t.kind=="interface"?"interface":t.kind=="enum"?"enum":t.kind=="struct"||p.revision==2&&t.serializable&&(t.fields?.Count??0)>0?"data":"controller_constructed";
                if(disposition.Count!=1||disposition[0].disposition!=expected||expected!="hosted"&&string.IsNullOrWhiteSpace(disposition[0].rationale))e.Add("actual type disposition missing/mismatch: "+t.type);
                var hosts=p.objects.SelectMany(o=>o.components.Where(comp=>comp.type==t.type&&comp.source_path==t.source_path).Select(comp=>new {o,comp})).ToList();
                if(expected=="hosted"&&hosts.Count==0||expected!="hosted"&&hosts.Count!=0)e.Add("actual generated hosting omitted/mismatch: "+t.type);
                foreach(var h in hosts) {
                    if((t.hosting=="config")!=(h.o.kind=="config"))e.Add("actual hosting kind mismatch: "+t.type);
                    if(t.fields==null||h.comp.fields.Count!=t.fields.Count||t.fields.Any(f=>!h.comp.fields.Any(pf=>pf.path==f.path)))e.Add("source field coverage omitted/extra: "+t.type);
                    foreach(var field in t.fields??new List<FieldObligation>()) {
                        var f=h.comp.fields.SingleOrDefault(pf=>pf.path==field.path);
                        if(field.sensitive) {if(p.revision!=2||f?.disposition!="preserve_external")e.Add("sensitive serialized field requires opaque preservation: "+t.type+"."+field.path);continue;}
                        if(f!=null)e.AddRange(ReconcileSerialized(field.type,t,f.disposition=="reference"?JToken.FromObject(f):f.value,obligations,f.disposition,t.type+"."+field.path,p.revision,new HashSet<string>()));
                        if(p.revision==1&&field.type=="string"&&f?.value?.Type==JTokenType.String&&(string)f.value=="")e.Add("empty configuration unsupported: "+t.type+"."+field.path);
                    }
                }
            }
            foreach(var comp in p.objects.SelectMany(o=>o.components))if(comp.source_path!="builtin"&&!obligations.Any(t=>t.type==comp.type&&t.source_path==comp.source_path))e.Add("undeclared source component: "+comp.type);
            foreach(var d in c.source_bound_context["accepted_designs"])if(!obligations.Any(t=>t.source_path==(string)d["source_path"]))e.Add("accepted module source missing: "+(string)d["module_id"]);
            var controllers=obligations.Where(t=>t.source_path=="Assets/GDAI_Generated/GameIntegrationController.cs"&&t.hosting=="component"&&!t.@abstract).ToList();
            if(controllers.Count!=1||!p.objects.Any(o=>o.kind=="scene"&&o.components.Any(comp=>comp.type==controllers[0].type)))e.Add("real integration controller scene host required");
            foreach(var role in c.source_bound_context["roles"])if(!p.objects.Any(o=>o.role==(string)role["role"]&&o.entity_id==(string)role["entity_id"]&&(o.role!="enemy"||o.kind=="prefab")))e.Add("gated role host omitted");
            var references=((JArray)JToken.FromObject(p.objects)).DescendantsAndSelf().OfType<JObject>().Where(o=>o["reference_kind"]!=null).Select(o=>(string)o["target_id"]).ToList();
            foreach(var host in p.objects.Where(o=>o.kind!="scene"))if(!p.objects.Where(o=>o.id!=host.id).Any(o=>((JArray)JToken.FromObject(o.components)).DescendantsAndSelf().OfType<JObject>().Any(r=>(string)r["reference_kind"]==host.kind&&(string)r["target_id"]==host.id)))e.Add("owned prefab/config is not consumed: "+host.id);
            foreach(var asset in c.source_bound_context["assets"])if(!references.Contains((string)asset["asset_id"]))e.Add("adopted asset binding omitted: "+(string)asset["asset_id"]);
            return e;
        }
        private static bool IsStringShape(string type) { var inner=Element(type);return inner!=null?IsStringShape(inner):type=="string"||type=="System.String"; }
        private static string Element(string type) {
            type=type.Replace(" ","");if(type.EndsWith("[]",StringComparison.Ordinal))return type.Substring(0,type.Length-2);
            var m=System.Text.RegularExpressions.Regex.Match(type,@"^(?:(?:System\.Collections\.Generic\.)?List)<(.+)>$");return m.Success?m.Groups[1].Value:null;
        }
        private static TypeObligation FieldType(string type,TypeObligation owner,List<TypeObligation> all) => all.SingleOrDefault(t=>t.type==type)??all.SingleOrDefault(t=>t.type==owner.type.Substring(0,Math.Max(0,owner.type.LastIndexOf('.')+1))+type);
        private static bool ObjectType(string type,TypeObligation target) => System.Text.RegularExpressions.Regex.IsMatch(type,@"^(?:(?:UnityEngine(?:\.Playables)?|UnityEngine\.InputSystem)\.)?(?:Object|GameObject|Transform|Sprite|SpriteRenderer|Material|Texture2D|AudioClip|AudioSource|ParticleSystem|ScriptableObject|Collider2D|CircleCollider2D|Camera|PlayableDirector|InputActionAsset|InputActionReference)$")||target!=null&&target.hosting!="none";
        private static bool Required(string type,TypeObligation owner,List<TypeObligation> all,HashSet<string> seen) {
            var inner=Element(type);if(inner!=null)return Required(inner,owner,all,seen);var t=FieldType(type,owner,all);if(ObjectType(type,t))return true;
            if(t==null||seen.Contains(t.type))return false;var next=new HashSet<string>(seen){t.type};return t.fields.Any(f=>Required(f.type,t,all,next));
        }
        private static bool Numeric(JToken value,out double number) {
            number=0;if(value==null||value.Type!=JTokenType.Integer&&value.Type!=JTokenType.Float)return false;
            try {number=(double)value;return !double.IsNaN(number)&&!double.IsInfinity(number);}catch{return false;}
        }
        private static bool SingleValue(JToken value) {
            if(!Numeric(value,out var number))return false;var magnitude=Math.Abs(number);
            return magnitude<=(double)float.MaxValue&&(number==0||magnitude>=(double)float.Epsilon)&&!float.IsNaN((float)number)&&!float.IsInfinity((float)number)&&(number==0||(float)number!=0);
        }
        private static List<string> ReconcileDeclaredLeaf(string type,JToken value,TypeObligation target,string path) {
            var e=new List<string>();var normalized=(type??"").Replace("global::","").Replace(" ","");
            if(new[]{"float","System.Single","Single"}.Contains(normalized)&&!SingleValue(value))e.Add("declared Single value wrong shape/range: "+path);
            else if(new[]{"double","System.Double","Double"}.Contains(normalized)&&!Numeric(value,out _))e.Add("declared Double value wrong shape/range: "+path);
            else {
                var vector=System.Text.RegularExpressions.Regex.Match(normalized,@"^(?:UnityEngine\.)?Vector([234])$");
                if(vector.Success) {var names=new[]{"x","y","z","w"}.Take(int.Parse(vector.Groups[1].Value)).ToArray();var o=value as JObject;if(o==null||o.Count!=names.Length||!new HashSet<string>(names).SetEquals(o.Properties().Select(property=>property.Name))||names.Any(name=>!SingleValue(o[name])))e.Add("declared vector value wrong shape/range: "+path);}
                else if(System.Text.RegularExpressions.Regex.IsMatch(normalized,@"^(?:UnityEngine\.)?LayerMask$")) {var layers=value as JArray;if(layers==null||layers.Count>32||layers.Any(layer=>layer.Type!=JTokenType.String||((string)layer).Length==0||((string)layer).Length>64)||layers.Select(layer=>(string)layer).Distinct(StringComparer.Ordinal).Count()!=layers.Count)e.Add("declared LayerMask named-layer list wrong shape: "+path);}
                else if(System.Text.RegularExpressions.Regex.IsMatch(normalized,@"^(?:UnityEngine\.)?AnimationCurve$")) {
                    var curve=value as JObject;var keys=curve?["keys"] as JArray;var keyNames=new[]{"time","value","in_tangent","out_tangent","in_weight","out_weight","weighted_mode"};var numberNames=keyNames.Take(6).ToArray();
                    bool valid=curve!=null&&new HashSet<string>(new[]{"keys","pre_wrap_mode","post_wrap_mode"}).SetEquals(curve.Properties().Select(property=>property.Name))&&keys!=null&&keys.Count>0&&keys.Count<=256&&keys.All(item=>item is JObject key&&new HashSet<string>(keyNames).SetEquals(key.Properties().Select(property=>property.Name))&&numberNames.All(name=>SingleValue(key[name]))&&new[]{"None","In","Out","Both"}.Contains((string)key["weighted_mode"]))&&new[]{"Default","Once","Loop","PingPong","ClampForever","Clamp"}.Contains((string)curve["pre_wrap_mode"])&&new[]{"Default","Once","Loop","PingPong","ClampForever","Clamp"}.Contains((string)curve["post_wrap_mode"]);
                    if(!valid)e.Add("declared AnimationCurve value wrong shape: "+path);
                } else if(target?.kind=="enum"&&(value?.Type!=JTokenType.String||target.enum_members==null||!target.enum_members.Contains((string)value,StringComparer.Ordinal)))e.Add("declared enum member unknown: "+path);
            }
            return e;
        }
        private static List<string> ReconcileSerialized(string type,TypeObligation owner,JToken value,List<TypeObligation> all,string disposition,string path,int revision,HashSet<string> seen) {
            var e=new List<string>();bool required=Required(type,owner,all,new HashSet<string>());
            if(required&&disposition!="reference"&&disposition!="value")return new List<string>{"required nested reference cannot be default/optional: "+path};
            var inner=Element(type);if(inner!=null) {
                if(!(value is JArray ar)||required&&ar.Count==0)return new List<string>{"required serialized collection omitted/wrong shape: "+path};
                for(int i=0;i<ar.Count;i++)e.AddRange(ReconcileSerialized(inner,owner,ar[i],all,"value",path+"["+i+"]",revision,seen));return e;
            }
            var t=FieldType(type,owner,all);if(ObjectType(type,t))return HasReference(value)?e:new List<string>{"required reference/config omitted: "+path};
            if(revision==2)e.AddRange(ReconcileDeclaredLeaf(type,value,t,path));
            if(t==null||t.kind=="enum")return e;if(seen.Contains(t.type))return new List<string>{"recursive serialized configuration unsupported: "+path};
            if(!(value is JObject data))return new List<string>{"nested configuration omitted/wrong shape: "+path};
            if(data.Count!=t.fields.Count||t.fields.Any(f=>data[f.path]==null))e.Add("complete nested configuration required: "+path);
            var next=new HashSet<string>(seen){t.type};foreach(var f in t.fields){if(SensitiveField(f.path)){e.Add("sensitive nested configuration unsupported");continue;}e.AddRange(ReconcileSerialized(f.type,t,data[f.path],all,disposition,path+"."+f.path,revision,next));}return e;
        }
        private static bool HasReference(JToken v) => v is JArray a ? a.Count>0&&a.All(HasReference) : v is JObject o&&o["reference_kind"]!=null&&o["target_id"]!=null;
        private static bool BoundedValue(JToken t,int depth=0)
        {
            if(depth>8)return false;
            if(t is JArray a)return a.Count<=256&&a.All(x=>BoundedValue(x,depth+1));
            if(t is JObject o)return o.Count<=64&&o.Properties().All(p=>!SensitiveField(p.Name)&&System.Text.RegularExpressions.Regex.IsMatch(p.Name,"^[A-Za-z_][A-Za-z0-9_]*$")&&BoundedValue(p.Value,depth+1));
            return t.Type==JTokenType.Null||t.Type==JTokenType.Boolean||t.Type==JTokenType.Integer||t.Type==JTokenType.Float||t.Type==JTokenType.String&&((string)t).Length<=4096&&!System.Text.RegularExpressions.Regex.IsMatch((string)t,"-----BEGIN|service_role|\\beyJ\\w+\\.\\w+\\.\\w+");
        }
        public static Type Builtin(string name)
        {
            var types=new[]{typeof(Camera),typeof(SpriteRenderer),typeof(Rigidbody2D),typeof(BoxCollider2D),typeof(CircleCollider2D),typeof(CapsuleCollider2D),typeof(PolygonCollider2D),typeof(AudioListener),typeof(AudioSource),typeof(ParticleSystem),typeof(UnityEngine.Playables.PlayableDirector)};
            return types.SingleOrDefault(t=>t.FullName==name);
        }
        public enum Readiness { Ready, Transient, Terminal }
        /// A settled identity/type mismatch is terminal. Only real import/compilation waits defer.
        public static Readiness PendingReadiness(bool compiling,bool updating) => compiling||updating?Readiness.Transient:Readiness.Ready;
        public static Readiness SettledArtifactReadiness(bool imported,bool assemblyCurrent) => imported&&assemblyCurrent?Readiness.Ready:Readiness.Terminal;
        public static bool CompiledDeclarationMatches(Type type,string kind,bool abstractClass) {
            if(type==null||type.IsGenericType||type.IsClass&&type.IsAbstract&&type.IsSealed)return false;
            var actual=type.IsInterface?"interface":type.IsEnum?"enum":type.IsValueType?"struct":type.IsClass&&!typeof(Delegate).IsAssignableFrom(type)?"class":"unsupported";
            return actual==kind&&(kind=="class"?type.IsAbstract==abstractClass:!abstractClass);
        }
        public static bool CompiledEnumMembersMatch(Type type,IEnumerable<string> declared) {
            if(type==null||!type.IsEnum||declared==null)return false;var members=declared.ToList();
            return members.Count==members.Distinct(StringComparer.Ordinal).Count()&&new HashSet<string>(members,StringComparer.Ordinal).SetEquals(Enum.GetNames(type));
        }
        public static Readiness AssessReadiness(GdaiPlayableContract c,out string error)
        {
            error=null;
            if(PendingReadiness(EditorApplication.isCompiling,EditorApplication.isUpdating)==Readiness.Transient){error="compiling/importing";return Readiness.Transient;}
            try {
                var errors=Validate(c);if(errors.Count>0){error=string.Join(";",errors);return Readiness.Terminal;}
                var root=Directory.GetParent(Application.dataPath).FullName;
                foreach(var s in c.source_bound_plan.sources) {
                    if(!File.Exists(Path.Combine(root,s.path))||Hash(File.ReadAllBytes(Path.Combine(root,s.path)))!=s.sha256){error="source identity mismatch: "+s.path;return Readiness.Terminal;}
                    if(SettledArtifactReadiness(AssetDatabase.LoadAssetAtPath<MonoScript>(s.path)!=null,true)==Readiness.Terminal){error="settled source import missing: "+s.path;return Readiness.Terminal;}
                }
                foreach(var obligation in c.source_bound_obligations) {
                    var t=CompiledType(obligation.type);
                    if(t==null){error="settled compiled FQTN unavailable: "+obligation.type;return Readiness.Terminal;}
                    bool hosted=typeof(MonoBehaviour).IsAssignableFrom(t)||typeof(ScriptableObject).IsAssignableFrom(t);
                    if(!CompiledDeclarationMatches(t,obligation.kind,obligation.@abstract) || hosted!=(obligation.hosting!="none")){error="settled compiled type obligation mismatch: "+obligation.type;return Readiness.Terminal;}
                    if(c.source_bound_plan.revision==2&&obligation.kind=="enum"&&!CompiledEnumMembersMatch(t,obligation.enum_members)){error="settled compiled enum member inventory mismatch: "+obligation.type;return Readiness.Terminal;}
                    if(hosted) {
                        var script=AssetDatabase.LoadAssetAtPath<MonoScript>(obligation.source_path);
                        if(script==null||script.GetClass()!=t){error="source script does not persist hosted type: "+obligation.type+" at "+obligation.source_path;return Readiness.Terminal;}
                    }
                    var location=t.Assembly.Location;
                    if(SettledArtifactReadiness(true,!string.IsNullOrEmpty(location)&&File.Exists(location)&&File.GetLastWriteTimeUtc(location)>=File.GetLastWriteTimeUtc(Path.Combine(root,obligation.source_path)))==Readiness.Terminal){error="settled stale compiled source identity: "+obligation.type;return Readiness.Terminal;}
                }
                return Readiness.Ready;
            }catch(Exception ex){error="compiled identity check: "+ex.Message;return Readiness.Terminal;}
        }
        // Player assembly reflection resolves secondary generated types as well as primary MonoScript classes.
        public static Type CompiledType(string fqtn)
        {
            var player=UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player);
            var types=AppDomain.CurrentDomain.GetAssemblies().Where(a=>player.Any(p=>p.name==a.GetName().Name)).SelectMany(a=>{var exact=a.GetType(fqtn,false);if(exact!=null)return new[]{exact};try{return a.GetTypes().Where(t=>(t.FullName??"").Replace('+','.')==fqtn);}catch{return Enumerable.Empty<Type>();}}).Distinct().ToList();
            return types.Count==1?types[0]:null;
        }
        public static bool Ready(GdaiPlayableContract c,out string error) => AssessReadiness(c,out error)==Readiness.Ready;
    }
}
