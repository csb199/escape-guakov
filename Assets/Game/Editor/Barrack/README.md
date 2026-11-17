# 整备室编辑器入口

开发基准为 Unity 6000.3.27f2 / Windows D3D11。

- BarrackStartup：打开 Assets/Game/Scenes/Barrack.unity 并设置编辑器默认播放场景。签名客户端保留 Boot 冷启动契约。
- BarrackStageImporter：创建源场景依赖定义、源环境配置、Unity 6 管线和 BarrackPresentation.prefab，将开发宿主接到真实默认背景。
- BarrackFullValidation.Run：两轮播放、UI 动作、骨骼触摸、镜头、冻结状态的效果开关差分和资源退出检查；使用 StandardRequest 真正提交 GPU 绘制，不能用仅有非空引用替代画面验证。
- BarrackPlayValidation.Run：保留早期角色资产回归入口，不代表完整环境验收。
- BarrackReloadRecovery：播放中编译脚本时先取消并释放整备室资源，退出播放后才允许程序集重载；重载完成重新进入并恢复角色选择，避免保留 UI 却丢失交互状态机和 PlayableGraph。动作从入场重新开始；编译失败时保持停止。
- BarrackTouchPlaybackValidation.Run：通过 EventSystem 射线验证点脸 Look 拉近、自然结束自动恢复镜头以及返回按钮；检查实际待机骨骼变化、动画过渡中立即返回的权重与姿态连续性，并覆盖旋转缩放后的返回、实际脚本重编译和所选角色恢复。结果及近景实帧位于 LocalOnly/Barrack/touch-playback；该批处理入口完成后退出编辑器。
- BarrackAnimationContinuityValidation.Run：逐角色使用自然帧等待入场、待机跨循环、Look 与 Head 完成，检查动画时间、骨骼变化、镜头位置/旋转/FOV 自动恢复和租约释放；不通过采样时间或强制结束动作代替正常播放。结果位于 LocalOnly/Barrack/animation-continuity。源契约审计入口为 Tools/Audit-BarrackAnimationContracts.py，逐字节核对导入片段，检查循环标记、控制器动作绑定与交互表映射。
- BarrackSourceImporter / GrozaSourceImporter：按 CAB 和精确 PathID 导入布局、网格、材质、纹理；编译 Shader 与压缩动画保留原生包数据，Shader 占位文件只承担可序列化引用。

执行 Tools/Test-BarrackEditor.ps1，日志及实帧保存在 LocalOnly/Barrack/live。编译、编辑器展示与完整发布验收是不同范围。

`BarrackHairValidation` 纳入完整回归，在冻结姿态下独立关闭头发投影、面部阴影、透明刘海，并检查实际像素变化；透明刘海的可见贡献依赖视角和模板覆盖，因此包含多个环绕角度。检查结束恢复材质 Pass、镜头与 TAA。`Tools/Test-BarrackEditor.ps1 -Offline` 通过 Sync-GameSource 同步隔离镜像并运行相同检查，输出位于 LocalOnly/OfflineProject/LocalOnly/Barrack/live，适用于根工程编辑器仍打开的情况。

在已由 RenderDoc 启动的 D3D11 验证进程中设置 `BARRACK_VALIDATE_HAIR_GPU=1` 可捕获头部验证帧；捕获边界使用目标纹理所属的 D3D11 设备，避免 Unity 多设备环境下捕获为空。普通回归不加载 RenderDoc。`Audit-BarrackHairPasses.py` 导出 GPU 状态和字节码，`Match-BarrackHairVariants.py` 保留全部同哈希变体，`Test-BarrackHairCapture.py` 比较实际通道、模板链和绘制顺序。

实时窗口回归：菜单 `Game Framework/Barrack/Probe Live Flicker` 在当前 Game View 采样 224 个连续帧，覆盖正常播放、冻结并开启 TAA、关闭 TAA、关闭 AO 四段。检测期间会临时冻结动画并切换效果，结束或退出播放时恢复效果；不重定向相机、不退出编辑器。结果写入 `LocalOnly/Barrack/flicker`，通过 `Tools/Analyze-BarrackFlicker.py` 校验连续帧与黑帧。该检查补充离屏测试，不能替代原作效果一致性验收。

角色导入与名单：`Tools/Prepare-BarrackCharacterImport.py` 以审计的 CAB/PathID、MeshResList 绑定和容器键生成导入计划，GrozaSourceImporter 现在接受 variant 与精确子节点路径，可在隔离导入工程持久化指定角色。保留原生 Shader/动画库，纹理从原始 payload 恢复，并生成颜色空间契约。`BarrackRosterImporter.Prepare` 读取显式 `roster.json`，将已导入角色、各自动画集合和头像写入默认 Barrack 场景及 BarrackPresentation.prefab。`BarrackRosterValidation.Run` 使用实际 UI 按钮与 GPU RenderRequest 验证切换，不以仅有引用代替画面检查。

`GrozaSourceImporter.UpdateVerifiedMeshes` 按已核验计划更新现有 Mesh 资产内容并保留 GUID，用于分段恢复后替换旧候选；应在停止播放后执行。主体动画只读取指定主动画库和 animationNames，辅助道具与镜头曲线不自动挂到角色骨骼。导入计划优先采用显式 verified-bindings.json，退回审计的精确 renderer 引用需单独核对骨骼顺序。

环境光连续性回归：`BarrackLightingValidation.Run` 使用正常 Camera 帧采集 Dusevnyj/Faelynn 的待机与入场，重复切换，验证源 8×27 SH 系数、探针区域边界连续性及待机亮度步长；结果写入 `LocalOnly/Barrack/lighting-<GF2_LIGHTING_LABEL>`。`Tools/Export-BarrackLightProbes.py` 根据解析场景 `_ArtNode.lightProbes` 引用导出原作数据，不重新烘焙或估算系数。

批量接入工具：`Find-BarrackMissingAssets.py` 按指定目标名和资产类型扫描逻辑分段，恢复精确 CAB 并保留可续扫日志；`Resolve-BarrackMeshBindings.py` 同时核对骨骼顺序、完整绑定姿势及 Avatar 静止姿势，Avatar 缺少路径表条目时使用源 Mesh 的路径 CRC32 进行精确比较。`Select-BarrackCompatibleMeshes.py` 拒绝几何或表情数据不同的歧义候选，不把资源文件存在当成绑定已确认。

`Import-BarrackRemaining.py` 逐个执行隔离原生导入，失败记录到角色日志；`Publish-BarrackRoster.py` 将成功原生导入结果、源头像、中文名和纹理颜色契约写入根工程，之后必须运行 Editor 验收。`Export-BarrackInteraction.py --characters <ID...>` 可重建指定角色契约，默认保留已有契约；源容器与子对象同名时仍必须使用明确的子树路径。

`BarrackRosterValidation.Run` 按实际名单逐个滚动到卡片并切换，核验源 CharForward 层掩码、原生 Shader、动作、网格变形、实例所有权和资源退出；每个角色保存有/无 UI 的实帧。自动非黑屏检查不能替代角色逐张视觉复核。历史同名嵌套根的迁移入口为 `BarrackRosterImporter.NormalizeNestedSourceRoots`，迁移保留资产 GUID；`RunAfterImport` 将该修复与名单回归顺序执行。
