using System.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.HotUpdate
{
    public sealed class LobbyProcedure : DemoProcedure
    {
        public Task Staging { get; private set; } = Task.CompletedTask;
        public LobbyProcedure(DemoContext context) : base(context) { }
        public override async Task EnterAsync(ProcedureManager owner, StateScope scope)
        {
            await Game.Scenes.LoadAsync("Lobby", scope);
            DemoView.CreateWorld(Demo.OrbMaterial);
            await OpenViewAsync(scope, "ENERGY\nCOLLECTOR", "A small game. A complete framework.\nCollect every orb before time runs out.");
            View.Button("START EXPEDITION", new Rect(32, 312, 360, 48), () =>
            { if (!context.Validating) owner.RequestChange<BattleProcedure>(scope); }, true);
            View.Button("SETTINGS", new Rect(32, 374, 174, 44), View.OpenSettings);
            View.Button("STAGE UPDATE", new Rect(218, 374, 174, 44), () =>
            {
                if (context.Validating || !Staging.IsCompleted) return;
                var view = View;
                Staging = scope.Run(async token =>
                {
                    view.Notice.text = "Downloading and verifying update...";
                    string result = await Game.StageUpdate(token);
                    token.ThrowIfCancellationRequested(); view.Notice.text = result;
                });
            });
            // Validation spans multiple procedures and is owned by the entry's test harness.
            View.Button("RUN FRAMEWORK CHECK", new Rect(32, 432, 360, 44), () => context.RequestValidation());
            View.Notice.text = "BEST " + Demo.Save.bestScore + "  /  COMPLETED " + Demo.Save.completedRuns + "\nUpdate takes effect after restart.";
            for (int i = 0; i < 5; i++)
                await Entities.ShowAsync(Game.Entities.NextId(), "orb", "Energy", () => new EnergyOrbLogic(),
                    new OrbSpawnData { Context = context, Position = new Vector3((i - 2) * 1.5f, 0.7f, Mathf.Sin(i) * 1.2f) });
            var orbs = Entities.GetEntities();
            Game.Entities.Attach(orbs[4], orbs[0], worldPositionStays: false);
            orbs[4].Instance.transform.localPosition = new Vector3(0.75f, 0.6f, 0);
            orbs[4].Instance.transform.localScale = Vector3.one * 0.35f;
        }
    }
}
