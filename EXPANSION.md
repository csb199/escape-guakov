# 发布、缓存、界面、多包资源与音频

基线：Unity 2022.3.62f1 / Windows x64 / HybridCLR 9.0.3。本次新增稳定层 API，需要完整更新客户端一次；之后通过独立发布工具更新兼容业务代码和资源。

## 独立热更发布

完整 `BuildHybrid` 成功后导出 `Builds/ClientBaselines/<clientBuild>/`：

- `baseline.json`：Unity 版本、平台、公钥、客户端标识、AOT 指纹、外部 API/泛型引用白名单。
- `metadata/`：该客户端对应的裁剪后 AOT 补充元数据，按原始 SHA-256 冻结。
- `latest-manifest.json`：构建时生成的 V2 清单，供首次独立发布比较版本与序号。

```powershell
# 在项目根目录执行；替换 <clientBuild> 与输出路径。
./Tools/Publish-Release.ps1 `
  -Baseline Builds/ClientBaselines/<clientBuild> `
  -Version 1.2.0 -Sequence 3 `
  -Output Releases/1.2.0 `
  -SigningKey LocalOnly/demo-signing-key.xml `
  -DemoVariant DEMO_V3 -Offline
```

`-Offline` 仅用于本机异常的 Unity Package Manager；正常环境省略。发布应保持与该客户端相同的 Unity、依赖和构建环境，不应在发布阶段重新生成 HybridCLR 桥接。交付源码同时保留 `Assets/HybridCLR` 中的生成代码。DemoVariant 是演示宏，正式业务通常省略。再次发布时用 `-PreviousManifest 上次发布目录/manifest.json` 指向实际最近一次发布，版本须不同，sequence 须更大。工具不承担跨机器发布序号分配，正式 CI 应串行管理发布记录。

工具编译脚本、验证冻结基线、构建资源、签名并导出到新目录，不调用 `Prepare`、`GenerateAll`、`StampClientBuild` 或 `BuildPlayer`。已有输出目录一律拒绝覆盖。目录提交前检查源文件与副本哈希；发布成功旁边生成 `.report.json`。

Unity 编译宏会改变 DLL 的构建标识，即使 AOT 代码没有变化。因此 AOT 指纹通过 dnlib 规范化：保留 IL、类型/成员元数据、属性与资源，去除模块构建 ID、PE 时间戳和调试记录，再计算 SHA-256。任何实际 AOT 代码变化、程序集增删、元数据变化或引擎/平台变化都会拒绝独立发布。

热更外部类型、成员、泛型实例化和原生声明使用保守白名单。新增引用可能需要新客户端，即使该引用理论上可兼容；发布器不自动重建桥接代码，也不能证明反射字符串、动态泛型或任意运行时行为安全。冻结基线目录应与客户端产物一起归档，禁止手工修改后冒充兼容。

客户端使用配置 `remoteDirectory`，也支持启动参数 `--update-source=本地目录或HTTPS目录URL`。签名信任仍由内置公钥决定。更新源需保留不可变版本包供修复；运行中暂存，重启生效。

```powershell
# 真正独立发布 V3、运行 V1→V2→V3，并确认 EXE/GameAssembly 不变。
./Tools/Test-Publish.ps1 -Offline
```

私钥不进入客户端、基线或交付 ZIP。交付包可直接运行和验证已签名版本；需要自行发布时，应持有匹配私钥，或者用自己的密钥重新构建客户端。

## 更新缓存与磁盘预算

`GameConfig.updateCache` 默认单个客户端指纹缓存上限 8 GiB、磁盘剩余保留 256 MiB、暂存与修复残留过期时间 48 小时。

下载前和成功启动后执行维护，与更新安装共用异步锁：

1. 保护 active、previous、pending、booting 和本次下载版本。
2. 删除不受保护的旧版本、过期 staging、repair-backups。
3. 空间仍不足时清理其余可清理暂存；绝不为满足预算删除受保护版本。
4. 缓存上限或实际可用磁盘不足则拒绝下载，不提交候选版本。

为避免残留文件低估下载空间，预留完整包和清单写入余量，断点文件仍计入已有占用。这是保守预算，接近上限时可能要求更大空间。下载进度不能超过签名声明大小；安装前仍完整验签、校验文件。

Windows IL2CPP 使用 `GetDiskFreeSpaceExW`，不调用 Unity 2022 未实现的 DriveInfo。清理拒绝目录链接，防止遍历到缓存外。仅清理当前客户端指纹目录；旧客户端目录由发行/卸载策略管理，全磁盘剩余量检查仍有效。外部进程可在检查后抢占空间，写入失败仍由事务更新流程处理，不承诺操作系统级空间预留。

## UIForm 生命周期

```csharp
public sealed class InventoryForm : UiFormLogic
{
    public override Task OnOpenAsync(object data) { /* 初始化界面 */ return Task.CompletedTask; }
    public override Task OnCloseAsync() { /* 关闭外部绑定 */ return Task.CompletedTask; }
    public override void OnCover() { /* 被上层阻挡 */ }
    public override void OnReveal() { /* 恢复交互 */ }
}

