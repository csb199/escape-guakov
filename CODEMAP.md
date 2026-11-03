# 代码分类与 AI 阅读入口

本文件描述当前源码的边界。先读本文件，再沿入口阅读；历史验收报告用于追溯，不作为当前目录索引。Unity 2022.3 LTS / Windows / HybridCLR。

## 先读这三个入口

1. [Bootstrap.cs](Assets/Game/Bootstrap/Bootstrap.cs)：AOT 冷启动、更新验证、元数据与 DLL 加载、统一退出。
2. [GameRuntime.cs](Assets/Game/Runtime/GameRuntime.cs)：框架服务组装、模块依赖和注入入口。
3. [GameEntry.cs](Assets/Game/HotUpdate/Demo/Entry/GameEntry.cs)：示例游戏热更入口、业务模块注册、流程组装和帧驱动。

程序集边界保持 `Contracts ← Core ← Runtime`。Bootstrap 使用稳定层，通过 IGameEntry 调用热更入口，不能静态引用 Game.HotUpdate。**HotUpdate 表示部署方式，不代表其中的代码是通用框架；当前 HotUpdate/Demo 全部是可替换的示例业务。**

## 目录分类

| 类别 | 目录 | 内容与修改边界 |
|---|---|---|
| 框架契约 / AOT | Assets/Game/Contracts | IModule、IGameEntry、日志/传输/序列化契约；不放具体玩法 |
| 框架核心 / AOT | Assets/Game/Core | ModuleHost、FSM/Procedure、StateScope、实体父子/缓存、事件、日志、存档基础、更新与发布校验；不依赖 Unity |
| Unity 适配 / AOT | Assets/Game/Runtime | AB/资源租约、场景、实体、UIForm、音频、配置与服务组装；不存成绩或业务数据 |
| 启动宿主 / AOT | Assets/Game/Bootstrap | Boot 场景入口、HybridCLR 加载与统一关闭；包含明确标注的 Demo 测试接缝 |
| 示例业务 / 热更 | Assets/Game/HotUpdate/Demo | 大厅、战斗、结算、能量球、界面、成绩、模拟排行榜和验收 |
| 示例内容构建 / Editor | Assets/Game/Editor/Demo | DemoBuilder：生成示例资源、场景、签名包及完整客户端 |
| 独立发布工具 / Editor | Assets/Game/Editor/Publishing | ReleasePublisher：冻结基线检查、兼容代码与资源发布；当前资源构建仍调用 DemoBuilder.BuildContent，新游戏需替换内容配置 |
| 示例素材 | Assets/Game/Content、HotScenes | 预制体、材质、音频与各版本热更新场景；资源路径仍保留，避免改变构建地址 |
| 启动场景 | Assets/Game/Scenes | 只有 Boot 进入 Player；旧 Lobby/Arena 空场景文件不在构建列表，玩法场景来自 HotScenes |
| 启动配置 | Assets/Resources/game-config.json | 公钥、平台、客户端标识、日志与更新缓存设置；不是业务配置表 |
| 构建生成内容 | Assets/StreamingAssets、Assets/HybridCLR | 前者是签名运行包，Git 忽略并通过 Prepare/Build 生成；后者是 HybridCLR 生成引用等，不能作为手写业务编辑 |
| 第三方依赖 | Packages | HybridCLR、UGUI 及许可证，独立于自研框架 |
| 自动测试 | Tests | .NET 核心回归；另编译 Demo 的纯 C# 数据/排行榜以验证示例边界 |
| 自动化工具 | Tools | 构建、发布、Editor/Player 验收、交付打包、离线源码镜像同步 |
| 文档与结果 | 根目录 *.md、TestResults | 架构、使用与历史验收；Git 仅保留 TestResults 文档及核心结果，运行目录不上传 |
| 本机生成数据 | Builds、Library、Temp、Logs、LocalOnly 等 | Player、冻结基线、缓存、备份、离线镜像及签名私钥，均不进入 Git |

## Demo 内部分类

```text
Assets/Game/HotUpdate/
  Game.HotUpdate.asmdef       保持程序集名与引用关系
  Demo/
    Entry/GameEntry.cs       GameEntry + DemoController + DemoReport
    Procedures/             DemoProcedure、Lobby、Battle、Result
    Entities/               EnergyOrbLogic：能量球自身行为
    UI/                     DemoView：示例窗口与展示世界
    Services/               DemoServices、DemoModel、Scores
    Validation/             DemoValidation、ServicesValidation、WorldValidation、ShutdownProbe
```

