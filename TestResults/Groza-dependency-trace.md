# 闪电资源依赖追踪

更新：2026-11-12。当前状态：**资源分段解析阻塞已解除，默认闪电模型与原生 Shader/动画库已导入根工程并在隔离 Unity 6 绘制；默认整备室完整场景、原作渲染管线/后处理及根工程 Unity 2022 验收仍未完成。**

以下 2026-11-11 小节保留为历史排查记录；其中“缺失 CAB/材质/面部网格”的结论已被 2026-11-12 的完整分段索引替代。

## 已确认的来源

原作 LocalCache/Data/Table/GunData.bytes 的角色记录指向 GrozaSR；本地化键 6246 在 LangPackageTableCnData.bytes 对应“闪电”。ModelConfigData.bytes 的 ID 1017 指向 GrozaSR01。更早使用的 GrozaR01 探针不能证明默认 GrozaSR01 的完整度。

当前可定位的 GrozaSR01 层级位于 ef3f19c1bd1006e547ce667d8f91d74c.bundle 的演出预制体 43044_01 内，并非已确认的独立整备室模型预制体。不能将其直接宣称为原作默认整备室成品。

## 精确缺口

- 面部 LOD0：CAB-ed9ad605f255b45be9a712856594039b，PathID 4808751214775136403；当前索引未定位。存在多个同名 face_lod0，但不能仅凭同名替换。
- 身体/服装材质：CAB-eae661eceff268e7d3a73ff32a85729f；引用 PathID -3046185854627964259、-2912705777568031443、7777731156654439647。
- 头发/面部材质：CAB-8dfbd4eec15b760169556bf05d740ade；对应引用仍未解决。
- 整备室动作包 74805d6a0134834b9bc296cefba5bc13.bundle 已定位，包括 BarrackIdle、Entrance、Touch、Inspect、Stretch 与镜头动作。未完成动画绑定和实机对照验收。

## 新排查结果

- GFF 已解析。08dfe7d89b6fe56375d6dfec87ffcc8a.bundle 包含 69,611 条、每条 40 字节的索引记录；三个 .d 文件保存名称映射或校验字符串。它们不是未导出的模型包。
- 已定位基础版和热更新版代码目录，以及 BarrackCharacterTouchController、BarrackCameraManager、BarrackTimelineManager 等类型。类型存在不等于方法语义已恢复。
- 两版 Addressables 的 ContentCatalogData.LoadFromFile 在隔离 Unity 6 执行均报 InvalidProgramException（无效 IL）；不能直接复用 DLL 解析目录。
- 定制 UniversalRenderPipelineAsset 在隔离 Unity 6 实例化时触发编辑器原生崩溃，未得到可用管线对象。此结果只说明本次直接移植方案失败，不证明在原作引擎内不可用。未向根工程导入这些 DLL。
- 基础 resources.assets 等文件尚未成功解析；其用途与缺失对象是否位于其中仍待验证。

## 工程改动

- Tools/Inspect-Gff.py：只读 GFF 索引解析，检查目录与数据范围。
- Tools/Inspect-GrozaTables.py：解析角色相关配置的字段级证据。
- Tools/Prepare-GrozaImport.py：生成本机依赖计划；本次 18 个已解码包、71 个未定位 CAB（不计 Unity 默认资源），此范围与源场景的 164 个引用不同。
- Editor/Barrack/GrozaSourceImporter.cs：受预检约束的源模型恢复入口，当前会在缺少面部网格时失败，写资产前退出；还不具备原作材质/Shader 持久化能力，遇到真实材质也会拒绝保存临时包引用。**本次未产出完整角色 prefab。**
- HotUpdate/Barrack：模块、流程及角色动画持有适配；SceneService、EntityService、ResourceService 与 StateScope 管理所有权。动作播放为直接切换，未实现原作完整状态机。未注册进默认入口，未修改 AOT 层或发布契约。

## 验证

- GFF 实际索引解析成功，69,611 条记录全部通过范围检查。
- 配置表重新解析与保存的证据逐字节一致。
- 使用现有 Contracts/Core/Runtime/HotUpdate 源码及项目 UGUI 在隔离 Unity 6000.3.27f2 编译通过：BARRACK_ADAPTER_COMPILE_PASS。
- 模型预检实际失败：Source LOD0 meshes unresolved: c_GrozaSR01_slg_face_lod0；确认没有生成目标资产目录。
- 未运行根工程 Unity 2022 的既有 Editor/Player 验收；本机未找到项目指定编辑器。没有角色显示、原作画风、后处理、完整构建或热更通过结论。

## 运行观察限制

本次窗口枚举只找到 PCLauncher，未找到原作游戏窗口；启动器两次窗口捕获分别出现 FrameArrived timed out 与 window capture timed out。未点击不可观察的界面，未取得运行时资源绑定或帧捕获。后续需要原作停在闪电默认整备室进行运行时对照，不再要求用户手工寻找 CAB。

详细日志位于 LocalOnly/Barrack，均不提交。

最终检查：新增源恢复预检保护后再次完成隔离编译；日志 adapter-final-compile.log。Packages/manifest.json 仅增加 Unity 内置 animation 模块依赖，未修改第三方包实现。

## 2026-11-11：用户进入默认整备室后的运行检查

已观察到 GF2_Exilium.exe 游戏窗口，Player.log 包含 GrozaSR01(Clone) 的 Animator 重绑定记录；资源包仍为 9,840 个，没有相对已有索引新增的包。两次窗口捕获均超时。只读 OpenProcess（查询信息与读取资源内存）返回 Win32 错误 5。随后仅使用 PROCESS_QUERY_LIMITED_INFORMATION 与 TokenElevation 查询，确认游戏进程 elevated=true，而检查工具 elevated=false。未修改游戏进程或提升权限。

