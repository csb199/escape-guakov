using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;

namespace Game.Core
{
    public abstract class FsmState<TOwner> where TOwner : class
    {
        public virtual Task EnterAsync(TOwner owner, StateScope scope) => Task.CompletedTask;
        public virtual void Update(TOwner owner, float deltaTime) { }
        public virtual Task ExitAsync(TOwner owner) => Task.CompletedTask;
    }

    // Async transitions are serialized on the owning context; this is independent of ModuleHost.
    public sealed class Fsm<TOwner> where TOwner : class
    {
        readonly TOwner owner;
        readonly IGameLog log;
        readonly Dictionary<Type, FsmState<TOwner>> states = new Dictionary<Type, FsmState<TOwner>>();
        StateScope scope;
        Task stopTask;
        Exception cleanupFailure;
        bool stopped, faulted;
        public FsmState<TOwner> Current { get; private set; }
        public bool IsTransitioning { get; private set; }
        public Task Transition { get; private set; } = Task.CompletedTask;
        public Fsm(TOwner owner, IGameLog log, params FsmState<TOwner>[] states)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner)); this.log = log;
            foreach (var state in states) this.states.Add(state.GetType(), state);
        }
        // External orchestration only. State-owned tasks use RequestChange to avoid awaiting their own exit.
        public Task ChangeAsync<TState>(CancellationToken cancellation = default) where TState : FsmState<TOwner>
        {
            if (StateScope.ExecutingWithin(this)) throw new InvalidOperationException("State tasks must use RequestChange, not await their own exit.");
            return BeginChange<TState>(cancellation);
        }
        Task BeginChange<TState>(CancellationToken cancellation = default) where TState : FsmState<TOwner>
        {
            if (stopped || faulted) throw new InvalidOperationException("FSM has stopped or failed.");
            if (!states.TryGetValue(typeof(TState), out var next)) throw new InvalidOperationException("Unregistered state: " + typeof(TState).Name);
            if (IsTransitioning) throw new InvalidOperationException("A state transition is already in progress.");
            cancellation.ThrowIfCancellationRequested();
            if (ReferenceEquals(Current, next) && scope.IsActive) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Transition = completion.Task; IsTransitioning = true; scope?.Cancel();
            StateScope.Drive(() => ChangeCoreAsync(next, cancellation, completion)); return Transition;
        }
        public bool RequestChange<TState>(StateScope source) where TState : FsmState<TOwner>
        {
            if (stopped || faulted || IsTransitioning || source == null || source != scope || !source.IsActive) return false;
            _ = ObserveTransition(BeginChange<TState>()); return true;
        }
        static async Task ObserveTransition(Task task) { try { await task; } catch { /* ChangeCoreAsync reports the failure. */ } }
        async Task ChangeCoreAsync(FsmState<TOwner> next, CancellationToken cancellation, TaskCompletionSource<bool> completion)
        {
            // A request made inside a scope task must return before that scope is drained.
            await Task.Yield();
            try
            {
                await LeaveCurrentAsync();
                if (stopped) throw new OperationCanceledException();
                cancellation.ThrowIfCancellationRequested();
                scope = new StateScope(next.GetType().Name, log, cancellation) { Owner = this }; Current = next;
                log.Write(GameLogLevel.Information, "procedure", "state.entering", next.GetType().Name);
                await next.EnterAsync(owner, scope);
                scope.Token.ThrowIfCancellationRequested();
                log.Write(GameLogLevel.Information, "procedure", "state.entered", next.GetType().Name);
                IsTransitioning = false; completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                try { await LeaveCurrentAsync(); } catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
                faulted = true; IsTransitioning = false;
                if (error is OperationCanceledException) completion.TrySetCanceled();
                else
                {
                    log.Write(GameLogLevel.Error, "procedure", "state.transition.failed", "FSM halted before entering another state", exception: error);
                    completion.TrySetException(error);
                }
            }
        }
        async Task LeaveCurrentAsync()
        {
            if (Current == null) return;
            var previous = Current; var previousScope = scope; Current = null; scope = null;
            log.Write(GameLogLevel.Information, "procedure", "state.leaving", previous.GetType().Name + " / pending=" + previousScope.PendingCount);
            var errors = new List<Exception>();
            try { await previousScope.CloseAsync(); } catch (Exception e) { errors.Add(e); }
            try { await previous.ExitAsync(owner); } catch (Exception e) { errors.Add(e); }
            if (errors.Count > 0) { cleanupFailure = new AggregateException("State exit failed", errors); throw cleanupFailure; }
            log.Write(GameLogLevel.Information, "procedure", "state.exited", previous.GetType().Name);
        }
        public void Tick(float deltaTime)
        {
            if (!stopped && !faulted && !IsTransitioning && scope != null && scope.IsActive) Current.Update(owner, deltaTime);
        }
        public Task StopAsync()
        {
            if (StateScope.ExecutingWithin(this)) throw new InvalidOperationException("A state task cannot await its own FSM shutdown.");
            if (stopTask != null) return stopTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            stopTask = completion.Task; stopped = true; scope?.Cancel();
            StateScope.Drive(() => StopCoreAsync(completion)); return stopTask;
        }
        async Task StopCoreAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                try { await Transition; } catch { /* Transition owns partial-enter cleanup and reporting. */ }
                await LeaveCurrentAsync();
                if (cleanupFailure != null) throw cleanupFailure;
                completion.TrySetResult(true);
            }
            catch (Exception e) { completion.TrySetException(e); }
        }
    }
}
