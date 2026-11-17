# 场景入口

- Barrack.unity：Unity 6 编辑器默认播放入口，直接引用闪电源资源，适用于展示和渲染接入开发。BarrackPreview 位于 HotUpdate/Barrack，退出会取消并等待库加载，然后释放动画、实例材质和原生库。
- BarrackSource.unity：原始整备室 UI 层级的导入记录，不能替代真实房间场景。
- Boot.unity：签名客户端冷启动入口；发布工具显式使用它。
- Lobby / Arena：可替换的框架使用示例，不定义框架目标。

默认播放场景由 Editor/Barrack/BarrackStartup 设置，也可通过菜单 Game Framework/Barrack/Open Default Scene 恢复。原生 Shader 当前限定 Windows D3D11。播放时加载真实默认整备室和环境光照；原生材质与宿主后处理共同绘制。逐像素画面对照及完整发布验证见对应验收报告。


