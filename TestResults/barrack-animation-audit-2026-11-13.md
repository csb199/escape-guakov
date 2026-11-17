# 整备室默认动画与交互回归审计（2026-11-13）

## 纠正前次结论

默认动画停播是前次接入导致的回归，不是源待机片段缺失。GrozaSR01、TololoSSR01 原生 AnimatorController 的 m_AnimationClips 为空，各状态的 m_BlendTreeConstantIndexArray 为 -1，未绑定可执行动作。仅检查 HasState 与状态名，无法证明控制器驱动了骨骼。原客户端还有运行时动画装配流程，宿主没有恢复它，不能直接把这两个状态模板接入基础层。此前“近景停留、再点脸、返回退出”的固定流程也没有充分原作证据，已撤销。

## 本次修复

- 所有已接入角色使用自己的原生 BarrackIdle 作为持续基础输入，要求原片段带循环标记；保留压缩片段的原始序列化数据。入场结束直接进入待机；交互返回不重置正在运行的待机相位。
- 动作自然结束进入本角色 ToIdle，同时镜头恢复默认位置、旋转与 FOV；不再永久停留近景。返回按钮可中断动作。ToIdle 完成不重新计时尚未结束的镜头插值。
- 中断混合时保留全部当前输入权重，权重归零后才释放片段，避免切换时突变。0.18 秒互动混合是宿主兼容参数，不能当作从原作恢复的过渡值。

## 原始资源交叉检查

工具：Tools/Audit-BarrackAnimationContracts.py。源索引：F:/gfresource/bundle_index.jsonl 与补充分段索引；源载荷：F:/gfresource/extracted/bundles；交互表：已安装原客户端 LocalCache/Data/Table 的 GunData 与 BarrackInteractData。

36 名角色、604 个原生动画片段按名称、PathID 对应源索引，再逐字节比较原始对象载荷，全部相同。36 个默认待机均为循环片段。465 条交互表记录核对 id、事件、片段名、权重、水平/垂直角度范围、缩放范围及记录完整性，全部匹配。已导入的两个控制器都是未绑定动作的模板，其原始对象载荷也与源包逐字节一致，并非导入丢失动画绑定。源片段事件共 586 个，全部为 PlayCommonAudio；不存在可直接用来恢复镜头时序的动画事件。

| 角色 | 原生片段数 | 源交互记录数 | 待机循环秒数 | 导入控制器 |
| --- | ---: | ---: | ---: | --- |
| GrozaSR01 | 15 | 14 | 2 | 无动作绑定的模板 |
| DusevnyjSSR01 | 18 | 12 | 2 | 包内无控制器 |
| PhaetusaSSR01 | 19 | 12 | 2 | 包内无控制器 |
| FlorenceSSR01 | 19 | 12 | 2 | 包内无控制器 |
| ZhaohuiSSR01 | 15 | 11 | 4 | 包内无控制器 |
| ClukaySSR01 | 16 | 12 | 2 | 包内无控制器 |
| TololoSSR01 | 17 | 13 | 2 | 无动作绑定的模板 |
| AndorisSSR01 | 16 | 12 | 2 | 包内无控制器 |
| PeriSSR01 | 14 | 14 | 1.98 | 包内无控制器 |
| FaelynnSSR01 | 17 | 13 | 2 | 包内无控制器 |
| AsteriaSSR01 | 18 | 13 | 2 | 包内无控制器 |
| CeciliaSSR01 | 19 | 13 | 2 | 包内无控制器 |
| CentaureissiSSR01 | 23 | 17 | 2 | 包内无控制器 |
| CheyanneSSR01 | 16 | 13 | 4 | 包内无控制器 |
| KseniaSR01 | 15 | 13 | 2 | 包内无控制器 |
| MitylSSR01 | 16 | 13 | 2 | 包内无控制器 |
| NikketaSSR01 | 15 | 13 | 1.98 | 包内无控制器 |
| SakuraSSR01 | 18 | 13 | 2 | 包内无控制器 |
| VoymastinaSSR01 | 18 | 13 | 2 | 包内无控制器 |
| YooHeeSSR01 | 15 | 13 | 2 | 包内无控制器 |
| BalthildeSSR01 | 16 | 13 | 2 | 包内无控制器 |
| DestroyerSSR01 | 16 | 13 | 2.5 | 包内无控制器 |
| HarpsySSR01 | 18 | 12 | 2 | 包内无控制器 |
| JiangyuSSR01 | 17 | 14 | 2 | 包内无控制器 |
| KoledaSSR01 | 17 | 13 | 2 | 包内无控制器 |
| LindSSR01 | 18 | 13 | 2 | 包内无控制器 |
| LoreleySSR01 | 18 | 13 | 2 | 包内无控制器 |
| PapashaSSR01 | 17 | 12 | 2 | 包内无控制器 |
| SpringfieldSSR01 | 16 | 13 | 4 | 包内无控制器 |
| UllridSSR01 | 14 | 13 | 2 | 包内无控制器 |
| LiushihSSR01 | 18 | 13 | 2 | 包内无控制器 |
| BiyocaSSR01 | 16 | 13 | 2 | 包内无控制器 |
| NemesisGnosisSSR01 | 15 | 13 | 4 | 包内无控制器 |
| VectorSSR01 | 14 | 13 | 2 | 包内无控制器 |
| MishtySSR01 | 17 | 12 | 4 | 包内无控制器 |
| RobellaSSR01 | 18 | 13 | 2 | 包内无控制器 |

