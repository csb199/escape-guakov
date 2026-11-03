# Unity 游戏框架与 Energy Collector Demo

Unity **2022.3.62f1** / Windows x64 / C# / **HybridCLR 9.0.3**。这是可运行的模块化游戏基础框架，附带代码与资源更新示例、自动自检和故障恢复测试。

**阅读代码先看 [代码分类与 AI 入口](CODEMAP.md)**：框架冷启动是 Bootstrap，服务组装是 GameRuntime，示例游戏入口位于 `HotUpdate/Demo/Entry/GameEntry.cs`。AI 协作约束见 [AGENTS.md](AGENTS.md)。Git 源码版本不包含 Player 和已签名包，首次运行先执行下文 Prepare；“立即试玩”适用于完整交付 ZIP。

## 立即试玩

运行 `Builds/HybridCLR/EnergyCollector.exe`。请保持 EXE 和同目录的数据文件完整，不要只拷贝 EXE。

1. 点击 **START EXPEDITION**，在 30 秒内点击收集 5 个能量球。
2. 收集结束后自动保存成绩，并提交到带延迟、超时和幂等处理的模拟排行榜。
3. 返回主界面打开 **SETTINGS**，调整音量或静音，重新启动后设置仍保留。
4. 点击 **STAGE UPDATE**，验证并暂存内置的 V2 更新包；关闭游戏后重新打开。
5. V2 显示 **DOUBLE ENERGY**，能量球由青色变成金色，每球由 1 分变为 2 分。EXE 不变，替换的是热更新 DLL 和 AssetBundle。
6. **RUN FRAMEWORK CHECK** 自动串联游戏流程，并在数据目录输出检查报告和截图。自检会产生两局真实的演示存档，其中第二局用于验证离开结算时取消排行榜请求。

默认数据目录：`%USERPROFILE%/AppData/LocalLow/FrameworkLab/EnergyCollector`。可通过 `--data=绝对路径` 指定独立目录。更新目录按客户端构建指纹隔离，避免不同客户端误用元数据或程序集；同一数据目录禁止多开。

## 框架结构

```text
Assets/Game/
  Contracts/   生命周期、入口、JSON、日志、传输接口；无 Unity 依赖
  Core/        模块依赖、FSM/Procedure、实体管理与作用域、事件、日志、存档、传输与更新
  Runtime/     Unity 多包资源租约、EntityService、UIForm、AudioService、场景与宿主配置
  Bootstrap/   冷启动、AOT 元数据补充、Assembly.Load、入口与清理
  HotUpdate/Demo/  示例业务，按 Entry / Procedures / Entities / UI / Services / Validation 分类
  Editor/Demo/    示例内容生成与完整客户端构建
  Editor/Publishing/  冻结基线检查与独立热更发布
  Content/     V1/V2 资源；不挂载热更新组件
  Scenes/      Boot（唯一内置场景）
  HotScenes/   V1/V2/V3 的 Lobby / Arena，通过场景 AssetBundle 发布
Assets/Resources/game-config.json
Assets/StreamingAssets/Baseline/     已签名 V1
Assets/StreamingAssets/DemoUpdate/   已签名 V2（演示下载源）
Tests/         直接编译 Core/Contracts 源码的 .NET 测试程序
Tools/         构建、自检脚本
TestResults/   测试结果、截图、日志与验收报告
```

依赖方向：`Contracts ← Core ← Runtime`；`Bootstrap` 与 `HotUpdate` 使用稳定层，**Bootstrap 不引用 HotUpdate asmdef**。入口类型来自签名清单，通过 `IGameEntry` 调用。业务程序集被构建过滤器排除出客户端，必须在启动时加载。

加载热更新入口后，先调用 `RegisterModules` 注册业务模块，再启动 `ModuleHost`。`presentation`、`scenes`、`entities`、`audio` 依赖 `resources`，`ui` 依赖 `presentation/resources`，`demo.gameplay` 依赖 `entities/ui/audio/scenes`，`game.entry` 依赖全部模块。逆序异步停止与释放，入口最后启动、最先停止，模块启动后禁止新增注册。

模块管理没有每帧轮询。`DemoController.Update → ProcedureManager.Tick → Fsm.Tick` 只调用当前已就绪流程的 Update；输入与倒计时属于战斗流程。切换时先取消并等待旧流程任务，再释放订阅与视图、等待 Exit，最后进入新流程。生命周期交错、统一退出及扩展约束见 [架构说明](ARCHITECTURE.md)。

实体通过 `EntityService` 创建、查询和回收，流程持有 `EntityOwner`。能量球旋转、拾取、防重复和得分规则由热更新的 `EnergyOrbLogic` 处理；流程退出会取消加载，等待实体任务并统一回收所属实体。设计与扩展示例见 [实体管理说明](ENTITY.md)。

