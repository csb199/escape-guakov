# 游戏框架逐项审查与扩展建议

> 历史报告：其后已实现独立发布、更新缓存预算、UIForm、多包资源、音频，以及热更新场景、实体挂载与缓存淘汰。当前状态以 [扩展说明](EXPANSION.md)、[场景与实体说明](WORLD.md) 和 [最新 review](TestResults/WORLD-REVIEW.md) 为准；下文的缺功能描述保留为历史证据。

日期：2026-11-03。审查基线：Unity 2022.3.62f1 / Windows x64 / HybridCLR 9.0.3。

> 后续状态：本报告保留修复前的审查证据。用户选定的问题 1～5 已完成修复，当前实现和验证结果以 [五项修复验收](TestResults/FIVE-FIXES.md) 为准；LocalOnly/ArchitectureAudit 是修复前的缺陷复现程序，当前回归入口为 Tests/Framework.Tests.csproj。

本项目通过冷启动热更加载游戏入口，串联大厅、收集战斗、结算、存档、模拟排行榜和资源回收。按当前单机小型游戏、默认 5 个实体、配置最多 100 个战斗实体的需求评估，不按大型联网项目假设增加模块。

**结论：架构主干合理，可以继续扩展；还不能把现有 Demo 的通过结果等同于完整的故障恢复能力。优先修复更新与存档恢复，然后完善发布工具。当前不需要推翻架构，也不需要一次补齐 UnityGameFramework 的全部模块。**

本次仅审查，未修改运行时代码、现有测试和构建产物。新增独立复现程序、复现结果和本报告。

## 1. 当前架构及职责

```text
Bootstrap                         冷启动、验签、加载程序集、启动与关闭
  ├─ Contracts                    稳定入口、模块、日志、传输契约
  ├─ Core                         无 Unity 引用的生命周期、FSM、实体核心等
  └─ Runtime                      Unity 资源、场景、UI、实体适配与组装

HotUpdate → Contracts / Core / Runtime
  ├─ GameEntry / DemoServices      注册业务模块、业务配置和存档
  ├─ Lobby / Battle / Result       组织流程、拥有订阅与异步任务
  └─ EnergyOrbLogic / DemoView     实体行为、界面展示

ModuleHost                        依赖排序、初始化、启动、反向停止释放
DemoController.Update
  └─ ProcedureManager.Tick
      └─ 当前流程.Update
          └─ 当前 EntityOwner.Tick → 活跃实体.OnUpdate
```

- AOT 程序集没有直接引用热更新程序集类型，通过 `IGameEntry` 加载入口，依赖方向正确。
- `ModuleHost` 不承担每帧业务调度；流程切换和模块启停是独立职责。
- 流程退出先取消并等待已登记任务，再逆序释放所属资源，最后执行 Exit，完成后才进入下一流程。
- `EntityService` 管创建、查询、显示、隐藏和实例池；`EntityLogic` 管自身行为；流程通过 `EntityOwner` 管理所属实体。旧句柄不重新生效。
- 当前流程通过继承分派行为，不是入口里用一个大 switch 硬写所有游戏状态。
- 实体使用统一管理器和实例池，但底层 Tick 仍遍历全局实体字典筛选 owner，详见问题 6。

**与 UnityGameFramework 的关系：**沿用了 FSM/Procedure、Entity 管理器/逻辑/分组、资源复用的职责划分；增加了适合当前项目的异步退出等待与流程所有权。不是直接移植，也不宣称接口和功能完全相同。上游的实体挂载、池淘汰、资源依赖管理等尚未全部实现。

## 2. 功能逐项检查

