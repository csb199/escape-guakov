using Game.HotUpdate.Barrack;

static class BarrackRegression
{
    public static void Run(Action<bool, string> check)
    {
        var lengths = BarrackInteraction.Clips.Keys.ToDictionary(k => k, _ => 2f);
        var state = new BarrackInteraction(lengths); int changes = 0;
        state.Changed += (_, _) => changes++;
        check(!state.Request(BarrackAction.Head), "barrack rejects touch before resources are ready");
        state.Start(); check(state.Mode == BarrackInteractionMode.Entrance, "barrack begins with native entrance");
        check(!state.BeginOrbit() && !state.Request(BarrackAction.Weapon), "entrance prevents overlapping camera and touch actions");
        state.Tick(1.9f); check(state.Mode == BarrackInteractionMode.Entrance, "entrance waits for the source clip duration");
        state.Tick(0.1f); check(state.Mode == BarrackInteractionMode.Idle, "entrance reaches idle");
        check(state.Request(BarrackAction.Head) && !state.Request(BarrackAction.Pocket), "touch actions do not overlap");
        state.Tick(2); check(state.Mode == BarrackInteractionMode.Returning && state.Action == BarrackAction.Return, "touch completes through source ToIdle transition");
        check(!state.BeginOrbit(), "return animation retains camera ownership");
        state.Tick(2); check(state.BeginOrbit(), "idle permits orbit");
        state.Tick(100); check(state.Mode == BarrackInteractionMode.Orbit, "manual orbit remains until explicitly returned");
        check(state.Return(), "back returns from orbit"); state.Tick(2);
        check(state.Request(BarrackAction.Look), "face sequence starts Look");
        state.Tick(2);
        check(!state.IsFocused&&state.Mode==BarrackInteractionMode.Returning&&!state.CanInteract,"Look completion releases the near camera and starts ToIdle automatically");
        state.Tick(2);check(state.Mode==BarrackInteractionMode.Idle,"Look returns to the default idle without another click");
        foreach(var action in lengths.Keys.Where(action=>action!=BarrackAction.Idle&&action!=BarrackAction.Entrance&&action!=BarrackAction.Return))
        {
            check(state.Request(action),"action starts from idle: "+action);state.Tick(2);
            check(state.Mode==BarrackInteractionMode.Returning&&!state.IsFocused,"action completion restores camera ownership: "+action);state.Tick(2);
            check(state.Mode==BarrackInteractionMode.Idle,"action automatically reaches idle: "+action);
        }
        int beforeClose = changes; state.Close(); state.Tick(100);
        check(!state.Request(BarrackAction.Head) && !state.Return() && !state.BeginOrbit() && changes == beforeClose, "closed scene rejects stale interactions and emits no callbacks");
        var partial = new Dictionary<BarrackAction,float>(lengths);
        partial.Remove(BarrackAction.Weapon); partial.Remove(BarrackAction.Pocket);
        var limited = new BarrackInteraction(partial); limited.Start(); limited.Tick(2);
        check(!limited.Supports(BarrackAction.Weapon) && !limited.Request(BarrackAction.Weapon) && limited.Request(BarrackAction.Head), "character-specific actions reject missing clips without substitution");
        partial.Remove(BarrackAction.Return);
        bool missingReturn = false; try { _ = new BarrackInteraction(partial); } catch (ArgumentException) { missingReturn = true; }
        check(missingReturn, "partial action sets still require ToIdle");
        lengths[BarrackAction.Idle] = 0;
        bool rejected = false; try { _ = new BarrackInteraction(lengths); } catch (ArgumentException) { rejected = true; }
        check(rejected, "barrack refuses missing native action durations");
    }
}
