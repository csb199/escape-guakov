using System;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using UnityEngine;

namespace Game.HotUpdate
{
    public sealed class ResultProcedure : DemoProcedure
    {
        public Task Submission { get; private set; } = Task.CompletedTask;
        public ResultProcedure(DemoContext context) : base(context) { }
        public override async Task EnterAsync(ProcedureManager owner, StateScope scope)
        {
            await Game.Scenes.LoadAsync("Arena", scope);
            DemoView.CreateWorld(Demo.OrbMaterial);
            var result = context.LastRound ?? throw new InvalidOperationException("Missing round result");
            Demo.RecordRun(result.Score);
            await OpenViewAsync(scope, "MISSION\nCOMPLETE", "Progress saved atomically.\nSubmitting score to the simulated leaderboard...");
            Game.Ui.Label(View.Panel, result.Score + " ENERGY", new Rect(32, 318, 360, 60), 38, DemoView.Accent);
            View.Button("RETURN TO BASE", new Rect(32, 422, 360, 48), () =>
            { if (!context.Validating) owner.RequestChange<LobbyProcedure>(scope); }, true);
            var view = View;
            Submission = scope.Run(async token =>
            {
                try
                {
                    int rank = await Demo.Scores.SubmitAsync(result.Id, result.Score, TimeSpan.FromSeconds(2), token);
                    token.ThrowIfCancellationRequested(); view.Notice.text = "SIMULATED RANK #" + rank;
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    if (scope.IsActive) view.Notice.text = "Saved locally / leaderboard unavailable";
                    Game.Log.Write(GameLogLevel.Warning, "game", "rank.failed", e.Message, result.Id, exception: e);
                }
            });
        }
    }
}
