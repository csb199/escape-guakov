# Unity 服务适配

将核心机制接到 Unity API。服务通过显式引用注入，生命周期由 ModuleHost 管理。

## 阅读入口

- [GameRuntime.cs](GameRuntime.cs)：基础服务组装入口。
- [ResourceService.cs](ResourceService.cs)：AB、依赖包、资产缓存及租约。
- [SceneService.cs](SceneService.cs)：Additive 场景及卸载所有权。
- [EntityService.cs](EntityService.cs)：实例化、挂点、交互路由和缓存维护。
- [UiForms.cs](UiForms.cs)：窗口生命周期。
- [AudioService.cs](AudioService.cs)：分组播放与资源回收。
- [UnityServices.cs](UnityServices.cs)：配置、JSON、异步操作、租约及 UI 创建辅助。
- [PlatformDiskSpace.cs](PlatformDiskSpace.cs)：平台磁盘空间查询。
