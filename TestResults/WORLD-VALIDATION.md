# 场景与实体扩展验收

日期：2026-11-03。Unity 2022.3.62f1 / Windows x64 / HybridCLR 9.0.3 / IL2CPP。

最终客户端：`unity2022.3.62f1-win64-il2cpp-9ba9e45782fea0ca`。审查修复后重新完整构建，以下结果均对应最终源码与客户端；此前 61 项检查的中间构建不作为本次交付证据。

## 验证结果

| 检查 | 结果 | 证据 |
|---|---|---|
| Core/Contracts 回归 | 177 项通过，0 失败 | `core-tests.json`、`scene-entity-core.log` |
| Unity C# / Windows IL2CPP | 完整构建成功 | 根目录 `BuildHybrid.log` |
| Editor Play Mode | 62 项通过，退出清理通过 | `Editor-20261103-160804/demo-report.json`、`shutdown-check.txt` |
| V1/V2、故障回退、首次失败与取消重试、损坏修复、普通退出 | 16 次启动全部符合预期；正常自检各 62 项 | `HybridCLR-20261103-160626/suite-result.json` |
| 独立发布 V3 与 V1→V2→V3 升级 | 3 次启动，每版 62 项通过 | `Publish-20261103-160626/suite-result.json`、`v1-report.json` 至 `v3-report.json` |
| 不兼容冻结基线 | 正确拒绝，未生成发布目录 | `Publish-20261103-160626/rejected-publish.log` |
| 源码与实际编译工程一致 | 42 个 Game C# 文件哈希一致；HybridCLR 生成文件一致 | `source-consistency.json` |
| 发布前后原生客户端 | EXE 与 GameAssembly SHA-256 均保持不变 | 两份 suite-result 的哈希字段 |

播放器共 19 次启动。故障注入场景预期失败，但其退出码、取消/回退与后续重试均由脚本断言，不能将其解释为每次都正常进入游戏。

## 本轮新增覆盖

- 父子挂载、挂点、拆卸回调、循环拒绝、跨 owner 拒绝、旧句柄隔离；子任务禁止等待祖先回收。
- 父回收等待子任务结束，整树迭代回收；1,500 层纯 C# 实体树无残留，全部 provider 实例恰好释放一次。
- 挂载失败清理，适配器回调中父回收，以及换父拆卸回调关闭目标父的重入情况。最后一项在修复前有明确失败复现。
- 空闲容量、优先级、锁定、过期、周期、单次释放预算、zero-cache，以及 OnDestroy 重入缓存维护。
- Boot 唯一内置，场景实际内容来自版本化资源包；重复加载拒绝、场景地址类型隔离、Additive 加载、卸载幂等、加载中 owner 取消后清理。
- 场景租约保护依赖包，父子实例回收后归还资源租约；Trim 等待期间新归还的包不会在同帧卸载，关闭等待在途维护。

## 独立场景热更新证据

V1/V2/V3 报告中的 sceneAsset 分别位于 `Assets/Game/HotScenes/V1/`、`V2/`、`V3/`。V3 最终为 `Assets/Game/HotScenes/V3/Lobby.unity`。

V3 的 scene-lobby 和 scene-arena 均由独立发布新生成，与 V2 对应包的 SHA-256 不同；初始客户端仅附带 V1/V2 包，未预埋 V3 场景包。自检核对 SceneContent-V3 根对象，截图确认场景几何由 V1 的小块变为 V3 的更大块。代码同时变为 TRIPLE ENERGY，完成代码与场景资源串联更新。

已查看 `HybridCLR-20261103-160626/v1-arena.png` 与 `Publish-20261103-160626/Data/demo-arena.png`，界面与实体可见，版本和场景内容变化符合预期。

- EXE SHA-256：`C125F28D80D7944102860930A96F3B5E5EFCF7E116DB1D7C2B0B8E37E4930D6B`
- GameAssembly SHA-256：`F899C9049D5386B0165AC8A7E9BC5B9DA215663147C8B5A3425941C6EB8F2B6A`

## 交付边界

本轮改变 Core/Runtime，需要完整更新客户端一次。后续兼容代码与场景仍通过冻结基线独立发布、冷启动生效；不支持运行中原地替换已加载场景。新资源组件、Shader 与裁剪后 AOT 能力必须受客户端支持。

实体仅支持同 owner 内挂载，缓存 maxCached 是目标而非硬内存预算，锁定项与分批淘汰可暂时超目标。未验证真实万实体 Unity 帧率、长期压力、移动平台或真实 CDN；1,500 层回归不代表性能压测。实现说明见 [WORLD](../WORLD.md)，审查修复见 [WORLD-REVIEW](WORLD-REVIEW.md)。
