# 整备室浏览与触摸接入验证

日期：2026-11-12，Asia/Shanghai。验证根工程本轮源码，未运行 Player/AOT/热更发布验收。

## 改动与证据

- 浏览镜头由展示宿主拥有 `VirtualCameraRoot/ShotVirtualCamera`，使用角色源 `CameraPoint` 作为旋转基准。进入浏览继承实际相机完整姿态；不再由渲染器包围盒中心、距离和屏幕偏移重建镜头。滚轮沿当前镜头局部前向移动，源旋转/距离限值与阻尼继续生效。
- 返回恢复捕获的默认构图；头部触摸不再强制进入独立的 Look 特写。换角和关闭释放镜头根，候选角色加载失败仍保留旧会话。
- `Tools/Export-BarrackInteraction.py` 从当前导入计划的源资产及只读 GunData/BarrackInteractData 导出十份契约，共 55 个源胶囊。保留路径、中心、半径、高度、方向、跟随骨骼、事件和动作条目；`.meta` 已由 Unity 生成，场景和展示预制体已绑定。
- 触摸命中使用最近的胶囊表面，武器区域跟随 Weapon_Socket。源事件分组/序号映射到本角色可用动作，缺失动作不跨角色替换。闪电头部 1011 对应 Head，左右腿 1510/1511 均对应 Pocket，胸部 1030 使用 Stretch/Around 动作组，武器使用自己的动作组。
- 左键按下、拖动、释放、点击属于同一个角色交互会话。拖动后不点击，右键与零滚轮不进入浏览，角色切换使旧指针手势失效。
- 实帧旋转测试发现斜裁剪反射投影会使 Unity 的 TryGetCullingParameters 报视锥错误。改为用普通反射投影保守剔除，再用原斜裁剪投影绘制；重跑未再出现该错误。

## 本次运行

| 检查 | 结果 | 新结果时间 |
| --- | --- | --- |
| .NET 9，Tests/Framework.Tests.csproj | PASS，193 项 | 21:22:37 |
| BarrackRosterValidation | PASS，388 项；十角色名单、12 次往返切换、旧手势失效、镜头根唯一、动作/画面/清理 | 21:30:14 |
| Tools/Test-BarrackEditor.ps1 | PASS，284 项；两轮完整交互与渲染差分、加载中取消、异步释放 | 21:34:15 |

新增 BarrackBrowsingValidation 覆盖非零宿主位置/旋转下的父节点所有权、CameraPoint 基准、零输入进入不跳位、局部轴缩放及反向恢复、旋转半径、返回复位、Head/Look 区分、源配置目标路径、源事件动作分派、右键/零滚轮/拖动释放误触。朝晖的源头部胶囊高度为零，按 Unity 胶囊退化球体的有效形状保留，未人为改写源尺寸。

本机详细证据（不上传运行目录）：

- `LocalOnly/Barrack/live/full-validation.txt` 与 `full-validation.log`。
- `LocalOnly/Barrack/character-import/validation/result.txt` 与 `LocalOnly/Barrack/interaction-roster.log`。
- `LocalOnly/Barrack/live/barrack-browse.png`、`barrack-browse-zoom.png`、`barrack-ui.png` 已人工式图像检查。浏览截图覆盖从特写返回过渡中接管镜头再旋转/缩放；默认构图与稳定性另有数值和实帧检查。

## 验证源码 SHA256

| 文件（Assets/Game/HotUpdate/Barrack 下） | SHA256 |
| --- | --- |
| BarrackCameraRig.cs | 7083BC6176CD177417D856AFE7157690DCB9750E71891AF294E4B22AD75BA2D3 |
| BarrackTouchConfiguration.cs | 56B913581941018E35171582CE9AB6489AE762124D95BEFE69725D0B57FD650C |
| BarrackPreview.cs | BD1317A4031F2640C2DD16F43EDA20828A4CC090D44F1B205F414F4D8954F87A |
| BarrackView.cs | 2333F9706DB46260DBFC3391BA4945D2CADA880EE255394752E073762B367A9A |
| BarrackRenderPipelineAsset.cs | 50ECF7D842306BC786FA7D24829ED642CF73B6E9A32605570983229283F5A004 |

## 边界

源原生脚本的方法体不能直接作为本宿主可执行实现。这里恢复了源资产层级基准、触摸形状与动作数据，并验证宿主浏览/点击行为；尚未完成原作客户端逐操作对拍。账号解锁条件、所有动作专属镜头轨迹、逐动作旋转/缩放范围表及执行器完整状态分支未全部恢复，不能据本报告声称整个整备室已逐分支或逐像素等价。此次仅修改 Barrack 示例接入及其资源/检查，不将交互规则移入通用框架。
