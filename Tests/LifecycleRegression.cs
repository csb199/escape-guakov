using Game.Contracts;
using Game.Core;

static class LifecycleRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var module = new ControlledModule();
        var host = new ModuleHost(new QuietLog()); host.Add(module);
        var starting = host.StartAsync(default);
        var stopping = host.StopAsync(default);
        check(!stopping.IsCompleted && module.DisposeCalls == 0, "stop waits for pending initialization before releasing resources");
        module.Initialized.SetResult(true);
        try { await starting; } catch (OperationCanceledException) { }
        await stopping;
        check(module.StartCalls == 0 && module.DisposeCalls == 1 && host.States[module.Id] == ModuleState.Stopped,
            "cancelled initialization cannot restart a disposed module");
        check(ReferenceEquals(stopping, host.StopAsync(default)), "repeated stop reuses the completed shutdown task");

        module = new ControlledModule { WaitOnStop = true }; module.Initialized.SetResult(true);
        host = new ModuleHost(new QuietLog()); host.Add(module); await host.StartAsync(default);
        var first = host.StopAsync(default); var second = host.StopAsync(default);
        check(ReferenceEquals(first, second), "overlapping stop requests share one task");
        module.Stopped.SetResult(true); await Task.WhenAll(first, second);
        check(module.StopCalls == 1 && module.DisposeCalls == 1, "overlapping stop requests never duplicate teardown");

        module = new ControlledModule { WaitOnStart = true }; module.Initialized.SetResult(true);
        host = new ModuleHost(new QuietLog()); host.Add(module);
        starting = host.StartAsync(default); stopping = host.StopAsync(default);
        check(module.DisposeCalls == 0, "stop waits for pending module startup");
        module.Started.SetResult(true);
        try { await starting; } catch (OperationCanceledException) { }
        await stopping;
        check(module.DisposeCalls == 1 && host.States[module.Id] == ModuleState.Stopped, "stop during startup ends stopped");

        var history = new List<string>();
        module = new ControlledModule { WaitOnStop = true, Required = new[] { "resources" } }; module.Initialized.SetResult(true);
        host = new ModuleHost(new QuietLog()); host.Add(new TestModule("resources", Array.Empty<string>(), history)); host.Add(module);
        await host.StartAsync(default);
        bool timedOut = false; stopping = host.StopAsync(default);
        try { await stopping; } catch (TimeoutException) { timedOut = true; }
        check(timedOut && module.DisposeCalls == 0 && !history.Contains("resources:dispose"),
            "timed-out asynchronous stop keeps its module and dependencies alive");
        module.Stopped.SetResult(true);
        check(ReferenceEquals(stopping, host.StopAsync(default)) && module.DisposeCalls == 0,
            "late stop completion cannot trigger a second teardown after timeout");

        history.Clear(); module = new ControlledModule { FailOnStop = true, Required = new[] { "resources" } }; module.Initialized.SetResult(true);
        host = new ModuleHost(new QuietLog()); host.Add(new TestModule("resources", Array.Empty<string>(), history)); host.Add(module);
        await host.StartAsync(default); bool stopFailed = false;
        try { await host.StopAsync(default); } catch (AggregateException) { stopFailed = true; }
        check(stopFailed && module.DisposeCalls == 1 && history.Contains("resources:dispose"),
            "completed stop failure is reported while remaining dependency cleanup continues");
    }
    sealed class ControlledModule : IModule
    {
        public readonly TaskCompletionSource<bool> Initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WaitOnStart, WaitOnStop, FailOnStop;
        public string[] Required = Array.Empty<string>();
        public int StartCalls, StopCalls, DisposeCalls;
        public string Id => "controlled";
        public IReadOnlyList<string> Dependencies => Required;
        public Task InitializeAsync(CancellationToken cancellation) => Initialized.Task;
        public Task StartAsync(CancellationToken cancellation) { StartCalls++; return WaitOnStart ? Started.Task : Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellation)
        {
            StopCalls++;
            if (FailOnStop) throw new InvalidOperationException("Expected stop fault");
            return WaitOnStop ? Stopped.Task : Task.CompletedTask;
        }
        public void Dispose() => DisposeCalls++;
    }
}