场景通过逻辑地址从热更新资源包 Additive 加载，退出流程时先清理实体、UI 和音频，再卸载场景。大厅伴随球使用实体 Attach/Detach；空闲实例支持过期、容量、优先级、锁定和分批淘汰。新功能与 API 见 [场景、挂载与缓存](WORLD.md)，最终审查见 [WORLD-REVIEW](TestResults/WORLD-REVIEW.md)。

## 打开源码

在 Unity Hub 中添加本目录，使用 Unity 2022.3.62f1 打开。项目附带 HybridCLR 9.0.3 和 UGUI 源码，保留各自许可证。先执行菜单：

1. `Game Framework / 1 Prepare Demo`
2. 打开 `Assets/Game/Scenes/Boot.unity`，点击 Play。

Editor 使用已编译业务程序集，**不验证 DLL 运行时替换**。更新包、AOT 元数据和完整热更新必须在 IL2CPP Player 中验证。

本机原有 Unity Package Manager 对空工程也报 `The "path" argument must be of type string`。为不修改该组件，提供 `Tools/Run-Unity.ps1 -Offline`：在 `LocalOnly/OfflineProject` 创建同源隔离工程，把官方依赖放入 Assets，并以 `-noUpm` 编译。普通安装不需要此选项。隔离工程自动复制回构建产物、场景、资源和设置；不要编辑镜像源码。

## 构建与验证

在项目根目录使用 PowerShell：

```powershell
# Core/Contracts 测试，需要 .NET 9 SDK
dotnet run --project Tests/Framework.Tests.csproj

# 标准构建：需要 Windows IL2CPP Support、VS C++ 工具链和 Windows SDK
./Tools/Run-Unity.ps1 -Action BuildHybrid

# 仅在本机包管理器异常时使用同源隔离构建
./Tools/Run-Unity.ps1 -Action BuildHybrid -Offline

# 十六次启动：V1/V2、自检、失败回退、取消重试、损坏修复、普通退出
./Tools/Test-Demo.ps1 -Backend HybridCLR

# Editor Play Mode：界面、流程、实体、资源、音频和退出清理
./Tools/Test-Editor.ps1 -Offline

# 不重建客户端，独立发布并验证 V3 升级与不兼容发布拒绝
./Tools/Test-Publish.ps1 -Offline

# Mono 仅用于快速排查，不能替代 HybridCLR 验收
./Tools/Run-Unity.ps1 -Action BuildMono -Offline
```

`BuildHybrid` 配置热更新程序集 → 安装项目专用 HybridCLR libil2cpp → `Generate/All` → 生成裁剪后 AOT 程序集、link.xml、泛型引用及桥接代码 → 构建两版资源与 DLL → 签名 → 打包 IL2CPP。

自动化使用独立数据目录。除 V1/V2、失败回退、普通失败和退出清理外，还验证首次启动失败后重试、首次/候选启动取消后重试，以及 V1 资源损坏、V2 资源和清单同时损坏后的同版本修复；修复前后存档字节和版本历史必须相同。Demo 自检包含错误类型资源请求不泄漏租约的检查。完整通过后写入 `suite-result.json`。

## 热更新设计

- RSA-2048 / SHA-256 验证清单来源；每个文件按大小及 SHA-256 校验。
- 签名覆盖原始 JSON 字节的 Base64 payload，不依赖重新序列化后的字段顺序。
- 严格检查客户端指纹、目标平台、递增序列号、路径、文件上限及程序集依赖。
- 下载 `.part` 支持本地源和 HTTP Range；服务器忽略 Range 时从头写入，避免错误追加。HTTP 仅允许 localhost，其他远端必须 HTTPS。
- 下载到独立 staging 目录，提交前再次校验全部文件；拒绝 payload 与 `.part` 临时路径及清单内部文件名冲突。状态通过同卷原子替换保存。
- 运行中只暂存。下次冷启动先验证文件，补充匹配的 AOT 元数据，再按依赖顺序加载 DLL。
- 游戏入口完成初始化且取消检查通过后才提交健康版本。已有健康版本时，异常候选在下次启动被隔离；首次安装没有健康版本时允许每次重启重新验证后重试，不在同一进程自动循环。正常取消并完成清理后保留候选供下次启动，不能在当前进程卸载 DLL 回滚。
- 保存 `active/previous/pending/booting/failed` 及已验证的 `activeManifest` 副本。已生效内容损坏时，Bootstrap 在加载程序集前从内置或配置更新源修复同一个签名版本；不自动回退存档。修复必须使用原清单和原内容，逻辑变更仍需发布新版本号。
- AOT 的 `SaveStore<T>` 负责原子写入与备份恢复；主文件缺失也检查备份，恢复时保护有效备份并写回原始格式。热更新层的 `SaveData` 负责 schema 1→2 迁移及未来版本拒绝。数据库不在此框架范围。

