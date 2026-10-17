using GDAI.Bridge.Editor.LayerA;
using NUnit.Framework;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using GDAI.Bridge.Editor.LayerB;
using GDAI.Bridge.Editor.LayerC;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.TestTools;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiSourceBoundPlayableContractTests
    {
        private static string PayloadSha256(byte[] bytes)
        {
            using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }
        private static byte[] SolidPng()
        {
            return SolidPng(Color.white);
        }
        private static byte[] SolidPng(Color color)
        {
            var texture=new Texture2D(4,4);try {texture.SetPixels(Enumerable.Repeat(color,16).ToArray());texture.Apply();return texture.EncodeToPNG();}finally {UnityEngine.Object.DestroyImmediate(texture);}
        }
        private static byte[] TestWav()
        {
            const int samples=800;const int sampleRate=8000;var pcm=new byte[samples*2];
            for(int i=0;i<samples;i++){short value=(short)(Math.Sin(2*Math.PI*440*i/sampleRate)*short.MaxValue/4);pcm[i*2]=(byte)value;pcm[i*2+1]=(byte)(value>>8);}
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(sampleRate);writer.Write(sampleRate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(pcm.Length);writer.Write(pcm);return stream.ToArray();
            }
        }
        private static GdaiBundleProxyAsset ImportedAsset(string id,string type,string role,string mime,string path,byte[] bytes,string project,string entity=null)
        {
            var audio=type=="audio";return new GdaiBundleProxyAsset {asset_id=id,asset_type=type,role=role,mime_type=mime,file_name=Path.GetFileName(path),unity_path=path,payload_mode="base64",payload_base64=Convert.ToBase64String(bytes),byte_size=bytes.Length,sha256=PayloadSha256(bytes),source=new GdaiBundleProxyAssetSource {project_id=project,entity_id=entity,asset_row_id=entity==null?null:id,world_entity_name=entity,audio_category=audio?(entity==null?"bgm":"sfx"):null,project_audio_asset_id=audio&&entity==null?id:null,audio_candidate_id=audio&&entity==null?id+"-candidate":null}};
        }
        public class ReferenceMergeSnapshot : ScriptableObject
        {
            public GameObject sceneObject;
            public Transform sceneComponent;
            public GameObject prefabObject;
            public Transform prefabComponent;
            public float creatorValue;
        }
        public interface KindInterface { int Count { get; } }
        public abstract class KindAbstract {}
        public class KindClass {}
        public static class KindStatic {}
        public struct KindStruct { public int field; }
        public enum KindEnum { A, B }
        public enum UnicodeEnemyState { 追踪, 激怒前摇, 激怒冲锋, 冷却 }
        [Test] public void CompiledDeclarationKind_AcceptsInterfaceAndSeparatesClassAbstractness() {
            Assert.IsTrue(typeof(KindInterface).IsAbstract);
            foreach(var entry in new[]{Tuple.Create(typeof(KindInterface),"interface",false),Tuple.Create(typeof(KindClass),"class",false),Tuple.Create(typeof(KindAbstract),"class",true),Tuple.Create(typeof(KindStruct),"struct",false),Tuple.Create(typeof(KindEnum),"enum",false)})Assert.IsTrue(GdaiSourceBoundPlayablePlan.CompiledDeclarationMatches(entry.Item1,entry.Item2,entry.Item3));
            Assert.IsFalse(GdaiSourceBoundPlayablePlan.CompiledDeclarationMatches(typeof(KindStatic),"class",true));
            Assert.IsFalse(GdaiSourceBoundPlayablePlan.CompiledDeclarationMatches(typeof(KindAbstract),"class",false));Assert.IsFalse(GdaiSourceBoundPlayablePlan.CompiledDeclarationMatches(typeof(KindInterface),"class",false));Assert.IsFalse(GdaiSourceBoundPlayablePlan.CompiledDeclarationMatches(typeof(Action),"class",false));Assert.IsFalse(GdaiSourceBoundPlayablePlan.CompiledDeclarationMatches(typeof(System.Collections.Generic.List<>),"class",false));
        }
        [Test] public void InheritedCanonicalFieldType_ReconcilesCrossNamespaceRequiredLeaves() {
            var c=GdaiSourceBoundPlayableFixtures.Build();var owner=c.source_bound_obligations.Single(t=>t.type=="FixtureA.PulseAgent");
            var nested=new GdaiSourceBoundPlayablePlan.TypeObligation {type="Common.Needs",source_path=owner.source_path,kind="struct",hosting="none",serializable=true,bases=new System.Collections.Generic.List<string>(),fields=new System.Collections.Generic.List<GdaiSourceBoundPlayablePlan.FieldObligation>{new GdaiSourceBoundPlayablePlan.FieldObligation {path="target",type="UnityEngine.GameObject",required_reference=true}}};
            c.source_bound_obligations.Add(nested);c.source_bound_plan.type_dispositions.Add(new GdaiSourceBoundPlayablePlan.TypeDisposition {type=nested.type,source_path=nested.source_path,disposition="data",rationale="inherited declaring namespace canonical identity"});owner.fields.Add(new GdaiSourceBoundPlayablePlan.FieldObligation {path="settings",type="Common.Needs"});
            var field=new GdaiSourceBoundPlayablePlan.Field {path="settings",disposition="value",value=JObject.Parse(@"{'target':{'reference_kind':'prefab','target_id':'one'}}")};c.source_bound_plan.objects[0].components[0].fields.Add(field);
            Assert.IsTrue(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
            foreach(var value in new[]{"{'target':null}","{}"}){field.value=JObject.Parse(value);Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);}
            field.disposition="source_default";field.rationale="synthetic downgrade";Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
        }
        [Test] public void SensitivePlan_PreReadGuardRejectsTopNestedAndCollections() {
            foreach(var name in new[]{"_token","credential","password","_key","_iv","IV","apiKey","signingKey","api_key","encryptionKey"}) {
                var fields=new[]{new GdaiSourceBoundPlayablePlan.Field {path=name,disposition="value",value=new JValue("synthetic-placeholder")}};
                Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayablePlan.RequireSafeFields(fields));
                fields[0].path="ordinary";fields[0].value=new JArray(new JObject {[name]="synthetic-placeholder"});Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayablePlan.RequireSafeFields(fields));
                var c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_plan.objects[0].components[0].fields.Add(fields[0]);Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.CaptureSavedBaselines(c));
                // Direct binding/readback refuses before constructing SerializedObject, even with no instance.
                var comp=new GdaiSourceBoundPlayablePlan.ComponentSpec {fields=fields.ToList()};Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.BindFields(null,comp,null,null,null,null,false));Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.BindFields(null,comp,null,null,null,null,true));
            }
            GdaiSourceBoundPlayablePlan.RequireSafeFields(new[]{new GdaiSourceBoundPlayablePlan.Field {path="count",value=new JValue(2)}});
        }
        [Test] public void NestedRequiredConfigAndCollections_RejectNullMissingDefaultAndAcceptBoundValues() {
            var c=GdaiSourceBoundPlayableFixtures.Build();var owner=c.source_bound_obligations.Single(t=>t.type=="GameIntegrationController");
            var nested=new GdaiSourceBoundPlayablePlan.TypeObligation {type="RequiredConfig",source_path=owner.source_path,kind="struct",hosting="none",serializable=true,bases=new System.Collections.Generic.List<string>(),fields=new System.Collections.Generic.List<GdaiSourceBoundPlayablePlan.FieldObligation>{new GdaiSourceBoundPlayablePlan.FieldObligation {path="target",type="UnityEngine.GameObject",required_reference=true}}};
            c.source_bound_obligations.Add(nested);c.source_bound_plan.type_dispositions.Add(new GdaiSourceBoundPlayablePlan.TypeDisposition {type=nested.type,source_path=nested.source_path,disposition="data",rationale="serialized config"});
            owner.fields.Add(new GdaiSourceBoundPlayablePlan.FieldObligation {path="nested",type="RequiredConfig[]"});
            var f=new GdaiSourceBoundPlayablePlan.Field {path="nested",disposition="value",value=JArray.Parse(@"[{'target':{'reference_kind':'prefab','target_id':'one'}}]")};c.source_bound_plan.objects.Single(o=>o.id=="director").components[0].fields.Add(f);
            Assert.IsTrue(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
            foreach(var value in new[]{"[{'target':null}]","[{}]","[]","null","[{'target':4}]"}) {f.value=JToken.Parse(value);Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok,value);}
            f.value=JArray.Parse(@"[{'target':{'reference_kind':'prefab','target_id':'one'}}]");f.disposition="source_default";f.rationale="model downgrade";Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
        }
        [Test] public void ImplicitGeometry_UsesFinalCameraOrderAndPriorReceiptDecisions() {
            var c=GdaiSourceBoundPlayableFixtures.Build();var camera=c.source_bound_plan.objects.Single(o=>o.id=="camera");camera.position="placement";camera.entity_id="pilot-entity";
            var old=GdaiSourceBoundPlayableComposer.ImplicitGeometryWrites(c);Assert.AreEqual(1,old.Count(w=>w.host_id=="camera"&&w.field=="position"));Assert.AreEqual(-10,(float)old.Single(w=>w.host_id=="camera"&&w.field=="position").expected["z"]);
            Assert.IsTrue(old.Any(w=>w.host_id=="one"&&w.field=="localScale"));Assert.AreEqual(3,old.Count(w=>w.type=="UnityEngine.Camera"));
            c.source_bound_context["placements"][0]["x"]=580;c.source_bound_context["placements"][0]["scale"]=2;c.source_bound_context["arena"]["height"]=720;
            c.source_bound_context_json=c.source_bound_context.ToString(Formatting.None);c.source_bound_plan.context_sha256=GdaiSourceBoundPlayablePlan.Hash(System.Text.Encoding.UTF8.GetBytes(c.source_bound_context_json));
            var changed=GdaiSourceBoundPlayableComposer.ImplicitGeometryWrites(c);
            foreach(var prior in old) {var next=changed.Single(w=>w.host_id==prior.host_id&&w.component_id==prior.component_id&&w.field==prior.field);
                Assert.AreEqual(JToken.DeepEquals(prior.expected,next.expected)?GdaiSourceBoundPlayableComposer.FieldUpdateDecision.AlreadyApplied:GdaiSourceBoundPlayableComposer.FieldUpdateDecision.CreatorUpdate,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(prior.expected,prior.expected,next.expected,true));
                Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.AlreadyApplied,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(prior.expected,next.expected,next.expected,true));
                Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.Conflict,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(prior.expected,new JValue(99),next.expected,true));
                Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.MissingBaseline,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(null,next.expected,next.expected,false));
            }
        }
        [Test] public void Readiness_OnlyRealPendingSignalsDeferAndSettledStaleTerminates() {
            Assert.AreEqual(GdaiSourceBoundPlayablePlan.Readiness.Transient,GdaiSourceBoundPlayablePlan.PendingReadiness(true,false));Assert.AreEqual(GdaiSourceBoundPlayablePlan.Readiness.Transient,GdaiSourceBoundPlayablePlan.PendingReadiness(false,true));Assert.AreEqual(GdaiSourceBoundPlayablePlan.Readiness.Ready,GdaiSourceBoundPlayablePlan.PendingReadiness(false,false));
            Assert.AreEqual(GdaiSourceBoundPlayablePlan.Readiness.Terminal,GdaiSourceBoundPlayablePlan.SettledArtifactReadiness(false,true));Assert.AreEqual(GdaiSourceBoundPlayablePlan.Readiness.Terminal,GdaiSourceBoundPlayablePlan.SettledArtifactReadiness(true,false));Assert.AreEqual(GdaiSourceBoundPlayablePlan.Readiness.Ready,GdaiSourceBoundPlayablePlan.SettledArtifactReadiness(true,true));
        }
        [Test] public void GenericPlan_ParsesBothIndependentNamespacesAndPrefabs() {
            var c=GdaiSourceBoundPlayableFixtures.Build();var parsed=GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c));Assert.IsTrue(parsed.Ok,parsed.Summary);Assert.AreEqual(2,c.source_bound_plan.objects.FindAll(o=>o.kind=="prefab").Count);
        }
        [Test] public void MissingContextAndWrongRevisionReject() {
            var c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_plan.revision=9;Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
            c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_context=null;Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
        }
        [Test] public void MissingFieldsAndConfigurationDispositionsReject() {
            var c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_plan.objects[0].components[0].fields=null;Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
            c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_plan.objects[0].components[0].fields[0].path="_encryptionKey";Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
        }
        [Test] public void ActualSourceWithoutPlanCannotBecomeSourceBoundPlayable() {
            var c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_plan=null;Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
        }
        [Test] public void UnsafePathAndUnknownReferenceReject() {
            var c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_plan.objects[1].asset_path="Assets/GDAI_Project/Generated/../Human.prefab";Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
            c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_plan.objects[1].components[0].fields[0].target_id="missing";Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
        }
        [Test] public void ExportBinding_UsesActualWindowVersionAdmission()
        {
            var binding=new GdaiProjectBindingData {schema_version=GdaiProjectBinding.ExpectedSchema,project_id="00000000-0000-0000-0000-000000000001",target_engine=GdaiProjectBinding.ExpectedEngine,unity_version="6000.5.1f1",render_pipeline=GdaiProjectBinding.ExpectedPipeline,plugin_version="0.1.0-alpha.8.11",generated_owned_paths=new[]{"Assets/GDAI_Generated"}};
            Assert.IsNull(GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));
            binding.plugin_version="0.1.0-alpha.8.10";Assert.IsNotNull(GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));
        }
        [Test] public void IndependentInventory_RejectsOmittedControllerModuleSecondaryPrefabSlotAndAsset()
        {
            foreach(var omission in new[]{"controller","module","secondary","prefab","slot","asset","types"}) {
                var c=GdaiSourceBoundPlayableFixtures.Build();var p=c.source_bound_plan;
                if(omission=="controller")p.objects.RemoveAll(o=>o.id=="director");
                if(omission=="module")p.objects[0].components.Clear();
                if(omission=="secondary")p.objects[2].components.RemoveAll(comp=>comp.type=="ExtraEnemy");
                if(omission=="prefab")p.objects.RemoveAll(o=>o.id=="two");
                if(omission=="slot")p.objects.Single(o=>o.id=="director").components[0].fields.RemoveAt(0);
                if(omission=="asset")foreach(var host in p.objects)host.components.RemoveAll(comp=>comp.type=="UnityEngine.SpriteRenderer");
                if(omission=="types")p.type_dispositions.Clear();
                Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok,omission);
            }
        }
        [Test] public void SourceInventory_ExactlyCoversUnhostedObligationsAndAllowsMultipleTypesPerSource()
        {
            var c=GdaiSourceBoundPlayableFixtures.Build();
            c.source_bound_plan.sources.RemoveAll(source=>source.path==GdaiSourceBoundPlayableFixtures.DirectoryPath+"Plain.cs");
            var parsed=GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c));Assert.IsFalse(parsed.Ok);StringAssert.Contains("obligation source identity missing",parsed.Summary);
            c=GdaiSourceBoundPlayableFixtures.Build();var plain=c.source_bound_obligations.Single(t=>t.type=="Plain");
            c.source_bound_obligations.Add(new GdaiSourceBoundPlayablePlan.TypeObligation {type="AuxiliaryPlainData",source_path=plain.source_path,kind="class",hosting="none",bases=new System.Collections.Generic.List<string>(),fields=new System.Collections.Generic.List<GdaiSourceBoundPlayablePlan.FieldObligation>()});
            c.source_bound_plan.type_dispositions.Add(new GdaiSourceBoundPlayablePlan.TypeDisposition {type="AuxiliaryPlainData",source_path=plain.source_path,disposition="controller_constructed",rationale="same genuine source contains multiple unhosted types"});
            parsed=GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c));Assert.IsTrue(parsed.Ok,parsed.Summary);
        }
        [Test] public void FrozenArena_ConvertsCenterNonCenterAndBackgroundAcrossReplayRoutes()
        {
            var c=GdaiSourceBoundPlayableFixtures.Build();
            Assert.AreEqual(Vector3.zero,GdaiSceneAssemblyCoordinateUtility.CanvasToWorld(480,270,960,540));
            Assert.AreEqual(new Vector3(1,1,0),GdaiSceneAssemblyCoordinateUtility.CanvasToWorld(580,170,960,540));
            var frame=GdaiSourceBoundPlayablePlan.Framing(c);Assert.AreEqual(2.7f,frame.SolveOrthographicSize(),0.00001f);
            var texture=new Texture2D(4,4);var sprite=Sprite.Create(texture,new Rect(0,0,4,4),new Vector2(.5f,.5f),100);
            var go=new GameObject("FramingScratch");
            try {Vector3? scale=null;foreach(var route in new[]{"first","deferred","second"}) {
                go.transform.position=new Vector3(100,200,0);go.transform.localScale=Vector3.one*999;
                GdaiSceneBackgroundBinder.PositionAndScaleToCover(go.transform,sprite,GdaiSourceBoundPlayablePlan.Framing(c));
                Assert.AreEqual(Vector3.zero,go.transform.position,route);if(scale.HasValue)Assert.AreEqual(scale.Value,go.transform.localScale,route);scale=go.transform.localScale;
            }}finally {UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }
        [Test] public void PriorFieldReceipt_ImplementsCompleteThreeWayDecisionMatrix()
        {
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.CreatorUpdate,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3.0),new JValue(3.0),new JValue(2.5),true));
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.AlreadyApplied,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3.0),new JValue(2.5),new JValue(2.5),true));
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.PreserveUserEdit,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3.0),new JValue(4.0),new JValue(3.0),true));
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.AlreadyApplied,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3.0),new JValue(3.0),new JValue(3.0),true));
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.Conflict,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3.0),new JValue(4.0),new JValue(2.5),true));
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.MissingBaseline,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(null,new JValue(3.0),new JValue(2.5),false));
        }
        [Test] public void PriorFieldReceipt_MixedAndRepeatedSyncDecisionsRemainIndependent()
        {
            var userPreserved=GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3.0),new JValue(4.0),new JValue(3.0),true);
            var creatorUpdated=GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3.0),new JValue(3.0),new JValue(2.5),true);
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.PreserveUserEdit,userPreserved);
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.CreatorUpdate,creatorUpdated);
            Assert.IsFalse(GdaiSourceBoundPlayableComposer.ShouldApplyFieldUpdate(userPreserved),"preserved user fields must not be mutated");
            Assert.IsFalse(GdaiSourceBoundPlayableComposer.ShouldRestorePriorField(userPreserved),"rollback must not replace a preserved user field");
            var alreadyApplied=GdaiSourceBoundPlayableComposer.FieldUpdateDecision.AlreadyApplied;
            Assert.IsFalse(GdaiSourceBoundPlayableComposer.ShouldApplyFieldUpdate(alreadyApplied),"equal concurrent edits must not be rewritten");
            Assert.IsFalse(GdaiSourceBoundPlayableComposer.ShouldRestorePriorField(alreadyApplied),"equal concurrent edits must not be rolled back");
            Assert.IsTrue(GdaiSourceBoundPlayableComposer.ShouldApplyFieldUpdate(creatorUpdated));
            Assert.IsTrue(GdaiSourceBoundPlayableComposer.ShouldRestorePriorField(creatorUpdated));
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.PreserveUserEdit,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3.0),new JValue(4.0),new JValue(3.0),true),"second sync must retain the creator baseline rather than promote user live");
            Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.Conflict,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3.0),new JValue(4.0),new JValue(2.0),true),"both different must remain atomic conflict");
            Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.ShouldApplyFieldUpdate(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.Conflict),"conflict must stop before mutation");
            Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.ShouldRestorePriorField(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.MissingBaseline),"missing baselines must not enter rollback mutation");
        }
        [Test] public void PriorFieldReceipt_NativeSceneAndComponentReferencesRemainPreservedDuringCreatorUpdate()
        {
            var sceneTarget=new GameObject("GDAI_NativeReferenceTarget");var live=ScriptableObject.CreateInstance<ReferenceMergeSnapshot>();var retained=ScriptableObject.CreateInstance<ReferenceMergeSnapshot>();
            try {
                live.sceneObject=sceneTarget;live.sceneComponent=sceneTarget.transform;live.creatorValue=3f;
                using(var liveSerialized=new SerializedObject(live))using(var retainedSerialized=new SerializedObject(retained)) {
                    retainedSerialized.CopyFromSerializedProperty(liveSerialized.FindProperty("sceneObject"));retainedSerialized.CopyFromSerializedProperty(liveSerialized.FindProperty("sceneComponent"));retainedSerialized.ApplyModifiedPropertiesWithoutUndo();
                    live.creatorValue=4f;liveSerialized.Update();retainedSerialized.Update();
                    Assert.IsTrue(SerializedProperty.DataEquals(liveSerialized.FindProperty("sceneObject"),retainedSerialized.FindProperty("sceneObject")),"preserved scene object identity changed while another field updated");
                    Assert.IsTrue(SerializedProperty.DataEquals(liveSerialized.FindProperty("sceneComponent"),retainedSerialized.FindProperty("sceneComponent")),"preserved scene component identity changed while another field updated");
                    Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.PreserveUserEdit,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue("creator-old"),new JValue("user-scene-reference"),new JValue("creator-old"),true));
                    Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.CreatorUpdate,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3f),new JValue(3f),new JValue(4f),true));
                }
            } finally {UnityEngine.Object.DestroyImmediate(retained);UnityEngine.Object.DestroyImmediate(live);UnityEngine.Object.DestroyImmediate(sceneTarget);}
        }
        [Test] public void PriorFieldReceipt_NativePrefabInternalReferencesSurviveReloadWithMixedCreatorUpdate()
        {
            var prefabPath=AssetDatabase.GenerateUniqueAssetPath("Assets/GDAI_ReferenceMergeFixture.prefab");var root=new GameObject("Root");var child=new GameObject("Child");child.transform.SetParent(root.transform);var retained=ScriptableObject.CreateInstance<ReferenceMergeSnapshot>();GameObject first=null,second=null;
            try {
                Assert.IsNotNull(PrefabUtility.SaveAsPrefabAsset(root,prefabPath));
                first=PrefabUtility.LoadPrefabContents(prefabPath);var firstChild=first.transform.Find("Child");
                var live=ScriptableObject.CreateInstance<ReferenceMergeSnapshot>();
                try {
                    live.prefabObject=firstChild.gameObject;live.prefabComponent=firstChild;live.creatorValue=3f;
                    var composer=typeof(GdaiSourceBoundPlayableComposer);var fieldState=composer.GetMethod("FieldState",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var restoreFieldState=composer.GetMethod("RestoreFieldState",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);Assert.IsNotNull(fieldState);Assert.IsNotNull(restoreFieldState);JToken retainedObjectState,retainedComponentState;
                    using(var liveSerialized=new SerializedObject(live)) {retainedObjectState=(JToken)fieldState.Invoke(null,new object[]{liveSerialized.FindProperty("prefabObject")});retainedComponentState=(JToken)fieldState.Invoke(null,new object[]{liveSerialized.FindProperty("prefabComponent")});}
                    PrefabUtility.UnloadPrefabContents(first);first=null;second=PrefabUtility.LoadPrefabContents(prefabPath);var secondChild=second.transform.Find("Child");live.prefabObject=secondChild.gameObject;live.prefabComponent=secondChild;live.creatorValue=4f;
                    using(var liveSerialized=new SerializedObject(live))using(var retainedSerialized=new SerializedObject(retained)) {
                        restoreFieldState.Invoke(null,new object[]{retainedSerialized.FindProperty("prefabObject"),retainedObjectState,second,prefabPath});restoreFieldState.Invoke(null,new object[]{retainedSerialized.FindProperty("prefabComponent"),retainedComponentState,second,prefabPath});retainedSerialized.ApplyModifiedPropertiesWithoutUndo();
                        Assert.IsTrue(SerializedProperty.DataEquals(liveSerialized.FindProperty("prefabObject"),retainedSerialized.FindProperty("prefabObject")),"preserved prefab object identity must survive prefab-contents remapping");
                        Assert.IsTrue(SerializedProperty.DataEquals(liveSerialized.FindProperty("prefabComponent"),retainedSerialized.FindProperty("prefabComponent")),"preserved prefab component identity must survive prefab-contents remapping");
                    }
                    Assert.AreEqual(GdaiSourceBoundPlayableComposer.FieldUpdateDecision.CreatorUpdate,GdaiSourceBoundPlayableComposer.DecideFieldUpdate(new JValue(3f),new JValue(3f),new JValue(4f),true));
                } finally {UnityEngine.Object.DestroyImmediate(live);}
            } finally {if(first!=null)PrefabUtility.UnloadPrefabContents(first);if(second!=null)PrefabUtility.UnloadPrefabContents(second);UnityEngine.Object.DestroyImmediate(retained);UnityEngine.Object.DestroyImmediate(root);AssetDatabase.DeleteAsset(prefabPath);}
        }

        [UnityTest] public IEnumerator CompleteTransactionFailurePreservesPrefabChildComponentReferenceDuringMixedCreatorUpdate()
        {
            var root=Directory.GetParent(Application.dataPath).FullName;var marker=Path.Combine(root,"Library/GDAI/native-test-scratch.marker");
            if(!File.Exists(marker)||File.ReadAllText(marker)!="gdai-disposable-sdk-native-test-v1")throw new InvalidOperationException("source-bound native tests require an explicitly marked disposable project");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);AssetDatabase.DeleteAsset("Assets/GDAI_Project/Generated");AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");
            var sourcesChanged=GdaiSourceBoundPlayableFixtures.WriteSourceFiles();AssetDatabase.Refresh();if(sourcesChanged)yield return new RecompileScripts();GdaiSourceBoundPlayableFixtures.WriteGenericAssets();
            var c=GdaiSourceBoundPlayableFixtures.Build();var first=GdaiSourceBoundPlayableComposer.Run(c,"generic-project","prefab-reference-baseline",new string('1',64),"Assets/Scenes/Main.unity",DateTime.UtcNow);Assert.IsTrue(first.Completed,first.Error);
            var prefabHost=c.source_bound_plan.objects.Single(o=>o.id=="one");var prefabSpec=prefabHost.components.Single(component=>component.id=="enemy-one");var extra=c.source_bound_plan.objects.Single(o=>o.id=="extra-tuning").components[0].fields[0];var manifestPath=Path.Combine(root,GdaiPlayableOwnershipManifest.ManifestPath);var oldManifest=File.ReadAllBytes(manifestPath);GameObject contents=null;
            try {
                contents=PrefabUtility.LoadPrefabContents(prefabHost.asset_path);var userChild=new GameObject("UserChild");userChild.transform.SetParent(contents.transform);var instance=contents.GetComponent(GdaiSourceBoundPlayableComposer.Resolve(prefabSpec,"prefab"));using(var serialized=new SerializedObject(instance)){serialized.FindProperty("self").objectReferenceValue=userChild.transform;serialized.ApplyModifiedPropertiesWithoutUndo();}Assert.IsNotNull(PrefabUtility.SaveAsPrefabAsset(contents,prefabHost.asset_path));PrefabUtility.UnloadPrefabContents(contents);contents=null;AssetDatabase.SaveAssets();
                extra.disposition="value";extra.value=new JValue(4.0);var failed=GdaiSourceBoundPlayableComposer.Run(c,"generic-project","prefab-reference-failure",new string('2',64),"Assets/Scenes/Main.unity",DateTime.UtcNow,()=>{throw new InvalidOperationException("injected prefab reference transaction failure");});Assert.IsFalse(failed.Completed);StringAssert.Contains("injected prefab reference transaction failure",failed.Error);
                contents=PrefabUtility.LoadPrefabContents(prefabHost.asset_path);var persistedChild=contents.transform.Find("UserChild");Assert.IsNotNull(persistedChild,"preserved child must survive prefab reload");instance=contents.GetComponent(GdaiSourceBoundPlayableComposer.Resolve(prefabSpec,"prefab"));using(var serialized=new SerializedObject(instance))Assert.AreSame(persistedChild,serialized.FindProperty("self").objectReferenceValue,"preserved prefab child component identity changed during mixed creator update rollback");
                var objects=GdaiSourceBoundPlayableComposer.ReadObjects(c);using(var mixed=new SerializedObject(objects["extra-tuning"]))Assert.AreEqual(2.0,mixed.FindProperty("factor").doubleValue,0.00001,"mixed CreatorUpdate was not rolled back");CollectionAssert.AreEqual(oldManifest,File.ReadAllBytes(manifestPath),"failed prefab reference transaction changed the old manifest");
            } finally {if(contents!=null)PrefabUtility.UnloadPrefabContents(contents);UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);AssetDatabase.DeleteAsset("Assets/GDAI_Project/Generated");AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");}
        }
        [UnityTest] public IEnumerator CompleteTransactionRejectsDifferentRealScenePrefabInstanceAliasAndPreservesConcurrentReplacement()
        {
            var root=Directory.GetParent(Application.dataPath).FullName;var marker=Path.Combine(root,"Library/GDAI/native-test-scratch.marker");
            if(!File.Exists(marker)||File.ReadAllText(marker)!="gdai-disposable-sdk-native-test-v1")throw new InvalidOperationException("source-bound native tests require an explicitly marked disposable project");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);AssetDatabase.DeleteAsset("Assets/GDAI_Project/Generated");AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");
            var sourcesChanged=GdaiSourceBoundPlayableFixtures.WriteSourceFiles();AssetDatabase.Refresh();if(sourcesChanged)yield return new RecompileScripts();GdaiSourceBoundPlayableFixtures.WriteGenericAssets();
            var c=GdaiSourceBoundPlayableFixtures.Build();var first=GdaiSourceBoundPlayableComposer.Run(c,"generic-project","external-instance-baseline",new string('3',64),"Assets/Scenes/Main.unity",DateTime.UtcNow);Assert.IsTrue(first.Completed,first.Error);
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/Main.unity");var prefabPath=c.source_bound_plan.objects.Single(o=>o.id=="one").asset_path;var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);Assert.IsNotNull(prefab);var instanceA=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);var instanceB=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);Assert.IsNotNull(instanceA);Assert.IsNotNull(instanceB);instanceA.name="UserExternalInstanceA";instanceB.name="UserExternalInstanceB";
            foreach(var ownedMarker in instanceA.GetComponentsInChildren<GDAI.Runtime.GdaiGeneratedPlayableMarker>(true))UnityEngine.Object.DestroyImmediate(ownedMarker);foreach(var ownedMarker in instanceB.GetComponentsInChildren<GDAI.Runtime.GdaiGeneratedPlayableMarker>(true))UnityEngine.Object.DestroyImmediate(ownedMarker);
            var identityProbe=ScriptableObject.CreateInstance<ReferenceMergeSnapshot>();
            try {
                Assert.IsTrue(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,"Assets/Scenes/Main.unity"));Assert.AreNotEqual(GlobalObjectId.GetGlobalObjectIdSlow(instanceA.transform),GlobalObjectId.GetGlobalObjectIdSlow(instanceB.transform),"two real scene prefab instances must have distinct Unity GlobalObjectIds");
                var composer=typeof(GdaiSourceBoundPlayableComposer);var fieldState=composer.GetMethod("FieldState",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);Assert.IsNotNull(fieldState);JToken stateA,stateB;using(var probe=new SerializedObject(identityProbe)){identityProbe.sceneComponent=instanceA.transform;probe.Update();stateA=((JToken)fieldState.Invoke(null,new object[]{probe.FindProperty("sceneComponent")})).DeepClone();identityProbe.sceneComponent=instanceB.transform;probe.Update();stateB=((JToken)fieldState.Invoke(null,new object[]{probe.FindProperty("sceneComponent")})).DeepClone();}Assert.AreNotEqual((string)stateA["identity"],(string)stateB["identity"],"production FieldState must distinguish real scene prefab instances");
                var objects=GdaiSourceBoundPlayableComposer.ReadObjects(c);var pilotHost=c.source_bound_plan.objects.Single(o=>o.id=="pilot");var pilotSpec=pilotHost.components.Single(component=>component.id=="pilot-component");var pilot=(GameObject)objects["pilot"];var pilotComponent=pilot.GetComponent(GdaiSourceBoundPlayableComposer.Resolve(pilotSpec,"scene"));using(var serialized=new SerializedObject(pilotComponent)){serialized.FindProperty("optional").objectReferenceValue=instanceA.transform;serialized.ApplyModifiedPropertiesWithoutUndo();}Assert.IsTrue(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,"Assets/Scenes/Main.unity"));
                var manifestPath=Path.Combine(root,GdaiPlayableOwnershipManifest.ManifestPath);var oldManifest=File.ReadAllBytes(manifestPath);var extra=c.source_bound_plan.objects.Single(o=>o.id=="extra-tuning").components[0].fields[0];extra.disposition="value";extra.value=new JValue(4.0);
                var failed=GdaiSourceBoundPlayableComposer.Run(c,"generic-project","external-instance-replacement",new string('4',64),"Assets/Scenes/Main.unity",DateTime.UtcNow,()=>{var liveObjects=GdaiSourceBoundPlayableComposer.ReadObjects(c);var livePilot=(GameObject)liveObjects["pilot"];var liveComponent=livePilot.GetComponent(GdaiSourceBoundPlayableComposer.Resolve(pilotSpec,"scene"));using(var serialized=new SerializedObject(liveComponent)){serialized.FindProperty("optional").objectReferenceValue=instanceB.transform;serialized.ApplyModifiedPropertiesWithoutUndo();}if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,"Assets/Scenes/Main.unity"))throw new InvalidOperationException("concurrent external instance replacement save failed");});Assert.IsFalse(failed.Completed);StringAssert.Contains("preserved user field changed: optional",failed.Error);
                objects=GdaiSourceBoundPlayableComposer.ReadObjects(c);pilot=(GameObject)objects["pilot"];pilotComponent=pilot.GetComponent(GdaiSourceBoundPlayableComposer.Resolve(pilotSpec,"scene"));using(var serialized=new SerializedObject(pilotComponent))Assert.AreSame(instanceB.transform,serialized.FindProperty("optional").objectReferenceValue,"concurrent live user replacement must survive failed transaction rollback");using(var mixed=new SerializedObject(objects["extra-tuning"]))Assert.AreEqual(2.0,mixed.FindProperty("factor").doubleValue,0.00001,"CreatorUpdate was not rolled back after external instance identity rejection");CollectionAssert.AreEqual(oldManifest,File.ReadAllBytes(manifestPath),"failed external instance transaction changed the old manifest");
            } finally {UnityEngine.Object.DestroyImmediate(identityProbe);UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);AssetDatabase.DeleteAsset("Assets/GDAI_Project/Generated");AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");}
        }
        [UnityTest] public IEnumerator CompleteTransactionFailuresPreserveEqualConcurrentScalarAndReferenceEdits()
        {
            var root=Directory.GetParent(Application.dataPath).FullName;var marker=Path.Combine(root,"Library/GDAI/native-test-scratch.marker");
            if(!File.Exists(marker)||File.ReadAllText(marker)!="gdai-disposable-sdk-native-test-v1")throw new InvalidOperationException("source-bound native tests require an explicitly marked disposable project");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);AssetDatabase.DeleteAsset("Assets/GDAI_Project/Generated");AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");
            var sourcesChanged=GdaiSourceBoundPlayableFixtures.WriteSourceFiles();AssetDatabase.Refresh();if(sourcesChanged)yield return new RecompileScripts();GdaiSourceBoundPlayableFixtures.WriteGenericAssets();
            var c=GdaiSourceBoundPlayableFixtures.Build();var tuning=c.source_bound_plan.objects.Single(o=>o.id=="tuning").components[0].fields[0];tuning.disposition="value";tuning.value=new JValue(3.0);
            var first=GdaiSourceBoundPlayableComposer.Run(c,"generic-project","rollback-baseline",new string('a',64),"Assets/Scenes/Main.unity",DateTime.UtcNow);Assert.IsTrue(first.Completed,first.Error);
            var manifestPath=Path.Combine(root,GdaiPlayableOwnershipManifest.ManifestPath);var oldManifest=File.ReadAllBytes(manifestPath);var extra=c.source_bound_plan.objects.Single(o=>o.id=="extra-tuning").components[0].fields[0];extra.disposition="value";extra.value=new JValue(4.0);
            try {
                var objects=GdaiSourceBoundPlayableComposer.ReadObjects(c);var config=objects["tuning"];using(var serialized=new SerializedObject(config)){serialized.FindProperty("multiplier").doubleValue=2.5;serialized.ApplyModifiedPropertiesWithoutUndo();}AssetDatabase.SaveAssets();tuning.value=new JValue(2.5);
                var scalarFailure=GdaiSourceBoundPlayableComposer.Run(c,"generic-project","rollback-scalar",new string('b',64),"Assets/Scenes/Main.unity",DateTime.UtcNow,()=>{throw new InvalidOperationException("injected completeTransaction scalar failure");});Assert.IsFalse(scalarFailure.Completed);StringAssert.Contains("injected completeTransaction scalar failure",scalarFailure.Error);
                objects=GdaiSourceBoundPlayableComposer.ReadObjects(c);using(var scalar=new SerializedObject(objects["tuning"]))Assert.AreEqual(2.5,scalar.FindProperty("multiplier").doubleValue,0.00001,"AlreadyApplied scalar edit was overwritten");using(var mixed=new SerializedObject(objects["extra-tuning"]))Assert.AreEqual(2.0,mixed.FindProperty("factor").doubleValue,0.00001,"CreatorUpdate scalar was not rolled back");CollectionAssert.AreEqual(oldManifest,File.ReadAllBytes(manifestPath),"failed scalar transaction changed the old manifest");

                objects=GdaiSourceBoundPlayableComposer.ReadObjects(c);var pilotHost=c.source_bound_plan.objects.Single(o=>o.id=="pilot");var pilotSpec=pilotHost.components.Single(component=>component.id=="pilot-component");var pilot=(GameObject)objects["pilot"];var director=(GameObject)objects["director"];var pilotComponent=pilot.GetComponent(GdaiSourceBoundPlayableComposer.Resolve(pilotSpec,"scene"));using(var serialized=new SerializedObject(pilotComponent)){serialized.FindProperty("optional").objectReferenceValue=director.transform;serialized.ApplyModifiedPropertiesWithoutUndo();}Assert.IsTrue(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/Main.unity"),"Assets/Scenes/Main.unity"));pilotSpec.fields.Single(field=>field.path=="optional").target_id="director";
                var referenceFailure=GdaiSourceBoundPlayableComposer.Run(c,"generic-project","rollback-reference",new string('c',64),"Assets/Scenes/Main.unity",DateTime.UtcNow,()=>{throw new InvalidOperationException("injected completeTransaction reference failure");});Assert.IsFalse(referenceFailure.Completed);StringAssert.Contains("injected completeTransaction reference failure",referenceFailure.Error);
                objects=GdaiSourceBoundPlayableComposer.ReadObjects(c);pilot=(GameObject)objects["pilot"];director=(GameObject)objects["director"];pilotComponent=pilot.GetComponent(GdaiSourceBoundPlayableComposer.Resolve(pilotSpec,"scene"));using(var reference=new SerializedObject(pilotComponent))Assert.AreSame(director.transform,reference.FindProperty("optional").objectReferenceValue,"AlreadyApplied reference edit was overwritten");using(var mixed=new SerializedObject(objects["extra-tuning"]))Assert.AreEqual(2.0,mixed.FindProperty("factor").doubleValue,0.00001,"mixed CreatorUpdate was not rolled back");CollectionAssert.AreEqual(oldManifest,File.ReadAllBytes(manifestPath),"failed reference transaction changed the old manifest");
            } finally {UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);AssetDatabase.DeleteAsset("Assets/GDAI_Project/Generated");AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");}
        }
        [Test] public void ImportedRegistry_RejectsDuplicateIdentityPathBadTypeAndMissingSource()
        {
            var written=new HashSet<string>{"Assets/GDAI_Generated/Audio/BGM/one.wav","Assets/GDAI_Generated/Audio/BGM/two.wav"};
            Func<string,string,string,string,GdaiBundleProxyAsset> asset=(id,path,type,project)=>new GdaiBundleProxyAsset {asset_id=id,unity_path=path,asset_type=type,mime_type="audio/wav",sha256=new string('a',64),source=new GdaiBundleProxyAssetSource {project_id=project,audio_category="bgm",project_audio_asset_id=id,audio_candidate_id=id+"-candidate"}};
            Assert.Throws<InvalidOperationException>(()=>GdaiImportedAssetRegistry.Build(new List<GdaiBundleProxyAsset>{asset("same",written.First(),"audio","project"),asset("same",written.Last(),"audio","project")},written,"snapshot"));
            Assert.Throws<InvalidOperationException>(()=>GdaiImportedAssetRegistry.Build(new List<GdaiBundleProxyAsset>{asset("one",written.First(),"audio","project"),asset("two",written.First(),"audio","project")},written,"snapshot"));
            Assert.Throws<InvalidOperationException>(()=>GdaiImportedAssetRegistry.Build(new List<GdaiBundleProxyAsset>{asset("one",written.First(),"sprite","project")},written,"snapshot"));
            Assert.Throws<InvalidOperationException>(()=>GdaiImportedAssetRegistry.Build(new List<GdaiBundleProxyAsset>{asset("one",written.First(),"audio",null)},written,"snapshot"));
        }
        [Test] public void BinaryAssetDtoRoundTripAndBatchPreflightPreserveCandidateLineage()
        {
            var wav=TestWav();var bgm=ImportedAsset("project-bgm-slot","audio","project_bgm","audio/wav","Assets/GDAI_Generated/Audio/BGM/project_theme.wav",wav,"project-one");
            var roundTrip=JsonConvert.DeserializeObject<GdaiBundleProxyAsset>(JsonConvert.SerializeObject(bgm));
            Assert.AreEqual("project-bgm-slot",roundTrip.source.project_audio_asset_id);Assert.AreEqual("project-bgm-slot-candidate",roundTrip.source.audio_candidate_id);Assert.AreEqual("bgm",roundTrip.source.audio_category);
            Assert.IsEmpty(AssetPayloadImporter.ValidateBatch(new List<GdaiBundleProxyAsset>{roundTrip},"project-one"));
            var duplicate=JsonConvert.DeserializeObject<GdaiBundleProxyAsset>(JsonConvert.SerializeObject(roundTrip));duplicate.unity_path="Assets/GDAI_Generated/Audio/BGM/duplicate.wav";
            StringAssert.Contains("duplicate_asset_id",string.Join(" | ",AssetPayloadImporter.ValidateBatch(new List<GdaiBundleProxyAsset>{roundTrip,duplicate},"project-one")));
            var badMedia=JsonConvert.DeserializeObject<GdaiBundleProxyAsset>(JsonConvert.SerializeObject(roundTrip));badMedia.mime_type="image/png";
            StringAssert.Contains("media_mismatch",string.Join(" | ",AssetPayloadImporter.ValidateBatch(new List<GdaiBundleProxyAsset>{badMedia},"project-one")));
            var wrongCandidate=JsonConvert.DeserializeObject<GdaiBundleProxyAsset>(JsonConvert.SerializeObject(roundTrip));wrongCandidate.source.audio_candidate_id=null;
            StringAssert.Contains("registry_bgm_lineage_incomplete",string.Join(" | ",AssetPayloadImporter.ValidateBatch(new List<GdaiBundleProxyAsset>{wrongCandidate},"project-one")));
            var wrongProject=JsonConvert.DeserializeObject<GdaiBundleProxyAsset>(JsonConvert.SerializeObject(roundTrip));wrongProject.source.project_id="project-two";
            StringAssert.Contains("project_lineage_mismatch",string.Join(" | ",AssetPayloadImporter.ValidateBatch(new List<GdaiBundleProxyAsset>{wrongProject},"project-one")));
        }
        [UnityTest] public IEnumerator ProjectBgmImportRegistryAndSourceBoundRunBindExactAudioClip()
        {
            var root=Directory.GetParent(Application.dataPath).FullName;var marker=Path.Combine(root,"Library/GDAI/native-test-scratch.marker");
            if(!File.Exists(marker)||File.ReadAllText(marker)!="gdai-disposable-sdk-native-test-v1")throw new InvalidOperationException("source-bound native tests require an explicitly marked disposable project");
            const string project="generic-project";const string sourcePath="Assets/GDAI_Generated/SourceBoundTests/BgmConsumer.cs";const string bgmPath="Assets/GDAI_Generated/Audio/BGM/project_theme.wav";const string sfxPath="Assets/GDAI_Generated/Audio/SFX/pilot_hit.wav";
            const string bgmSource="public class BgmConsumer : UnityEngine.MonoBehaviour { public UnityEngine.AudioClip music; }";
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);AssetDatabase.DeleteAsset("Assets/GDAI_Project/Generated");AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");AssetDatabase.DeleteAsset(GdaiImportedAssetRegistry.RegistryPath);AssetDatabase.DeleteAsset("Assets/GDAI_Generated/Art/pilot.png");AssetDatabase.DeleteAsset("Assets/GDAI_Generated/Art/enemy.png");AssetDatabase.DeleteAsset(bgmPath);AssetDatabase.DeleteAsset(sfxPath);
            var sourcesChanged=GdaiSourceBoundPlayableFixtures.WriteSourceFiles();Directory.CreateDirectory(Path.GetDirectoryName(sourcePath));if(!File.Exists(sourcePath)||File.ReadAllText(sourcePath)!=bgmSource){File.WriteAllText(sourcePath,bgmSource);sourcesChanged=true;}AssetDatabase.Refresh();if(sourcesChanged)yield return new RecompileScripts();
            var c=GdaiSourceBoundPlayableFixtures.Build();c.source_bound_plan.sources.Add(new GdaiSourceBoundPlayablePlan.Source {path=sourcePath,sha256=GdaiSourceBoundPlayablePlan.Hash(System.Text.Encoding.UTF8.GetBytes(bgmSource))});
            c.source_bound_obligations.Add(new GdaiSourceBoundPlayablePlan.TypeObligation {type="BgmConsumer",source_path=sourcePath,kind="class",hosting="component",bases=new List<string>{"UnityEngine.MonoBehaviour"},fields=new List<GdaiSourceBoundPlayablePlan.FieldObligation>{new GdaiSourceBoundPlayablePlan.FieldObligation {path="music",type="UnityEngine.AudioClip",required_reference=true}}});
            c.source_bound_plan.type_dispositions.Add(new GdaiSourceBoundPlayablePlan.TypeDisposition {type="BgmConsumer",source_path=sourcePath,disposition="hosted",rationale="native BGM registry consumer"});
            var consumer=new GdaiSourceBoundPlayablePlan.ComponentSpec {id="bgm-consumer",type="BgmConsumer",source_path=sourcePath,fields=new List<GdaiSourceBoundPlayablePlan.Field>{new GdaiSourceBoundPlayablePlan.Field {path="music",disposition="reference",reference_kind="asset",target_id="project-bgm-slot"}}};c.source_bound_plan.objects.Single(o=>o.id=="director").components.Add(consumer);
            ((JArray)c.source_bound_context["assets"]).Add(new JObject {["asset_id"]="project-bgm-slot",["unity_path"]=bgmPath,["project_id"]=project,["asset_type"]="audio",["mime_type"]="audio/wav",["project_audio_asset_id"]="project-bgm-slot",["audio_candidate_id"]="project-bgm-slot-candidate",["audio_category"]="bgm"});c.source_bound_context_json=c.source_bound_context.ToString(Formatting.None);c.source_bound_plan.context_sha256=GdaiSourceBoundPlayablePlan.Hash(System.Text.Encoding.UTF8.GetBytes(c.source_bound_context_json));
            var png=SolidPng();var wav=TestWav();var imported=new List<GdaiBundleProxyAsset>{ImportedAsset("pilot-art","sprite","entity_sprite","image/png","Assets/GDAI_Generated/Art/pilot.png",png,project,"pilot-entity"),ImportedAsset("enemy-art","sprite","entity_sprite","image/png","Assets/GDAI_Generated/Art/enemy.png",png,project,"enemy-entity"),ImportedAsset("pilot-hit","audio","entity_sfx","audio/wav",sfxPath,wav,project,"pilot-entity"),ImportedAsset("project-bgm-slot","audio","project_bgm","audio/wav",bgmPath,wav,project)};
            try {
                var missing=GdaiSourceBoundPlayableComposer.Run(c,project,"bgm-missing-registry",new string('d',64),"Assets/Scenes/Main.unity",DateTime.UtcNow);Assert.IsFalse(missing.Completed);StringAssert.Contains("adopted asset imported registry mismatch",missing.Error);
                var summary=AssetPayloadImporter.ImportAll(imported,"bgm-import-snapshot");Assert.IsTrue(summary.Succeeded,summary.Error);Assert.AreEqual(4,summary.Imported,string.Join(" | ",summary.SkippedWithReason));
                var registry=GdaiImportedAssetRegistry.All();Assert.AreEqual(4,registry.Count);var bgm=registry.Single(entry=>entry.asset_id=="project-bgm-slot");Assert.IsTrue(string.IsNullOrEmpty(bgm.entity_id));Assert.AreEqual(project,bgm.source_project_id);Assert.AreEqual(bgmPath,bgm.unity_path);Assert.AreEqual("audio",bgm.asset_type);Assert.AreEqual("audio/wav",bgm.mime_type);Assert.AreEqual(PayloadSha256(wav),bgm.sha256);Assert.AreEqual("project-bgm-slot",bgm.project_audio_asset_id);Assert.AreEqual("project-bgm-slot-candidate",bgm.audio_candidate_id);Assert.AreEqual("bgm",bgm.audio_category);var sfx=registry.Single(entry=>entry.asset_id=="pilot-hit");Assert.AreEqual("sfx",sfx.audio_category);Assert.AreEqual("pilot-entity",sfx.entity_id);Assert.IsTrue(string.IsNullOrEmpty(sfx.audio_candidate_id));
                Assert.IsTrue(GdaiImportedAssetRegistry.TryGetSpriteForEntity("pilot-entity",out var pilotSprite,out var spriteReason),spriteReason);Assert.IsNotNull(pilotSprite);Assert.IsFalse(GdaiImportedAssetRegistry.TryGetSpriteByAssetId("project-bgm-slot",out _,out var bgmSpriteReason));Assert.AreEqual("asset_not_in_registry",bgmSpriteReason);
                var bgmContext=((JArray)c.source_bound_context["assets"]).OfType<JObject>().Single(asset=>(string)asset["asset_id"]=="project-bgm-slot");bgmContext["audio_candidate_id"]="wrong-candidate";c.source_bound_context_json=c.source_bound_context.ToString(Formatting.None);c.source_bound_plan.context_sha256=GdaiSourceBoundPlayablePlan.Hash(System.Text.Encoding.UTF8.GetBytes(c.source_bound_context_json));var mismatched=GdaiSourceBoundPlayableComposer.Run(c,project,"bgm-candidate-mismatch",new string('f',64),"Assets/Scenes/Main.unity",DateTime.UtcNow);Assert.IsFalse(mismatched.Completed);StringAssert.Contains("adopted asset imported lineage mismatch",mismatched.Error);bgmContext["audio_candidate_id"]="project-bgm-slot-candidate";c.source_bound_context_json=c.source_bound_context.ToString(Formatting.None);c.source_bound_plan.context_sha256=GdaiSourceBoundPlayablePlan.Hash(System.Text.Encoding.UTF8.GetBytes(c.source_bound_context_json));
                var result=GdaiSourceBoundPlayableComposer.Run(c,project,"bgm-imported-registry",new string('e',64),"Assets/Scenes/Main.unity",DateTime.UtcNow);Assert.IsTrue(result.Completed,result.Error);
                var objects=GdaiSourceBoundPlayableComposer.ReadObjects(c);var director=(GameObject)objects["director"];var instance=director.GetComponent(GdaiSourceBoundPlayableComposer.Resolve(consumer,"scene"));var expected=AssetDatabase.LoadAssetAtPath<AudioClip>(bgmPath);Assert.IsNotNull(expected);using(var serialized=new SerializedObject(instance))Assert.AreSame(expected,serialized.FindProperty("music").objectReferenceValue);
            } finally {UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);AssetDatabase.DeleteAsset("Assets/GDAI_Project/Generated");AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");AssetDatabase.DeleteAsset(GdaiImportedAssetRegistry.RegistryPath);AssetDatabase.DeleteAsset("Assets/GDAI_Generated/Art/pilot.png");AssetDatabase.DeleteAsset("Assets/GDAI_Generated/Art/enemy.png");AssetDatabase.DeleteAsset(bgmPath);AssetDatabase.DeleteAsset(sfxPath);}
        }
        [UnityTest] public IEnumerator BinaryAssetTransaction_PreflightAndLaterPathFailureLeaveFilesMetaAndRegistryUnchanged()
        {
            var root=Directory.GetParent(Application.dataPath).FullName;var marker=Path.Combine(root,"Library/GDAI/native-test-scratch.marker");
            if(!File.Exists(marker)||File.ReadAllText(marker)!="gdai-disposable-sdk-native-test-v1")throw new InvalidOperationException("binary transaction native tests require an explicitly marked disposable project");
            const string path="Assets/GDAI_Generated/AtomicTests/prior.png";const string blockedParent="Assets/GDAI_Generated/AtomicTests/blocked";const string blockedPath=blockedParent+"/later.wav";const string untouchedPath="Assets/GDAI_Generated/AtomicTests/untouched.wav";var absolute=Path.Combine(root,path);var blockedAbsolute=Path.Combine(root,blockedParent);var registryAbsolute=Path.Combine(root,GdaiImportedAssetRegistry.RegistryPath);AssetDatabase.DeleteAsset(path);AssetDatabase.DeleteAsset(blockedParent);AssetDatabase.DeleteAsset(untouchedPath);AssetDatabase.DeleteAsset(GdaiImportedAssetRegistry.RegistryPath);Directory.CreateDirectory(Path.GetDirectoryName(absolute));var priorBytes=SolidPng(Color.black);File.WriteAllBytes(absolute,priorBytes);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var priorMeta=File.ReadAllBytes(absolute+".meta");Directory.CreateDirectory(Path.GetDirectoryName(registryAbsolute));var priorRegistry=System.Text.Encoding.UTF8.GetBytes("prior-registry");File.WriteAllBytes(registryAbsolute,priorRegistry);AssetDatabase.ImportAsset(GdaiImportedAssetRegistry.RegistryPath,ImportAssetOptions.ForceSynchronousImport);var priorRegistryMeta=File.ReadAllBytes(registryAbsolute+".meta");
            try {
                var png=SolidPng();var wav=TestWav();var replacement=ImportedAsset("atomic-image","sprite","entity_sprite","image/png",path,png,"project-one","pilot-entity");var later=ImportedAsset("atomic-sfx","audio","entity_sfx","audio/wav",blockedPath,wav,"project-one","pilot-entity");
                var duplicateId=ImportedAsset("atomic-image","audio","entity_sfx","audio/wav",untouchedPath,wav,"project-one","pilot-entity");var rejected=AssetPayloadImporter.ImportAll(new List<GdaiBundleProxyAsset>{replacement,duplicateId},"duplicate-id");Assert.IsFalse(rejected.Succeeded);Assert.AreEqual(0,rejected.Imported);Assert.IsFalse(File.Exists(Path.Combine(root,untouchedPath)));CollectionAssert.AreEqual(priorRegistry,File.ReadAllBytes(registryAbsolute));
                var duplicatePath=ImportedAsset("other-image","sprite","entity_sprite","image/png",path,png,"project-one","enemy-entity");rejected=AssetPayloadImporter.ImportAll(new List<GdaiBundleProxyAsset>{replacement,duplicatePath},"duplicate-path");Assert.IsFalse(rejected.Succeeded);Assert.AreEqual(0,rejected.Imported);CollectionAssert.AreEqual(priorBytes,File.ReadAllBytes(absolute));CollectionAssert.AreEqual(priorRegistry,File.ReadAllBytes(registryAbsolute));
                var badMedia=ImportedAsset("bad-media","sprite","entity_sprite","image/png",untouchedPath,png,"project-one","pilot-entity");badMedia.mime_type="audio/wav";rejected=AssetPayloadImporter.ImportAll(new List<GdaiBundleProxyAsset>{badMedia},"bad-media");Assert.IsFalse(rejected.Succeeded);Assert.IsFalse(File.Exists(Path.Combine(root,untouchedPath)));CollectionAssert.AreEqual(priorRegistry,File.ReadAllBytes(registryAbsolute));
                var wrongCandidate=ImportedAsset("wrong-candidate","audio","project_bgm","audio/wav",untouchedPath,wav,"project-one");wrongCandidate.source.audio_candidate_id=null;rejected=AssetPayloadImporter.ImportAll(new List<GdaiBundleProxyAsset>{wrongCandidate},"wrong-candidate");Assert.IsFalse(rejected.Succeeded);Assert.IsFalse(File.Exists(Path.Combine(root,untouchedPath)));CollectionAssert.AreEqual(priorRegistry,File.ReadAllBytes(registryAbsolute));
                File.WriteAllBytes(blockedAbsolute,new byte[]{9,8,7});LogAssert.Expect(LogType.Error,"[GDAI][LayerA][Assets] binary asset transaction failed: The file '"+blockedAbsolute+"' already exists.");var summary=AssetPayloadImporter.ImportAll(new List<GdaiBundleProxyAsset>{replacement,later},"atomic-failure");
                Assert.IsFalse(summary.Succeeded);Assert.AreEqual(0,summary.Imported);StringAssert.Contains("binary asset transaction failed",summary.Error);CollectionAssert.AreEqual(priorBytes,File.ReadAllBytes(absolute));CollectionAssert.AreEqual(priorMeta,File.ReadAllBytes(absolute+".meta"));CollectionAssert.AreEqual(priorRegistry,File.ReadAllBytes(registryAbsolute));CollectionAssert.AreEqual(priorRegistryMeta,File.ReadAllBytes(registryAbsolute+".meta"));CollectionAssert.AreEqual(new byte[]{9,8,7},File.ReadAllBytes(blockedAbsolute));
            } finally {AssetDatabase.DeleteAsset(path);AssetDatabase.DeleteAsset(blockedParent);if(File.Exists(blockedAbsolute))File.Delete(blockedAbsolute);if(File.Exists(blockedAbsolute+".meta"))File.Delete(blockedAbsolute+".meta");AssetDatabase.DeleteAsset(untouchedPath);AssetDatabase.DeleteAsset(GdaiImportedAssetRegistry.RegistryPath);AssetDatabase.Refresh();}
            yield break;
        }
        [Test] public void ExportBinding_AdmitsPairedUrp2DAndRetainsLegacyBuiltIn()
        {
            var binding=new GdaiProjectBindingData {schema_version=GdaiProjectBinding.ExpectedSchema,project_id="00000000-0000-0000-0000-000000000001",target_engine=GdaiProjectBinding.ExpectedEngine,unity_version="6000.5.1f1",render_pipeline=GdaiProjectBinding.ExpectedPipeline,plugin_version=GdaiCoherentBundleWindow.PluginVersion,generated_owned_paths=new[]{"Assets/GDAI_Generated"}};
            Assert.IsNull(GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));
            binding.render_pipeline=GdaiProjectBinding.ExpectedSourceBoundPipeline;binding.template_profile_id=GdaiProjectBinding.ExpectedSourceBoundTemplateProfile;binding.code_bundle_snapshot_id="00000000-0000-0000-0000-000000000002";
            Assert.IsNull(GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));
        }
        [Test] public void ExportBinding_RejectsIncompleteOrContradictoryUrp2DProfiles()
        {
            var binding=new GdaiProjectBindingData {schema_version=GdaiProjectBinding.ExpectedSchema,project_id="00000000-0000-0000-0000-000000000001",target_engine=GdaiProjectBinding.ExpectedEngine,unity_version="6000.5.1f1",render_pipeline=GdaiProjectBinding.ExpectedSourceBoundPipeline,plugin_version=GdaiCoherentBundleWindow.PluginVersion,generated_owned_paths=new[]{"Assets/GDAI_Generated"}};
            Assert.AreEqual("BINDING_TEMPLATE_PROFILE_REQUIRED: urp_2d",GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));
            binding.template_profile_id="unknown.profile";Assert.AreEqual("BINDING_UNKNOWN_TEMPLATE_PROFILE: unknown.profile",GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));
            binding.template_profile_id=GdaiProjectBinding.ExpectedSourceBoundTemplateProfile;binding.render_pipeline=GdaiProjectBinding.ExpectedPipeline;Assert.AreEqual("BINDING_TEMPLATE_PROFILE_PIPELINE_MISMATCH: built_in",GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));
            foreach(var pipeline in new[]{"urp","URP3D","arbitrary"}) {binding.render_pipeline=pipeline;Assert.AreEqual("BINDING_TEMPLATE_PROFILE_PIPELINE_MISMATCH: "+pipeline,GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));}
            binding.render_pipeline=GdaiProjectBinding.ExpectedSourceBoundPipeline;
            foreach(var snapshot in new[]{null,"","00000000-0000-0000-0000-000000000000","not-a-uuid"}) {binding.code_bundle_snapshot_id=snapshot;Assert.AreEqual("BINDING_BAD_CODE_BUNDLE_SNAPSHOT_UUID",GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));}
            binding.code_bundle_snapshot_id="00000000-0000-0000-0000-000000000002";binding.plugin_version="0.1.0-alpha.8.10";Assert.AreEqual("BINDING_PLUGIN_VERSION_MISMATCH: 0.1.0-alpha.8.10",GdaiProjectBinding.Validate(binding,GdaiCoherentBundleWindow.PluginVersion,"6000.5.1f1"));
        }
        [Test] public void Revision2Contract_ImportsReachableTextCurveReferencesAndOpaqueSensitivePolicy()
        {
            var c=GdaiSourceBoundPlayableFixtures.BuildRevision2();var parsed=GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c));Assert.IsTrue(parsed.Ok,parsed.Summary);
            var nested=c.source_bound_obligations.Single(t=>t.type=="FixtureRevisionTwo.RevisionTwoHost.TextEntry");Assert.AreEqual("data",c.source_bound_plan.type_dispositions.Single(d=>d.type==nested.type).disposition);
            var dto=c.source_bound_obligations.Single(t=>t.type=="FixtureRevisionTwo.JsonSaveData");Assert.IsEmpty(dto.fields);Assert.AreEqual("controller_constructed",c.source_bound_plan.type_dispositions.Single(d=>d.type==dto.type).disposition);
            var component=c.source_bound_plan.objects.SelectMany(o=>o.components).Single(x=>x.id=="revision-two-host");Assert.IsTrue(component.fields.Any(f=>f.path=="label")&&component.fields.Any(f=>f.path=="entries")&&component.fields.Any(f=>f.path=="curve")&&component.fields.Any(f=>f.path=="audio"));
            var opaque=component.fields.Single(f=>f.path=="syntheticCredential");Assert.AreEqual("preserve_external",opaque.disposition);Assert.IsNull(opaque.value);Assert.IsNull(opaque.target_id);
            opaque.disposition="value";opaque.value=new JValue("synthetic-payload");Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok);
            Assert.Throws<InvalidOperationException>(()=>GdaiSourceBoundPlayableComposer.BindFields(null,component,c,null,null,null,false));
            c=GdaiSourceBoundPlayableFixtures.BuildRevision2();component=c.source_bound_plan.objects.SelectMany(o=>o.components).Single(x=>x.id=="revision-two-host");component.fields.RemoveAll(f=>f.path=="syntheticCredential");Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok,"partial opaque coverage must refuse");
        }
        [Test] public void Revision2SensitiveCamelCase_OpaquePolicyAcceptsWithoutPayloadAndRejectsInjection()
        {
            foreach(var name in new[]{"apiKey","signingKey","IV"}) {
                var c=GdaiSourceBoundPlayableFixtures.BuildRevision2();var host=c.source_bound_obligations.Single(t=>t.type=="FixtureRevisionTwo.RevisionTwoHost");var obligation=host.fields.Single(f=>f.path=="syntheticCredential");obligation.path=name;
                var field=c.source_bound_plan.objects.SelectMany(o=>o.components).Single(x=>x.id=="revision-two-host").fields.Single(f=>f.path=="syntheticCredential");field.path=name;
                var json=JsonConvert.SerializeObject(c);var admitted=GdaiPlayableContract.Parse(json);Assert.IsTrue(admitted.Ok,name+": "+admitted.Summary);Assert.IsNull(JObject.Parse(json)["source_bound_plan"]["objects"].SelectMany(o=>o["components"]).SelectMany(component=>component["fields"]).OfType<JObject>().Single(candidate=>(string)candidate["path"]==name).Property("value"));
                field.value=new JValue("synthetic-payload");Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok,name+" payload must reject");
            }
        }
        [Test] public void Revision2TypedValues_ConsumeEnumInventorySingleBoundsAndNamedLayerList()
        {
            var c=GdaiSourceBoundPlayableFixtures.BuildRevision2();var host=c.source_bound_obligations.Single(t=>t.type=="FixtureRevisionTwo.RevisionTwoHost");var component=c.source_bound_plan.objects.SelectMany(o=>o.components).Single(x=>x.id=="revision-two-host");
            var mode=new GdaiSourceBoundPlayablePlan.TypeObligation {type="FixtureRevisionTwo.RevisionTwoHost.Mode",source_path=GdaiSourceBoundPlayableFixtures.Revision2SourcePath,kind="enum",hosting="none",bases=new System.Collections.Generic.List<string>(),enum_members=new System.Collections.Generic.List<string>{"A","B"},fields=new System.Collections.Generic.List<GdaiSourceBoundPlayablePlan.FieldObligation>()};
            c.source_bound_obligations.Add(mode);c.source_bound_plan.type_dispositions.Add(new GdaiSourceBoundPlayablePlan.TypeDisposition {type=mode.type,source_path=mode.source_path,disposition="enum",rationale="exact nested enum"});
            host.fields.AddRange(new[]{new GdaiSourceBoundPlayablePlan.FieldObligation {path="mode",type=mode.type},new GdaiSourceBoundPlayablePlan.FieldObligation {path="singleMaximum",type="float"},new GdaiSourceBoundPlayablePlan.FieldObligation {path="singleMinimum",type="System.Single"},new GdaiSourceBoundPlayablePlan.FieldObligation {path="doubleWide",type="double"},new GdaiSourceBoundPlayablePlan.FieldObligation {path="layers",type="UnityEngine.LayerMask"}});
            component.fields.AddRange(new[]{new GdaiSourceBoundPlayablePlan.Field {path="mode",disposition="value",value=new JValue("A")},new GdaiSourceBoundPlayablePlan.Field {path="singleMaximum",disposition="value",value=new JValue((double)float.MaxValue)},new GdaiSourceBoundPlayablePlan.Field {path="singleMinimum",disposition="value",value=new JValue((double)float.Epsilon)},new GdaiSourceBoundPlayablePlan.Field {path="doubleWide",disposition="value",value=new JValue(1e100)},new GdaiSourceBoundPlayablePlan.Field {path="layers",disposition="value",value=new JArray("Default")}});
            var parsed=GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c));Assert.IsTrue(parsed.Ok,parsed.Summary);Assert.IsTrue(GdaiSourceBoundPlayablePlan.CompiledEnumMembersMatch(typeof(KindEnum),new[]{"A","B"}));Assert.IsFalse(GdaiSourceBoundPlayablePlan.CompiledEnumMembersMatch(typeof(KindEnum),new[]{"A","NotAMode"}));
            component.fields.Single(field=>field.path=="mode").value=new JValue("NotAMode");Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok,"unknown enum member must reject");component.fields.Single(field=>field.path=="mode").value=new JValue("A");
            component.fields.Single(field=>field.path=="singleMaximum").value=new JValue(1e100);Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok,"Single overflow must reject");component.fields.Single(field=>field.path=="singleMaximum").value=new JValue((double)float.MaxValue);
            component.fields.Single(field=>field.path=="singleMinimum").value=new JValue(1e-100);Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok,"Single underflow must reject");component.fields.Single(field=>field.path=="singleMinimum").value=new JValue((double)float.Epsilon);
            component.fields.Single(field=>field.path=="layers").value=new JValue(1);Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok,"numeric LayerMask must reject");
        }
        [Test] public void Revision2UnicodeEnum_UsesCompiledInventoryAndExactTypedValue()
        {
            var c=GdaiSourceBoundPlayableFixtures.BuildRevision2();var host=c.source_bound_obligations.Single(t=>t.type=="FixtureRevisionTwo.RevisionTwoHost");var component=c.source_bound_plan.objects.SelectMany(o=>o.components).Single(x=>x.id=="revision-two-host");
            var type="FixtureRevisionTwo.RevisionTwoHost.EnemyState";var members=new[]{"追踪","激怒前摇","激怒冲锋","冷却"};
            c.source_bound_obligations.Add(new GdaiSourceBoundPlayablePlan.TypeObligation {type=type,source_path=GdaiSourceBoundPlayableFixtures.Revision2SourcePath,kind="enum",hosting="none",bases=new System.Collections.Generic.List<string>(),enum_members=members.ToList(),fields=new System.Collections.Generic.List<GdaiSourceBoundPlayablePlan.FieldObligation>()});
            c.source_bound_plan.type_dispositions.Add(new GdaiSourceBoundPlayablePlan.TypeDisposition {type=type,source_path=GdaiSourceBoundPlayableFixtures.Revision2SourcePath,disposition="enum",rationale="exact Unicode enum"});host.fields.Add(new GdaiSourceBoundPlayablePlan.FieldObligation {path="enemyState",type=type});component.fields.Add(new GdaiSourceBoundPlayablePlan.Field {path="enemyState",disposition="value",value=new JValue("激怒冲锋")});
            var parsed=GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c));Assert.IsTrue(parsed.Ok,parsed.Summary);Assert.IsTrue(GdaiSourceBoundPlayablePlan.CompiledEnumMembersMatch(typeof(UnicodeEnemyState),members));Assert.IsFalse(GdaiSourceBoundPlayablePlan.CompiledEnumMembersMatch(typeof(UnicodeEnemyState),members.Concat(new[]{"虚构状态"})));
            component.fields.Single(field=>field.path=="enemyState").value=new JValue("虚构状态");Assert.IsFalse(GdaiPlayableContract.Parse(JsonConvert.SerializeObject(c)).Ok,"invented Unicode enum member must reject");
        }
    }
}

