using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed class SceneHandle
    {
        internal readonly SceneService Service;
        internal readonly CancellationTokenSource Cancellation;
        internal readonly TaskCompletionSource<bool> LoadFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Unloading;
        internal SceneAssetLease Lease;
        internal IDisposable Ownership;
        internal Scene Previous;
        public string Address { get; }
        public string AssetPath { get; internal set; }
        public Scene Scene { get; internal set; }
        public bool IsLoaded => Scene.IsValid() && Scene.isLoaded;
        public bool IsUnloading => Unloading != null;
        internal SceneHandle(SceneService service, string address, CancellationTokenSource cancellation)
        { Service = service; Address = address; Cancellation = cancellation; }
        public Task UnloadAsync() => Service.UnloadAsync(this);
    }
    public sealed class SceneService : IModule
    {
        readonly ResourceService resources;
        readonly IGameLog log;
        readonly Dictionary<string, SceneHandle> scenes = new Dictionary<string, SceneHandle>(StringComparer.Ordinal);
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        bool stopped;
        Task stopping;
        public string Id => "scenes";
        public IReadOnlyList<string> Dependencies { get; } = new[] { "resources" };
        public string Current => SceneManager.GetActiveScene().name;
        public int LoadedCount => scenes.Values.Count(s => s.IsLoaded);
        public int PendingCount => scenes.Values.Count(s => !s.LoadFinished.Task.IsCompleted || s.Unloading != null);
        public SceneService(ResourceService resources, IGameLog log) { this.resources = resources; this.log = log; }
        public Task InitializeAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task StartAsync(CancellationToken cancellation) => Task.CompletedTask;
        public bool TryGet(string address, out SceneHandle scene) => scenes.TryGetValue(address, out scene);
        public SceneHandle[] GetLoadedScenes() => scenes.Values.Where(s => s.IsLoaded).ToArray();
        public Task<SceneHandle> LoadAsync(string address, StateScope owner, bool activate = true, CancellationToken cancellation = default)
        {
            if (stopped || !owner.IsActive) throw new InvalidOperationException("Scene service or owner is closing");
            if (scenes.ContainsKey(address)) throw new InvalidOperationException("Scene already reserved: " + address);
            var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, lifetime.Token, cancellation);
            var handle = new SceneHandle(this, address, linked); scenes.Add(address, handle);
            handle.Ownership = owner.OwnAsync(handle.UnloadAsync);
            return LoadCoreAsync(handle, activate);
        }
        async Task<SceneHandle> LoadCoreAsync(SceneHandle handle, bool activate)
        {
            Exception failure = null; bool entered = false;
            try
            {
                await gate.WaitAsync(handle.Cancellation.Token); entered = true;
                handle.Lease = await resources.LoadSceneBundleAsync(handle.Address, handle.Cancellation.Token);
                handle.AssetPath = handle.Lease.Path;
                handle.Cancellation.Token.ThrowIfCancellationRequested();
                if (scenes.Values.Any(s => s != handle && s.Lease?.Path == handle.Lease.Path)) throw new InvalidOperationException("Scene path already loaded");
                handle.Previous = SceneManager.GetActiveScene();
                var operation = SceneManager.LoadSceneAsync(handle.Lease.Path, LoadSceneMode.Additive)
                    ?? throw new InvalidOperationException("Scene load could not start: " + handle.Address);
                // Native scene loading cannot be cancelled. Keep its lease until load and any rollback unload finish.
                await UnityTask.Complete(operation);
                handle.Scene = SceneManager.GetSceneByPath(handle.Lease.Path);
                if (!handle.IsLoaded) throw new InvalidOperationException("Scene did not finish loading: " + handle.Address);
                handle.Cancellation.Token.ThrowIfCancellationRequested();
                if (activate && !SceneManager.SetActiveScene(handle.Scene)) throw new InvalidOperationException("Cannot activate scene");
                log.Write(GameLogLevel.Information, "scene", "scene.loaded", handle.Address + " / " + handle.Lease.Path);
            }
            catch (Exception error) { failure = error; }
            finally { if (entered) gate.Release(); handle.LoadFinished.TrySetResult(true); }
            if (failure != null)
            {
                try { await UnloadAsync(handle); } catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return handle;
        }
        public void SetActive(SceneHandle handle)
        {
            if (handle.Service != this || !handle.IsLoaded || handle.IsUnloading || stopped) throw new InvalidOperationException("Scene is not available");
            if (!SceneManager.SetActiveScene(handle.Scene)) throw new InvalidOperationException("Cannot activate scene");
        }
        public Task UnloadAsync(SceneHandle handle)
        {
            if (handle.Service != this) throw new ArgumentException("Foreign scene handle");
            if (handle.Unloading != null) return handle.Unloading;
            if (!scenes.TryGetValue(handle.Address, out var current) || current != handle) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            handle.Unloading = completion.Task; handle.Cancellation.Cancel();
            StateScope.Drive(() => UnloadCoreAsync(handle, completion)); return completion.Task;
        }
        async Task UnloadCoreAsync(SceneHandle handle, TaskCompletionSource<bool> completion)
        {
            await Task.Yield(); bool entered = false;
            try
            {
                await handle.LoadFinished.Task; await gate.WaitAsync(); entered = true;
                if (handle.IsLoaded)
                {
                    if (SceneManager.GetActiveScene() == handle.Scene && handle.Previous.IsValid() && handle.Previous.isLoaded)
                        SceneManager.SetActiveScene(handle.Previous);
                    var operation = SceneManager.UnloadSceneAsync(handle.Scene) ?? throw new InvalidOperationException("Scene unload could not start");
                    await UnityTask.Complete(operation);
                    if (handle.IsLoaded) throw new InvalidOperationException("Scene remained loaded; resource lease retained");
                }
                handle.Lease?.Dispose(); handle.Lease = null; handle.Ownership.Dispose(); handle.Cancellation.Dispose();
                scenes.Remove(handle.Address);
                log.Write(GameLogLevel.Information, "scene", "scene.unloaded", handle.Address);
                completion.TrySetResult(true);
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally { if (entered) gate.Release(); }
        }
        public Task StopAsync(CancellationToken cancellation)
        {
            if (stopping != null) return stopping;
            stopped = true; lifetime.Cancel(); stopping = Task.WhenAll(scenes.Values.ToArray().Select(UnloadAsync)); return stopping;
        }
        public void Dispose()
        {
            if (scenes.Count != 0) throw new InvalidOperationException("Scenes still hold resource leases");
            lifetime.Dispose(); gate.Dispose();
        }
    }
}
