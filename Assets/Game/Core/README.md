# 框架核心

不依赖 Unity 的通用机制；不放具体玩法、界面布局或业务存档字段。

## 阅读入口

- [Lifecycle.cs](Lifecycle.cs)：ModuleHost、依赖排序、启动/关闭与事件。
- [GameEntryModule.cs](GameEntryModule.cs)：将业务入口纳入宿主生命周期。
- [Fsm.cs](Fsm.cs)、[Procedure.cs](Procedure.cs)、[StateScope.cs](StateScope.cs)：状态切换、当前流程更新和异步所有权。
- [Entities.cs](Entities.cs)、[EntityHierarchy.cs](EntityHierarchy.cs)、[EntityCache.cs](EntityCache.cs)：实体、父子关系和缓存。
- [ResourceCatalog.cs](ResourceCatalog.cs)：资源地址和依赖图。
- [Updates.cs](Updates.cs)、[UpdateCache.cs](UpdateCache.cs)、[Networking.cs](Networking.cs)：签名更新、缓存预算与下载。
- [GameLog.cs](GameLog.cs)、[Persistence.cs](Persistence.cs)：日志、原子存储与恢复。
- [ReleasePublishing.cs](ReleasePublishing.cs)：发布基线与兼容检查。
