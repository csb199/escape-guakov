using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Game.HotUpdate.Barrack;
namespace Game.Editor.Barrack
{
 [InitializeOnLoad] public static class BarrackLightingValidation
 {
  const string Key="Barrack.LightingValidation";
  static bool running; static string root; static StreamWriter csv; static Texture2D readback;
  static BarrackLightingValidation(){EditorApplication.update+=Tick;}
  public static void Run(){AssetDatabase.Refresh();BarrackStageImporter.Prepare();BarrackStartup.Open();SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;}
  static async void Tick()
  {
   if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||running)return;
   var host=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();if(host==null)return;if(host.Failure!=null){SessionState.SetBool(Key,false);Debug.LogError(host.Failure);EditorApplication.Exit(1);return;}if(!host.Ready)return;running=true;
   root="LocalOnly/Barrack/lighting-"+(Environment.GetEnvironmentVariable("GF2_LIGHTING_LABEL")??"probe")+"/";Directory.CreateDirectory(root);
   try
   {
    csv=new StreamWriter(root+"frames.csv");csv.WriteLine("character,mode,frame,rawMean,finalMean,rawDelta,finalDelta,mainLight,lightCount,inputMean,inputDelta,shadowMean,reflectionMean,adaptedR,probeR,probeY,anchorX,anchorY,anchorZ,probeCount");
    var target=new RenderTexture(640,360,24,RenderTextureFormat.ARGBHalf);target.Create();host.displayCamera.targetTexture=target;host.View.SetVisible(false);
    await Task.Delay(1000);var pipeline=(BarrackRenderPipeline)RenderPipelineManager.currentPipeline;pipeline.CaptureDiagnostics=true;
    foreach(int id in new[]{1,9,1,9})
    {
     host.FreezeAnimation(false);await host.SelectCharacterAsync(id);host.Interaction.Tick(100);host.Interaction.Tick(100);host.PlayAction(host.characters[id].ActionClips()[BarrackAction.Idle]);
     var skins=host.GetComponentsInChildren<SkinnedMeshRenderer>();
     var lighting=host.GetComponentInChildren<BarrackCharacterLighting>();
     var native=LightmapSettings.lightProbes;
     if(native==null||native.count!=8)throw new Exception("Expected eight source probes");
     for(int i=0;i<native.count;i++)
     {
      var exact=lighting.Sample(native.positions[i]);
      for(int c=0;c<3;c++)for(int k=0;k<9;k++)if(Mathf.Abs(exact[c,k]-native.bakedProbes[i][c,k])>.00001f)throw new Exception("Source probe coefficient mismatch");
     }
     foreach(string mode in new[]{"idle","entrance"})
     {
      if(lighting==null||skins.Any(r=>r.lightProbeUsage!=LightProbeUsage.CustomProvided))throw new Exception("Character SH adapter missing");
      var below=lighting.Sample(new Vector3(.03f,.92999f,-.04f));var above=lighting.Sample(new Vector3(.03f,.93001f,-.04f));
      for(int c=0;c<3;c++)for(int k=0;k<9;k++)if(Mathf.Abs(below[c,k]-above[c,k])>.0001f)throw new Exception("Discontinuous hull boundary");
      var clipName=host.characters[id].ActionClips()[mode=="idle"?BarrackAction.Idle:BarrackAction.Entrance];host.PlayAction(clipName);
      var duration=Resources.FindObjectsOfTypeAll<AnimationClip>().First(c=>c.name==clipName).length;
      pipeline.FrozenTime=1;float[] prevRaw=null,prevFinal=null,prevInput=null;
      for(int frame=0;frame<90;frame++)
      {
       host.SampleValidationPose(mode=="idle"?1+frame*.05:duration*frame/89);
       int unityFrame=Time.frameCount;while(Time.frameCount==unityFrame)await Task.Delay(1);
       // Observe the natural camera frame; do not advance TAA a second time.
       var anchor=skins[0].probeAnchor!=null?skins[0].probeAnchor.position:skins[0].bounds.center;LightProbes.GetInterpolatedProbe(anchor,skins[0],out var probe);var input=Read(pipeline.InputDiagnostic);if(frame%5==0)File.WriteAllBytes(root+host.CharacterId+"-"+mode+"-input-"+frame+".png",readback.EncodeToPNG());var shadow=Read(pipeline.ShadowDiagnostic).Average();var reflection=Read(pipeline.ReflectionDiagnostic).Average();var raw=Read(pipeline.LatestRaw(host.displayCamera));var final=Read(pipeline.LatestFrame(host.displayCamera));
       csv.WriteLine(FormattableString.Invariant($"{host.CharacterId},{mode},{frame},{raw.Average()},{final.Average()},{Delta(raw,prevRaw)},{Delta(final,prevFinal)},{Shader.GetGlobalColor("_MainLightColor").maxColorComponent},{Shader.GetGlobalVector("_AdditionalLightsCount").x},{input.Average()},{Delta(input,prevInput)},{shadow},{reflection},{lighting.Sample(anchor)[0,0]},{probe[0,0]},{skins[0].bounds.center.y},{anchor.x},{anchor.y},{anchor.z},{LightmapSettings.lightProbes?.count}"));csv.Flush();
       if(frame%10==0)File.WriteAllBytes(root+host.CharacterId+"-"+mode+"-"+frame+".png",readback.EncodeToPNG());
       if(mode=="idle"&&prevInput!=null&&Mathf.Abs(input.Average()-prevInput.Average())>.003f)throw new Exception("Idle lighting discontinuity");
       prevRaw=raw;prevFinal=final;prevInput=input;
      }
     }
    }
    host.displayCamera.targetTexture=null;await host.StopAsync();target.Release();UnityEngine.Object.DestroyImmediate(target);
    if(AssetBundle.GetAllLoadedAssetBundles().Any())throw new Exception("Native leases leaked");
    File.WriteAllText(root+"result.txt","PASS "+DateTime.Now.ToString("O")+"\n720 natural camera samples; four selections; idle + entrance; original 8x27 coefficients; hull boundary continuity; idle input mean step <= .003; character CustomProvided bindings; native leases released");SessionState.SetBool(Key,false);csv.Dispose();EditorApplication.Exit(0);
   }
   catch(Exception e){csv?.Dispose();File.WriteAllText(root+"result.txt","FAIL "+e);SessionState.SetBool(Key,false);Debug.LogException(e);EditorApplication.Exit(1);}
  }
  static float Delta(float[] a,float[] b)=>b==null?0:a.Select((v,i)=>Mathf.Abs(v-b[i])).Average();
  static float[] Read(RenderTexture rt)
  {
   if(readback==null||readback.width!=rt.width||readback.height!=rt.height){if(readback!=null)UnityEngine.Object.DestroyImmediate(readback);readback=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);}
   var old=RenderTexture.active;RenderTexture.active=rt;readback.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);readback.Apply();RenderTexture.active=old;
   var pixels=readback.GetPixels();var values=new List<float>();
   for(int y=rt.height/5;y<rt.height*9/10;y++)for(int x=rt.width*2/5;x<rt.width*3/5;x++){var c=pixels[y*rt.width+x];values.Add(c.r*.2126f+c.g*.7152f+c.b*.0722f);}return values.ToArray();
  }
 }
}
