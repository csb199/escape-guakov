# 整备室截帧校对记录 — 2026-11-12

## 证据与范围

- 原作：RenderDoc 1.47，D3D11，`-force-gfx-direct`，帧 8378；完整文件保留在 `LocalOnly/Barrack/Captures/barrack-direct-valid.rdc`，未上传。
- 原作完整画面：`LocalOnly/Barrack/Captures/barrack-frame/final-0.png`，1600×900。`event-3287.png` 是主 UI 之前的画面，仍含世界空间触摸提示，不能称为完全无 UI。
- 验证宿主：Unity 6000.3.27f2，D3D11，1280×720。校对代码与资源均在根工程；没有改动生成镜像。

## 本轮修正

1. 项目保持 Linear。原来克隆的角色纹理全部标为 Linear；按捕获的 SRV 格式修复漫反射、眼睛、眼部混合与头发高光图为 sRGB，法线/RMO/Ramp/面部混合数据保留 Linear。导入计划显式传递源 `m_ColorSpace`；`texture-color-contract.json` 与 Editor 验证防止退化。
2. 默认镜头使用捕获位置、旋转、40° FOV、0.1 近裁面。主渲染使用三 MRT、2×MSAA、1.4 渲染比例。角色面向与默认镜头匹配。
3. 字节码匹配确认原生 LUT、Bloom、Opaque Post、UberPost 实际分支。接入五级 Bloom 下采样、三级上采样、四向量指数雾参数、Unreal Filmic、HDR grading、暗角。不透明后处理移到透明绘制之前，去除宿主 Bloom 重复执行。
4. 重建运行时反射接收平面的顶点/UV/位置；使用原生 `RerlectionAdd` 材质与 2048 平面反射，移除宿主全屏反射叠加。补齐附加聚光灯阴影和距离衰减常量。
5. 体积光按事件 2932 的网格、常量、透视 Shader 指令和加法混合状态重建为 Unity 6 适配 Shader。其原作完整组件与其他变体未恢复。
6. UI 使用源中文字体、源控件图、头像、技能和武器图，恢复三列列表与概览布局。GPU 导出图片的线性编码和垂直方向单独标记，优先使用源包 sRGB 原图。UI Shader 适配四点采样和源字体 alpha 运算。

## 新验证

`Tools/Test-BarrackEditor.ps1`：PASS。两次完整进入/退出、8 项动作及返回、触摸射线、环绕镜头、颜色空间契约、字体与图片绑定、反射接收面、阴影/AO/反射/后处理像素差异、加载中取消、StateScope 等待和全部原生包释放均通过。详细输出：`LocalOnly/Barrack/live/full-validation.txt`。最新画面：同目录 `barrack-ui.png`、`barrack-full.png`。

该测试证明运行与接线有效，不证明原作视觉完全一致。原生旧包的 missing-script / fallback-shader 警告仍存在；本轮没有 C# 编译错误或被测试捕获的运行 Error/Exception。

## 尚未达到原作一致的部分

- 武器与附件的三维模型尚未挂接；当前角色动作相位与参考帧不同。
- 反射投影、阴影过滤、AO、时域滤波仍需继续校对；尚未接回原作完整运动向量与历史深度链。
- UI 部分徽章、升级节点、边框、图片裁切与间距仍有差异。当前账户数据是离线显示样本；列表头像不代表其他角色的三维模型已导入。返回、列表收起与角色触摸已接入，其他页签及账户业务未完整恢复。
- 未进行本轮完整 Player/AOT/签名发布与热更验收，不能把 Editor PASS 当作发布验收。

因此本记录是实测校对进展，不是“已与原作完全一致”的验收声明。
