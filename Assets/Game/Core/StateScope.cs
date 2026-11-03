using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;

namespace Game.Core
{
    // One activation owns one scope. All calls use the FSM's owning context (Unity main thread).
    public sealed class StateScope
    {
        static readonly AsyncLocal<StateScope> executing = new AsyncLocal<StateScope>();
        internal static StateScope Executing => executing.Value;
        internal object Owner { get; set; }
        internal StateScope Parent { get; set; }
        internal static bool ExecutingWithin(object owner)
        {
            for (var scope = Executing; scope != null; scope = scope.Parent) if (ReferenceEquals(scope.Owner, owner)) return true;
            return false;
        }
        public static void Drive(Func<Task> action)
        {
            var previous = executing.Value;
            try { executing.Value = null; _ = action(); }
            finally { executing.Value = previous; }
        }
        readonly CancellationTokenSource cancellation;
        readonly HashSet<Task> tasks = new HashSet<Task>();
        readonly List<Func<Task>> resources = new List<Func<Task>>();
        readonly List<Exception> cleanupErrors = new List<Exception>();
        readonly IGameLog log;
        readonly string name;
        Task closeTask;
        bool closing;
        public CancellationToken Token { get; }
        public bool IsActive => !closing && !Token.IsCancellationRequested;
        public int PendingCount => tasks.Count;
        public StateScope(string name, IGameLog log, CancellationToken parent = default)
        {
            this.name = name; this.log = log;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent); Token = cancellation.Token;
        }
        public T Own<T>(T resource) where T : IDisposable
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (!IsActive) { resource.Dispose(); throw new InvalidOperationException("State scope is closing: " + name); }
            resources.Add(() => { resource.Dispose(); return Task.CompletedTask; }); return resource;
        }
        // Register before acquiring asynchronous resources; cleanup runs in reverse ownership order.
        public IDisposable OwnAsync(Func<Task> cleanup)
        {
            if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
            if (!IsActive) throw new InvalidOperationException("State scope is closing: " + name);
            resources.Add(cleanup);
            return new Registration(() => resources.Remove(cleanup));
        }
        public IDisposable Subscribe<T>(EventBus events, Action<T> callback) =>
            Own(events.Subscribe<T>(message => { if (IsActive) callback(message); }));
        public Task Run(Func<CancellationToken, Task> action)
        {
            if (!IsActive) throw new InvalidOperationException("Cannot start work in an inactive state: " + name);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            tasks.Add(completion.Task);
            _ = RunCoreAsync(action, completion);
            return completion.Task;
        }
        async Task RunCoreAsync(Func<CancellationToken, Task> action, TaskCompletionSource<bool> completion)
        {
            var previous = executing.Value; executing.Value = this;
            try { await action(Token); completion.TrySetResult(true); }
            catch (OperationCanceledException) when (Token.IsCancellationRequested) { completion.TrySetCanceled(); }
            catch (Exception e)
            {
                log.Write(GameLogLevel.Error, name, "state.task.failed", "State operation failed", exception: e);
                completion.TrySetException(e);
            }
            finally
            {
                tasks.Remove(completion.Task);
                // Scope owns failure reporting even when a UI callback does not await the returned task.
                if (completion.Task.IsFaulted) _ = completion.Task.Exception;
                executing.Value = previous;
            }
        }
        public StateScope CreateChild(string childName)
        {
            if (!IsActive) throw new InvalidOperationException("State scope is closing: " + name);
            return new StateScope(childName, log, Token) { Parent = this };
        }
        public Task DisposeAsync()
        {
            EnsureCanDispose();
            return CloseAsync();
        }
        public void EnsureCanDispose()
        {
            for (var current = Executing; current != null; current = current.Parent)
                if (ReferenceEquals(current, this)) throw new InvalidOperationException("A scope task cannot await its own disposal");
        }
        public void Cancel()
        {
            if (closing) return;
            closing = true;
            try { cancellation.Cancel(); } catch (Exception e) { cleanupErrors.Add(e); }
        }
        internal Task CloseAsync()
        {
            if (closeTask != null) return closeTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            closeTask = completion.Task; Cancel(); _ = CloseCoreAsync(completion); return closeTask;
        }
        async Task CloseCoreAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                // Stop accepting work before taking the snapshot. Even non-cooperative work must finish.
                try { await Task.WhenAll(tasks.ToArray()); } catch { /* RunCoreAsync has observed and reported failures. */ }
                var owned = resources.ToArray(); resources.Clear();
                for (int i = owned.Length - 1; i >= 0; i--)
                    try { await owned[i](); } catch (Exception e) { cleanupErrors.Add(e); }
                cancellation.Dispose();
                if (cleanupErrors.Count > 0) throw new AggregateException("State cleanup failed: " + name, cleanupErrors);
                completion.TrySetResult(true);
            }
            catch (Exception e) { completion.TrySetException(e); }
        }
        sealed class Registration : IDisposable
        {
            Action remove;
            public Registration(Action remove) { this.remove = remove; }
            public void Dispose() { var action = remove; remove = null; action?.Invoke(); }
        }
    }
}
