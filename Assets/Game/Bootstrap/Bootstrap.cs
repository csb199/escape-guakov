using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using Game.Runtime;
using HybridCLR;
using UnityEngine;

namespace Game.Bootstrap
{
    /// <summary>
    /// 框架冷启动入口（AOT，挂在 Boot 场景）。AI 阅读导航：项目根 CODEMAP.md。
    /// BootCoreAsync：配置/日志 → 更新校验与修复 → HybridCLR 元数据/DLL → GameRuntime
    /// → IGameEntry.RegisterModules → ModuleHost.StartAsync → 提交健康版本。
    /// ShutdownAsync：取消启动/入口任务，等待模块逆序停止，再释放依赖资源。
    /// 框架能力在 Contracts/Core/Runtime；示例业务在 HotUpdate/Demo，禁止在这里写玩法。
    /// --self-test、故障注入、IDemoValidation 是 Demo 验证接缝，不是正式游戏业务流程。
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        CancellationTokenSource lifetime;
        GameRuntime game;
        IGameEntry entry;
        UpdateEngine updates;
        GameLog log;
        FileStream instanceLock;
        Task bootTask, shutdownTask;
        string status = "Starting...", data;
        bool ready, bootCancelled;
        bool allowQuit;
        Task quitTask;
        int exitCode;
        static bool Has(string argument) => Environment.GetCommandLineArgs().Contains(argument);
        static string Argument(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
        void Awake() { Application.wantsToQuit += WantsToQuit; }
        async void Start()
        {
            DontDestroyOnLoad(gameObject); Application.targetFrameRate = 60;
            lifetime = new CancellationTokenSource();
            data = Argument("--data=") ?? Application.persistentDataPath;
            Directory.CreateDirectory(data);
            try
            {
                instanceLock = new FileStream(Path.Combine(data, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                bootTask = BootAsync(lifetime.Token);
                await bootTask;
                if (Has("--quit-after-ready")) { Exit(0); return; }
                if (Has("--self-test"))
                {
                    string report = await ((IDemoValidation)entry).ValidateAsync(lifetime.Token);
                    File.WriteAllText(Path.Combine(data, "demo-report.json"), report);
                    if (Has("--stage-update")) await StageRemoteAsync(lifetime.Token);
                    await ShutdownAsync();
                    Exit(0);
                }
            }
            catch (Exception e)
            {
                status = "START / CHECK FAILED\n" + e.Message + "\nSee log in: " + data;
                log?.Write(GameLogLevel.Critical, "bootstrap", "boot.failed", e.Message, exception: e);
                Debug.LogException(e);
                try { await ShutdownAsync(); }
                catch (Exception cleanupError) { Debug.LogException(cleanupError); }
                File.WriteAllText(Path.Combine(data, "failure.txt"), e.ToString());
                if (Has("--self-test") || Has("--exit-on-startup-failure")) Exit(1);
            }
        }
        async Task BootAsync(CancellationToken cancellation)
        {
            try { await BootCoreAsync(cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { bootCancelled = true; throw; }
        }
        async Task BootCoreAsync(CancellationToken cancellation)
        {
            var json = new UnityJson();
            var asset = Resources.Load<TextAsset>("game-config");
            if (asset == null) throw new InvalidOperationException("Run Game Framework/Prepare Demo first");
            var config = json.Decode<GameConfig>(asset.text); Resources.UnloadAsset(asset); config.Validate();
            config.remoteDirectory = Argument("--update-source=") ?? config.remoteDirectory;
            log = new GameLog(Path.Combine(data, "Logs", "game.jsonl"), config.clientBuild, json, line => Debug.Log(line))
            { MinimumLevel = (GameLogLevel)(Debug.isDebugBuild ? config.developmentLogLevel : config.releaseLogLevel) };
            var verifier = new ReleaseVerifier(json, config.publicKey, config.clientBuild, config.platform);
            ReleaseVerifier.ValidateName(config.clientBuild);
            updates = new UpdateEngine(Path.Combine(data, "Updates", config.clientBuild), json, verifier, log, config.updateCache,
                () => PlatformDiskSpace.AvailableBytes(data));
            string baseline = Path.Combine(Application.streamingAssetsPath, "Baseline");
            if (string.IsNullOrEmpty(updates.ActiveVersion) && string.IsNullOrEmpty(updates.PendingVersion))
            {
                using (var source = new FileTransport(baseline))
                    await updates.StageAsync(await source.ManifestAsync(cancellation), source, Progress, cancellation);
            }
#if !UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(config.remoteDirectory))
            {
                try
                {
                    using (var source = new FileTransport(config.remoteDirectory))
                        await updates.StageAsync(await source.ManifestAsync(cancellation), source, Progress, cancellation);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { log.Write(GameLogLevel.Warning, "update", "remote.unavailable", "Using locally verified release", exception: e); }
            }
#endif
            var release = await SelectReleaseAsync(config, baseline, cancellation); log.CodeVersion = release.version;
            log.Write(GameLogLevel.Information, "bootstrap", "release.selected", "code=" + release.version + " resources=" + release.resourceVersion);
            string releaseDirectory = updates.DirectoryFor(release.version);
            status = "Loading code " + release.version;
            Assembly hot = null;
#if UNITY_EDITOR
            hot = AppDomain.CurrentDomain.GetAssemblies().Single(x => x.GetName().Name == "Game.HotUpdate");
            log.Write(GameLogLevel.Warning, "bootstrap", "editor.mode", "Editor uses its compiled hot assembly; validate code replacement in an IL2CPP player");
#else
            foreach (var file in release.files.Where(f => f.kind == "aot"))
            {
#if ENABLE_IL2CPP
                var result = RuntimeApi.LoadMetadataForAOTAssembly(File.ReadAllBytes(Path.Combine(releaseDirectory, file.name)), HomologousImageMode.SuperSet);
                if (result != LoadImageErrorCode.OK) throw new InvalidOperationException("AOT metadata rejected: " + file.name + " / " + result);
                log.Write(GameLogLevel.Debug, "bootstrap", "metadata.loaded", file.name);
#endif
            }
            foreach (var file in ReleaseVerifier.OrderedAssemblies(release))
            {
                var assembly = Assembly.Load(File.ReadAllBytes(Path.Combine(releaseDirectory, file.name)));
                log.Write(GameLogLevel.Information, "bootstrap", "assembly.loaded", assembly.GetName().Name);
                if (assembly.GetType(release.entryType, false) != null) hot = assembly;
            }
#endif
            if (hot == null) throw new TypeLoadException("Entry assembly was not loaded");
            game = new GameRuntime(config, log, transform, data, releaseDirectory, release) { StageUpdate = StageRemoteAsync };
            var type = hot.GetType(release.entryType, true);
            if (!typeof(IGameEntry).IsAssignableFrom(type)) throw new TypeLoadException("Entry does not implement shared contract");
            entry = (IGameEntry)Activator.CreateInstance(type, game);
            try { GameEntryModule.Register(game.Modules, entry); }
            catch { entry.Dispose(); throw; }
            await game.Modules.StartAsync(cancellation);
            if (Has("--fail-before-ready"))
                throw new InvalidOperationException("Requested test fault: candidate failed before readiness");
            if (Has("--cancel-before-ready")) lifetime.Cancel();
            cancellation.ThrowIfCancellationRequested();
            updates.MarkHealthy(); ready = true;
            try { await updates.MaintainCacheAsync(cancellation); }
            catch (Exception e) { log.Write(GameLogLevel.Warning, "update", "cache.cleanup.failed", "Protected releases retained", exception: e); }
        }
        async Task<ReleaseManifest> SelectReleaseAsync(GameConfig config, string baseline, CancellationToken cancellation)
        {
            try { return updates.BeginBoot(); }
            catch (Exception e) when (!string.IsNullOrEmpty(updates.ActiveVersion))
            {
                log.Write(GameLogLevel.Warning, "update", "update.repair.required", "Repairing the active release without downgrading saves", exception: e);
                var errors = new System.Collections.Generic.List<Exception> { e };
                string remote = string.IsNullOrWhiteSpace(config.remoteDirectory)
                    ? Path.Combine(Application.streamingAssetsPath, "DemoUpdate") : config.remoteDirectory;
                foreach (string directory in new[] { baseline, remote }.Distinct())
                {
                    try
                    {
                        using (var source = new FileTransport(directory))
                            await updates.RepairActiveAsync(await source.ManifestAsync(cancellation), source, Progress, cancellation);
                        return updates.BeginBoot();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception repairError) { errors.Add(repairError); }
                }
                throw new AggregateException("Active release could not be repaired. Restore its signed package at the update source, then restart.", errors);
            }
        }
        void Progress(string file, float value) { status = "Verifying update " + (int)(value * 100) + "%\n" + file; }
        async Task<string> StageRemoteAsync(CancellationToken cancellation)
        {
            string sourcePath = string.IsNullOrWhiteSpace(game.Config.remoteDirectory)
                ? Path.Combine(Application.streamingAssetsPath, "DemoUpdate") : game.Config.remoteDirectory;
            using (var source = new FileTransport(sourcePath))
                await updates.StageAsync(await source.ManifestAsync(cancellation), source, Progress, cancellation);
            return string.IsNullOrEmpty(updates.PendingVersion) ? "Already on the latest version." : "Verified " + updates.PendingVersion + " / restart to activate.";
        }
        Task ShutdownAsync()
        {
            if (shutdownTask != null) return shutdownTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdownTask = completion.Task; ready = false;
            _ = ShutdownCoreAsync(completion);
            return shutdownTask;
        }
        async Task ShutdownCoreAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                lifetime?.Cancel();
                if (bootTask != null) { try { await bootTask; } catch { /* Startup reports its own error. */ } }
                if (game != null) await game.Modules.StopAsync(CancellationToken.None);
                if (game != null)
                {
                    if (game.Events.SubscriptionCount != 0 || game.Resources.ReferenceCount != 0 || game.Ui.OpenCount != 0 ||
                        game.Entities.ActiveCount != 0 || game.Entities.LoadingCount != 0 || game.Entities.InstanceCount != 0 || game.Audio.InstanceCount != 0 || game.Resources.PendingCount != 0 ||
                        game.Scenes.LoadedCount != 0 || game.Scenes.PendingCount != 0)
                        throw new InvalidOperationException("Shutdown leaked resources");
                    File.WriteAllText(Path.Combine(data, "shutdown-check.txt"), "PASS: modules stopped; subscriptions=0; resource leases=0; UI=0; entities=0; entity loads=0; entity instances=0; audio=0; resource loads=0");
                }
                if (bootCancelled) updates?.CancelBoot();
                instanceLock?.Dispose(); completion.TrySetResult(true);
            }
            catch (Exception e) { completion.TrySetException(e); }
        }
        void OnApplicationQuit()
        {
            lifetime?.Cancel();
        }
        bool WantsToQuit()
        {
            if (allowQuit) return true;
            if (quitTask == null) quitTask = QuitAfterShutdownAsync();
            return false;
        }
        async Task QuitAfterShutdownAsync()
        {
            // A synchronous shutdown must not recursively call Quit inside the first wantsToQuit callback.
            await Task.Yield();
            try { await ShutdownAsync(); }
            catch (Exception e)
            {
                status = "SHUTDOWN FAILED\n" + e.Message;
                log?.Write(GameLogLevel.Critical, "bootstrap", "shutdown.failed", "Quit blocked because shutdown did not complete", exception: e);
                return;
            }
            allowQuit = true; Application.Quit(exitCode);
        }
        void OnDestroy()
        {
            Application.wantsToQuit -= WantsToQuit;
            lifetime?.Cancel(); lifetime?.Dispose(); instanceLock?.Dispose();
        }
        void OnGUI()
        {
            if (ready) return;
            GUI.backgroundColor = new Color(0.05f, 0.09f, 0.15f);
            GUI.Box(new Rect(25, 25, Screen.width - 50, 180), "ENERGY COLLECTOR\n\n" + status);
        }
        void Exit(int code)
        {
            exitCode = code;
#if UNITY_EDITOR
            _ = ExitEditorAsync(code);
#else
            Application.Quit(code);
#endif
        }
#if UNITY_EDITOR
        async Task ExitEditorAsync(int code) { await ShutdownAsync(); UnityEditor.EditorApplication.Exit(code); }
#endif
    }
}
