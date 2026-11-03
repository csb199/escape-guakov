# 验收记录

## 最新源码整理：Demo 分类与 AI 阅读入口

16 个脚本移动并保留 GUID，全部 C# 执行代码保持一致；177 项核心回归和 Editor 62 项检查通过。新目录与入口见 [CODEMAP](../CODEMAP.md)，本轮范围见 [整理验收](ORGANIZATION-VALIDATION.md)。本轮未重建 Player，下面的完整构建结果与交付 ZIP 仍属于整理前快照。

## 最近完整构建：场景热更、实体挂载与缓存淘汰

客户端 `unity2022.3.62f1-win64-il2cpp-9ba9e45782fea0ca`：核心 177 项、Editor 和 V1/V2/V3 Demo 各 62 项通过；最终播放器 16 次恢复/退出验证与独立发布 3 次升级验证通过。V3 新场景包独立发布，EXE/GameAssembly 未改变；42 个 Game C# 源文件与实际编译工程逐一核对一致。

详见 [最新验收](WORLD-VALIDATION.md)、[最新 Review](WORLD-REVIEW.md) 和 [功能与 API](../WORLD.md)。以下均为历史记录，旧指纹和检查数量不代表当前客户端。

## 历史：五项框架扩展

当前客户端为 `unity2022.3.62f1-win64-il2cpp-43331affbb0660e2`。核心 155 项、Editor 和各版本 Demo 46 项均通过；播放器 16 次恢复/退出场景与独立发布三次升级场景通过。V3 通过冻结基线独立发布，EXE/GameAssembly 保持不变，错误基线被拒绝。

详见 [本轮验收](EXPANSION-VALIDATION.md)、[本轮 Review](EXPANSION-REVIEW.md) 与 [功能说明](../EXPANSION.md)。以下记录保留历史构建与测试数量，不代表当前客户端。


## 历史：五项审查问题修复

2026-11-03 后续修复已完成；当前客户端指纹为 `unity2022.3.62f1-win64-il2cpp-600f0b53b11d6f1a`。核心 131 项通过，最终 Windows IL2CPP 构建成功，HybridCLR 播放器 16 次启动通过，完整游戏自检为 27 项；Editor Play Mode 同样通过 27 项与退出清理。新增验证涵盖首次失败/取消重试、已生效版本和清单损坏后的同版本修复，以及错误资源类型不泄漏租约；修复前后存档字节保持一致。

详细行为、证据与边界见 [五项修复验收](FIVE-FIXES.md)，播放器证据在 `HybridCLR-20261103-142810/`。以下保留的是此前实体改造的历史验收记录，其中旧客户端指纹、101/26 项与七次启动均不代表当前构建。

## 历史：实体管理改造

日期：2026-11-03（Asia/Shanghai）。最终基线：Unity 2022.3.62f1、Windows x64、HybridCLR 9.0.3、IL2CPP、NVIDIA RTX 3060 / Direct3D 11。

本次记录对应 EntityService 实体管理改造后的重新构建，保留 P1 与 FSM / Procedure 修复；客户端指纹：`unity2022.3.62f1-win64-il2cpp-2de44567e4074df4`。根工程与实际编译镜像的全部 Game C# 源文件已逐一比较哈希，一致。

## 已通过

| 检查 | 结果 | 证据 |
|---|---|---|
| Core / Contracts 与纯 C# 业务源码测试 | 101 项通过，0 失败 | `core-tests.json` |
| Unity C# 编译和 Windows IL2CPP 构建 | 成功 | 项目根目录 `BuildHybrid.log`，包含 `DEMO_BUILD_SUCCEEDED` |
| Editor Play Mode 完整 Demo | 26 项通过；退出清理通过 | `Editor-20261103-121843/demo-report.json`、`Editor-20261103-121843/shutdown-check.txt` |
| HybridCLR V1 冷启动与全流程 | 26 项通过 | `HybridCLR-20261103-121843/v1-report.json` |
| 暂存 V2，重启加载代码与资源 | 26 项通过 | `HybridCLR-20261103-121843/v2-report.json` |
| V2 加载后、就绪前故障恢复 | 预期失败；下一进程恢复 V1 并通过 26 项检查 | `HybridCLR-20261103-121843/recovery-report.json` |
| 普通启动失败，无 self-test 分支 | 预期退出码 1；延迟 Stop 完成，清理通过 | 同目录 `NormalFailure/failure.txt`、`async-stop-completed.txt`、`shutdown-check.txt` |
| 普通退出，无 self-test 分支 | 退出码 0；wantsToQuit 等待延迟 Stop 完成 | 同目录 `NormalQuit/async-stop-completed.txt`、`shutdown-check.txt` |
| 模块退出清理 | 订阅=0、资源租约=0、UI=0、实体实例=0、实体加载=0 | 同目录 `Data/shutdown-check.txt`、`Recovery/shutdown-check.txt` |
| 游戏画面 | 已检查 V1 青球、V2 金球截图，布局正常 | `HybridCLR-20261103-121843/v1-arena.png`、`v2-arena.png` |

最终 IL2CPP 验收共启动七次：正常 V1、正常 V2、恢复测试的 V1、预期失败的 V2、恢复后的 V1、普通启动失败、普通退出。失败注入发生在动态程序集加载、游戏入口启动之后；V2 的失败版本号持久化为隔离状态，V1 仍为健康版本。最后两次通过热更新注册一个 Stop 延迟 250ms 的模块，检查完成标记与清理标记，防止进程在异步停止前退出。