var form = await game.Ui.OpenFormAsync("Inventory", UiLayer.Popup, procedureScope,
    () => new InventoryForm(), data: model, prefab: "inventory-ui");
// 窗口内按钮或自身任务只请求关闭，不能等待自己结束。
form.RequestClose();
// 外部编排可等待 form.CloseAsync()。
```

ID 在加载前占位，重复 ID 拒绝；加载失败回收占位和租约。每个窗口拥有 child StateScope，任务与订阅通过 Scope 登记。预制体在不激活的父节点下实例化，完成 OnOpen 后才激活，避免 OnEnable 早于数据初始化。

关闭先取消并隐藏，等待打开过程和所有已登记任务，再调用 OnClose、销毁实例和归还租约。父流程退出自动关闭其窗口；Close 幂等。Screen/Popup/Overlay 与打开顺序共同决定显示层级，通过 CanvasGroup 阻止被覆盖窗口交互，支持 Cover/Reveal。关闭失败仍继续清理其他窗口和在途操作，最后报告异常。

UIService 管实例与生命周期，UiFormLogic 管界面行为，Procedure 只组织何时打开。没有每帧遍历所有 UIForm。

## 多包资源与依赖

签名清单增加 `assets` 地址表；bundle 文件的 `dependencies` 声明直接依赖。构建器读取 Unity AssetBundleManifest，运行时拒绝缺失依赖、依赖环、重复地址和缺少地址表的多包版本。

Demo 当前分为七包：`config`、`ui`、`audio`、`gamecontent`、`shared`、`scene-lobby`、`scene-arena`。球预制体和场景依赖 shared 材质。业务通过 `orb`、`demo-config`、`ui-shell`、`music`、`chime` 等逻辑地址访问普通资源；场景使用 `kind=scene` 的 `Lobby`/`Arena` 地址，由 SceneService 管理。新增场景、实体挂载与缓存淘汰见 [WORLD](WORLD.md)。

```csharp
using (var lease = await game.Resources.LoadAsync<GameObject>("orb", scope.Token))
{
    // 租约必须覆盖该资产所有实例的使用期。
}
await game.Resources.TrimUnusedAsync(scope.Token);
```

按依赖拓扑顺序懒加载，串行合并包加载，缓存资产；每份 AssetLease 同时持有所属包和依赖闭包的引用。类型校验通过后才增加引用。Trim 只卸载引用为零的包；关闭等待加载与卸载请求收敛。Unity 原生加载不能强制取消，完成后检查取消并清理。

当前串行加载适合小型游戏；没有引入多线程资源调度、地址重定向或运行时热替换已加载 AssetBundle。新版本资源在冷启动切换。

## 音频服务

`AudioService` 是独立模块，依赖 resources。Demo 注册 Music/UI/Sfx 三组，分别用于流程背景音、设置按钮、拾取反馈。

```csharp
game.Audio.AddGroup("Music", capacity: 2, volume: 0.35f);
var sound = await game.Audio.PlayAsync("music", "Music", scope, loop: true);
game.Audio.SetMaster(0.8f, mute: false);
game.Audio.SetGroup("Music", 0.5f, mute: false);
await sound.StopAsync(fadeSeconds: 0.2f);
```

支持分组并发上限、主/组/实例音量相乘、主/组静音、循环播放、淡出、查询和幂等停止。静音和音量对淡出中的声音同样立即生效。每次播放拥有自己的作用域与资源租约，播放结束、owner 退出或模块关闭会回收；完成后注销父作用域清理登记，避免持续音效累积回调。

AudioDriver 只检查当前播放实例，回收自然结束或所属作用域已取消的声音。ModuleHost 仍不轮询模块，FSM 仍只更新当前激活流程。暂停、3D 跟随、混音器、优先级抢占和音频池未纳入本次需求。
