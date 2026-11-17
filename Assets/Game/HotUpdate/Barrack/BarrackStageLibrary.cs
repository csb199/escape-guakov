using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.HotUpdate.Barrack
{
    public sealed class BarrackStageLibrary
    {
        readonly List<BarrackNativeBundleLease> leases = new List<BarrackNativeBundleLease>();
        Scene scene;
        Scene previous;
        public Scene Scene => scene;
        public async Task LoadAsync(BarrackStageDefinition definition, CancellationToken cancellation)
        {
            if (definition == null || definition.bundles == null || definition.bundles.Length == 0) throw new InvalidOperationException("Source stage is not configured");
            try
            {
                previous = SceneManager.GetActiveScene();
                foreach (var bytes in definition.bundles)
                    { var lease = await BarrackNativeBundleLease.LoadAsync(bytes.bytes, cancellation); leases.Add(lease); if (bytes.name.Contains("93e96eb") || bytes.name.Contains("b2dbe043")) { lease.Bundle.LoadAllAssets<Shader>(); lease.Bundle.LoadAllAssets<ComputeShader>(); } }
                cancellation.ThrowIfCancellationRequested();
                var loading = SceneManager.LoadSceneAsync(definition.scenePath, LoadSceneMode.Additive);
                if (loading == null) throw new InvalidOperationException("Source stage cannot be loaded: " + definition.scenePath);
                await Wait(loading);
                scene = SceneManager.GetSceneByPath(definition.scenePath);
                if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Source stage scene did not activate");
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                    foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                    foreach (var director in root.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true)) director.Stop();
                }
                if(definition.reflectionReceiver!=null)
                {
                    var receiver=new GameObject("Source reflection receiver");SceneManager.MoveGameObjectToScene(receiver,scene);receiver.AddComponent<BarrackReflectionReceiver>().Initialize(definition.reflectionReceiver);
                }
                if(definition.beamContract!=null)
                {var beam=new GameObject("Source volumetric beam");SceneManager.MoveGameObjectToScene(beam,scene);beam.AddComponent<BarrackBeam>().Initialize(definition.beamContract,definition.beamShader);}
                SceneManager.SetActiveScene(scene); // Applies this scene's source RenderSettings and baked probe context.
                cancellation.ThrowIfCancellationRequested();
            }
            catch { await DisposeAsync(); throw; }
        }
        static async Task Wait(AsyncOperation operation)
        {
            if (operation.isDone) return;
            var completion = new TaskCompletionSource<bool>(); operation.completed += _ => completion.TrySetResult(true); await completion.Task;
        }
        public async Task DisposeAsync()
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded)
            {
                var unloading = SceneManager.UnloadSceneAsync(scene); if (unloading != null) await Wait(unloading);
            }
            scene = default;
            for (int i = leases.Count - 1; i >= 0; i--) leases[i].Dispose();
            leases.Clear();
        }
    }
}