| 功能 | 当前实现 | 判断与边界 | 下一步 |
|---|---|---|---|
| 启动与热更入口 | Bootstrap 验证配置、清单、加载代码，入口注册模块 | 主路径完整；首次启动中断恢复有缺陷 | 修问题 1 |
| 模块管理 | 明确依赖、拓扑排序、循环检测、反向停止、部分失败清理 | 合理；只启动一次；停止超时保留活跃任务可能使用的依赖 | 保持，不加模块轮询 |
| FSM / Procedure | 当前流程 Tick，异步 Enter/Exit，切换期间暂停 Tick | 符合流程解耦；重叠请求拒绝、不排队 | 保持并明确调用规则 |
| 作用域与异步 | Run、Subscribe、Own、OwnAsync；取消后等待完成 | 对已登记工作有效；无法终止无视取消且永不完成的任务 | 新功能沿用作用域 |
| 实体管理 | ID、分组、容量、查询、逻辑回调、加载取消、owner 回收 | 已形成通用实体核心，不是简单 Instantiate 包装 | 先优化问题 6，再按需求补挂载 |
| 对象池 | 实体按组、资源、逻辑类型复用；旧 handle 隔离 | 当前需求已覆盖；缓存直到服务退出，无定时淘汰 | 删除重复旧池，见问题 7 |
| 资源 | 单 AssetBundle、地址缓存、租约计数 | 满足当前小包；租约释放不意味着单资源立即卸载；存在问题 4 | 修计数；多包需求出现后升级 |
| 场景 | 串行 Single 加载；取消时等 Unity 操作完成 | 当前大厅/战斗足够；不可中途撤销加载；场景来自客户端 Build Settings | 暂不加世界流式加载 |
| UI | 分层、按 ID 开关、设置弹窗、流程拥有 DemoView | 满足 Demo；尚无通用 UIForm 实例生命周期、异步 prefab 窗口管理 | 正式多窗口需求时扩展现有服务 |
| 配置 | 宿主配置与热更业务配置分离，有字段校验 | 合理；当前是 JSON 配置，不是数据表系统 | 多表关联出现后补表工具 |
| 日志 | JSONL、等级、模块/操作/关联 ID、耗时、版本、堆栈、滚动 | 当前可用；基础字符串脱敏，不能视为任意结构的全面脱敏；文件同步写 | 禁止高频逐帧日志；接真实认证时补字段策略 |
| 事件 | 类型化订阅、可释放订阅、异常聚合 | 合理；Unity 主线程约定；一个订阅失败不阻断其他订阅 | 不把直接业务调用全部改成事件 |
| 存档 | 泛型机制与业务模型分离，原子写、备份、迁移、防未来 schema 覆盖 | 方向正确；恢复后备份与主文件缺失处理不完整 | 修问题 2 |
| 设置与音频 | 音量/静音存档，Demo 直接管理 AudioSource 与 AudioClip | 当前可用；不是完整音频服务 | 多类音频时新增 AudioService |
| 输入 | 鼠标点击、UI 遮挡判断、射线交互 | 当前 Windows 鼠标玩法够用；没有动作映射/重绑定 | 键盘角色控制或手柄需求时补输入适配 |
| 网络 | IScoreTransport + Mock；超时、取消、稳定请求 ID 重试 | 符合“不假设后端协议”的原需求；不是联网游戏协议栈 | 接真实后端时增加具体适配器 |
| 更新传输与安全 | RSA-2048、SHA-256、平台/客户端兼容、路径限制、续传和重试 | 基本链路完整；临时文件与包文件命名空间冲突 | 修问题 5 |
| 版本激活与恢复 | pending/booting/active/previous、就绪提交、失败隔离 | 已验证“已有健康旧版本时候选失败”；其他恢复情况不完整 | 修问题 1、3 |
| 构建与发布 | 一键 Demo V1/V2 包、HybridCLR 生成和 Windows 构建 | Demo 工具完整；不是正式独立热更发布工具 | 发布真实内容前补发布工具 |
| 验证 | 101 项核心检查，历史 Editor/IL2CPP 全链路验证 | 基础良好，但遗漏本次恢复场景 | 把复现转换为修复回归 |

“功能存在”与“可以支撑任意游戏”是两回事。当前满足小型 Demo 的大部分需求；多窗口、多包资源、手柄、联机等能力没有隐含实现。

