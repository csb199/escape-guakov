# Unity 6 整备室实时窗口闪烁修复验收

日期：2026-11-12。环境：Unity 6000.3.27f2、Windows、D3D11。最后一次普通编辑器采样结束：17:02:31（Asia/Shanghai）。

## 问题与根因

相机位置、旋转与 FOV 稳定，但原生 TAA 开启后，正常图像与全黑图像严格交替。关闭 TAA 后输出稳定。

RenderDoc 修复前的 `before-fix.rdc` 事件 4351 表明：TAA 的 DrawIndexed(3) 实际颜色输出仍是 `_BarrackTemporalMotion`，没有绑定历史颜色/亮度 MRT；运动向量 SRV 因输入输出冲突变为 null。全屏顶点正常。因此历史颜色缓冲一直保留旧内容，双缓冲交换产生隔帧黑屏。

在该 Unity 6/D3D11 路径中，使用 `BuiltinRenderTextureType.None` 作为该 MRT 的深度附件未完成预期绑定。修复为两个交换用颜色 RT 分别拥有尺寸匹配的 24 位深度附件，并在 TAA MRT 绑定中显式指定当前输出的深度附件。原作通道为 ZTest Always / ZWrite Off，该附件不参与图像深度判定，且跟随原有相机历史资源一起释放。

修复后的 `unity_capture_2.rdc` 事件 4349 确认：

- 颜色输出 0：`Source TAA result`。
- 颜色输出 1：`Source TAA next luminance`。
- 运动向量输入：`_BarrackTemporalMotion`，不再为 null。
- 修复前后像素着色器哈希相同：`7c79a0d8-a98d21f3-4027e18b-949645d7`，与原作截帧中的 TAA 字节码一致。

未保留排查过程中未奏效的剔除方向修改、双面三角形或额外窗口输出纹理路径。

## 新源码验证

新增 `Game Framework/Barrack/Probe Live Flicker` 实时窗口采样入口，不绑定离屏验证相机。每轮采集 224 个相邻 Unity 帧：32 帧正常播放，64 帧冻结并开启 TAA，64 帧关闭 TAA，64 帧关闭 AO。结束后恢复动作与效果开关。以 160×90 线性图像读回统计整帧黑屏与帧差；它用于检测本次隔帧黑屏，不是精细拖影或原作全范围一致性评分。

| 场景 | 修复前黑帧 | 修复后普通编辑器黑帧 |
| --- | ---: | ---: |
| 正常播放、TAA 开启 | 16 / 32 | 0 / 32 |
| 冻结姿态、TAA 开启 | 32 / 64 | 0 / 64 |
| TAA 关闭对照 | 0 / 64 | 0 / 64 |
| AO 关闭对照 | 0 / 64 | 0 / 64 |

普通编辑器的 224 帧连续性通过；相机位置和旋转最大变化均为 0。冻结且开启 TAA 时，相邻帧 RGB 平均绝对差的 P95 从 0.04773166 降至 0.00055310（0～1 线性读回）。RenderDoc 环境和退出 RenderDoc 后的普通编辑器分别通过一次完整采样。最终普通 Unity 保持播放，TAA、AO 与角色动作已恢复。新 C# 完成 Editor 编译，分析工具完成 Python 语法检查。

验证范围为本次实时窗口闪烁修复；未将此前离屏报告当成本次验证，也不代表原作所有运动、反射和阴影指标已全部达标。本次未进行发布构建。

## 本机证据与复跑

- `LocalOnly/Barrack/flicker/before.csv`：修复前窗口采样。
- `LocalOnly/Barrack/flicker/frames.csv`、`validation.json`：最后一次普通编辑器采样及 PASS 结果。
- `LocalOnly/Barrack/flicker/renderdoc-validation.json`：截帧环境采样结果。
- `LocalOnly/Barrack/flicker/gpu-binding-verification.json`：GPU 输出与输入绑定对照。
- `LocalOnly/Barrack/flicker/before-fix.rdc`、`unity_capture_2.rdc`：修复前后 GPU 帧，仅保留本机。
- `LocalOnly/Barrack/flicker/normal-editor.log`：普通编辑器日志。

运行菜单采样完成后，执行 `python Tools/Analyze-BarrackFlicker.py`。若需对照基线，追加 `--baseline LocalOnly/Barrack/flicker/before.csv`。完整采样不连续或开启 TAA 时出现整帧黑屏，分析工具返回非零退出码。
