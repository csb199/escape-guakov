# Unity 6 整备室编辑器验证（2026-11-12）

本文件为较早的角色导入阶段记录。真实背景、渲染及交互的后续验收见 [Unity6-Barrack-environment-interaction.md](Unity6-Barrack-environment-interaction.md)。

工程：F:/escape-guakov；Unity：6000.3.27f2；图形后端：Windows D3D11。

本次直接运行根工程，未使用 OfflineProject/UnityProbe 历史结果替代验证。

## 已修复

- 启用 com.unity.modules.physics2d，解决 UGUI 测试引用 BoxCollider2D 时的 CS1069。
- 原导出 Shader.asset 缺少平台字节码，加载/卸载时产生 Corrupt shader bytecode。改用保留 GUID、名称和全部属性的 .shader 编辑器引用，并把材质的引用类型从 NativeFormat 更新为 ShaderImporter。原生包仍负责实际着色；没有用占位 Shader 绘制角色。
- 导入器同步改为 BarrackShaderReference，后续导入不会再次生成损坏 Shader.asset。
- 播放验证等待编辑器 delayCall，避免 Unity 6 在首次搜索数据库创建之前进入播放而产生 SearchDatabase.EnumerateAll 越界异常。
- DemoBuilder 使用 Unity 6 NamedBuildTarget API。

## 本次结果

调用 Game.Editor.Barrack.BarrackPlayValidation.Run，通过两轮进入/退出 Play Mode：

- 默认场景 Assets/Game/Scenes/Barrack.unity 成功加载。
- 五个 SkinnedMeshRenderer 的网格/骨骼引用有效。
- 原生待机、伸展、头部、武器、口袋动作均成功切换，并回到待机。
- 相机实际渲染图包含非均匀像素，已人工检查闪电正面图像。
- 验证期间未捕获 Error / Exception / Assert；日志无 error CS、Corrupt shader、Failed to load。
- 第二次进入能重新加载原生库，第二次退出后未遗留动画库。

原始结果：LocalOnly/Barrack/live/unity6-play-result.txt
日志：LocalOnly/Barrack/live/unity6-play.log
实帧：LocalOnly/Barrack/live/unity6-default-scene.png
复验：Tools/Test-BarrackEditor.ps1（先关闭根工程编辑器）。

## 范围与限制

- 编辑器默认播放整备室开发场景；签名客户端仍使用 Boot 冷启动入口，未更改发布入口全名/程序集契约。
- 当前背景为纯色，保留原 UI 层级证据；真实默认房间几何、原版 UI、完整阴影/反射/后处理没有完成。
- 原生 Shader 加载仍有原作 URP FallbackError 缺失警告；本次实际 GFCharForward 绘制未走该 fallback。这不等于完整原作渲染通过。
- 本次没有进行 Unity 6 的完整 Player 构建、AOT 和热更新发布验收。
