using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public sealed class EntityService : EntityManager<GameObject>, IModule
    {
        readonly UnityEntityProvider provider;
        readonly ResourceService resources;
        readonly IGameLog log;
        EntityCacheDriver driver;
        Task maintenance = Task.CompletedTask, stopping;
        float nextMaintenance;
        public string Id => "entities";
        public IReadOnlyList<string> Dependencies { get; } = new[] { "resources" };
        public EntityService(ResourceService resources, Transform root, IGameLog log) : this(new UnityEntityProvider(resources, root), resources, log) { }
        EntityService(UnityEntityProvider provider, ResourceService resources, IGameLog log) : base(provider, log)
        { this.provider = provider; this.resources = resources; this.log = log; }
        public Task InitializeAsync(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); provider.Initialize();
            driver = provider.Root.gameObject.AddComponent<EntityCacheDriver>(); driver.Service = this;
            return Task.CompletedTask;
        }
        public Task StartAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellation) => StopAsync();
        public override Task StopAsync()
        {
            if (stopping != null) return stopping;
            var entities = base.StopAsync();
            if (driver != null) driver.Service = null;
            stopping = Task.WhenAll(entities, maintenance); return stopping;
        }
        internal void TickCache()
        {
            if (stopping != null || !maintenance.IsCompleted || Time.unscaledTime < nextMaintenance) return;
            nextMaintenance = Time.unscaledTime + 1; maintenance = MaintainAsync();
        }
        async Task MaintainAsync()
        {
            try
            {
                MaintainCaches();
                // Also reclaim bundles released by capacity eviction during entity hide.
                await resources.TrimUnusedAsync(CancellationToken.None);
            }
            catch (Exception e) { log.Write(GameLogLevel.Error, "entity", "entity.cache.failed", e.Message, exception: e); }
        }
        public void Dispose()
        {
            if (InstanceCount != 0 || LoadingCount != 0) throw new InvalidOperationException("Entity service must finish stopping before disposal.");
            provider.Dispose();
        }
        public bool Interact(EntityOwner<GameObject> owner, GameObject hit)
        {
            for (var node = hit.transform; node != null; node = node.parent)
                if (TryGetByInstance(node.gameObject, out var entity)) return entity.Owner == owner && Interact(entity);
            return false;
        }
        sealed class UnityEntityProvider : IEntityProvider<GameObject>, IEntityAttachmentProvider<GameObject>, IDisposable
        {
            readonly ResourceService resources;
            readonly Transform parent;
            Transform root;
            public Transform Root => root;
            public UnityEntityProvider(ResourceService resources, Transform parent) { this.resources = resources; this.parent = parent; }
            public void Initialize() { root = new GameObject("EntityInstances").transform; root.SetParent(parent); }
            public async Task<EntityInstance<GameObject>> CreateAsync(string asset, CancellationToken cancellation)
            {
                var lease = await resources.LoadAsync<GameObject>(asset, cancellation);
                GameObject instance = null;
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    instance = UnityEngine.Object.Instantiate(lease.Asset, root); instance.SetActive(false);
                    return new EntityInstance<GameObject>(instance, () => { UnityEngine.Object.Destroy(instance); lease.Dispose(); });
                }
                catch { if (instance != null) UnityEngine.Object.Destroy(instance); lease.Dispose(); throw; }
            }
            public void SetVisible(GameObject instance, bool visible) => instance.SetActive(visible);
            public void Attach(GameObject child, GameObject parent, string socket, bool worldPositionStays)
            {
                var target = string.IsNullOrEmpty(socket) ? parent.transform : parent.transform.Find(socket);
                if (target == null || (target != parent.transform && !target.IsChildOf(parent.transform))) throw new ArgumentException("Missing entity socket: " + socket);
                child.transform.SetParent(target, worldPositionStays);
            }
            public void Detach(GameObject child) => child.transform.SetParent(root, true);
            public void Dispose() { if (root != null) UnityEngine.Object.Destroy(root.gameObject); root = null; }
        }
    }
    public sealed class EntityCacheDriver : MonoBehaviour
    {
        internal EntityService Service;
        void Update() => Service?.TickCache();
    }
}
