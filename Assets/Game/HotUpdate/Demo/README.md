# 框架接入示例

本目录仅用于框架能力展示与串联验证；具体业务可以替换，不构成框架功能定义。

## 阅读入口

| 目录 | 职责 | 文件入口 |
|---|---|---|
| Entry | 入口注册、生命周期和帧驱动 | [GameEntry.cs](Entry/GameEntry.cs) |
| Procedures | 流程编排与资源所有权 | [DemoProcedure.cs](Procedures/DemoProcedure.cs) |
| Entities | 实体自身行为与回收钩子 | [EnergyOrbLogic.cs](Entities/EnergyOrbLogic.cs) |
| UI | UIForm 接入与展示 | [DemoView.cs](UI/DemoView.cs) |
| Services | 业务模块、数据与传输适配 | [DemoServices.cs](Services/DemoServices.cs) |
| Validation | 跨流程验证与退出探针 | [DemoValidation.cs](Validation/DemoValidation.cs) |
