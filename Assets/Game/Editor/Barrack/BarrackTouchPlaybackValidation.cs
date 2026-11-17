using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace Game.Editor.Barrack
{
    [InitializeOnLoad]
    public static class BarrackTouchPlaybackValidation
    {
        const string Key="Barrack.TouchPlayback",Root="LocalOnly/Barrack/touch-playback/";
        static bool running;
        static string error;
        static RenderTexture target;
        static readonly List<string> checks=new List<string>();
        static BarrackTouchPlaybackValidation()
        {
            EditorApplication.update+=Tick;
            Application.logMessageReceived+=(message,stack,type)=>{if(SessionState.GetBool(Key,false)&&(type==LogType.Exception||type==LogType.Error))error=message;};
        }
        public static void Run()
        {
            Directory.CreateDirectory(Root);EditorSceneManager.OpenScene(BarrackStartup.ScenePath);
            SessionState.SetBool(Key+".Reload",false);
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        static async void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
            var host=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();
            if(running)
            {
                if(host!=null&&host.Ready)
                {
                    if(target==null){target=new RenderTexture(host.displayCamera.pixelWidth,host.displayCamera.pixelHeight,24,RenderTextureFormat.ARGBHalf);target.Create();}
                    RenderPipeline.SubmitRenderRequest(host.displayCamera,new RenderPipeline.StandardRequest{destination=target});
                }
                return;
            }
            if(host==null)return;
            running=true;
            try
            {
                await Until(()=>host.Ready||host.Failure!=null,120);Check(host.Failure==null,"initial load");
                if(SessionState.GetBool(Key+".Reload",false))
                {
                    await Until(()=>host.Ready&&!host.Switching&&host.CharacterId=="DusevnyjSSR01",120);
                    Check(true,"script recompilation restores the selected character and a live session");
                }
                await Until(()=>host.Interaction.Mode==BarrackInteractionMode.Idle,30);
                await Task.Delay(2200);
                var character=host.characters[host.SelectedCharacter];
                var contract=JsonUtility.FromJson<BarrackTouchConfiguration>(character.interactionContract.text);
                var region=contract.capsules.Single(entry=>entry.path.EndsWith("/Head_M"));
                var expected=contract.Resolve(region.@event,character.ActionClips(),.5f);
                var head=host.Bone("Head_M");
                var transforms=head.GetComponentInParent<Animator>().GetComponentsInChildren<Transform>();var rotations=transforms.Select(transform=>transform.localRotation).ToArray();
                double idleStart=host.ValidationIdleTime;await Task.Delay(650);
                Check(host.ValidationIdleTime>idleStart+.1,"default idle advances after entrance and across its loop");
                Check(transforms.Select((transform,index)=>Quaternion.Angle(rotations[index],transform.localRotation)).Max()>.001,"default idle changes actual bones, not just a controller state name");
                var screen=host.displayCamera.WorldToScreenPoint(head.TransformPoint(region.center));
                checks.Add("screen="+Screen.width+"x"+Screen.height+" camera="+host.displayCamera.pixelRect+" canvas="+host.GetComponentInChildren<Canvas>().pixelRect);
                var overview=host.displayCamera.transform.position;float overviewFov=host.displayCamera.fieldOfView;
                Click(screen);
                Check(expected!=BarrackAction.Idle&&host.Interaction.Action==expected,"face UI raycast selects source action "+expected);
                double start=host.ValidationPoseTime;
                rotations=transforms.Select(transform=>transform.localRotation).ToArray();
                await Task.Delay(600);
                Check(host.ValidationPoseTime>start+.1,"face action advances natural playable time");
                Check(transforms.Select((transform,index)=>Quaternion.Angle(rotations[index],transform.localRotation)).Max()>.1,"face action changes bones in natural frames");
                Check(host.Interaction.Action==BarrackAction.Look&&Vector3.Distance(overview,host.displayCamera.transform.position)>.2f&&host.displayCamera.fieldOfView<overviewFov,"face click runs Look with camera movement and a narrower field of view");
                Capture(host,"look");
                await Until(()=>host.Interaction.Mode==BarrackInteractionMode.Idle,20);await Task.Delay(1200);
                Check(!host.Interaction.IsFocused&&Vector3.Distance(overview,host.displayCamera.transform.position)<.001f&&Mathf.Abs(overviewFov-host.displayCamera.fieldOfView)<.01f,"face animation automatically restores camera position and FOV without another click");
                Check(host.Interaction.Request(BarrackAction.Head),"Head can play after the face sequence returns");
                await Task.Delay(600);
                Capture(host,"head");
                var back=host.GetComponent<BarrackSourceUi>().ReturnButton;
                Click(RectTransformUtility.WorldToScreenPoint(null,((RectTransform)back.transform).TransformPoint(((RectTransform)back.transform).rect.center)));
                Check(host.Interaction.Mode==BarrackInteractionMode.Returning,"top-left Back starts return");
                start=host.ValidationPoseTime;await Task.Delay(250);
                Check(host.ValidationPoseTime>start+.05&&host.CurrentAction.EndsWith("_ToIdle"),"Back plays and advances source ToIdle");
                await Until(()=>host.Interaction.Mode==BarrackInteractionMode.Idle,20);
                Check(Mathf.Abs(host.ValidationBlendWeight-1)<.0001f,"animation blend weights remain normalized after returning: "+host.ValidationBlendWeight);
                Check(host.CurrentAction.EndsWith("_BarrackIdle"),"return reaches idle");
                idleStart=host.ValidationIdleTime;rotations=transforms.Select(transform=>transform.localRotation).ToArray();await Task.Delay(650);
                Check(host.ValidationIdleTime>idleStart+.1&&transforms.Select((transform,index)=>Quaternion.Angle(rotations[index],transform.localRotation)).Max()>.001,"default idle keeps animating after an interaction");
                host.Interaction.Request(BarrackAction.Look);host.FreezeAnimation(true);host.EvaluateValidationBlend(.05f);
                rotations=transforms.Select(transform=>transform.localRotation).ToArray();
                host.Interaction.Return();host.EvaluateValidationBlend(0);
                Check(Mathf.Abs(host.ValidationBlendWeight-1)<.0001f,"interrupting an unfinished blend retains total weight one");
                Check(transforms.Select((transform,index)=>Quaternion.Angle(rotations[index],transform.localRotation)).Max()<.1f,"immediate Back preserves the mixed pose at the interruption frame");
                host.FreezeAnimation(false);await Until(()=>host.Interaction.Mode==BarrackInteractionMode.Idle,20);
                host.Interaction.BeginOrbit();host.CameraRig.Orbit(new Vector2(80,10));host.CameraRig.Zoom(1);
                await Task.Delay(300);
                Click(RectTransformUtility.WorldToScreenPoint(null,((RectTransform)back.transform).TransformPoint(((RectTransform)back.transform).rect.center)));
                Check(host.Interaction.Mode==BarrackInteractionMode.Returning,"Back after drag and wheel starts source ToIdle");
                await Until(()=>host.Interaction.Mode==BarrackInteractionMode.Idle,20);
                if(!SessionState.GetBool(Key+".Reload",false))
                {
                    await host.SelectCharacterAsync(Array.FindIndex(host.characters,entry=>entry.id=="DusevnyjSSR01"));
                    Check(host.Ready&&host.CharacterId=="DusevnyjSSR01","select a nondefault character before recompilation");
                    File.WriteAllLines(Root+"before-reload.txt",checks);
                    SessionState.SetBool(Key+".Reload",true);
                    CompilationPipeline.RequestScriptCompilation();
                    return;
                }
                await host.StopAsync();Finish(null);
            }
            catch(Exception exception){Finish(exception);}
        }
        static void Click(Vector2 point)
        {
            Canvas.ForceUpdateCanvases();var pointer=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            Check(hits.Count>0,"UI raycast at "+point+": "+string.Join(",",hits.Select(hit=>hit.gameObject.name)));
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
        static void Capture(BarrackPreview host,string step)
        {
            RenderPipeline.SubmitRenderRequest(host.displayCamera,new RenderPipeline.StandardRequest{destination=target});
            var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Root+host.CharacterId+"-"+step+".png",image.EncodeToPNG());}
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
        static async Task Until(Func<bool> ready,int seconds)
        {
            var deadline=DateTime.UtcNow.AddSeconds(seconds);
            while(!ready()){if(error!=null)throw new Exception(error);if(DateTime.UtcNow>deadline)throw new TimeoutException();await Task.Delay(50);}
        }
        static void Check(bool valid,string message){if(!valid)throw new Exception(message);checks.Add(message);File.WriteAllLines(Root+"progress.txt",checks);}
        static void Finish(Exception exception)
        {
            SessionState.SetBool(Key,false);File.WriteAllText(Root+"result.txt",(exception==null?"PASS "+DateTime.Now.ToString("O"):"FAIL "+exception)+"\n"+string.Join("\n",checks));
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            EditorApplication.Exit(exception==null?0:1);
        }
    }
}