`remoteDirectory` 留空时使用随包附带的 `DemoUpdate`；也可以填写本地目录或 HTTPS 目录 URL，目录下包含 `manifest.json` 和所有清单文件。冷启动会检查已配置远端，断网时继续使用本地已验证版本。

Demo 的 `LocalOnly/demo-signing-key.xml` 仅供本机重新生成演示包，**不会进入 Player**。正式发行必须使用构建系统管理的私钥与独立公钥，不能沿用演示密钥。

## 扩展方法

- 新玩法：在 `HotUpdate` 添加业务代码，通过 `GameRuntime` 获取明确的服务，不从全局容器查找对象。
- 新实体：继承 `EntityLogic<GameObject>`，在 OnShow 接收本次数据，OnRecycle 清空临时状态；通过 `Entity.Scope.Run/Subscribe` 登记任务与订阅。流程取得 `EntityOwner` 后调用 `ShowAsync`。自身请求回收使用 `RequestHide`，外部可以等待 `HideAsync`，详见 [实体管理说明](ENTITY.md)。
- 新流程：继承 `ProcedureBase`，构造注入所需业务服务，加入入口创建的 `ProcedureManager`。通过 `scope.Run` 登记异步任务、`scope.Subscribe` 登记事件、`scope.Own` 登记资源。流程内用 `RequestChange<T>(scope)` 请求切换，不能在自己的任务里等待 `ChangeAsync` 或 `StopAsync`，否则会被拒绝以防自等待死锁。
- 新模块：实现 `IModule`，在热更新入口的 `RegisterModules(IModuleRegistry)` 中注册，声明依赖；异步工作放入 Initialize/Start，Stop 等待在途工作结束，Dispose 释放资源。构造与注册阶段不要获取活跃资源。
- 新资源：扩展 `DemoBuilder` 的 `AssetBundleBuild` 配置，通过 `ResourceService.LoadAsync<T>` 获取租约并释放；实例释放后再释放其资源租约。
- 新界面：继承 `UiFormLogic`，通过 `OpenFormAsync` 绑定流程作用域；窗口内请求 `RequestClose`，外部可等待 `CloseAsync`。任务和订阅归窗口 Scope 管理。
- 真后端：在热更新层实现 `IScoreTransport`，由入口组装并传入 `DemoServices`，替换模拟排行榜；服务器负责持久化幂等键、鉴权和防作弊。
- 独立热更发布：完整构建导出冻结客户端基线，之后使用 `Tools/Publish-Release.ps1` 仅编译兼容代码与资源、校验并签名。命令、兼容限制与 V3 验收见 [扩展功能说明](EXPANSION.md)。

## 日志与边界

JSONL 日志位于数据目录 `Logs/game.jsonl`，含时间、级别、模块、操作、关联 ID、耗时、异常、客户端和代码版本。单文件约 1 MB 后滚动，保留 3 个历史文件。启动配置区分开发与发布级别。关键字和 Bearer 令牌脱敏仅是补充防线，调用方仍不得主动记录敏感数据。

当前只承诺 Windows x64 基线；Android/iOS/WebGL 需要单独适配传输、存储与构建配置。资源管理采用签名地址表和多包依赖图，懒加载、依赖闭包租约与空闲逐包卸载；模块关闭等待加载/卸载操作。AOT 元数据演示包保留全部匹配的裁剪后程序集，方便验证但体积较大，可基于泛型分析结果收敛。

本轮新增功能涉及 AOT 的 Core、Runtime 和 Bootstrap，必须完整升级客户端一次，不能只投放热更新 DLL。客户端指纹会隔离旧更新缓存；原 `save.json` 模型保持兼容。后续添加兼容的业务流程、实体逻辑与资源可在 HotUpdate 中进行，不需要改变 ModuleHost。

更新前及健康启动后自动清理不受保护旧版本和过期暂存，检查缓存上限和 Windows 实际磁盘剩余量；保护当前/上一版/候选/启动中版本。预算按当前客户端指纹计算。修复源必须保留该版本原始签名包；旧状态没有 activeManifest 且安装清单也损坏时，框架会拒绝推测兼容基线。当前健康门槛是入口就绪，不覆盖任意数小时后的业务故障或断电下的存储硬件保证。人工鼠标试玩、不同 DPI、长时间压力测试及真实 CDN 验收需另行执行。

发布、UI、多包资源和音频见 [扩展功能说明](EXPANSION.md)；最新场景与实体扩展见 [WORLD](WORLD.md)、[最新验收](TestResults/WORLD-VALIDATION.md)。历史验证记录保留在 `TestResults/VALIDATION.md`。
