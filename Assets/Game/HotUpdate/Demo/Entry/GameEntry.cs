using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using Game.Runtime;
using UnityEngine;
using UnityEngine.Scripting;

namespace Game.HotUpdate
{
    /// <summary>
    /// IGameEntry 的参考实现，展示框架的热更业务接入边界。
    /// RegisterModules 注册业务模块；StartAsync 组装流程；StopAsync 取消并等待业务退出。
    /// Entry 管入口，Procedures 管流程，Entities 管实体，UI 管窗口，Services 管业务服务，
    /// Validation 管验收。帧驱动只更新当前流程，资源和任务随作用域释放。
    /// 接入项目替换业务组装，保留 IGameEntry 契约和作用域清理规则。
    /// 类型全名 Game.HotUpdate.GameEntry 与程序集 Game.HotUpdate 被发布清单引用，不能随意改名。
    /// </summary>
    [Preserve]
    public sealed class GameEntry : IGameEntry, IDemoValidation
    {
        readonly GameRuntime game;
        readonly DemoServices demo;
        DemoController controller;
        [Preserve] public GameEntry(GameRuntime game) : this(game, new MockScoreTransport()) { }
        public GameEntry(GameRuntime game, IScoreTransport transport) { this.game = game; demo = new DemoServices(game, transport); }
        public void RegisterModules(IModuleRegistry modules)
        {
            modules.Add(demo);
            if (Environment.GetCommandLineArgs().Contains("--validate-shutdown")) modules.Add(new ShutdownProbe(game));
        }
        public async Task StartAsync(CancellationToken cancellation)
        {
            controller = game.Root.gameObject.AddComponent<DemoController>();
            await controller.InitializeAsync(game, demo, cancellation);
        }
        public Task<string> ValidateAsync(CancellationToken cancellation) => controller.ValidateAsync(cancellation);
        public Task StopAsync(CancellationToken cancellation) => controller != null ? controller.StopAsync(cancellation) : Task.CompletedTask;
        public void Dispose() { if (controller != null) { controller.Cleanup(); UnityEngine.Object.Destroy(controller); controller = null; } }
    }
    [Serializable] public sealed class DemoReport
    {
        public bool passed;
        public string version, codeFlavor, scene, runtime, sceneAsset;
        public string[] checks;
    }
    public sealed class DemoController : MonoBehaviour
    {
#if DEMO_V3
        public const string CodeFlavor = "V3 / TRIPLE ENERGY";
#elif DEMO_V2
        public const string CodeFlavor = "V2 / DOUBLE ENERGY";
#else
        public const string CodeFlavor = "V1 / CLASSIC ENERGY";
#endif
        GameRuntime game;
        DemoContext context;
        ProcedureManager procedures;
        CancellationTokenSource lifetime;
        Task validation = Task.CompletedTask;
        public async Task InitializeAsync(GameRuntime runtime, DemoServices demo, CancellationToken cancellation)
        {
            game = runtime; lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            context = new DemoContext(game, demo) { RequestValidation = RunInteractiveCheck };
            procedures = new ProcedureManager(game.Log, new LobbyProcedure(context), new BattleProcedure(context), new ResultProcedure(context));
            await procedures.ChangeAsync<LobbyProcedure>(lifetime.Token);
            game.Log.Write(GameLogLevel.Information, "game", "game.ready", CodeFlavor);
        }
        void Update() { if (lifetime != null && !lifetime.IsCancellationRequested) procedures?.Tick(Time.deltaTime); }
        void RunInteractiveCheck()
        {
            if (!validation.IsCompleted) return;
            _ = ObserveInteractiveCheckAsync();
        }
        async Task ObserveInteractiveCheckAsync()
        {
            try
            {
                string report = await ValidateAsync(lifetime.Token);
                File.WriteAllText(Path.Combine(game.DataDirectory, "interactive-check.json"), report);
            }
            catch (OperationCanceledException) when (lifetime == null || lifetime.IsCancellationRequested) { }
            catch (Exception e) { game.Log.Write(GameLogLevel.Error, "validation", "demo.failed", e.Message, exception: e); }
        }
        public Task<string> ValidateAsync(CancellationToken cancellation)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (!validation.IsCompleted) throw new InvalidOperationException("Validation is already running");
            var task = ValidateCoreAsync(cancellation); validation = task; return task;
        }
        async Task<string> ValidateCoreAsync(CancellationToken cancellation)
        {
            context.Validating = true;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation))
                try { return await DemoValidation.RunAsync(context, procedures, linked.Token); }
                finally { context.Validating = false; }
        }
        public async Task StopAsync(CancellationToken cancellation)
        {
            if (lifetime == null) return;
            lifetime.Cancel();
            var stopping = procedures?.StopAsync() ?? Task.CompletedTask;
            try { await validation; } catch (Exception e) { if (!(e is OperationCanceledException)) game.Log.Write(GameLogLevel.Warning, "validation", "demo.stopped", e.Message); }
            await stopping;
        }
        public void Cleanup()
        {
            if (lifetime == null) return;
            lifetime.Cancel(); lifetime.Dispose(); lifetime = null;
        }
        void OnDestroy() => Cleanup();
    }
}
