using Game.Core;

static class SceneEntityRegression
{
    sealed class Item { public Item Parent; public bool Visible; }
    sealed class Provider : IEntityProvider<Item>, IEntityAttachmentProvider<Item>
    {
        public int Created, Released;
        public Action OnAttach;
        public Task<EntityInstance<Item>> CreateAsync(string asset, CancellationToken token)
        { Created++; return Task.FromResult(new EntityInstance<Item>(new Item(), () => Released++)); }
        public void SetVisible(Item item, bool visible) => item.Visible = visible;
        public void Attach(Item child, Item parent, string socket, bool keepWorld) { if (socket == "missing") throw new ArgumentException("socket"); child.Parent = parent; OnAttach?.Invoke(); }
        public void Detach(Item child) => child.Parent = null;
    }
    sealed class Logic : EntityLogic<Item>
    {
        public int AttachTo, Attached, DetachFrom, Detached;
        public bool FailAttach;
        public Action Destroy, Detaching;
        public override void OnDestroy() => Destroy?.Invoke();
        public override void OnAttachTo(EntityHandle<Item> parent, object data) { AttachTo++; if (FailAttach) throw new InvalidOperationException("attach callback"); }
        public override void OnAttached(EntityHandle<Item> child, object data) => Attached++;
        public override void OnDetachFrom(EntityHandle<Item> parent, object data) { DetachFrom++; Detaching?.Invoke(); }
        public override void OnDetached(EntityHandle<Item> child, object data) => Detached++;
    }
    public static Task Run(Action<bool, string> check) => ProcedureRegression.SingleContext.Run(async () =>
    {
        var provider = new Provider(); double now = 0;
        var manager = new EntityManager<Item>(provider, new QuietLog(), () => now);
        manager.AddGroup("units", 3000, new EntityCachePolicy { maxCached = 20, expireSeconds = 10, releaseIntervalSeconds = 2, maxReleasePerPass = 2 });
        var scope = new StateScope("hierarchy", new QuietLog()); var owner = manager.CreateOwner(scope, "units");
        Task<EntityHandle<Item>> Show() => owner.ShowAsync(manager.NextId(), "unit", "units", () => new Logic());
        var parent = await Show(); var child = await Show(); var grandchild = await Show();
        manager.Attach(child, parent); manager.Attach(grandchild, child);
        check(child.Parent == parent && parent.GetChildren().Single() == child && child.Instance.Parent == parent.Instance && ((Logic)parent.Logic).Attached == 1,
            "entity attach updates provider hierarchy parent child indexes and lifecycle callbacks");
        bool cycle = false; try { manager.Attach(parent, grandchild); } catch (InvalidOperationException) { cycle = true; }
        check(cycle && parent.Parent == null, "entity attachment rejects cycles without changing the tree");
        bool selfAwait = false;
        await grandchild.Scope.Run(async _ => { try { await parent.HideAsync(); } catch (InvalidOperationException) { selfAwait = true; } });
        check(selfAwait && parent.IsShown, "child task cannot await parent teardown that waits for the child");
        var otherScope = new StateScope("other", new QuietLog()); var other = manager.CreateOwner(otherScope, "other");
        var foreign = await other.ShowAsync(manager.NextId(), "unit", "units", () => new Logic());
        bool crossOwner = false; try { manager.Attach(child, foreign); } catch (InvalidOperationException) { crossOwner = true; }
        check(crossOwner && child.Parent == parent, "cross-owner attachment is rejected without transferring lifetime ownership");
        manager.Detach(child);
        check(child.Parent == null && parent.GetChildren().Length == 0 && child.Instance.Parent == null && ((Logic)child.Logic).DetachFrom == 1,
            "detach restores provider root and notifies both entity logics");
        manager.Attach(child, parent);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = grandchild.Scope.Run(_ => gate.Task); var hide = parent.HideAsync(); await Task.Yield();
        check(!hide.IsCompleted && grandchild.Scope.Token.IsCancellationRequested && !child.IsShown && manager.GetGroup("units").Cached == 0,
            "parent hide cancels the whole tree and retains instances until descendant work drains");
        gate.SetResult(true); await work; await hide;
        check(parent.Instance == null && child.Parent == null && grandchild.Parent == null && manager.ActiveCount == 1 && manager.GetGroup("units").Cached == 3,
            "parent hide recycles descendants before parent and leaves other owner active");
        var reused = await Show(); bool stale = false;
        try { manager.Attach(child, reused); } catch (InvalidOperationException) { stale = true; }
        check(stale && reused.Parent == null && manager.TryGetByInstance(reused.Instance, out var found) && found == reused,
            "reused instances get fresh graph membership and stale handles cannot attach");
        await reused.HideAsync(); await otherScope.DisposeAsync(); manager.ReleaseAllUnused();

        parent = await Show(); child = await Show(); ((Logic)child.Logic).FailAttach = true;
        bool callbackFailed = false; try { manager.Attach(child, parent); } catch (AggregateException) { callbackFailed = true; }
        await child.HideAsync();
        check(callbackFailed && parent.GetChildren().Length == 0 && child.Instance == null && parent.IsShown,
            "attachment callback failure rolls back graph and discards the poisoned child");
        await parent.HideAsync(); manager.ReleaseAllUnused();

        parent = await Show(); child = await Show();
        provider.OnAttach = () => parent.RequestHide();
        manager.Attach(child, parent); await parent.HideAsync(); provider.OnAttach = null;
        check(child.Instance == null && child.Parent == null && owner.GetEntities().Length == 0,
            "provider reentrant parent-hide sees the committed attachment graph and drains its child");
        manager.ReleaseAllUnused();

        parent = await Show(); child = await Show(); var nextParent = await Show();
        manager.Attach(child, parent);
        ((Logic)child.Logic).Detaching = () => nextParent.RequestHide();
        bool closingTarget = false;
        try { manager.Attach(child, nextParent); } catch (AggregateException) { closingTarget = true; }
        check(closingTarget && child.Parent == null && nextParent.GetChildren().Length == 0,
            "reparenting rejects a target hidden by detach callbacks before committing a new graph edge");
        await child.HideAsync(); await nextParent.HideAsync(); await parent.HideAsync(); manager.ReleaseAllUnused();

        var high = await Show(); var low = await Show(); var locked = await Show();
        manager.SetCacheProperties(high, 10, false); manager.SetCacheProperties(low, -5, false); manager.SetCacheProperties(locked, 0, true);
        await high.HideAsync(); now = 1; await low.HideAsync(); await locked.HideAsync();
        var cached = manager.GetCachedInstances("units"); int pinnedId = cached.Single(c => c.Locked).Id;
        manager.SetCachePolicy("units", new EntityCachePolicy { maxCached = 2, expireSeconds = 10, releaseIntervalSeconds = 2, maxReleasePerPass = 1 });
        check(manager.GetCachedInstances("units").All(c => c.Priority != -5) && manager.GetGroup("units").Cached == 2,
            "capacity eviction chooses lowest priority idle instance and protects locked cache");
        now = 20; int released = manager.MaintainCaches();
        check(released == 1 && manager.GetCachedInstances("units").Single().Locked,
            "periodic expiry removes old unused instances within release budget and retains locked ones");
        check(manager.ReleaseAllUnused() == 0, "manual unused-cache release also respects locked entries");
        manager.SetCachedProperties("units", pinnedId, 0, false);
        check(manager.ReleaseAllUnused() == 1 && manager.InstanceCount == 0, "unlocking cached entry permits explicit lease release");

        manager.SetCachePolicy("units", new EntityCachePolicy { maxCached = 0, maxReleasePerPass = 10, expireSeconds = 0 });
        parent = await Show(); child = await Show(); manager.TrimCache("units", true);
        check(parent.IsShown && child.IsShown && manager.InstanceCount == 2, "cache trimming never evicts active entities");
        await parent.HideAsync(); await child.HideAsync();
        check(manager.InstanceCount == 0, "zero-cache policy releases instances immediately on recycle");

        var chain = new List<EntityHandle<Item>>();
        for (int i = 0; i < 1500; i++) { var entity = await Show(); if (i > 0) manager.Attach(entity, chain[i - 1]); chain.Add(entity); }
        await chain[0].HideAsync();
        check(manager.InstanceCount == 0 && owner.GetEntities().Length == 0 && chain.All(e => e.Parent == null),
            "deep attachment tree of 1500 entities drains without recursive hide or leaked graph entries");
        await scope.DisposeAsync(); await manager.StopAsync();
        check(provider.Created == provider.Released, "hierarchy and cache scenarios release every provider instance once");

        provider = new Provider(); manager = new EntityManager<Item>(provider, new QuietLog()); manager.AddGroup("units", 2);
        scope = new StateScope("cache-reentry", new QuietLog()); owner = manager.CreateOwner(scope, "cache-reentry");
        parent = await owner.ShowAsync(1, "unit", "units", () => new Logic { Destroy = () => manager.ReleaseAllUnused() });
        child = await owner.ShowAsync(2, "unit", "units", () => new Logic());
        await scope.DisposeAsync(); await manager.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        check(manager.InstanceCount == 0 && provider.Released == 2, "shutdown removes cache entries before reentrant OnDestroy release callbacks");

        var release = new ReleaseManifest { files = new[] { new ReleaseFile { name = "scene", kind = "bundle" } },
            assets = new[] { new ResourceAddress { address = "Arena", bundle = "scene", asset = "Assets/World/Arena.unity", kind = "scene" } } };
        check(new ResourceCatalog(release).Resolve("Arena").kind == "scene", "signed catalog distinguishes scene addresses from ordinary assets");
        release.assets[0].asset = "Assets/../outside.unity"; bool invalid = false;
        try { new ResourceCatalog(release); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "scene catalog rejects traversal in scene asset paths");
    });
}
