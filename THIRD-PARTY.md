# 第三方组件

- Quaternius Universal Animation Library 1/2，CC0 1.0。用于独立训练场全身动作重定向。官方来源：https://quaternius.com/packs/universalanimationlibrary.html 与 https://quaternius.com/packs/universalanimationlibrary2.html 。固定下载镜像、提交和SHA256见 Assets/Game/Content/Training/PublicAnimations/source-manifest.json，许可原文见同目录 LICENSE.txt。

- LiteNetLib **1.3.1**，提交 `bf100dbf7b1e73aa88c929e2022c8fc765fe19df`。来源：https://github.com/RevenantX/LiteNetLib/tree/1.3.1 。Packages/com.revenantx.litenetlib 原样保存上游库源码，MIT 许可位于该目录 LICENSE.txt。上游 package.json 的版本字符串为 1.0.1-1，版本锁定以上述 Git 提交为准。仅为 M1 技术原型引入，不代表现有正式客户端的 AOT 兼容性已验证。

- HybridCLR Unity package **v9.0.3**，上游提交 `6cadd24d101f4cbd00cd80190106f6621132d24d`。来源：https://github.com/focus-creative-games/hybridclr_unity/tree/v9.0.3 。完整许可证保留于 `Packages/com.code-philosophy.hybridclr/LICENSE`。
- UGUI：来自 Unity 2022.3.62f1 安装附带的 `com.unity.ugui`。完整许可证保留于 `Packages/com.unity.ugui/LICENSE.md`。
- Unity 引擎和 Player 运行文件适用 Unity 自身许可；使用者需要满足其授权要求。

HybridCLR 兼容性参考：https://www.hybridclr.cn/docs/intro 。本项目使用冷启动加载模式，不依赖商业热重载或热补丁能力。
