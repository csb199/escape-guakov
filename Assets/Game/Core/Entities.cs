using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;

namespace Game.Core
{
    public interface IEntityProvider<T> where T : class
    {
        Task<EntityInstance<T>> CreateAsync(string asset, CancellationToken cancellation);
        void SetVisible(T instance, bool visible);
    }
    public sealed class EntityInstance<T> : IDisposable where T : class
    {
        Action release;
        public T Value { get; }
        public EntityInstance(T value, Action release) { Value = value ?? throw new ArgumentNullException(nameof(value)); this.release = release; }
        public void Dispose() { var action = release; release = null; action?.Invoke(); }
    }
    public interface IEntityInteraction { void OnInteract(); }
    public abstract class EntityLogic<T> where T : class
    {
        public T Instance { get; internal set; }
        public EntityHandle<T> Entity { get; internal set; }
        public virtual void OnInit() { }
        public virtual void OnShow(object data) { }
        public virtual void OnUpdate(float deltaTime) { }
        public virtual void OnHide() { }
        public virtual void OnRecycle() { }
        public virtual void OnDestroy() { }
        public virtual void OnAttachTo(EntityHandle<T> parent, object data) { }
        public virtual void OnDetachFrom(EntityHandle<T> parent, object data) { }
        public virtual void OnAttached(EntityHandle<T> child, object data) { }
        public virtual void OnDetached(EntityHandle<T> child, object data) { }
    }
    // A handle identifies one activation, not a pooled instance. Old handles never become valid again.
    public sealed class EntityHandle<T> where T : class
    {
        internal readonly EntityManager<T> Manager;
        internal EntityManager<T>.Slot Slot;
        internal CancellationTokenSource Loading;
        internal readonly TaskCompletionSource<bool> ShowFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task HideTask;
        internal TaskCompletionSource<bool> HideCompletion;
        internal bool HideStarted;
        internal bool Visible, ShowCalled, Broken;
        internal readonly HashSet<EntityHandle<T>> Children = new HashSet<EntityHandle<T>>();
        public EntityHandle<T> Parent { get; internal set; }
        public EntityHandle<T>[] GetChildren() => Children.ToArray();
        public int Id { get; }
        public string Asset { get; }
        public string Group { get; }
        public EntityOwner<T> Owner { get; }
        public StateScope Scope { get; internal set; }
        public T Instance => Slot?.Instance.Value;
        public EntityLogic<T> Logic => Slot?.Logic;
        public bool IsShown => Visible && Owner.IsActive && Scope != null && Scope.IsActive;
        internal EntityHandle(EntityManager<T> manager, EntityOwner<T> owner, int id, string asset, string group)
        { Manager = manager; Owner = owner; Id = id; Asset = asset; Group = group; }
        public bool Interact() => Manager.Interact(this);
        public bool RequestHide() => Manager.RequestHide(this);
        public Task HideAsync() => Manager.HideAsync(this);
    }
    public sealed class EntityOwner<T> where T : class
    {
        internal readonly StateScope Lifetime;
        internal readonly EntityManager<T> Manager;
        internal readonly HashSet<EntityHandle<T>> Members = new HashSet<EntityHandle<T>>();
        internal readonly List<EntityHandle<T>> UpdateSnapshot = new List<EntityHandle<T>>();
        internal bool Updating;
        Task closeTask;
        internal EntityOwner(EntityManager<T> manager, StateScope lifetime) { Manager = manager; Lifetime = lifetime; lifetime.Owner = this; }
        public bool IsActive => closeTask == null && Lifetime.IsActive;
        public EntityHandle<T>[] GetEntities() => Manager.GetOwned(this);
        public async Task<EntityHandle<T>> ShowAsync<TLogic>(int id, string asset, string group, Func<TLogic> factory, object data = null,
            CancellationToken cancellation = default) where TLogic : EntityLogic<T>
        {
            if (!IsActive) throw new InvalidOperationException("Entity owner is closing.");
            EntityHandle<T> result = null;
            OperationCanceledException cancelled = null;
            await Lifetime.Run(async token =>
            {
                try { result = await Manager.ShowAsync(this, id, asset, group, factory, data, cancellation); }
                catch (OperationCanceledException e) { cancelled = e; }
            });
            if (cancelled != null) throw cancelled;
            return result;
        }
        public void Tick(float deltaTime) { if (IsActive) Manager.TickOwner(this, deltaTime); }
        public Task CloseAsync()
        {
            if (StateScope.ExecutingWithin(this))
                throw new InvalidOperationException("An entity task cannot await its own owner shutdown.");
            if (closeTask != null) return closeTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            closeTask = completion.Task; Lifetime.Cancel(); StateScope.Drive(() => CloseCoreAsync(completion)); return closeTask;
        }
        async Task CloseCoreAsync(TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            try { await Lifetime.CloseAsync(); } catch (Exception e) { errors.Add(e); }
            try { await Manager.CloseOwnerAsync(this); } catch (Exception e) { errors.Add(e); }
            if (errors.Count > 0) completion.TrySetException(new AggregateException("Entity owner cleanup failed", errors));
            else completion.TrySetResult(true);
        }
    }
    public sealed class EntityGroupInfo
    {
        public readonly string Name;
        public readonly int Capacity, Reserved, Cached;
        internal EntityGroupInfo(string name, int capacity, int reserved, int cached)
        { Name = name; Capacity = capacity; Reserved = reserved; Cached = cached; }
    }
    // All operations run on the owning context. Unity adapters use the main thread.
    public partial class EntityManager<T> where T : class
    {
        internal sealed class Slot
        {
            public EntityInstance<T> Instance;
            public EntityLogic<T> Logic;
            public (string, Type) Key;
            public int CacheId, Priority;
            public bool Locked;
            public double LastUse;
        }
        sealed class Group
        {
            public int Capacity, Reserved, Cached;
            public EntityCachePolicy Policy;
            public double NextRelease;
            public readonly HashSet<EntityHandle<T>> Members = new HashSet<EntityHandle<T>>();
            public readonly Dictionary<(string, Type), Stack<Slot>> Idle = new Dictionary<(string, Type), Stack<Slot>>();
        }
        readonly IEntityProvider<T> provider;
        readonly IGameLog log;
        readonly Func<double> clock;
        readonly Dictionary<string, Group> groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        readonly Dictionary<int, EntityHandle<T>> entities = new Dictionary<int, EntityHandle<T>>();
        readonly Dictionary<T, EntityHandle<T>> instances = new Dictionary<T, EntityHandle<T>>();
        readonly HashSet<EntityOwner<T>> owners = new HashSet<EntityOwner<T>>();
        Task stopTask;
        bool stopped;
        int nextId;
        public int ActiveCount => entities.Values.Count(e => e.IsShown);
        public int LoadingCount => entities.Values.Count(e => !e.ShowFinished.Task.IsCompleted);
        public int InstanceCount { get; private set; }
        public int CreatedCount { get; private set; }
        public EntityManager(IEntityProvider<T> provider, IGameLog log, Func<double> clock = null)
        { this.provider = provider; this.log = log; this.clock = clock ?? (() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency); }
        public int NextId() { do { nextId = checked(nextId + 1); } while (entities.ContainsKey(nextId)); return nextId; }
        public void AddGroup(string name, int capacity, EntityCachePolicy cachePolicy = null)
        {
            if (stopped) throw new ObjectDisposedException(nameof(EntityManager<T>));
            if (string.IsNullOrWhiteSpace(name) || capacity < 1) throw new ArgumentException("Entity group requires a name and positive capacity.");
            var policy = (cachePolicy ?? new EntityCachePolicy { maxCached = capacity }).Snapshot();
            groups.Add(name, new Group { Capacity = capacity, Policy = policy, NextRelease = clock() + policy.releaseIntervalSeconds });
        }
        public EntityGroupInfo GetGroup(string name)
        {
            var group = groups[name]; return new EntityGroupInfo(name, group.Capacity, group.Reserved, group.Cached);
        }
        public EntityOwner<T> CreateOwner(StateScope parent, string name)
        {
            if (stopped || !parent.IsActive) throw new InvalidOperationException("Cannot create an entity owner during shutdown.");
            var owner = new EntityOwner<T>(this, new StateScope(name, log, parent.Token) { Parent = parent });
            parent.OwnAsync(owner.CloseAsync); owners.Add(owner); return owner;
        }
        public bool IsLoading(int id) => entities.TryGetValue(id, out var entity) && !entity.ShowFinished.Task.IsCompleted;
        public bool TryGet(int id, out EntityHandle<T> entity)
        {
            if (entities.TryGetValue(id, out entity) && entity.IsShown) return true;
            entity = null; return false;
        }
        public bool TryGetByInstance(T instance, out EntityHandle<T> entity)
        {
            if (instances.TryGetValue(instance, out entity) && entity.IsShown) return true;
            entity = null; return false;
        }
        public EntityHandle<T>[] GetGroupEntities(string group) => groups[group].Members.Where(e => e.IsShown).ToArray();
        internal EntityHandle<T>[] GetOwned(EntityOwner<T> owner) => owner.Members.Where(e => e.IsShown).ToArray();
        bool Contains(EntityHandle<T> entity) => entity != null && entity.Manager == this && entities.TryGetValue(entity.Id, out var current) && ReferenceEquals(current, entity);
        internal async Task<EntityHandle<T>> ShowAsync<TLogic>(EntityOwner<T> owner, int id, string asset, string groupName,
            Func<TLogic> factory, object data, CancellationToken cancellation) where TLogic : EntityLogic<T>
        {
            if (stopped || !owner.IsActive) throw new InvalidOperationException("Entity service or owner is closing.");
            if (string.IsNullOrWhiteSpace(asset) || factory == null) throw new ArgumentException("Entity asset and logic factory are required.");
            if (!groups.TryGetValue(groupName, out var group)) throw new InvalidOperationException("Unknown entity group: " + groupName);
            if (entities.ContainsKey(id)) throw new InvalidOperationException("Entity ID already reserved: " + id);
            if (group.Reserved >= group.Capacity) throw new InvalidOperationException("Entity group capacity reached: " + groupName);
            var entity = new EntityHandle<T>(this, owner, id, asset, groupName);
            entity.Loading = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token, cancellation);
            entities.Add(id, entity); owner.Members.Add(entity); group.Members.Add(entity); group.Reserved++;
            try
            {
                var token = entity.Loading.Token; token.ThrowIfCancellationRequested();
                var key = (asset, typeof(TLogic));
                if (group.Idle.TryGetValue(key, out var idle) && idle.Count > 0) { entity.Slot = idle.Pop(); group.Cached--; }
                else
                {
                    var instance = await provider.CreateAsync(asset, token);
                    entity.Slot = new Slot { Instance = instance, Key = key, CacheId = ++CreatedCount }; InstanceCount++;
                    token.ThrowIfCancellationRequested();
                    entity.Slot.Logic = factory() ?? throw new InvalidOperationException("Null entity logic");
                    entity.Slot.Logic.Instance = instance.Value;
                    entity.Slot.Logic.OnInit();
                }
                token.ThrowIfCancellationRequested();
                entity.Scope = new StateScope("entity/" + id, log, owner.Lifetime.Token) { Owner = entity, Parent = owner.Lifetime };
                entity.Slot.Logic.Entity = entity; entity.ShowCalled = true;
                entity.Slot.Logic.OnShow(data);
                token.ThrowIfCancellationRequested();
                if (entity.HideTask != null) throw new OperationCanceledException("Entity hidden during show.");
                provider.SetVisible(entity.Instance, true); entity.Visible = true;
                instances.Add(entity.Instance, entity);
                log.Write(GameLogLevel.Debug, "entity", "entity.shown", id + " / " + groupName + " / " + asset);
                return entity;
            }
            catch (Exception error)
            {
                entity.Broken = true;
                try { await ReleaseAsync(entity); } catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
            finally { entity.Loading.Dispose(); entity.Loading = null; entity.ShowFinished.TrySetResult(true); }
        }
        public Task HideAsync(int id) => entities.TryGetValue(id, out var entity) ? HideAsync(entity) : Task.CompletedTask;
        public Task HideAsync(EntityHandle<T> entity)
        {
            EnsureCanHide(entity);
            return BeginHide(entity);
        }
        public bool RequestHide(EntityHandle<T> entity)
        {
            if (!Contains(entity) || entity.HideTask != null) return false;
            _ = ObserveHide(BeginHide(entity)); return true;
        }
        static async Task ObserveHide(Task task) { try { await task; } catch { /* HideCoreAsync reports errors. */ } }
        Task BeginHide(EntityHandle<T> entity)
        {
            if (!Contains(entity)) return Task.CompletedTask;
            if (entity.HideTask != null) return entity.HideTask;
            var tree = new List<EntityHandle<T>>(); var pending = new Stack<EntityHandle<T>>(); pending.Push(entity);
            while (pending.Count > 0)
            {
                var next = pending.Pop(); tree.Add(next);
                foreach (var child in next.Children) pending.Push(child);
            }
            // Reserve teardown for the whole tree before cancellation callbacks can run.
            foreach (var item in tree) PrepareHide(item);
            foreach (var item in tree) StartHide(item);
            return entity.HideTask;
        }
        void PrepareHide(EntityHandle<T> entity)
        {
            if (entity.HideTask != null) return;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            entity.HideCompletion = completion; entity.HideTask = completion.Task; entity.Visible = false;
        }
        void StartHide(EntityHandle<T> entity)
        {
            if (entity.HideStarted) return; entity.HideStarted = true;
            entity.Scope?.Cancel();
            try { entity.Loading?.Cancel(); }
            catch (Exception e) { entity.Broken = true; log.Write(GameLogLevel.Error, "entity", "entity.cancel.failed", entity.Id.ToString(), exception: e); }
            var children = entity.Children.ToArray();
            StateScope.Drive(() => HideCoreAsync(entity, children, entity.HideCompletion));
        }
        async Task HideCoreAsync(EntityHandle<T> entity, EntityHandle<T>[] children, TaskCompletionSource<bool> completion)
        {
            // OnInteract/OnUpdate must return before their instance can be recycled.
            await Task.Yield();
            try
            {
                await entity.ShowFinished.Task;
                var errors = new List<Exception>();
                try { await Task.WhenAll(children.Select(c => c.HideTask)); } catch (Exception e) { errors.Add(e); }
                if (Contains(entity)) try { await ReleaseAsync(entity); } catch (Exception e) { errors.Add(e); }
                if (errors.Count > 0) throw new AggregateException("Entity tree cleanup failed", errors);
                completion.TrySetResult(true);
            }
            catch (Exception e)
            {
                log.Write(GameLogLevel.Error, "entity", "entity.hide.failed", entity.Id.ToString(), exception: e);
                completion.TrySetException(e);
            }
        }
        async Task ReleaseAsync(EntityHandle<T> entity)
        {
            entity.Visible = false; var errors = new List<Exception>(); var slot = entity.Slot;
            if (slot != null) instances.Remove(slot.Instance.Value);
            if (entity.Scope != null) try { await entity.Scope.CloseAsync(); } catch (Exception e) { errors.Add(e); }
            try { DetachCore(entity, null); } catch (Exception e) { errors.Add(e); }
            if (slot != null)
            {
                if (entity.ShowCalled) try { slot.Logic.OnHide(); } catch (Exception e) { errors.Add(e); }
                try { slot.Logic?.OnRecycle(); } catch (Exception e) { errors.Add(e); }
                try { provider.SetVisible(slot.Instance.Value, false); } catch (Exception e) { errors.Add(e); }
                if (slot.Logic != null) slot.Logic.Entity = null;
                if (entity.Broken || errors.Count > 0 || stopped) DestroySlot(slot, errors);
                else
                {
                    var idle = groups[entity.Group].Idle;
                    if (!idle.TryGetValue(slot.Key, out var stack)) idle.Add(slot.Key, stack = new Stack<Slot>());
                    slot.LastUse = clock(); stack.Push(slot); groups[entity.Group].Cached++;
                }
            }
            entities.Remove(entity.Id); entity.Owner.Members.Remove(entity); groups[entity.Group].Members.Remove(entity);
            groups[entity.Group].Reserved--; entity.Slot = null;
            if (groups[entity.Group].Cached > groups[entity.Group].Policy.maxCached)
                try { TrimCache(entity.Group); } catch (Exception e) { errors.Add(e); }
            if (errors.Count > 0) throw new AggregateException("Entity cleanup failed: " + entity.Id, errors);
            log.Write(GameLogLevel.Debug, "entity", "entity.recycled", entity.Id.ToString());
        }
        void DestroySlot(Slot slot, List<Exception> errors)
        {
            try { slot.Logic?.OnDestroy(); } catch (Exception e) { errors.Add(e); }
            try { slot.Instance.Dispose(); } catch (Exception e) { errors.Add(e); }
            InstanceCount--;
        }
        public bool Interact(EntityHandle<T> entity)
        {
            if (!Contains(entity) || !entity.IsShown || !(entity.Logic is IEntityInteraction interaction)) return false;
            try { interaction.OnInteract(); return true; }
            catch (Exception e) { entity.Broken = true; RequestHide(entity); log.Write(GameLogLevel.Error, "entity", "entity.interact.failed", entity.Id.ToString(), exception: e); throw; }
        }
        internal void TickOwner(EntityOwner<T> owner, float deltaTime)
        {
            if (owner.Updating) throw new InvalidOperationException("Entity updates cannot reenter");
            owner.Updating = true; owner.UpdateSnapshot.Clear(); owner.UpdateSnapshot.AddRange(owner.Members);
            try { foreach (var entity in owner.UpdateSnapshot)
            {
                if (!entity.IsShown) continue;
                try { entity.Logic.OnUpdate(deltaTime); }
                catch (Exception e) { entity.Broken = true; RequestHide(entity); log.Write(GameLogLevel.Error, "entity", "entity.update.failed", entity.Id.ToString(), exception: e); }
            }
            } finally { owner.UpdateSnapshot.Clear(); owner.Updating = false; }
        }
        internal async Task CloseOwnerAsync(EntityOwner<T> owner)
        {
            var errors = new List<Exception>();
            foreach (var entity in owner.Members.ToArray())
                try { await HideAsync(entity); } catch (Exception e) { errors.Add(e); }
            owners.Remove(owner);
            if (errors.Count > 0) throw new AggregateException("Owned entities failed to close", errors);
        }
        public virtual Task StopAsync()
        {
            for (var scope = StateScope.Executing; scope != null; scope = scope.Parent)
                if (scope.Owner is EntityOwner<T> owner && owner.Manager == this)
                    throw new InvalidOperationException("An entity task cannot await its own service shutdown.");
            if (stopTask != null) return stopTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            stopTask = completion.Task; stopped = true;
            foreach (var owner in owners.ToArray()) owner.Lifetime.Cancel();
            StateScope.Drive(() => StopCoreAsync(completion)); return stopTask;
        }
        async Task StopCoreAsync(TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            try
            {
            foreach (var owner in owners.ToArray()) try { await owner.CloseAsync(); } catch (Exception e) { errors.Add(e); }
            var cached = groups.Values.SelectMany(group => group.Idle.Values).SelectMany(stack => stack).ToArray();
            foreach (var group in groups.Values)
            {
                group.Cached = 0; group.Idle.Clear();
            }
            foreach (var slot in cached) DestroySlot(slot, errors);
            if (errors.Count > 0) throw new AggregateException("Entity service shutdown failed", errors);
            completion.TrySetResult(true);
            }
            catch (Exception e) { completion.TrySetException(e); }
        }
    }
}