`DemoServices` 管理 DemoConfig、SaveData、ScoreClient、MockScoreTransport 和示例资源组；这些不是正式联网后端或通用存档 schema。Validation 故意跨越游戏流程做验收，不能复制其测试编排来替代正常业务生命周期。GameEntry 中的验证入口和 Contracts.IDemoValidation 是现有测试接缝，并未声称验证代码从业务程序集完全剥离。

Runtime/UnityServices.cs 同时包含 JSON、异步操作、租约和 UI 创建辅助；其中旧 GameObjectPool 是保留的简单池辅助，当前玩法实体走 EntityService，不应将其作为新实体入口。

## 模块与运行链路

| 模块 ID | 实现 | 职责 / 依赖 |
|---|---|---|
| resources | ResourceService | 资源地址、AB 依赖、加载缓存和租约 |
| presentation | GameRuntime 内部 ServiceModule | EventSystem；依赖 resources |
| scenes | SceneService | 包场景加载/卸载和所有权；依赖 resources |
| ui | UiService | 窗口生命周期；依赖 resources/presentation |
| entities | EntityService | 创建、查询、挂载、回收与缓存；依赖 resources |
| audio | AudioService | 分组、播放与回收；依赖 resources |
| demo.gameplay | DemoServices | 示例业务；依赖 entities/ui/audio/scenes |
| demo.shutdown-probe | ShutdownProbe | 仅验证参数启用的异步退出探针 |
| game.entry | GameEntryModule | 包装 IGameEntry，依赖前面全部已注册模块，最后启动、最先停止 |

```text
Boot / Bootstrap
  → 校验/安装更新 → HybridCLR 元数据 + DLL
  → new GameRuntime（仅组装框架服务）
  → new GameEntry → RegisterModules（注册 DemoServices）
  → ModuleHost 启动 → GameEntry.StartAsync
  → DemoController → ProcedureManager → Lobby / Battle / Result
  → 流程 scope：先登记场景，再登记 UI/音频/EntityOwner
  → 离开：取消并等待任务 → 逆序回收实体/UI/音频 → 卸载场景 → 下一流程
```

ModuleHost 不轮询模块。DemoController 仅 Tick 当前已就绪流程，流程再更新自己的 EntityOwner。EntityCacheDriver 和 AudioDriver 维护各自服务，不承担全局业务更新。

## 修改任务应落在哪里

- 新玩法/新流程：Demo/Procedures 或新业务目录；通过构造注入取得服务，不改 Bootstrap。
- 新角色/怪物：业务 EntityLogic + 预制体，通过 EntityOwner.ShowAsync 创建；不直接 Destroy 受管实例。
- 新窗口：业务 UiFormLogic，通过 UiService 打开；任务、订阅跟随窗口 Scope。
- 新框架能力：Core 写不依赖 Unity 的逻辑，Runtime 适配 Unity，在 GameRuntime 中显式组装；业务模块在 GameEntry.RegisterModules 注册。
- 资源和热更发布：先看 WORLD.md / EXPANSION.md，再改内容构建配置；不手工修改已签名清单或冻结基线。

保持作用域退出时取消并等待任务，不用 async void 业务任务；父子实体同 owner；场景卸载完成才归还租约；资源包不能在 Destroy 尚未完成时卸载。框架要求 Unity 主线程调用，不提供任意线程安全保证。

目录移动保留 .meta GUID、命名空间、程序集名、入口全名和资源地址。新路径不要求新增 asmdef。历史报告仍可能指向旧路径，按本文件查找。离线构建只编辑根源码，Tools/Sync-GameSource.ps1 会镜像同步并移除旧路径残留。

## 拉取源码后运行

Git 仓库是源码版本，不包含已编译 Player、签名包、私钥和本机冻结基线。Unity 中先执行 `Game Framework / 1 Prepare Demo`，生成本机示例包后打开 Boot 运行。完整 IL2CPP 热更验收须执行 BuildHybrid；新的本机签名密钥对应新的公钥，不能直接沿用其他发行者的冻结发布基线。

```powershell
dotnet run --project Tests/Framework.Tests.csproj
./Tools/Run-Unity.ps1 -Action Prepare
./Tools/Run-Unity.ps1 -Action BuildHybrid
./Tools/Test-Demo.ps1 -Backend HybridCLR
./Tools/Test-Publish.ps1
```

`-Offline` 仅用于本机 Unity Package Manager 异常环境，正常安装不需要。目录整理本身不等于重新验收旧 Player；旧交付 ZIP 和历史 19 次启动报告仍对应上一轮完整构建。
