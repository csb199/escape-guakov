using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Editor.Barrack
{
    // Opt-in live Game-view probe; never redirects the user's camera or exits the editor.
    [InitializeOnLoad]
    public static class BarrackFlickerProbe
    {
        const string Key="Barrack.FlickerProbe", Root="LocalOnly/Barrack/flicker/";
        static BarrackPreview host;
        static BarrackRenderPipeline pipeline;
        static readonly List<string> rows=new List<string>();
        static int count;
        static bool temporal,ao,diagnostics;
        static float frozenTime;
        static Vector3 previousPosition;
        static Quaternion previousRotation;
        static Color32[] previousPixels;
        static RenderTexture small;
        static Texture2D readback;
        static BarrackFlickerProbe()
        {
            EditorApplication.update+=Tick;
            AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(pipeline!=null)Finish("RELOAD_CANCELLED");};
            RenderPipelineManager.endCameraRendering+=EndCamera;
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingPlayMode)Finish("STOPPED");};
        }
        [MenuItem("Game Framework/Barrack/Probe Live Flicker")]
        public static void Run()
        {
            if(SessionState.GetBool(Key,false))return;
            Directory.CreateDirectory(Root);SessionState.SetBool(Key,true);
            File.WriteAllText(Root+"status.txt","RUNNING\n"+DateTime.Now.ToString("O"));
            SessionState.SetString(Key+".deadline",DateTime.UtcNow.AddMinutes(5).ToString("O"));
            if(!EditorApplication.isPlaying)EditorApplication.isPlaying=true;
        }
        static void Tick()
        {
            if(File.Exists(Root+"request.txt") && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                string request=File.ReadAllText(Root+"request.txt").Trim();File.Delete(Root+"request.txt");
                if(request=="capture")
                {
                    var type=typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.RenderDoc");var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
                    File.WriteAllText(Root+"capture-api.txt","loaded="+type.GetMethod("IsLoaded",flags).Invoke(null,null));
                    var game=Resources.FindObjectsOfTypeAll<EditorWindow>().First(w=>w.GetType().Name=="GameView");
                    type.GetMethods(flags).First(m=>m.Name=="CaptureRenderDoc"&&m.GetParameters().Length==2).Invoke(null,new object[]{game,null});return;
                }
                Run();
            }
            if(!SessionState.GetBool(Key,false))return;
            if(DateTime.UtcNow>DateTime.Parse(SessionState.GetString(Key+".deadline",""))){Finish("TIMEOUT");return;}
            if(!EditorApplication.isPlaying)return;
            if(host==null)host=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();
            if(host==null||!host.Ready)return;
            if(pipeline!=null)return;
            pipeline=RenderPipelineManager.currentPipeline as BarrackRenderPipeline;
            if(pipeline==null)return;
            temporal=pipeline.TemporalEnabled;ao=pipeline.AmbientOcclusionEnabled;diagnostics=pipeline.CaptureDiagnostics;frozenTime=pipeline.FrozenTime;
            pipeline.CaptureDiagnostics=true;
            Selection.activeGameObject=host.gameObject;
            count=0;previousPixels=null;rows.Clear();rows.Add("sample,frame,phase,camera,positionDelta,rotationDelta,fov,aspect,projection00,projection02,projection12,history,jitterX,jitterY,meanR,meanG,meanB,deltaRgb,deltaTime");
            previousPosition=host.displayCamera.transform.position;previousRotation=host.displayCamera.transform.rotation;
            small=new RenderTexture(160,90,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);small.Create();
            readback=new Texture2D(160,90,TextureFormat.RGBA32,false,true);
        }
        static void EndCamera(ScriptableRenderContext context,Camera camera)
        {
            if(!SessionState.GetBool(Key,false)||pipeline==null||host==null||camera!=host.displayCamera)return;
            try
            {
                var frame=pipeline.LatestFrame(camera);if(frame==null)return;
                var active=RenderTexture.active;
                try{Graphics.Blit(frame,small);RenderTexture.active=small;readback.ReadPixels(new Rect(0,0,160,90),0,0);readback.Apply();}
                finally{RenderTexture.active=active;}
                var pixels=readback.GetPixels32();double r=0,g=0,b=0,delta=0;
                for(int i=0;i<pixels.Length;i++){var p=pixels[i];r+=p.r;g+=p.g;b+=p.b;if(previousPixels!=null){var q=previousPixels[i];delta+=Math.Abs(p.r-q.r)+Math.Abs(p.g-q.g)+Math.Abs(p.b-q.b);}}
                string phase=count<32?"live":count<96?"frozen-taa":count<160?"frozen-no-taa":"frozen-no-ao";
                var p0=camera.projectionMatrix;var jitter=pipeline.TemporalJitter;
                rows.Add(string.Join(",",new[]{count.ToString(),Time.frameCount.ToString(),phase,camera.GetInstanceID().ToString(),F(Vector3.Distance(previousPosition,camera.transform.position)),F(Quaternion.Angle(previousRotation,camera.transform.rotation)),F(camera.fieldOfView),F(camera.aspect),F(p0.m00),F(p0.m02),F(p0.m12),pipeline.TemporalHistoryUsed.ToString(),F(jitter.x),F(jitter.y),F(r/pixels.Length/255),F(g/pixels.Length/255),F(b/pixels.Length/255),F(delta/pixels.Length/765),F(Time.deltaTime)}));
                if(count%16==0)File.WriteAllBytes(Root+"frame-"+count.ToString("D3")+".png",readback.EncodeToPNG());
                previousPixels=pixels;previousPosition=camera.transform.position;previousRotation=camera.transform.rotation;
                count++;
                if(count==32){host.FreezeAnimation(true);pipeline.FrozenTime=2;}
                if(count==96)pipeline.TemporalEnabled=false;
                if(count==160)pipeline.AmbientOcclusionEnabled=false;
                if(count==224)Finish("COLLECTED");
            }
            catch(Exception e){Finish("ERROR: "+e);Debug.LogException(e);}
        }
        static string F(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
        static void Finish(string status)
        {
            if(!SessionState.GetBool(Key,false))return;
            SessionState.SetBool(Key,false);
            if(pipeline!=null){pipeline.TemporalEnabled=temporal;pipeline.AmbientOcclusionEnabled=ao;pipeline.CaptureDiagnostics=diagnostics;pipeline.FrozenTime=frozenTime;}
            if(host!=null)host.FreezeAnimation(false);
            File.WriteAllLines(Root+"frames.csv",rows);File.WriteAllText(Root+"status.txt",status+"\n"+DateTime.Now.ToString("O"));
            if(small!=null){small.Release();UnityEngine.Object.DestroyImmediate(small);}if(readback!=null)UnityEngine.Object.DestroyImmediate(readback);
            pipeline=null;host=null;
        }
    }
}
