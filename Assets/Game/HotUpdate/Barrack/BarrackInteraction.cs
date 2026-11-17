using System;
using System.Collections.Generic;

namespace Game.HotUpdate.Barrack
{
    public enum BarrackAction { Idle, Entrance, Return, Stretch, Inspect, InspectWeapon, Head, Weapon, Pocket, Around, Look }
    public enum BarrackInteractionMode { Loading, Entrance, Idle, Action, Orbit, Returning, Closed }
    // The host supplies verified native clip durations. No Unity or framework lifetime lives in this state machine.
    public sealed class BarrackInteraction
    {
        public static readonly IReadOnlyDictionary<BarrackAction, string> Clips = new Dictionary<BarrackAction, string>
        {
            [BarrackAction.Idle] = "c_GrozaSR01_BarrackIdle",
            [BarrackAction.Entrance] = "c_GrozaSR01_BarrackEntrance_Normal",
            [BarrackAction.Return] = "c_GrozaSR01_ToIdle",
            [BarrackAction.Stretch] = "c_GrozaSR01_Barracksp_Behave_Stretch",
            [BarrackAction.Inspect] = "c_GrozaSR01_Barracksp_Behave_InspectNormal",
            [BarrackAction.InspectWeapon] = "c_GrozaSR01_Barracksp_Behave_InspectExclusive",
            [BarrackAction.Head] = "c_GrozaSR01_Barracksp_Touch_Head",
            [BarrackAction.Weapon] = "c_GrozaSR01_Barracksp_Touch_Gun",
            [BarrackAction.Pocket] = "c_GrozaSR01_Barracksp_Touch_Pocket",
            [BarrackAction.Around] = "c_GrozaSR01_Barracksp_Camera_Aroud",
            [BarrackAction.Look] = "c_GrozaSR01_Barracksp_Camera_Look"
        };
        readonly IReadOnlyDictionary<BarrackAction, float> durations;
        float elapsed;
        public BarrackInteractionMode Mode { get; private set; } = BarrackInteractionMode.Loading;
        public BarrackAction Action { get; private set; } = BarrackAction.Idle;
        public float Progress => Math.Min(1, elapsed / durations[Action]);
        public bool IsFocused { get; private set; }
        public bool CanInteract => Mode == BarrackInteractionMode.Idle || Mode == BarrackInteractionMode.Orbit;
        public event Action<BarrackAction, BarrackInteractionMode> Changed;
        public BarrackInteraction(IReadOnlyDictionary<BarrackAction, float> durations)
        {
            foreach (var required in new[] { BarrackAction.Idle, BarrackAction.Entrance, BarrackAction.Return })
                if (!durations.ContainsKey(required)) throw new ArgumentException("Missing required native clip: " + required, nameof(durations));
            foreach (var pair in durations)
                if (!Clips.ContainsKey(pair.Key) || float.IsNaN(pair.Value) || float.IsInfinity(pair.Value) || pair.Value <= 0)
                    throw new ArgumentException("Invalid native clip duration: " + pair.Key, nameof(durations));
            this.durations = new Dictionary<BarrackAction, float>(durations);
        }
        public void Start()
        {
            if (Mode != BarrackInteractionMode.Loading) throw new InvalidOperationException("Interaction host already started");
            Change(BarrackAction.Entrance, BarrackInteractionMode.Entrance);
        }
        public bool Supports(BarrackAction action) => durations.ContainsKey(action);
        public bool Request(BarrackAction action)
        {
            if (!CanInteract || !Supports(action) || action == BarrackAction.Entrance || action == BarrackAction.Return || action == BarrackAction.Idle) return false;
            if(action==BarrackAction.Look)IsFocused=true;
            Change(action, BarrackInteractionMode.Action); return true;
        }
        public bool BeginOrbit()
        {
            if (!CanInteract) return false;
            IsFocused=false;
            if (Mode != BarrackInteractionMode.Orbit) Change(BarrackAction.Idle, BarrackInteractionMode.Orbit);
            return true;
        }
        public bool Return()
        {
            if (Mode == BarrackInteractionMode.Loading || Mode == BarrackInteractionMode.Closed || Mode == BarrackInteractionMode.Returning) return false;
            IsFocused=false;
            Change(BarrackAction.Return, BarrackInteractionMode.Returning); return true;
        }
        public void Tick(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Mode == BarrackInteractionMode.Closed || Mode == BarrackInteractionMode.Loading || Mode == BarrackInteractionMode.Idle || Mode == BarrackInteractionMode.Orbit) return;
            elapsed += seconds;
            if (elapsed < durations[Action]) return;
            if (Mode == BarrackInteractionMode.Action) Return();
            else Change(BarrackAction.Idle, BarrackInteractionMode.Idle);
        }
        void Change(BarrackAction action, BarrackInteractionMode mode)
        { Action = action; Mode = mode; elapsed = 0; Changed?.Invoke(action, mode); }
        public void Close() { Mode = BarrackInteractionMode.Closed; Changed = null; }
    }
}
