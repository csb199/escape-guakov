using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Core;
using Game.Runtime;
using UnityEngine;

namespace Game.HotUpdate
{
    public static class ServicesValidation
    {
        sealed class ProbeForm : UiFormLogic
        {
            public readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task WaitFor = Task.CompletedTask;
            public bool Closed;
            public override async Task OnOpenAsync(object data) { Entered.TrySetResult(true); await WaitFor; Scope.Token.ThrowIfCancellationRequested(); }
            public override Task OnCloseAsync() { Closed = true; return Task.CompletedTask; }
        }
        public static async Task RunAsync(DemoContext context, List<string> checks, CancellationToken cancellation)
        {
            var game = context.Game;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Service assertion: " + name); checks.Add(name); }
            int references = game.Resources.ReferenceCount;
            int shared = game.Resources.BundleReferences("shared");
            using (var first = await game.Resources.LoadAsync<GameObject>("orb", cancellation))
            using (var second = await game.Resources.LoadAsync<GameObject>("orb", cancellation))
                Check(first.Asset == second.Asset && game.Resources.BundleReferences("shared") == shared + 2, "shared bundle dependency is retained by every asset lease");
            Check(game.Resources.ReferenceCount == references && game.Resources.BundleReferences("shared") == shared, "disposing resource leases releases their dependency references");
            int beforeTrim = game.Resources.LoadedBundleCount;
            int trimmed = await game.Resources.TrimUnusedAsync(cancellation);
            Check(!game.Resources.IsBundleLoaded("config") && game.Resources.BundleReferences("shared") > 0, "unused config bundle unloads while live shared dependencies remain loaded");
            using (var config = await game.Resources.LoadAsync<TextAsset>("demo-config", cancellation))
                Check(config.Asset.text.Contains("targetScore"), "unloaded bundle can be loaded again by resource address");
            var delayedLease = await game.Resources.LoadAsync<TextAsset>("demo-config", cancellation);
            int releaseFrame = -1;
            async Task ReleaseNextFrame()
            {
                await UnityTask.NextFrame(); releaseFrame = Time.frameCount; delayedLease.Dispose();
            }
            var releasing = ReleaseNextFrame(); var concurrentTrim = game.Resources.TrimUnusedAsync(cancellation);
            await Task.WhenAll(releasing, concurrentTrim);
            Check(Time.frameCount > releaseFrame || game.Resources.IsBundleLoaded("config"),
                "cache trim retains bundles released in its completion frame until Unity destruction can finish");
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel(); bool rejected = false;
                try { using (await game.Resources.LoadAsync<GameObject>("orb", cancel.Token)) { } } catch (OperationCanceledException) { rejected = true; }
                Check(rejected && game.Resources.ReferenceCount == references, "cancelled resource request cannot acquire a lease");
            }

            int windows = game.Ui.OpenCount;
            var owner = new StateScope("ui-probe", game.Log, cancellation);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logic = new ProbeForm { WaitFor = release.Task };
            var opening = game.Ui.OpenFormAsync("Probe.Loading", UiLayer.Overlay, owner, () => logic, prefab: "ui-shell");
            await logic.Entered.Task;
            var closingOwner = owner.DisposeAsync(); await Task.Yield();
            Check(!closingOwner.IsCompleted && logic.Scope.Token.IsCancellationRequested, "UI owner cancellation waits for in-flight form open");
            release.TrySetResult(true);
            try { await opening; } catch (OperationCanceledException) { }
            await closingOwner; await game.Ui.WhenIdleAsync();
            Check(logic.Closed && game.Ui.OpenCount == windows && game.Resources.ReferenceCount == references, "cancelled UI form releases prefab lease and runs close lifecycle");

            owner = new StateScope("ui-close-probe", game.Log, cancellation); logic = new ProbeForm();
            var form = await game.Ui.OpenFormAsync("Probe.Close", UiLayer.Overlay, owner, () => logic, prefab: "ui-shell");
            release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = form.Scope.Run(async token => { await release.Task; token.ThrowIfCancellationRequested(); });
            var closing = form.CloseAsync(); await Task.Yield();
            Check(!closing.IsCompleted && !form.IsOpen && game.Resources.ReferenceCount == references + 1, "form close retains its asset until owned work has drained");
            release.TrySetResult(true); try { await pending; } catch (OperationCanceledException) { }
            await closing; await owner.DisposeAsync();
            Check(logic.Closed && game.Resources.ReferenceCount == references, "completed form close releases exactly one lease");

            owner = new StateScope("ui-failure", game.Log, cancellation); bool failed = false;
            try { await game.Ui.OpenFormAsync("Probe.Failure", UiLayer.Popup, owner, () => throw new InvalidOperationException("Expected form factory fault"), prefab: "ui-shell"); }
            catch (InvalidOperationException) { failed = true; }
            await owner.DisposeAsync(); await game.Ui.WhenIdleAsync();
            Check(failed && game.Ui.OpenCount == windows && game.Resources.ReferenceCount == references, "failed UI creation removes its ID and releases loaded prefab");

            owner = new StateScope("audio-probe", game.Log, cancellation);
            var voice = await game.Audio.PlayAsync("chime", "Sfx", owner, loop: true, volume: 0.5f);
            bool oldMute = game.Audio.MasterMute; float oldVolume = game.Audio.MasterVolume;
            game.Audio.SetMaster(0.8f, false); game.Audio.SetGroup("Sfx", 0.5f, false);
            Check(Math.Abs(voice.EffectiveVolume - 0.2f) < 0.001f, "audio applies master group and instance volume independently");
            game.Audio.SetGroup("Sfx", 0.5f, true);
            Check(voice.EffectiveVolume == 0, "audio group mute updates existing playback");
            game.Audio.SetGroup("Sfx", 1, false); game.Audio.SetMaster(oldVolume, oldMute);
            int voiceId = voice.Id;
            var fading = voice.StopAsync(0.03f); await Task.Yield();
            game.Audio.SetMaster(oldVolume, true); await Task.Yield();
            Check(voice.EffectiveVolume == 0, "master mute applies immediately during audio fade-out");
            await fading; await voice.StopAsync(); game.Audio.SetMaster(oldVolume, oldMute);
            Check(!voice.IsPlaying && !game.Audio.TryGet(voiceId, out _) && game.Resources.ReferenceCount == references, "audio fade-out and repeated stop release one playback lease");
            voice = await game.Audio.PlayAsync("music", "Music", owner, loop: true);
            bool full = false;
            try { await game.Audio.PlayAsync("music", "Music", owner, loop: true); } catch (InvalidOperationException) { full = true; }
            Check(full, "audio group voice capacity is enforced");
            await owner.DisposeAsync();
            Check(!voice.IsPlaying && game.Audio.ActiveCount == 1 && game.Resources.ReferenceCount == references, "audio owner exit stops owned music and preserves the other owner");

            var isolated = new ResourceService("", new ReleaseManifest { files = new[] { new ReleaseFile { name = "unused", kind = "bundle" } } });
            await isolated.InitializeAsync(cancellation);
            var trim1 = isolated.TrimUnusedAsync(cancellation); var trim2 = isolated.TrimUnusedAsync(cancellation);
            var stop = isolated.StopAsync(cancellation);
            try { await Task.WhenAll(trim1, trim2); } catch (OperationCanceledException) { }
            await stop;
            Check(isolated.PendingCount == 0 && trim1.IsCompleted && trim2.IsCompleted, "resource shutdown drains active and queued cache trim operations");
            isolated.Dispose();
        }
    }
}
