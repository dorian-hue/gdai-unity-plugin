using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GDAI.Bridge.Editor.Tests
{
    // Diagnostics only. Coordinator supplies the unchanged native fixture harness
    // in an explicitly marked disposable project. This is not S0 Play acceptance.
    public class GdaiBoundsSerializationTests
    {
        private const string HostPath="Assets/GDAI_Generated/SourceBoundTests/BoundsNativeHost.cs";
        private const string WorldPath="Assets/GDAI_Generated/SourceBoundTests/WorldBlocker.cs";
        private const string Source=@"using UnityEngine;
public class BoundsNativeHost : MonoBehaviour {
    public Bounds region = new Bounds(Vector3.one, new Vector3(2,4,6));
    public Bounds defaultRegion;
    public Bounds[] regions = new Bounds[0];
    public System.Collections.Generic.List<BoundsNativeEntry> entries = new System.Collections.Generic.List<BoundsNativeEntry>();
}
[System.Serializable] public class BoundsNativeEntry { public Bounds box; }
";
        private static JObject Vector(float x,float y,float z)=>new JObject { ["x"]=(double)x,["y"]=(double)y,["z"]=(double)z };
        private static JObject Box(float offset=1)=>new JObject { ["center"]=Vector(offset,offset,offset),["extents"]=Vector(1,2,3) };
        private static GdaiSourceBoundPlayablePlan.Field Value(string path,JToken value,string disposition="value")=>new GdaiSourceBoundPlayablePlan.Field {path=path,disposition=disposition,value=value,rationale="native diagnostic declared default"};
        private static GdaiSourceBoundPlayablePlan.ComponentSpec Spec(string id)=>new GdaiSourceBoundPlayablePlan.ComponentSpec {
            id=id,type="BoundsNativeHost",source_path=HostPath,fields=new List<GdaiSourceBoundPlayablePlan.Field> {
                Value("region",Box()),Value("defaultRegion",new JObject {["center"]=Vector(0,0,0),["extents"]=Vector(0,0,0)},"source_default"),
                Value("regions",new JArray(Box(2),Box(3))),Value("entries",new JArray(new JObject {["box"]=Box(4)}))
            }
        };
        private static Type HostType=>GdaiSourceBoundPlayablePlan.CompiledType("BoundsNativeHost")??throw new InvalidOperationException("diagnostic host not compiled");
        [SetUp] public void RequireDisposableProjectAndResetOwnedState() {
            var marker=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Library/GDAI/native-test-scratch.marker");
            if(!File.Exists(marker)||File.ReadAllText(marker)!="gdai-disposable-sdk-native-test-v1")throw new InvalidOperationException("Bounds tests require marked disposable project");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            AssetDatabase.DeleteAsset("Assets/GDAI_Project/Generated");AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");
        }
        [UnitySetUp] public IEnumerator PrepareCompiledFixtures() {
            var changed=GdaiSourceBoundPlayableFixtures.WriteSourceFiles();
            Directory.CreateDirectory(Path.GetDirectoryName(HostPath));
            if(!File.Exists(HostPath)||File.ReadAllText(HostPath)!=Source){File.WriteAllText(HostPath,Source);changed=true;}
            // Byte-exact physical output of the actual six-source materializer,
            // supplied by the bounded offline evidence; no gameplay source edits.
            var frozen=Environment.GetEnvironmentVariable("GDAI_BOUNDS_WORLD_BLOCKER_SOURCE");
            if(string.IsNullOrEmpty(frozen))throw new InvalidOperationException("Coordinator must supply GDAI_BOUNDS_WORLD_BLOCKER_SOURCE from frozen physical evidence");
            var source=File.ReadAllBytes(frozen);
            if(!File.Exists(WorldPath)||!File.ReadAllBytes(WorldPath).SequenceEqual(source)){File.WriteAllBytes(WorldPath,source);changed=true;}
            AssetDatabase.Refresh();if(changed)yield return new RecompileScripts();
            GdaiSourceBoundPlayableFixtures.WriteGenericAssets();
        }
        private static GdaiPlayableContract Contract() {
            var c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_plan.revision=2;
            c.source_bound_plan.sources.Add(new GdaiSourceBoundPlayablePlan.Source {path=HostPath,sha256=GdaiSourceBoundPlayablePlan.Hash(File.ReadAllBytes(HostPath))});
            c.source_bound_obligations.Add(new GdaiSourceBoundPlayablePlan.TypeObligation {type="BoundsNativeHost",source_path=HostPath,kind="class",hosting="component",bases=new List<string>{"UnityEngine.MonoBehaviour"},fields=new List<GdaiSourceBoundPlayablePlan.FieldObligation>{
                new GdaiSourceBoundPlayablePlan.FieldObligation {path="region",type="UnityEngine.Bounds"},new GdaiSourceBoundPlayablePlan.FieldObligation {path="defaultRegion",type="UnityEngine.Bounds"},new GdaiSourceBoundPlayablePlan.FieldObligation {path="regions",type="UnityEngine.Bounds[]"},new GdaiSourceBoundPlayablePlan.FieldObligation {path="entries",type="List<BoundsNativeEntry>"}
            }});
            c.source_bound_obligations.Add(new GdaiSourceBoundPlayablePlan.TypeObligation {type="BoundsNativeEntry",source_path=HostPath,kind="class",hosting="none",serializable=true,bases=new List<string>(),fields=new List<GdaiSourceBoundPlayablePlan.FieldObligation>{new GdaiSourceBoundPlayablePlan.FieldObligation {path="box",type="UnityEngine.Bounds"}}});
            c.source_bound_plan.type_dispositions.Add(new GdaiSourceBoundPlayablePlan.TypeDisposition {type="BoundsNativeHost",source_path=HostPath,disposition="hosted"});
            c.source_bound_plan.type_dispositions.Add(new GdaiSourceBoundPlayablePlan.TypeDisposition {type="BoundsNativeEntry",source_path=HostPath,disposition="data",rationale="reachable Bounds configuration"});
            c.source_bound_plan.objects.Single(o=>o.id=="pilot").components.Add(Spec("scene-bounds"));
            c.source_bound_plan.objects.First(o=>o.kind=="prefab").components.Add(Spec("prefab-bounds"));return c;
        }
        private static GdaiSourceBoundPlayablePlan.ComponentSpec SceneSpec(GdaiPlayableContract c)=>c.source_bound_plan.objects.SelectMany(o=>o.components).Single(s=>s.id=="scene-bounds");
        private static GdaiSourceBoundPlayablePlan.Field Region(GdaiSourceBoundPlayablePlan.ComponentSpec s)=>s.fields.Single(f=>f.path=="region");
        private static GdaiPlayableComposerCta.Result Compose(GdaiPlayableContract c,string snapshot,Action lateFailure=null) {
            GdaiSourceBoundPlayableFixtures.WriteImportedContract(c);
            var result=GdaiSourceBoundPlayableComposer.Run(c,"generic-project",snapshot,GdaiSourceBoundPlayablePlan.Hash(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(c))),"Assets/Scenes/Main.unity",DateTime.UtcNow,lateFailure);
            return result;
        }
        private static void Pass(GdaiPlayableComposerCta.Result result){Assert.IsTrue(result.Completed,result.Error);Assert.IsNotNull(result.Receipt);Assert.IsTrue(result.Receipt.IsPass,string.Join(";",result.Receipt.failures));}
        private static Object SceneInstance(GdaiPlayableContract c)=>((GameObject)GdaiSourceBoundPlayableComposer.ReadObjects(c)["pilot"]).GetComponent(HostType);
        private static Bounds Live(Object instance,string path="region") {using(var so=new SerializedObject(instance)){return so.FindProperty(path).boundsValue;}}
        private static void Edit(Object instance,JObject value) {using(var so=new SerializedObject(instance)){var p=so.FindProperty("region");p.boundsValue=new Bounds {center=new Vector3((float)value["center"]["x"],(float)value["center"]["y"],(float)value["center"]["z"]),extents=new Vector3((float)value["extents"]["x"],(float)value["extents"]["y"],(float)value["extents"]["z"])};so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(instance);}}
        private static void SaveMain(){Assert.IsTrue(EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Scenes/Main.unity"));}
        [UnityTest] public IEnumerator BoundsNative_ExactValueDefaultCollectionsAndMalformedShapes() {
            var go=new GameObject("BoundsScratch");go.SetActive(false);
            try {
                var instance=go.AddComponent(HostType);var spec=Spec("scratch");var c=Contract();
                GdaiSourceBoundPlayableComposer.CheckCoverage(instance,spec,2);GdaiSourceBoundPlayableComposer.BindFields(instance,spec,c,null,null,go,true);
                using(var so=new SerializedObject(instance))Assert.AreEqual(SerializedPropertyType.Bounds,so.FindProperty("region").propertyType);
                GdaiSourceBoundPlayableComposer.BindFields(instance,spec,c,null,null,go,false);
                var tiny=Box();tiny["center"]["x"]=(double)float.MaxValue;tiny["extents"]["x"]=(double)float.Epsilon;tiny["extents"]["z"]=-3;
                Region(spec).value=tiny;GdaiSourceBoundPlayableComposer.BindFields(instance,spec,c,null,null,go,true);Assert.AreEqual(float.Epsilon,Live(instance).extents.x);Assert.AreEqual(-3,Live(instance).extents.z);
                foreach(var token in new JToken[]{new JValue("1"),new JValue(true),JValue.CreateNull(),new JValue(1e100),new JValue(1e-100),new JValue(double.NaN),new JValue(double.PositiveInfinity)}) {
                    var bad=Box();bad["center"]["x"]=token;Region(spec).value=bad;Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.CheckCoverage(instance,spec,2));
                }
                foreach(var bad in new JToken[]{JValue.CreateNull(),new JArray(),new JObject(),JObject.Parse(@"{'center':{'x':0,'y':0,'z':0},'size':{'x':1,'y':1,'z':1}}"),JObject.Parse(@"{'center':{'x':0,'y':0,'z':0,'w':0},'extents':{'x':1,'y':1,'z':1}}")}) {Region(spec).value=bad;Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.CheckCoverage(instance,spec,2));}
                Region(spec).value=Box();Edit(instance,Box(1.0000001f));Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.BindFields(instance,spec,c,null,null,go,false),"readback must use exact native equality");
                var defaultField=spec.fields.Single(f=>f.path=="defaultRegion");defaultField.value=Box();Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.BindFields(instance,spec,c,null,null,go,true),"false source_default must refuse");
                Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.RequireBaselineShape(typeof(Bounds)),"revision1 remains unchanged");
            }finally {Object.DestroyImmediate(go);}yield break;
        }
        [UnityTest] public IEnumerator BoundsNative_ScenePrefabReloadAndRepeatedCreatorSync() {
            var c=Contract();Pass(Compose(c,"bounds-first"));
            var expected=GdaiPlayableOwnershipManifest.Load().source_bound_field_baselines.Where(f=>f.component_id=="scene-bounds"||f.component_id=="prefab-bounds").ToArray();Assert.AreEqual(8,expected.Length);Assert.IsTrue(expected.Any(f=>(string)f.state?["kind"]=="bounds"));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach(var host in c.source_bound_plan.objects.Where(o=>o.components.Any(s=>s.type=="BoundsNativeHost"))) {
                GameObject contents=null;try {var target=host.kind=="prefab"?(contents=PrefabUtility.LoadPrefabContents(host.asset_path)).GetComponent(HostType):SceneInstance(c);GdaiSourceBoundPlayableComposer.CheckCoverage(target,host.components.Single(s=>s.type=="BoundsNativeHost"),2);GdaiSourceBoundPlayableComposer.BindFields(target,host.components.Single(s=>s.type=="BoundsNativeHost"),c,GdaiSourceBoundPlayableComposer.ReadObjects(c),host,contents,false);}finally {if(contents!=null)PrefabUtility.UnloadPrefabContents(contents);}
            }
            foreach(var spec in c.source_bound_plan.objects.SelectMany(o=>o.components).Where(s=>s.type=="BoundsNativeHost"))Region(spec).value=Box(7);
            Pass(Compose(c,"bounds-creator"));Pass(Compose(c,"bounds-replay"));Assert.AreEqual(7,Live(SceneInstance(c)).center.x);
            var prefab=c.source_bound_plan.objects.First(o=>o.kind=="prefab");var asset=AssetDatabase.LoadAssetAtPath<GameObject>(prefab.asset_path);Assert.AreEqual(7,Live(asset.GetComponent(HostType)).center.x);
            var reloaded=JsonConvert.DeserializeObject<GdaiPlayableAssetsManifest>(File.ReadAllText(GdaiPlayableOwnershipManifest.ManifestPath));Assert.AreEqual(8,reloaded.source_bound_field_baselines.Count(f=>f.component_id=="scene-bounds"||f.component_id=="prefab-bounds"));yield break;
        }
        [UnityTest] public IEnumerator BoundsNative_UserEditPreservedAndConflictRefusesBeforeMutation() {
            var c=Contract();Pass(Compose(c,"bounds-base"));Edit(SceneInstance(c),Box(9));SaveMain();
            Pass(Compose(c,"bounds-preserve"));Pass(Compose(c,"bounds-preserve-repeated"));Assert.AreEqual(9,Live(SceneInstance(c)).center.x);
            var before=File.ReadAllBytes(GdaiPlayableOwnershipManifest.ManifestPath);var scene=File.ReadAllBytes("Assets/Scenes/Main.unity");Region(SceneSpec(c)).value=Box(5);
            var conflict=Compose(c,"bounds-conflict");Assert.IsFalse(conflict.Completed);StringAssert.Contains("user field conflict before mutation",conflict.Error);Assert.IsTrue(conflict.Receipt==null||!conflict.Receipt.IsPass);CollectionAssert.AreEqual(before,File.ReadAllBytes(GdaiPlayableOwnershipManifest.ManifestPath));CollectionAssert.AreEqual(scene,File.ReadAllBytes("Assets/Scenes/Main.unity"));Assert.AreEqual(9,Live(SceneInstance(c)).center.x);yield break;
        }
        [UnityTest] public IEnumerator BoundsNative_LateTransactionRollbackRestoresScenePrefabAndBaseline() {
            var c=Contract();Pass(Compose(c,"bounds-rollback-base"));Edit(SceneInstance(c),Box(9));SaveMain();
            var manifest=File.ReadAllBytes(GdaiPlayableOwnershipManifest.ManifestPath);var prefab=c.source_bound_plan.objects.First(o=>o.kind=="prefab");
            var prefabBytes=File.ReadAllBytes(prefab.asset_path);var prefabMeta=File.ReadAllBytes(prefab.asset_path+".meta");
            foreach(var spec in c.source_bound_plan.objects.SelectMany(o=>o.components).Where(s=>s.type=="BoundsNativeHost")) {if(spec.id!="scene-bounds")Region(spec).value=Box(8);spec.fields.Single(f=>f.path=="regions").value=new JArray(Box(8));spec.fields.Single(f=>f.path=="entries").value=new JArray(new JObject {["box"]=Box(8)});}
            var failed=Compose(c,"bounds-late-failure",()=>{throw new InvalidOperationException("injected Bounds transaction failure");});
            Assert.IsFalse(failed.Completed);StringAssert.Contains("injected Bounds transaction failure",failed.Error);Assert.IsFalse(failed.Error.Contains("rollback failed"),failed.Error);Assert.IsTrue(failed.Receipt==null||!failed.Receipt.IsPass);
            CollectionAssert.AreEqual(manifest,File.ReadAllBytes(GdaiPlayableOwnershipManifest.ManifestPath));CollectionAssert.AreEqual(prefabBytes,File.ReadAllBytes(prefab.asset_path));CollectionAssert.AreEqual(prefabMeta,File.ReadAllBytes(prefab.asset_path+".meta"));Assert.AreEqual(9,Live(SceneInstance(c)).center.x);
            using(var so=new SerializedObject(SceneInstance(c))){Assert.AreEqual(2,so.FindProperty("regions").arraySize);Assert.AreEqual(4,so.FindProperty("entries").GetArrayElementAtIndex(0).FindPropertyRelative("box").boundsValue.center.x);}
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");Assert.AreEqual(9,Live(SceneInstance(c)).center.x);
            Pass(Compose(Contract(),"bounds-after-rollback"));yield break;
        }
        [UnityTest] public IEnumerator BoundsNative_FalseSourceDefaultRefusesWithoutPublishingOrOwnedArtifacts() {
            var c=Contract();SceneSpec(c).fields.Single(f=>f.path=="defaultRegion").value=Box(6);
            var failed=Compose(c,"bounds-false-default");Assert.IsFalse(failed.Completed);StringAssert.Contains("Bounds readback mismatch",failed.Error);Assert.IsTrue(failed.Receipt==null||!failed.Receipt.IsPass);
            Assert.IsFalse(File.Exists("Assets/Scenes/Main.unity"));Assert.IsFalse(File.Exists(GdaiPlayableOwnershipManifest.ManifestPath));
            foreach(var host in c.source_bound_plan.objects.Where(o=>o.kind!="scene"))Assert.IsFalse(File.Exists(host.asset_path));
            Pass(Compose(Contract(),"bounds-real-default"));yield break;
        }
        [UnityTest] public IEnumerator BoundsNative_MixedUserLeafAndCreatorNestedArrayPersistAcrossSync() {
            var c=Contract();Pass(Compose(c,"bounds-mixed-base"));Edit(SceneInstance(c),Box(9));SaveMain();
            var spec=SceneSpec(c);spec.fields.Single(f=>f.path=="regions").value=new JArray(Box(12));spec.fields.Single(f=>f.path=="entries").value=new JArray(new JObject {["box"]=Box(13)});
            Pass(Compose(c,"bounds-mixed-update"));Pass(Compose(c,"bounds-mixed-replay"));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");Assert.AreEqual(9,Live(SceneInstance(c)).center.x);
            using(var so=new SerializedObject(SceneInstance(c))){Assert.AreEqual(1,so.FindProperty("regions").arraySize);Assert.AreEqual(12,so.FindProperty("regions").GetArrayElementAtIndex(0).boundsValue.center.x);Assert.AreEqual(13,so.FindProperty("entries").GetArrayElementAtIndex(0).FindPropertyRelative("box").boundsValue.center.x);}
            yield break;
        }
        [UnityTest] public IEnumerator BoundsNative_ActualWorldBlockerDefaultPrecedesAwakeColliderCache() {
            var type=GdaiSourceBoundPlayablePlan.CompiledType("ProjectSlash.WorldBlocker");Assert.IsNotNull(type);var go=new GameObject("ActualWorldBlocker");go.SetActive(false);
            try {
                var collider=go.AddComponent<BoxCollider2D>();var component=go.AddComponent(type);
                var host=new GdaiSourceBoundPlayablePlan.Host {id="blocker",kind="scene",components=new List<GdaiSourceBoundPlayablePlan.ComponentSpec>()};var c=Contract();
                var spec=new GdaiSourceBoundPlayablePlan.ComponentSpec {source_path=WorldPath,type=type.FullName,fields=new List<GdaiSourceBoundPlayablePlan.Field>{Value("Bounds",new JObject {["center"]=Vector(0,0,0),["extents"]=Vector(0,0,0)},"source_default"),new GdaiSourceBoundPlayablePlan.Field {path="BlockCollider",disposition="reference",reference_kind="component",target_id="blocker",target_component="collider"}}};
                host.components.Add(new GdaiSourceBoundPlayablePlan.ComponentSpec {id="collider",type="UnityEngine.BoxCollider2D",source_path="builtin",native_defaults=true,fields=new List<GdaiSourceBoundPlayablePlan.Field>()});c.source_bound_plan.objects.Add(host);
                GdaiSourceBoundPlayableComposer.CheckCoverage(component,spec,2);GdaiSourceBoundPlayableComposer.BindFields(component,spec,c,new Dictionary<string,Object>{{"blocker",go}},host,go,true);
                Assert.AreEqual(default(Bounds),(Bounds)type.GetField("Bounds").GetValue(component));Assert.AreSame(collider,type.GetField("BlockCollider").GetValue(component));
                go.SetActive(true);collider.size=new Vector2(4,6);Physics2D.SyncTransforms();type.GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(component,null);
                var cached=(Bounds)type.GetField("Bounds").GetValue(component);Assert.AreEqual(collider.bounds.center,cached.center);Assert.AreEqual(collider.bounds.extents,cached.extents);Assert.AreNotEqual(default(Bounds),cached);
            }finally {Object.DestroyImmediate(go);}yield break;
        }
    }
}
