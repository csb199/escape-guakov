using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Runtime;

namespace Game.HotUpdate
{
    // Registered only by the demo's --validate-shutdown scenario; tests the real player quit callback.
    public sealed class ShutdownProbe : IModule
    {
        readonly GameRuntime runtime;
        bool stopCompleted;
        public ShutdownProbe(GameRuntime runtime) { this.runtime = runtime; }
        public string Id => "demo.shutdown-probe";
        public IReadOnlyList<string> Dependencies { get; } = new[] { "demo.gameplay" };
        public Task InitializeAsync(CancellationToken cancellation) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellation) => Task.CompletedTask;
        public async Task StopAsync(CancellationToken cancellation)
        {
            await Task.Delay(250, cancellation);
            File.WriteAllText(Path.Combine(runtime.DataDirectory, "async-stop-completed.txt"), "PASS: awaited asynchronous shutdown before releasing dependencies");
            stopCompleted = true;
        }
        public void Dispose()
        {
            if (!stopCompleted) throw new System.InvalidOperationException("Probe disposed before asynchronous stop completed");
        }
    }
}
