# 实体管理设计

流程负责组织战斗，EntityService 负责创建、查询和回收实体，热更新 EntityLogic 负责自身行为。流程退出统一回收所属实体；实体拥有的异步任务结束前，不复用其 GameObject。

## 创建与回收

```text
EntityOwner.ShowAsync(ID, 资源, 分组, 逻辑工厂, 显示数据)
  → 预留 ID 与组容量
  → 查找同组、同资源、同逻辑类型的空闲实例
  → 未命中则加载预制体并实例化，OnInit 一次
  → 建立本次 EntityHandle / StateScope → OnShow → 激活实例

HideAsync / RequestHide / 流程退出
  → 立即停止交互与更新，取消加载及实体任务
  → 等待任务结束 → 释放实体作用域资源 → OnHide → OnRecycle
  → 停用实例、移除 ID、归还组内缓存
  → 缓存容量/过期淘汰或服务关闭时 OnDestroy，销毁实例并释放预制体租约
```

| 类型 | 职责 |
|---|---|
| Core / EntityManager<T> | 与 Unity 无关的 ID、加载状态、分组容量、父子关系、复用、缓存淘汰、生命周期和失败清理 |
| Runtime / EntityService | GameObject 适配与 IModule 生命周期；依赖 resources；将射线命中路由到所属实体 |
| EntityOwner<T> | 流程拥有的实体集合，登记到流程异步清理栈；取消加载并回收所属实体 |
| EntityHandle<T> | 一次显示的身份；按 ID 查询、隐藏和交互，回收后永久失效 |
| EntityLogic<T> | OnInit / OnShow / OnUpdate / OnHide / OnRecycle / OnDestroy，以及挂载/拆卸回调；Entity.Scope 管理本次任务与订阅 |
| HotUpdate / EnergyOrbLogic | 旋转、拾取、防重复、V1/V2 得分规则、音效和拾取事件；回收时清空显示数据 |

实体逻辑是普通 C# 类，不要求预制体挂热更新脚本。战斗流程创建一组实体、路由输入并订阅带 RunId 的拾取事件，由流程累计成绩、决定结算。大厅使用相同逻辑类型，显示数据将 Collectible 设为 false。

## 扩展示例

业务模块初始化时声明分组；流程进入时创建 owner，并通过它创建实体：

```csharp
game.Entities.AddGroup("Energy", capacity: 12); // 业务模块 Initialize 中执行一次

// 流程 Enter 中：先加载并登记场景，再登记界面和 owner，退出时实体先于界面、场景清理。
var owner = game.Entities.CreateOwner(scope, "Battle/entities");
var handle = await owner.ShowAsync(game.Entities.NextId(), "orb", "Energy",
    () => new EnergyOrbLogic(), spawnData);

// 外部控制；实体自己的任务中使用 handle.RequestHide()，不要等待自身 HideAsync。
if (game.Entities.TryGet(handle.Id, out var current))
    await current.HideAsync();
```

流程的 EntityOwner 会通过 `scope.OwnAsync` 自动清理，无需逐个保存 GameObject 后手动 Return。实体逻辑中用 `Entity.Scope.Run` 登记任务、`Entity.Scope.Subscribe` 登记订阅；持有的同步资源可用 Own，异步清理用 OwnAsync。

OnInit 只初始化实例级状态，OnShow 接收本次 Entity 与显示数据，OnRecycle 清空临时数据，OnDestroy 释放实例级资源。逻辑工厂只在新实例创建时调用，逐次变化的数据必须放进 OnShow 参数。业务代码不直接 Destroy 实例或调用生命周期钩子。

生命周期钩子是同步方法，不使用 async void。异步行为登记到 Scope.Run，异步资源释放登记到 Scope.OwnAsync，服务会统一等待。

## 所有权与边界

- ID 在加载、显示和隐藏期间都被预留，重复 ID 被拒绝。隐藏完成后可复用数字 ID，但必须使用新句柄；旧句柄不能操作新激活对象。
- 分组 capacity 硬限制加载、显示、回收中的总预留数；EntityCachePolicy.maxCached 独立控制空闲缓存目标，默认等于 capacity。缓存按资源与逻辑类型隔离；锁定对象和分批淘汰可让空闲数暂时超过目标。
- 每个 Unity 实例持有自己的预制体租约，空闲缓存仍持有租约，只有销毁实例时才释放。原始资源由 ResourceService 缓存。
- 流程取消后立即屏蔽所属实体交互和更新；等待未完成加载，迟到的结果直接释放，不会出现在下一流程。
- 实体任务必须通过 Entity.Scope 登记；隐藏等待任务完成后才能复用实例。不合作任务会阻止回收，应用退出仍受 ModuleHost 的 5 秒监测约束。
- 作用域记录父子所有权，拒绝实体任务等待自己或所属流程/服务关闭。结束流程使用不等待完成的 RequestChange，自行回收使用 RequestHide。
- OnShow 失败会清理部分初始化并丢弃实例；OnHide/OnRecycle 失败仍清理其他实体，丢弃失败实例并汇总上报。
- 所有 API 在所属上下文调用，Unity 使用主线程。流程只调用当前 owner.Tick，加载、隐藏和其他流程实体不更新；没有新增全模块轮询。
- owner 与 group 各自维护成员索引；Tick 使用 owner 的可复用快照。射线从命中 Transform 向上查实例索引，取最近实体，避免父实体抢先接收子实体交互。
- Attach/Detach 支持挂点、父子回调和查询；仅允许同一 owner 内挂载。父回收先预留整棵树的隐藏任务并取消，再等待子实体先完成。子实体任务不能等待祖先 HideAsync，应使用 RequestHide。
- 自动缓存维护保护活动与锁定实例；过期优先，其次低优先级、最久未使用。策略、API 与使用示例见 [WORLD](WORLD.md)。

## 与 UnityGameFramework 的对应关系

参考 [UnityGameFramework](https://github.com/EllanJiang/UnityGameFramework) 提交 `e7eb3ef11393df0ac504204c480cdb839752b05f` 的 EntityComponent、EntityLogic，以及 [GameFramework](https://github.com/EllanJiang/GameFramework) 提交 `d0c010b05167c58e92350449d04864a91ca13fd2` 的 EntityManager。

对应其 ShowEntity、InternalShowEntity、InternalHideEntity、加载回调、分组复用与 EntityLogic 生命周期。本项目额外采用异步等待清理、激活句柄和流程作用域所有权，避免旧异步工作访问复用后的对象。

已实现上游的父子 Attach/Detach 思路及对象池的时间、优先级、锁定淘汰策略，并增加异步所有权边界。相机、灯光和地板是随场景销毁的展示对象；能量球由实体服务管理。未增加加载优先级队列或全套显示事件 API。新增稳定层接口需要完整升级客户端一次，之后兼容的实体逻辑、预制体及场景可通过发布包更新。
