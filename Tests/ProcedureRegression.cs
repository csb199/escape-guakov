using System.Collections.Concurrent;
using Game.Core;

static class ProcedureRegression
{
    public static Task Run(Action<bool, string> check) => SingleContext.Run(async () =>
    {
        var a = new A(); var b = new B();
        var manager = new ProcedureManager(new QuietLog(), a, b);
        await manager.ChangeAsync<A>(); manager.Tick(0.1f);
        check(a.Ticks == 1 && b.Ticks == 0, "FSM ticks only the active procedure");
        await manager.ChangeAsync<A>();
        check(a.Enters == 1, "repeated request for the active state does not re-enter it");
        bool missing = false;
        try { await manager.ChangeAsync<C>(); } catch (InvalidOperationException) { missing = true; }
        check(missing && a.Scope.IsActive, "unregistered target is rejected before cancelling current state");

        var bus = new EventBus(); int messages = 0, releases = 0;
        a.Scope.Subscribe<int>(bus, _ => messages++);
        a.Scope.Own(new Release(() => releases++));
        var oldScope = a.Scope; var taskGate = Gate(); var exitGate = Gate();
        a.Leave = () => exitGate.Task;
        bool lateWrite = false;
        var owned = oldScope.Run(async token => { await taskGate.Task; if (!token.IsCancellationRequested) lateWrite = true; });
        bus.Publish(1);
        var changing = manager.ChangeAsync<B>(); manager.Tick(0.1f); bus.Publish(2);
        check(oldScope.Token.IsCancellationRequested && !changing.IsCompleted && b.Enters == 0 && releases == 0,
            "switch cancels old work but retains resources until the work finishes");
        check(messages == 1 && a.Ticks == 1, "exiting state receives neither event callbacks nor ticks");
        bool overlap = false;
        try { await manager.ChangeAsync<B>(); } catch (InvalidOperationException) { overlap = true; }
        check(overlap, "overlapping external transitions are rejected");
        taskGate.SetResult(true); await owned;
        while (a.Exits == 0) await Task.Yield();
        check(releases == 1 && bus.SubscriptionCount == 0 && b.Enters == 0,
            "subscriptions and resources release before asynchronous exit completes");
        exitGate.SetResult(true); await changing;
        check(!lateWrite && b.Enters == 1, "next state enters only after old work and exit have completed");
        check(!manager.RequestChange<A>(oldScope) && manager.Current == b, "stale activation cannot switch the current procedure");
        manager.Tick(0.1f);
        check(a.Ticks == 1 && b.Ticks == 1, "previous procedure is not ticked after switching");
        await manager.ChangeAsync<A>();
        check(a.Scope != oldScope && a.Scope.IsActive, "re-entering a procedure creates a fresh activation scope");
        var stop1 = manager.StopAsync(); var stop2 = manager.StopAsync(); await stop1;
        check(ReferenceEquals(stop1, stop2) && a.Exits == 2 && manager.Current == null, "repeated FSM stop shares cleanup and leaves no active procedure");

        a = new A(); b = new B(); manager = new ProcedureManager(new QuietLog(), a, b);
        var enterGate = Gate(); a.Enter = scope => enterGate.Task;
        var entering = manager.ChangeAsync<A>();
        while (a.Scope == null) await Task.Yield();
        manager.Tick(0.1f); var stopping = manager.StopAsync();
        check(a.Ticks == 0 && !stopping.IsCompleted && a.Scope.Token.IsCancellationRequested, "stop during enter waits and never ticks a partial state");
        enterGate.SetResult(true);
        try { await entering; } catch (OperationCanceledException) { }
        await stopping;
        check(a.Exits == 1 && manager.Current == null && b.Enters == 0, "cancelled enter cleans partial state without entering another");

        a = new A(); b = new B(); manager = new ProcedureManager(new QuietLog(), a, b);
        await manager.ChangeAsync<A>();
        bool guard = false, requested = false;
        var requestingManager = manager; var requestScope = a.Scope;
        await requestScope.Run(async _ =>
        {
            await Task.Yield();
            try { await requestingManager.ChangeAsync<B>(); } catch (InvalidOperationException) { guard = true; }
            requested = requestingManager.RequestChange<B>(requestScope);
        });
        await manager.Transition;
        check(guard && requested && manager.Current == b, "state task requests transition without waiting for its own teardown");
        await manager.StopAsync();

        a = new A(); b = new B(); int partialReleases = 0;
        b.Enter = scope => { scope.Own(new Release(() => partialReleases++)); throw new InvalidOperationException("Expected enter failure"); };
        manager = new ProcedureManager(new QuietLog(), a, b); await manager.ChangeAsync<A>();
        bool enterFailed = false;
        try { await manager.ChangeAsync<B>(); } catch (InvalidOperationException) { enterFailed = true; }
        check(enterFailed && partialReleases == 1 && b.Exits == 1 && manager.Current == null, "failed enter cleans its partial activation and halts the FSM");
        await manager.StopAsync();

        a = new A(); b = new B(); manager = new ProcedureManager(new QuietLog(), a, b); await manager.ChangeAsync<A>();
        int remainingCleanup = 0;
        a.Scope.Own(new Release(() => remainingCleanup++));
        a.Scope.Own(new Release(() => throw new InvalidOperationException("Expected release failure")));
        bool cleanupFailed = false;
        try { await manager.ChangeAsync<B>(); } catch (AggregateException) { cleanupFailed = true; }
        check(cleanupFailed && remainingCleanup == 1 && a.Exits == 1 && b.Enters == 0, "cleanup failure still releases other resources and blocks the next state");
        bool stopFailed = false;
        try { await manager.StopAsync(); } catch (AggregateException) { stopFailed = true; }
        check(stopFailed, "shutdown preserves an earlier procedure cleanup failure");

        a = new A(); manager = new ProcedureManager(new QuietLog(), a); await manager.ChangeAsync<A>();
        int stopReleases = 0; a.Scope.Own(new Release(() => stopReleases++));
        var runningGate = Gate(); var running = a.Scope.Run(_ => runningGate.Task);
        stopping = manager.StopAsync();
        check(a.Scope.Token.IsCancellationRequested && !stopping.IsCompleted && stopReleases == 0,
            "FSM shutdown retains resources until active procedure work finishes");
        runningGate.SetResult(true); await running; await stopping;
        check(stopReleases == 1 && a.Exits == 1, "FSM shutdown drains work and releases the activation once");

        a = new A(); b = new B(); manager = new ProcedureManager(new QuietLog(), a, b); await manager.ChangeAsync<A>();
        var failedGate = Gate(); var failedWork = a.Scope.Run(_ => failedGate.Task);
        changing = manager.ChangeAsync<B>(); failedGate.SetException(new IOException("Expected background fault"));
        try { await failedWork; } catch (IOException) { }
        await changing;
        check(a.Exits == 1 && manager.Current == b, "failed background task is observed and cannot skip exit cleanup");
        await manager.StopAsync();
    });