## 3. 必须优先修复

### 1. [P1] 首次启动未确认健康后，内置版本被永久隔离

- **位置：** [Updates.cs:129](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Updates.cs:129)、[Updates.cs:162](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Updates.cs:162)、[Bootstrap.cs:76](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Bootstrap/Bootstrap.cs:76)。
- **功能与问题：** 未就绪的候选版本在下次启动被隔离。如果是全新安装，BeginBoot 后、MarkHealthy 前进程中断，恢复时 active/pending 均为空；Bootstrap 再次暂存 baseline 被拒绝，并且还没走到远端检查。
- **实际结果：** 独立复现得到 `Failed release is quarantined; publish a new version`，没有可启动版本。普通受控退出若发生在同一窗口且留下 booting，也会进入这一逻辑。
- **最小修复：** 为“无健康版本”定义恢复分支：区分已完成清理的正常取消与启动故障；允许经过明确策略的内置包重新验证/有限重试，并保证仍可获取修复版本。继续隔离真正失败的远端候选，不能简单清空所有 failed 或跳过验签。
- **不修后果与验收：** 一次启动中断即可让后续普通启动持续失败。补全首次失败、首次启动期间受控退出、恢复后获取修复版本三种回归；原有 V2→V1 隔离回退不能退化。

## 4. 应该修复

### 2. [P2] 存档恢复没有维护后续备份，主文件缺失时也忽略备份

- **位置：** [Persistence.cs:35](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Persistence.cs:35)、[Persistence.cs:18](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Persistence.cs:18)。
- **功能与问题：** Load 从 .bak 恢复后仍保留损坏的主文件；下一次 Save 的 File.Replace 把坏主文件放进 .bak。另外，主文件不存在时直接返回默认值，即使备份有效。
- **实际结果：** “主文件损坏→读取备份得分 7→保存”后，备份内容变成 `broken`；只删除测试主文件、保留得分 7 的有效备份，Load 返回默认得分 0。两条均已复现。
- **最小修复：** 主文件缺失也进入有效备份检查；备份恢复后原子修复主文件，或在下一次写入时保护已验证备份。修复过程中不得用坏文件替换唯一有效副本，保留未来 schema 拒绝策略。
- **不修后果与验收：** 一次成功恢复后失去可用备份，第二次损坏无法恢复；主文件丢失时会表现为进度归零。增加连续恢复/保存/再次损坏及仅备份存在的测试。

### 3. [P2] 已生效版本损坏后，没有修复路径

- **位置：** [Updates.cs:163](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Updates.cs:163)、[Updates.cs:219](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Updates.cs:219)。
- **功能与问题：** BeginBoot 只在 pending 与 active 同时存在时处理候选校验失败；active 本身损坏直接抛错。StageAsync 也先读取校验 active，因此同版本重下载或更高版本更新都会先被坏 active 阻断；previous 目前只记录、不用于恢复。
- **实际结果：** V1、V2 依次健康启动，破坏 V2 的资源文件后，BeginBoot 报完整性错误；V1 仍可独立校验通过。此项已复现。
- **最小修复：** 优先提供同版本签名内容修复，或受控获取新版本的启动恢复分支。若选择回退 previous，必须先确认存档兼容；不能仅凭旧包存在就盲目回退。
- **不修后果与验收：** 已下载的有效旧包不能帮助恢复，普通更新入口也无法修复损坏。增加 active 损坏后的修复测试，以及存档不兼容时拒绝自动回退的测试。

### 4. [P2] 资源类型请求错误会泄漏租约计数

