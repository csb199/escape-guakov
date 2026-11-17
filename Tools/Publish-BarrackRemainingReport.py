"""Publish the measured barrack roster and unresolved default-model binding evidence."""
import ast,datetime,hashlib,importlib.util,json,math,re,shutil
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'TestResults/barrack-remaining-import-2026-11-13'
OUT.mkdir(exist_ok=True)
CONTENT=ROOT/'Assets/Game/Content/BarrackSource'
LOCAL=ROOT/'LocalOnly/Barrack/remaining-import'
VALID=ROOT/'LocalOnly/Barrack/character-import/validation'
roster=json.loads((CONTENT/'roster.json').read_text(encoding='utf8'))['characters'];ids={e['id'] for e in roster}
accepted=set((VALID/'accepted-characters.txt').read_text(encoding='utf8').splitlines())
assert ids==accepted,'Roster acceptance is incomplete'
assert (VALID/'result.txt').read_text(encoding='utf8').startswith('PASS')
cs=list((ROOT/'Assets/Game/HotUpdate/Barrack').glob('*.cs'))+list((ROOT/'Assets/Game/Editor/Barrack').glob('*.cs'))
assert (VALID/'result.txt').stat().st_mtime>=max(p.stat().st_mtime for p in cs),'Roster result predates source edits'
full=ROOT/'LocalOnly/Barrack/live/full-validation.txt'
assert full.read_text(encoding='utf8').startswith('PASS') and full.stat().st_mtime>=max(p.stat().st_mtime for p in cs),'Full validation is stale or failed'
raw=json.loads((ROOT/'LocalOnly/Barrack/next-batch-audit/raw-audit.json').read_text(encoding='utf8'))
# Per-variant files include the corrected, exact inner model roots.
variants=[json.loads((ROOT/'LocalOnly/Barrack/next-batch-audit/variants'/(v['variant']+'.json')).read_text(encoding='utf8')) for v in raw['variants']]
status={x['character']:x for x in json.loads((LOCAL/'batch-status.json').read_text(encoding='utf8'))}
s=importlib.util.spec_from_file_location('interaction',ROOT/'Tools/Export-BarrackInteraction.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
langs=json.loads((ROOT/'LocalOnly/Barrack/live/ui-localization.json').read_text(encoding='utf8'))['langs']
names={r[5].decode('utf8'):langs[str(m.fields(r[2])[1][0])]['2'] for r in m.table(Path('E:/Games/GF2Exilium/GF2 Game/GF2_Exilium_Data/LocalCache/Data/Table/GunData.bytes')) if 5 in r and 2 in r}
known={m['shader'] for v in variants if v['variant'] in ids for m in v['materials']}
rows=[]
for v in variants:
 key=v['variant'];default=bool(re.fullmatch(r'[A-Za-z]+(?:SSR|SR|R)01',key));failure=status.get(key,{}).get('reason','');last=failure.splitlines()[-1] if failure else ''
 row=dict(id=key,name=names.get(key[:-2],key),defaultOutfit=default,status='editor_verified' if key in ids else 'binding_evidence_required' if default else 'outfit_not_integrated',firstBlockingIssue=last,unverifiedShaderFamilies=sorted({m['shader'] for m in v['materials']}-known))
 if default and key not in ids:
  path=ROOT/'LocalOnly/Barrack/character-import'/key/'mesh-resolution.json'
  if path.exists():
   resolution=json.loads(path.read_text(encoding='utf8'));row['meshEvidence']=resolution['bindings']+resolution['gaps']
  row['requiredEvidence']='原作默认整备室的精确 MeshResPath 到资源 CAB/PathID 映射，或该角色截帧中的网格/索引/骨骼绑定及材质证据；普通整屏截图不足以区分同名几何版本。'
 rows.append(row)
(OUT/'all-variants-status.json').write_text(json.dumps(dict(created=datetime.datetime.now().astimezone().isoformat(),roster=roster,variants=rows),ensure_ascii=False,indent=2),encoding='utf8')
frames=OUT/'frames';frames.mkdir(exist_ok=True)
for e in roster:
 for suffix in ['.png','-ui.png']:shutil.copy2(VALID/(e['id']+suffix),frames/(e['id']+suffix))
shutil.copy2(VALID/'result.txt',OUT/'roster-validation.txt');shutil.copy2(full,OUT/'full-validation.txt')
new=roster[10:];w,h=256,340;sheet=Image.new('RGB',(w*5,h*math.ceil(len(new)/5)),(20,25,27));draw=ImageDraw.Draw(sheet);font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',17)
for i,e in enumerate(new):
 im=Image.open(frames/(e['id']+'.png')).crop((350,0,900,720));im.thumbnail((w,302));x=(i%5)*w+(w-im.width)//2;y=(i//5)*h;sheet.paste(im,(x,y));draw.text(((i%5)*w+8,y+305),e['displayName'],font=font,fill='white')
sheet.save(OUT/'new-character-contact-sheet.jpg',quality=92)
def sha(p):
 with p.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
files=cs+[CONTENT/'roster.json',ROOT/'Assets/Game/Scenes/Barrack.unity',CONTENT/'Stage/Stage.asset',CONTENT/'Stage/BarrackPresentation.prefab']+list((ROOT/'Tools').glob('*Barrack*.py'))
(OUT/'source-sha256.json').write_text(json.dumps({p.relative_to(ROOT).as_posix():sha(p) for p in files},indent=2),encoding='utf8')
blocked=[x for x in rows if x['defaultOutfit'] and x['id'] not in ids];checks=len((VALID/'result.txt').read_text(encoding='utf8').splitlines())-1
text=f'''# 整备室剩余角色导入验收（2026-11-13）

本轮新增 **{len(new)}** 名默认角色，名单合计 **{len(roster)}** 名。解析资源共有 68 个默认角色、155 个含服装变体条目；剩余 **{len(blocked)}** 个默认角色仍需资源绑定证据，另外 87 个服装变体未接入。本报告不宣称所有解析角色均已完成。

## 本轮新增

| 中文名称（原作语言表） | 资源 ID |
|---|---|
'''
text+=''.join(f"| {e['displayName']} | `{e['id']}` |\n" for e in new)
text+='''
## 接入与修复

- 左侧三列角色卡片增加滚轮/拖拽滚动；中文名称、原头像、触摸胶囊和 CameraPoint 采用各自源数据。未实现的账户数值控件仍不显示。
- 按逻辑分段恢复原物理索引漏掉的模型和网格。首批 131 个 CAB 根引用及后续模型入口的依赖闭包均已补齐；“文件存在”与“准确绑定”分开记录。
- 绑定校验覆盖源骨骼顺序、完整绑定姿势、Avatar 静止姿势；同名不同几何或不同表情数据的候选继续阻塞。仅 m_MeshUsageFlags 不同不视为几何差异。
- 主动画包里的唯一片段不会因为其他资源包存在同名动画而被误删；修复 CeciliaSSR01 返待机动作。保留压缩原生动画，未借用其他角色动作。
- 修复同名容器导致实际角色子树未被选择的问题，保留 GUID；镜头锚点和胶囊路径随准确子树重新导出。
- 刚性附件也排除低级 LOD；没有 lod0 后缀的蒙皮饰品仍参与绑定。
- 修复 DestroyerSSR01、BiyocaSSR01 黑色轮廓：源预制体是第 0 层，被源 CharForward 的 24576 掩码排除。角色实例按整备室接入契约进入第 13 层，不扩大源渲染通道掩码，也未提高曝光掩盖问题。
- 继续使用原生角色 Shader、源材质/纹理、背景、反射与后处理，以及此前修复的原作 SH 探针插值。

## 本次验证

'''
text+=f'- Unity 6000.3.27f2 / D3D11：名单回归 PASS，{checks} 条检查，{len(ids)} 个唯一角色全部覆盖；含滚动可达性、实际 UI 切换、原生动作、Shader/渲染层、变形及取消/租约释放。\n'
text+='''- 整备室整体 Editor 回归 PASS；详细原始输出见 `roster-validation.txt`、`full-validation.txt`。源码哈希见 `source-sha256.json`。
- 每名角色均保存有 UI 和无 UI 的 1280×720 实帧，见 `frames/`；总览见 `new-character-contact-sheet.jpg`。自动非黑屏检查曾漏过两名黑色轮廓角色，逐张视觉复核后已修复并重新跑完整名单回归。
- 这次验证是 Editor 展示/运行验证，不是 Player、签名发布或 AOT 构建验收；也不代表逐个角色与原作实机完成像素级一致性校对。
- 原有 Florence/Zhaohui/Clukay 道具依赖动作仍按名单规则隐藏；辅助相机/道具动画不因角色进入名单就自动宣称已完整绑定。
- 默认机位目前仍以闪电的原作截帧为共用基准。威玛西娜等高个角色的头顶接近或超出画面上沿，可进入浏览后缩放；逐角色的原作默认机位偏移仍需源配置或对应截帧，尚未自动用模型包围盒替代原作机位。

## 剩余默认角色的首个阻塞点

以下是当前脚本遇到的首个阻塞点；修复该项后仍须继续完整核验。详细候选 CAB、PathID、骨骼和姿势误差见 `all-variants-status.json`。这些角色未放入可选列表。

| 角色 | ID | 首个阻塞点 |
|---|---|---|
'''
for row in blocked:
 reason=row['firstBlockingIssue'].replace('ValueError: ','').replace('|','/')
 try:
  issue=ast.literal_eval(reason)
  if issue[0]=='No structural candidate':reason='骨骼顺序或绑定姿势未通过：'+issue[1]
  elif issue[0]=='Different geometry candidate':reason='同名候选的几何/表情数据不同：'+issue[1]
 except (ValueError,SyntaxError,IndexError):pass
 text+=f"| {row['name']} | `{row['id']}` | {reason} |\n"
text+='''
## 需要补充的证据

优先补一名同名网格歧义角色（如 SoppoSSR01 或 VectorSSR01）的默认整备室 GPU 截帧，读取实际绘制的顶点/索引、骨骼绑定和材质资源；如能恢复当前客户端的 MeshResPath→资源标识映射，可批量解除同类阻塞。普通画面截图或画质设置不能确定同名 Mesh 的准确版本。

HelenSSR01 还出现 `gf_shader/vfx/UV_Effect_UVAll_Fast_3_New`、`gf_shader/vfx/gf_vfx_rgb_Block_Noise_Fast`，需要在网格绑定解决后检查原生特效 Pass 与实际绘制顺序；当前不标为已适配。
'''
(OUT/'README.md').write_text(text,encoding='utf8')
print(json.dumps(dict(roster=len(roster),added=len(new),blocked=len(blocked),checks=checks,report=str(OUT/'README.md')),ensure_ascii=False))
