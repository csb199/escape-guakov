# 闪电源资源与原生库

本目录包含从本机原作默认 GrozaSR01 预制体及精确 CAB/PathID 依赖恢复的资源。不是完整整备室成品，也不是原作渲染画风验收结果。

- GrozaSR01.prefab：5 个 LOD0 网格、204 个骨骼引用、10 个材质；保留默认模型层级，删除无法在宿主执行的原作组件。
- Assets：5 个网格、21 张按原格式/mip/色彩空间恢复的贴图、10 个材质、Avatar 和 4 个 Shader 编辑器引用文件。
- OriginalCharacterShaders.bytes：原生编译 Shader 包。Shader 引用文件保留名称与属性，不绘制角色；原生 Shader 不能直接克隆并持久化其 D3D 字节码，显示前必须由 BarrackShaderLibrary 克隆实例材质并绑定此库。
- OriginalBarrackAnimations.bytes：原作压缩动画包，包含 11 段已识别整备室动作。不得直接克隆后保存成 .anim；本次验证这种方式会丢失运行时曲线。BarrackAnimationLibrary 保留原对象，角色 Animator.fireEvents=false，宿主负责交互事件。
- import-report.json：本次导入对象计数，不是完整画面或发布通过标记。

根工程已迁移到 Unity 6000.3.27f2。原生字节码仅验证 Windows D3D11，不代表其他图形后端或平台可用。

资源生命周期：ResourceService 加载原生库的 TextAsset 租约；StateScope 依次持有库与角色。退出时销毁 PlayableGraph、恢复并释放实例材质，随后卸载动画库和 Shader 库，最后释放场景。编辑器默认 Barrack 场景由 BarrackPreview 直接持有资源引用；签名发布路径继续通过 BarrackProcedure 接入资源租约。