- **位置：** [UnityServices.cs:56](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Runtime/UnityServices.cs:56)。
- **功能与问题：** 缓存仅按路径查找；返回前先执行 `references++`，再 `(T)value` 强制转换。例如先缓存 `LoadAsync<GameObject>("orb")`，随后误用 `LoadAsync<AudioClip>("orb")`，第二次抛异常前已经加计数，却没有返回可释放的租约。
- **证据：** 源码确定的执行顺序；当前 Demo 均用正确类型，不会触发。本轮未在 Unity 中新增该错误类型用例。
- **最小修复：** 在增加计数前校验/转换资源类型，类型不匹配返回含路径与类型的明确错误。无需为了修复此问题引入另一套资源框架。
- **不修后果与验收：** 一次调用错误会污染全局计数，随后 ResourceService.Dispose 因“仍有租约”失败。补 Unity 测试：错误调用前后 ReferenceCount 相同，释放合法租约后能正常关闭。

### 5. [P2] 合法清单中的文件名可与下载临时文件冲突

- **位置：** [Updates.cs:70](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Updates.cs:70)、[Updates.cs:186](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Updates.cs:186)。
- **功能与问题：** 验证器允许包同时包含 `hot.dll.part` 与 `hot.dll`，下载后者时会把前者当作断点续传临时文件。逐文件校验通过并不保证目录提交时所有文件仍完整。
- **实际结果：** 使用独立测试密钥生成合法签名包，StageAsync 成功，但 BeginBoot 报 `File integrity rejected: hot.dll.part`。现有 DemoBuilder 固定文件名不触发，此项是自定义发布包时的边界缺陷，不是绕过签名执行代码。
- **最小修复：** 对临时文件名/内部元数据名做冲突验证，或将临时文件放在独立目录；提交版本目录前再次校验完整文件集合。
- **不修后果与验收：** 特定合法命名的更新包会被错误报告为暂存成功，启动时才失败。补冲突文件名、恢复下载、完整目录提交的测试。

## 5. 可顺手优化

### 6. [P3] 活跃流程更新实体时仍扫描全局实体并每帧分配数组

- **位置：** [Entities.cs:156](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Entities.cs:156)、[Entities.cs:274](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Core/Entities.cs:274)、[DemoView.cs:22](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/HotUpdate/DemoView.cs:22)。
- **功能与问题：** 每次 owner.Tick 都通过 LINQ 筛选全局实体并 ToArray；诊断 UI 同时逐帧统计和拼接文本。只更新当前流程的职责没有问题，但集合访问仍有额外遍历与分配。
- **最小修复：** owner 保存自身活跃集合，并使用能安全处理显示/隐藏的可复用快照或延后变更；诊断文本按变化或低频刷新。不要直接遍历会在回调中修改的集合。
- **不修后果与验收：** 默认 5 个实体规模下没有证据表明存在明显卡顿；实体/owner 数量增加后会放大遍历与 GC 成本。用 Unity Profiler 对比每帧分配及最大配置负载，不据此直接引入 ECS。

### 7. [P3] 旧 GameObjectPool 已被实体实例池替代

- **位置：** [UnityServices.cs:84](D:/nkg-ai-flow-main/unity-game-framework/Assets/Game/Runtime/UnityServices.cs:84)。
- **功能与问题：** 当前源码、测试及项目配置搜索仅见该类声明和构造函数，没有使用入口。类中还保留 `OrbPool` 业务名称，与新的通用实体管理形成两套生命周期入口。
- **最小修复：** 删除该 33 行旧实现，业务实例统一使用 EntityService；如果以后确有非实体池需求，再根据实际对象类型设计。
- **不修后果与验收：** 新代码容易继续选择旧池并绕过实体 owner 的异步退出管理。删除后运行已有测试与 Unity 编译即可，不需要为删除类另写镜像测试。

可直接精简约 **33 行运行时代码、0 个依赖**；本次尚未执行删除。

## 6. OOP 与框架思路判断

