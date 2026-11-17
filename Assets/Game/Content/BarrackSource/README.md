# 整备室源资源与证据

此目录保存解析证据、闪电资源及真实默认房间依赖。2026-11-12 已通过完整 GFF 分段索引补齐原 164 项 CAB 缺口；旧 layout.json 的缺口统计只代表当时的扫描范围。

- GrozaSR01：五个角色网格、骨骼、材质、原纹理、原生编译 Shader 和 11 个压缩动作。
- Stage：真实 04Aimo_Training_Platform_GFPC 场景及 727 个去重后的依赖包。Stage.asset 定义加载顺序与源场景路径；environment.json 记录该场景引用的环境/后处理参数；camera-config.json 记录源虚拟相机参数。
- Stage/BarrackPresentation.prefab：通过框架实体和资源租约装载的展示宿主；原生背景仍为运行时 additive 场景。
- Rendering：Unity 6 深度和后处理适配 Shader。它们是宿主实现，不是原作 Shader 源码。
- SourcePresentation：源 UI 节点/组件证据、可执行布局契约及原图。Tools/Export-BarrackPresentation.py 可从本机依赖包和中文表重建；源脚本仅解析，不执行。
- GrozaEvidence：依赖、材质及渲染契约的解析证据。
- layout.json / components.json：BarrackCharacterCanvas 的 99 节点、251 组件导出记录，不代表原作全部 UI 已恢复。

源游戏目录只读。Stage 中的场景包是独立副本，移除依赖原客户端脚本的组件并保留引擎对象及 PathID，处理记录见 scene-adaptation.json。原始脚本数据是配置证据，不作为可执行指令。

当前接入名单以 `roster.json` 为准；角色目录包含原生模型、材质、纹理、Shader/动画库以及交互和颜色契约。每轮验收范围、未解决依赖和实帧记录在对应 TestResults 报告中，不能把已导入资产数量当成全部角色已可展示。完整发布与原作实机画面对照另行记录。

`mesh-selection-overrides.json` 记录有截帧证据的限定范围网格选择。角色的 `mesh-selection-contract.json` 保留证据摘要、源数据哈希、适用动作和 `resourceIdentity: unresolved` 标记；此标记不能被导入成功或运行测试通过替换为原作资源身份已确定。
