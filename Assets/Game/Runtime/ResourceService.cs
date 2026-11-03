using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Runtime
{
    public sealed class SceneAssetLease : IDisposable
    {
        Action release;
        public string Path { get; }
        internal SceneAssetLease(string path, Action release) { Path = path; this.release = release; }
        public void Dispose() { var action = release; release = null; action?.Invoke(); }
    }
    public sealed class ResourceService : IModule
    {
        sealed class Bundle { public AssetBundle Value; public int References; public int LastReleaseFrame = -1; }
        readonly string directory;
        readonly ResourceCatalog catalog;
        readonly Dictionary<string, Bundle> bundles = new Dictionary<string, Bundle>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Object> cache = new Dictionary<string, Object>();
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        bool initialized, stopped, disposed;
        int references, pending;
        Task stopping;
        public string Id => "resources";
        public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();
        public int ReferenceCount => references;
        public int CachedCount => cache.Count;
        public int LoadedBundleCount => bundles.Count;
        public bool IsBundleLoaded(string name) => bundles.ContainsKey(name);
        public int PendingCount => pending;
        public int BundleReferences(string bundle) => bundles.TryGetValue(bundle, out var value) ? value.References : 0;
        public ResourceService(string directory, ReleaseManifest release) { this.directory = directory; catalog = new ResourceCatalog(release); }
        public Task InitializeAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); initialized = true; return Task.CompletedTask; }
        public Task StartAsync(CancellationToken cancellation) => Task.CompletedTask;
        public async Task<AssetLease<T>> LoadAsync<T>(string address, CancellationToken cancellation) where T : Object
        {
            if (!initialized || stopped) throw new ObjectDisposedException(nameof(ResourceService));
            var asset = catalog.Resolve(address); string[] order = catalog.LoadOrder(asset.bundle);
            if (asset.kind != "asset") throw new InvalidOperationException("Use the scene service for scene addresses: " + address);
            pending++; bool entered = false;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
                {
                    await gate.WaitAsync(linked.Token); entered = true;
                    await EnsureBundlesAsync(order, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    if (!cache.TryGetValue(address, out var value))
                    {
                        var request = bundles[asset.bundle].Value.LoadAssetAsync<T>(asset.asset); await UnityTask.Complete(request);
                        linked.Token.ThrowIfCancellationRequested();
                        value = request.asset;
                        if (value == null) throw new InvalidDataException("Missing asset or wrong type: " + address);
                        cache.Add(address, value);
                    }
                    if (!(value is T typed)) throw new InvalidOperationException("Asset type mismatch: " + address + " / requested " + typeof(T).Name);
                    linked.Token.ThrowIfCancellationRequested();
                    return new AssetLease<T>(typed, Retain(order));
                }
            }
            finally { if (entered) gate.Release(); pending--; }
        }
        async Task EnsureBundlesAsync(string[] order, CancellationToken cancellation)
        {
            foreach (string name in order)
            {
                cancellation.ThrowIfCancellationRequested();
                if (bundles.ContainsKey(name)) continue;
                var request = AssetBundle.LoadFromFileAsync(System.IO.Path.Combine(directory, name)); await UnityTask.Complete(request);
                if (request.assetBundle == null) throw new InvalidDataException("Unable to load bundle: " + name);
                bundles.Add(name, new Bundle { Value = request.assetBundle });
            }
        }
        Action Retain(string[] order)
        {
            foreach (string name in order) bundles[name].References++; references++;
            return () =>
            {
                foreach (string name in order) { bundles[name].References--; bundles[name].LastReleaseFrame = Time.frameCount; }
                references--;
            };
        }
        public async Task<SceneAssetLease> LoadSceneBundleAsync(string address, CancellationToken cancellation)
        {
            if (!initialized || stopped) throw new ObjectDisposedException(nameof(ResourceService));
            var asset = catalog.Resolve(address);
            if (asset.kind != "scene") throw new InvalidOperationException("Address is not a scene: " + address);
            string[] order = catalog.LoadOrder(asset.bundle); pending++; bool entered = false;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
                {
                    await gate.WaitAsync(linked.Token); entered = true;
                    await EnsureBundlesAsync(order, linked.Token); linked.Token.ThrowIfCancellationRequested();
                    var bundle = bundles[asset.bundle].Value;
                    if (!bundle.isStreamedSceneAssetBundle || !bundle.GetAllScenePaths().Contains(asset.asset, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException("Scene is absent from its signed bundle: " + address);
                    string path = bundle.GetAllScenePaths().Single(p => string.Equals(p, asset.asset, StringComparison.OrdinalIgnoreCase));
                    return new SceneAssetLease(path, Retain(order));
                }
            }
            finally { if (entered) gate.Release(); pending--; }
        }
        public async Task<int> TrimUnusedAsync(CancellationToken cancellation)
        {
            if (stopped) throw new ObjectDisposedException(nameof(ResourceService));
            pending++; bool entered = false;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
                {
                await gate.WaitAsync(linked.Token); entered = true;
                // Allow Destroy calls made by owners to reach Unity's end-of-frame disposal.
                await UnityTask.NextFrame(); linked.Token.ThrowIfCancellationRequested();
                // Other owners can release during this wait, including earlier in this frame.
                var idle = bundles.Where(pair => pair.Value.References == 0 && pair.Value.LastReleaseFrame < Time.frameCount).Select(pair => pair.Key).ToArray();
                foreach (string address in cache.Keys.ToArray())
                    if (idle.Contains(catalog.Resolve(address).bundle, StringComparer.OrdinalIgnoreCase)) cache.Remove(address);
                foreach (string name in idle) { bundles[name].Value.Unload(true); bundles.Remove(name); }
                return idle.Length;
                }
            }
            finally { if (entered) gate.Release(); pending--; }
        }
        public Task StopAsync(CancellationToken cancellation)
        {
            if (stopping != null) return stopping;
            stopped = true; lifetime.Cancel(); stopping = StopCoreAsync(); return stopping;
        }
        async Task StopCoreAsync()
        {
            await gate.WaitAsync();
            try { while (pending > 0) await Task.Yield(); await UnityTask.NextFrame(); }
            finally { gate.Release(); }
        }
        public void Dispose()
        {
            if (disposed) return;
            if (pending != 0 || references != 0) throw new InvalidOperationException("Resource operations or leases still in use");
            foreach (var bundle in bundles.Values) bundle.Value.Unload(true);
            bundles.Clear(); cache.Clear(); disposed = true; stopped = true; lifetime.Dispose(); gate.Dispose();
        }
    }
}
