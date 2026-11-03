# 共享契约

AOT 与热更程序集共享的稳定接口，不依赖 Unity。接口变化会影响客户端兼容性。

## 阅读入口

[Contracts.cs](Contracts.cs)：IGameEntry 业务入口、IModule/IModuleRegistry 生命周期、IGameLog 日志、IJsonCodec 序列化、IFileTransport 传输。IDemoValidation 仅供现有验证入口使用。
