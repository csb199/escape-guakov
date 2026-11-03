using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Core;
using Game.Runtime;
using UnityEngine;

namespace Game.HotUpdate
{
    public sealed class RoundResult
    {
        public readonly string Id;
        public readonly int Score, Events;
        public RoundResult(string id, int score, int events) { Id = id; Score = score; Events = events; }
    }
    public sealed class DemoContext
    {
        public readonly GameRuntime Game;
        public readonly DemoServices Demo;
        public RoundResult LastRound;
        public bool Validating;
        public Action RequestValidation;
        public DemoContext(GameRuntime game, DemoServices demo) { Game = game; Demo = demo; }
    }
    public abstract class DemoProcedure : ProcedureBase
    {
        protected readonly DemoContext context;
        protected GameRuntime Game => context.Game;
        protected DemoServices Demo => context.Demo;
        public EntityOwner<GameObject> Entities { get; private set; }
        public StateScope Scope { get; protected set; }
        public DemoView View { get; protected set; }
        protected DemoProcedure(DemoContext context) { this.context = context; }
        protected async Task OpenViewAsync(StateScope scope, string title, string subtitle)
        {
            Scope = scope; View = new DemoView(context);
            await Game.Ui.OpenFormAsync("Demo.Main", UiLayer.Screen, scope, () => View, new DemoView.Content { Title = title, Subtitle = subtitle }, "ui-shell");
            await Game.Audio.PlayAsync("music", "Music", scope, loop: true);
            // Entity tasks drain before the view is released (scope cleanup is LIFO).
            Entities = Game.Entities.CreateOwner(scope, GetType().Name + "/entities");
        }
        public override void Update(ProcedureManager owner, float deltaTime)
        {
            View.UpdateDiagnostics(GetType().Name);
            Entities.Tick(deltaTime);
        }
        public override Task ExitAsync(ProcedureManager owner)
        {
            View = null; return Task.CompletedTask;
        }
    }
    public readonly struct EnergyCollected
    {
        public readonly int EntityId, Score;
        public readonly string RunId;
        public EnergyCollected(int entityId, int score, string runId) { EntityId = entityId; Score = score; RunId = runId; }
    }
}
