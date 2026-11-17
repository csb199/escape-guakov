using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Editor.Barrack
{
    [InitializeOnLoad]
    public static class BarrackAnimationContinuityValidation
    {
        const string Key="Barrack.AnimationContinuity";
        static string Root=>"LocalOnly/Barrack/animation-continuity/"+(SessionState.GetInt(Key+".Start",0)>0?"from-"+SessionState.GetInt(Key+".Start",0)+"/":"");
        static bool running;
        static string error;
        static RenderTexture target;
        static readonly List<string> checks=new List<string>();
        static BarrackAnimationContinuityValidation()
        {
            EditorApplication.update+=Tick;
            Application.logMessageReceived+=(message,stack,type)=>{if(SessionState.GetBool(Key,false)&&(type==LogType.Error||type==LogType.Exception))error=message;};
        }
        public static void Run()
        {
            int.TryParse(Environment.GetEnvironmentVariable("BARRACK_VALIDATE_START_INDEX"),out int start);
            if(start<0)throw new ArgumentOutOfRangeException(nameof(start));
            SessionState.SetInt(Key+".Start",start);
            Directory.CreateDirectory(Root);File.WriteAllText(Root+"accepted.txt","");File.WriteAllText(Root+"checks.txt","");
            EditorSceneManager.OpenScene(BarrackStartup.ScenePath);SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        static async void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
            var host=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();if(host==null)return;
            if(running)
            {
                if(host.Ready)
                {
                    if(target==null){target=new RenderTexture(host.displayCamera.pixelWidth,host.displayCamera.pixelHeight,24,RenderTextureFormat.ARGBHalf);target.Create();}
                    RenderPipeline.SubmitRenderRequest(host.displayCamera,new RenderPipeline.StandardRequest{destination=target});
                }
                return;
            }
            running=true;
            try
            {
                await Until(()=>host.Ready,180);
                int first=SessionState.GetInt(Key+".Start",0);Check(first<host.characters.Length,"validation start is in the roster");
                for(int index=first;index<host.characters.Length;index++)
                {
                    File.WriteAllText(Root+"progress.txt",index+"/"+host.characters.Length+" "+host.characters[index].id);
                    await host.SelectCharacterAsync(index);Check(host.Ready&&host.SelectedCharacter==index,"character ready "+host.CharacterId);
                    await Until(()=>host.Interaction.Mode==BarrackInteractionMode.Idle,45);await Task.Delay(300);
                    var transforms=host.Bone("Head_M").GetComponentInParent<Animator>().GetComponentsInChildren<Transform>();
                    await IdleMoves(host,transforms,4600,"after entrance across at least one complete idle loop");
                    var camera=host.displayCamera;var position=camera.transform.position;var rotation=camera.transform.rotation;float fov=camera.fieldOfView;
                    var action=host.Interaction.Supports(BarrackAction.Look)?BarrackAction.Look:host.Interaction.Supports(BarrackAction.Head)?BarrackAction.Head:BarrackAction.Idle;
                    if(action==BarrackAction.Idle)
                    {
                        Check(!host.Interaction.Request(BarrackAction.Look)&&!host.Interaction.Request(BarrackAction.Head),"excluded face actions remain unavailable "+host.CharacterId);
                        Check(host.Interaction.BeginOrbit(),"character without face actions can browse "+host.CharacterId);
                        host.CameraRig.Orbit(new Vector2(80,10));host.CameraRig.Zoom(1);await Task.Delay(450);
                        Check(host.Interaction.Return(),"character without face actions can return "+host.CharacterId);
                        await Until(()=>host.Interaction.Mode==BarrackInteractionMode.Idle,45);await Task.Delay(1200);
                        Check(Vector3.Distance(position,camera.transform.position)<.001f&&Quaternion.Angle(rotation,camera.transform.rotation)<.01f&&Mathf.Abs(fov-camera.fieldOfView)<.01f,"browsing return restores position rotation and FOV "+host.CharacterId);
                        await IdleMoves(host,transforms,650,"after browsing return");File.AppendAllText(Root+"accepted.txt",host.CharacterId+"\n");continue;
                    }
                    Check(host.Interaction.Request(action),"starts natural face sequence "+host.CharacterId+"/"+action);
                    double start=host.ValidationPoseTime;var before=transforms.Select(transform=>transform.localRotation).ToArray();
                    await Task.Delay(450);
                    Check(host.ValidationPoseTime>start+.1&&Difference(transforms,before)>.001f,"native face clip advances bones "+host.CharacterId);
                    if(action==BarrackAction.Look)Check(Vector3.Distance(position,camera.transform.position)>.1f,"face camera leaves default "+host.CharacterId);
                    await Until(()=>host.Interaction.Mode==BarrackInteractionMode.Idle,45);await Task.Delay(1200);
                    Check(!host.Interaction.IsFocused&&Vector3.Distance(position,camera.transform.position)<.001f&&Quaternion.Angle(rotation,camera.transform.rotation)<.01f&&Mathf.Abs(fov-camera.fieldOfView)<.01f,"automatic face completion restores position rotation and FOV "+host.CharacterId);
                    await IdleMoves(host,transforms,650,"after automatic face return");
                    if(host.Interaction.Supports(BarrackAction.Head))
                    {
                        Check(host.Interaction.Request(BarrackAction.Head),"starts Head "+host.CharacterId);
                        await Until(()=>host.Interaction.Mode==BarrackInteractionMode.Idle,45);await Task.Delay(300);
                        Check(Vector3.Distance(position,camera.transform.position)<.001f&&Mathf.Abs(fov-camera.fieldOfView)<.01f,"Head automatically returns to default "+host.CharacterId);
                        await IdleMoves(host,transforms,650,"after Head return");
                    }
                    File.AppendAllText(Root+"accepted.txt",host.CharacterId+"\n");
                }
                await host.StopAsync();Check(!AssetBundle.GetAllLoadedAssetBundles().Any(),"all source leases released");Finish(null);
            }
            catch(Exception exception){Finish(exception);}
        }
        static async Task IdleMoves(BarrackPreview host,Transform[] transforms,int milliseconds,string phase)
        {
            double start=host.ValidationIdleTime;var before=transforms.Select(transform=>transform.localRotation).ToArray();float motion=0;
            for(int elapsed=0;elapsed<milliseconds;elapsed+=230)
            {await Task.Delay(230);motion=Mathf.Max(motion,Difference(transforms,before));}
            Check(host.Interaction.Mode==BarrackInteractionMode.Idle&&host.CurrentAction.EndsWith("_BarrackIdle")&&host.ValidationIdleTime>start+milliseconds/2000.0&&motion>.001f,"idle actually animates "+phase+" "+host.CharacterId+" motion="+motion.ToString("F5"));
            Check(Mathf.Abs(host.ValidationBlendWeight-1)<.0001f,"idle blend normalized "+host.CharacterId);
        }
        static float Difference(Transform[] transforms,Quaternion[] before)=>transforms.Select((transform,index)=>Quaternion.Angle(before[index],transform.localRotation)).Max();
        static async Task Until(Func<bool> ready,int seconds)
        {var deadline=DateTime.UtcNow.AddSeconds(seconds);while(!ready()){if(error!=null)throw new Exception(error);if(DateTime.UtcNow>deadline)throw new TimeoutException();await Task.Delay(50);}}
        static void Check(bool valid,string message){if(!valid)throw new Exception(message);checks.Add(message);File.AppendAllText(Root+"checks.txt",message+"\n");}
        static void Finish(Exception exception)
        {
            SessionState.SetBool(Key,false);File.WriteAllText(Root+"result.txt",(exception==null?"PASS "+DateTime.Now.ToString("O"):"FAIL "+exception)+"\n"+string.Join("\n",checks));
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            EditorApplication.Exit(exception==null?0:1);
        }
    }
}
