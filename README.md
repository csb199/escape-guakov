# Unity 可热更新游戏框架

面向 Unity 的模块化 C# 游戏框架，使用 **HybridCLR（华佗）** 实现业务代码热更新，通过 AssetBundle 管理资源与场景。当前验证基线：Unity **2022.3.62f1**、Windows x64、HybridCLR **9.0.3**。

仓库中的 Demo 仅用于展示服务接入和串联验证，不规定游戏类型或玩法。

## 阅读入口

- [目录与源码导航](CODEMAP.md)：文件夹分类、模块入口、修改位置。
- [框架架构](ARCHITECTURE.md)：分层、依赖、生命周期与运行链路。
- [AI 协作约定](AGENTS.md)：代码边界、资源所有权和验证要求。

## 框架功能

| 能力 | 功能 |
|---|---|
| 模块生命周期 | 显式依赖、拓扑启动、逆序异步关闭、失败清理；ModuleHost 不做每帧模块轮询 |
| FSM / Procedure | 只更新当前流程；退出时取消并等待所属任务，再进入下一流程 |
| 作用域与事件 | StateScope 管任务、取消令牌、订阅和资源，防止过期回调访问新流程 |
| 代码热更新 | 签名验证、断点下载、AOT 元数据补充、热更程序集加载、冷启动版本切换与失败恢复 |
| 多包资源 | 逻辑地址、AB 依赖图、懒加载、资产缓存、引用租约与空闲卸载 |
| 场景资源 | 场景 AB 更新、Additive 加载、流程所有权、取消后的补偿卸载 |
| 实体管理 | 异步创建、分组、实例复用、激活句柄、父子挂载与挂点、所属流程统一回收 |
| 实体缓存 | 活动容量与空闲目标独立，支持过期、优先级、锁定和分批淘汰 |
| UIForm | 异步打开/关闭、覆盖/恢复、层级与交互管理，窗口任务跟随作用域清理 |
| 音频 | 分组、音量、静音、播放容量、淡出和作用域回收 |
| 存档与日志 | 原子写入、备份恢复、业务迁移回调；结构化日志、关联 ID、滚动与基础脱敏 |
| 发布工具 | 冻结客户端基线、兼容引用检查、独立代码/资源包发布、签名校验 |
| 更新存储 | 保护活动/回退/候选版本，清理过期缓存并检查磁盘预算 |

## 架构与入口

```text
Contracts ← Core ← Runtime
                 ↑       ↑
             Bootstrap   HotUpdate（业务接入）

Bootstrap → 更新验证与 DLL 加载 → GameRuntime 组装服务
          → IGameEntry 注册业务模块 → ModuleHost 启动
          → ProcedureManager 驱动当前流程
```

Bootstrap 通过共享的 IGameEntry 契约调用热更业务，不静态引用业务程序集。框架基础服务在 GameRuntime 组装，具体业务在热更入口注册。

| 目录 | 分类 | 阅读入口 |
|---|---|---|
| [Assets/Game](Assets/Game/README.md) | 框架源码与业务接入 | Bootstrap、GameRuntime、IGameEntry |
| [Assets/Game/Contracts](Assets/Game/Contracts/README.md) | 稳定契约 | Contracts.cs |
| [Assets/Game/Core](Assets/Game/Core/README.md) | 不依赖 Unity 的核心 | Lifecycle.cs、Procedure.cs、StateScope.cs |
| [Assets/Game/Runtime](Assets/Game/Runtime/README.md) | Unity 服务适配 | GameRuntime.cs |
| [Assets/Game/Bootstrap](Assets/Game/Bootstrap/README.md) | 冷启动与统一退出 | Bootstrap.cs |
| [Assets/Game/HotUpdate](Assets/Game/HotUpdate/README.md) | 热更业务接入，含可替换示例 | IGameEntry 实现 |
| [Assets/Game/Editor](Assets/Game/Editor/README.md) | 内容构建与发布 | DemoBuilder、ReleasePublisher |
| [Tools](Tools/README.md) / [Tests](Tests/README.md) | 自动化与回归 | 构建脚本、Framework.Tests.csproj |
| [Packages](Packages/README.md) / [ProjectSettings](ProjectSettings/README.md) | 第三方依赖与工程配置 | manifest.json、ProjectVersion.txt |
| [TestResults](TestResults/README.md) | 验收与历史证据 | VALIDATION.md |

## 生命周期与资源所有权

模块负责服务生命周期，流程负责业务编排，实体和窗口负责自身行为。三者各自拥有任务与资源边界。

流程退出先取消并等待任务，再逆序释放订阅、实体、窗口和音频，最后卸载所属场景。实体回收可进入缓存，只有实例销毁才归还预制体租约；资源包在引用归零且越过安全帧后才允许卸载。父实体回收等待子实体结束，挂载限于同一 owner。

使用方式见 [实体管理](ENTITY.md)、[场景与缓存](WORLD.md)、[发布/UI/资源/音频](EXPANSION.md)。其中具体资源名和业务类型均为接口使用示例。

## 构建与验证

Unity Hub 打开仓库，使用指定 Unity 版本。源码仓库不包含 Player、签名运行包、私钥或冻结基线。先执行菜单 `Game Framework / 1 Prepare Demo` 生成验证用内容，再打开 `Assets/Game/Scenes/Boot.unity`。Editor 只验证功能接入，完整热更必须在 IL2CPP Player 中验证。

```powershell
# .NET 9 SDK：核心回归
dotnet run --project Tests/Framework.Tests.csproj
# 完整 Windows IL2CPP 客户端，需要 Windows IL2CPP Support、VS C++ 与 Windows SDK
./Tools/Run-Unity.ps1 -Action BuildHybrid
# 功能、退出与更新恢复验证
./Tools/Test-Demo.ps1 -Backend HybridCLR
./Tools/Test-Editor.ps1
# 独立热更发布与兼容检查验证
./Tools/Test-Publish.ps1
```

本机 Unity Package Manager 异常时可加 `-Offline`，使用 LocalOnly 下的源码镜像。根工程是唯一编辑源，镜像由工具同步。详细命令见 [工具目录](Tools/README.md)。

## 使用边界

- 代码和资源更新运行中暂存、下次冷启动生效，不原地替换已加载程序集或场景。
- 修改 AOT 契约、原生依赖或客户端能力需要完整更新客户端；独立发布仅面向兼容变更。
- Unity 服务要求主线程；任务需合作取消。框架不会提前销毁仍被任务使用的资源。
- 业务存档模型、流程与远端协议由接入项目定义；示例传输不代表正式后端。
- 当前验证平台为 Windows x64，其他平台需适配；大规模实体帧率与长期压力需按目标项目测量。
- 生产签名密钥由发行系统管理，不提交 Git。当前验收范围和历史结果见 [验证记录](TestResults/VALIDATION.md)。
