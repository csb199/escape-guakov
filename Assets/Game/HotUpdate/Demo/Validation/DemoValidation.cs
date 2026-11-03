using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using Game.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate
{
    // Application-owned test harness: it deliberately crosses procedure lifetimes.
    public static class DemoValidation
    {
        public static async Task<string> RunAsync(DemoContext context, ProcedureManager procedures, CancellationToken cancellation)
        {
            var game = context.Game; var demo = context.Demo;
            var mock = demo.Transport as MockScoreTransport ?? throw new InvalidOperationException("Validation needs the mock transport");
            var checks = new List<string>();
            void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Demo assertion: " + name); checks.Add(name); }
            Button Button(string name) => game.Root.GetComponentsInChildren<Button>().Single(b => b.name == name);
            Check(game.Modules.States.Values.All(s => s == ModuleState.Running), "module dependency initialization");
            Check(MatchesMaterial(demo.OrbMaterial.color, EnergyOrbLogic.ScorePerOrb), "resource bundle matches code version");
            Check(procedures.Current is LobbyProcedure, "procedure starts in lobby");
            int references = game.Resources.ReferenceCount;
            bool wrongTypeRejected = false;
            try { using (await game.Resources.LoadAsync<AudioClip>("orb", cancellation)) { } }
            catch (InvalidOperationException e) { wrongTypeRejected = e.Message.Contains("Asset type mismatch"); }
            Check(wrongTypeRejected && game.Resources.ReferenceCount == references, "wrong asset type does not leak a resource lease");
            Button("SETTINGS").onClick.Invoke();
            await ((LobbyProcedure)procedures.Current).View.SettingsTask;
            Check(game.Ui.OpenCount == 2, "settings button opens popup layer");
            Check(((LobbyProcedure)procedures.Current).View.Form.IsCovered, "popup covers main form and disables underlying interaction");
            float volume = demo.Save.volume; bool mute = demo.Save.mute;
            game.Root.GetComponentsInChildren<Slider>().Single().value = 0.35f;
            if (!demo.Save.mute) Button("MUTE").onClick.Invoke();
            Check(demo.Saves.Load().mute && game.Audio.MasterMute && Math.Abs(demo.Save.volume - 0.35f) < 0.001f, "settings controls persistence and audio");
            Button("CLOSE").onClick.Invoke();
            await game.Ui.WhenIdleAsync();
            Check(game.Ui.OpenCount == 1, "close button releases popup");
            Check(!((LobbyProcedure)procedures.Current).View.Form.IsCovered && ((LobbyProcedure)procedures.Current).View.Reveals == 1, "closing popup reveals its previous form");
            demo.SaveSettings(volume, mute);
            await ServicesValidation.RunAsync(context, checks, cancellation);
            await WorldValidation.RunAsync(context, checks, cancellation);

            var lobby = (LobbyProcedure)procedures.Current;
            var oldEntities = lobby.Entities.GetEntities();
            Check(oldEntities.Length == 5 && oldEntities.All(e => e.Logic is EnergyOrbLogic && game.Entities.TryGet(e.Id, out var found) && found == e),
                "entity service creates and queries hot-update logic by ID");
            Check(game.Entities.GetGroupEntities("Energy").Length == 5 && game.Entities.GetGroup("Energy").Reserved == 5,
                "entity group tracks the procedure-owned instances");
            var oldScope = lobby.Scope;
            var oldStartButton = Button("START EXPEDITION").onClick;
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool staleWrite = false;
            var pending = oldScope.Run(async token =>
            {
                await release.Task; // Deliberately cannot be cancelled until the dependency completes.
                if (!token.IsCancellationRequested) staleWrite = true;
            });
            var entityPending = oldEntities[0].Scope.Run(async token => { await release.Task; token.ThrowIfCancellationRequested(); });
            var changing = procedures.ChangeAsync<BattleProcedure>(cancellation);
            try
            {
                await Task.Yield();
                Check(!changing.IsCompleted && oldScope.Token.IsCancellationRequested && game.Scenes.Current == "Lobby",
                    "procedure switch cancels and waits for old tasks before loading next scene");
            }
            finally { release.TrySetResult(true); }
            await changing; await pending;
            try { await entityPending; } catch (OperationCanceledException) { }
            Check(oldEntities[0].Scope.PendingCount == 0 && oldEntities.All(e => !e.IsShown && !game.Entities.TryGet(e.Id, out _)),
                "procedure exit drains entity tasks and removes all owned IDs");
            Check(!oldEntities[0].Interact() && game.Entities.ActiveCount == demo.Config.targetScore,
                "old entity handle cannot interact with a reused instance");
            Check(!staleWrite && oldScope.PendingCount == 0, "old procedure cannot write into new UI");
            context.Validating = false;
            try { oldStartButton.Invoke(); } finally { context.Validating = true; }
            Check(procedures.Current is BattleProcedure && !procedures.IsTransitioning, "stale UI callback cannot request another transition");
            int runs = demo.Save.completedRuns;
            var battle = (BattleProcedure)procedures.Current;
            Check(game.Scenes.Current == "Arena" && game.Entities.ActiveCount == demo.Config.targetScore, "scene and pooled resource spawn");
            await Task.Delay(400, cancellation);
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                DemoCapture.Save(battle.Camera, game.Root, Path.Combine(game.DataDirectory, "demo-arena.png"));
            mock.FailuresRemaining = 1;
            CollectAll(battle);
            await procedures.Transition;
            var result = (ResultProcedure)procedures.Current;
            await result.Submission;
            var round = context.LastRound;
            Check(round.Score == demo.Config.targetScore * EnergyOrbLogic.ScorePerOrb && round.Events == demo.Config.targetScore, "raycast collection event and score");
            Check(game.Entities.ActiveCount == 0 && game.Events.SubscriptionCount == 0, "round resources and subscriptions released");
            Check(game.Entities.GetGroup("Energy").Reserved == 0 && game.Entities.GetGroup("Energy").Cached == game.Entities.InstanceCount,
                "collected entities leave ID registry and return to their group pool");
            Check(demo.Saves.Load().completedRuns == runs + 1, "save round trip");
            int submits = mock.UniqueSubmissions;
            await demo.Scores.SubmitAsync(round.Id, round.Score, TimeSpan.FromSeconds(2), cancellation);
            Check(mock.UniqueSubmissions == submits, "network retry and idempotency");
            mock.DelayMs = 200; bool timedOut = false;
            try { await demo.Scores.SubmitAsync("timeout", 1, TimeSpan.FromMilliseconds(10), cancellation); }
            catch (TimeoutException) { timedOut = true; }
            Check(timedOut, "network timeout"); mock.DelayMs = 120;
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool observed = false;
                try { await demo.Scores.SubmitAsync("cancel", 1, TimeSpan.FromSeconds(1), cancelled.Token); }
                catch (OperationCanceledException) { observed = true; }
                Check(observed, "network cancellation");
            }
            await procedures.ChangeAsync<BattleProcedure>(cancellation); int created = game.Entities.CreatedCount;
            await procedures.ChangeAsync<LobbyProcedure>(cancellation); await procedures.ChangeAsync<BattleProcedure>(cancellation);
            Check(game.Entities.CreatedCount == created, "pool reuses instances across scenes");

            mock.DelayMs = 1500;
            CollectAll((BattleProcedure)procedures.Current); await procedures.Transition;
            result = (ResultProcedure)procedures.Current;
            Check(!result.Submission.IsCompleted, "result procedure owns an asynchronous leaderboard request");
            await procedures.ChangeAsync<LobbyProcedure>(cancellation);
            Check(result.Submission.IsCompleted && result.Scope.PendingCount == 0 && procedures.Current is LobbyProcedure,
                "leaving result drains leaderboard request before entering lobby");
            mock.DelayMs = 120;
            Check(game.Events.SubscriptionCount == 0 && game.Resources.ReferenceCount == game.Entities.InstanceCount + 4 && game.Audio.ActiveCount == 1, "scene cleanup and shared asset lease");
            Check(game.Entities.LoadingCount == 0 && game.Entities.ActiveCount == 5,
                "returning to lobby leaves no pending entity loads or battle entities");
            game.Log.Write(GameLogLevel.Information, "validation", "demo.passed", string.Join(", ", checks), round.Id);
            game.Scenes.TryGet("Lobby", out var finalScene);
            return JsonUtility.ToJson(new DemoReport { passed = true, version = game.Version, codeFlavor = DemoController.CodeFlavor,
                scene = game.Scenes.Current, sceneAsset = finalScene.AssetPath, runtime = Application.unityVersion, checks = checks.ToArray() }, true);
        }
        // Keep both field accesses in the baseline IL; a constant branch would hide V2's Color.r reference.
        static bool MatchesMaterial(Color color, int scorePerOrb) => scorePerOrb >= 2 ? color.r > 0.9f : color.g > 0.7f;
        static void CollectAll(BattleProcedure battle)
        {
            Physics.SyncTransforms();
            foreach (var orb in battle.ActiveOrbs)
            {
                var point = battle.Camera.WorldToScreenPoint(orb.Instance.transform.position);
                battle.ClickWorld(point); battle.ClickWorld(point);
            }
        }
    }
}
