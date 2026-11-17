using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using UnityEditor;
namespace Game.Editor.Barrack { public static class BarrackResourceProbe {
[Serializable] class Binding {public string renderer,assetKey;}
[Serializable] class Plan {public string[] paths; public string sourceAssetName;public Binding[] meshBindings;}
[Serializable] class Item {public string name,shader;public bool supported;public string[] passes;public int missingTextures;}
[Serializable] class Report {public string status;public int renderers,meshes,materials,missingMaterials,missingBones;public Item[] items;public string error;}
public static void Run(){var previousPipeline=Shader.globalRenderPipeline;Shader.globalRenderPipeline="UniversalPipeline";var report=new Report();var bundles=new List<AssetBundle>();try{
var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Environment.GetEnvironmentVariable("GF2_IMPORT_PLAN")));
foreach(var path in plan.paths){var b=AssetBundle.LoadFromFile(path);if(b==null)throw new Exception("Bundle load failed: "+path);bundles.Add(b);}
var source=bundles.Single(b=>b.GetAllAssetNames().Contains(plan.sourceAssetName)).LoadAsset<GameObject>(plan.sourceAssetName);
foreach(var binding in plan.meshBindings){var renderer=source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name==binding.renderer);renderer.sharedMesh=bundles.Single(b=>b.GetAllAssetNames().Contains(binding.assetKey)).LoadAsset<Mesh>(binding.assetKey);}
var rr=source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.EndsWith("lod0",StringComparison.OrdinalIgnoreCase)).ToArray();
report.renderers=rr.Length;report.meshes=rr.Count(r=>r.sharedMesh!=null);report.materials=rr.Sum(r=>r.sharedMaterials.Count(m=>m!=null));report.missingMaterials=rr.Sum(r=>r.sharedMaterials.Count(m=>m==null));report.missingBones=rr.Sum(r=>r.bones.Count(b=>b==null));
report.items=rr.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().Select(m=>new Item{name=m.name,shader=m.shader==null?null:m.shader.name,supported=m.shader!=null&&m.shader.isSupported,passes=Enumerable.Range(0,m.passCount).Select(i=>m.GetPassName(i)).ToArray(),missingTextures=m.GetTexturePropertyNames().Count(n=>m.GetTexture(n)==null)}).ToArray();
report.status=report.renderers>0&&report.meshes==report.renderers&&report.missingMaterials==0&&report.missingBones==0?"source-references-resolved":"incomplete-source";
}catch(Exception ex){report.status="failed";report.error=ex.ToString();}finally{File.WriteAllText(Environment.GetEnvironmentVariable("GF2_PROBE_REPORT"),JsonUtility.ToJson(report,true));foreach(var b in bundles)b.Unload(true);Shader.globalRenderPipeline=previousPipeline;}
Debug.Log("BARRACK_RESOURCE_PROBE "+report.status);if(report.status=="failed")throw new Exception(report.error);
}}}
