using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Runtime
{
    /// <summary>
    /// 框架服务组装入口（AOT），仅持有通用能力；业务通过构造注入取得本对象。
    /// Modules：模块依赖和异步生命周期，无全模块 Update 轮询。
    /// Resources：AB/资源缓存、依赖图、租约；Scenes：场景包加载及流程退出卸载。
    /// Entities：实例创建/查询/父子挂载/回收/缓存；Ui：UIForm；Audio：音频组及播放。
    /// Log/Events：日志与事件。presentation 模块仅维护 Unity EventSystem。
    /// FSM/Procedure 在 Core，由接入方组装；业务数据、存档模型与远端协议由业务模块管理。
    /// 注册顺序与依赖见构造函数；新增框架模块在此组装，业务模块在 IGameEntry 注册。
    /// 完整目录、模块 ID 与修改边界：项目根 CODEMAP.md。
    /// </summary>
    public sealed class GameRuntime
    {
        public readonly GameConfig Config;
        public readonly GameLog Log;
        public readonly EventBus Events = new EventBus();
        public readonly ModuleHost Modules;
        public readonly ResourceService Resources;
        public readonly EntityService Entities;
        public readonly SceneService Scenes;
        public readonly UiService Ui;
        public readonly AudioService Audio;
        public readonly Transform Root;
        public readonly string DataDirectory;
        public readonly string Version;
        public Func<CancellationToken, Task<string>> StageUpdate;
        GameObject eventSystem;
        public GameRuntime(GameConfig config, GameLog log, Transform root, string dataDirectory, string releaseDirectory, ReleaseManifest release)
        {
            Config = config; Log = log; Root = root; DataDirectory = dataDirectory; Version = release.version;
            Modules = new ModuleHost(log); Resources = new ResourceService(releaseDirectory, release); Scenes = new SceneService(Resources, log); Ui = new UiService(root, Resources, log);
            Entities = new EntityService(Resources, root, log);
            Audio = new AudioService(Resources, root, log);
            Modules.Add(Resources);
            Modules.Add(new ServiceModule("presentation", new[] { "resources" }, ct =>
            {
                eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(root); return Task.CompletedTask;
            }, () => { if (eventSystem != null) UnityEngine.Object.Destroy(eventSystem); }));
            Modules.Add(Scenes); Modules.Add(Ui); Modules.Add(Entities); Modules.Add(Audio);
        }
        sealed class ServiceModule : IModule
        {
            readonly Func<CancellationToken, Task> initialize;
            readonly Action dispose;
            bool disposed;
            public string Id { get; }
            public IReadOnlyList<string> Dependencies { get; }
            public ServiceModule(string id, string[] dependencies, Func<CancellationToken, Task> initialize, Action dispose)
            { Id = id; Dependencies = dependencies; this.initialize = initialize; this.dispose = dispose; }
            public Task InitializeAsync(CancellationToken cancellation) => initialize(cancellation);
            public Task StartAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }
            public Task StopAsync(CancellationToken cancellation) => Task.CompletedTask;
            public void Dispose() { if (!disposed) { disposed = true; dispose(); } }
        }
    }
}
