# 构建与发布入口

仅在 Unity Editor 编译，不是运行时服务模块。内容构建和独立发布分目录维护。

## 阅读入口

- [Demo/DemoBuilder.cs](Demo/DemoBuilder.cs)：参考内容配置、Prepare、完整客户端构建与编辑器验证入口。
- [Publishing/ReleasePublisher.cs](Publishing/ReleasePublisher.cs)：冻结基线检查与独立签名发布。当前复用 DemoBuilder.BuildContent，新业务需替换资源构建配置。
