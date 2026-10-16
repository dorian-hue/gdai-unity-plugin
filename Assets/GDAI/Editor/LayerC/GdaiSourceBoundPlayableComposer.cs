using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerB;
using GDAI.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GDAI.Bridge.Editor.LayerC
{
    /// Compiler-backed composition; no C# parser, type-name fallback, user-object adoption or business code.
    public static class GdaiSourceBoundPlayableComposer
    {
        private static string Root => Directory.GetParent(Application.dataPath).FullName;
        private static readonly HashSet<string> NativeFields = new HashSet<string> {
            "m_ObjectHideFlags","m_CorrespondingSourceObject","m_PrefabInstance","m_PrefabAsset","m_GameObject",
            "m_Enabled","m_EditorHideFlags","m_Script","m_Name","m_EditorClassIdentifier"
        };
        private const string NeutralEmptySceneAsset=@"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_OcclusionBakeSettings:
    smallestOccluder: 5
    smallestHole: 0.25
    backfaceThreshold: 100
  m_SceneGUID: 00000000000000000000000000000000
  m_OcclusionCullingData: {fileID: 0}
--- !u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 10
  m_Fog: 0
  m_FogColor: {r: 0.5, g: 0.5, b: 0.5, a: 1}
  m_FogMode: 3
  m_FogDensity: 0.01
  m_LinearFogStart: 0
  m_LinearFogEnd: 300
  m_AmbientSkyColor: {r: 0.212, g: 0.227, b: 0.259, a: 1}
  m_AmbientEquatorColor: {r: 0.114, g: 0.125, b: 0.133, a: 1}
  m_AmbientGroundColor: {r: 0.047, g: 0.043, b: 0.035, a: 1}
  m_AmbientIntensity: 1
  m_AmbientMode: 0
  m_SubtractiveShadowColor: {r: 0.42, g: 0.478, b: 0.627, a: 1}
  m_SkyboxMaterial: {fileID: 10304, guid: 0000000000000000f000000000000000, type: 0}
  m_HaloStrength: 0.5
  m_FlareStrength: 1
  m_FlareFadeSpeed: 3
  m_HaloTexture: {fileID: 0}
  m_SpotCookie: {fileID: 10001, guid: 0000000000000000e000000000000000, type: 0}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_ReflectionBounces: 1
  m_ReflectionIntensity: 1
  m_CustomReflection: {fileID: 0}
  m_Sun: {fileID: 0}
  m_UseRadianceAmbientProbe: 0
--- !u!157 &3
LightmapSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 13
  m_BakeOnSceneLoad: 0
  m_GISettings:
    serializedVersion: 2
    m_BounceScale: 1
    m_IndirectOutputScale: 1
    m_AlbedoBoost: 1
    m_EnvironmentLightingMode: 0
    m_EnableBakedLightmaps: 1
    m_EnableRealtimeLightmaps: 0
  m_LightmapEditorSettings:
    serializedVersion: 12
    m_Resolution: 2
    m_BakeResolution: 40
    m_AtlasSize: 1024
    m_AO: 0
    m_AOMaxDistance: 1
    m_CompAOExponent: 1
    m_CompAOExponentDirect: 0
    m_ExtractAmbientOcclusion: 0
    m_Padding: 2
    m_LightmapParameters: {fileID: 0}
    m_LightmapsBakeMode: 1
    m_TextureCompression: 1
    m_ReflectionCompression: 2
    m_MixedBakeMode: 2
    m_BakeBackend: 2
    m_PVRSampling: 1
    m_PVRDirectSampleCount: 32
    m_PVRSampleCount: 512
    m_PVRBounces: 2
    m_PVREnvironmentSampleCount: 256
    m_PVREnvironmentReferencePointCount: 2048
    m_PVRFilteringMode: 1
    m_PVRDenoiserTypeDirect: 1
    m_PVRDenoiserTypeIndirect: 1
    m_PVRDenoiserTypeAO: 1
    m_PVRFilterTypeDirect: 0
    m_PVRFilterTypeIndirect: 0
    m_PVRFilterTypeAO: 0
    m_PVREnvironmentMIS: 1
    m_PVRCulling: 1
    m_PVRFilteringGaussRadiusDirect: 1
    m_PVRFilteringGaussRadiusIndirect: 1
    m_PVRFilteringGaussRadiusAO: 1
    m_PVRFilteringAtrousPositionSigmaDirect: 0.5
    m_PVRFilteringAtrousPositionSigmaIndirect: 2
    m_PVRFilteringAtrousPositionSigmaAO: 1
    m_ExportTrainingData: 0
    m_TrainingDataDestination: TrainingData
    m_LightProbeSampleCountMultiplier: 4
  m_LightingDataAsset: {fileID: 20201, guid: 0000000000000000f000000000000000, type: 0}
  m_LightingSettings: {fileID: 0}
--- !u!196 &4
NavMeshSettings:
  serializedVersion: 2
  m_ObjectHideFlags: 0
  m_BuildSettings:
    serializedVersion: 3
    agentTypeID: 0
    agentRadius: 0.5
    agentHeight: 2
    agentSlope: 45
    agentClimb: 0.4
    ledgeDropHeight: 0
    maxJumpAcrossDistance: 0
    minRegionArea: 2
    manualCellSize: 0
    cellSize: 0.16666667
    manualTileSize: 0
    tileSize: 256
    buildHeightMesh: 0
    maxJobWorkers: 0
    preserveTilesOutsideBounds: 0
    debug:
      m_Flags: 0
  m_NavMeshData: {fileID: 0}
--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots: []
";
        private sealed class MarkerRollbackState { public bool present; public string profile,snapshot,role; }
        private sealed class HostRollbackState { public bool active,aspectLock; public MarkerRollbackState marker; public HashSet<string> components=new HashSet<string>(); }
        private sealed class OwnedRollbackPlan { public GdaiPlayableAssetsManifest prior; public Dictionary<string,HostRollbackState> hosts=new Dictionary<string,HostRollbackState>(); }
        private sealed class RetainedFieldSnapshot : IDisposable
        {
            private readonly Object holder;private readonly GameObject shell;private readonly string path;private readonly Dictionary<string,string> objectIdentities;
            private RetainedFieldSnapshot(Object holder,GameObject shell,string path,Dictionary<string,string> objectIdentities){this.holder=holder;this.shell=shell;this.path=path;this.objectIdentities=objectIdentities;}
            private static bool HasSemanticChildren(SerializedProperty property)
            {
                if(property.propertyType==SerializedPropertyType.ManagedReference)throw new InvalidOperationException("managed reference retention unsupported: "+property.propertyPath);
                return property.propertyType==SerializedPropertyType.Generic||property.isArray&&property.propertyType!=SerializedPropertyType.String;
            }
            private static IEnumerable<SerializedProperty> Properties(SerializedProperty root)
            {
                var current=root.Copy();var end=root.GetEndProperty();
                while(true) {yield return current.Copy();if(!current.Next(HasSemanticChildren(current))||SerializedProperty.EqualContents(current,end))yield break;}
            }
            public static RetainedFieldSnapshot Capture(Object instance,SerializedProperty live,GameObject prefabRoot,string prefabPath)
            {
                GameObject shell=null;Object holder=null;
                try {
                    if(instance is ScriptableObject)holder=ScriptableObject.CreateInstance(instance.GetType());
                    else {shell=new GameObject("GDAI_RetainedFieldSnapshot");shell.SetActive(false);holder=shell.AddComponent(instance.GetType());}
                    using(var copy=new SerializedObject(holder)){copy.CopyFromSerializedProperty(live);copy.ApplyModifiedPropertiesWithoutUndo();}
                    var identities=new Dictionary<string,string>();foreach(var property in Properties(live))if(property.propertyType==SerializedPropertyType.ObjectReference)identities.Add(property.propertyPath,StableObjectIdentity(property.objectReferenceValue,prefabRoot,prefabPath));
                    return new RetainedFieldSnapshot(holder,shell,live.propertyPath,identities);
                } catch {if(shell!=null)Object.DestroyImmediate(shell);else if(holder!=null)Object.DestroyImmediate(holder);throw;}
            }
            public void Verify(SerializedProperty live,GameObject prefabRoot,string prefabPath)
            {
                using(var retained=new SerializedObject(holder)) {
                    var expected=retained.FindProperty(path)??throw new InvalidOperationException("retained field snapshot missing: "+path);
                    foreach(var property in Properties(live)) {
                        var prior=retained.FindProperty(property.propertyPath)??throw new InvalidOperationException("retained nested field snapshot missing: "+property.propertyPath);
                        if(property.propertyType!=prior.propertyType||property.isArray!=prior.isArray||property.isArray&&property.arraySize!=prior.arraySize)throw new InvalidOperationException("preserved user field shape changed: "+property.propertyPath);
                        if(property.propertyType==SerializedPropertyType.ObjectReference) {
                            if(!objectIdentities.TryGetValue(property.propertyPath,out var identity)||StableObjectIdentity(property.objectReferenceValue,prefabRoot,prefabPath)!=identity)throw new InvalidOperationException("preserved user field changed: "+property.propertyPath);
                        } else if(!HasSemanticChildren(property)&&!SerializedProperty.DataEquals(property,prior))throw new InvalidOperationException("preserved user field changed: "+property.propertyPath);
                    }
                }
            }
            public void Dispose(){if(shell!=null)Object.DestroyImmediate(shell);else if(holder!=null)Object.DestroyImmediate(holder);}
        }
        private sealed class FieldMergePlan : IDisposable
        {
            public readonly HashSet<string> updateComponents=new HashSet<string>();
            private readonly Dictionary<string,FieldUpdateDecision> decisions=new Dictionary<string,FieldUpdateDecision>();
            private readonly Dictionary<string,RetainedFieldSnapshot> retained=new Dictionary<string,RetainedFieldSnapshot>();
            private readonly Dictionary<string,JToken> rollbackStates=new Dictionary<string,JToken>();
            private readonly HashSet<string> writesStarted=new HashSet<string>();
            private static string Key(string host,string component,string field)=>host+"|"+component+"|"+field;
            public void Add(string host,string component,string field,FieldUpdateDecision decision,Object instance,SerializedProperty property,GameObject prefabRoot,string prefabPath,JToken rollbackState=null){var key=Key(host,component,field);decisions.Add(key,decision);if(decision==FieldUpdateDecision.PreserveUserEdit)retained.Add(key,RetainedFieldSnapshot.Capture(instance,property,prefabRoot,prefabPath));if(decision==FieldUpdateDecision.CreatorUpdate&&rollbackState!=null)rollbackStates.Add(key,rollbackState.DeepClone());}
            public void AdmitInitialWrite(string host,string component,string field){decisions.Add(Key(host,component,field),FieldUpdateDecision.CreatorUpdate);}
            public FieldUpdateDecision Decision(string host,string component,string field)=>decisions.TryGetValue(Key(host,component,field),out var decision)?decision:FieldUpdateDecision.CreatorUpdate;
            public void MarkWriteStarted(string host,string component,string field){var key=Key(host,component,field);if(!decisions.TryGetValue(key,out var decision)||decision!=FieldUpdateDecision.CreatorUpdate)throw new InvalidOperationException("field write was not admitted: "+field);writesStarted.Add(key);}
            public bool WasWritten(string host,string component,string field)=>writesStarted.Contains(Key(host,component,field));
            public JToken RollbackState(string host,string component,string field)=>rollbackStates.TryGetValue(Key(host,component,field),out var state)?state:null;
            public void VerifyRetained(string host,string component,string field,SerializedProperty property,GameObject prefabRoot,string prefabPath){if(retained.TryGetValue(Key(host,component,field),out var snapshot))snapshot.Verify(property,prefabRoot,prefabPath);else throw new InvalidOperationException("retained user field snapshot missing: "+field);}
            public void Dispose(){foreach(var snapshot in retained.Values)snapshot.Dispose();retained.Clear();rollbackStates.Clear();writesStarted.Clear();}
        }
        public static Type Resolve(GdaiSourceBoundPlayablePlan.ComponentSpec spec,string kind)
        {
            if(spec.source_path=="builtin")return spec.native_defaults&&kind!="config"?GdaiSourceBoundPlayablePlan.Builtin(spec.type)??throw new InvalidOperationException("unknown builtin"):throw new InvalidOperationException("explicit native defaults required");
            var script=AssetDatabase.LoadAssetAtPath<MonoScript>(spec.source_path);
            var t=GdaiSourceBoundPlayablePlan.CompiledType(spec.type);
            if(script==null)throw new InvalidOperationException("source script not imported: "+spec.source_path);
            if(t==null||t.FullName!=spec.type||t.IsAbstract||t.IsGenericType||t.Assembly.GetName().Name.EndsWith("Editor",StringComparison.Ordinal))throw new InvalidOperationException("compiled type/source mismatch: "+spec.type);
            var player=UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player);
            if(!player.Any(a=>a.name==t.Assembly.GetName().Name))throw new InvalidOperationException("not a player assembly: "+spec.type);
            if(!(kind=="config"?typeof(ScriptableObject):typeof(MonoBehaviour)).IsAssignableFrom(t))throw new InvalidOperationException("hosting kind/type mismatch (plain classes cannot be attached): "+spec.type);
            if(script.GetClass()!=t)throw new InvalidOperationException("source script does not persist hosted type: "+spec.type+" at "+spec.source_path);
            if(t.IsDefined(typeof(ExecuteAlways),true)||t.IsDefined(typeof(ExecuteInEditMode),true))throw new InvalidOperationException("edit-mode executable components unsupported");
            if(kind=="config"&&new[]{"Awake","OnEnable","OnValidate"}.Any(name=>t.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!=null))throw new InvalidOperationException("ScriptableObject lifecycle callbacks unsupported during inactive composition");
            return t;
        }
        private static IEnumerable<string> SerializedFields(SerializedObject s)
        {
            var it=s.GetIterator();if(!it.Next(true))yield break;
            do {if(it.depth==0&&(!NativeFields.Contains(it.name) || HasGeneratedField(s.targetObject.GetType(),it.name)))yield return it.name;}while(it.Next(false));
        }
        private static bool HasGeneratedField(Type type,string name)
        {
            for(var t=type;t!=null&&t!=typeof(MonoBehaviour)&&t!=typeof(ScriptableObject);t=t.BaseType)
                if(t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!=null)return true;
            return false;
        }
        private static FieldInfo Field(Type type,string name)
        {
            for(var t=type;t!=null&&t!=typeof(MonoBehaviour)&&t!=typeof(ScriptableObject);t=t.BaseType) {
                var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(f!=null)return f;
            }
            throw new InvalidOperationException("serialized field reflection missing: "+name);
        }
        public static void CheckCoverage(Object target,GdaiSourceBoundPlayablePlan.ComponentSpec c,int revision=1)
        {
            GdaiSourceBoundPlayablePlan.RequireSafeFields(c.fields,revision);
            if(target==null)throw new InvalidOperationException("planned component not persisted: "+c.type);
            if(c.source_path!="builtin"&&revision==1&&SerializedTypeFields(target.GetType()).Any(f=>Sensitive(f.Name)))throw new InvalidOperationException("sensitive serialized configuration unsupported");
            using(var so=new SerializedObject(target)) {
                var actual=SerializedFields(so).ToArray();var planned=c.fields.Select(f=>f.path).ToArray();
                if(c.source_path!="builtin"&&(actual.Length!=planned.Length||!new HashSet<string>(actual).SetEquals(planned)))throw new InvalidOperationException("complete serialized field coverage mismatch: "+c.type);
                foreach(var f in c.fields) {
                    var p=so.FindProperty(f.path);
                    if(p==null||p.propertyType==SerializedPropertyType.ManagedReference)throw new InvalidOperationException("unsupported/missing serialized field: "+f.path);
                    if(c.source_path!="builtin") {
                        var actualField=Field(target.GetType(),f.path);if(revision==1)RequireBaselineShape(actualField.FieldType);else RequireRevision2Shape(actualField.FieldType,new HashSet<Type>());
                        if(f.disposition!="preserve_external")RuntimeShape(p,actualField.FieldType,f.disposition=="reference"?JToken.FromObject(f):f.value,f.disposition,new HashSet<Type>(),f.path,revision);
                    }
                }
            }
        }
        private static IEnumerable<FieldInfo> SerializedTypeFields(Type type,int revision=1) {
            var fields=new List<FieldInfo>();
            for(var t=type;t!=null&&t!=typeof(object)&&t!=typeof(MonoBehaviour)&&t!=typeof(ScriptableObject);t=t.BaseType)
                fields.AddRange(t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Where(f=>!f.IsStatic&&!f.IsInitOnly&&!f.IsNotSerialized&&(f.IsPublic||f.IsDefined(typeof(SerializeField),true))&&(revision==1||UnitySerializableType(f.FieldType,new HashSet<Type>()))));
            if(fields.Select(f=>f.Name).Distinct().Count()!=fields.Count)throw new InvalidOperationException("ambiguous inherited serialized fields");return fields;
        }
        private static Type ElementType(Type t) => t.IsArray?t.GetElementType():t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(List<>)?t.GetGenericArguments()[0]:null;
        private static bool UnitySerializableType(Type type,HashSet<Type> seen) {
            if(typeof(Object).IsAssignableFrom(type)||type.IsPrimitive||type.IsEnum||type==typeof(string)||type==typeof(AnimationCurve)||type==typeof(Bounds)||type==typeof(Vector2)||type==typeof(Vector3)||type==typeof(Vector4)||type==typeof(LayerMask))return true;
            if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(Dictionary<,>))return false;
            var inner=ElementType(type);if(inner!=null)return UnitySerializableType(inner,seen);
            return type.IsSerializable;
        }
        public static void RequireBaselineShape(Type type) => BaselineShape(type,new HashSet<Type>());
        private static void BaselineShape(Type type,HashSet<Type> seen) {
            if(type==typeof(Bounds))throw new InvalidOperationException("Bounds requires revision2 configuration");
            if(type==typeof(string))throw new InvalidOperationException("string baseline configuration unsupported");
            if(typeof(Object).IsAssignableFrom(type)||type.IsEnum||new[]{typeof(int),typeof(float),typeof(double),typeof(long),typeof(short),typeof(byte),typeof(bool),typeof(Vector2),typeof(Vector3),typeof(Vector4),typeof(LayerMask)}.Contains(type))return;
            var inner=ElementType(type);if(inner!=null){BaselineShape(inner,seen);return;}
            if(seen.Contains(type)||!type.IsSerializable)throw new InvalidOperationException("unsupported/cyclic baseline configuration");
            var next=new HashSet<Type>(seen){type};foreach(var field in SerializedTypeFields(type)) {
                if(Sensitive(field.Name))throw new InvalidOperationException("sensitive baseline configuration unsupported");
                BaselineShape(field.FieldType,next);
            }
        }
        private static void RequireRevision2Shape(Type type,HashSet<Type> seen) {
            if(typeof(Object).IsAssignableFrom(type)||type.IsPrimitive||type.IsEnum||type==typeof(string)||type==typeof(AnimationCurve)||type==typeof(Bounds)||type==typeof(Vector2)||type==typeof(Vector3)||type==typeof(Vector4)||type==typeof(LayerMask))return;
            if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(Dictionary<,>))throw new InvalidOperationException("dictionary is not a Unity serialized field");
            var inner=ElementType(type);if(inner!=null){RequireRevision2Shape(inner,seen);return;}
            if(seen.Contains(type)||!type.IsSerializable)throw new InvalidOperationException("unsupported/cyclic revision2 configuration");
            var next=new HashSet<Type>(seen){type};foreach(var field in SerializedTypeFields(type,2)) {
                if(Sensitive(field.Name))throw new InvalidOperationException("sensitive nested configuration unsupported");
                RequireRevision2Shape(field.FieldType,next);
            }
        }
        public static void PublishWithCompleteBaseline(Action captureAndCommitBaseline,Action publishPass) {
            captureAndCommitBaseline();publishPass();
        }
        private static bool RuntimeRequired(Type type,HashSet<Type> seen,int revision) {
            if(typeof(Object).IsAssignableFrom(type))return true;var inner=ElementType(type);if(inner!=null)return RuntimeRequired(inner,seen,revision);
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||type==typeof(AnimationCurve)||type==typeof(Bounds)||type==typeof(Vector2)||type==typeof(Vector3)||type==typeof(Vector4)||type==typeof(LayerMask))return false;
            if(seen.Contains(type))throw new InvalidOperationException("recursive serialized configuration unsupported");
            var next=new HashSet<Type>(seen){type};return SerializedTypeFields(type,revision).Any(f=>RuntimeRequired(f.FieldType,next,revision));
        }
        // The runtime SerializedObject tree and compiled inherited fields independently cover nested leaves.
        private static void RuntimeShape(SerializedProperty property,Type type,JToken value,string disposition,HashSet<Type> seen,string path,int revision) {
            if(RuntimeRequired(type,new HashSet<Type>(),revision)&&disposition!="reference"&&disposition!="value")throw new InvalidOperationException("required nested reference cannot be default/optional: "+path);
            if(typeof(Object).IsAssignableFrom(type)) {
                if(property!=null&&property.propertyType!=SerializedPropertyType.ObjectReference||!(value is JObject reference)||reference["reference_kind"]==null||reference["target_id"]==null)throw new InvalidOperationException("required runtime reference missing: "+path);return;
            }
            var inner=ElementType(type);if(inner!=null) {
                if(!(value is JArray array)||RuntimeRequired(inner,new HashSet<Type>(),revision)&&array.Count==0)throw new InvalidOperationException("required runtime collection missing: "+path);
                for(int i=0;i<array.Count;i++)RuntimeShape(property!=null&&i<property.arraySize?property.GetArrayElementAtIndex(i):null,inner,array[i],"value",seen,path+"["+i+"]",revision);return;
            }
            if(type==typeof(Bounds)) {
                if(property!=null&&property.propertyType!=SerializedPropertyType.Bounds)throw new InvalidOperationException("runtime Bounds shape mismatch: "+path);
                ParseBounds(value);return;
            }
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||type==typeof(AnimationCurve)||type==typeof(Vector2)||type==typeof(Vector3)||type==typeof(Vector4)||type==typeof(LayerMask))return;
            if(seen.Contains(type)||!type.IsSerializable)throw new InvalidOperationException("unsupported/cyclic compiled configuration: "+path);
            var fields=SerializedTypeFields(type,revision).ToArray();
            if(!(value is JObject data)||data.Count!=fields.Length||fields.Any(f=>data[f.Name]==null))throw new InvalidOperationException("complete runtime nested configuration required: "+path);
            if(property!=null) {
                if(property.propertyType!=SerializedPropertyType.Generic)throw new InvalidOperationException("runtime configuration shape mismatch: "+path);
                var child=property.Copy();var end=property.GetEndProperty();var names=new List<string>();bool enter=true;
                while(child.Next(enter)&&!SerializedProperty.EqualContents(child,end)){enter=false;if(child.depth==property.depth+1)names.Add(child.name);}
                if(names.Count>0&&!new HashSet<string>(names).SetEquals(fields.Select(f=>f.Name)))throw new InvalidOperationException("runtime nested serialized coverage mismatch: "+path);
            }
            var next=new HashSet<Type>(seen){type};foreach(var field in fields) {
                if(Sensitive(field.Name))throw new InvalidOperationException("sensitive nested configuration unsupported");
                RuntimeShape(property?.FindPropertyRelative(field.Name),field.FieldType,data[field.Name],disposition,next,path+"."+field.Name,revision);
            }
        }
        public sealed class GeometryWrite { public string host_id,component_id,type,field; public JToken expected; }
        private static JObject VectorState(Vector3 v) => new JObject { ["x"]=v.x,["y"]=v.y,["z"]=v.z };
        public static List<GeometryWrite> ImplicitGeometryWrites(GdaiPlayableContract c) {
            var writes=new List<GeometryWrite>();
            foreach(var host in c.source_bound_plan.objects.Where(h=>h.kind!="config")) {
                void Add(string component,string type,string field,JToken value) => writes.Add(new GeometryWrite {host_id=host.id,component_id=component,type=type,field=field,expected=value});
                var camera=host.kind=="scene"?host.components.SingleOrDefault(x=>x.type=="UnityEngine.Camera"):null;
                if(host.position=="placement") {
                    var placement=c.source_bound_context["placements"].Single(x=>(string)x["entity_id"]==host.entity_id);
                    var position=GdaiSceneAssemblyCoordinateUtility.CanvasToWorld((float)placement["x"],(float)placement["y"],(float)c.source_bound_context["arena"]["width"],(float)c.source_bound_context["arena"]["height"]);
                    if(camera==null)Add("__implicit_transform","UnityEngine.Transform","position",VectorState(position));
                    Add("__implicit_transform","UnityEngine.Transform","localScale",VectorState(Vector3.one*(float)placement["scale"]));
                }
                if(camera!=null) {
                    var framing=GdaiSourceBoundPlayablePlan.Framing(c);
                    Add("__implicit_transform","UnityEngine.Transform","position",VectorState(new Vector3(0,0,-10)));
                    Add(camera.id,"UnityEngine.Camera","orthographic",new JValue(true));Add(camera.id,"UnityEngine.Camera","orthographicSize",new JValue(framing.SolveOrthographicSize()));Add(camera.id,"UnityEngine.Camera","aspect",new JValue(framing.target_aspect));
                }
            }return writes;
        }
        private static JToken GeometryState(GameObject go,GeometryWrite w) {
            if(w.type=="UnityEngine.Transform")return VectorState(w.field=="position"?go.transform.position:go.transform.localScale);
            var camera=go.GetComponent<Camera>()??throw new InvalidOperationException("prior camera removed");
            if(w.field=="aspect") {
                var locks=go.GetComponents<GdaiCameraAspectLock>();
                if(locks.Length!=1)throw new InvalidOperationException("saved camera aspect lock missing/ambiguous");
                return new JValue(locks[0].TargetAspect);
            }
            return w.field=="orthographic"?new JValue(camera.orthographic):new JValue(camera.orthographicSize);
        }
        private static JToken GeometryStateForAdmission(GameObject go,GeometryWrite w,JToken prior,bool hasBaseline) {
            if(w.field!="aspect")return GeometryState(go,w);
            var camera=go.GetComponent<Camera>()??throw new InvalidOperationException("prior camera removed");
            var locks=go.GetComponents<GdaiCameraAspectLock>();
            if(locks.Length==1)return new JValue(locks[0].TargetAspect);
            if(locks.Length>1)throw new InvalidOperationException("saved camera aspect lock missing/ambiguous");
            if(!hasBaseline)throw new InvalidOperationException("pre-lock camera aspect baseline missing/ambiguous");
            var live=new JValue(camera.aspect);
            if(!GeometryEqual(live,prior))throw new InvalidOperationException("pre-lock camera aspect conflict before mutation: "+w.host_id);
            return live;
        }
        // Geometry is stored by Unity as float; JSON may reload the same value as double.
        private static bool GeometryEqual(JToken left,JToken right) {
            if(left is JValue a&&right is JValue b&&
               (a.Type==JTokenType.Float||a.Type==JTokenType.Integer)&&
               (b.Type==JTokenType.Float||b.Type==JTokenType.Integer)) {
                var x=(float)a;var y=(float)b;
                return !float.IsNaN(x)&&!float.IsInfinity(x)&&!float.IsNaN(y)&&!float.IsInfinity(y)&&x==y;
            }
            if(left is JObject xObject&&right is JObject yObject)
                return xObject.Count==yObject.Count&&xObject.Properties().All(p=>yObject.TryGetValue(p.Name,out var value)&&GeometryEqual(p.Value,value));
            return JToken.DeepEquals(left,right);
        }
        private static FieldUpdateDecision DecideGeometryUpdate(JToken prior,JToken live,JToken expected,bool hasBaseline) {
            if(!hasBaseline)return FieldUpdateDecision.MissingBaseline;
            if(GeometryEqual(live,expected))return FieldUpdateDecision.AlreadyApplied;
            return GeometryEqual(live,prior)?FieldUpdateDecision.CreatorUpdate:FieldUpdateDecision.Conflict;
        }
        private static void ApplyGeometry(GameObject go,IEnumerable<GeometryWrite> writes) {
            foreach(var w in writes) {
                if(w.type=="UnityEngine.Transform") {var vector=new Vector3((float)w.expected["x"],(float)w.expected["y"],(float)w.expected["z"]);if(w.field=="position")go.transform.position=vector;else go.transform.localScale=vector;}
                else {var camera=go.GetComponent<Camera>();if(w.field=="orthographic")camera.orthographic=(bool)w.expected;else if(w.field=="orthographicSize")camera.orthographicSize=(float)w.expected;else {var aspect=go.GetComponent<GdaiCameraAspectLock>()??go.AddComponent<GdaiCameraAspectLock>();aspect.SetTargetAspect((float)w.expected);}}
            }
        }
        public static void VerifyImplicitGeometry(GdaiPlayableContract c,Dictionary<string,Object> objects,bool requireActive=false) {
            foreach(var w in ImplicitGeometryWrites(c)) {
                var go=(GameObject)objects[w.host_id];
                if(!GeometryEqual(GeometryState(go,w),w.expected))throw new InvalidOperationException("saved implicit geometry mismatch: "+w.host_id+"/"+w.field);
                if(requireActive&&w.field=="aspect"&&!GeometryEqual(new JValue(go.GetComponent<Camera>().aspect),w.expected))throw new InvalidOperationException("active camera aspect mismatch: "+w.host_id);
            }
        }
        private static void AdmitGeometry(GdaiPlayableContract c,GdaiPlayableAssetsManifest prior,Dictionary<string,Object> existing) {
            foreach(var w in ImplicitGeometryWrites(c).Where(w=>existing.ContainsKey(w.host_id))) {
                var baseline=prior.source_bound_field_baselines.Where(b=>b.host_id==w.host_id&&b.component_id==w.component_id&&b.type==w.type&&b.source_path=="__implicit_geometry"&&b.field==w.field).ToList();
                var priorState=baseline.Count==1?baseline[0].state:null;
                var liveState=GeometryStateForAdmission((GameObject)existing[w.host_id],w,priorState,baseline.Count==1);
                var decision=DecideGeometryUpdate(priorState,liveState,w.expected,baseline.Count==1);
                if(decision==FieldUpdateDecision.MissingBaseline||decision==FieldUpdateDecision.Conflict)throw new InvalidOperationException("implicit geometry "+decision+" before mutation: "+w.host_id+"/"+w.field);
                if(w.field=="aspect"&&((GameObject)existing[w.host_id]).activeInHierarchy) {
                    var live=new JValue(((GameObject)existing[w.host_id]).GetComponent<Camera>().aspect);
                    if(DecideGeometryUpdate(baseline[0].state,live,w.expected,true)==FieldUpdateDecision.Conflict)throw new InvalidOperationException("implicit geometry Conflict before mutation: "+w.host_id+"/"+w.field);
                }
            }
        }
        private static void Folder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            var parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);
            if(!AssetDatabase.IsValidFolder(path))AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        private static void TrackMissingOwnedFolders(string assetPath,HashSet<string> missing)
        {
            var ownedRoot=GdaiPlayableContract.OwnedPrefix.TrimEnd('/');
            for(var path=Path.GetDirectoryName(assetPath)?.Replace('\\','/');!string.IsNullOrEmpty(path)&&path.StartsWith(ownedRoot,StringComparison.Ordinal);path=Path.GetDirectoryName(path)?.Replace('\\','/'))
                if(!AssetDatabase.IsValidFolder(path))missing.Add(path);
        }
        private static void DeleteCreatedOwnedFolders(HashSet<string> created)
        {
            foreach(var path in created.OrderByDescending(value=>value.Length)) {
                var absolute=Path.Combine(Root,path);
                if(Directory.Exists(absolute)&&!Directory.EnumerateFileSystemEntries(absolute).Any())AssetDatabase.DeleteAsset(path);
            }
        }
        private static IEnumerable<GameObject> SceneObjects(UnityEngine.SceneManagement.Scene scene) => scene.IsValid()&&scene.isLoaded?scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject):Enumerable.Empty<GameObject>();
        private static List<GameObject> SceneMatches(UnityEngine.SceneManagement.Scene scene,string name) => SceneObjects(scene).Where(go=>go.name==name).ToList();
        private static List<GameObject> SceneMatches(string name) => SceneMatches(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),name);
        private static bool PriorOwns(GdaiPlayableAssetsManifest prior,GameObject go,string project,string profile)
        {
            var m=go.GetComponent<GdaiGeneratedPlayableMarker>();
            return prior!=null&&prior.project_id==project&&prior.profile_id==profile&&m!=null&&m.profileId==profile&&prior.owned_scene_objects.Count(o=>o.name==go.name&&o.role==m.ownedRole&&o.snapshot_id==m.snapshotId&&o.profile_id==m.profileId)==1;
        }
        private static bool PriorAsset(GdaiPlayableAssetsManifest prior,string path,string project,string profile) => prior!=null&&prior.project_id==project&&prior.profile_id==profile&&prior.assets.Count(a=>a.path==path&&a.guid==AssetDatabase.AssetPathToGUID(path))==1;
        private static void Stamp(GameObject go,GdaiSourceBoundPlayablePlan.Host host,string snapshot)
        {
            var marker=go.GetComponent<GdaiGeneratedPlayableMarker>()??go.AddComponent<GdaiGeneratedPlayableMarker>();
            marker.profileId=GdaiSourceBoundPlayablePlan.Profile;marker.snapshotId=snapshot;marker.ownedRole=host.id;
        }
        public static Dictionary<string,Type> AdmitTransientComposition(GdaiPlayableContract c)
        {
            var types=c.source_bound_plan.objects.SelectMany(o=>o.components.Select(comp=>new {comp.id,type=Resolve(comp,o.kind)})).ToDictionary(x=>x.id,x=>x.type);
            VerifyCompiledObligations(c);
            var preview=default(UnityEngine.SceneManagement.Scene);var stagedObjects=new Dictionary<string,Object>();var stagedConfigs=new List<ScriptableObject>();
            try {
                preview=EditorSceneManager.NewPreviewScene();
                foreach(var host in c.source_bound_plan.objects) {
                    if(host.kind=="config") {
                        var value=ScriptableObject.CreateInstance(types[host.components[0].id]);stagedConfigs.Add(value);stagedObjects.Add(host.id,value);
                    }else {
                        var go=new GameObject("GDAI_Admission_"+host.id);go.SetActive(false);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,preview);
                        foreach(var component in host.components)go.AddComponent(types[component.id]);
                        ApplyGeometry(go,ImplicitGeometryWrites(c).Where(w=>w.host_id==host.id));stagedObjects.Add(host.id,go);
                    }
                }
                foreach(var host in c.source_bound_plan.objects.OrderBy(o=>o.kind=="scene"?2:o.kind=="prefab"?1:0)) {
                    var target=stagedObjects[host.id];
                    foreach(var component in host.components) {
                        var instance=target is GameObject go?go.GetComponent(types[component.id]):target;
                        CheckCoverage(instance,component,c.source_bound_plan.revision);BindFields(instance,component,c,stagedObjects,host,target,true);
                    }
                }
            }finally {
                foreach(var value in stagedConfigs)if(value!=null)Object.DestroyImmediate(value);
                if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);
            }
            return types;
        }
        private static void RequireOwnedOutputPathAvailable(string assetPath)
        {
            var absolute=Path.Combine(Root,assetPath);var temporary=absolute+".tmp";
            if(Directory.Exists(absolute)||Directory.Exists(temporary))throw new InvalidOperationException("owned output path is a directory: "+assetPath);
            if(File.Exists(temporary))throw new InvalidOperationException("owned output temporary path is occupied: "+assetPath);
            for(var parent=Path.GetDirectoryName(absolute);!string.IsNullOrEmpty(parent)&&!Directory.Exists(parent);parent=Path.GetDirectoryName(parent))
                if(File.Exists(parent))throw new InvalidOperationException("owned output parent is a file: "+assetPath);
        }
        private static bool IsLoadedNonPreviewScene(UnityEngine.SceneManagement.Scene scene)
            => scene.IsValid()&&scene.isLoaded&&!EditorSceneManager.IsPreviewScene(scene);
        private static bool IsSameSceneIdentity(UnityEngine.SceneManagement.Scene actual,UnityEngine.SceneManagement.Scene expected)
            => IsLoadedNonPreviewScene(actual)&&IsLoadedNonPreviewScene(expected)&&actual.handle==expected.handle&&string.Equals(actual.path,expected.path,StringComparison.Ordinal);
        private static void ActivateSceneWithReadback(UnityEngine.SceneManagement.Scene scene,string failure)
        {
            if(!IsLoadedNonPreviewScene(scene))throw new InvalidOperationException(failure+": target scene invalid, unloaded or preview");
            if(IsSameSceneIdentity(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene))return;
            var reported=UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            if(!IsSameSceneIdentity(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene))throw new InvalidOperationException(failure+(reported?": active scene readback mismatch":": activation returned false and active scene readback mismatch"));
        }
        private static UnityEngine.SceneManagement.Scene OpenCanonicalAdditive(string scenePath,out bool openedByOperation)
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);openedByOperation=false;
            try {
                if(!scene.IsValid()||!scene.isLoaded) {
                    if(File.Exists(Path.Combine(Root,scenePath))) {scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);openedByOperation=true;}
                    else {
                        Folder(Path.GetDirectoryName(scenePath).Replace('\\','/'));
                        File.WriteAllText(Path.Combine(Root,scenePath),NeutralEmptySceneAsset);
                        AssetDatabase.ImportAsset(scenePath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                        if(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath)==null)throw new InvalidOperationException("failed to import neutral canonical scene asset");
                        scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
                        openedByOperation=true;
                    }
                }
                if(!IsLoadedNonPreviewScene(scene)||!string.Equals(scene.path,scenePath,StringComparison.Ordinal))throw new InvalidOperationException("canonical scene identity invalid, unloaded, preview or wrong path");
                ActivateSceneWithReadback(scene,"failed to activate canonical scene");
                if(!GdaiCanonicalScene.AddToBuildSettings(scenePath))throw new InvalidOperationException("failed to register canonical scene in Build Settings");
                return scene;
            }catch {
                if(openedByOperation&&scene.IsValid()&&scene.isLoaded)try {EditorSceneManager.CloseScene(scene,true);}catch {}
                throw;
            }
        }
        private static void VerifyPersistedOwnership(GdaiPlayableContract c,string project,string snapshot,string sha,UnityEngine.SceneManagement.Scene persisted)
        {
            var m=GdaiPlayableOwnershipManifest.Load();
            if(m==null||m.schema_version!=GdaiPlayableAssetsManifest.SchemaVersion||m.project_id!=project||m.snapshot_id!=snapshot||m.profile_id!=c.profile_id||m.contract_revision!=c.contract_revision||m.contract_sha256!=sha)throw new InvalidOperationException("source-bound ownership identity mismatch");
            var expected=c.source_bound_plan.objects.Where(o=>o.kind!="scene").Select(o=>o.asset_path).Concat(new[]{c.canonical_scene.path}).ToArray();
            if(m.assets.Count!=expected.Length||!new HashSet<string>(m.assets.Select(a=>a.path)).SetEquals(expected))throw new InvalidOperationException("source-bound ownership asset set mismatch");
            foreach(var asset in m.assets)if(string.IsNullOrEmpty(asset.guid)||AssetDatabase.AssetPathToGUID(asset.path)!=asset.guid)throw new InvalidOperationException("source-bound asset GUID mismatch");
            var hosts=c.source_bound_plan.objects.Where(o=>o.kind=="scene").ToArray();
            if(m.owned_scene_objects.Count!=hosts.Length||!new HashSet<string>(m.owned_scene_objects.Select(o=>o.role)).SetEquals(hosts.Select(o=>o.id)))throw new InvalidOperationException("source-bound ownership host set mismatch");
            var markers=SceneObjects(persisted).Select(go=>go.GetComponent<GdaiGeneratedPlayableMarker>()).Where(marker=>marker!=null&&marker.profileId==c.profile_id).ToArray();
            if(markers.Length!=hosts.Length)throw new InvalidOperationException("source-bound persisted ownership count mismatch");
            foreach(var host in hosts)if(markers.Count(marker=>marker.gameObject.name==host.name&&marker.ownedRole==host.id&&marker.snapshotId==snapshot)!=1||m.owned_scene_objects.Count(o=>o.name==host.name&&o.role==host.id&&o.profile_id==c.profile_id&&o.snapshot_id==snapshot)!=1)throw new InvalidOperationException("source-bound persisted host ownership mismatch");
        }
        private static GdaiPlayableReceipt BuildPersistedReceipt(GdaiPlayableContract c,string project,string snapshot,string sha,string scenePath,DateTime now,bool requireActive,FieldMergePlan mergePlan)
        {
            var persisted=EditorSceneManager.OpenPreviewScene(scenePath);
            try {
                var r=new GdaiPlayableReceipt {project_id=project,snapshot_id=snapshot,contract_revision=c.contract_revision,contract_sha256=sha,generated_at=now.ToString("o"),manual_assembly_steps=0};
                void Check(string key,Action check) {try {check();r.checks.Add(new GdaiReceiptCheck {key=key,expected="persisted source-bound identity/serialized readback",actual="verified",pass=true});}catch(Exception ex){r.checks.Add(new GdaiReceiptCheck {key=key,expected="persisted source-bound identity/serialized readback",actual="failed",pass=false});r.failures.Add(key+": "+ex.Message);}}
                Check("contract",()=>{var errors=GdaiSourceBoundPlayablePlan.Validate(c);if(errors.Count>0)throw new InvalidOperationException(string.Join(";",errors));});
                Check("saved_scene",()=>{if(scenePath!=c.canonical_scene.path||!File.Exists(Path.Combine(Root,scenePath))||!persisted.IsValid()||!persisted.isLoaded||persisted.path!=scenePath)throw new InvalidOperationException("persisted scene path missing/mismatch");if(!EditorBuildSettings.scenes.Any(s=>s.path==scenePath&&s.enabled))throw new InvalidOperationException("Build Settings scene missing");});
                Check("compiled_source_identity",()=>{if(!GdaiSourceBoundPlayablePlan.Ready(c,out var error))throw new InvalidOperationException(error);});
                Check("independent_compiled_obligations",()=>VerifyCompiledObligations(c));
                Check("ownership",()=>VerifyPersistedOwnership(c,project,snapshot,sha,persisted));
                Dictionary<string,Object> objects=null;Check("all_owned_hosts",()=>objects=ReadObjects(c,persisted));
                if(objects!=null)Check("implicit_geometry",()=>VerifyImplicitGeometry(c,objects,requireActive));
                if(objects!=null)foreach(var host in c.source_bound_plan.objects) {
                    Check("host:"+host.id,()=>{if(host.kind!="scene")return;var go=(GameObject)objects[host.id];var marker=go.GetComponent<GdaiGeneratedPlayableMarker>();if(marker==null||marker.profileId!=c.profile_id||marker.snapshotId!=snapshot||marker.ownedRole!=host.id)throw new InvalidOperationException("host ownership mismatch");if(requireActive&&!go.activeSelf||!requireActive&&go.activeSelf)throw new InvalidOperationException("inactive/activation ordering mismatch");});
                    foreach(var comp in host.components)Check("fields:"+comp.id,()=>{GameObject contents=null;Object target=objects[host.id];try {if(host.kind=="prefab"){contents=PrefabUtility.LoadPrefabContents(host.asset_path);target=contents;if(!contents.activeSelf)throw new InvalidOperationException("bound prefab must be activated for runtime instantiation");}var type=Resolve(comp,host.kind);var instance=target is GameObject go?go.GetComponent(type):target;if(instance==null||instance.GetType()!=type)throw new InvalidOperationException("saved component type missing");CheckCoverage(instance,comp,c.source_bound_plan.revision);BindFieldsCore(instance,comp,c,objects,host,target,false,false,mergePlan);}finally {if(contents!=null)PrefabUtility.UnloadPrefabContents(contents);}});
                }
                r.status=r.failures.Count==0?"PASS":"FAIL";return r;
            }finally {if(persisted.IsValid())EditorSceneManager.ClosePreviewScene(persisted);}
        }
        public static GdaiPlayableComposerCta.Result Run(GdaiPlayableContract c,string project,string snapshot,string sha,string scenePath,DateTime now,Action completeTransaction=null)
        {
            var result=new GdaiPlayableComposerCta.Result();
            var op=GdaiPlayableOperation.Create(Root,project,snapshot,c.schema_version,c.contract_revision,sha,scenePath,now);result.OperationId=op.operation_id;
            var transient=new List<GameObject>();
            var createdSceneHosts=new List<GameObject>();
            var createdAssetPaths=new HashSet<string>();
            var createdOwnedFolderPaths=new HashSet<string>();
            var preview=default(UnityEngine.SceneManagement.Scene);
            var previousActiveScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var previousBuildSettings=EditorBuildSettings.scenes.Select(s=>new EditorBuildSettingsScene(s.path,s.enabled)).ToArray();
            var canonicalSceneExistedBefore=File.Exists(Path.Combine(Root,scenePath));
            bool canonicalLifecycleAttempted=false;
            bool canonicalOpenedByOperation=false;
            var canonicalScene=default(UnityEngine.SceneManagement.Scene);
            OwnedRollbackPlan rollbackPlan=null;
            FieldMergePlan mergePlan=null;
            byte[] previousManifest=null;var manifestPath=Path.Combine(Root,GdaiPlayableOwnershipManifest.ManifestPath);var manifestTemporaryPath=manifestPath+".tmp";var manifestTemporaryExistedBefore=File.Exists(manifestTemporaryPath);
            if(File.Exists(manifestPath))previousManifest=File.ReadAllBytes(manifestPath);
            bool sceneSaved=false;
            try {
                var validation=GdaiSourceBoundPlayablePlan.Validate(c);
                if(validation.Count>0)throw new InvalidOperationException(string.Join(";",validation));
                if(scenePath!=c.canonical_scene.path)throw new InvalidOperationException("canonical scene identity mismatch");
                if(!GdaiSourceBoundPlayablePlan.Ready(c,out var readiness))throw new InvalidOperationException(readiness);
                // Resolve and stage every compiled field/reference/default before scene/assets are touched.
                var types=AdmitTransientComposition(c);
                // All fallible owned output shapes known at admission are checked before scene,
                // Build Settings, Layer-B, prefab or config state can change.
                RequireOwnedOutputPathAvailable(GdaiPlayableOwnershipManifest.ManifestPath);
                TrackMissingOwnedFolders(GdaiPlayableOwnershipManifest.ManifestPath,createdOwnedFolderPaths);
                canonicalLifecycleAttempted=true;
                canonicalScene=OpenCanonicalAdditive(scenePath,out canonicalOpenedByOperation);
                var prior=GdaiPlayableOwnershipManifest.Load();
                // Ownership admission before mutation: duplicates/user objects and unrecorded assets reject.
                foreach(var host in c.source_bound_plan.objects) {
                    if(host.kind=="scene") {var found=SceneMatches(host.name);if(found.Count>1||found.Count==1&&!PriorOwns(prior,found[0],project,c.profile_id))throw new InvalidOperationException("user/ambiguous scene object: "+host.name);}
                    else if(File.Exists(Path.Combine(Root,host.asset_path))&&!PriorAsset(prior,host.asset_path,project,c.profile_id))throw new InvalidOperationException("unowned asset path: "+host.asset_path);
                }
                // Compare every existing field before any planned object/field mutation.
                mergePlan=new FieldMergePlan();
                AdmitExistingFields(c,prior,mergePlan);
                rollbackPlan=BuildRollbackPlan(c,prior);
                foreach(var host in c.source_bound_plan.objects.Where(h=>h.kind!="scene"))
                    if(!File.Exists(Path.Combine(Root,host.asset_path))) {createdAssetPaths.Add(host.asset_path);TrackMissingOwnedFolders(host.asset_path,createdOwnedFolderPaths);}
                op.Advance(Root,GdaiPlayablePhase.CanonicalSceneReady,now);
                var objects=new Dictionary<string,Object>();
                preview=EditorSceneManager.NewPreviewScene();
                // All shells are inactive. ScriptableObject editor OnEnable is outside scene activation;
                // config implementations with editor side effects are unsupported until runtime verified.
                foreach(var host in c.source_bound_plan.objects) {
                    if(host.kind=="config") {
                        Folder(Path.GetDirectoryName(host.asset_path).Replace('\\','/'));
                        var value=AssetDatabase.LoadAssetAtPath<ScriptableObject>(host.asset_path);
                        if(value==null) {value=ScriptableObject.CreateInstance(types[host.components[0].id]);AssetDatabase.CreateAsset(value,host.asset_path);}
                        if(value.GetType()!=types[host.components[0].id])throw new InvalidOperationException("existing config type changed");objects.Add(host.id,value);
                    }else {
                        GameObject go;
                        if(host.kind=="scene") {var found=SceneMatches(host.name);if(found.Count==1)go=found[0];else {go=new GameObject(host.name);createdSceneHosts.Add(go);}}
                        else {var existing=AssetDatabase.LoadAssetAtPath<GameObject>(host.asset_path);go=existing!=null?Object.Instantiate(existing):new GameObject(host.name);go.name=host.name;UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,preview);transient.Add(go);}
                        go.SetActive(false);Stamp(go,host,snapshot);
                        foreach(var comp in host.components) {var t=types[comp.id];if(go.GetComponents(t).Length>1)throw new InvalidOperationException("duplicate component host");if(go.GetComponent(t)==null)go.AddComponent(t);}
                        ApplyGeometry(go,ImplicitGeometryWrites(c).Where(w=>w.host_id==host.id));
                        if(host.kind=="prefab") {Folder(Path.GetDirectoryName(host.asset_path).Replace('\\','/'));var existing=AssetDatabase.LoadAssetAtPath<GameObject>(host.asset_path);if(existing!=null)objects.Add(host.id,existing);else {var asset=PrefabUtility.SaveAsPrefabAsset(go,host.asset_path);if(asset==null)throw new InvalidOperationException("prefab shell save failed");objects.Add(host.id,asset);}}
                        else objects.Add(host.id,go);
                    }
                }
                op.Advance(Root,GdaiPlayablePhase.SceneObjectsComposed,now);
                // Bind every config/prefab/scene host, including every prefab's internal references.
                foreach(var host in c.source_bound_plan.objects.OrderBy(o=>o.kind=="scene"?2:o.kind=="prefab"?1:0)) {
                    GameObject contents=null;Object target=objects[host.id];
                    try {
                        if(host.kind=="prefab") {contents=PrefabUtility.LoadPrefabContents(host.asset_path);target=contents;}
                        foreach(var comp in host.components) {
                            var instance=target is GameObject g?g.GetComponent(types[comp.id]):target;
                            CheckCoverage(instance,comp,c.source_bound_plan.revision);
                            BindFieldsCore(instance,comp,c,objects,host,target,true,mergePlan.updateComponents.Contains(host.id+"|"+comp.id),mergePlan);
                        }
                        if(contents!=null) {contents.SetActive(true);PrefabUtility.SaveAsPrefabAsset(contents,host.asset_path);objects[host.id]=AssetDatabase.LoadAssetAtPath<GameObject>(host.asset_path);}
                        else EditorUtility.SetDirty(target);
                    }finally {if(contents!=null)PrefabUtility.UnloadPrefabContents(contents);}
                }
                // Final geometry writes run after explicit fields; the same bounded inventory defines admission/readback.
                foreach(var host in c.source_bound_plan.objects.Where(o=>o.kind!="config")) {
                    GameObject contents=null;try {
                        var go=host.kind=="prefab"?(contents=PrefabUtility.LoadPrefabContents(host.asset_path)):(GameObject)objects[host.id];
                        ApplyGeometry(go,ImplicitGeometryWrites(c).Where(w=>w.host_id==host.id));
                        if(contents!=null)PrefabUtility.SaveAsPrefabAsset(contents,host.asset_path);
                    }finally {if(contents!=null)PrefabUtility.UnloadPrefabContents(contents);}
                }
                AssetDatabase.SaveAssets();op.Advance(Root,GdaiPlayablePhase.BindingsApplied,now);
                if(!EditorSceneManager.SaveScene(canonicalScene,scenePath))throw new InvalidOperationException("scene save failed");
                sceneSaved=true;
                op.Advance(Root,GdaiPlayablePhase.SceneSaved,now);
                if(!GdaiPlayableOwnershipManifest.WriteSourceBound(c,project,snapshot,sha,scenePath,out var manifestError))throw new InvalidOperationException(manifestError);
                completeTransaction?.Invoke();
                result.Receipt=BuildPersistedReceipt(c,project,snapshot,sha,scenePath,now,false,mergePlan);
                if(!result.Receipt.IsPass)throw new InvalidOperationException("saved persisted readback: "+string.Join(";",result.Receipt.failures));
                // Only after independent persisted readback may scene objects activate.
                foreach(var host in c.source_bound_plan.objects.Where(o=>o.kind=="scene")) {
                    var go=SceneMatches(host.name).Single();
                    if(host.components.Any(component=>component.type=="UnityEngine.Camera"))go.GetComponent<GdaiCameraAspectLock>().Apply();
                    go.SetActive(true);
                }
                if(!EditorSceneManager.SaveScene(canonicalScene,scenePath))throw new InvalidOperationException("activated scene save failed");
                result.Receipt=BuildPersistedReceipt(c,project,snapshot,sha,scenePath,now,true,mergePlan);
                if(!result.Receipt.IsPass)throw new InvalidOperationException("activation/readback receipt failed");
                PublishWithCompleteBaseline(()=>GdaiPlayableOwnershipManifest.CommitSourceBoundBaseline(c),()=>{if(!GdaiPlayableReceiptWriter.Write(result.Receipt,out var receiptError))throw new InvalidOperationException("activation/readback receipt failed");});
                op.Advance(Root,GdaiPlayablePhase.ReceiptWritten,now);op.Advance(Root,GdaiPlayablePhase.Complete,now);result.Completed=true;return result;
            }catch(Exception ex) {
                var rollbackErrors=new List<string>();
                // Existing owned state is restored only from the prior admitted baseline and
                // explicit owned marker/active/component-presence metadata. Opaque fields are skipped.
                if(rollbackPlan!=null)try {RestorePriorOwnedState(c,rollbackPlan,canonicalScene,mergePlan);}catch(Exception rollback){rollbackErrors.Add("owned state: "+rollback.Message);}
                // Roll back only artifacts proven absent before this attempt and scene hosts
                // created by this exact operation. Existing/user assets are never deleted.
                var removeCreatedSceneHosts=createdSceneHosts.Any(go=>go!=null);
                foreach(var go in createdSceneHosts.Where(go=>go!=null))try {Object.DestroyImmediate(go);}catch(Exception cleanup){rollbackErrors.Add("created scene host: "+cleanup.Message);}
                if(sceneSaved&&removeCreatedSceneHosts) {
                    try {var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(active.IsValid()&&active.path==scenePath&&!EditorSceneManager.SaveScene(active,scenePath))throw new InvalidOperationException("created scene host cleanup save failed");}catch(Exception cleanup){rollbackErrors.Add("scene cleanup: "+cleanup.Message);}
                }
                foreach(var path in createdAssetPaths)if(File.Exists(Path.Combine(Root,path)))try {if(!AssetDatabase.DeleteAsset(path))throw new InvalidOperationException("asset delete returned false: "+path);}catch(Exception cleanup){rollbackErrors.Add("created asset: "+cleanup.Message);}
                if(canonicalLifecycleAttempted) {
                    // Restore only the active-scene selection. User scenes remained loaded
                    // additively throughout, including dirty and never-saved scenes.
                    try {if(IsLoadedNonPreviewScene(previousActiveScene))ActivateSceneWithReadback(previousActiveScene,"active scene restore failed");}catch(Exception cleanup){rollbackErrors.Add("active scene: "+cleanup.Message);}
                    if(canonicalOpenedByOperation&&canonicalScene.IsValid()&&canonicalScene.isLoaded)try {if(!EditorSceneManager.CloseScene(canonicalScene,true))throw new InvalidOperationException("canonical close failed");}catch(Exception cleanup){rollbackErrors.Add("canonical close: "+cleanup.Message);}
                    if(!canonicalSceneExistedBefore&&File.Exists(Path.Combine(Root,scenePath))) {
                        try {
                            var absoluteScenePath=Path.Combine(Root,scenePath);
                            if(!AssetDatabase.DeleteAsset(scenePath)&&File.Exists(absoluteScenePath)) {
                                File.Delete(absoluteScenePath);
                                var sceneMetaPath=absoluteScenePath+".meta";
                                if(File.Exists(sceneMetaPath))File.Delete(sceneMetaPath);
                                AssetDatabase.Refresh();
                            }
                        }catch(Exception cleanup){rollbackErrors.Add("canonical asset: "+cleanup.Message);}
                    }
                    // EnsureSavedAndInBuild may add/enable Main and disable the owned template;
                    // a failed compose must restore the complete prior list and enabled flags.
                    try {EditorBuildSettings.scenes=previousBuildSettings;}catch(Exception cleanup){rollbackErrors.Add("Build Settings: "+cleanup.Message);}
                }
                // Failed readback must not promote expected values into the prior-state receipt.
                try {
                    if(!manifestTemporaryExistedBefore&&File.Exists(manifestTemporaryPath))File.Delete(manifestTemporaryPath);
                    if(previousManifest!=null&&(!File.Exists(manifestPath)||!previousManifest.SequenceEqual(File.ReadAllBytes(manifestPath)))) {
                        if(Directory.Exists(manifestPath)) {if(Directory.EnumerateFileSystemEntries(manifestPath).Any())throw new InvalidOperationException("occupied manifest directory is not empty");Directory.Delete(manifestPath);}
                        File.WriteAllBytes(manifestPath,previousManifest);AssetDatabase.ImportAsset(GdaiPlayableOwnershipManifest.ManifestPath,ImportAssetOptions.ForceSynchronousImport);
                    }else if(previousManifest==null&&File.Exists(manifestPath))AssetDatabase.DeleteAsset(GdaiPlayableOwnershipManifest.ManifestPath);
                }catch(Exception cleanup){rollbackErrors.Add("manifest: "+cleanup.Message);}
                try {DeleteCreatedOwnedFolders(createdOwnedFolderPaths);}catch(Exception cleanup){rollbackErrors.Add("created folders: "+cleanup.Message);}
                result.Error="source-bound composition: "+ex.Message+(rollbackErrors.Count==0?"":" | rollback failed: "+string.Join("; ",rollbackErrors));op.Fail(Root,now,result.Error);return result;
            }finally {mergePlan?.Dispose();foreach(var go in transient)if(go!=null)Object.DestroyImmediate(go);if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);}
        }
        public enum FieldUpdateDecision { CreatorUpdate, AlreadyApplied, PreserveUserEdit, Conflict, MissingBaseline }
        public static FieldUpdateDecision DecideFieldUpdate(JToken prior,JToken live,JToken expected,bool hasBaseline)
        {
            if(!hasBaseline)return FieldUpdateDecision.MissingBaseline;
            if(JToken.DeepEquals(live,expected))return FieldUpdateDecision.AlreadyApplied;
            if(JToken.DeepEquals(prior,expected))return FieldUpdateDecision.PreserveUserEdit;
            return JToken.DeepEquals(live,prior)?FieldUpdateDecision.CreatorUpdate:FieldUpdateDecision.Conflict;
        }
        public static bool ShouldApplyFieldUpdate(FieldUpdateDecision decision)
        {
            if(decision==FieldUpdateDecision.Conflict||decision==FieldUpdateDecision.MissingBaseline)throw new InvalidOperationException("unadmitted field decision");
            return decision==FieldUpdateDecision.CreatorUpdate;
        }
        public static bool ShouldRestorePriorField(FieldUpdateDecision decision)
        {
            if(decision==FieldUpdateDecision.Conflict||decision==FieldUpdateDecision.MissingBaseline)throw new InvalidOperationException("unadmitted field decision");
            return decision==FieldUpdateDecision.CreatorUpdate;
        }
        private static bool Sensitive(string field) => GdaiSourceBoundPlayablePlan.SensitiveField(field);
        private static JToken FieldState(SerializedProperty p)
        {
            // No string or unknown-shape reads: they may contain user-provided sensitive data.
            if(Sensitive(p.name))throw new InvalidOperationException("sensitive field baseline unsupported");
            if(p.isArray&&p.propertyType!=SerializedPropertyType.String){var values=new JArray();for(int i=0;i<p.arraySize;i++)values.Add(FieldState(p.GetArrayElementAtIndex(i)));return values;}
            switch(p.propertyType) {
                case SerializedPropertyType.Integer:return new JObject { ["kind"]="integer",["value"]=p.longValue };
                case SerializedPropertyType.Enum:case SerializedPropertyType.LayerMask:return new JObject { ["kind"]="integer",["value"]=p.intValue };
                case SerializedPropertyType.Float:return new JObject { ["kind"]="float",["value"]=p.doubleValue };
                case SerializedPropertyType.Boolean:return new JObject { ["kind"]="boolean",["value"]=p.boolValue };
                case SerializedPropertyType.ObjectReference:return new JObject { ["kind"]="object",["identity"]=StableObjectIdentity(p.objectReferenceValue) };
                case SerializedPropertyType.Bounds:return new JObject { ["kind"]="bounds",["value"]=BoundsState(p.boundsValue) };
                case SerializedPropertyType.Vector2:case SerializedPropertyType.Vector3:case SerializedPropertyType.Vector4:
                    var vector=new JObject();foreach(var name in new[]{"x","y","z","w"}){var child=p.FindPropertyRelative(name);if(child!=null)vector[name]=FieldState(child);}return vector;
                case SerializedPropertyType.Generic:
                    var data=new JObject();var childProperty=p.Copy();var end=p.GetEndProperty();bool enter=true;while(childProperty.Next(enter)&&!SerializedProperty.EqualContents(childProperty,end)){enter=false;if(childProperty.depth==p.depth+1)data[childProperty.name]=FieldState(childProperty);}return data;
                default:throw new InvalidOperationException("non-sensitive field baseline unsupported: "+p.propertyType);
            }
        }
        private static int[] PrefabSiblingPath(Transform value,Transform root,string field)
        {
            var reversed=new List<int>();var current=value;
            while(current!=root) {if(current==null)throw new InvalidOperationException("prefab-local object outside owned root: "+field);reversed.Add(current.GetSiblingIndex());current=current.parent;}
            reversed.Reverse();return reversed.ToArray();
        }
        private static bool IsLoadedPrefabContentsReference(Object value,Object source)
        {
            if(value==null||source==null||EditorUtility.IsPersistent(value)||!EditorSceneManager.IsPreviewSceneObject(value))return false;
            var gameObject=value as GameObject;if(gameObject==null&&value is Component component)gameObject=component.gameObject;if(gameObject==null)return false;
            var sourcePath=AssetDatabase.GetAssetPath(source);if(string.IsNullOrEmpty(sourcePath)||!sourcePath.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase))return false;
            var previewPath=gameObject.scene.path;if(!string.IsNullOrEmpty(previewPath)&&!previewPath.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase))return false;
            return previewPath==sourcePath||PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject)==sourcePath;
        }
        private static string StableObjectIdentity(Object value,GameObject prefabRoot=null,string prefabPath=null)
        {
            if(value==null)return "null";
            Transform transform=null;if(value is GameObject gameObject)transform=gameObject.transform;else if(value is Component component)transform=component.transform;
            if(prefabRoot!=null&&!string.IsNullOrEmpty(prefabPath)&&transform!=null&&transform.root==prefabRoot.transform) {
                var guid=AssetDatabase.AssetPathToGUID(prefabPath);if(string.IsNullOrEmpty(guid))throw new InvalidOperationException("prefab-local owner GUID unavailable");
                var hierarchy=string.Join(".",PrefabSiblingPath(transform,prefabRoot.transform,"retained reference").Select(index=>index.ToString()));
                if(value is GameObject)return "prefab-local|"+guid+"|"+hierarchy+"|game-object";
                if(value is Transform)return "prefab-local|"+guid+"|"+hierarchy+"|transform";
                var retainedComponent=(Component)value;var type=retainedComponent.GetType();var typeIdentity=type.AssemblyQualifiedName;if(string.IsNullOrEmpty(typeIdentity))throw new InvalidOperationException("prefab-local component type unavailable");
                var matches=transform.gameObject.GetComponents(type);var componentIndex=Array.IndexOf(matches,retainedComponent);if(componentIndex<0)throw new InvalidOperationException("prefab-local component unavailable");
                return "prefab-local|"+guid+"|"+hierarchy+"|component|"+typeIdentity+"|"+componentIndex;
            }
            var correspondingSource=PrefabUtility.GetCorrespondingObjectFromSource(value);var identityObject=IsLoadedPrefabContentsReference(value,correspondingSource)?correspondingSource:value;var objectId=GlobalObjectId.GetGlobalObjectIdSlow(identityObject);
            if(objectId.Equals(default(GlobalObjectId)))throw new InvalidOperationException("stable object identity unavailable");return objectId.ToString();
        }
        private static Transform RemapPrefabTransform(Transform source,GameObject prefabContents,string field)
        {
            var path=PrefabSiblingPath(source,source.root,field);var mapped=prefabContents.transform;
            foreach(var index in path) {if(index<0||index>=mapped.childCount)throw new InvalidOperationException("prior local prefab transform unavailable: "+field);mapped=mapped.GetChild(index);}
            return mapped;
        }
        private static Object RemapPrefabObject(Object source,GameObject prefabContents,string field)
        {
            if(source is GameObject sourceObject)return RemapPrefabTransform(sourceObject.transform,prefabContents,field).gameObject;
            if(source is Transform sourceTransform)return RemapPrefabTransform(sourceTransform,prefabContents,field);
            if(source is Component sourceComponent) {
                var mapped=RemapPrefabTransform(sourceComponent.transform,prefabContents,field);var sourceComponents=sourceComponent.gameObject.GetComponents(sourceComponent.GetType());var index=Array.IndexOf(sourceComponents,sourceComponent);var mappedComponents=mapped.gameObject.GetComponents(sourceComponent.GetType());
                if(index<0||index>=mappedComponents.Length)throw new InvalidOperationException("prior local prefab component unavailable: "+field);return mappedComponents[index];
            }
            throw new InvalidOperationException("prior local prefab object unsupported: "+field);
        }
        private static JObject BaselineObject(JToken state,string field)
        {
            return state as JObject??throw new InvalidOperationException("prior baseline shape missing: "+field);
        }
        private static void RestoreFieldState(SerializedProperty property,JToken state,GameObject prefabContents=null,string prefabPath=null)
        {
            if(Sensitive(property.name))throw new InvalidOperationException("opaque field rollback forbidden: "+property.propertyPath);
            if(property.isArray&&property.propertyType!=SerializedPropertyType.String) {
                var values=state as JArray??throw new InvalidOperationException("prior array baseline missing: "+property.propertyPath);
                property.arraySize=values.Count;for(var index=0;index<values.Count;index++)RestoreFieldState(property.GetArrayElementAtIndex(index),values[index],prefabContents,prefabPath);return;
            }
            var data=BaselineObject(state,property.propertyPath);
            switch(property.propertyType) {
                case SerializedPropertyType.Integer:case SerializedPropertyType.Enum:case SerializedPropertyType.LayerMask:
                    if((string)data["kind"]!="integer"||data["value"]?.Type!=JTokenType.Integer)throw new InvalidOperationException("prior integer baseline invalid: "+property.propertyPath);property.longValue=(long)data["value"];return;
                case SerializedPropertyType.Float:
                    if((string)data["kind"]!="float"||data["value"]==null)throw new InvalidOperationException("prior float baseline invalid: "+property.propertyPath);property.doubleValue=(double)data["value"];return;
                case SerializedPropertyType.Boolean:
                    if((string)data["kind"]!="boolean"||data["value"]?.Type!=JTokenType.Boolean)throw new InvalidOperationException("prior boolean baseline invalid: "+property.propertyPath);property.boolValue=(bool)data["value"];return;
                case SerializedPropertyType.ObjectReference:
                    if((string)data["kind"]!="object"||data["identity"]?.Type!=JTokenType.String)throw new InvalidOperationException("prior object baseline invalid: "+property.propertyPath);
                    var identity=(string)data["identity"];
                    if(identity=="null")property.objectReferenceValue=null;
                    else {
                        if(!GlobalObjectId.TryParse(identity,out var objectId))throw new InvalidOperationException("prior object identity invalid: "+property.propertyPath);var value=GlobalObjectId.GlobalObjectIdentifierToObjectSlow(objectId);if(value==null)throw new InvalidOperationException("prior object identity unavailable: "+property.propertyPath);
                        if(prefabContents!=null&&AssetDatabase.GetAssetPath(value)==prefabPath)value=RemapPrefabObject(value,prefabContents,property.propertyPath);
                        property.objectReferenceValue=value;
                    }
                    return;
                case SerializedPropertyType.Bounds:
                    if((string)data["kind"]!="bounds"||data.Count!=2)throw new InvalidOperationException("prior Bounds baseline invalid: "+property.propertyPath);
                    property.boundsValue=ParseBounds(data["value"]);return;
                case SerializedPropertyType.Vector2:case SerializedPropertyType.Vector3:case SerializedPropertyType.Vector4:
                    foreach(var name in new[]{"x","y","z","w"}) {var child=property.FindPropertyRelative(name);if(child!=null)RestoreFieldState(child,data[name],prefabContents,prefabPath);}return;
                case SerializedPropertyType.Generic:
                    foreach(var item in data.Properties()) {var child=property.FindPropertyRelative(item.Name)??throw new InvalidOperationException("prior nested baseline field absent: "+property.propertyPath+"."+item.Name);RestoreFieldState(child,item.Value,prefabContents,prefabPath);}return;
                default:throw new InvalidOperationException("prior baseline type unsupported: "+property.propertyType);
            }
        }
        private static OwnedRollbackPlan BuildRollbackPlan(GdaiPlayableContract c,GdaiPlayableAssetsManifest prior)
        {
            if(prior==null)return null;
            var plan=new OwnedRollbackPlan {prior=prior};
            foreach(var host in c.source_bound_plan.objects) {
                Object target=host.kind=="scene"?SceneMatches(host.name).SingleOrDefault():AssetDatabase.LoadAssetAtPath<Object>(host.asset_path);
                if(target==null)continue;
                var state=new HostRollbackState();plan.hosts.Add(host.id,state);
                if(target is GameObject go) {
                    state.active=go.activeSelf;state.aspectLock=go.GetComponent<GdaiCameraAspectLock>()!=null;
                    var marker=go.GetComponent<GdaiGeneratedPlayableMarker>();state.marker=new MarkerRollbackState {present=marker!=null,profile=marker?.profileId,snapshot=marker?.snapshotId,role=marker?.ownedRole};
                    foreach(var component in host.components)if(go.GetComponent(Resolve(component,host.kind))!=null)state.components.Add(component.id);
                }else foreach(var component in host.components)state.components.Add(component.id);
            }
            return plan;
        }
        private static void RestorePriorOwnedState(GdaiPlayableContract c,OwnedRollbackPlan plan,UnityEngine.SceneManagement.Scene canonicalScene,FieldMergePlan mergePlan)
        {
            var objects=ReadObjects(c);
            foreach(var host in c.source_bound_plan.objects.Where(host=>plan.hosts.ContainsKey(host.id))) {
                var state=plan.hosts[host.id];GameObject contents=null;Object target=objects[host.id];
                try {
                    if(host.kind=="prefab") {contents=PrefabUtility.LoadPrefabContents(host.asset_path);target=contents;}
                    foreach(var component in host.components.Where(component=>state.components.Contains(component.id))) {
                        var type=Resolve(component,host.kind);var instance=target is GameObject go?go.GetComponent(type):target;
                        if(instance==null)throw new InvalidOperationException("prior owned component unavailable: "+host.id+"/"+component.id);
                        using(var serialized=new SerializedObject(instance)) {
                            foreach(var field in component.fields) {
                                if(mergePlan!=null&&!mergePlan.WasWritten(host.id,component.id,field.path))continue;
                                var baseline=plan.prior.source_bound_field_baselines.Where(item=>item.host_id==host.id&&item.component_id==component.id&&item.field==field.path&&item.type==component.type&&item.source_path==component.source_path).ToList();
                                if(baseline.Count!=1)throw new InvalidOperationException("prior rollback baseline missing/ambiguous: "+host.id+"/"+component.id+"/"+field.path);
                                if(baseline[0].policy=="preserve_external") {if(baseline[0].state!=null)throw new InvalidOperationException("opaque rollback baseline contains state: "+field.path);continue;}
                                if(baseline[0].state==null)throw new InvalidOperationException("prior rollback state missing: "+field.path);
                                var property=serialized.FindProperty(field.path)??throw new InvalidOperationException("prior rollback field absent: "+field.path);
                                var fieldType=component.source_path=="builtin"?NativeType(property):Field(instance.GetType(),field.path).FieldType;
                                var actualState=mergePlan?.RollbackState(host.id,component.id,field.path);
                                if(actualState!=null)RestoreFieldState(property,actualState,contents,host.kind=="prefab"?host.asset_path:null);
                                else if(baseline[0].policy=="declared_expected")Value(property,fieldType,baseline[0].state,c,objects,host,target,true);
                                else RestoreFieldState(property,baseline[0].state,contents,host.kind=="prefab"?host.asset_path:null);
                            }
                            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(instance);
                        }
                    }
                    if(target is GameObject owned) {
                        foreach(var geometry in plan.prior.source_bound_field_baselines.Where(item=>item.host_id==host.id&&item.source_path=="__implicit_geometry")) {
                            if(geometry.state==null)throw new InvalidOperationException("prior geometry rollback state missing: "+host.id+"/"+geometry.field);
                            if(geometry.type=="UnityEngine.Transform") {var vector=new Vector3((float)geometry.state["x"],(float)geometry.state["y"],(float)geometry.state["z"]);if(geometry.field=="position")owned.transform.position=vector;else owned.transform.localScale=vector;}
                            else {var camera=owned.GetComponent<Camera>()??throw new InvalidOperationException("prior rollback camera unavailable: "+host.id);if(geometry.field=="orthographic")camera.orthographic=(bool)geometry.state;else if(geometry.field=="orthographicSize")camera.orthographicSize=(float)geometry.state;else if(state.aspectLock)(owned.GetComponent<GdaiCameraAspectLock>()??throw new InvalidOperationException("prior aspect lock unavailable")).SetTargetAspect((float)geometry.state);else camera.aspect=(float)geometry.state;}
                        }
                        foreach(var component in host.components.Where(component=>!state.components.Contains(component.id)).Reverse()) {var added=owned.GetComponent(Resolve(component,host.kind));if(added!=null)Object.DestroyImmediate(added);}
                        var aspectLock=owned.GetComponent<GdaiCameraAspectLock>();if(!state.aspectLock&&aspectLock!=null)Object.DestroyImmediate(aspectLock);
                        var marker=owned.GetComponent<GdaiGeneratedPlayableMarker>();
                        if(state.marker.present) {if(marker==null)marker=owned.AddComponent<GdaiGeneratedPlayableMarker>();marker.profileId=state.marker.profile;marker.snapshotId=state.marker.snapshot;marker.ownedRole=state.marker.role;}
                        else if(marker!=null)Object.DestroyImmediate(marker);
                        owned.SetActive(state.active);EditorUtility.SetDirty(owned);
                    }
                    if(contents!=null&&PrefabUtility.SaveAsPrefabAsset(contents,host.asset_path)==null)throw new InvalidOperationException("prior prefab rollback save failed: "+host.asset_path);
                }finally {if(contents!=null)PrefabUtility.UnloadPrefabContents(contents);}
            }
            AssetDatabase.SaveAssets();
            if(canonicalScene.IsValid()&&canonicalScene.isLoaded&&!EditorSceneManager.SaveScene(canonicalScene,c.canonical_scene.path))throw new InvalidOperationException("prior canonical rollback save failed");
        }
        private static void AdmitExistingFields(GdaiPlayableContract c,GdaiPlayableAssetsManifest prior,FieldMergePlan plan)
        {
            var existing=new Dictionary<string,Object>();
            foreach(var host in c.source_bound_plan.objects) {
                Object target=host.kind=="scene"?SceneMatches(host.name).SingleOrDefault():AssetDatabase.LoadAssetAtPath<Object>(host.asset_path);
                if(target!=null)existing.Add(host.id,target);
            }
            if(existing.Count>0&&(prior==null||prior.source_bound_baseline_revision!=c.source_bound_plan.revision||prior.source_bound_field_baselines==null))throw new InvalidOperationException("safe second-sync requires prior validated field baseline; old manifest needs authorized readback/migration, no values adopted");
            if(existing.Count>0)AdmitGeometry(c,prior,existing);
            foreach(var host in c.source_bound_plan.objects)foreach(var comp in host.components) {
                if(!existing.ContainsKey(host.id)) {foreach(var field in comp.fields.Where(field=>field.disposition!="preserve_external"))plan.AdmitInitialWrite(host.id,comp.id,field.path);continue;}
                var target=existing[host.id];var type=Resolve(comp,host.kind);var instance=target is GameObject go?go.GetComponent(type):target;
                if(instance==null){if(prior.source_bound_field_baselines.Any(b=>b.host_id==host.id&&b.component_id==comp.id))throw new InvalidOperationException("user removed prior component: "+comp.id);foreach(var field in comp.fields.Where(field=>field.disposition!="preserve_external"))plan.AdmitInitialWrite(host.id,comp.id,field.path);continue;}
                CheckCoverage(instance,comp,c.source_bound_plan.revision);
                plan.updateComponents.Add(host.id+"|"+comp.id);
                using(var so=new SerializedObject(instance))foreach(var field in comp.fields) {
                    var baseline=prior.source_bound_field_baselines.Where(b=>b.host_id==host.id&&b.component_id==comp.id&&b.field==field.path&&b.type==comp.type&&b.source_path==comp.source_path).ToList();
                    if(baseline.Count!=1)throw new InvalidOperationException("prior field baseline missing/ambiguous: "+host.id+"/"+comp.id+"/"+field.path);
                    if(field.disposition=="preserve_external") {if(baseline[0].policy!="preserve_external"||baseline[0].state!=null)throw new InvalidOperationException("opaque preservation baseline mismatch: "+host.id+"/"+comp.id+"/"+field.path);continue;}
                    var property=so.FindProperty(field.path)??throw new InvalidOperationException("prior serialized field absent: "+field.path);
                    var fieldType=comp.source_path=="builtin"?NativeType(property):Field(type,field.path).FieldType;
                    if(c.source_bound_plan.revision==2&&DeclaredNativeEqualityShape(fieldType,new HashSet<Type>())) {
                        if(baseline[0].policy!="declared_expected"||baseline[0].state==null)throw new InvalidOperationException("declared baseline missing: "+host.id+"/"+comp.id+"/"+field.path);
                        var priorField=new GdaiSourceBoundPlayablePlan.Field {path=field.path,disposition="value",value=baseline[0].state};
                        var matchesPrior=MatchesDeclaredProperty(instance,property,fieldType,priorField,c,existing,host,target);var matchesDeclaredNew=MatchesDeclaredProperty(instance,property,fieldType,field,c,existing,host,target);
                        var creatorUnchanged=JToken.DeepEquals(baseline[0].state,field.value);
                        var declaredDecision=matchesDeclaredNew?FieldUpdateDecision.AlreadyApplied:creatorUnchanged?FieldUpdateDecision.PreserveUserEdit:matchesPrior?FieldUpdateDecision.CreatorUpdate:FieldUpdateDecision.Conflict;
                        if(declaredDecision==FieldUpdateDecision.Conflict)throw new InvalidOperationException("user field conflict before mutation: "+host.id+"/"+comp.id+"/"+field.path);
                        plan.Add(host.id,comp.id,field.path,declaredDecision,instance,property,host.kind=="prefab"?target as GameObject:null,host.kind=="prefab"?host.asset_path:null);
                        if(field.disposition=="source_default"&&!matchesDeclaredNew)VerifyFreshDefault(type,comp,field,c,existing,host);
                        continue;
                    }
                    var live=FieldState(property);var expected=ExpectedFieldState(type,comp,field,c,existing,host,target);
                    var fieldDecision=DecideFieldUpdate(baseline[0].state,live,expected,true);
                    if(fieldDecision==FieldUpdateDecision.Conflict)throw new InvalidOperationException("user field conflict before mutation: "+host.id+"/"+comp.id+"/"+field.path);
                    plan.Add(host.id,comp.id,field.path,fieldDecision,instance,property,host.kind=="prefab"?target as GameObject:null,host.kind=="prefab"?host.asset_path:null,live);
                    if(field.disposition=="source_default"&&!JToken.DeepEquals(live,expected))VerifyFreshDefault(type,comp,field,c,existing,host);
                }
            }
        }
        private static JToken ExpectedFieldState(Type type,GdaiSourceBoundPlayablePlan.ComponentSpec comp,GdaiSourceBoundPlayablePlan.Field field,GdaiPlayableContract c,Dictionary<string,Object> objects,GdaiSourceBoundPlayablePlan.Host host,Object self)
        {
            GameObject shell=null;Object prototype=null;
            try {
                if(typeof(ScriptableObject).IsAssignableFrom(type))prototype=ScriptableObject.CreateInstance(type);
                else {shell=new GameObject("GDAI_ExpectedFieldState");shell.SetActive(false);prototype=shell.AddComponent(type);}
                var single=new GdaiSourceBoundPlayablePlan.ComponentSpec {id=comp.id,source_path=comp.source_path,type=comp.type,fields=new List<GdaiSourceBoundPlayablePlan.Field>{field}};
                BindFieldsCore(prototype,single,c,objects,host,self,true,true,null);
                using(var serialized=new SerializedObject(prototype)){var property=serialized.FindProperty(field.path)??throw new InvalidOperationException("expected field absent: "+field.path);return FieldState(property);}
            } finally {if(shell!=null)Object.DestroyImmediate(shell);else if(prototype!=null)Object.DestroyImmediate(prototype);}
        }
        private static void VerifyFreshDefault(Type type,GdaiSourceBoundPlayablePlan.ComponentSpec comp,GdaiSourceBoundPlayablePlan.Field field,GdaiPlayableContract c,Dictionary<string,Object> objects,GdaiSourceBoundPlayablePlan.Host host)
        {
            GameObject shell=null;Object prototype=null;
            try {
                if(typeof(ScriptableObject).IsAssignableFrom(type))prototype=ScriptableObject.CreateInstance(type);
                else {shell=new GameObject("GDAI_DefaultVerification");shell.SetActive(false);prototype=shell.AddComponent(type);}
                var single=new GdaiSourceBoundPlayablePlan.ComponentSpec {source_path=comp.source_path,type=comp.type,fields=new List<GdaiSourceBoundPlayablePlan.Field>{field}};BindFields(prototype,single,c,objects,host,prototype,false);
            }finally {if(shell!=null)Object.DestroyImmediate(shell);else if(prototype!=null)Object.DestroyImmediate(prototype);}
        }
        public static List<GdaiSourceBoundFieldBaseline> CaptureSavedBaselines(GdaiPlayableContract c)
        {
            var admission=GdaiSourceBoundPlayablePlan.Validate(c);if(admission.Count>0)throw new InvalidOperationException(string.Join(";",admission));
            foreach(var component in c.source_bound_plan.objects.SelectMany(h=>h.components))GdaiSourceBoundPlayablePlan.RequireSafeFields(component.fields,c.source_bound_plan.revision);
            var result=new List<GdaiSourceBoundFieldBaseline>();var objects=ReadObjects(c);
            foreach(var host in c.source_bound_plan.objects)foreach(var comp in host.components) {
                var target=objects[host.id];var instance=target is GameObject go?go.GetComponent(Resolve(comp,host.kind)):target;
                using(var so=new SerializedObject(instance))foreach(var field in comp.fields) {
                    if(field.disposition=="preserve_external") {result.Add(new GdaiSourceBoundFieldBaseline {host_id=host.id,component_id=comp.id,source_path=comp.source_path,type=comp.type,field=field.path,policy="preserve_external"});continue;}
                    var property=so.FindProperty(field.path);if(property==null)throw new InvalidOperationException("missing baseline field: "+field.path);
                    var fieldType=comp.source_path=="builtin"?NativeType(property):Field(instance.GetType(),field.path).FieldType;
                    if(c.source_bound_plan.revision==2&&DeclaredNativeEqualityShape(fieldType,new HashSet<Type>()))result.Add(new GdaiSourceBoundFieldBaseline {host_id=host.id,component_id=comp.id,source_path=comp.source_path,type=comp.type,field=field.path,policy="declared_expected",state=field.value.DeepClone()});
                    else {if(property.propertyType==SerializedPropertyType.String)throw new InvalidOperationException("missing/unsupported baseline field: "+field.path);result.Add(new GdaiSourceBoundFieldBaseline {host_id=host.id,component_id=comp.id,source_path=comp.source_path,type=comp.type,field=field.path,state=ExpectedFieldState(instance.GetType(),comp,field,c,objects,host,target)});}
                }
            }
            foreach(var w in ImplicitGeometryWrites(c))result.Add(new GdaiSourceBoundFieldBaseline {host_id=w.host_id,component_id=w.component_id,type=w.type,source_path="__implicit_geometry",field=w.field,state=GeometryState((GameObject)objects[w.host_id],w)});
            return result;
        }
        public static void VerifyCompiledObligations(GdaiPlayableContract c)
        {
            foreach(var obligation in c.source_bound_obligations) {
                var type=GdaiSourceBoundPlayablePlan.CompiledType(obligation.type);if(type==null)throw new InvalidOperationException("compiled obligation missing: "+obligation.type);
                foreach(var field in obligation.fields) {var compiled=Field(type,field.path);if(field.sensitive!=Sensitive(compiled.Name))throw new InvalidOperationException("compiled sensitive policy mismatch: "+obligation.type+"."+field.path);if(field.sensitive) {if(c.source_bound_plan.revision!=2)throw new InvalidOperationException("sensitive compiled field unsupported");continue;}if(c.source_bound_plan.revision==1)RequireBaselineShape(compiled.FieldType);else RequireRevision2Shape(compiled.FieldType,new HashSet<Type>());}
                foreach(var attribute in type.GetCustomAttributes(typeof(RequireComponent),true).Cast<RequireComponent>()) {
                    foreach(var required in new[]{attribute.m_Type0,attribute.m_Type1,attribute.m_Type2}.Where(t=>t!=null))
                        foreach(var host in c.source_bound_plan.objects.Where(o=>o.components.Any(comp=>comp.type==type.FullName)))
                            if(!host.components.Any(comp=>required.IsAssignableFrom(Resolve(comp,host.kind))))throw new InvalidOperationException("required component omitted: "+type.FullName+" -> "+required.FullName);
                }
            }
        }
        public static Dictionary<string,Object> ReadObjects(GdaiPlayableContract c) => ReadObjects(c,UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        private static Dictionary<string,Object> ReadObjects(GdaiPlayableContract c,UnityEngine.SceneManagement.Scene scene)
        {
            var d=new Dictionary<string,Object>();
            foreach(var host in c.source_bound_plan.objects) {
                Object target;
                if(host.kind=="scene") {var matches=SceneMatches(scene,host.name);if(matches.Count!=1)throw new InvalidOperationException("saved host missing/ambiguous");target=matches[0];}
                else target=AssetDatabase.LoadAssetAtPath<Object>(host.asset_path);
                if(target==null)throw new InvalidOperationException("saved asset missing");d.Add(host.id,target);
            }return d;
        }
        public static void BindFields(Object instance,GdaiSourceBoundPlayablePlan.ComponentSpec comp,GdaiPlayableContract c,Dictionary<string,Object> objects,GdaiSourceBoundPlayablePlan.Host host,Object self,bool write,bool updateSourceDefaults=false)
            =>BindFieldsCore(instance,comp,c,objects,host,self,write,updateSourceDefaults,null);
        private static void BindFieldsCore(Object instance,GdaiSourceBoundPlayablePlan.ComponentSpec comp,GdaiPlayableContract c,Dictionary<string,Object> objects,GdaiSourceBoundPlayablePlan.Host host,Object self,bool write,bool updateSourceDefaults,FieldMergePlan mergePlan)
        {
            var revision=c?.source_bound_plan?.revision??1;GdaiSourceBoundPlayablePlan.RequireSafeFields(comp.fields,revision);
            if(instance==null)throw new InvalidOperationException("planned component missing");
            if(comp.source_path!="builtin"&&revision==1&&SerializedTypeFields(instance.GetType()).Any(f=>Sensitive(f.Name)))throw new InvalidOperationException("sensitive serialized configuration unsupported");
            using(var so=new SerializedObject(instance)) {
                foreach(var f in comp.fields) {
                    if(f.disposition=="preserve_external")continue;
                    var property=so.FindProperty(f.path);if(property==null)throw new InvalidOperationException("field missing: "+f.path);
                    var decision=mergePlan?.Decision(host.id,comp.id,f.path)??FieldUpdateDecision.CreatorUpdate;
                    if(mergePlan!=null&&write&&!ShouldApplyFieldUpdate(decision)){if(decision==FieldUpdateDecision.PreserveUserEdit)mergePlan.VerifyRetained(host.id,comp.id,f.path,property,host.kind=="prefab"?self as GameObject:null,host.kind=="prefab"?host.asset_path:null);continue;}
                    if(mergePlan!=null&&!write&&decision==FieldUpdateDecision.PreserveUserEdit){mergePlan.VerifyRetained(host.id,comp.id,f.path,property,host.kind=="prefab"?self as GameObject:null,host.kind=="prefab"?host.asset_path:null);continue;}
                    var expectedType=comp.source_path=="builtin"?NativeType(property):Field(instance.GetType(),f.path).FieldType;
                    if(mergePlan!=null&&write)mergePlan.MarkWriteStarted(host.id,comp.id,f.path);
                    if(!write&&revision==2&&DeclaredNativeEqualityShape(expectedType,new HashSet<Type>())) {if(!MatchesDeclaredProperty(instance,property,expectedType,f,c,objects,host,self))throw new InvalidOperationException("declared text/curve readback mismatch: "+f.path);continue;}
                    if(f.disposition=="reference") {
                        var target=Reference(f.reference_kind,f.target_id,f.target_component,c,objects,host,self);
                        ObjectReference(property,expectedType,target,write);
                    }else if(f.disposition=="optional_null") {
                        if(property.propertyType!=SerializedPropertyType.ObjectReference)throw new InvalidOperationException("optional-null only supported for serialized object refs");
                        if(write)property.objectReferenceValue=null;
                        else if(property.objectReferenceValue!=null)throw new InvalidOperationException("optional-null readback mismatch");
                    }else if(f.disposition=="value"||f.disposition=="source_default") {
                        // Defaults are independently verified; never overwritten to manufacture source-default proof.
                        Value(property,expectedType,f.value,c,objects,host,self,write&&(f.disposition=="value"||updateSourceDefaults));
                    }else throw new InvalidOperationException("unresolved disposition");
                }
                if(write) {so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(instance);}
            }
            if(write)BindFieldsCore(instance,comp,c,objects,host,self,false,false,mergePlan);
        }
        private static bool DeclaredNativeEqualityShape(Type type,HashSet<Type> seen) {
            if(type==typeof(string)||type==typeof(AnimationCurve))return true;var inner=ElementType(type);if(inner!=null)return DeclaredNativeEqualityShape(inner,seen);
            if(type.IsPrimitive||type.IsEnum||typeof(Object).IsAssignableFrom(type)||type==typeof(Bounds)||type==typeof(Vector2)||type==typeof(Vector3)||type==typeof(Vector4)||type==typeof(LayerMask))return false;
            if(!type.IsSerializable||seen.Contains(type))return false;var next=new HashSet<Type>(seen){type};return SerializedTypeFields(type,2).Any(f=>DeclaredNativeEqualityShape(f.FieldType,next));
        }
        private static bool MatchesDeclaredProperty(Object instance,SerializedProperty live,Type type,GdaiSourceBoundPlayablePlan.Field field,GdaiPlayableContract c,Dictionary<string,Object> objects,GdaiSourceBoundPlayablePlan.Host host,Object self) {
            GameObject shell=null;Object prototype=null;
            try {
                if(instance is ScriptableObject)prototype=ScriptableObject.CreateInstance(instance.GetType());
                else {shell=new GameObject("GDAI_DeclaredComparison");shell.SetActive(false);prototype=shell.AddComponent(instance.GetType());}
                using(var expected=new SerializedObject(prototype)) {var property=expected.FindProperty(field.path)??throw new InvalidOperationException("declared comparison field missing: "+field.path);Value(property,type,field.value,c,objects,host,self,true);return SerializedProperty.DataEquals(live,property);}
            }finally {if(shell!=null)Object.DestroyImmediate(shell);else if(prototype!=null)Object.DestroyImmediate(prototype);}
        }
        private static Type NativeType(SerializedProperty p)
        {
            switch(p.propertyType) {
                case SerializedPropertyType.ObjectReference:
                    if(p.name=="m_Sprite")return typeof(Sprite);
                    if(p.name=="m_Material")return typeof(Material);
                    throw new InvalidOperationException("unsupported native reference field: "+p.name);
                case SerializedPropertyType.Bounds:return typeof(Bounds);
                case SerializedPropertyType.Vector2:return typeof(Vector2);
                case SerializedPropertyType.Vector3:return typeof(Vector3);
                case SerializedPropertyType.Vector4:return typeof(Vector4);
                case SerializedPropertyType.Integer:return typeof(long);
                case SerializedPropertyType.Float:return typeof(double);
                case SerializedPropertyType.Boolean:return typeof(bool);
                case SerializedPropertyType.String:return typeof(string);
                case SerializedPropertyType.AnimationCurve:return typeof(AnimationCurve);
                default:throw new InvalidOperationException("unsupported explicit native configuration: "+p.name);
            }
        }
        private static Object Reference(string kind,string id,string component,GdaiPlayableContract c,Dictionary<string,Object> objects,GdaiSourceBoundPlayablePlan.Host host,Object self)
        {
            if(kind=="asset") {
                var entries=c.source_bound_context["assets"].Where(a=>(string)a["asset_id"]==id).ToList();if(entries.Count!=1)throw new InvalidOperationException("adopted asset identity missing");
                var path=(string)entries[0]["unity_path"];
                var registry=GdaiImportedAssetRegistry.All().Where(a=>a.asset_id==id&&a.unity_path==path).ToList();
                if(registry.Count!=1)throw new InvalidOperationException("adopted asset imported registry mismatch");
                var expected=entries[0];var imported=registry[0];
                string expectedType=(string)expected["asset_type"],expectedMime=(string)expected["mime_type"],expectedProject=(string)expected["project_id"],expectedEntity=(string)expected["entity_id"],expectedCategory=(string)expected["audio_category"],expectedSlot=(string)expected["project_audio_asset_id"],expectedCandidate=(string)expected["audio_candidate_id"];
                if(expectedType=="audio"&&expectedCategory=="bgm"&&(string.IsNullOrEmpty(expectedProject)||!string.IsNullOrEmpty(expectedEntity)||expectedSlot!=id||string.IsNullOrEmpty(expectedCandidate)))throw new InvalidOperationException("adopted BGM expected lineage incomplete");
                if(expectedType!=null&&expectedType!=imported.asset_type||expectedMime!=null&&expectedMime!=imported.mime_type||expectedProject!=null&&expectedProject!=imported.source_project_id||expectedEntity!=null&&expectedEntity!=imported.entity_id||expectedCategory!=null&&expectedCategory!=imported.audio_category||expectedSlot!=null&&expectedSlot!=imported.project_audio_asset_id||expectedCandidate!=null&&expectedCandidate!=imported.audio_candidate_id)throw new InvalidOperationException("adopted asset imported lineage mismatch");
                if(GdaiImportedAssetRegistry.TryGetSpriteByAssetId(id,out var sprite,out _))return sprite;
                var asset=AssetDatabase.LoadAssetAtPath<Object>(path);if(asset==null)throw new InvalidOperationException("adopted asset unavailable");return asset;
            }
            var targets=c.source_bound_plan.objects.Where(o=>o.id==id).ToList();if(targets.Count!=1)throw new InvalidOperationException("owned reference identity missing");var targetHost=targets[0];
            if(host.kind!="scene"&&targetHost.kind=="scene")throw new InvalidOperationException("persistent asset cannot reference scene");
            var target=id==host.id?self:objects[id];
            if(kind=="component") {
                var specs=targetHost.components.Where(x=>x.id==component).ToList();if(specs.Count!=1)throw new InvalidOperationException("component ref identity missing");
                return target is GameObject go?go.GetComponent(Resolve(specs[0],targetHost.kind)):target;
            }
            if(kind=="transform")return (target as GameObject)?.transform??throw new InvalidOperationException("Transform host required");
            if(kind=="prefab"&&targetHost.kind!="prefab"||kind=="config"&&targetHost.kind!="config")throw new InvalidOperationException("reference hosting kind mismatch");
            return target;
        }
        private static void ObjectReference(SerializedProperty p,Type type,Object value,bool write)
        {
            if(p.propertyType!=SerializedPropertyType.ObjectReference||value==null||!type.IsInstanceOfType(value))throw new InvalidOperationException("reference assignability mismatch: "+p.propertyPath);
            if(write)p.objectReferenceValue=value;
            else if(p.objectReferenceValue!=value)throw new InvalidOperationException("reference identity readback mismatch: "+p.propertyPath);
        }
        private static void Value(SerializedProperty p,Type type,JToken v,GdaiPlayableContract c,Dictionary<string,Object> objects,GdaiSourceBoundPlayablePlan.Host host,Object self,bool write)
        {
            if(Sensitive(p.name))throw new InvalidOperationException("sensitive configuration unsupported");
            if(v==null)throw new InvalidOperationException("explicit value required");
            if(p.isArray&&p.propertyType!=SerializedPropertyType.String) {
                if(!(v is JArray array)||array.Count>256)throw new InvalidOperationException("bounded list required");
                var elementType=type.IsArray?type.GetElementType():type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(List<>)?type.GetGenericArguments()[0]:null;
                if(elementType==null)throw new InvalidOperationException("unsupported collection type");
                if(write)p.arraySize=array.Count;else if(p.arraySize!=array.Count)throw new InvalidOperationException("list readback length mismatch");
                for(int i=0;i<array.Count;i++)Value(p.GetArrayElementAtIndex(i),elementType,array[i],c,objects,host,self,write);return;
            }
            switch(p.propertyType) {
                case SerializedPropertyType.ObjectReference:
                    if(v.Type==JTokenType.Null)throw new InvalidOperationException("required nested object reference cannot be null: "+p.propertyPath);
                    if(!(v is JObject reference))throw new InvalidOperationException("typed reference value required");
                    ObjectReference(p,type,Reference((string)reference["reference_kind"],(string)reference["target_id"],(string)reference["target_component"],c,objects,host,self),write);return;
                case SerializedPropertyType.Boolean:
                    if(v.Type!=JTokenType.Boolean)throw new InvalidOperationException("bool required");if(write)p.boolValue=(bool)v;else if(p.boolValue!=(bool)v)throw new InvalidOperationException("bool readback mismatch");return;
                case SerializedPropertyType.Integer:
                    if(v.Type!=JTokenType.Integer)throw new InvalidOperationException("integer required");var integer=(long)v;if(write)p.longValue=integer;else if(p.longValue!=integer)throw new InvalidOperationException("integer readback mismatch");return;
                case SerializedPropertyType.Float:
                    if(v.Type!=JTokenType.Integer&&v.Type!=JTokenType.Float||double.IsNaN((double)v)||double.IsInfinity((double)v))throw new InvalidOperationException("finite float required");
                    if(write)p.doubleValue=(double)v;else if(Math.Abs(p.doubleValue-(double)v)>0.000001*Math.Max(1,Math.Abs((double)v)))throw new InvalidOperationException("float readback mismatch");return;
                case SerializedPropertyType.String:
                    if(v.Type!=JTokenType.String)throw new InvalidOperationException("string required");if(write)p.stringValue=(string)v;else if(p.stringValue!=(string)v)throw new InvalidOperationException("string readback mismatch");return;
                case SerializedPropertyType.AnimationCurve:
                    if(!(v is JObject curve)||!(curve["keys"] is JArray keys)||keys.Count>256)throw new InvalidOperationException("bounded AnimationCurve required");
                    var frames=new List<Keyframe>();foreach(var item in keys) {if(!(item is JObject key)||!new HashSet<string>(new[]{"time","value","in_tangent","out_tangent","in_weight","out_weight","weighted_mode"}).SetEquals(key.Properties().Select(x=>x.Name)))throw new InvalidOperationException("complete AnimationCurve key required");
                        var values=new[]{"time","value","in_tangent","out_tangent","in_weight","out_weight"}.Select(name=>(float)key[name]).ToArray();if(values.Any(x=>float.IsNaN(x)||float.IsInfinity(x)))throw new InvalidOperationException("finite AnimationCurve key required");if(!Enum.TryParse((string)key["weighted_mode"],out WeightedMode weighted))throw new InvalidOperationException("AnimationCurve weighted mode required");var frame=new Keyframe(values[0],values[1],values[2],values[3],values[4],values[5]) {weightedMode=weighted};frames.Add(frame);}
                    if(!Enum.TryParse((string)curve["pre_wrap_mode"],out WrapMode pre)||!Enum.TryParse((string)curve["post_wrap_mode"],out WrapMode post))throw new InvalidOperationException("AnimationCurve wrap modes required");var declaredCurve=new AnimationCurve(frames.ToArray()) {preWrapMode=pre,postWrapMode=post};if(write)p.animationCurveValue=declaredCurve;else throw new InvalidOperationException("AnimationCurve readback requires native declared comparison");return;
                case SerializedPropertyType.Enum:
                    if(v.Type!=JTokenType.String||!type.IsEnum||!Enum.IsDefined(type,(string)v))throw new InvalidOperationException("declared enum name required");
                    var ev=Convert.ToInt32(Enum.Parse(type,(string)v));if(write)p.intValue=ev;else if(p.intValue!=ev)throw new InvalidOperationException("enum readback mismatch");return;
                case SerializedPropertyType.LayerMask:
                    if(!(v is JArray layers)||layers.Any(x=>x.Type!=JTokenType.String))throw new InvalidOperationException("named layer list required");
                    int mask=0;foreach(var layer in layers){var index=LayerMask.NameToLayer((string)layer);if(index<0)throw new InvalidOperationException("undefined layer");mask|=1<<index;}
                    if(write)p.intValue=mask;else if(p.intValue!=mask)throw new InvalidOperationException("layer mask readback mismatch");return;
                case SerializedPropertyType.Bounds:
                    if(type!=typeof(Bounds))throw new InvalidOperationException("native Bounds type required");
                    var bounds=ParseBounds(v);
                    if(write)p.boundsValue=bounds;
                    else if(!p.boundsValue.center.Equals(bounds.center)||!p.boundsValue.extents.Equals(bounds.extents))throw new InvalidOperationException("Bounds readback mismatch");return;
                case SerializedPropertyType.Vector2: Vector(p,v,new[]{"x","y"},write);return;
                case SerializedPropertyType.Vector3: Vector(p,v,new[]{"x","y","z"},write);return;
                case SerializedPropertyType.Vector4: Vector(p,v,new[]{"x","y","z","w"},write);return;
                case SerializedPropertyType.Generic:
                    if(!(v is JObject data))throw new InvalidOperationException("serializable configuration object required");
                    var child=p.Copy();var names=new List<string>();var end=p.GetEndProperty();bool enter=true;
                    while(child.Next(enter)&&!SerializedProperty.EqualContents(child,end)){enter=false;if(child.depth==p.depth+1)names.Add(child.name);}
                    if(names.Count!=data.Count||!new HashSet<string>(names).SetEquals(data.Properties().Select(x=>x.Name)))throw new InvalidOperationException("complete nested configuration required");
                    foreach(var name in names){if(Sensitive(name))throw new InvalidOperationException("sensitive nested configuration unsupported");Value(p.FindPropertyRelative(name),Field(type,name).FieldType,data[name],c,objects,host,self,write);}return;
                default:throw new InvalidOperationException("unsupported serialized type: "+p.propertyType);
            }
        }
        // Bounds serializes center and extents as Single values. Never round-trip
        // through size: halving a subnormal size can silently change the value.
        private static Vector3 BoundsVector(JToken value) {
            var names=new[]{"x","y","z"};
            if(!(value is JObject data)||data.Count!=3||!new HashSet<string>(names).SetEquals(data.Properties().Select(item=>item.Name)))throw new InvalidOperationException("complete Bounds vector required");
            var values=new float[3];
            for(var i=0;i<names.Length;i++) {
                var token=data[names[i]];
                if(token.Type!=JTokenType.Integer&&token.Type!=JTokenType.Float)throw new InvalidOperationException("numeric Bounds coordinate required");
                var number=(double)token;var magnitude=Math.Abs(number);var converted=(float)number;
                if(double.IsNaN(number)||double.IsInfinity(number)||magnitude>(double)float.MaxValue||number!=0&&magnitude<(double)float.Epsilon||float.IsNaN(converted)||float.IsInfinity(converted)||number!=0&&converted==0)throw new InvalidOperationException("Single-compatible Bounds coordinate required");
                values[i]=converted;
            }
            return new Vector3(values[0],values[1],values[2]);
        }
        private static Bounds ParseBounds(JToken value) {
            if(!(value is JObject data)||data.Count!=2||!new HashSet<string>(new[]{"center","extents"}).SetEquals(data.Properties().Select(item=>item.Name)))throw new InvalidOperationException("complete Bounds center/extents required");
            return new Bounds {center=BoundsVector(data["center"]),extents=BoundsVector(data["extents"])};
        }
        private static JObject BoundsState(Bounds value) {
            JObject VectorData(Vector3 vector)=>new JObject { ["x"]=(double)vector.x,["y"]=(double)vector.y,["z"]=(double)vector.z };
            var state=new JObject { ["center"]=VectorData(value.center),["extents"]=VectorData(value.extents) };
            ParseBounds(state);return state;
        }
        private static void Vector(SerializedProperty p,JToken v,string[] names,bool write)
        {
            if(!(v is JObject o)||o.Count!=names.Length||!new HashSet<string>(names).SetEquals(o.Properties().Select(x=>x.Name)))throw new InvalidOperationException("complete vector required");
            foreach(var name in names) {var value=(double)o[name];if(double.IsNaN(value)||double.IsInfinity(value))throw new InvalidOperationException("finite vector required");var child=p.FindPropertyRelative(name);if(write)child.floatValue=(float)value;else if(Math.Abs(child.floatValue-value)>0.000001*Math.Max(1,Math.Abs(value)))throw new InvalidOperationException("vector readback mismatch");}
        }
    }
}
