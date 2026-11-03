# 框架架构与运行流程

## 分层与依赖

| 层 | 职责 | 依赖 |
|---|---|---|
| Contracts / AOT | 入口、模块、日志、序列化与传输契约 | 基础 C# |
| Core / AOT | 生命周期、FSM、作用域、实体、存储、更新与发布校验 | Contracts，不依赖 Unity |
| Runtime / AOT | Unity 资源、场景、实体、窗口、音频及服务组装 | Core、Contracts、Unity |
| Bootstrap / AOT | 更新验证、HybridCLR 加载、启动与关闭 | 稳定层与 HybridCLR，通过 IGameEntry 调用业务 |
| HotUpdate | 业务模块、流程、实体逻辑及界面 | 稳定层；示例实现可整体替换 |
| Editor / Tools | 构建、发布、自动验收 | 编辑器与离线工具环境 |

源码目录索引见 [CODEMAP](CODEMAP.md)。Bootstrap 不静态引用热更程序集。构造和注册只组装对象，活跃资源在 Initialize/Start/Enter 中取得。

## 三个入口

1. **Bootstrap.BootCoreAsync**：读取配置、创建日志、验证/安装/修复版本，加载匹配的 AOT 元数据和业务程序集。
2. **GameRuntime 构造函数**：组装通用服务及显式依赖，向业务提供明确的服务引用。
3. **IGameEntry.RegisterModules / StartAsync**：接入方注册业务模块，再启动业务流程。GameEntryModule 把入口纳入宿主生命周期。

```text
Boot 场景
  → Bootstrap：校验更新 → 加载热更入口
  → GameRuntime：组装基础服务
  → GameEntryModule.Register：业务注册 → 包装入口模块
  → ModuleHost：依赖排序 → InitializeAsync → StartAsync
  → IGameEntry.StartAsync：组装并进入初始流程
  → 帧驱动 → ProcedureManager.Tick → 当前流程
```

入口成功就绪且取消检查通过后，才提交健康版本。已有健康版本时，失败候选在下一进程回退；首次安装可在下一次启动重新验证重试。正常取消保留候选，不能在同一进程卸载 DLL 后重启另一业务版本。

## 模块生命周期

ModuleHost 按 ID 与 Dependencies 做拓扑排序，拒绝缺失和循环依赖。启动后禁止新增模块；入口依赖已注册的全部模块，因此最后启动、最先停止。

```text
基础注册顺序：resources → presentation → scenes → ui → entities → audio
随后：接入方业务模块 → game.entry
正常关闭：逆序 StopAsync → Dispose
```

启动与停止交错时，先取消并等待启动收敛，再统一清理。共享关闭任务防止重复释放。Stop 超过监测时限会报告失败并保留仍被使用的依赖，不强制释放。Application.wantsToQuit 等待受控退出完成；强杀进程与断电不属于此保证。

ModuleHost 没有每帧轮询。服务若需要驱动，由专用适配器维护自己的活动对象；业务帧循环与模块生命周期分离。

## FSM / Procedure 与 StateScope

Fsm 提供通用状态机，ProcedureManager 管理业务流程，每次进入创建新的 StateScope。只更新当前已完成进入的流程，切换期间不执行流程 Update。

切换顺序：屏蔽旧作用域操作并取消令牌 → 等待登记任务结束 → 逆序释放订阅和资源 → 等待 ExitAsync → 创建新作用域并 EnterAsync。

作用域内使用 Run 登记任务、Subscribe 登记订阅、Own/OwnAsync 登记资源。流程自己的任务请求 RequestChange，不能等待会反过来等待自身的 ChangeAsync/StopAsync。窗口和实体同样用请求式关闭避免自等待。业务异步工作必须合作取消，不能用未登记任务继续访问旧界面。

## 资源与对象所有权

| 所有者 | 管理内容 | 释放时机 |
|---|---|---|
| ResourceService | AB、依赖闭包与资产缓存 | 引用归零且越过安全帧后卸载 |
| SceneHandle / 流程 scope | 场景及场景包租约 | 场景卸载完成后归还租约 |
| EntityOwner | 流程所属实体 | 退出时取消加载和任务，统一回收 |
| 实体实例缓存 | GameObject、逻辑对象、预制体租约 | 淘汰时销毁实例并归还租约 |
| UIForm / 音频句柄 | 窗口或播放及自身资源 | 关闭完成后归还租约 |

场景先登记到流程 scope，实体/UI/音频后登记，退出按反序清理。Unity 原生加载不能强制取消，完成后检查取消并补偿释放。场景采用 Additive，Boot 保留；业务不绕过服务卸载受管场景或销毁实体。

实体父子图与缓存策略位于 Core，Transform/挂点适配位于 Runtime。父子必须同 owner；父回收等待所有子任务完成。活动容量是硬上限，空闲容量是淘汰目标，锁定项和分批释放可暂时超目标。

## 热更新与发布

签名清单描述程序集、资源地址、包依赖、大小与哈希。客户端先校验平台、构建指纹和序号，下载到 staging，完成校验后提交候选。更新缓存维护保护活动、回退、候选和启动中版本，并检查磁盘余量。

完整构建导出冻结客户端基线。独立发布检查 AOT 指纹、外部 API 与泛型引用后生成兼容业务代码和资源包，不重建原生客户端。资源中的组件、Shader 和反射使用仍受既有客户端能力限制。

## 扩展职责

- 通用且不依赖 Unity 的机制放 Core；引擎适配放 Runtime；稳定接口放 Contracts。
- 框架服务在 GameRuntime 组装，业务模块通过 IGameEntry 注册，具体流程和逻辑在 HotUpdate。
- 存档基础提供原子写入与恢复，业务提供 schema 和迁移；传输基础提供文件下载，业务定义远端协议。
- 示例目录仅用于接入参考与回归验证，玩法类型不构成框架契约。

参考 UnityGameFramework 的模块、FSM/Procedure、Entity 和 ObjectPool 职责分离，结合本项目的显式依赖、构造注入与异步作用域。版本与细节见 [ENTITY](ENTITY.md)、[WORLD](WORLD.md)、[EXPANSION](EXPANSION.md)。历史审查记录保留在 TestResults，不替代当前架构说明。
