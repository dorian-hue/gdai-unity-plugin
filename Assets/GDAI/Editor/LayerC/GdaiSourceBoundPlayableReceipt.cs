using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerB;
using GDAI.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC
{
    /// Reads saved scene and persistent prefab/config assets, never a composer success flag.
    public static class GdaiSourceBoundPlayableReceipt
    {
        public static GdaiPlayableReceipt Build(GdaiPlayableContract c,string project,string snapshot,string sha,string scene,DateTime now,bool requireActive=false)
        {
            var r=new GdaiPlayableReceipt {project_id=project,snapshot_id=snapshot,contract_revision=c.contract_revision,contract_sha256=sha,generated_at=now.ToString("o"),manual_assembly_steps=0};
            void Check(string key,Action check) {
                try {check();r.checks.Add(new GdaiReceiptCheck {key=key,expected="source-bound identity/serialized readback",actual="verified",pass=true});}
                catch(Exception ex) {r.checks.Add(new GdaiReceiptCheck {key=key,expected="source-bound identity/serialized readback",actual="failed",pass=false});r.failures.Add(key+": "+ex.Message);}
            }
            Check("contract",()=>{var errors=GdaiSourceBoundPlayablePlan.Validate(c);if(errors.Count>0)throw new InvalidOperationException(string.Join(";",errors));});
            Check("saved_scene",()=>{if(scene!=c.canonical_scene.path||!File.Exists(scene))throw new InvalidOperationException("scene path missing/mismatch");EditorSceneManager.OpenScene(scene,OpenSceneMode.Single);if(!EditorBuildSettings.scenes.Any(s=>s.path==scene&&s.enabled))throw new InvalidOperationException("Build Settings scene missing");});
            Check("compiled_source_identity",()=>{if(!GdaiSourceBoundPlayablePlan.Ready(c,out var error))throw new InvalidOperationException(error);});
            Check("independent_compiled_obligations",()=>GdaiSourceBoundPlayableComposer.VerifyCompiledObligations(c));
            Check("ownership",()=>{if(!GdaiPlayableOwnershipManifest.VerifySourceBound(c,project,snapshot,sha,out var error))throw new InvalidOperationException(error);});
            Dictionary<string,UnityEngine.Object> objects=null;
            Check("all_owned_hosts",()=>objects=GdaiSourceBoundPlayableComposer.ReadObjects(c));
            if(objects!=null)Check("implicit_geometry",()=>GdaiSourceBoundPlayableComposer.VerifyImplicitGeometry(c,objects,requireActive));
            if(objects!=null)foreach(var host in c.source_bound_plan.objects) {
                Check("host:"+host.id,()=>{
                    if(host.kind!="scene")return;
                    var go=(GameObject)objects[host.id];var marker=go.GetComponent<GdaiGeneratedPlayableMarker>();
                    if(marker==null||marker.profileId!=c.profile_id||marker.snapshotId!=snapshot||marker.ownedRole!=host.id)throw new InvalidOperationException("host ownership mismatch");
                    if(requireActive&&!go.activeSelf||!requireActive&&go.activeSelf)throw new InvalidOperationException("inactive/activation ordering mismatch");

                });
                foreach(var comp in host.components)Check("fields:"+comp.id,()=> {
                    GameObject contents=null;UnityEngine.Object target=objects[host.id];
                    try {
                        if(host.kind=="prefab"){contents=PrefabUtility.LoadPrefabContents(host.asset_path);target=contents;if(!contents.activeSelf)throw new InvalidOperationException("bound prefab must be activated for runtime instantiation");}
                        var type=GdaiSourceBoundPlayableComposer.Resolve(comp,host.kind);
                        var instance=target is GameObject go?go.GetComponent(type):target;
                        if(instance==null||instance.GetType()!=type)throw new InvalidOperationException("saved component type missing");
                        GdaiSourceBoundPlayableComposer.CheckCoverage(instance,comp,c.source_bound_plan.revision);
                        GdaiSourceBoundPlayableComposer.BindFields(instance,comp,c,objects,host,target,false);
                    }finally {if(contents!=null)PrefabUtility.UnloadPrefabContents(contents);}
                });
            }
            r.status=r.failures.Count==0?"PASS":"FAIL";return r;
        }
    }
}
