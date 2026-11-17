# 移除3C并保留整备室

2026-11-17，按用户要求移除Training/TrainingV2源码、场景、专用资源、卡拉比丘动画适配、启动与构建/验证工具。保留Barrack源码、资源、场景及通用框架。批量永久删除被自动审批拦截，改为移入Git忽略的LocalOnly/Removed3C-20261117；这些文件不参与编译或提交。独立网络实验不属于本次提交。

清理了Demo程序集过滤器中的TrainingV2特殊放行，以及目录入口中的3C链接。根源码通过Sync-GameSource同步到隔离工程，运行Tools/Test-BarrackEditor.ps1 -Offline，得到本轮新生成PASS。覆盖真实整备室加载、原生渲染、角色交互/浏览、动作返回和资源关闭；源包缺失脚本/后备Shader警告仍存在，不把通过视为原作像素等价或完整发布验收。

整备室大体积asset/bytes/ttf使用Git LFS。克隆后需git lfs pull。LocalOnly、Player、签名密钥和生成签名配置不提交。完整AOT/热更发布不在本次验收范围。
