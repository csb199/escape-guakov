using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using Game.Runtime;
namespace Game.HotUpdate.Barrack
{
    // Register from IGameEntry.RegisterModules before constructing BarrackProcedure.
    public sealed class BarrackModule : IModule
    {
        readonly GameRuntime game;
        public BarrackModule(GameRuntime game) { this.game = game; }
        public string Id => "barrack.presentation";
        public IReadOnlyList<string> Dependencies { get; } = new[] { "entities", "scenes" };
        public Task InitializeAsync(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            game.Entities.AddGroup("Barrack", 1, new EntityCachePolicy { maxCached = 0 });
            return Task.CompletedTask;
        }
        public Task StartAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellation) => Task.CompletedTask;
        public void Dispose() { }
    }
}