`HybridCLR-20261103-121843/suite-result.json` 保存七次运行的总结果及实际 EXE、GameAssembly.dll 的 SHA-256；交付脚本核对二进制哈希后打包本次证据。此前 Mono 七次启动验证亦通过（`Mono-20261103-121155`），最终结论以本轮 HybridCLR 和 Editor 结果为准。

P1 阶段新增的 19 项继续通过：10 项生命周期交错/幂等/超时/失败清理、7 项热更注册与入口所有权、1 项独立存档模型、1 项可替换排行榜传输。停止超时后保留依赖资源是有意行为，不能宣称任意不合作模块都能被安全强制关闭。

前一轮的 21 项 FSM/Procedure 回归继续通过：当前流程单独 Tick、切换与停止等待任务、作用域取消、事件屏蔽与释放、过期回调、重复请求、新作用域、自等待防护、部分 Enter 失败以及清理异常传播。对应游戏自检覆盖旧流程延迟任务和离开结算时取消排行榜请求。

本轮新增 23 项实体核心回归：ID 与组查询、容量限制、逻辑生命周期、实例复用与旧句柄隔离、加载取消与迟到结果释放、流程拥有的实体回收、实体任务等待、部分显示/隐藏失败，以及实体任务对自身和父流程关闭的自等待防护。Demo 自检从 20 项扩展至 26 项，实际经过 Unity 实例化、射线交互、流程自动回收与 HybridCLR 实体逻辑更新。每个球连续点击两次，确认不会重复计分。

V1 每球 1 分，V2 每球 2 分；自检通过场景摄像机投射射线拾取全部 5 个球，校验实际事件数与得分。V2 同时加载另一份 AssetBundle，材质校验与截图证明资源也发生变化。

## 功能覆盖

- 实体：EntityService 按 ID、分组管理加载与实例池；EnergyOrbLogic 负责旋转、拾取、音效和得分规则；流程退出取消并等待实体任务，回收所属实体，旧句柄不能操作复用实例。
- 流程：LobbyProcedure→BattleProcedure→ResultProcedure→LobbyProcedure；只 Tick 当前流程，异步 Enter/Exit，取消并等待旧任务，释放订阅与所属 UI 后进入下一流程。日志含 entering / entered / leaving / exited 与待完成任务数。
- 模块：热更入口先注册、依赖排序、循环和缺失依赖拒绝、部分初始化失败清理、逆序停止与释放；启动/停止交错、重复停止、停止超时和异常均有回归。
- 配置：宿主参数在 AOT；目标数量、时限、容量由热更资源中的 `demo-config` 提供并校验，用于真实运行。
- 日志：JSONL、关联 ID、版本字段、异常、基础敏感字段脱敏、滚动限容量。
- UI：设置按钮、弹窗层、音量 Slider、静音按钮、关闭按钮实际回调。
- 场景：Lobby→Arena→结算→Lobby，多轮切换。
- 资源：AssetBundle 加载、租约持有、停止后释放；代码与资源版本配套。
- 实体实例池：按分组、资源和逻辑类型复用；容量限制、跨场景复用与退出回收。每个实例持有预制体租约，关闭服务时全部释放。
- 事件：拾取发布、计分订阅、场景结束释放、异常订阅隔离上报。
- 存档：AOT 泛型存储支持独立业务模型；热更层负责成绩与设置、v1→v2 迁移及未来版本拒绝，损坏备份恢复通过。
- 网络：模拟排行榜、幂等重试、超时、取消；真实 localhost HTTP 的 206 续传和 200 重下载。
- 热更新：RSA-2048 签名、SHA-256 完整性、客户端/平台兼容、路径安全、下载中断恢复、待生效版本、冷启动激活、失败恢复和隔离。

## 验证边界

- 本机 Unity 包管理器初始化失败，因此实际编译使用 `Tools/Run-Unity.ps1 -Offline` 创建的同源镜像；正常包管理器路径未在本机验证。最终项目保留标准 Packages/asmdef 结构。
- 桌面 UI 自动化返回 `GetCursorPos failed: 拒绝访问`，未完成真实鼠标人工试玩。按钮回调、UI 控件状态、摄像机射线拾取和离屏图像已验证；这些不能代替不同分辨率/DPI下的人工验收。
- 已实际验证 Windows IL2CPP / HybridCLR，未验证 Android、iOS、WebGL。
- 模拟排行榜没有真实认证、服务端持久化或防作弊。未对公网 CDN、TLS 异常和长时间压力进行验收。
- 故障恢复测试是就绪前异常并退出进程，尚未覆盖强制断电、文件系统硬件故障或就绪数小时后的任意业务故障。
- 示例私钥仅供本机演示，不进入 Player 或交付 ZIP；正式发布需独立密钥与发布流程。
- 生命周期依赖协作取消。强杀进程和 Editor 手动停止 Play 不由 Player 的受控退出路径保证；模块启动永不结束时也不能保证安全关闭。
- DemoController 的流程逻辑和自检已拆出，Demo 界面改为按作用域关闭所属窗口；详见 `ARCHITECTURE.md`。新增 EntityService AOT 能力需要完整升级客户端，不能仅发布新热更新 DLL 给旧客户端。
- Entity 本轮不包含父子 Attach/Detach、加载优先级队列和按时间淘汰；详细对应关系见 `ENTITY.md`。
- FSM 与实体只管理通过作用域登记的任务与资源；未登记的 fire-and-forget 工作不受生命周期保护。无视取消且永不完成的任务会阻止切换，框架不会强制释放其资源。

复现：先运行 `dotnet run --project Tests/Framework.Tests.csproj`，再运行 `Tools/Test-Demo.ps1 -Backend HybridCLR`。
