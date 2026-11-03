# 热更新场景、实体挂载与缓存淘汰

本轮在现有 FSM/Procedure、EntityService、多包资源与独立发布工具上补齐三项常用能力。基线为 Unity 2022.3.62f1、Windows x64、HybridCLR 9.0.3。Core/Runtime 的接口变化需要完整升级客户端一次；之后兼容的业务代码、实体预制体和场景可独立发布。

## 热更新场景

只有 Boot 放进 Player Build Settings。Lobby、Arena 通过签名清单中的逻辑地址加载，其 `kind` 为 `scene`，`asset` 指向 Assets 下的 `.unity` 文件，bundle 为 `scene-lobby` 或 `scene-arena`。发布器读取 Unity 构建结果生成包依赖；场景和普通资源共用依赖闭包租约。

```csharp
// Procedure.EnterAsync 内先登记场景，再创建 UI、音频、EntityOwner。
var scene = await game.Scenes.LoadAsync("Arena", scope);
var owner = game.Entities.CreateOwner(scope, "Battle/entities");

// 叠加预加载时可暂不切换活动场景：
var extra = await game.Scenes.LoadAsync("Lobby", scope, activate: false);
game.Scenes.SetActive(extra);
await extra.UnloadAsync(); // 同一 handle 重复调用共用卸载任务。
```

所有场景使用 Additive 加载，保留 Boot。服务串行处理场景加载/卸载，拒绝重复逻辑地址和指向同一路径的重复加载；可用 TryGet、GetLoadedScenes 查询。未激活的叠加场景仍会正常运行其组件，`activate:false` 只表示不设为 active scene，不是延迟 Unity 场景激活。

SceneHandle 自动登记到流程 scope。切换先取消旧流程并等待任务，逆序清理实体/UI/音频，最后卸载旧场景；卸载完成后才释放场景包及依赖租约，再进入新流程。结算也加载自己的 Arena，避免借用战斗的已释放资源。

Unity 原生场景加载不能直接取消：取消后等待原生加载结束，再补偿卸载；期间保留包租约。卸载失败会保留租约并上报，不假装清理成功。业务不要绕开 SceneService 调用 SceneManager 卸载受管场景。

Demo 的 V1/V2/V3 场景带不同 SceneContent 标记；V3 改变场景内几何尺寸，两个场景包都与 V2 不同。V3 场景包由独立发布工具新建，并不在初始客户端内。更新仍是暂存后冷启动生效，不会原地替换正在运行的场景。新资源使用的组件、Shader 和 AOT API 必须受现有客户端支持；代码引用白名单不能证明任意资源组件都兼容。

## 实体挂载

```csharp
var parent = await owner.ShowAsync(game.Entities.NextId(), "orb", "Energy",
    () => new EnergyOrbLogic(), parentData);
var child = await owner.ShowAsync(game.Entities.NextId(), "orb", "Energy",
    () => new EnergyOrbLogic(), childData);
game.Entities.Attach(child, parent, socket: "WeaponSocket", worldPositionStays: false);
// socket 为父预制体内 Transform 相对路径；省略时直接挂父根节点。
var children = parent.GetChildren();
game.Entities.Detach(child); // 回到实体池根节点，保留世界位置。
await parent.HideAsync();
```

逻辑类可重写 OnAttachTo(parent, data)、OnDetachFrom(parent, data)、OnAttached(child, data)、OnDetached(child, data)。这些是同步回调；异步任务仍登记到 Entity.Scope。挂载图由 Core 管理，Runtime 通过 IEntityAttachmentProvider 适配 Transform。所有挂载操作使用当前激活句柄，拒绝旧句柄、跨 manager、自挂载与循环。

父子必须属于同一个 EntityOwner，可以属于不同实体组；不提供跨流程所有权转移。回调中不能再次修改挂载图，可以 RequestHide。父回收会先预留整棵树的隐藏任务并传播取消，然后等待所有子实体结束再回收自己；子实体通过正常 OnHide/OnRecycle 回池，不能由 Unity 随父对象直接销毁。子任务等待父 HideAsync 会形成自等待，因此被拒绝，应该 RequestHide。

