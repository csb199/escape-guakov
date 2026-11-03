using System;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.HotUpdate
{
    public sealed class BattleProcedure : DemoProcedure
    {
        ProcedureManager procedures;
        Text scoreLabel, timer;
        int score, events;
        float remaining;
        string runId;
        public Camera Camera { get; private set; }
        public EntityHandle<GameObject>[] ActiveOrbs => Entities.GetEntities();
        public BattleProcedure(DemoContext context) : base(context) { }
        public override async Task EnterAsync(ProcedureManager owner, StateScope scope)
        {
            procedures = owner;
            await Game.Scenes.LoadAsync("Arena", scope);
            Camera = DemoView.CreateWorld(Demo.OrbMaterial);
            score = 0; events = 0; remaining = Demo.Config.roundSeconds; runId = Guid.NewGuid().ToString("N");
            await OpenViewAsync(scope, "COLLECT\nTHE LIGHT", "Click the glowing energy orbs.\nEvery pickup travels through the event bus.");
            scoreLabel = Game.Ui.Label(View.Panel, "0 ENERGY", new Rect(32, 316, 360, 60), 35, DemoView.Accent);
            timer = Game.Ui.Label(View.Panel, "", new Rect(32, 385, 360, 46), 24);
            View.Button("ABANDON / BACK", new Rect(32, 446, 360, 44), () =>
            { if (!context.Validating) owner.RequestChange<LobbyProcedure>(scope); });
            scope.Subscribe<EnergyCollected>(Game.Events, message =>
            {
                if (message.RunId != runId) return;
                events++; score += message.Score; scoreLabel.text = score + " ENERGY";
                View.Notice.text = "Entity pickup event / pooled instance returned";
                if (events == Demo.Config.targetScore) Finish();
            });
            for (int i = 0; i < Demo.Config.targetScore; i++)
            {
                float angle = i * Mathf.PI * 2 / Demo.Config.targetScore;
                await Entities.ShowAsync(Game.Entities.NextId(), "orb", "Energy", () => new EnergyOrbLogic(), new OrbSpawnData
                { Context = context, AudioOwner = scope, Position = new Vector3(Mathf.Cos(angle) * 3.2f, 0.3f, Mathf.Sin(angle) * 2.5f), RunId = runId, Collectible = true });
            }
            Game.Log.Write(GameLogLevel.Information, "game", "round.start", DemoController.CodeFlavor, runId);
        }
        public override void Update(ProcedureManager owner, float deltaTime)
        {
            base.Update(owner, deltaTime); remaining -= deltaTime;
            timer.text = Mathf.CeilToInt(Mathf.Max(0, remaining)) + " SECONDS LEFT";
            if (remaining <= 0) { Finish(); return; }
            if (!context.Validating && Input.GetMouseButtonDown(0) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
                ClickWorld(Input.mousePosition);
        }
        public void ClickWorld(Vector3 screenPoint)
        {
            if (!Scope.IsActive || !Physics.Raycast(Camera.ScreenPointToRay(screenPoint), out var hit)) return;
            Game.Entities.Interact(Entities, hit.collider.gameObject);
        }
        void Finish()
        {
            context.LastRound = new RoundResult(runId, score, events);
            procedures.RequestChange<ResultProcedure>(Scope);
        }
    }
}
