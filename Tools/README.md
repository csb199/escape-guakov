# 自动化工具


在项目根目录运行 PowerShell 脚本。默认 Unity 路径可通过相应参数覆盖；整备室验证的离线选项也支持根工程编辑器仍打开时使用隔离镜像。

头部渲染校对：`Audit-BarrackHairPasses.py` 由 qrenderdoc --python 执行，使用 `BARRACK_HAIR_CAPTURE`、`BARRACK_HAIR_OUTPUT`、`BARRACK_HAIR_EVENTS` 指定输入、输出和事件；事件设为 auto 时匹配本机原作头部字节码。`Match-BarrackHairVariants.py` 通过 --audit、--bundle 和可选 --unitypy 解析全部匹配变体，禁止用单值哈希映射推断唯一关键词。`Test-BarrackHairCapture.py --source <原作 passes.json> --host <项目 passes.json> --output <结果.json>` 校对字节码、深度/模板、混合及相对顺序。GPU 原始资源保留于 LocalOnly，不提交仓库。

## 阅读入口

M1：Prepare-M1Transport.ps1 校验固定网络依赖并构建 .NET 实验；Test-M1Transport.ps1 启动真实服务器和正常/非法客户端。Test-M1Player.ps1 启动一个 Unity 无画面服务器及两个自动客户端（默认批处理，不作GPU渲染验收），可用 Seconds/Latency/Loss 设置时长和收包延迟/丢包；Latency 是每端接收方向的毫秒延迟，不是实测 RTT。结果在 LocalOnly/M1，必须按源码版本重新构建后执行。

M1后续验收使用独立UDP代理（Experiments/M1/UdpImpairment.cs），不再用SDK内置组合注入。Test-M1Player 支持 Il2Cpp/Dedicated、30ms Jitter、BlackoutSeconds=5/30及EnforceBudgets；后者执行纠正、模拟tick和带宽门槛，GPU不可用仍保持待验收。Test-M1Matrix 默认各600秒，可IncludeSoak追加1800秒；默认1080p。Test-M1Protocol 对真实IL2CPP服务器做版本、身份、范围、重复、旧会话检查。Build-M1Native 支持同步镜像构建与既有热更回归；Write-M1Evidence 写全包摘要。Measure-M1Processes 保存进程角色、内存/CPU采样；Test-M1Memory 校验预热后的峰值和末段增长。Test-M1Player的RenderFrames移除客户端batchmode，用于单独渲染诊断。Start-M1Manual 提供双窗口手感验收，关闭两个窗口后停止本次服务器，不把服务器签名密钥发给客户端。

M0 复现：`Sync-GameSource.ps1 -Project <根工程> -DestinationProject <根工程/LocalOnly/新镜像>` 可同步到新的隔离工程；默认仍为 OfflineProject。目标限定在根工程 LocalOnly 中并拒绝重解析点。此工具只同步 Assets/Game，其余配置与依赖需按 [M0 最终验收](../M0最终验收.md) 的复现步骤准备，不复制 Library 缓存。

授权资产盘点：`Audit-M0Assets.py --source F:/gfresource --output <LocalOnly 下输出目录> --verify-groza` 统计文件与索引候选，并抽查 Groza 战斗动画数据。基础扫描仅依赖 Python 标准库；动画抽查使用来源目录 tools/python 中的 UnityPy，禁止写入该目录的字节码缓存。输出必须在来源目录之外，名称命中不代表 Unity 适配完成。

| 文件 | 职责 |
|---|---|
| [Run-Unity.ps1](Run-Unity.ps1) | Prepare / BuildMono / BuildHybrid / Open |
| [Publish-Release.ps1](Publish-Release.ps1) | 指定冻结基线、版本、输出和签名密钥，独立发布 |
| [Test-Demo.ps1](Test-Demo.ps1) | Player 功能、版本恢复及退出验证 |
| [Test-Editor.ps1](Test-Editor.ps1) | Editor Play Mode 功能与退出验证 |
| [Test-Publish.ps1](Test-Publish.ps1) | 独立升级和不兼容基线拒绝验证 |
| [Sync-GameSource.ps1](Sync-GameSource.ps1) | 镜像同步 Assets/Game，清理离线镜像旧路径 |
| [Package-Delivery.py](Package-Delivery.py) | 核对验证结果与哈希，生成源码/Player 交付 ZIP |

新完整交付包必须对应当前构建和验证证据，不能用旧哈希替代新源码验证。

源资源接入：`Export-BarrackSource.py` 提取已解析资源中的原始布局、组件证据和缺失依赖；不负责恢复可执行游戏逻辑。范围见 `TestResults/Barrack-source-import.md`。

`Inspect-Gff.py` 检查 GFF 文件目录；`Inspect-GrozaTables.py` 提取角色配置字段证据；`Prepare-GrozaImport.py` 生成本机依赖计划。当前缺口和验证边界见 `TestResults/Groza-dependency-trace.md`。

## 原作整备室资源恢复

- Index-GfResourceSegments.py：解析 GFF 40 字节映射记录中的物理包、偏移和长度，索引每个 UnityFS 分段及其 CAB；长度为零的自身映射表示独立包。旧的逐物理文件扫描会遗漏后续分段。
- Resolve-GfResourceClosure.py：按精确 CAB 导出递归依赖，检查区段边界、目录与实际 CAB，记录 SHA-256 和缺口。输出留在 LocalOnly。
- Prepare-GrozaImport.py --resolved-closure：从默认 prefab 的 MeshResList 和演出源的精确网格引用生成绑定计划，保留 64 位 PathID 字符串，提取原纹理像素数据和原生库路径。
- Export-BarrackRenderContract.py：保存原管线、RendererFeature、PC 后处理与默认场景配置；静态配置不能证明运行时选中了哪个质量档或混合权重。

