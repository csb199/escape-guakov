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
    /// Energy Collector 示例的热更新入口，不是通用框架服务容器。
    /// RegisterModules 注册 DemoServices；StartAsync 通过 DemoController 组装大厅/战斗/结算。
    /// Procedures 组织玩法，Entities 放实体行为，UI 放窗口，Services 放数据与排行榜，
    /// Validation 放自动验收。DemoController.Update 只驱动当前流程，不遍历所有模块。
    /// 更换游戏时替换此处业务组装，保留 IGameEntry 契约和作用域清理规则。
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
