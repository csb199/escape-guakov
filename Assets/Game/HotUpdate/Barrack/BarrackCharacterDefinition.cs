using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.HotUpdate.Barrack
{
    [Serializable]
    public sealed class BarrackCharacterDefinition
    {
        public string id, displayName;
        public Texture2D portrait;
        public GameObject prefab;
        public TextAsset shaders, animations, materialContract, interactionContract;
        public TextAsset[] additionalShaders;
        public string[] clips;
        public Dictionary<BarrackAction,string> ActionClips() => BarrackInteraction.Clips
            .ToDictionary(p=>p.Key,p=>p.Value.Replace("GrozaSR01",id))
            .Where(p=>clips==null || clips.Contains(p.Value)).ToDictionary(p=>p.Key,p=>p.Value);
    }

    // One switch candidate owns its instance, graph and native leases until commit or cancellation.
    sealed class BarrackCharacterSession : IDisposable
    {
        public GameObject Instance { get; private set; }
        public BarrackCharacterDefinition Definition { get; private set; }
        public Dictionary<BarrackAction,string> Clips { get; private set; }
        public Dictionary<BarrackAction,float> Durations { get; private set; }
        public Bounds Bounds { get; private set; }
        public SkinnedMeshRenderer[] Renderers { get; private set; }
        public string CurrentAction { get; private set; }
        public BarrackTouchTargets TouchTargets { get; private set; }
        readonly Dictionary<string,Transform> bones=new Dictionary<string,Transform>();
        readonly List<AnimationClipPlayable> slots=new List<AnimationClipPlayable>{default};
        readonly List<float> blendWeights=new List<float>{1};
        BarrackShaderLibrary shaders; BarrackAnimationLibrary animations; IDisposable binding;
        PlayableGraph graph; AnimationMixerPlayable mixer; int activeSlot; float blendTime;
        public Transform Bone(string name)=>bones.TryGetValue(name,out var value)?value:null;
        public static async Task<BarrackCharacterSession> LoadAsync(BarrackCharacterDefinition definition,Transform parent,TextAsset lightProbes,int characterLayer,CancellationToken cancellation)
        {
            var result=new BarrackCharacterSession {Definition=definition};
            try
            {
                if(characterLayer<0||characterLayer>31)throw new ArgumentOutOfRangeException(nameof(characterLayer));
                if(definition.prefab==null||definition.shaders==null||definition.animations==null)throw new InvalidOperationException("Incomplete character: "+definition.id);
                result.shaders=await BarrackShaderLibrary.LoadAsync(new[]{definition.shaders}.Concat(definition.additionalShaders??Array.Empty<TextAsset>()).Select(a=>a.bytes),cancellation);
                result.animations=await BarrackAnimationLibrary.LoadAsync(definition.animations.bytes,cancellation);
                cancellation.ThrowIfCancellationRequested();
                result.Clips=definition.ActionClips();
                result.Durations=result.Clips.ToDictionary(p=>p.Key,p=>result.animations.Get(p.Value).length);
                // Validate required actions before replacing the current character.
                _=new BarrackInteraction(result.Durations);
                result.Instance=UnityEngine.Object.Instantiate(definition.prefab,parent);result.Instance.SetActive(false);
                result.Instance.transform.localPosition=Vector3.zero;result.Instance.transform.localRotation=Quaternion.Euler(0,180,0);
                result.binding=result.shaders.Bind(result.Instance,definition.materialContract);
                // Source prefab authoring layers differ; the barrack spawn contract owns the presentation layer.
                foreach(var t in result.Instance.GetComponentsInChildren<Transform>(true)){t.gameObject.layer=characterLayer;if(!result.bones.ContainsKey(t.name))result.bones.Add(t.name,t);}
                result.TouchTargets=new BarrackTouchTargets(result.Instance,definition.interactionContract,result.Bone);
                var animator=result.Instance.GetComponentInChildren<Animator>(true);
                if(animator==null)throw new InvalidOperationException("Animator missing: "+definition.id);
                animator.enabled=true;animator.fireEvents=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();
                result.graph=PlayableGraph.Create("Barrack."+definition.id);result.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var output=AnimationPlayableOutput.Create(result.graph,"Character",animator);
                result.mixer=AnimationMixerPlayable.Create(result.graph,1);output.SetSourcePlayable(result.mixer);
                var idle=result.animations.Get(result.Clips[BarrackAction.Idle]);
                if(!idle.isLooping)throw new InvalidOperationException("Source BarrackIdle must loop: "+definition.id);
                result.slots[0]=AnimationClipPlayable.Create(result.graph,idle);
                result.graph.Connect(result.slots[0],0,result.mixer,0);
                result.mixer.SetInputWeight(0,1);
                result.Play(result.Clips[BarrackAction.Idle]);result.graph.Play();
                result.Instance.SetActive(true);result.graph.Evaluate(0);
                result.Renderers=result.Instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if(result.Renderers.Length==0||result.Renderers.Any(r=>r.sharedMesh==null||r.bones.Any(b=>b==null||!b.IsChildOf(result.Instance.transform))))throw new InvalidOperationException("Mesh/bone ownership incomplete: "+definition.id);
                var bounds=result.Renderers[0].bounds;
                foreach(var r in result.Renderers){if(r.enabled&&r.gameObject.activeInHierarchy)bounds.Encapsulate(r.bounds);r.updateWhenOffscreen=true;}
                result.Bounds=bounds;result.Instance.AddComponent<BarrackCharacterLighting>().Initialize(lightProbes);result.Instance.SetActive(false);
                return result;
            }
            catch {result.Dispose();throw;}
        }
        public void Play(string name)
        {
            var clip=animations.Get(name);
            for(int index=0;index<slots.Count;index++)blendWeights[index]=mixer.GetInputWeight(index);
            int next=0;
            if(name==Clips[BarrackAction.Idle])
            {
                if(CurrentAction==Clips[BarrackAction.Entrance])slots[0].SetTime(0);
            }
            else
            {
                next=slots.FindIndex(1,slot=>!slot.IsValid());
                if(next<0){next=slots.Count;slots.Add(default);blendWeights.Add(0);mixer.SetInputCount(slots.Count);}
                slots[next]=AnimationClipPlayable.Create(graph,clip);slots[next].SetTime(0);
                graph.Connect(slots[next],0,mixer,next);mixer.SetInputWeight(next,0);
            }
            for(int index=0;index<slots.Count;index++)mixer.SetInputWeight(index,blendWeights[index]);
            bool immediate=name==Clips[BarrackAction.Entrance]||CurrentAction==Clips[BarrackAction.Entrance];
            blendTime=immediate||mixer.GetInputWeight(next)>=1?1:0;
            if(immediate)for(int index=0;index<slots.Count;index++)mixer.SetInputWeight(index,index==next?1:0);
            activeSlot=next;CurrentAction=name;
        }
        public void Tick(float dt)
        {
            if(blendTime<1)
            {
                blendTime=Mathf.Min(1,blendTime+dt/.18f);float amount=Mathf.SmoothStep(0,1,blendTime);
                for(int index=0;index<slots.Count;index++)mixer.SetInputWeight(index,Mathf.Lerp(blendWeights[index],index==activeSlot?1:0,amount));
            }
            for(int index=1;index<slots.Count;index++)
                if(index!=activeSlot&&slots[index].IsValid()&&mixer.GetInputWeight(index)==0)
                {graph.Disconnect(mixer,index);graph.DestroyPlayable(slots[index]);slots[index]=default;mixer.SetInputWeight(index,0);}
        }
        public void Freeze(bool freeze){if(!graph.IsValid())return;if(freeze)graph.Stop();else graph.Play();}
#if UNITY_EDITOR
        public double PoseTime=>slots[activeSlot].GetTime();
        public double IdleTime=>slots[0].GetTime();
        public float BlendWeight=>Enumerable.Range(0,slots.Count).Sum(index=>mixer.GetInputWeight(index));
        public void EvaluateBlend(float seconds){Tick(seconds);graph.Evaluate(seconds);}
        public void Sample(double seconds)
        {
            Freeze(true);
            for(int index=0;index<slots.Count;index++)mixer.SetInputWeight(index,index==activeSlot?1:0);
            blendTime=1;
            slots[activeSlot].SetTime(seconds);
            graph.Evaluate(0);
        }
#endif
        public void Dispose()
        {
            if(graph.IsValid())graph.Destroy();binding?.Dispose();binding=null;
            if(Instance!=null){Instance.SetActive(false);UnityEngine.Object.Destroy(Instance);Instance=null;}
            bones.Clear();animations?.Dispose();animations=null;shaders?.Dispose();shaders=null;
        }
    }
}