此轮仍未获得运行时材质绑定、面部网格或渲染帧数据，不能宣称完成角色展示或原作管线移植。下一步需要用户使游戏与检查环境处于相同权限级别，再验证只读访问；权限一致并不保证其他兼容问题已解决。本机检查脚本与结果位于 LocalOnly/Barrack/live。

## 2026-11-12：资源分段根因与独立绘制验证

普通权限启动原作后，只读访问恢复。当前原作日志确认 GrozaSR01；没有注入或修改原作进程。窗口捕获仍连续超时，未取得原作帧对照。

### 缺失依赖根因

原物理 .bundle 文件常包含多个逻辑 UnityFS 分段，旧解析只读取第一段。GFF 的 40 字节记录依次包含 16 字节物理包标识、32 位小端偏移、32 位长度、16 字节记录哈希；零长度且逻辑名等于物理名的记录表示独立文件。哈希字段语义尚未验证，不将其当作完整性校验。

Index-GfResourceSegments.py 对 69,611 条记录完成 UnityFS 目录、区段边界和长度检查，零错误。身体材质 CAB-eae661eceff268e7d3a73ff32a85729f、头发/面部材质 CAB-8dfbd4eec15b760169556bf05d740ade、渲染依赖 CAB-ed2f918160b43247ec86605e1564d98d 的磁盘分段与进程中的完整包逐字节一致。面部 LOD0 的原 CAB 和 PathID 4808751214775136403 已恢复。三个先前关键渲染/后处理 CAB 都已定位。

完整解析得到：整备室 UI/管线/模型联合静态依赖计划 1,220 个包、零缺失；默认模型与动作/精确网格证据计划 287 个包、零缺失；真实场景计划初版 728 项、零缺失。后者包含同物理场景包多个 CAB 的重复导出，工具现已按实际 CAB 集合去重，因此数量不是唯一物理包数，也不是当前可见对象数。

### 角色产物与持久化陷阱

已定位独立默认 GrozaSR01 prefab：CAB-8197543835efac73430dc329e0fea24c，地址 7dc1252d9ecc94a49828b58d0d50a41c。网格由原 MeshResList 动态绑定，不能仅加载 prefab。使用配置路径、精确 CAB/PathID/container key 恢复五个 LOD0 网格，逐项核对演出源与默认模型骨骼顺序一致。

根工程 Assets/Game/Content/BarrackSource/GrozaSR01 包含 5 个网格、204 个骨骼引用、10 个材质、21 张原格式/mip 贴图、Avatar、4 个 Shader 元数据资产，以及原生 Shader 和动画库。原库中识别 11 段整备室动作。

实测 Unity 编辑器克隆并保存编译 Shader 会丢失 D3D 字节码，克隆/改写压缩 AnimationClip 会丢失曲线，流式贴图直接克隆会丢像素。已用原始像素重建贴图，用原生 .bytes 包保留 Shader 与动画。Shader 元数据资产仅提供材质引用，必须通过 BarrackShaderLibrary 绑定原库才能绘制；动画必须通过 BarrackAnimationLibrary 取原对象，禁止重新序列化为空 .anim。Animator.fireEvents=false 防止执行原游戏回调，释放时恢复。

新进程只读取已保存模型、贴图及两份原生库，成功绘制闪电原待机姿态；原 Idle 时长 2 秒且非空。诊断图 LocalOnly/Barrack/live/final-resource-diagnostic.png **不是最终渲染**：未应用完整原作环境光、阴影、后处理，画面明显偏暗；无原作帧对照。没有据此宣称画风一致。

### 默认场景与渲染契约

真正场景已定位为 Assets/ArtsResource/Scene/Aimo/04Aimo_Training_Platform/04Aimo_Training_Platform_GFPC.unity（CAB-6d4a13e19b845a96b4a0d3b9a0355f5c）。BarrackCharacterCanvas 是 UI/相机层，不能替代该房间。其 _Light_Weapon 分支默认 inactive，相关灯光/Weapon 后处理不能当作角色默认光照。

真实场景 _LightSetting/_PostEffect 精确引用 CAB-29933d6c11d21cd4b00268198a92fda7 / 5645129384652940861。render-contract.json 保存该 profile 及其精确组件引用，同时保存原管线和 RendererFeature，共 190 项静态配置，读取零错误。同包其他 profile 不标作默认配置。静态资产存在不证明运行时质量档和混合权重。

### 框架接入与本次验证

BarrackProcedure 通过 ResourceService 持有原生库 TextAsset 租约；StateScope 先释放角色/PlayableGraph/实例材质，再释放动画库、Shader 库和场景。库加载等待完成后响应取消；失败绑定会回滚材质。未修改 AOT 层或注册默认 Demo。

隔离 Unity 6000.3.27f2 + Windows D3D11：
- 最终角色导入执行成功；新进程独立绘制非空。
- 框架原生库集成测试通过：材质绑定/恢复、2 秒非空 Idle、缓存、预取消。日志 native-integration.txt 标记 NATIVE_LIBRARY_INTEGRATION_PASS。
- 分段数据与运行时三包完全一致，越界读取拒绝，Python 工具语法检查通过。
- 根工程指定 2022.3.62f1，本机仍只有 Unity 6；未完成该版本重导入、场景运行、Player/热更验收。

当前仍需完成真实房间的可发布场景恢复、原作定制 SRP 调度与全局常量、阴影/反射/后处理、UI 与动作状态机、原作画面对照。资源解析与角色原生资源绘制已推进，整体任务没有完成。
