# 文件夹分类与框架阅读入口

按“契约 → 核心 → 引擎适配 → 启动 → 业务接入”阅读。各目录 README 是本目录说明，GitHub 文件列表中的提交文字仅表示最近变更。

## 主入口

| 阅读次序 | 文件 | 关注点 |
|---|---|---|
| 1 | [Contracts.cs](Assets/Game/Contracts/Contracts.cs) | IGameEntry、IModule 等共享契约 |
| 2 | [Lifecycle.cs](Assets/Game/Core/Lifecycle.cs) | ModuleHost 的依赖排序、启动和关闭 |
| 3 | [GameRuntime.cs](Assets/Game/Runtime/GameRuntime.cs) | 基础服务注册、依赖与构造注入 |
| 4 | [Bootstrap.cs](Assets/Game/Bootstrap/Bootstrap.cs) | 更新验证、HybridCLR 加载与统一退出 |
| 5 | [GameEntry.cs](Assets/Game/HotUpdate/Demo/Entry/GameEntry.cs) | IGameEntry 的参考实现；注册业务并驱动流程 |

## 文件夹职责

| 目录 | 分类 | 入口与边界 |
|---|---|---|
| [Contracts](Assets/Game/Contracts/README.md) | AOT 共享契约 | 不声明具体业务数据 |
| [Core](Assets/Game/Core/README.md) | AOT 核心机制 | 无 Unity 依赖；模块、流程、实体、存储、更新 |
| [Runtime](Assets/Game/Runtime/README.md) | AOT Unity 适配 | GameRuntime 组装资源、场景、实体、UI、音频 |
| [Bootstrap](Assets/Game/Bootstrap/README.md) | 冷启动宿主 | 通过共享契约调用热更入口，不静态依赖业务程序集 |
| [HotUpdate](Assets/Game/HotUpdate/README.md) | 热更业务接入 | Demo 仅是参考实现，业务可替换 |
| [Editor](Assets/Game/Editor/README.md) | 内容构建与独立发布 | 仅 Editor 编译，不属于运行时模块 |
| Assets/Game/Content、HotScenes | 验证用资源源文件 | 预制体、材质、音频、版本化场景；按逻辑地址访问 |
| Assets/Game/Scenes | 启动场景 | 只有 Boot 进入 Player；历史空场景不参与构建 |
| Assets/Resources | 启动配置 | game-config.json：公钥、平台、客户端与日志/更新配置 |
| Assets/StreamingAssets | 生成的签名包 | Git 忽略；Prepare/Build 生成 |
| Assets/HybridCLR | 生成引用与桥接相关文件 | 使用 HybridCLR 工具生成，不手写业务 |
| [Packages](Packages/README.md) | 第三方依赖 | 保留许可证，和自研框架分开维护 |
| [ProjectSettings](ProjectSettings/README.md) | Unity 工程配置 | 版本、平台、场景列表与 HybridCLR 配置 |
| [Tools](Tools/README.md) | 自动化 | 构建、发布、验证、打包与离线镜像同步 |
| [Tests](Tests/README.md) | 核心回归 | .NET 直接编译核心源码与部分纯 C# 接入样例 |
| [TestResults](TestResults/README.md) | 验收证据 | 说明验证版本与边界，不作为源码导航 |
| Builds、Library、Temp、Logs、LocalOnly | 本机产物 | Player、缓存、冻结基线、备份、镜像和密钥，不上传 Git |

## 核心功能定位

| 功能 | 核心入口 | Unity 适配 |
|---|---|---|
| 生命周期 | Core/Lifecycle.cs、GameEntryModule.cs | Runtime/GameRuntime.cs |
| FSM 与流程 | Core/Fsm.cs、Procedure.cs | 由业务帧驱动调用 Tick |
| 异步所有权 | Core/StateScope.cs | 各服务句柄与业务作用域 |
| 实体、挂载、缓存 | Core/Entities.cs、EntityHierarchy.cs、EntityCache.cs | Runtime/EntityService.cs |
| 资源地址与依赖 | Core/ResourceCatalog.cs | Runtime/ResourceService.cs |
| 场景 | 流程作用域 | Runtime/SceneService.cs |
| UI / 音频 | 作用域与资源租约 | Runtime/UiForms.cs、AudioService.cs |
| 日志、存储、事件 | Core/GameLog.cs、Persistence.cs、Lifecycle.cs | Runtime/UnityServices.cs 提供 JSON 等适配 |
| 更新、下载、磁盘预算 | Core/Updates.cs、Networking.cs、UpdateCache.cs | Bootstrap、Runtime/PlatformDiskSpace.cs |
| 发布兼容检查 | Core/ReleasePublishing.cs | Editor/Publishing/ReleasePublisher.cs |

Runtime/UnityServices.cs 也保留旧 GameObjectPool 辅助，当前实体入口统一使用 EntityService。

## 业务接入目录

[HotUpdate/Demo](Assets/Game/HotUpdate/Demo/README.md) 仅演示框架接口组合，不规定玩法：

- Entry：入口实现、业务模块注册与帧驱动。
- Procedures：流程编排与作用域所有权。
- Entities：实体自身逻辑。
- UI：窗口逻辑与展示适配。
- Services：业务服务、数据模型与外部协议适配。
- Validation：串联验证与退出探针。

IDemoValidation 是已有自动验收接缝；验证代码仍位于业务程序集内。Editor/Publishing 当前复用 Editor/Demo 的资源构建方法，新项目需替换内容配置。

## 修改与阅读规则

通用机制在 Core，Unity API 在 Runtime，业务在 HotUpdate。框架服务在 GameRuntime 组装，业务服务在 IGameEntry.RegisterModules 注册。模块职责与完整运行时序见 [ARCHITECTURE](ARCHITECTURE.md)。

只编辑根工程；LocalOnly/OfflineProject 由工具同步。移动资产保留 .meta GUID，更改程序集/入口全名需同步发布契约。历史报告可能仍使用旧路径，以本索引为准。签名私钥、已编译客户端与测试运行目录不进入 Git。
