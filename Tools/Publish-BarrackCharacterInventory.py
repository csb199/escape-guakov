"""Publish conservative character import triage from Audit-BarrackCharacters evidence."""
import collections,csv,html,json,re
from pathlib import Path
import sys
sys.stdout.reconfigure(encoding='utf8')
ROOT=Path(__file__).resolve().parents[1]
DATA=ROOT/'LocalOnly/Barrack/character-audit'
OUT=ROOT/'TestResults/barrack-character-inventory-2026-11-12'
OUT.mkdir(parents=True,exist_ok=True)
audit=json.loads((DATA/'raw-audit.json').read_text(encoding='utf8'));rows=audit['variants']
baseline=next(r for r in rows if r['variant']=='GrozaSR01')
def signature(m):return(m['shader'],tuple(sorted(m['keywords'])),tuple(sorted(m['disabledPasses'])))
known={signature(m)for m in baseline['materials']};knownshaders={m['shader']for m in baseline['materials']}
knownkeywords=set(k for m in baseline['materials']for k in m['keywords']);knownbehaviours=set(baseline['behaviours'])
summary=[];details=[];needs=collections.defaultdict(set)
for r in rows:
 new=[m for m in r['materials']if signature(m)not in known]
 shaders=sorted({m['shader']for m in r['materials']if m['shader']and m['shader']not in knownshaders})
 keywords=sorted({k for m in r['materials']for k in m['keywords']} - knownkeywords)
 behaviours=sorted(set(r['behaviours'])-knownbehaviours)
 reasons=[];missingcab=sorted({m['cab']for m in r['missing']if 'CAB not available'in m['reason'] and m['cab'].startswith('cab-')})
 nullmesh=sum(m['reason']=='Null mesh reference'for m in r['missing'])
 if not r['prefabs']:reasons.append('模型层级未定位（不等同于确定资源不存在）')
 if missingcab:reasons.append(f'直接引用缺 {len(missingcab)} 个 CAB')
 if nullmesh:reasons.append(f'{nullmesh} 个网格运行时绑定未恢复')
 other=[m for m in r['missing']if not ('CAB not available'in m['reason'] or m['reason']=='Null mesh reference' or m['label']=='Prefab')]
 if other:reasons.append('其他依赖/结构问题 '+str(len(other))+' 项')
 if r['variant']=='GrozaSR01':category='已导入基准';reason='闪电核心展示已接入；不宣称武器附件和全部原作逻辑完整'
 elif r['missing']:category='缺依赖';reason='；'.join(reasons)
 elif new or behaviours or r['relocations']:category='需新渲染适配';reason='核心展示静态引用可解析；未覆盖的材质组合/角色组件需验证适配'
 else:category='可完整导入';reason='核心展示静态引用和基准材质契约覆盖；仍需逐角色 Unity 运行验收'
 sub='引用缺失'if missingcab else '运行时绑定待恢复'if nullmesh else '模型入口待定位'if not r['prefabs']else '结构待核对'if r['missing']else '静态核心资产齐全'
 row=dict(角色标识=r['variant'],角色组=r['group'],分类=category,子状态=sub,模型入口=len(r['prefabs']),渲染器=len(r['renderers']),材质=len(r['materials']),动画条目=len(r['clips']),缺失CAB数=len(missingcab),网格绑定待恢复=nullmesh,依赖问题项=len(r['missing']),补充CAB数=len(r['supplement_cabs']),未覆盖材质组合数=len(new),新增Shader='; '.join(shaders),新增关键字='; '.join(keywords),新增组件='; '.join(behaviours),原因=reason,缺失CAB='; '.join(missingcab),证据文件=str(DATA/'variants'/f'{r["variant"]}.json'))
 summary.append(row)
 for m in r['missing']:details.append(dict(角色标识=r['variant'],对象=m['label'],CAB=m['cab'],PathID=m['pathID'],原因=m['reason'],候选数=m.get('candidates',0)))
 for c in missingcab:needs[c].add(r['variant'])
 r['classification']=category;r['renderingReview']=dict(newShaderNames=shaders,newKeywords=keywords,newBehaviours=behaviours,uncoveredMaterialContracts=[dict(name=m['name'],shader=m['shader'],keywords=m['keywords'],disabledPasses=m['disabledPasses'])for m in new])

def writecsv(name,data):
 with (OUT/name).open('w',encoding='utf-8-sig',newline='')as f:
  if data:w=csv.DictWriter(f,fieldnames=list(data[0]));w.writeheader();w.writerows(data)
writecsv('characters.csv',summary);writecsv('dependency-issues.csv',details)
writecsv('missing-cabs.csv',[dict(CAB=c,影响变体数=len(v),角色标识='; '.join(sorted(v)))for c,v in sorted(needs.items(),key=lambda x:(-len(x[1]),x[0]))])
groups={r['group']for r in rows};excluded=[]
for p in sorted(Path(audit['source'],'extracted/bundle_sets').glob('characters_*.json')):
 token=p.stem.removeprefix('characters_')
 if token not in groups:excluded.append(dict(分组=token,状态='未识别到精确角色标识的整备室动画',说明='可能为道具、敌人、别名或无整备室角色；未断言不支持',证据=str(p)))
