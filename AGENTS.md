# AI 协作入口

- 始终使用简体中文回复。
- 开始修改前先读 CODEMAP.md；框架冷启动入口是 Assets/Game/Bootstrap/Bootstrap.cs，服务组装入口是 Assets/Game/Runtime/GameRuntime.cs，示例业务入口是 Assets/Game/HotUpdate/Demo/Entry/GameEntry.cs。
- Contracts/Core/Runtime/Bootstrap 是 AOT 稳定层，HotUpdate/Demo 是示例业务。不得将 Demo 成绩、UI 布局或排行榜规则搬入通用框架。
- 保持 StateScope 的取消与等待、模块依赖、资源租约、实体父子所有权边界。移动 Unity 资产时连同 .meta 保留 GUID；更改程序集名和入口全名需同步发布契约。
- 只编辑根工程源码；LocalOnly/OfflineProject 是工具生成的镜像，使用 Tools/Sync-GameSource.ps1 同步。Packages 是第三方依赖，Assets/HybridCLR 是生成内容。
- 运行与改动相关的已有检查。纯核心逻辑使用 Tests/Framework.Tests.csproj，Unity 路径/程序集/运行逻辑变化使用 Editor 验证；发布或 AOT 行为变化使用完整构建与热更验收。
- 不上传 LocalOnly、签名私钥、缓存、Player 或测试运行目录；历史报告的结果不能冒充新源码的完整构建验证。
