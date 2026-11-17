# 整备室业务接入

角色头部通道按闪电帧 8378 的顺序执行：主体前向、头发模板投影、眼部加法/乘法叠加、面部阴影、脸部标记、透明刘海、描边。眼部叠加的源 Shader 队列为 2458/2459，宿主以 2450 分隔前后两段前向绘制。闪电材质契约中的 hostRenderQueue=2440/2441 是宿主排序槽，保证眼睛之后先整片头发再刘海；它们不是声称从 GPU 截帧读取出的原作队列值。面部阴影与透明刘海分别使用独立标签，不再让战斗发丝通道参与首个匹配 Pass 的选择；主相机与反射相机共用该调度。主深度显式使用 D32_SFloat_S8_UInt，保留原生材质的模板引用、读写掩码和混合规则。证据见 GrozaEvidence/hair-render-contract.json；该契约不代表所有角色或最终像素已经逐帧等价。

本目录是在 Unity 6000.3.27f2 / Windows D3D11 上的可替换展示业务，使用现有流程、实体和资源服务，不修改 Contracts/Core/Runtime/Bootstrap。

## 接入契约

- 注册 BarrackModule（依赖 entities/scenes），创建无缓存实体组。BarrackContent.presentationPrefabAddress 指向发布目录中的 BarrackPresentation.prefab；可选 sceneAddress 为宿主场景。所有地址须由既有签名内容发布链提供。
- BarrackProcedure 通过 EntityOwner 加载展示 prefab；StateScope 先取消并等待 BarrackPreview.StopAsync，再释放实体和 prefab 租约。保留分散模型/动画地址模式，供其他展示内容使用。
- BarrackPreview 负责角色实例、原生场景、PlayableGraph、相机和 UI；原生包按内容 SHA256 共享租约。异步加载完成后处理取消，先销毁动画图和材质实例，再卸载场景，最后释放原生包。
- BarrackStageDefinition 引用 727 个去重后的原生依赖包、源环境配置和 Unity 6 管线。载入真实场景后使用其 RenderSettings、LightmapSettings、LightProbes 和反射立方体；退出恢复先前场景及渲染管线。

## 展示与交互

BarrackInteraction 是不依赖 Unity 的动作状态机，串联入场、待机、动作、返回、环绕和关闭；播放中的动作拒绝重复触摸。动作直接使用源压缩 AnimationClip，禁止克隆重序列化，关闭源脚本动画事件。Idle、Entrance、ToIdle 为必需动作，其余动作按当前角色实际动画开放；不借用其他角色的动画填补缺项。基础输入持续播放本角色带循环标记的 BarrackIdle；入场结束从待机起点开始，交互返回沿用持续的待机相位。已导入的 Groza/Tololo AnimatorController 只有状态模板，没有动画绑定，不能以 HasState 成功作为可播放的依据。互动叠加层保留中断时所有仍有权重的输入，在权重归零后释放，避免复用双槽时删掉正在淡出的姿态。

BarrackView 使用源 UI 布局资产、PC 截帧坐标、原始中文字体与图标。左侧只显示 `BarrackPreview.characters` 中的已导入人形，头像按钮异步切换角色，选中框与名称跟随成功提交的实例更新；切换时禁用按钮。战力、等级、属性、技能、强化页签、排序/筛选和其他未接通业务入口不生成显示控件。返回、列表收起、拖动/缩放与已存在的触摸动作可操作；Tab 隐藏界面，F8 显示动作与渲染调试控件。旧 `presentation-snapshot.json` 保留为历史布局/纹理证据，账户数据不参与当前界面显示。

`BarrackCharacterDefinition` 是本展示业务的角色接入数据，包含 prefab、原生 Shader/动画包、材质契约、头像和准确动画名。名单由 `Assets/Game/Content/BarrackSource/roster.json` 显式维护，经 Editor 展示验证后接入场景；中文名称来自原作语言表。左侧三列列表支持滚轮和拖拽滚动。新增角色的原始主体动画按所在包和准确片段名导入；辅助对象片段另行归档。Florence/Zhaohui/Clukay 的口袋互动，以及 Zhaohui 的头部互动，在道具/镜头未绑定前不开放。`BarrackCharacterSession` 拥有候选实例、动画图、材质副本和包租约；验证候选完成后才替换当前实例，失败保留当前角色。切换不卸载整备室场景，重新绑定骨骼目标并清除该相机的 TAA 历史；关闭先取消并等待初始化/进行中的切换，再释放实例、背景与包租约。Phaetusa 两套发束保留源动画的骨骼缩放选择，不根据 Dorm/Fight 名称强改可见性。