| 设计点 | 判断 |
|---|---|
| 流程、实体逻辑使用虚方法扩展 | 合理，行为扩展位置明确，不需要所有玩法继续塞进 DemoController |
| 泛型 EntityManager + Unity provider | 合理，核心可脱离 Unity 测试，Unity 实例化职责集中 |
| GameRuntime 显式组装服务 | 当前规模合理；是可见依赖的组合入口，没有全局静态万能单例 |
| 业务直接使用具体 ResourceService/UiService | 不自动构成 OOP 问题；有替换实现、独立测试隔离需求时再引入窄接口 |
| DemoContext 聚合业务依赖 | 当前可读；后续新功能只传所需依赖，避免逐渐成为全局杂物容器 |
| UiService 包含按钮/面板拼装工具 | Demo 方便，但美术布局工具不宜长期占据稳定 AOT 服务；做正式 UIForm 时把示例布局移入业务/视图 |
| Demo 捕获/验证契约在 Runtime/Contracts | 属于示例设施；制作可复用框架包时再抽出 Demo/Diagnostics，不值得现在重建所有程序集 |
| “一项能力必须是一个 IModule” | 不成立。IModule 表示需要受控启停的参与者，纯函数、数据模型、简单工具不需要伪造生命周期 |

以下是当前明确边界，不作为本轮已证实故障计数：

- FSM 的 Update 异常由业务处理，通用 FSM 不自动停机或恢复；生产接入前要确定“记录、停止当前流程、显示错误/重试”的统一政策，不能依赖 Unity 每帧反复报异常。
- 流程自己发起跳转用 RequestChange；外部编排用 ChangeAsync。切换期间请求会被拒绝，不能将它当作自动排队的流程调度器。
- Entity provider 当前 Instantiate 后才 SetActive(false)。扩展带 MonoBehaviour 的 active prefab 时，OnEnable 可能早于 EntityLogic.OnShow；必须明确组件初始化契约，不能在 OnEnable 假定业务数据已经注入。若要保证初始化后才激活，应增加非激活创建路径及 Unity 生命周期测试。
- 模块/FSM/实体 API 按 Unity 主线程使用；后台回调接入时需切回主线程，框架当前没有通用跨线程调度保障。
- 回退的是代码与配套资源，不会自动撤销已写入的新 schema 存档。当前 V1/V2 Demo 共用兼容 schema；未来不可逆迁移需另定恢复策略。

## 7. 是否新增功能模块

**当前建议新增运行时模块：0 个。** 先把现有生命周期、持久化和更新承诺补完整。下一阶段最必要的新增能力是发布工具；音频、输入、UI 则由真实玩法决定。

| 能力 | 是否现在增加 | 最小范围与触发条件 |
|---|---|---|
| 独立热更发布工具 | 发布正式内容前必须补 | 固定已发布客户端的 clientBuild、AOT 元数据与兼容基线；输入新代码/资源版本和递增 sequence；校验后签名导出新目录；用旧安装客户端验收。现 DemoBuilder 固定 V1/V2 且重新计算客户端指纹，只适合演示构建 |
| 启动修复界面/操作 | 随恢复修复一起补 | 展示失败阶段，提供可执行的重试/修复入口及日志位置；仍由 Bootstrap/UpdateEngine 负责，不另造全局 RepairManager |
| AudioService | 有 BGM、UI 音效、战斗音效的不同生命周期时加 | 音频分组/音量、播放句柄、停止与租约归还；可作为 IModule 依赖 resources；不把伤害、拾取规则移入音频服务 |
| 输入适配/动作映射 | 增加角色移动、手柄或重绑定时加 | 将设备输入转成玩法动作；只激活当前流程的动作映射；优先复用 Unity Input System，不重复造底层设备系统 |
| UIForm 生命周期 | 增加背包、商店、多层弹窗或异步 prefab UI 时补 | 扩展现有 UiService：实例句柄、OnOpen/OnClose、窗口作用域、资源租约、同层排序；支持流程拥有的窗口和全局窗口 |
| 游戏时钟/计时工具 | 有暂停、慢动作、重复计时需求时加 | 明确缩放时间与真实时间；计时任务属于流程/实体 scope；仅当前倒计时用 deltaTime 已足够 |
| 数据表与导入校验 | 多张表、ID 引用、策划批量编辑时加 | 配置生成/校验工具优先；业务 schema 留在热更层，不提前引入大型表框架 |
| 多包资源与缓存淘汰 | 出现多个大关卡、按需下载或内存预算时加 | 地址目录、包依赖、包租约、未使用包回收；决定升级现有 ResourceService 或替换为一套资源方案，不能并行维护两套 |
| Entity Attach/Detach | 有武器挂点、角色子实体、跟随特效时加 | 明确父子关系、父隐藏时子处理、回收解除挂载；它是 EntityService 扩展，不是新的全局模块 |
| 真实后端适配 | 后端协议确定后加 | 实现 IScoreTransport 或实际业务接口；认证、错误码、服务端幂等按协议实现，不预造万能网络模块 |
| ECS、复杂 DI、全局任务调度、万能消息中心 | 不建议现在加 | 当前没有足够的性能或依赖复杂度证据；会增加接入成本和生命周期维护面 |

