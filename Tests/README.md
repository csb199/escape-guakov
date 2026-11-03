# 核心回归测试

通过 .NET 9 直接编译 Contracts/Core 源码，不依赖 Unity Editor。部分纯 C# 业务参考实现用于验证存档迁移与传输边界。

## 阅读入口

入口：[Framework.Tests.csproj](Framework.Tests.csproj) → [Program.cs](Program.cs)。

在仓库根运行 `dotnet run --project Tests/Framework.Tests.csproj`。各 Regression 文件按生命周期、流程、实体、更新恢复及发布能力分类。Unity 引擎行为由 Tools 下的 Editor/Player 验证覆盖。
