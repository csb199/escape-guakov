using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;

namespace Game.Core
{
    public abstract class ProcedureBase : FsmState<ProcedureManager> { }

    // One procedure FSM, driven explicitly by its Unity adapter rather than the module host.
    public sealed class ProcedureManager
    {
        readonly Fsm<ProcedureManager> fsm;
        public ProcedureBase Current => (ProcedureBase)fsm.Current;
        public bool IsTransitioning => fsm.IsTransitioning;
        public Task Transition => fsm.Transition;
        public ProcedureManager(IGameLog log, params ProcedureBase[] procedures) { fsm = new Fsm<ProcedureManager>(this, log, procedures); }
        public Task ChangeAsync<T>(CancellationToken cancellation = default) where T : ProcedureBase => fsm.ChangeAsync<T>(cancellation);
        public bool RequestChange<T>(StateScope source) where T : ProcedureBase => fsm.RequestChange<T>(source);
        public void Tick(float deltaTime) => fsm.Tick(deltaTime);
        public Task StopAsync() => fsm.StopAsync();
    }
}