新增通用能力若落在 AOT/Core/Runtime，需要升级客户端；不能假定只发布业务 DLL 就能给旧客户端增加这些 API。新能力接入后，仍按 `ModuleHost → 服务生命周期`、`Procedure → 玩法组织`、`Scope → 所属任务和资源` 的分工验证。

## 8. 推荐实施顺序

1. 修问题 1，并增加首次安装失败后再次启动的播放器回归。
2. 修问题 2、3，明确“修复同版本”与“兼容回退”的区别。
3. 修问题 4、5，补资源租约错误路径与包文件冲突回归。
4. 删除问题 7 的旧池；问题 6 在扩展实体数量前优化并测量。
5. 制作独立发布工具，以不重装旧客户端的方式发布并验证 V3。
6. 确定下一款游戏的实际玩法后，再选择 Audio、Input、UIForm 等能力，每次新增一项并在 Demo 中串联验证。

## 9. 验证证据与未检查范围

- **本轮核心回归：** `dotnet run --project Tests/Framework.Tests.csproj`，101 项通过、0 失败；[结果](D:/nkg-ai-flow-main/unity-game-framework/TestResults/core-tests.json)。
- **本轮边界复现：** 独立工程直接编译当前 Contracts/Core 源码，5/5 场景复现预期缺陷；[复现结果](D:/nkg-ai-flow-main/unity-game-framework/TestResults/ArchitectureAudit/probe-results.json)。这表示缺陷得到确认，不表示功能通过验收。
- **复现程序：** [Program.cs](D:/nkg-ai-flow-main/unity-game-framework/LocalOnly/ArchitectureAudit/Program.cs)，运行 `dotnet run --project LocalOnly/ArchitectureAudit/AuditProbe.csproj -- TestResults/ArchitectureAudit`。测试密钥仅在内存中生成，测试数据写入独立目录。
- **历史播放器证据：** [VALIDATION.md](D:/nkg-ai-flow-main/unity-game-framework/TestResults/VALIDATION.md) 中的 Editor 26 项、HybridCLR 七次启动属于前一轮验收，本轮没有重新构建/运行 Unity 播放器。
- 已检查项目入口、全部自有运行时主要实现、asmdef、配置依赖、构建和测试入口、核心与 Demo 验证覆盖；参考了本地 UnityGameFramework/GameFramework 的 FSM、Procedure、Entity 实现。没有审计第三方引擎/包源码全量、原生二进制、每个自动生成文件。
- 未进行本轮 Unity 错误类型资源测试、带自定义组件 prefab 的生命周期测试、Profiler 压力测试、公网 CDN/真实后端测试、断电测试或其他平台验证。相关静态推导和扩展边界已分别标注。
- 正式发布工具当前缺失是能力边界；单包、单鼠标、简单 UI 是需求取舍，不应统一算作 bug。本报告未把这些取舍和未验证猜测混入已复现缺陷。

**最终判断：保留现有分层和 FSM/Procedure/Entity 主干，先修恢复链路，再完善发布能力，按具体玩法扩展模块。**
