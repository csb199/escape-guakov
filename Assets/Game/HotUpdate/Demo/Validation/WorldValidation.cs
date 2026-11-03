using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Core;
using Game.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.HotUpdate
{
    public static class WorldValidation
    {
        sealed class ProbeEntity : EntityLogic<GameObject>
        {
            public int Attaches, Detaches;
            public override void OnInit()
            {
                var socket = new GameObject("Socket"); socket.transform.SetParent(Instance.transform, false);
                socket.transform.localPosition = new Vector3(0.5f, 1, 0);
            }
            public override void OnAttachTo(EntityHandle<GameObject> parent, object data) => Attaches++;
            public override void OnDetachFrom(EntityHandle<GameObject> parent, object data) => Detaches++;
        }
        public static async Task RunAsync(DemoContext context, List<string> checks, CancellationToken cancellation)
        {
            var game = context.Game;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException("World assertion: " + name); checks.Add(name); }
            Check(SceneManager.sceneCountInBuildSettings == 1 && game.Scenes.TryGet("Lobby", out var lobby) && lobby.IsLoaded,
                "only Boot is built in and gameplay scene loads from its versioned resource bundle");
            game.Scenes.TryGet("Lobby", out lobby);
            string marker = "SceneContent-V" + EnergyOrbLogic.ScorePerOrb;
            Check(lobby.Scene.GetRootGameObjects().Any(o => o.name == marker), "authored scene content matches the installed hot-update version");
            int leases = game.Resources.ReferenceCount;
            var sceneOwner = new StateScope("scene-probe", game.Log, cancellation);
            try
            {
                bool duplicate = false;
                try { await game.Scenes.LoadAsync("Lobby", sceneOwner); } catch (InvalidOperationException) { duplicate = true; }
                Check(duplicate && game.Resources.ReferenceCount == leases, "duplicate scene load cannot reserve another scene or lease");
                bool wrongType = false;
                try { using (await game.Resources.LoadAsync<GameObject>("Arena", cancellation)) { } } catch (InvalidOperationException) { wrongType = true; }
                Check(wrongType && game.Resources.ReferenceCount == leases, "scene addresses cannot be loaded as ordinary assets");
                var arena = await game.Scenes.LoadAsync("Arena", sceneOwner, activate: false);
                await game.Resources.TrimUnusedAsync(cancellation);
                Check(game.Scenes.LoadedCount == 2 && game.Scenes.Current == "Lobby" && game.Resources.BundleReferences("scene-arena") == 1,
                    "additive scene load retains its bundle and leaves current scene active");
                var unloading = arena.UnloadAsync();
                Check(ReferenceEquals(unloading, arena.UnloadAsync()), "repeated scene unload shares one operation");
                await unloading;
                Check(!arena.IsLoaded && game.Scenes.LoadedCount == 1 && game.Resources.ReferenceCount == leases,
                    "scene unload completes before releasing its bundle dependency lease");
            }
            finally { await sceneOwner.DisposeAsync(); }
            sceneOwner = new StateScope("scene-cancel", game.Log, cancellation);
            var opening = game.Scenes.LoadAsync("Arena", sceneOwner, activate: false);
            var closing = sceneOwner.DisposeAsync();
            bool cancelled = false; try { await opening; } catch (OperationCanceledException) { cancelled = true; }
            await closing;
            Check(cancelled && game.Scenes.PendingCount == 0 && game.Scenes.LoadedCount == 1 && game.Resources.ReferenceCount == leases,
                "owner cancellation during scene load waits for rollback and preserves the existing scene");

            var visible = game.Entities.GetGroupEntities("Energy"); var attached = visible.Single(e => e.Parent != null);
            Check(attached.Instance.transform.parent == attached.Parent.Instance.transform && attached.Parent.GetChildren().Contains(attached),
                "lobby companion uses entity hierarchy and follows its parent transform");
            var entityScope = new StateScope("attachment-probe", game.Log, cancellation);
            var owner = game.Entities.CreateOwner(entityScope, "attachment-probe");
            try
            {
                var parent = await owner.ShowAsync(game.Entities.NextId(), "orb", "AttachmentProbe", () => new ProbeEntity());
                var child = await owner.ShowAsync(game.Entities.NextId(), "orb", "AttachmentProbe", () => new ProbeEntity());
                var logic = (ProbeEntity)child.Logic;
                game.Entities.Attach(child, parent, "Socket", worldPositionStays: false);
                Check(child.Instance.transform.parent == parent.Instance.transform.Find("Socket") && logic.Attaches == 1,
                    "entity attachment targets named socket and invokes attach lifecycle");
                game.Entities.Detach(child);
                Check(child.Parent == null && !child.Instance.transform.IsChildOf(parent.Instance.transform) && logic.Detaches == 1,
                    "entity detach restores pool root and invokes detach lifecycle");
                game.Entities.Attach(child, parent, "Socket");
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var work = child.Scope.Run(_ => gate.Task); var hiding = parent.HideAsync(); await Task.Yield();
                Check(!hiding.IsCompleted && child.Scope.Token.IsCancellationRequested && game.Resources.ReferenceCount == leases + 2,
                    "parent teardown retains Unity instances and leases until child tasks complete");
                gate.TrySetResult(true); await work; await hiding;
                Check(child.Parent == null && child.Instance == null && parent.Instance == null && game.Resources.ReferenceCount == leases,
                    "parent teardown recycles attached child first and zero-cache group releases both leases");

                game.Entities.SetCachePolicy("AttachmentProbe", new EntityCachePolicy { maxCached = 2, expireSeconds = 0.1, releaseIntervalSeconds = 0.1, maxReleasePerPass = 1 });
                var locked = await owner.ShowAsync(game.Entities.NextId(), "orb", "AttachmentProbe", () => new ProbeEntity());
                var idle = await owner.ShowAsync(game.Entities.NextId(), "orb", "AttachmentProbe", () => new ProbeEntity());
                var active = await owner.ShowAsync(game.Entities.NextId(), "orb", "AttachmentProbe", () => new ProbeEntity());
                game.Entities.SetCacheProperties(locked, 10, true);
                await locked.HideAsync(); await idle.HideAsync();
                float deadline = Time.realtimeSinceStartup + 4;
                while (game.Entities.GetGroup("AttachmentProbe").Cached > 1 && Time.realtimeSinceStartup < deadline) { cancellation.ThrowIfCancellationRequested(); await Task.Yield(); }
                Check(active.IsShown && game.Entities.GetGroup("AttachmentProbe").Cached == 1 && game.Entities.GetCachedInstances("AttachmentProbe")[0].Locked,
                    "automatic cache expiry releases unused instance while preserving active and locked instances");
                await active.HideAsync();
                foreach (var item in game.Entities.GetCachedInstances("AttachmentProbe")) game.Entities.SetCachedProperties("AttachmentProbe", item.Id, item.Priority, false);
                game.Entities.ReleaseAllUnused();
                Check(game.Resources.ReferenceCount == leases && game.Entities.ActiveCount == visible.Length,
                    "manual cache eviction returns resource leases without changing live gameplay entities");
            }
            finally
            {
                await entityScope.DisposeAsync();
                game.Entities.SetCachePolicy("AttachmentProbe", new EntityCachePolicy { maxCached = 0 });
            }
        }
    }
}