## 验证

- 纯逻辑回归：Tests/Framework.Tests.csproj，本次 219 项通过，包含所有支持动作的自动返回流程。
- 自然播放回归：首轮完成前 34 名角色，在米什缇因验证器错误地要求已排除的 Head 而失败；失败结果原样保留。修正验证器尊重角色实际可用动作后，从索引 34 补跑，2026-11-13 19:48:36 通过。运行时代码在两轮间未变化，两份 accepted.txt 合计 36 个不同角色。36 名待机均跨循环实际运动；35 名具备 Look 的角色自动恢复镜头，34 名具备 Head 的角色自然返回；米什缇验证待机、浏览和返回，不计入脸部动作通过数。
- 实际 UI 射线、返回按钮、脚本重编译恢复：2026-11-13 19:50:09 通过。覆盖 Groza 与重编译恢复后的 Dusevnyj：待机实际骨骼运动、点脸 Look 拉近及自动恢复、返回按钮播放 ToIdle、混合中断权重归一与姿态连续、旋转缩放后的返回。已目视检查本次两个角色的 Look 截图，确认脸部近景构图；单帧截图不作为动画连续性证据。
- Editor 展示回归：2026-11-13 19:52:45，Tools/Test-BarrackEditor.ps1 -Offline 通过；覆盖两轮播放、浏览与触摸、效果差分、StateScope 取消等待、加载中取消与全部原生租约释放。
- 全名单动作回归：2026-11-13 19:56:00，BarrackRosterValidation.Run 通过；36 名角色、38 次切换（含重复往返）、257 次已开放动作骨骼采样、1370 项检查。覆盖真实列表按钮、旧手势隔离、主体与骨骼所有权、原生动作形变和切换中关闭的资源释放。该项采用手动采样验证每个已开放片段，与前述自然播放检查互补，不证明未开放片段或原作 Timeline 已恢复。

自然播放检查不通过强制推进状态或手动采样代替实播：逐角色等待入场完成，观察至少 4.6 秒待机骨骼变化并跨越源循环，等待可用 Look/Head 自然结束后检查镜头恢复，再检查待机仍在驱动骨骼。补跑路径为 animation-continuity/from-34；验证入口支持 BARRACK_VALIDATE_START_INDEX 指定补跑起点，默认检查整个名单。

## 尚未恢复的原作逻辑

本次通过不等于原客户端所有交互与 Timeline 等价。原客户端 FacilityBarrackModelInteractManager 含运行时 PlayableDirector/AnimPlayableDirector、Timeline 状态与动态绑定；现有资源中没有恢复其完整可执行流程。当前宿主还没有完整实现交互表的视角/缩放/装备条件筛选、专属道具和镜头轨道绑定。默认点脸选择 Look 是现有适配入口，不能由它推导所有视角下的 Head 分支。

Florence、Zhaohui、Clukay 的口袋动作及 Zhaohui 的 Head 涉及未绑定道具，仍未开放。Vector 的 InspectExclusive/Gun，以及 Mishty 的 InspectExclusive/Stretch/Aroud/Look/Gun/Head/Pocket，已由名单的资源等价性限制排除；米什缇当前并不具备可用的点脸动作，本次没有擅自解除限制。排除片段没有用别的角色片段代替。音频事件接收器未接通。本次不把这些缺项计为已经恢复，也不把自己的状态机测试当作原作行为证据。

原始明细：LocalOnly/Barrack/animation-audit/contracts.json。运行日志与图片位于隔离镜像 LocalOnly/OfflineProject/LocalOnly/Barrack；这些缓存不纳入源码提交。历史报告 barrack-face-sequence-2026-11-13.md 已加更正说明。
