# 框架冷启动宿主

挂在 Boot 场景，负责进入业务之前和退出应用时的统一处理。依赖稳定层与 HybridCLR，不静态引用热更业务。

## 阅读入口

[Bootstrap.cs](Bootstrap.cs)：BootCoreAsync 读取配置、验证/修复版本、加载元数据和 DLL、创建 GameRuntime 并启动入口；ShutdownAsync 取消并等待模块退出。测试参数只用于自动验收。