浏览由展示宿主拥有的 `VirtualCameraRoot/ShotVirtualCamera` 驱动，角色原始 `CameraPoint` 提供旋转基准。进入浏览保存当前镜头完整姿态，不再从模型包围盒中心和 ScreenX/Y 重算构图；滚轮沿该镜头局部前向移动，使用源执行器的旋转范围、距离范围和阻尼。默认视角点脸先进入本角色的 `Look` 并拉近；动作自然完成或点击返回时，通过 ToIdle 恢复默认镜头与待机。拉近和退出采用源执行器的 0.5 秒、1 秒时长；ToIdle 完成不重启尚未结束的镜头插值。切换和关闭释放旧镜头根节点。只接受同一角色会话内的左键手势，拖拽释放不触发点击，零滚轮事件不改变交互状态。当前是对源片段、触摸表与镜头参数的宿主适配；原作运行时 Timeline 轨道绑定、完整筛选分支和专属道具尚未恢复，不能据此宣称与原客户端全部交互等价。

每个角色的 `interaction-contract.json` 由 `Tools/Export-BarrackInteraction.py` 从实际导入入口与原作 `GunData/BarrackInteractData` 导出。保留 CameraPoint 路径、胶囊形状/局部中心/轴向、武器跟随骨骼、源事件号和动作组。会话内建立跟随骨骼的触摸胶囊，以最近表面命中分派本角色已有动作；未接入动作不替换成其他角色动作。源枚举 LBEEJEDGKEH.Head=1011 表示头部区域，末位不是动作行序号；闪电的 101 组同时含 Head 和 Camera_Look，不能以 id 的末两位固定选择 Head。左右腿保持精确事件 1510/1511，胸部与武器读取各自动作组。闪电源 Animator 的 BarrackEntrance→BarrackIdle 退出时间为 1、过渡时长为 0，基础状态直接交给原生控制器；源控制器不包含触摸状态。脸部序列由宿主协调原生片段与镜头配置，并非加载完整原客户端 Timeline 执行器；账号条件、全部 Timeline 轨道及逐动作范围表仍未全量还原。

`BarrackBrowsingValidation` 纳入现有 `BarrackFullValidation`，覆盖父节点所有权、非零宿主变换、浏览进入不跳位、局部轴缩放、回转半径、复位、源动作映射与输入误触；`BarrackRosterValidation` 覆盖换角时旧手势失效及镜头根唯一性。平面反射使用普通反射视锥做保守剔除，再用斜裁剪投影绘制，避免浏览角度变化使 Unity 的剔除参数提取失败。

## 渲染边界

原始角色/场景材质使用原生编译 Shader。BarrackRenderPipeline 适配 Unity 6 的绘制、主光及附加光阴影、环境球谐、烘焙光照和探针，提供平面反射及后处理。环境参数来自真实默认场景引用的 Volume，包含 Bloom、AO、雾、色调、饱和度和暗角。

HDR 调色通过原作 LutBuilderHdr 与 UberPost 原生 Shader 执行，使用 32 阶 LUT、原作 Unreal Filmic 分支及 Volume 的色调/暗角参数；已删除旧 Film 近似函数。原材质关键字与禁用 Pass 从材质契约恢复。Bloom、Opaque Post 与 UberPost 的实际分支已由截帧字节码哈希核对；执行原生 Bloom 五级下采样/三级上采样、完整四向量雾参数、HDR LUT 与暗角。颜色纹理契约明确 sRGB/Linear，Editor 验证禁止漫反射误导入为 Linear。三 MRT、2×MSAA、1.4 渲染比例与默认镜头取自捕获。