namespace GDAI.Bridge.Editor.Tests
{
    // Generic contract tests; no project-specific source or accepted product configuration.
    public class GdaiDynamicEnemyPrefabContractTests
    {
        public static GdaiPlayableContract DynamicEnemyPrefabFixture()
        {
            var c=JsonConvert.DeserializeObject<GdaiPlayableContract>(@"{""schema_version"":""gdai.unity.playable_contract.v1"",""contract_revision"":4,""profile_id"":""unity.source_bound_demo.v1"",""canonical_scene"":{""path"":""Assets/Scenes/Main.unity"",""add_to_build_settings"":true},""source_bound_context"":{""accepted_designs"":[{""module_id"":""generic"",""accepted_design_identity"":""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"",""design"":{""content"":""generic accepted design""},""source_path"":""Assets/GDAI_Generated/GameIntegrationController.cs""}],""arena"":{""width"":960,""height"":540,""coordinate_space"":""canvas_top_left_y_down_px""},""roles"":[{""role"":""player"",""entity_id"":""pilot-entity""},{""role"":""enemy"",""entity_id"":""enemy-entity""}],""placements"":[{""entity_id"":""pilot-entity"",""x"":580,""y"":170,""scale"":2}],""assets"":[{""asset_id"":""pilot-art"",""entity_id"":""pilot-entity"",""unity_path"":""Assets/GDAI_Generated/Art/Pilot.png""},{""asset_id"":""enemy-art"",""entity_id"":""enemy-entity"",""unity_path"":""Assets/GDAI_Generated/Art/Enemy.png""}]},""source_bound_plan"":{""revision"":2,""context_sha256"":""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"",""sources"":[{""path"":""Assets/GDAI_Generated/GameIntegrationController.cs"",""sha256"":""bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb""}],""objects"":[{""id"":""pilot"",""name"":""Pilot"",""kind"":""scene"",""role"":""player"",""entity_id"":""pilot-entity"",""position"":""placement"",""components"":[{""id"":""pilot-sprite"",""type"":""UnityEngine.SpriteRenderer"",""source_path"":""builtin"",""native_defaults"":true,""fields"":[{""path"":""sprite"",""disposition"":""reference"",""reference_kind"":""asset"",""target_id"":""pilot-art""}]}]},{""id"":""enemy"",""name"":""Enemy"",""kind"":""prefab"",""role"":""enemy"",""entity_id"":""enemy-entity"",""position"":""neutral"",""asset_path"":""Assets/GDAI_Project/Generated/Enemy.prefab"",""components"":[{""id"":""enemy-sprite"",""type"":""UnityEngine.SpriteRenderer"",""source_path"":""builtin"",""native_defaults"":true,""fields"":[{""path"":""sprite"",""disposition"":""reference"",""reference_kind"":""asset"",""target_id"":""enemy-art""}]}]},{""id"":""manager"",""name"":""Manager"",""kind"":""scene"",""role"":""system"",""position"":""neutral"",""components"":[{""id"":""controller"",""type"":""GameIntegrationController"",""source_path"":""Assets/GDAI_Generated/GameIntegrationController.cs"",""native_defaults"":false,""fields"":[{""path"":""enemyPrefab"",""disposition"":""reference"",""reference_kind"":""prefab"",""target_id"":""enemy""}]}]}],""type_dispositions"":[{""type"":""GameIntegrationController"",""source_path"":""Assets/GDAI_Generated/GameIntegrationController.cs"",""disposition"":""hosted""}],""unresolved"":[]},""source_bound_obligations"":[{""type"":""GameIntegrationController"",""source_path"":""Assets/GDAI_Generated/GameIntegrationController.cs"",""kind"":""class"",""hosting"":""component"",""abstract"":false,""fields"":[{""path"":""enemyPrefab"",""type"":""UnityEngine.GameObject"",""required_reference"":true}]}]}");
            Rebind(c);return c;
        }
        private static void Rebind(GdaiPlayableContract c) {
            c.source_bound_context_json=c.source_bound_context.ToString(Formatting.None);
            c.source_bound_plan.context_sha256=GdaiSourceBoundPlayablePlan.Hash(System.Text.Encoding.UTF8.GetBytes(c.source_bound_context_json));
        }
        [Test] public void DynamicEnemyPrefab_AcceptsWithoutPlacementAndRetainsPlayerGeometry() {
            var c=DynamicEnemyPrefabFixture();
            Assert.IsEmpty(GdaiSourceBoundPlayablePlan.Validate(c));
            var writes=GdaiSourceBoundPlayableComposer.ImplicitGeometryWrites(c);
            Assert.IsFalse(writes.Any(w=>w.host_id=="enemy"));
            var position=writes.Single(w=>w.host_id=="pilot"&&w.field=="position");
            Assert.AreEqual(1f,(float)position.expected["x"]);Assert.AreEqual(1f,(float)position.expected["y"]);
            Assert.AreEqual(2f,(float)writes.Single(w=>w.host_id=="pilot"&&w.field=="localScale").expected["x"]);
            Assert.AreEqual(1,((JArray)c.source_bound_context["placements"]).Count);
        }
        [TestCase("scene_enemy")][TestCase("scene_player")][TestCase("prefab_player")]
        [TestCase("missing_role")][TestCase("wrong_role")][TestCase("wrong_entity")]
        [TestCase("missing_asset")][TestCase("missing_placement")][TestCase("duplicate_placement")]
        [TestCase("unowned_path")][TestCase("wrong_reference")][TestCase("wrong_host")]
        [TestCase("unconsumed_prefab")]
        public void DynamicEnemyPrefab_RejectsInvalidSemantics(string change) {
            var c=DynamicEnemyPrefabFixture();var enemy=c.source_bound_plan.objects.Single(o=>o.id=="enemy");var pilot=c.source_bound_plan.objects.Single(o=>o.id=="pilot");
            var use=c.source_bound_plan.objects.Single(o=>o.id=="manager").components[0].fields[0];
            switch(change) {
                case "scene_enemy":enemy.kind="scene";break;
                case "scene_player":pilot.position="neutral";break;
                case "prefab_player":pilot.kind="prefab";pilot.position="neutral";pilot.asset_path="Assets/GDAI_Project/Generated/Pilot.prefab";break;
                case "missing_role":enemy.role=null;break;
                case "wrong_role":enemy.role="npc";break;
                case "wrong_entity":enemy.entity_id="unknown";break;
                case "missing_asset":((JArray)c.source_bound_context["assets"]).RemoveAt(1);break;
                case "missing_placement":enemy.position="placement";break;
                case "duplicate_placement":((JArray)c.source_bound_context["placements"]).Add(c.source_bound_context["placements"][0].DeepClone());break;
                case "unowned_path":enemy.asset_path="Assets/External/Enemy.prefab";break;
                case "wrong_reference":use.target_id="missing";break;
                case "wrong_host":enemy.kind="config";enemy.asset_path="Assets/GDAI_Project/Generated/Enemy.asset";break;
                case "unconsumed_prefab":use.reference_kind="object";break;
                default:throw new InvalidOperationException(change);
            }
            Rebind(c);Assert.IsNotEmpty(GdaiSourceBoundPlayablePlan.Validate(c),change);
        }
        [Test] public void DynamicEnemyPrefab_ExplicitPlacementStillRequiresOnePersistedMatch() {
            var c=DynamicEnemyPrefabFixture();var enemy=c.source_bound_plan.objects.Single(o=>o.id=="enemy");enemy.position="placement";
            Assert.IsNotEmpty(GdaiSourceBoundPlayablePlan.Validate(c));
            var placement=JObject.Parse(@"{'entity_id':'enemy-entity','x':25,'y':50,'scale':1}");
            ((JArray)c.source_bound_context["placements"]).Add(placement);Rebind(c);
            Assert.IsEmpty(GdaiSourceBoundPlayablePlan.Validate(c));
            ((JArray)c.source_bound_context["placements"]).Add(placement.DeepClone());Rebind(c);
            Assert.IsNotEmpty(GdaiSourceBoundPlayablePlan.Validate(c));
        }
        [TestCase("12345678-1234-4234-8234-123456789abc",false)]
        [TestCase("12345678-1234-4234-8234-123456789abc",true)]
        [TestCase("abcdefab-1234-4234-8234-123456789abc",false)]
        [TestCase("abcdefab-1234-4234-8234-123456789abc",true)]
        [TestCase("pilot-art",false)][TestCase("pilot-art",true)]
        public void AssetIdentity_ExactAdoptedStorageIdentityAccepted(string id,bool nested) {
            var c=DynamicEnemyPrefabFixture();
            c.source_bound_context["assets"][0]["asset_id"]=id;
            var f=c.source_bound_plan.objects.Single(o=>o.id=="pilot").components[0].fields[0];
            f.target_id=id;
            if(nested) {f.disposition="value";f.value=new JObject {["reference_kind"]="asset",["target_id"]=id};f.reference_kind=null;f.target_id=null;}
            Rebind(c);Assert.IsEmpty(GdaiSourceBoundPlayablePlan.Validate(c));
        }
        [TestCase("unknown",false)][TestCase("unknown",true)]
        [TestCase("case",false)][TestCase("case",true)]
        [TestCase("duplicate",false)][TestCase("duplicate",true)]
        [TestCase("empty",false)][TestCase("empty",true)]
        public void AssetIdentity_RejectsMissingWrongOrAmbiguousMembership(string change,bool nested) {
            const string id="12345678-1234-4234-8234-123456789abc";
            var c=DynamicEnemyPrefabFixture();var assets=(JArray)c.source_bound_context["assets"];
            assets[0]["asset_id"]=id;
            var f=c.source_bound_plan.objects.Single(o=>o.id=="pilot").components[0].fields[0];
            f.target_id=change=="unknown"?"92345678-1234-4234-8234-123456789abc":change=="case"?id.ToUpperInvariant():change=="empty"?"":id;
            if(change=="duplicate")assets.Add(assets[0].DeepClone());
            if(nested){f.disposition="value";f.value=new JObject {["reference_kind"]="asset",["target_id"]=f.target_id};f.reference_kind=null;f.target_id=null;}
            Rebind(c);Assert.IsNotEmpty(GdaiSourceBoundPlayablePlan.Validate(c),change);
        }
        [TestCase("object")][TestCase("component")][TestCase("transform")][TestCase("prefab")][TestCase("config")]
        public void AssetIdentity_DoesNotWidenOwnedReferenceNames(string kind) {
            var c=DynamicEnemyPrefabFixture();var enemy=c.source_bound_plan.objects.Single(o=>o.id=="enemy");enemy.id="123owned";
            var f=c.source_bound_plan.objects.Single(o=>o.id=="manager").components[0].fields[0];f.reference_kind=kind;f.target_id=enemy.id;f.target_component=enemy.components[0].id;
            var errors=GdaiSourceBoundPlayablePlan.Validate(c);
            Assert.Contains("object id/name duplicate or invalid",errors);Assert.Contains("reference intent missing",errors);
        }
        [Test] public void AssetIdentity_DoesNotWidenComponentNames() {
            var c=DynamicEnemyPrefabFixture();c.source_bound_plan.objects[0].components[0].id="123component";
            Assert.Contains("component identity/source invalid",GdaiSourceBoundPlayablePlan.Validate(c));
        }
    }
}
