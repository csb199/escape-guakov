# 整备室脸部序列与动画过渡核对

> 已被本日后续审计纠正：下文“执行原生基础状态”和“完成后近景停留”不是可靠结论。控制器含状态名但动画列表为空、Motion 索引为 -1；此前检查没有验证默认待机实际驱动骨骼，漏掉了退化。近景停留也是未证实的流程推断，已撤销。保留本文仅作历史记录，当前证据和验证见 barrack-animation-audit-2026-11-13.md。

日期：2026-11-13。环境：Unity 6000.3.27f2 / Windows D3D11，根源码同步到 LocalOnly/OfflineProject 验证。

## 源数据与判断边界

- 原客户端 Assembly-CSharp 的枚举 LBEEJEDGKEH 中 Head=1011、Weapon=1020、Chest=1030、Toe1=1510、Toe2=1511。1011 的末位不能解释成动作行序号。
- 原 BarrackInteractData 的闪电 101 组含 101710101 / Touch_Head 和 101710102 / Camera_Look；杜莎妮两种动作的行序号相反。按序号选择会使两名角色点击同一区域走不同入口。
- CAB-5596806505a89f79d104350619fd553e 内的 GrozaSR01_Barrack 原生 Animator 包含 BarrackEntrance→BarrackIdle，HasExitTime=true、ExitTime=1、TransitionDuration=0；触摸状态不在该 Animator 内。
- 源镜头执行器参数 BaseToLookAtTransitionTime=0.5、LookAtToBaseTransitionTime=1、LookAtFollowDuration=0.2。原互动管理器另有 PlayableDirector 和 Stop/Playing/Waiting 状态。
- 本次按用户描述修复脸部入口和近景停留，并复用源片段、控制器和镜头参数。没有完整恢复原客户端 Timeline 的全部绑定及分支，不把宿主序列视为原 Timeline 资产逐轨道等价。

## 修复

1. 默认视角点脸进入当前角色的 Look，完成后停留近景；近景点脸进入该角色 Head，返回通过 ToIdle 退出。没有对应片段时不借用其他角色动作。
2. 包内存在原生 AnimatorController 时执行原生基础状态及过渡；仅含动画片段的角色使用持续待机输入。返回不重新从零启动正在运行的待机相位。
3. 动作切换保存当前混合权重，旧输入权重归零后才销毁，避免双槽复用在提前返回时删掉仍有贡献的姿态。新增 mixer 输入时恢复全部输入权重，避免总权重变化。
4. 保持镜头返回插值连续；ToIdle 完成不会再重启同一目标镜头的插值。首次近景对焦直接初始化当前头部位置，避免从旧锚点追赶。

## 验证入口

- Tests/Framework.Tests.csproj：197 项检查通过，含 Look→近景停留→Head→返回状态流。
- BarrackFullValidation：两轮 Editor 回归通过，覆盖镜头父节点、旋转缩放、动作与返回、源资源生命周期、渲染差分。
- BarrackTouchPlaybackValidation：18:52:30 完成，通过 EventSystem 射线点击，验证自然帧动画时间/骨骼变化、Look 机位和 FOV、近景停留、第二次点击 Head、真实返回、混合中断的姿态连续性及脚本重编译恢复。已目视复核 GrozaSR01-look.png 和 GrozaSR01-head.png：脸部居中、近景构图正确、第二次点击后表情姿态改变。近景输出在 LocalOnly/OfflineProject/LocalOnly/Barrack/touch-playback。
- BarrackRosterValidation：18:50:37 完成，36 名角色全部通过，含重复往返切换、各动作骨骼变化、变形范围和资源退出；结果位于 LocalOnly/OfflineProject/LocalOnly/Barrack/character-import/validation。

本次不涉及 Player、AOT 或热更发布验收。LocalOnly 原始日志和实帧不上传。