反射接收平面由捕获的顶点、UV、材质参数重建，使用原生 RerlectionAdd 和 2048 平面反射。体积光的透视分支由捕获网格、常量和 Shader 指令重建为 Unity 6 适配 Shader；不是找回了原作完整组件。GTAO 使用原生 ComputeShader 的半分辨率采样、横向及纵向上采样，参数来自事件 2625/2637/2649。TAA 使用原生中等质量分支，在 Bloom/调色之前运行，绑定三 MRT 的运动向量、历史 HDR/亮度/深度及 Halton 抖动；切镜、尺寸或开关变化重置历史。深度从实际 2×MSAA 绘制缓冲解析，避免独立覆盖材质遗漏发丝/描边。主光采用捕获确认的单张 4096 阴影图及软阴影参数；阴影视锥拟合仍由 Unity 6 计算。反射使用源 ReflectionPlane 高度 .0015，不额外叠加宿主高斯模糊。静态 TAA 稳定性与效果开关有 Editor 像素检查；动态骨骼运动向量精度、快速转镜头拖影和反射投影的全范围误差尚未逐帧定量验收。资源和部分 Pass 一致不等于逐像素一致，武器及附件模型、动画相位、部分 UI 细节仍有差异。完整 Player/AOT/热更发布须单独验收。

验证入口为 Tools/Test-BarrackEditor.ps1 和 Tests/Framework.Tests.csproj；最新事实以 TestResults 下注明源码与日期的报告为准。

连续帧验证入口：`Tools/Test-BarrackSequence.ps1`。使用正常 Camera 帧推进蒙皮历史，按 rootBone 坐标恢复 GPU 顶点；输出逐帧运动、深度、矩阵与残留指标。采集完成与原作等价验收分开，最新差异见 `TestResults/barrack-continuous-validation-2026-11-12.md`。

角色切换回归入口：`Game.Editor.Barrack.BarrackRosterValidation.Run`，覆盖真实左侧按钮、重复往返切换、各角色已有动作的骨骼变化、源纹理颜色空间、发束几何尺寸、背景复用和切换中取消清理。结果及含 UI/无 UI 实帧写入 `LocalOnly/Barrack/character-import/validation`。新角色接入不代表其物理发丝、角色专属镜头或全部原作渲染分支已完成截帧等价验证。

原生透明角色材质：ubertrans 的正面 Pass 名为 GFCharTransFront、LightMode 为 UniversalForward；背面 LightMode 为 GFCharTransBack。主画面及角色平面反射补充背面绘制，阴影仍使用原生 ShadowCaster。导入校验按实际透明 Pass 契约检查，不能要求所有材质包含 GFCharForward。此适配通过 Editor 展示回归，不等于透明材质逐像素或所有绘制顺序完全一致。

角色 Shader 库可包含主包与 `additionalShaders`：按内容租约共享加载，全部成功后绑定材质，取消或失败释放已经取得的租约。FaelynnSSR01（芙铃）额外保留原生 `UV_Effect_UVAll_Fast_2` 服装特效库；其无 LightMode 的透明 Pass 通过 SRPDefaultUnlit 在主画面和角色平面反射中绘制。中文名依据原作 GunData 与中文语言表关联，PeriSSR01 对应佩莉。

角色环境光由 BarrackCharacterLighting 接入原作 `_ArtNode.lightProbes` 的 8 个探针、27 项 SH 系数及内部四面体，保持各 Renderer 原始 probeAnchor。区域内采用源单元的重心插值；区域外延续凸包最近边界的系数，避免 Unity 6 对此旧探针资源越界时退回 ambientProbe 造成明暗跳变。使用 CustomProvided 与 MaterialPropertyBlock 写入逐物体 SH，动画求值后刷新，数据随角色实例销毁。边界延续是本项目的兼容处理，不宣称恢复了原作引擎全部外部四面体算法。导出入口为 Tools/Export-BarrackLightProbes.py；Editor 回归入口为 BarrackLightingValidation.Run。

角色实例的渲染层由 `BarrackStageDefinition.characterLayer` 统一提供，取默认整备室源角色的第 13 层，须落在源 `CharForward` 的 24576 层掩码中。不能保留部分源预制体的编辑默认层，否则主体着色通道会跳过，仅留下黑色轮廓。此规则仅属于本展示业务，不改变通用实体层或框架层。