writecsv('other-resource-groups.csv',excluded)
counts=collections.Counter(r['分类']for r in summary);counts={k:counts[k]for k in ['可完整导入','缺依赖','需新渲染适配','已导入基准']}
report=dict(date='2026-11-12',scope='LOD0核心模型、骨骼、材质贴图、原生Shader、核心整备室动画；角色换装逐项统计',limitations=['静态审计，不是新角色运行验收','补充资源取自本项目已有恢复文件，不能视作gfresource原始目录已齐全','未找到精确模型入口只能判定待定位，不能断言游戏没有资源','缺依赖优先分类，同时保留新Shader/关键字/组件的适配风险','不证明动作外部武器/道具、物理、IK和镜头与原作等价','按名称识别候选，可能漏掉别名和共享模型映射；PathID替代候选不等于已验证绑定','未覆盖材质组合只表示尚未验收，不断言必须新增Shader代码'],counts=counts,characterGroups=len(groups),variantCount=len(rows),exactModelHierarchies=sum(bool(r['prefabs'])for r in rows),parsedCabs=audit['baseCabCount'],combinedCabs=audit['combinedCabCount'],missingCabs=len(needs),excludedGroups=len(excluded),sourceCreated=audit['created'],variants=rows)
(OUT/'inventory.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
md=['# 整备室角色导入清单','', '生成日期：2026-11-12。基于本次重新读取的解析对象与项目补充包，未用历史缺失统计代替当前结果。','',
 f'识别 **{len(groups)} 个角色标识组、{len(rows)} 个角色/换装变体**，其中 {report["exactModelHierarchies"]} 个有精确同名模型层级入口。另列出 {len(excluded)} 个未识别到对应整备室动画的资源分组。分组不是官方角色数量。','',
 '## 分类汇总','', '| 分类 | 变体数 | 含义 |','| --- | ---: | --- |',
 f'| 可完整导入 | {counts["可完整导入"]} | 新角色核心资产齐全，且现有材质/组件契约覆盖；本轮没有可直接承诺的条目 |',
 f'| 缺依赖 | {counts["缺依赖"]} | 包括确认的引用缺失、网格运行时绑定未恢复、模型入口待定位；详见子状态 |',
 f'| 需新渲染适配 | {counts["需新渲染适配"]} | 核心静态资源可解析，但有未验证的材质组合或组件；不意味着必然重写 Shader |',
 f'| 已导入基准 | {counts["已导入基准"]} | GrozaSR01；单列，不冒充本轮新增的完整角色 |','',
 '## 优先处理','',
 '- **DusevnyjSSR01**：核心模型、材质、纹理、骨骼及整备室动作静态检查未发现阻塞；6 个渲染器。具有闪电未覆盖的材质关键字/Pass 组合与 DormMeshInfo 组件，适合先做批量导入器的第二个角色验证。',
 '- **PhaetusaSSR01**：核心引用静态检查未发现阻塞；10 个渲染器。含额外武器/附件、材质组合、GrounderIK、LegIK、LobbyCharacterParams 等组件，需要分清角色主体与剧情层级内容。',
 '- **ClukaySSR01、FlorenceSSR01、ZhaohuiSSR01**：本轮选中层级的主要阻塞是运行时网格绑定尚未恢复；不能据此断言网格文件不存在。',
 '- **KseniaSR01、TololoSSR01**：除网格绑定外，还分别缺可解析的 Avatar 引用。','',
 '## 口径和边界','',
 f'- 扫描解析目录中可用的 {audit["baseCabCount"]} 个 CAB，合并项目已有补充包后共 {audit["combinedCabCount"]} 个 CAB 地址；新读取补充索引没有解析异常。没有修改源游戏、解析目录或当前 Unity 场景。',
 '- 模型可以来自独立预制体或剧情场景里的同名子树。多候选选取缺失项最少者，完整替代入口信息保留在 JSON；不将剧情子树自动认定为原作默认整备室预制体。',
 '- MeshResList 的空网格槽不直接等同于文件缺失：只有精确同名、声明路径一致且其他渲染器存在唯一真实网格引用时才恢复检查；其余标记绑定待恢复。',
 '- 检查了可达网格、bindpose/骨骼数量、骨骼所有权、材质、Shader 引用、非空贴图像素，以及本变体待机/入场动画可用性。完整动作继承、事件外部道具、动态物理和镜头逻辑仍需运行验证。',
 '- 缺失类中的“模型入口待定位”不代表确定缺文件，可能还需角色表、换装表、运行时映射或新解析资源；不得据此要求重复下载全部角色。',
 '- 渲染适配以闪电已接入材质的 Shader/关键字/禁用 Pass 组合为基准，不把“Shader 名字相同”当作已验证。Shader 变体是否实际需要新增代码需在 Unity/截帧中决定。',
 '- “补充 CAB 数”仅统计本轮解析到的模型/材质等引用使用了原解析目录之外的包，是下界，非完整场景依赖包总量。',
 '- 本轮未批量创建 Unity 资产、未执行新角色渲染、未进行发布构建；清单用于排期与补证据。','',
 '## 所有角色与换装','', '| 角色/换装 | 分类 | 渲染器 | 缺CAB | 待绑定网格 | 未覆盖材质组合 | 原因 |','| --- | --- | ---: | ---: | ---: | ---: | --- |']
for r in summary:md.append(f'| {r["角色标识"]} | {r["分类"]} | {r["渲染器"]} | {r["缺失CAB数"]} | {r["网格绑定待恢复"]} | {r["未覆盖材质组合数"]} | {r["原因"]} |')
md+=['','## 文件','', '- `characters.csv`：全部变体及分类，可筛选。','- `dependency-issues.csv`：逐个对象的 CAB、PathID 和缺失/未恢复原因。','- `missing-cabs.csv`：确认无法解析的 CAB 与受影响变体。','- `other-resource-groups.csv`：未纳入角色变体统计的名称分组。','- `inventory.json`：完整可追踪证据，包括每个候选模型、材质和纹理引用。','- `index.html`：本地可搜索、分类筛选的清单。','', '复跑：先执行 `Tools/Audit-BarrackCharacters.py`，再执行 `Tools/Publish-BarrackCharacterInventory.py`。读取 Unity 资源使用 F:/gfresource/tools/python 下现有解析库；审计只写项目报告和 LocalOnly 缓存。']
(OUT/'README.md').write_text('\n'.join(md)+'\n',encoding='utf8')
cols=['角色标识','分类','子状态','渲染器','材质','缺失CAB数','网格绑定待恢复','未覆盖材质组合数','新增Shader','新增关键字','原因']
body=''.join('<tr data-category="'+html.escape(r['分类'])+'">'+''.join('<td>'+html.escape(str(r[c]))+'</td>'for c in cols)+'</tr>'for r in summary)
page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>整备室角色导入清单</title><style>body{font:15px system-ui;background:#101722;color:#e6edf3;padding:28px}h1{font-size:27px}p{max-width:1050px;line-height:1.7;color:#b5c4d5}input,select{padding:10px;margin:0 12px 16px 0;background:#1b293b;color:white;border:1px solid #53647b;border-radius:5px}table{border-collapse:collapse;width:100%;font-size:13px}th{position:sticky;top:0;background:#263a50;text-align:left}td,th{border-bottom:1px solid #304056;padding:10px;vertical-align:top}tr:nth-child(even){background:#172332}td:first-child{font-weight:600;white-space:nowrap}a{color:#82c3ff}.cards{display:flex;gap:12px;flex-wrap:wrap;margin:20px 0}.card{background:#203149;padding:16px;border-radius:8px}b{color:#9ed5ff}</style><h1>整备室角色导入清单</h1><p>2026-11-12 · 本地静态审计。范围为角色/换装核心展示资产；缺依赖包含“已确认引用缺失”和“绑定/入口待恢复”。不把未定位当成文件不存在，不把新材质组合当成必需重写 Shader。</p>'''
page+='<div class="cards">'+''.join('<div class="card">'+html.escape(k)+' <b>'+str(v)+'</b></div>'for k,v in counts.items())+'</div>'
page+='<p>优先验证 DusevnyjSSR01 和 PhaetusaSSR01；GrozaSR01 为既有基准。资源齐全的判定已使用项目中后来补齐的包，不表示 gfresource 原始目录单独齐全。<a href="README.md">完整口径与说明</a> · <a href="characters.csv">CSV 清单</a> · <a href="dependency-issues.csv">逐项依赖</a></p>'
page+='<input id="q" placeholder="搜索角色、关键字或缺失原因" size="44"><select id="cat"><option value="">所有分类</option>'+''.join('<option>'+html.escape(k)+'</option>'for k in counts)+'</select><span id="count"></span><table><thead><tr>'+''.join('<th>'+c+'</th>'for c in cols)+'</tr></thead><tbody>'+body+'</tbody></table>'
page+='''<script>const rows=[...document.querySelectorAll('tbody tr')],q=document.querySelector('#q'),cat=document.querySelector('#cat');function filter(){let n=0;for(const row of rows){const yes=(!cat.value||row.dataset.category===cat.value)&&row.textContent.toLowerCase().includes(q.value.toLowerCase());row.hidden=!yes;n+=yes}document.querySelector('#count').textContent=n+' 个变体'}q.addEventListener('input',filter);cat.addEventListener('change',filter);filter();</script></html>'''
(OUT/'index.html').write_text(page,encoding='utf8')
print(json.dumps(dict(counts=counts,groups=len(groups),variants=len(rows),missingCabs=len(needs),excludedGroups=len(excluded),output=str(OUT)),ensure_ascii=False,indent=2))
