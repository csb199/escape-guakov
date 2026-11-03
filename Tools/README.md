# 自动化工具

在项目根目录运行 PowerShell 脚本。默认 Unity 路径可通过相应参数覆盖；离线选项仅用于本机包管理器异常。

## 阅读入口

| 文件 | 职责 |
|---|---|
| [Run-Unity.ps1](Run-Unity.ps1) | Prepare / BuildMono / BuildHybrid / Open |
| [Publish-Release.ps1](Publish-Release.ps1) | 指定冻结基线、版本、输出和签名密钥，独立发布 |
| [Test-Demo.ps1](Test-Demo.ps1) | Player 功能、版本恢复及退出验证 |
| [Test-Editor.ps1](Test-Editor.ps1) | Editor Play Mode 功能与退出验证 |
| [Test-Publish.ps1](Test-Publish.ps1) | 独立升级和不兼容基线拒绝验证 |
| [Sync-GameSource.ps1](Sync-GameSource.ps1) | 镜像同步 Assets/Game，清理离线镜像旧路径 |
| [Package-Delivery.py](Package-Delivery.py) | 核对验证结果与哈希，生成源码/Player 交付 ZIP |

新完整交付包必须对应当前构建和验证证据，不能用旧哈希替代新源码验证。