根工程 Editor/Barrack 的 GrozaSourceImporter 负责持久化角色资源；GrozaPersistenceProbe 只生成诊断图，不代表原作画风验收。

- Test-BarrackEditor.ps1：Unity 6000.3.27f2 根工程整备室播放验证，结果和实帧截图输出到 LocalOnly/Barrack/live；运行前关闭该根工程的编辑器实例。Test-Editor.ps1 保留签名框架 Demo 的完整编辑器验收路径。


- Export-BarrackEnvironment.py：从默认背景实际引用的 Volume 和 PC 管线导出环境/后处理配置；保留源证据，宿主计算实现另见 Rendering。
- Export-BarrackInteraction.py：从当前角色导入计划选定的源层级及本机 GunData/BarrackInteractData 导出 CameraPoint、触摸胶囊、跟随关系和动作表；原游戏只读，输出各角色 interaction-contract.json。运行 BarrackRosterImporter.Prepare 将契约绑定到场景与展示预制体。
- Audit-BarrackAnimationContracts.py：核对完整名单的原生动画/控制器对象载荷、待机循环标记、控制器动作绑定及源交互表字段；结果写入 LocalOnly/Barrack/animation-audit。配合 BarrackAnimationContinuityValidation.Run 的自然帧骨骼与镜头返回检查使用，不以状态名或静态引用代替实际播放验证。
- Sanitize-BarrackScene.py：只处理项目副本，移除原客户端脚本组件及其预载引用，保留场景引擎对象与 PathID，记录输入/输出 SHA256。不得对原游戏文件使用同一路径覆盖。
- BarrackFullValidation 运行两轮完整交互和效果差分，另做一次加载中取消；同时检查 StateScope 先等待宿主关闭再释放外层所有权。Tests/Framework.Tests.csproj 包含纯 C# 动作状态机回归。

## 批量角色资源预处理

- `Resolve-BarrackMeshBindings.py`：对默认下一批 FlorenceSSR01、ZhaohuiSSR01、ClukaySSR01 的声明路径、Avatar 骨骼路径哈希及静态 bindpose 做结构校验。结构匹配不替代原作资源目录映射；异常与多候选不会自动晋升为可展示角色。
- `Export-BarrackNextBatch.py`：依据最新审计与绑定证据，将原生包、纹理原始像素/色彩空间、完整材质属性及主体/镜头/辅助动作清单导出至 `LocalOnly/Barrack/next-batch-export`。动作类别按包与命名推断，仍需验证目标绑定；不改写源资源或角色列表。
- `Recover-BarrackBatchDependencies.py`：读取导出清单中的缺失 CAB，通过已有 GFF 分段索引和精确 CAB 校验恢复本机物理包。恢复后重新运行审计、绑定校验和导出；不将“闭包完整”当作“网格映射和渲染验证完成”。

以上工具依赖本机 `F:/gfresource/tools/python/UnityPy` 和已生成审计；矩阵核对另需 numpy。角色名可作为绑定与导出脚本的位置参数。二进制产物留在 LocalOnly，供 Editor 持久化导入前核验。当前批次说明见 `TestResults/barrack-next-batch-export-2026-11-12/README.md`。

- `Find-BarrackSegmentMeshes.py`：从完整逻辑分段索引查找未进入旧物理包索引的两张面部 Mesh；原始包只读，匹配包导出至 LocalOnly，逐段日志可续扫。
- `Verify-BarrackGpuMeshes.py`：对照 Florence 已验证 RDC 导出的完整索引、UV/颜色流及可解码权重流，记录实际绘制与原生 Mesh 的对应。不能把变形后的 GPU 顶点替代原始带骨骼 Mesh。
- `Select-BarrackCompatibleMeshes.py`：在声明路径、Avatar 骨骼顺序和静态矩阵校验基础上选择接入候选；多候选必须仅在 bindpose/骨骼包围盒不同，按最小静态矩阵误差选择并保留替代项。它是宿主兼容选择，不声称恢复了原作 catalog 地址。

- 经截帧核验的限定范围例外记录在 `Assets/Game/Content/BarrackSource/mesh-selection-overrides.json`。选择器仍要求骨骼/矩阵结构合法、CAB/PathID/assetKey 精确匹配及源 Mesh 完整数据 SHA256 一致；数据变化即拒绝。发布器将记录复制到角色的 `mesh-selection-contract.json`，保留资源身份未唯一确定、证据来源和适用动作范围。例外不表示角色运行验证已通过。

- `Audit-BarrackMeshEquivalence.py`：细分同名网格差异，解码实际顶点通道；骨骼索引、权重、UV、拓扑须精确一致，位置/法线/切线/颜色只允许记录的 5e-7 绝对差。表情差异按原生动画绑定解码验证恒零；活动差异的可选动作列为排除项，基础动作存在差异则拒绝。只生成审计，不自动批准或发布角色。当前限定选择和仍需补证清单见 `TestResults/barrack-equivalence-import-2026-11-13/README.md`。


M1完整原生验收入口Test-M1Acceptance.ps1：可见1080p渲染预算、150ms附加RTT/3%丢包/±30ms抖动、5/30秒断线、九组各600秒、1800秒长稳，统一管理内存采样的启动和停止，并检查源码/原生文件未变。矩阵优先执行历史失败条件；仅所有步骤及30个进程区间预算通过后写suite-result.json。相同PID按进程启动时间区分，避免长测中PID复用混淆数据。



