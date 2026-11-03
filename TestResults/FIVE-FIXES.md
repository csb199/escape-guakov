# 五项审查问题修复与验收

日期：2026-11-03。Unity 2022.3.62f1 / Windows x64 / HybridCLR 9.0.3 / IL2CPP。

当前客户端指纹：`unity2022.3.62f1-win64-il2cpp-600f0b53b11d6f1a`。

## 修复范围

| 审查问题 | 修复行为 | 主要实现 |
|---|---|---|
| 首次启动中断后无法重试 | 没有健康版本时，下次用户启动重新验证后重试；兼容旧状态的首次隔离记录。正常取消且完整清理后保留候选；已有健康版本时，真正失败的候选仍隔离 | Core/Updates.cs、Bootstrap/Bootstrap.cs |
| 存档恢复污染备份、缺少主文件时丢进度 | 验证备份后恢复主文件，保留备份和原始 schema 字节；缺少主文件也使用有效备份；拒绝未来 schema 的规则不变 | Core/Persistence.cs |
| 已生效版本损坏后无法修复 | 持久化健康版本的签名清单；冷启动从原签名内容重建同版本目录；支持修复中断恢复及安装清单损坏。更高版本暂存不再被 active 的坏 payload 阻断 | Core/Updates.cs、Bootstrap/Bootstrap.cs |
| 错误资源类型污染引用计数 | 缓存类型检查和转换成功之后才增加租约计数 | Runtime/UnityServices.cs |
| 更新包文件与临时路径冲突 | 验证时拒绝 `.part` 路径冲突及清单内部保留名称；提交安装目录前重新验证全部 payload | Core/Updates.cs |

未增加运行时模块，未修改 FSM/Procedure/Entity 的职责与更新方式；未混入审查问题 6、7 的性能和旧对象池清理。

## 验证

| 项目 | 结果 | 证据 |
|---|---|---|
| 核心回归 | 131 项通过，0 失败；新增 30 项 | `core-tests.json`、`five-fixes-core.log` |
| Windows IL2CPP/HybridCLR 构建 | 成功，最终源码与离线构建镜像的 C# 文件哈希一致 | 项目根 `BuildHybrid.log` |
| Windows 播放器 | 16 次启动全部通过；每次完整自检 27 项 | `HybridCLR-20261103-142810/suite-result.json` |
| Editor Play Mode | 27 项通过，退出资源检查通过 | `Editor-20261103-143026/demo-report.json`、`shutdown-check.txt` |

核心新增检查覆盖首次安装重试、旧隔离状态恢复、候选正常取消和异常隔离的区别、连续存档恢复、主文件丢失、未来 schema、修复中断恢复、签名/不可变版本/递增序列、已安装清单及目录损坏、修复与启动交错、文件名冲突和提交前全量校验。

播放器使用真实加载的 HybridCLR DLL 和 AssetBundle。全流程自检增加“错误类型资源请求不泄漏租约”；后续退出仍须通过所有资源归零检查。修复场景同时比较存档 SHA-256 和 active/previous 字段，不能通过改用旧版本或重置存档来假装恢复成功。

## 重现与使用

```powershell
dotnet run --project Tests/Framework.Tests.csproj
./Tools/Run-Unity.ps1 -Action BuildHybrid -Offline
./Tools/Test-Demo.ps1 -Backend HybridCLR
```

普通安装可去掉 `-Offline`；本机 Package Manager 异常，沿用既有同源离线构建方式。试玩入口为 `Builds/HybridCLR/EnergyCollector.exe` 或项目根 `Launch-Demo.cmd`。

## 兼容与边界

- 这是 AOT 修复，必须更新完整客户端；只替换 Game.HotUpdate.dll 不能修复旧客户端。存档模型不变，原有存档继续兼容。
- 同版本修复只恢复原签名包，不能用相同版本号发布新内容；代码逻辑修复仍需新版本和递增 sequence。
- 首次没有健康版本时，每次用户重启可重试；同一进程不自动循环重试。若代码本身一直有缺陷，仍需发布修复版本。
- 启动取消仅在所有关闭检查成功后清除 booting；异常或清理失败仍保留故障证据。程序集加载后不能在当前进程切换修复版本。
- 修复需能取得原版本签名包。Demo 内置 V1/V2 可本地恢复；正式更新源应保留当前版本包。旧状态没有签名副本且安装清单也损坏时拒绝推测兼容基线。
- 修复不回退已迁移存档，也不自动回退 previous。损坏目录保留在 `repair-backups`，与现有版本缓存一样暂不自动清理。
- 本轮不包含真实断电、真实 CDN、跨平台、长时间压力和手工鼠标试玩验证；没有重跑 Mono，旧 Mono 报告不作为本次新验收结果。
