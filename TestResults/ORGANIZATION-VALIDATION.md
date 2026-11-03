# 代码分类与入口标注验收

日期：2026-11-03。本轮仅整理源码目录、补充入口说明和 Git 源码管理，不改变 C# 执行逻辑。

- 14 个热更 Demo 脚本按 Entry/Procedures/Entities/UI/Services/Validation 分类；2 个 Editor 脚本分到 Demo/Publishing。
- 16 个脚本全部保留原 .meta GUID；程序集、命名空间、入口类型、资源地址不变。
- 对照 LocalOnly/Before-Code-Organization 备份，逐一确认全部 42 个 C# 源文件除空行、注释和路径外，执行代码行一致。
- Tests/Framework.Tests.csproj 同步 DemoModel/Scores 路径，177 项核心回归通过，结果见 core-tests.json。
- Editor Play Mode 62 项检查和统一退出清理通过，本机证据目录 Editor-20261103-162613；运行目录不上传 Git。
- 离线同步改为同步整个 Assets/Game 镜像并移除旧路径残留，避免移动脚本后重复类型编译；三个调用入口均使用 Tools/Sync-GameSource.ps1。
- PowerShell 工具脚本语法检查通过。Git 候选文件检查未发现私钥内容；LocalOnly、Builds、Library、StreamingAssets 和测试运行目录均被忽略。

新增 CODEMAP.md 为目录与模块索引，AGENTS.md 为 AI 阅读入口；Bootstrap、GameRuntime、Demo GameEntry 均标注职责和调用链。框架、Demo、第三方、生成数据和历史证据分别说明。

本轮未重新构建或验收 IL2CPP Player，上一轮交付 ZIP 保持原有验证快照；WORLD-VALIDATION 的 19 次启动结论对应整理前完整构建。后续生成新完整交付包时，应重建并重新生成 source-consistency 和发布/Player 验收，不能直接覆盖旧验证哈希。

Git 版本为可重新构建的源码版本，不包含签名私钥、已签名包、Player 或冻结客户端基线。首次拉取按 CODEMAP 的 Prepare/Build 步骤生成本机内容。
