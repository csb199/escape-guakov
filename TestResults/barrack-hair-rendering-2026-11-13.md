# 闪电头部模板与透明发丝渲染校对

日期：2026-11-13（Asia/Shanghai）。根工程源码修改后经 Tools/Sync-GameSource.ps1 同步到隔离验证镜像，Unity 6000.3.27f2 / D3D11；未关闭根工程正在打开的编辑器。最后一次普通 Editor 回归结果写入时间为 11:37:59。

## 原作证据

原作 barrack-direct-valid.rdc，frame 8378。重新回放并导出实际 GPU 状态，不仅依据静态 RendererData 或历史关键词映射。

| 原作事件 | 通道 | 实际作用 |
|---|---|---|
| 2302 → 2313 | 头发、刘海前向 | 先绘制头发，再绘制刘海；刘海模板写掩码为 248 |
| 2324 | GFHairShadow | 颜色写掩码 0，不写深度；模板 Equal 200，成功时饱和递增 |
| 2349 → 2370 | 眼部 add / multiply | 头发模板投影之后、面部阴影之前叠加 |
| 2407 | GFCharFaceShadow | 模板 Equal 201，成功时饱和递减；写 RGB，不写深度 |
| 2430 | GFCharHairTransE | 模板 Equal 204，保持模板；SrcAlpha / OneMinusSrcAlpha，写 RGB 和深度 |

三个通道均保持原生 Shader，不手写近似公式或改写材质模板值。原作眼部叠加 Shader 的 Queue 标签为 Geometry+458 / Geometry+459。

先前 matched-shader-variants.json 使用单值哈希映射，丢弃了同字节码的其他关键词组合。本次完整匹配确认：GFCharFaceShadow 的像素字节码在有/无 _GF_CHAR_LOD1 时相同。因此不能把该关键词缺失当作已经证实的根因，也未强制补入这个关键词。

## 实现修正

- 主体前向和眼部叠加按队列分段；头发模板投影夹在两者之间。
- 脸部阴影、脸部标记和透明刘海独立提交，透明刘海不再通过 GFCharHairTransBattle 等候选标签选择。
- 闪电材质契约添加 hostRenderQueue 排序槽：头发 2440，刘海 2441。它们只表达捕获证实的相对顺序，并非原作运行时队列的实测数值。恢复了眼睛之后先头发再刘海、随后眼部叠加的顺序。
- 主深度显式保留 D32_SFloat_S8_UInt；不覆盖原生材质的模板比较、读写掩码、混合或深度状态。
- 主相机和反射相机共用 feature 调度。新增队列信息由环境导出工具和角色导入契约生成工具保留。
- 未修改既有 GlobalCharOutlineZBias=3。曾尝试的零偏移产生明显内部黑线，已撤回，未将其混入本次头部模板修复。

## 新验证

1. 新 GPU 捕获 character-ordered_capture.rdc：92 项比较全部通过。覆盖头发模板、眼部加法/乘法、脸部阴影和透明刘海的 VS/PS 字节码、深度/模板状态、混合、剔除、深度模板格式及相对绘制顺序。项目事件分别为 2866、2900、2909、2953、3004；头发/刘海前向为 2831、2841。
2. 开发中捕获到反例：未固定排序时项目先绘制刘海再绘制头发；透明刘海开关在多个角度无像素变化。固定排序后通过像素验证。
3. Tools/Test-BarrackEditor.ps1 -Offline：普通无 RenderDoc 注入路径 PASS，660 项检查；包含两轮动作/浏览/效果检查，以及加载中取消和原生包释放。
4. 最后一次冻结姿态像素验证：关闭 GFHairShadow 和 GFCharFaceShadow 的 RGB 总差分别为 24034、24034；关闭 GFCharHairTransE 为 732。这些是开关贡献检测，不是对原作的误差评分。
5. Python 工具语法检查通过；从原始 RendererData 重新生成 feature 列表与根工程 environment.json 完全一致。

## 本机证据位置

- LocalOnly/Barrack/hair-source/passes.json：原作实际状态；同目录保留 VS/PS 字节码、反汇编和各事件输出图。
- LocalOnly/Barrack/hair-host/ordered/passes.json、comparison.json：新项目 GPU 状态和 92 项结果。
- LocalOnly/Barrack/hair-host/character-ordered_capture.rdc：有效项目捕获。
- LocalOnly/Barrack/hair-host/ordered/full-validation.txt：RenderDoc 注入环境回归结果。
- LocalOnly/OfflineProject/LocalOnly/Barrack/live/full-validation.txt、full-validation.log、hair-*.png、barrack-full.png：最后普通 Editor 回归及画面。

早期 character_capture.rdc、character-renderthread_capture.rdc、character-allcmd_capture.rdc 是空捕获，不作为证据。Unity 多设备环境需要使用目标纹理所属的 D3D11 设备捕获，工具已经修正。

验证版本 SHA256：

- BarrackRenderPipelineAsset.cs：81DD9BA8D856B468894B82FD259325D43D5D555E0B29F1D3BFEFFDE5622EA380
- BarrackShaderLibrary.cs：AEBC866629100E02BCE37ACD94083DB80D610901EF2BA6CFD40150EDD41DDEF7
- Stage/environment.json：F3AB04BB13B6731A7A1BAFCEF8DD282EBBBC88140B11C538EAE2A5179DA07D42
- GrozaSR01/material-contract.json：7BAF59549F0CAFA197EDC238A355476879CEE1CC9D6EBF36DA23A6ECD968971B

## 边界

通过的是本次头部通道状态、顺序与实际像素贡献；源帧与项目验证姿态、分辨率不同，不宣称最终头部画面逐像素一致。闪电排序契约没有扩展为所有角色的已验证排序。反射投影、主光阴影视锥、完整时域一致性仍需独立验收。未执行 Player/AOT/签名发布与热更构建。
