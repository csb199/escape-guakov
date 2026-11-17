using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace Game.Editor.Barrack
{
    [InitializeOnLoad]
    public static class BarrackRosterValidation
    {
        const string Key="Barrack.RosterValidation",Root="LocalOnly/Barrack/character-import/validation/";
        [Serializable] sealed class ImportReport { public int renderers; }
        static RenderTexture target;static bool running;static string error;static readonly List<string> checks=new List<string>();
        static BarrackRosterValidation(){EditorApplication.update+=Tick;Application.logMessageReceived+=(m,s,t)=>{if(SessionState.GetBool(Key,false)&&(t==LogType.Error||t==LogType.Exception||t==LogType.Assert))error=m;};}
        public static void RunAfterImport(){BarrackRosterImporter.NormalizeNestedSourceRoots();Run();}
        public static void Run(){BarrackRosterImporter.Prepare();BarrackTextureContract.Validate();Directory.CreateDirectory(Root);File.WriteAllText(Root+"accepted-characters.txt","");SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;}
        static async void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
            if(running)
            {
                var active=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();
                if(active!=null&&active.Ready)
                {
                    if(target==null){target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGBHalf);target.Create();}
                    var request=new RenderPipeline.StandardRequest {destination=target};
                    if(RenderPipeline.SupportsRenderRequest(active.displayCamera,request))RenderPipeline.SubmitRenderRequest(active.displayCamera,request);
                }
                return;
            }
            var host=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();if(host==null)return;running=true;
            try
            {
                await Until(()=>host.Ready||host.Failure!=null,180);Check(host.Ready,"initial character ready");
                Check(host.characters.Select(c=>c.id).SequenceEqual(BarrackRosterImporter.Entries().Select(e=>e.id)),"explicit imported roster entries");int scene=SceneManager.GetActiveScene().handle;
                var ids=Enumerable.Range(1,host.characters.Length-1).Concat(new[]{0,host.characters.Length-1,1});
                foreach(int index in ids)
                {
                    File.WriteAllText(Root+"current-character.txt",host.characters[index].id);
                    host.FreezeAnimation(false);host.Interaction.Tick(100);host.Interaction.Tick(100);
                    var surface=host.GetComponentInChildren<BarrackInputSurface>();
                    var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,delta=new Vector2(100,20)};
                    surface.OnPointerDown(pointer);
                    var button=host.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Roster "+host.characters[index].id);
                    var scroll=host.GetComponentInChildren<ScrollRect>();
                    Check(scroll!=null&&scroll.vertical&&!scroll.horizontal,"scrollable roster");
                    Canvas.ForceUpdateCanvases();
                    float overflow=scroll.content.rect.height-scroll.viewport.rect.height;
                    scroll.StopMovement();scroll.verticalNormalizedPosition=overflow>0?1-Mathf.Clamp01((index/3)*140*1.2f/overflow):1;
                    Canvas.ForceUpdateCanvases();await Task.Delay(50);
                    var buttonRect=(RectTransform)button.transform;
                    var corners=new Vector3[4];buttonRect.GetWorldCorners(corners);
                    var bounds=new Bounds(scroll.viewport.InverseTransformPoint(corners[0]),Vector3.zero);foreach(var corner in corners)bounds.Encapsulate(scroll.viewport.InverseTransformPoint(corner));
                    Check(bounds.max.y<=scroll.viewport.rect.yMax+1&&bounds.min.y>=scroll.viewport.rect.yMin-1,"roster entry reachable in viewport");
                    await Until(()=>button.interactable,10);Check(button.interactable,"roster button enabled: "+index);button.onClick.Invoke();
                    Check(host.Switching||host.SelectedCharacter==index,"UI dispatch starts switch");
                    await Until(()=>host.Ready&&host.SelectedCharacter==index||host.Failure!=null,90);
                    Check(host.Failure==null&&host.SelectedCharacter==index,"UI selected "+host.CharacterId);
                    host.Interaction.Tick(100);surface.OnBeginDrag(pointer);surface.OnDrag(pointer);surface.OnPointerClick(pointer);
                    Check(host.Interaction.Mode==BarrackInteractionMode.Idle,"character switch discards the previous pointer gesture");
                    Check(host.CameraRig.Root.parent==host.transform&&host.GetComponentsInChildren<Transform>().Count(t=>t.name=="VirtualCameraRoot")==1,"character switch retains exactly one owned browsing rig");
                    Check(SceneManager.GetActiveScene().handle==scene,"background retained across switch");
                    if(index>=3&&index<=5)Check(!host.Interaction.Supports(BarrackAction.Pocket),"unbound prop action hidden");
                    if(index==4)Check(!host.Interaction.Supports(BarrackAction.Head),"unbound flute action hidden");
                    var character=host.transform.GetComponentsInChildren<Animator>(true).Single(a=>a.gameObject.activeInHierarchy);
                    var renderers=character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    var expected=JsonUtility.FromJson<ImportReport>(File.ReadAllText("Assets/Game/Content/BarrackSource/"+host.CharacterId+"/import-report.json"));
                    Check(character.GetComponentsInChildren<Renderer>(true).Length==expected.renderers,"renderer count "+host.CharacterId+": "+renderers.Length);
                    var forwardMask=JsonUtility.FromJson<BarrackEnvironment>(host.stageDefinition.environment.text).features.Single(f=>f.name=="CharForward").layerMask;
                    Check(renderers.All(r=>(forwardMask&(1L<<r.gameObject.layer))!=0),"all character renderers enter source forward pass");
                    Check(renderers.All(r=>r.sharedMesh!=null&&r.bones.All(b=>b!=null&&b.IsChildOf(character.transform))),"owned mesh/bone references");
                    Check(renderers.SelectMany(r=>r.sharedMaterials).All(m=>m!=null&&m.shader.isSupported&&(m.FindPass("GFCharForward")>=0||(m.shader.name=="gf_shader/scene/pbr_env_standard"&&m.FindPass("ForwardLit")>=0)||(m.shader.name=="gf_shader/vfx/UV_Effect_UVAll_Fast_2"&&m.passCount>0)||(m.shader.name=="gf_shader/pbr/character/ubertrans"&&m.FindPass("GFCharTransBack")>=0&&m.FindPass("GFCharTransFront")>=0))),"native shader support and pass bindings");
                    Check(host.GetComponentsInChildren<Text>().All(t=>t.name!="Combat power"&&t.name!="Level"&&t.name!="Power"&&t.name!="Attributes"),"unimplemented account UI hidden");
                    foreach(var action in host.characters[index].ActionClips().Keys)
                    {
                        if(action==BarrackAction.Idle||action==BarrackAction.Entrance||action==BarrackAction.Return)continue;
                        host.Interaction.Tick(100);host.Interaction.Tick(100);
                        Check(host.Interaction.Request(action),"action accepted "+host.CharacterId+"/"+action);
                        host.SampleValidationPose(.1);var transforms=character.GetComponentsInChildren<Transform>();var before=transforms.Select(t=>t.localRotation).ToArray();
                        host.SampleValidationPose(.8);float change=transforms.Select((t,i)=>Quaternion.Angle(before[i],t.localRotation)).Max();
                        Check(change>.001f,"native clip moves bones "+action+" maxDegrees="+change.ToString("F3"));host.FreezeAnimation(false);
                    }
                    host.Interaction.Return();host.Interaction.Tick(100);host.CameraRig.Tick(100,0);host.PlayAction(host.characters[index].ActionClips()[BarrackAction.Idle]);host.SampleValidationPose(1);host.View.SetVisible(true);
                    await Task.Delay(2500);Check(error==null,"no runtime exceptions");
                    var pipeline=(BarrackRenderPipeline)RenderPipelineManager.currentPipeline;
                    if(index>=3)
                    {
                        foreach(var renderer in renderers)
                        {
                            var baked=new Mesh();renderer.BakeMesh(baked);baked.RecalculateBounds();
                            Check(baked.vertexCount>0 && !float.IsNaN(baked.bounds.size.sqrMagnitude) && baked.bounds.size.magnitude<10,"finite native deformation "+renderer.name);
                            UnityEngine.Object.DestroyImmediate(baked);
                        }
                    }
                    if(host.CharacterId=="PhaetusaSSR01")
                    {
                        var hair=renderers.Single(r=>r.name.Contains("hair_Fight_lod0"));var baked=new Mesh();hair.BakeMesh(baked);baked.RecalculateBounds();
                        Check(hair.enabled&&hair.gameObject.activeInHierarchy&&baked.bounds.size.x>.2f&&baked.bounds.size.y>.15f,"authored hair remains visible with noncollapsed geometry");
                        File.WriteAllText(Root+"phaetusa-hair.txt", "Native BarrackIdle phase 1s: "+hair.name+" baked="+baked.bounds+"\nDorm root scale="+renderers.Single(r=>r.name.Contains("hair_Dorm_lod0")).bones[0].lossyScale);
                        UnityEngine.Object.DestroyImmediate(baked);
                    }
                    Capture(pipeline.LatestFrame(host.displayCamera),host.CharacterId+".png");
                    Capture(target,host.CharacterId+"-ui.png");File.AppendAllText(Root+"accepted-characters.txt",host.CharacterId+"\n");await Task.Delay(400);host.FreezeAnimation(false);
                }
                // Closing while a native animation bundle is in flight must wait before releasing the entity.
                var switching=host.SelectCharacterAsync(9);var stopping=host.StopAsync();await Task.WhenAll(switching,stopping);await Task.Delay(150);
                Check(!host.Ready&&!host.Switching,"stop awaits in-flight switch");
                Check(SceneManager.sceneCount==1,"native scene released");Check(!AssetBundle.GetAllLoadedAssetBundles().Any(),"all native leases released");
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);target=null;}
                SessionState.SetBool(Key,false);File.WriteAllText(Root+"result.txt","PASS "+DateTime.Now.ToString("O")+"\n"+string.Join("\n",checks));EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(0);
            }
            catch(Exception e){SessionState.SetBool(Key,false);File.WriteAllText(Root+"result.txt","FAIL "+e+"\n"+string.Join("\n",checks));Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static async Task Until(Func<bool> condition,int seconds){var end=DateTime.UtcNow.AddSeconds(seconds);while(!condition()){if(error!=null)throw new Exception(error);if(DateTime.UtcNow>end)throw new TimeoutException("Roster validation deadline");await Task.Delay(100);}}
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
        static void Capture(RenderTexture source,string name)
        {
            if(source==null)throw new Exception("No completed frame");var previous=RenderTexture.active;var target=RenderTexture.GetTemporary(1280,720,0,RenderTextureFormat.ARGB32);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try{Graphics.Blit(source,target);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();var px=image.GetPixels32();Check(px.Average(c=>(c.r+c.g+c.b)/3.0)>12,"nonblack completed frame");File.WriteAllBytes(Root+name,image.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
