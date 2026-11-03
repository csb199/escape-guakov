# 热更业务接入

本目录由 Game.HotUpdate.asmdef 定义业务程序集。热更是部署边界；业务代码不属于 AOT 通用框架。

## 阅读入口

[Demo/Entry/GameEntry.cs](Demo/Entry/GameEntry.cs) 是 IGameEntry 参考实现：RegisterModules 注册业务模块，StartAsync 组装流程，StopAsync 等待退出。

[示例目录分类](Demo/README.md)仅展示接口使用方式。替换业务时保留入口契约、资源所有权和发布兼容要求。
