# 热更业务接入

本目录由 Game.HotUpdate.asmdef 定义业务程序集。热更是部署边界；业务代码不属于 AOT 通用框架。

## 阅读入口

[Demo/Entry/GameEntry.cs](Demo/Entry/GameEntry.cs) 是 IGameEntry 参考实现：RegisterModules 注册业务模块，StartAsync 组装流程，StopAsync 等待退出。

[示例目录分类](Demo/README.md)仅展示接口使用方式。替换业务时保留入口契约、资源所有权和发布兼容要求。

[Barrack/README.md](Barrack/README.md)：整备室开发展示与发布接入契约；编辑器默认展示闪电与真实默认背景；源资源/光照配置、宿主渲染适配、交互状态机和 StateScope 退出边界详见该目录。
