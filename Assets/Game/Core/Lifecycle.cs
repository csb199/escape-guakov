using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;

namespace Game.Core
{
    public enum ModuleState { Registered, Initializing, Ready, Running, Stopping, Stopped, Failed }

    // Invoke lifecycle methods on the owning context (Unity main thread for Unity modules).
    // Async overlap is supported; background callers must marshal to that context.
    public sealed class ModuleHost : IModuleRegistry
    {
        readonly Dictionary<string, IModule> modules = new Dictionary<string, IModule>();
        readonly List<IModule> entered = new List<IModule>();
        readonly Dictionary<string, ModuleState> states = new Dictionary<string, ModuleState>();
        readonly IGameLog log;
        CancellationTokenSource startupCancellation;
        Task startTask, stopTask, cleanupTask;
        bool used;
        public IReadOnlyDictionary<string, ModuleState> States { get; }
        public ModuleHost(IGameLog log)
        {
            this.log = log;
            States = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModuleState>(states);
        }
        public void Add(IModule module)
        {
            if (used || stopTask != null) throw new InvalidOperationException("Modules are frozen after startup or shutdown.");
            if (module == null || string.IsNullOrWhiteSpace(module.Id)) throw new ArgumentException("Module requires an ID.");
            modules.Add(module.Id, module); states.Add(module.Id, ModuleState.Registered);
        }
        public Task StartAsync(CancellationToken cancellation)
        {
            if (used || stopTask != null) throw new InvalidOperationException("Host starts once, before shutdown.");
            used = true;
            startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var completion = NewCompletion(); startTask = completion.Task;
            _ = StartCoreAsync(startupCancellation.Token, completion);
            return startTask;
        }
        async Task StartCoreAsync(CancellationToken cancellation, TaskCompletionSource<bool> completion)
        {
            IModule current = null;
            try
            {
                var order = new List<IModule>();
                var visiting = new HashSet<string>(); var visited = new HashSet<string>();
                void Visit(string id)
                {
                    if (visited.Contains(id)) return;
                    if (!modules.TryGetValue(id, out var module)) throw new InvalidOperationException("Missing dependency: " + id);
                    if (!visiting.Add(id)) throw new InvalidOperationException("Circular dependency: " + id);
                    foreach (var dependency in module.Dependencies) Visit(dependency);
                    visiting.Remove(id); visited.Add(id); order.Add(module);
                }
                foreach (var id in modules.Keys) Visit(id);
                foreach (var module in order)
                {
                    cancellation.ThrowIfCancellationRequested();
                    current = module; entered.Add(module); states[module.Id] = ModuleState.Initializing;
                    await module.InitializeAsync(cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    states[module.Id] = ModuleState.Ready;
                    await module.StartAsync(cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    states[module.Id] = ModuleState.Running;
                    log.Write(GameLogLevel.Information, module.Id, "module.started", "Module ready");
                    current = null;
                }
                completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                if (current != null && !(error is OperationCanceledException)) states[current.Id] = ModuleState.Failed;
                try { await CleanupAsync(); }
                catch (Exception cleanupError) { completion.TrySetException(new AggregateException(error, cleanupError)); return; }
                if (error is OperationCanceledException) completion.TrySetCanceled();
                else completion.TrySetException(error);
            }
            finally { startupCancellation.Dispose(); startupCancellation = null; }
        }
        public Task StopAsync(CancellationToken cancellation)
        {
            if (stopTask != null) return stopTask;
            var completion = NewCompletion(); stopTask = completion.Task;
            try { startupCancellation?.Cancel(); }
            catch (Exception e) { log.Write(GameLogLevel.Error, "host", "module.cancel", "Cancellation callback failed", exception: e); }
            _ = StopCoreAsync(completion);
            // Shutdown ownership is not cancelled by an individual caller; every caller awaits the same result.
            return stopTask;
        }
        async Task StopCoreAsync(TaskCompletionSource<bool> completion)
        {
            if (startTask != null) { try { await startTask; } catch { /* Startup already reports its failure. */ } }
            try { await CleanupAsync(); completion.TrySetResult(true); }
            catch (Exception e) { completion.TrySetException(e); }
        }
        Task CleanupAsync()
        {
            if (cleanupTask != null) return cleanupTask;
            var completion = NewCompletion(); cleanupTask = completion.Task;
            _ = CleanupCoreAsync(completion); return cleanupTask;
        }
        async Task CleanupCoreAsync(TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            try
            {
                foreach (var module in entered.AsEnumerable().Reverse())
                {
                    bool failed = states[module.Id] == ModuleState.Failed;
                    if (!failed) states[module.Id] = ModuleState.Stopping;
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        Task stopping = null;
                        try
                        {
                            stopping = module.StopAsync(timeout.Token);
                            if (await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(5))) != stopping)
                            {
                                states[module.Id] = ModuleState.Failed;
                                _ = ObserveLateStop(stopping);
                                // Dependencies must remain alive while the timed-out module might still access them.
                                throw new TimeoutException("Module is still stopping; resources retained: " + module.Id);
                            }
                            await stopping;
                        }
                        catch (Exception e)
                        {
                            failed = true; errors.Add(e);
                            log.Write(GameLogLevel.Error, module.Id, "module.stop", "Stop failed", exception: e);
                            if (stopping != null && !stopping.IsCompleted) throw;
                        }
                    }
                    try { module.Dispose(); }
                    catch (Exception e) { failed = true; errors.Add(e); log.Write(GameLogLevel.Error, module.Id, "module.dispose", "Dispose failed", exception: e); }
                    states[module.Id] = failed ? ModuleState.Failed : ModuleState.Stopped;
                }
                entered.Clear();
                if (errors.Count > 0) throw new AggregateException("Module shutdown failed", errors);
                completion.TrySetResult(true);
            }
            catch (Exception e) { completion.TrySetException(e); }
        }
        static TaskCompletionSource<bool> NewCompletion() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        static async Task ObserveLateStop(Task task) { try { await task; } catch { /* Original timeout is logged by the host. */ } }
    }

    // Unity-facing publishers and subscribers run on the Unity main thread.
    public sealed class EventBus
    {
        readonly Dictionary<Type, List<Delegate>> handlers = new Dictionary<Type, List<Delegate>>();
        public int SubscriptionCount => handlers.Values.Sum(x => x.Count);
        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (!handlers.TryGetValue(typeof(T), out var list)) handlers.Add(typeof(T), list = new List<Delegate>());
            list.Add(handler);
            return new Subscription(() => { list.Remove(handler); if (list.Count == 0) handlers.Remove(typeof(T)); });
        }
        public void Publish<T>(T message)
        {
            if (!handlers.TryGetValue(typeof(T), out var list)) return;
            List<Exception> failures = null;
            foreach (var handler in list.ToArray())
                try { ((Action<T>)handler)(message); }
                catch (Exception e) { (failures ?? (failures = new List<Exception>())).Add(e); }
            if (failures != null) throw new AggregateException("Event subscriber failure", failures);
        }
        sealed class Subscription : IDisposable
        {
            Action release;
            public Subscription(Action release) { this.release = release; }
            public void Dispose() { var action = release; release = null; action?.Invoke(); }
        }
    }
}
