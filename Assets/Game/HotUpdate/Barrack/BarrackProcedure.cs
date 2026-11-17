using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Core;
using Game.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.HotUpdate.Barrack
{
    [Serializable]
    public sealed class BarrackContent
    {
        public string sceneAddress, modelAddress, idleAddress, shaderLibraryAddress;
        public string presentationPrefabAddress;
        public string animationLibraryAddress, idleClipName;
        public string[] actionClipNames = Array.Empty<string>();
        public string[] actionAddresses = Array.Empty<string>();
    }

    // Business adapter only. Addresses must come from the host's verified release catalog.
    // Registration into a concrete entry is deferred until source/rendering readiness is verified.
    public sealed class BarrackProcedure : ProcedureBase
    {
        readonly GameRuntime game;
        readonly BarrackContent content;
        EntityOwner<GameObject> entities;
        EntityHandle<GameObject> character;
        readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        StateScope lifetime;
        BarrackPreview presentation;
        public BarrackProcedure(GameRuntime game, BarrackContent content)
        { this.game = game ?? throw new ArgumentNullException(nameof(game)); this.content = content ?? throw new ArgumentNullException(nameof(content)); }
        public override async Task EnterAsync(ProcedureManager owner, StateScope scope)
        {
            if (!string.IsNullOrWhiteSpace(content.presentationPrefabAddress))
            {
                lifetime = scope;
                if (!string.IsNullOrWhiteSpace(content.sceneAddress)) await game.Scenes.LoadAsync(content.sceneAddress, scope);
                entities = game.Entities.CreateOwner(scope, "barrack/presentation");
                character = await entities.ShowAsync(game.Entities.NextId(), content.presentationPrefabAddress, "Barrack", () => new BarrackPresentationLogic());
                presentation = ((BarrackPresentationLogic)character.Logic).Host;
                scope.OwnAsync(presentation.StopAsync); // Await native scene/graph teardown before EntityOwner releases its prefab lease.
                await presentation.EnsureInitializedAsync(scope.Token);
                if (!presentation.Ready) throw new InvalidOperationException(presentation.Failure ?? "Barrack presentation was cancelled");
                return;
            }
            if (string.IsNullOrWhiteSpace(content.sceneAddress) || string.IsNullOrWhiteSpace(content.modelAddress) || (string.IsNullOrWhiteSpace(content.idleAddress) && string.IsNullOrWhiteSpace(content.idleClipName)) || string.IsNullOrWhiteSpace(content.shaderLibraryAddress))
                throw new InvalidOperationException("Barrack content addresses are incomplete");
            if (string.IsNullOrWhiteSpace(content.animationLibraryAddress) ? string.IsNullOrWhiteSpace(content.idleAddress) : string.IsNullOrWhiteSpace(content.idleClipName))
                throw new InvalidOperationException("Idle animation address/name is missing for the selected library mode");
            lifetime = scope;
            await game.Scenes.LoadAsync(content.sceneAddress, scope);
            var shaderBytes = scope.Own(await game.Resources.LoadAsync<TextAsset>(content.shaderLibraryAddress, scope.Token));
            var shaders = scope.Own(await BarrackShaderLibrary.LoadAsync(shaderBytes.Asset.bytes, scope.Token));
            clips.Clear();
            var idleKey = content.idleAddress;
            if (!string.IsNullOrWhiteSpace(content.animationLibraryAddress))
            {
                var animationBytes = scope.Own(await game.Resources.LoadAsync<TextAsset>(content.animationLibraryAddress, scope.Token));
                var animations = scope.Own(await BarrackAnimationLibrary.LoadAsync(animationBytes.Asset.bytes, scope.Token));
                idleKey = content.idleClipName;
                foreach (var name in new[] { idleKey }.Concat(content.actionClipNames ?? Array.Empty<string>()).Distinct())
                    clips.Add(name, animations.Get(name));
            }
            else
            foreach (var address in new[] { content.idleAddress }.Concat(content.actionAddresses ?? Array.Empty<string>()).Distinct())
            {
                var lease = scope.Own(await game.Resources.LoadAsync<AnimationClip>(address, scope.Token));
                clips.Add(address, lease.Asset);
            }
            // Entity ownership is registered last: instances/graphs are destroyed before animation leases and scene.
            entities = game.Entities.CreateOwner(scope, "barrack/character");
            character = await entities.ShowAsync(game.Entities.NextId(), content.modelAddress, "Barrack", () => new BarrackCharacterLogic(), new BarrackCharacterShow(clips[idleKey], shaders));
        }
        public void PlayAction(string address)
        {
            if (lifetime == null || !lifetime.IsActive || character == null || !character.IsShown) throw new InvalidOperationException("Barrack is not active");
            if (presentation != null)
            {
                var action = BarrackInteraction.Clips.FirstOrDefault(pair => pair.Value == address);
                if (action.Value == null || !presentation.Interaction.Request(action.Key)) throw new InvalidOperationException("Barrack interaction is busy or action is unavailable");
                return;
            }
            if (!clips.TryGetValue(address, out var clip)) throw new ArgumentException("Action is not loaded", nameof(address));
            ((BarrackCharacterLogic)character.Logic).Play(clip);
        }
        public override void Update(ProcedureManager owner, float deltaTime) => entities?.Tick(deltaTime);
        public override Task ExitAsync(ProcedureManager owner)
        { lifetime = null; character = null; entities = null; presentation = null; clips.Clear(); return Task.CompletedTask; }
    }

    public sealed class BarrackPresentationLogic : EntityLogic<GameObject>
    {
        public BarrackPreview Host { get; private set; }
        public override void OnShow(object data) { Host = Instance.GetComponent<BarrackPreview>() ?? throw new InvalidOperationException("Presentation prefab has no BarrackPreview"); }
        public override void OnHide() => Host?.BeginStop();
        public override void OnDestroy() => Host?.BeginStop();
    }

    public sealed class BarrackCharacterShow
    {
        public readonly AnimationClip Idle;
        public readonly BarrackShaderLibrary Shaders;
        public BarrackCharacterShow(AnimationClip idle, BarrackShaderLibrary shaders) { Idle = idle; Shaders = shaders; }
    }

    public sealed class BarrackCharacterLogic : EntityLogic<GameObject>
    {
        IDisposable shaderBinding;
        PlayableGraph graph;
        AnimationPlayableOutput output;
        AnimationClipPlayable current;
        Animator animator;
        bool previousEnabled, previousFireEvents;
        public override void OnShow(object data)
        {
            try
            {
                var show = (BarrackCharacterShow)data;
                shaderBinding = show.Shaders.Bind(Instance);
                var renderers = Instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length == 0 || renderers.Any(r => r.sharedMesh == null || r.bones.Any(b => b == null) ||
                    r.sharedMaterials.Length < r.sharedMesh.subMeshCount || r.sharedMaterials.Any(m => m == null || m.shader == null || !m.shader.isSupported)))
                    throw new InvalidOperationException("Character source dependencies/rendering are incomplete");
                var animators = Instance.GetComponentsInChildren<Animator>(true);
                if (animators.Length != 1) throw new InvalidOperationException("Expected one verified character animator");
                animator = animators[0]; previousEnabled = animator.enabled; previousFireEvents = animator.fireEvents;
                animator.fireEvents = false; animator.enabled = true;
                graph = PlayableGraph.Create("Barrack.Character"); graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                output = AnimationPlayableOutput.Create(graph, "Character", animator);
                Play(show.Idle); graph.Play();
            }
            catch { Release(); throw; }
        }
        public void Play(AnimationClip clip)
        {
            if (!graph.IsValid() || clip == null || clip.empty || clip.length <= 0) throw new InvalidOperationException("Character animation is unavailable");
            if (animator.fireEvents) throw new InvalidOperationException("Source animation callbacks must be disabled by the host");
            if (current.IsValid()) graph.DestroyPlayable(current);
            current = AnimationClipPlayable.Create(graph, clip); current.SetTime(0);
            output.SetSourcePlayable(current);
        }
        void Release()
        {
            if (graph.IsValid()) graph.Destroy();
            if (animator != null) { animator.enabled = previousEnabled; animator.fireEvents = previousFireEvents; }
            animator = null;
            shaderBinding?.Dispose(); shaderBinding = null;
        }
        public override void OnHide() => Release();
        public override void OnRecycle() => Release();
        public override void OnDestroy() => Release();
    }
}
