# 整备室 GTAO、时域及阴影校对 — 2026-11-12

本轮依据本机原作 barrack-direct-valid.rdc（frame 8378）继续解析 GPU 事件；没有重新启动原作或修改原作程序。

## 证据与实现

- 事件 2625/2637/2649：GTAOComputeShader 原生三个 kernel，半分辨率 RGBA8 → 全宽半高 RGBA8 → 全分辨率 R8。半径 .5、分布 2、衰减 .4、降采样 2，从匹配 GPU 常量读取。替换原先 8 点近似 AO。
- 事件 3074/3091：原生 TemporalAntiAliasing 中等质量分支、Sharpen=5、HDR 和亮度双输出、历史深度。时序改为透明物体之后、Bloom/调色之前；使用运动向量 MRT、PerObjectData.MotionVectors、Halton 抖动及逐相机历史。Halton 周期及历史失效阈值是宿主适配，并非已恢复原组件源码。
- 修复原生全屏 Pass 背面剔除导致历史双缓冲未写入的问题。新增静态稳定性验证真实捕获过该故障，不能只依赖着色器存在和效果开关测试。
- 实际 MSAA 深度解析替代独立 overrideMaterial 预绘制，覆盖原生发丝和描边绘制结果；反向 Z 取近样本。
- 角色事件 2102 精确字节码匹配：_MAIN_LIGHT_SHADOWS；没有级联关键词。主光图 4096，软阴影参数 (1,1,0,0)。去除错误的两级级联以及距离淡出；投影拟合和偏移仍由 Unity 6 宿主执行，未宣称矩阵逐元素一致。
- 反射裁剪采用 layout.json 中 ReflectionPlane 高度 .0015；移除原接收材质 BlurSize=0 之外的宿主 Gaussian 模糊。反射完整投影及不同镜头误差仍待定量比较。

## 最新验证

Unity 6000.3.27f2 / D3D11，Tools/Test-BarrackEditor.ps1：PASS。两轮动作/触摸/镜头返回、像素开关、取消与租约释放通过。静止 TAA 图与后续图 RGB 平均差分别 .71136 和 .76913（0–255）；这是静态稳定性数据，不是与原作的误差或锯齿减少百分比。TAA 开关平均差约 1.16，确认原生解析实际改变输出。

输出：LocalOnly/Barrack/live/barrack-taa.png、barrack-taa-later.png、barrack-full.png 和 full-validation.txt。日志仍有原包缺脚本和 fallback Shader 缺失警告；没有新增 C# 或 Shader 编译错误。未做完整 Player/AOT/热更验收。

## 未完成的等价性验证

快速转镜头、动态骨骼/发丝运动向量和拖影仍需要连续 GPU 帧；当前静态验证不覆盖这些情况。默认动作相位与武器附件模型仍不同，不能将截图称为原作逐像素复刻。反射亮度/投影全范围、阴影视锥/偏移仍有宿主适配。TAA 已消除此前完全未写历史的问题，但视觉锐度和残余边缘差异还需相同动作相位的对照。

## 验证源文件 SHA256

验证结果时间：2026-11-12T15:03:08.731502

- `Assets/Game/HotUpdate/Barrack/BarrackRenderPipelineAsset.cs`：`863d0d3f7ca4e55e82b81c44739972e5b3cb658490a3383fbe6a669eb3b07d05`
- `Assets/Game/HotUpdate/Barrack/BarrackNativeTemporal.cs`：`09bc196c6e4245945b436aedce27bccd0b5d0e51ebe8ac11200e089ed41fa6db`
- `Assets/Game/HotUpdate/Barrack/BarrackNativeOcclusion.cs`：`22a84beda3258062e37592fca06b83f95e50a5c583d1829730d63ae140ed98da`
- `Assets/Game/HotUpdate/Barrack/BarrackStageLibrary.cs`：`be6e5239cfab99e5d6e4164e75ed41b56d72ca3e8b916f4293aa768fbcc579fd`
- `Assets/Game/Content/BarrackSource/Rendering/BarrackPost.shader`：`6581c562dbfc092985a5320abf858a5f778ce6e004f0771dc1e103d21b799e79`
- `Assets/Game/Editor/Barrack/BarrackFullValidation.cs`：`858608197bfca98acfe9af2cacfe6db106aab60e93d1a99d6d5f579c1602a3e7`
