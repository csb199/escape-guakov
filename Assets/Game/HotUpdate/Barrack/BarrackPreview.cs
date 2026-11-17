using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.HotUpdate.Barrack
{
    /// <summary>Direct-reference scene host. Published content uses BarrackProcedure's resource leases.</summary>
    public sealed class BarrackPreview : MonoBehaviour
    {
        public GameObject characterPrefab;
        public TextAsset shaderData, animationData, interactionData;
        public BarrackCharacterDefinition[] characters=Array.Empty<BarrackCharacterDefinition>();
        public Camera displayCamera;
        public BarrackStageDefinition stageDefinition;
        public bool Ready { get; private set; }
        public bool Switching { get; private set; }
        public int SelectedCharacter { get; private set; }=-1;
        public string CharacterId=>session?.Definition.id;
        public string CharacterName=>session?.Definition.displayName;
        public string Failure { get; private set; }
        public int RendererCount=>session?.Renderers.Length??0;
        public string CurrentAction=>session?.CurrentAction;
        public BarrackInteraction Interaction { get; private set; }
        public BarrackCameraRig CameraRig { get; private set; }
        public BarrackView View { get; private set; }
        public event Action CharacterChanged;
        public static readonly string[] Actions={"c_GrozaSR01_BarrackIdle","c_GrozaSR01_Barracksp_Behave_Stretch","c_GrozaSR01_Barracksp_Touch_Head","c_GrozaSR01_Barracksp_Touch_Gun","c_GrozaSR01_Barracksp_Touch_Pocket"};
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        Task initialization,stopping,switchingTask;
        CancellationTokenRegistration externalCancellation;
        BarrackStageLibrary stage; BarrackCharacterSession session;
        bool frozen,ownsPipeline;
        RenderPipelineAsset previousGraphicsPipeline,previousQualityPipeline;
        string previousPipeline;
        void Start(){_=EnsureInitializedAsync();}
        public Task EnsureInitializedAsync(CancellationToken cancellation=default)
        {
            cancellation.ThrowIfCancellationRequested();
            if(cancellation.CanBeCanceled){externalCancellation.Dispose();externalCancellation=cancellation.Register(BeginStop);}
            if(initialization!=null)return initialization;
            if(characters==null||characters.Length==0)characters=new[]{new BarrackCharacterDefinition {id="GrozaSR01",displayName="闪电",prefab=characterPrefab,shaders=shaderData,animations=animationData,materialContract=stageDefinition?.materialContract,interactionContract=interactionData}};
            View=gameObject.AddComponent<BarrackView>();View.Initialize(this);
            initialization=InitializeAsync();return initialization;
        }
        public void BeginStop(){if(!lifetime.IsCancellationRequested)lifetime.Cancel();Ready=false;Interaction?.Close();}
        public Task StopAsync()=>stopping??(stopping=StopCoreAsync());
        async Task StopCoreAsync(){BeginStop();if(initialization!=null)await initialization;if(switchingTask!=null)await switchingTask;await ReleaseAsync();externalCancellation.Dispose();}
        async Task InitializeAsync()
        {
            try
            {
                if(displayCamera==null||stageDefinition==null||stageDefinition.pipeline==null)throw new InvalidOperationException("Barrack source scene resource references are incomplete");
                previousPipeline=Shader.globalRenderPipeline;Shader.globalRenderPipeline="UniversalPipeline";
                stage=new BarrackStageLibrary();await stage.LoadAsync(stageDefinition,lifetime.Token);lifetime.Token.ThrowIfCancellationRequested();
                previousGraphicsPipeline=GraphicsSettings.defaultRenderPipeline;previousQualityPipeline=QualitySettings.renderPipeline;
                GraphicsSettings.defaultRenderPipeline=stageDefinition.pipeline;QualitySettings.renderPipeline=stageDefinition.pipeline;ownsPipeline=true;
                var environment=JsonUtility.FromJson<BarrackEnvironment>(stageDefinition.environment.text);
                displayCamera.cullingMask=environment.cameraMask;displayCamera.nearClipPlane=.1f;displayCamera.farClipPlane=5000;displayCamera.allowHDR=true;
                await SwitchCoreAsync(0);
            }
            catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
            catch(Exception e){Failure=e.Message;Debug.LogException(e,this);await ReleaseAsync();View?.Refresh();}
        }
        public Task SelectCharacterAsync(int index)
        {
            if(!Ready||Switching||lifetime.IsCancellationRequested||index==SelectedCharacter)return Task.CompletedTask;
            if(index<0||index>=characters.Length)throw new ArgumentOutOfRangeException(nameof(index));
            return switchingTask=SwitchCoreAsync(index);
        }
        async Task SwitchCoreAsync(int index)
        {
            Switching=true;Ready=false;Failure=null;CharacterChanged?.Invoke();View?.Refresh();
            BarrackCharacterSession candidate=null;
            try
            {
                candidate=await BarrackCharacterSession.LoadAsync(characters[index],transform,stageDefinition.characterLightProbes,stageDefinition.characterLayer,lifetime.Token);lifetime.Token.ThrowIfCancellationRequested();
                var interaction=new BarrackInteraction(candidate.Durations);
                var rig=new BarrackCameraRig(displayCamera,transform,candidate.TouchTargets.CameraPoint,candidate.Bone,JsonUtility.FromJson<BarrackCameraConfiguration>(stageDefinition.cameraConfiguration.text));
                Interaction?.Close();if(Interaction!=null)Interaction.Changed-=OnInteractionChanged;
                CameraRig?.Dispose();session?.Dispose();session=candidate;candidate=null;session.Instance.SetActive(true);
                Interaction=interaction;CameraRig=rig;Interaction.Changed+=OnInteractionChanged;SelectedCharacter=index;
                (RenderPipelineManager.currentPipeline as BarrackRenderPipeline)?.ResetCharacterHistory(displayCamera);
                frozen=false;Ready=true;Interaction.Start();
            }
            catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
            catch(Exception e){Failure=e.Message;Debug.LogException(e,this);Ready=session!=null&&!lifetime.IsCancellationRequested;}
            finally{candidate?.Dispose();Switching=false;CharacterChanged?.Invoke();View?.Refresh();}
        }
        public Transform Bone(string name)=>session?.Bone(name);
        void OnInteractionChanged(BarrackAction action,BarrackInteractionMode mode){if(mode==BarrackInteractionMode.Closed)return;if(mode!=BarrackInteractionMode.Orbit||session.CurrentAction!=session.Clips[BarrackAction.Idle])PlayAction(session.Clips[action]);CameraRig.Change(action,mode,Interaction.IsFocused);View.Refresh();}
        public void PlayAction(string name)=>session.Play(name);
#if UNITY_EDITOR
        public double ValidationPoseTime=>session.PoseTime;
        public float ValidationBlendWeight=>session.BlendWeight;
        public double ValidationIdleTime=>session.IdleTime;
        public void EvaluateValidationBlend(float seconds)=>session.EvaluateBlend(seconds);
        public void SampleValidationPose(double seconds){FreezeAnimation(true);session.Sample(seconds);}
#endif
        public void FreezeAnimation(bool freeze){frozen=freeze;foreach(var p in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)){if(freeze)p.Pause();else p.Play();}session?.Freeze(freeze);}
        void Update(){if(!Ready||frozen)return;Interaction.Tick(Time.deltaTime);session.Tick(Time.deltaTime);}
        void LateUpdate(){if(Ready&&!frozen)CameraRig.Tick(Time.deltaTime,Interaction.Progress);}
        public bool TryTouch(Vector2 screenPosition)
        {
            if(!Ready||!Interaction.CanInteract)return false;
            var ray=displayCamera.ScreenPointToRay(screenPosition);
            var selected=session.TouchTargets.Hit(ray,session.Clips,Interaction.IsFocused);
            return selected!=BarrackAction.Idle&&Interaction.Request(selected);
        }
        async void OnDestroy(){try{await StopAsync();}catch(Exception e){Debug.LogException(e);}lifetime.Dispose();}
        async Task ReleaseAsync()
        {
            Ready=false;Interaction?.Close();if(Interaction!=null)Interaction.Changed-=OnInteractionChanged;
            CameraRig?.Dispose();CameraRig=null;session?.Dispose();session=null;
            if(stage!=null){await stage.DisposeAsync();stage=null;}
            if(ownsPipeline){GraphicsSettings.defaultRenderPipeline=previousGraphicsPipeline;QualitySettings.renderPipeline=previousQualityPipeline;ownsPipeline=false;}
            if(previousPipeline!=null){Shader.globalRenderPipeline=previousPipeline;previousPipeline=null;}
        }
    }
}
