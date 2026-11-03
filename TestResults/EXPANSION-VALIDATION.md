# 五项框架扩展验收

日期：2026-11-03。Unity 2022.3.62f1 / Windows x64 / HybridCLR 9.0.3 / IL2CPP。

最终客户端：`unity2022.3.62f1-win64-il2cpp-43331affbb0660e2`。本次包含稳定层修改，需要完整升级客户端一次；下表中的 V3 已通过独立发布工具生成并加载，未重建该客户端。

## 已通过

| 验证 | 结果 | 证据 |
|---|---|---|
| Core / Contracts | 155 项通过，0 失败 | [core-tests.json](core-tests.json)、[运行输出](expansion-core.log) |
| Windows IL2CPP 构建与冻结基线导出 | 成功 | [BuildHybrid.log](../BuildHybrid.log) |
| Editor Play Mode | 46 项检查及关闭清理通过 | [Editor-20261103-152619](Editor-20261103-152619/demo-report.json) |
| V1/V2、失败回退、首次/候选取消重试、同版本损坏修复、普通退出 | 16 次启动场景验收通过 | [HybridCLR-20261103-152447](HybridCLR-20261103-152447/suite-result.json) |
| 独立发布、V1→V2→V3、原客户端不变、不兼容基线拒绝 | 3 次启动；每版 46 项检查；V3 为 TRIPLE ENERGY | [Publish-20261103-152447](Publish-20261103-152447/suite-result.json) |
| 源码与编译镜像一致性 | 38 个 Game C# 文件 SHA-256 全部一致；HybridCLR 生成文件逐一相同 | [source-consistency.json](source-consistency.json) |
| PowerShell 脚本与打包脚本 | 语法检查通过 | Tools/*.ps1、Tools/Package-Delivery.py |

故障场景中的预期非零退出码计入通过的场景，表示恢复行为符合断言；不表示每次启动都成功进入游戏。

## 新能力检查点

- 发布：固定引擎、平台、AOT 集合与指纹、裁剪元数据、外部类型/成员/泛型/原生引用；密钥匹配、不可覆盖输出、签名包自校验。
- 缓存：保护 active/previous/pending/booting，旧版本及过期暂存/修复目录清理，缓存容量和实际磁盘保留量拒绝，staging 垃圾不抵消下载空间。
- UI：设置弹窗遮挡/恢复，打开中取消并等待，不合作任务结束前保持租约，factory 故障清理，父作用域退出回收。
- 多包：共享依赖引用计数、资产复用、空闲包卸载后重载、错误类型和取消不泄漏，停止等待正在执行和排队的卸载。
- 音频：主/组/实例音量、组静音、淡出中主静音、重复停止、分组容量、owner 回收并保持其他 owner 的背景音。
- 全流程：大厅→战斗→结算→大厅，实体回收、事件取消、存档与网络重试，旧流程任务不能写新界面，统一关闭时订阅/租约/UI/实体/音频/资源操作归零。

## 独立发布的客户端不变证据

发布与三次启动前后的 SHA-256 一致：

```text
EnergyCollector.exe: C125F28D80D7944102860930A96F3B5E5EFCF7E116DB1D7C2B0B8E37E4930D6B
GameAssembly.dll:    31E58CFAF2104C4FAE96A573CC27637E5E231825D3D19932015C6DD4149B5342
```

V3 签名包位于 `Publish-20261103-152447/Release/`，发布报告为同目录的 `Release.report.json`。交付 ZIP 同时包含该包和当前客户端对应的 `Builds/ClientBaselines/unity2022.3.62f1-win64-il2cpp-43331affbb0660e2/`，不含私钥与 LocalOnly 缓存。

## 重跑

```powershell
dotnet run --project Tests/Framework.Tests.csproj
./Tools/Run-Unity.ps1 -Action BuildHybrid -Offline
./Tools/Test-Demo.ps1 -Backend HybridCLR
./Tools/Test-Publish.ps1 -Offline
./Tools/Test-Editor.ps1 -Offline
python Tools/Package-Delivery.py
```

独立发布需持有匹配的私钥；交付包未携带演示私钥。第一次自行构建可产生新的演示密钥和客户端，后续固定其基线发布。正式发行应使用受控的签名密钥。

## Review 与验证边界

[本轮 Review](EXPANSION-REVIEW.md) 的已确认问题均已修复。静态审查、核心测试、Windows Editor/IL2CPP 集成检查与截图核对已完成；未做长时间压力、不同 DPI 全覆盖、人耳音质、真实 CDN 或其他平台验收。静态兼容白名单无法证明任意反射和运行时泛型行为安全。
