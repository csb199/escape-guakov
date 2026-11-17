# 整备室源布局恢复记录

日期：2026-11-11。状态：**部分恢复，用户要求的完整整备室尚未完成**。

## 已保存

- Assets/Game/Scenes/BarrackSource.unity：真实源预制体的 99 个节点、2 台相机和 3 盏灯。源相机禁用，未接入原作渲染层和相机栈。
- Tools/Export-BarrackSource.py：可重复提取原始层级、组件和缺失依赖；不更改源游戏。
- Assets/Game/Editor/Barrack/BarrackSourceImporter.cs：仅 Editor 的布局导入和校验。
- Assets/Game/Content/BarrackSource：原始组件记录与依赖清单。

## 本次实际检查

- Python 源布局校验通过；独立重新提取的 layout.json 与工程内文件逐字节一致。
- Unity 6000.3.27f2 隔离工程中编译导入器，导入后检查节点名、父子关系、局部位置/旋转/缩放、激活状态、相机/灯光组件存在性和缺失脚本。
- 原生资源包加载探针：f8cb571ee0ce581c4aa2f1054595f2a2.bundle 的 c_GrozaR01_skin 有 12 个 SkinnedMeshRenderer，12 个 Mesh 可加载，骨骼引用非空合计 234，但非空材质数量为 0。234 是跨渲染器引用总数，不是独立骨骼数量。
- 源整备室包递归引用闭包中有 164 个 CAB 未在已有本机索引定位，完整列表见 layout.json 的 missingDependencies。

## 未完成与阻塞

- 尚未导入可展示的完整角色名单；原解析库的 129 个角色分类包含 NPC、道具和局部素材，不能当作 129 名完整角色。
- 未恢复原作 UI 事件逻辑、动作切换、触摸交互、镜头状态机与运行时材质参数。保留组件数据不等于这些功能已经实现。
  - 定制 URP 的关键依赖在索引中缺失：CAB-aa8a0bfb6f054816b7aa6b8f871769b2、CAB-ed2f918160b43247ec86605e1564d98d、CAB-93e96eb7612a47a89533022aaa7305de。未导入定制渲染管线、后处理或替代 Shader。
- 未实现通过 IGameEntry、SceneService、EntityService、StateScope 的完整业务接入；AOT 稳定层保持原状。源布局不加入 Player 构建。

## 验证边界

根工程声明 Unity 2022.3.62f1，本机找到的是 6000.3.27f2。为避免升级根工程，编译与生成在 LocalOnly/Barrack/UnityProbe 隔离工程运行，生成的场景及其 .meta 保存到根工程；没有修改根工程版本和 Packages。本机包管理器启动失败，隔离检查使用 -noUpm。

本次不是根工程 Unity 2022 验证、Play Mode 画面验收、完整 Player 构建或热更验收。没有原作同姿态/同机位画面对比，不能宣称画风贴合原作。当前场景是源布局恢复基线，打开后不会出现完整角色或可操作 UI。

本机日志：LocalOnly/Barrack/layout-import.log、layout-validate.log、probe-model.txt。日志和运行目录不提交。继续完整恢复需要补齐上述依赖、角色完整材质/预制体和定制管线实现，并在项目要求的 Unity 版本完成运行与发布验证。
