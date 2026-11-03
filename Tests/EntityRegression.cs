using Game.Core;

static class EntityRegression
{
    public static Task Run(Action<bool, string> check) => ProcedureRegression.SingleContext.Run(async () =>
    {
        var provider = new Provider(); var service = new EntityManager<Instance>(provider, new QuietLog());
        service.AddGroup("units", 3); service.AddGroup("effects", 1);
        var parent = new StateScope("procedure", new QuietLog()); var otherParent = new StateScope("other", new QuietLog());
        var owner = service.CreateOwner(parent, "owned"); var other = service.CreateOwner(otherParent, "other");
        var first = await owner.ShowAsync(1, "unit", "units", () => new Logic(), "first");
        var logic = (Logic)first.Logic; var instance = first.Instance;
        check(first.IsShown && service.TryGet(1, out var found) && ReferenceEquals(found, first), "entity ID lookup returns the visible activation");
        check(logic.Init == 1 && logic.Shows == 1 && logic.Data == "first", "entity logic initializes once and receives show data");
        var second = await other.ShowAsync(2, "unit", "units", () => new Logic());
        var secondLogic = (Logic)second.Logic;
        owner.Tick(0.1f);
        check(logic.Ticks == 1 && secondLogic.Ticks == 0, "entity updates are limited to the active procedure owner");
        bool duplicate = false;
        try { await owner.ShowAsync(1, "unit", "units", () => new Logic()); } catch (InvalidOperationException) { duplicate = true; }
        check(duplicate && service.ActiveCount == 2, "duplicate entity ID is rejected without replacing the live entity");
        var hide = first.HideAsync(); check(ReferenceEquals(hide, first.HideAsync()), "repeated entity hide shares one cleanup task"); await hide;
        check(logic.Hides == 1 && logic.Recycles == 1 && !instance.Visible && !service.TryGet(1, out _), "hide removes lookup and recycles logic and instance");
        var reused = await owner.ShowAsync(1, "unit", "units", () => new Logic(), "second");
        check(ReferenceEquals(reused.Instance, instance) && ReferenceEquals(reused.Logic, logic) && logic.Init == 1 && logic.Shows == 2 && logic.Data == "second",
            "pool reuses instance and logic but creates a new activation");
        await first.HideAsync();
        check(!first.Interact() && reused.IsShown, "stale handle cannot hide or interact with a reused ID");
        var effect = await owner.ShowAsync(3, "unit", "effects", () => new Logic());
        check(service.GetGroupEntities("units").Length == 2 && service.GetGroupEntities("effects").Length == 1 && effect.Instance != reused.Instance,
            "entity groups isolate pools and support grouped lookup");
        bool capacity = false;
        try { await owner.ShowAsync(4, "unit", "effects", () => new Logic()); } catch (InvalidOperationException) { capacity = true; }
        check(capacity, "entity group enforces its reservation limit");
        await parent.CloseAsync();
        check(service.ActiveCount == 1 && second.IsShown && service.GetGroup("effects").Reserved == 0, "procedure exit hides only the entities it owns");
        await otherParent.CloseAsync(); var stopping = service.StopAsync(); await stopping;
        check(ReferenceEquals(stopping, service.StopAsync()) && provider.Released == provider.Created && service.InstanceCount == 0,
            "service shutdown releases all pooled instance asset leases exactly once");

        provider = new Provider { Gate = Gate() }; service = new EntityManager<Instance>(provider, new QuietLog()); service.AddGroup("units", 1);
        parent = new StateScope("loading", new QuietLog()); owner = service.CreateOwner(parent, "loading");
        var showing = owner.ShowAsync(10, "unit", "units", () => new Logic());
        check(service.IsLoading(10) && service.LoadingCount == 1 && !service.TryGet(10, out _), "loading entity reserves its ID without exposing a visible entity");
        var closing = parent.CloseAsync();
        check(!closing.IsCompleted && provider.Token.IsCancellationRequested, "procedure exit cancels and waits for unfinished entity loading");
        provider.Gate.SetResult(true);
        try { await showing; } catch (OperationCanceledException) { }
        await closing;
        check(service.ActiveCount == 0 && service.LoadingCount == 0 && service.GetGroup("units").Reserved == 0 && provider.Released == 1,
            "late load completion cannot spawn into the next procedure and releases its lease");
        await service.StopAsync();

        var cancellationLog = new CaptureLog();
        provider = new Provider { Gate = Gate() }; service = new EntityManager<Instance>(provider, cancellationLog); service.AddGroup("units", 1);
        parent = new StateScope("cancel load", cancellationLog); owner = service.CreateOwner(parent, "cancel load");
        showing = owner.ShowAsync(11, "unit", "units", () => new Logic()); hide = service.HideAsync(11);
        check(provider.Token.IsCancellationRequested && !hide.IsCompleted, "hide during loading cancels the request and waits for provider completion");
        provider.Gate.SetResult(true); try { await showing; } catch (OperationCanceledException) { } await hide;
        check(provider.Released == 1 && service.GetGroup("units").Reserved == 0, "cancelled loading releases ID and group capacity");
        check(cancellationLog.Errors == 0, "explicitly cancelled entity creation is not logged as a background failure");
        await parent.CloseAsync(); await service.StopAsync();

        provider = new Provider(); service = new EntityManager<Instance>(provider, new QuietLog()); service.AddGroup("units", 3);
        parent = new StateScope("tasks", new QuietLog()); int viewReleased = 0;
        parent.Own(new Release(() => viewReleased++)); owner = service.CreateOwner(parent, "tasks");
        first = await owner.ShowAsync(20, "unit", "units", () => new Logic());
        logic = (Logic)first.Logic;
        var workGate = Gate(); var work = first.Scope.Run(_ => workGate.Task); closing = parent.CloseAsync();
        check(!closing.IsCompleted && viewReleased == 0 && logic.Recycles == 0, "procedure keeps view and entity alive until entity-owned tasks finish");
        workGate.SetResult(true); await work; await closing;
        check(viewReleased == 1 && logic.Recycles == 1, "asynchronous entity cleanup completes before procedure view disposal");
        await service.StopAsync();

        provider = new Provider(); service = new EntityManager<Instance>(provider, new QuietLog()); service.AddGroup("units", 3);
        parent = new StateScope("failures", new QuietLog()); owner = service.CreateOwner(parent, "failures");
        var broken = new Logic { FailShow = true }; bool showFailed = false;
        try { await owner.ShowAsync(30, "unit", "units", () => broken); } catch (InvalidOperationException) { showFailed = true; }
        check(showFailed && broken.Hides == 1 && broken.Recycles == 1 && provider.Released == 1 && service.InstanceCount == 0,
            "failed OnShow cleans partial entity and discards the poisoned instance");
        first = await owner.ShowAsync(31, "unit", "units", () => new Logic { FailHide = true });
        second = await owner.ShowAsync(32, "unit", "units", () => new Logic()); secondLogic = (Logic)second.Logic;
        bool closeFailed = false; try { await parent.CloseAsync(); } catch (AggregateException) { closeFailed = true; }
        check(closeFailed && secondLogic.Recycles == 1 && service.ActiveCount == 0, "one entity hide failure does not skip cleanup of its siblings");
        await service.StopAsync();

        provider = new Provider(); service = new EntityManager<Instance>(provider, new QuietLog()); service.AddGroup("units", 1);
        var procedure = new EntityProcedure(service); var next = new Next(); var procedures = new ProcedureManager(new QuietLog(), procedure, next);
        await procedures.ChangeAsync<EntityProcedure>(); first = procedure.Entity;
        bool selfGuard = false, ancestorGuard = false, requested = false;
        await first.Scope.Run(async _ =>
        {
            try { await first.HideAsync(); } catch (InvalidOperationException) { selfGuard = true; }
            try { await procedures.ChangeAsync<Next>(); } catch (InvalidOperationException) { ancestorGuard = true; }
            requested = procedures.RequestChange<Next>(procedure.Scope);
        });
        await procedures.Transition;
        check(selfGuard && ancestorGuard && requested && procedures.Current == next && service.ActiveCount == 0,
            "entity task cannot await its own or ancestor teardown but can request a procedure transition");
        await procedures.StopAsync(); await service.StopAsync();
    });
    static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    sealed class Instance { public bool Visible; }
    sealed class Provider : IEntityProvider<Instance>
    {
        public TaskCompletionSource<bool> Gate;
        public CancellationToken Token;
        public int Created, Released;
        public async Task<EntityInstance<Instance>> CreateAsync(string asset, CancellationToken cancellation)
        {
            Token = cancellation; if (Gate != null) await Gate.Task;
            Created++; return new EntityInstance<Instance>(new Instance(), () => Released++);
        }
        public void SetVisible(Instance instance, bool visible) => instance.Visible = visible;
    }
    sealed class Logic : EntityLogic<Instance>, IEntityInteraction
    {
        public int Init, Shows, Hides, Recycles, Ticks;
        public string Data;
        public bool FailShow, FailHide;
        public override void OnInit() => Init++;
        public override void OnShow(object data) { Shows++; Data = data as string; if (FailShow) throw new InvalidOperationException("Expected show failure"); }
        public override void OnUpdate(float deltaTime) => Ticks++;
        public override void OnHide() { Hides++; if (FailHide) throw new InvalidOperationException("Expected hide failure"); }
        public override void OnRecycle() { Recycles++; Data = null; }
        public void OnInteract() => Entity.RequestHide();
    }
    sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    sealed class CaptureLog : Game.Contracts.IGameLog
    {
        public int Errors;
        public void Write(Game.Contracts.GameLogLevel level, string module, string operation, string message, string correlationId = "", long durationMs = 0, Exception exception = null)
        { if (level >= Game.Contracts.GameLogLevel.Error) Errors++; }
    }
    sealed class EntityProcedure(EntityManager<Instance> service) : ProcedureBase
    {
        public StateScope Scope;
        public EntityHandle<Instance> Entity;
        public override async Task EnterAsync(ProcedureManager owner, StateScope scope)
        { Scope = scope; Entity = await service.CreateOwner(scope, "test").ShowAsync(99, "unit", "units", () => new Logic()); }
    }
    sealed class Next : ProcedureBase { }
}