    static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    class Probe : ProcedureBase
    {
        public StateScope Scope;
        public int Enters, Ticks, Exits;
        public Func<StateScope, Task> Enter = _ => Task.CompletedTask;
        public Func<Task> Leave = () => Task.CompletedTask;
        public override Task EnterAsync(ProcedureManager owner, StateScope scope) { Enters++; Scope = scope; return Enter(scope); }
        public override void Update(ProcedureManager owner, float deltaTime) => Ticks++;
        public override Task ExitAsync(ProcedureManager owner) { Exits++; return Leave(); }
    }
    sealed class A : Probe { }
    sealed class B : Probe { }
    sealed class C : Probe { }
    sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }

    // Emulate Unity's single owning thread; production FSM APIs do not promise cross-thread mutation.
    internal sealed class SingleContext : SynchronizationContext
    {
        readonly BlockingCollection<Action> work = new();
        public override void Post(SendOrPostCallback callback, object state) => work.Add(() => callback(state));
        public static Task Run(Func<Task> action)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                var context = new SingleContext(); SetSynchronizationContext(context);
                context.Post(async _ =>
                {
                    try { await action(); completion.TrySetResult(true); }
                    catch (Exception e) { completion.TrySetException(e); }
                    finally { context.work.CompleteAdding(); }
                }, null);
                foreach (var callback in context.work.GetConsumingEnumerable()) callback();
            }) { IsBackground = true };
            thread.Start(); return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
}
