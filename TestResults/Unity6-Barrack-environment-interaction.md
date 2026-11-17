# Unity 6 整备室环境与交互验收（2026-11-12）

工程 F:/escape-guakov，Unity 6000.3.27f2，Windows D3D11。以下为根工程新源码的本轮验证，不使用旧 OfflineProject 或仅角色预览结果代替。

## 接通范围

- 默认开发场景 Assets/Game/Scenes/Barrack.unity。运行时装载原生 04Aimo_Training_Platform_GFPC；726 个去重后的依赖包，原缺失 CAB 闭包问题已解决。
- 751 个源背景 Renderer（含非激活对象）、源烘焙 lightmap、8 个 light probes、环境球谐及 HDR cubemap。源主灯、4096 阴影图、两级级联与源距离配置进入 Unity 6 渲染路径。
- 原生角色 Shader、五个网格、骨骼、十份材质、原纹理和 11 个源压缩动作。修复 Unity 6 MRT 兼容导致角色只剩黑色轮廓的问题。
- 动态阴影真实绘制角色深度；地面阴影接收使用宿主适配 Pass。镜像相机绘制平面反射，并保留源 cubemap；AO、Bloom、雾、色调映射、饱和度、暗角使用默认场景的配置值。
- 八个 UGUI 动作入口、头/手/腰骨骼触摸、拖动旋转、滚轮缩放、返回、显示隐藏及渲染效果开关；动作与镜头均有状态管理、繁忙互斥、过渡和回待机。
- BarrackPresentation.prefab 可通过 BarrackProcedure 的 presentationPrefabAddress 接入已有资源目录和实体服务。StateScope 先取消并等待原生资源清理，再释放实体及 prefab 资源租约。未改动 AOT 稳定层业务边界。

## 验证方法与结果

运行 Tools/Test-BarrackEditor.ps1（BarrackFullValidation.Run）：两轮实际 Play Mode，使用 StandardRequest 绘制 GPU 帧；固定角色、粒子、镜头与时间后分别关闭阴影/AO/反射/后处理，检查图像差分。另检查角色面部亮度，避免“背景有图像但角色全黑”被当成通过。

覆盖八个动作入口、忙碌拒绝重入、原 ToIdle 返回、特写镜头位移、真实屏幕射线命中头骨骼、拖动环绕、完整背景光照资源、两轮 StateScope 关闭与原生包释放。第三轮重现自动 Start 先开始加载、框架稍后绑定取消令牌的时序，确认取消能停止加载并释放资源。

运行本地 .NET 9 SDK：dotnet run --project Tests/Framework.Tests.csproj --configuration Release，191 项通过，包含动作状态机的入场、互斥、返回、取消及关闭后拒绝旧回调。

本轮日志和图像位于 LocalOnly/Barrack/live：full-validation.txt、full-validation.log、core-and-interaction-tests.log、barrack-ui.png、barrack-full.png、barrack-no-shadows.png、barrack-no-ao.png、barrack-no-reflections.png、barrack-no-post.png、face-focus.png、shadow-atlas.png。此目录不提交发布。

## 实现边界

原生模型、纹理、材质 Shader、场景、烘焙数据、动作和配置来自原作；UI/镜头执行器、地面阴影接收及后处理为 Unity 6 宿主重建。时域滤波尚无原版完整骨骼运动向量链，不能等同原版 TAA。未取得原作同机位实帧，不能宣称逐像素一致或原客户端完整管线二进制移植。

源场景载入仍产生每次两条缺失源脚本的 Warning；场景组件适配已移除 11 个原客户端 MonoBehaviour，剩余预载警告没有作为项目脚本执行。新验证不接受 Error / Exception / Assert。此轮没有执行完整 Player/AOT/热更构建；已提供框架接入契约，不将 Editor 验证冒充发布通过。

## 最后一次运行证据

验证完成：2026-11-12 04:17:03 +08:00。根工程结果 PASS，未检出编译错误或运行 Error/Exception/Assert。

冻结场景的平均绝对像素差（0–255，阈值 0.005）：
- shadows changes actual pixels; mean absolute difference=0.06422
- ambient occlusion changes actual pixels; mean absolute difference=0.82883
- reflections changes actual pixels; mean absolute difference=2.33274
- post processing changes actual pixels; mean absolute difference=22.43363
