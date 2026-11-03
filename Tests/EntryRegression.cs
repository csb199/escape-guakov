using Game.Contracts;
using Game.Core;
using Game.HotUpdate;

sealed class InventoryData { public int coins; }
sealed class AlternateScoreTransport : IScoreTransport
{
    public Task<int> SubmitAsync(string requestId, int score, CancellationToken cancellation) => Task.FromResult(321);
}
static class EntryRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var history = new List<string>(); var host = new ModuleHost(new QuietLog());
        host.Add(new TestModule("resources", Array.Empty<string>(), history));
        var entry = new Entry(history);
        GameEntryModule.Register(host, entry);
        check(history.Count == 0 && host.States.ContainsKey("hot.inventory"), "hot entry registers modules before any module initialization");
        await host.StartAsync(default);
        check(history.IndexOf("hot.inventory:start") < history.IndexOf("entry:start"), "hot modules start after dependencies and before game entry");
        var stop = host.StopAsync(default);
        check(!stop.IsCompleted && !history.Contains("resources:dispose") && !history.Contains("hot.inventory:dispose"),
            "entry async shutdown keeps all service dependencies alive");
        entry.StopFinished.SetResult(true); await stop;
        check(history.IndexOf("entry:dispose") < history.IndexOf("hot.inventory:dispose") && history.IndexOf("hot.inventory:dispose") < history.IndexOf("resources:dispose"),
            "entry and hot modules release in reverse dependency order");
        check(entry.StopCalls == 1 && entry.DisposeCalls == 1, "entry lifecycle is owned exactly once by the module host");

        history.Clear(); host = new ModuleHost(new QuietLog()); host.Add(new TestModule("resources", Array.Empty<string>(), history));
        entry = new Entry(history) { FailOnStart = true }; entry.StopFinished.SetResult(true); GameEntryModule.Register(host, entry);
        bool failed = false;
        try { await host.StartAsync(default); } catch (InvalidOperationException) { failed = true; }
        check(failed && entry.DisposeCalls == 1 && history.Contains("resources:dispose") && history.Contains("hot.inventory:dispose"),
            "entry startup failure cleans hot and base modules without a self-test branch");
        await host.StopAsync(default);
        check(entry.StopCalls == 1 && entry.DisposeCalls == 1, "bootstrap cleanup after startup failure cannot dispose entry twice");
    }
    sealed class Entry : IGameEntry
    {
        readonly List<string> history;
        public readonly TaskCompletionSource<bool> StopFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCalls, DisposeCalls;
        public bool FailOnStart;
        public Entry(List<string> history) { this.history = history; }
        public void RegisterModules(IModuleRegistry modules) => modules.Add(new TestModule("hot.inventory", new[] { "resources" }, history));
        public Task StartAsync(CancellationToken cancellation)
        {
            history.Add("entry:start");
            if (FailOnStart) throw new InvalidOperationException("Expected entry startup fault");
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellation) { StopCalls++; history.Add("entry:stop"); return StopFinished.Task; }
        public void Dispose() { DisposeCalls++; history.Add("entry:dispose"); }
    }
}