挂载失败会拆除已提交关系，隐藏并丢弃失败的子实例，汇总清理错误。换父时，拆卸回调结束后再次检查父子是否仍活动，防止回调请求回收后继续建立关系。业务不要直接修改受管父子 Transform 关系或 Destroy 实体；逐次变化的数据在 OnShow 重置。大厅第五个球作为第一个球的附属实体，复用时会恢复缩放，战斗仍有五个正常大小的球。

## 缓存策略

```csharp
game.Entities.AddGroup("Projectile", capacity: 500, cachePolicy: new EntityCachePolicy
{
    maxCached = 100,
    expireSeconds = 30,
    releaseIntervalSeconds = 5,
    maxReleasePerPass = 16
});
game.Entities.SetCacheProperties(handle, priority: 10, locked: true);
await handle.HideAsync();
foreach (var cached in game.Entities.GetCachedInstances("Projectile"))
    game.Entities.SetCachedProperties("Projectile", cached.Id, cached.Priority, locked: false);
game.Entities.TrimCache("Projectile");
// 场景结束或内存压力时可主动释放全部未锁定的空闲实例：
game.Entities.ReleaseAllUnused();
```

capacity 是加载、显示、隐藏中总预留数的硬上限。maxCached 是空闲实例目标，独立于活动上限，默认等于 capacity。缓存按组、资源地址、逻辑类型隔离。锁定项和单次释放预算可能让空闲数暂时超过目标；maxCached=0 的未锁定实例在回收时直接释放。

每次只选择空闲且未锁定的实例，过期优先，再按低优先级、最久未使用排序。回收超过容量时立即维护，正常过期按组周期维护。EntityCacheDriver 每秒最多发起一次维护；单次 maxReleasePerPass 限制每组自动释放量。手动 ReleaseAllUnused 不限数量，但仍保留锁定项；服务最终 Stop 强制清理全部缓存。

先从缓存索引移除候选，再执行 OnDestroy，避免业务回调重入导致重复释放。销毁实例会归还资源租约，ResourceService 等待跨帧，并检查每个包最近释放租约的帧号，再卸载零引用包；即使等待期间有其他对象新发起 Destroy，也不会同帧卸载其包。资源服务最终关闭也等待跨帧。活动实体、加载中的实例以及场景/UI/音频仍持有的包不会被淘汰。

## 高频使用与边界

owner 与 group 各自维护成员索引，只有当前流程的 owner.Tick 更新实体；Tick 复用列表快照。射线沿命中 Transform 向上查实例字典，优先命中最近实体。ModuleHost 不轮询所有模块，Procedure 不轮询非当前状态，缓存维护也不执行业务 Update。

这些机制减少无关遍历并控制释放峰值，但创建大量实例仍需业务按帧安排，缓存也不是字节级内存预算。1,500 层实体树的纯 C# 回归用于证明迭代回收和任务顺序，不代表 Unity 万实体帧率测试。服务要求主线程调用，异步任务必须合作取消；长时间无视取消的任务会阻止安全切换。

## 与 UnityGameFramework 的对应

参考 UnityGameFramework `e7eb3ef11393df0ac504204c480cdb839752b05f` 与 GameFramework `d0c010b05167c58e92350449d04864a91ca13fd2`：

| 上游逻辑 | 本项目实现 |
|---|---|
| SceneManager 的加载/卸载状态与资源保护 | SceneService、SceneHandle、场景依赖租约、取消后补偿卸载 |
| EntityManager Attach/Detach、子先于父隐藏 | EntityHierarchy、挂点适配、父子回调、整树取消与异步等待 |
| ObjectPool 的 locked/inUse、Expire、Priority/LastUse、定期释放 | EntityCachePolicy、空闲/活动隔离、分批淘汰、最终强制清理 |

保留本项目的显式依赖、构造注入与 StateScope 所有权，未复制上游统一每帧模块轮询。验收见 [WORLD-VALIDATION](TestResults/WORLD-VALIDATION.md)，审查见 [WORLD-REVIEW](TestResults/WORLD-REVIEW.md)。
