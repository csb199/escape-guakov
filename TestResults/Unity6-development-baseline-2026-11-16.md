# Unity 6 开发基线建立与验证

日期：2026-11-16。用户已明确选择建立当前 Unity 6 开发基线，不再恢复旧 Unity 2022 基线。此处为 **Windows Mono 开发验证基线**，不是 HybridCLR/IL2CPP 正式冻结发布基线。

## 配置和产物

- Unity：6000.3.27f2；平台：StandaloneWindows64。
- 最终 clientBuild：`unity6000.3.27f2-win64-mono-dba8381e4d29de82`。
- 初次 Prepare 后的指纹为 `unity6000.3.27f2-win64-mono-d4f3cb6e98e50cf9`；BuildMono 切换后端并重新编译后生成最终指纹，最终 Editor 验证针对后者。
- Assets/Resources/game-config.json 已由现有工具更新 clientBuild 和开发公钥；初始本机无 demo-signing-key.xml，工具新建了本地开发密钥，位于被忽略的 LocalOnly 中，未输出或提交私钥。
- Assets/StreamingAssets/Baseline 为 1.0.0；DemoUpdate 为 1.1.0。两包各 8 个清单文件，公钥验签、clientBuild 匹配、逐文件 SHA256 和大小核对通过。
- 完整 Mono Player：Builds/Mono/EnergyCollector.exe。此可替换 Demo 用于框架接入和更新机制回归，不代表 TPS 游戏已实现。
- 相对于本次运行前备份，ProjectSettings 唯一内容变化为 ProjectSettings.asset 中 Standalone 后端由 IL2CPP 切换为 Mono。HybridCLRSettings 执行后恢复原内容。没有改动稳定层代码或程序集/入口名称。

## 备份与追溯

本机目录：`LocalOnly/M0/Unity6Baseline-20261116-103244`。

包含原 game-config、完整 ProjectSettings 备份、运行前 Git 状态、Prepare/BuildMono 日志、最终 config 和源码 SHA256 清单；该目录与生成包、Player、私钥均被 Git 忽略。

源码清单 `source-after.json` SHA256：`C8912255FD4ED1915DD3950737F80E750C8AE98E9BEF1829D3870C260378E21A`。覆盖 Assets/Game、Assets/Resources、ProjectSettings、包声明/锁文件、Tests 和 Tools 的非忽略文件；不包含整个引擎或第三方缓存。Player exe SHA256：`D91DBFA5C7D67CDF2C521D83112DF029079F358519A1282D531BAAD4E1CD80C9`，exe 哈希不代表整套 Player 数据文件哈希。

## 已完成验证

| 操作 | 结果 | 证据 |
|---|---|---|
| Tools/Run-Unity.ps1 -Action Prepare | 成功，退出码 0 | 本机 Prepare.log 副本 |
| 初次 Boot Editor | 62 项及 shutdown 通过 | TestResults/Editor-20261116-103312 |
| Tools/Run-Unity.ps1 -Action BuildMono | 完整 Mono 构建成功，退出码 0 | 本机 BuildMono.log 副本 |
| 最终 Boot Editor | 62 项及 shutdown 通过 | TestResults/Editor-20261116-103454 |
| 两个签名包内容校验 | RSA-SHA256 签名、指纹、8 文件哈希/大小逐包通过 | 本次执行输出 |

`Tools/Test-Demo.ps1 -Backend Mono` 已通过，退出码 0，共 16 次 Player 启动。覆盖 V1/V2 更新、失败版本回退、首次启动重试、取消重试、活动版本资源/清单损坏修复、资源租约和等待退出。证据为 `TestResults/Mono-20261116-103435/suite-result.json` 及各场景报告；摘要另复制到本机备份目录 `mono-suite-result.json`，最终 Editor 报告和 shutdown 标记也已留存。此为 Mono 动态加载与更新恢复验证，不是 IL2CPP 热更验收。

## 复现与安全边界

1. 使用相同 Unity，根工程无其他 Unity 实例占用。
2. 首次建立运行 `Tools/Run-Unity.ps1 -Action BuildMono`，该入口会执行 Prepare 并生成匹配配置/内容；只需 Editor 包时可单独 Prepare，但最终交付仍按最终构建指纹验证。
3. 执行 `Tools/Test-Editor.ps1`，再执行 `Tools/Test-Demo.ps1 -Backend Mono`，保存新运行目录及报告。
4. 不删除或随意轮换现有开发密钥；更换密钥即改变信任关系。新机器应有明确开发密钥管理方案，不从 Git 获取私钥。
5. 回退需一起恢复匹配配置、后端和签名包；只恢复旧配置不能修复旧包缺失。测试使用隔离数据目录，不删除用户存档。

未验证：IL2CPP/AOT 元数据、HybridCLR 原生执行、独立热更发布工具链、专用服务器、干净机器部署和对外资产许可。需要对应模块与 C++/SDK 后另建正式基线，不将 Mono 成功用于签署这些验收。
