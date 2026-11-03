using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;

namespace Game.Core
{
    // The entry is the final lifecycle participant, so its tasks stop before services are released.
    public sealed class GameEntryModule : IModule
    {
        readonly IGameEntry entry;
        public string Id => "game.entry";
        public IReadOnlyList<string> Dependencies { get; }
        GameEntryModule(IGameEntry entry, string[] dependencies) { this.entry = entry; Dependencies = dependencies; }
        public static void Register(ModuleHost host, IGameEntry entry)
        {
            entry.RegisterModules(host);
            host.Add(new GameEntryModule(entry, host.States.Keys.ToArray()));
        }
        public Task InitializeAsync(CancellationToken cancellation) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellation) => entry.StartAsync(cancellation);
        public Task StopAsync(CancellationToken cancellation) => entry.StopAsync(cancellation);
        public void Dispose() => entry.Dispose();
    }
}
