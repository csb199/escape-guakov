import sys,json,pathlib,argparse,struct
parser=argparse.ArgumentParser(description='Export source barrack UI layout, containers, sprites and Chinese localization.')
parser.add_argument('--bundles',default='LocalOnly/Barrack/live/closure/bundles')
parser.add_argument('--tables',default='E:/Games/GF2Exilium/GF2 Game/GF2_Exilium_Data/LocalCache/Data/Table')
parser.add_argument('--python-libraries',default='F:/gfresource/tools/python')
args=parser.parse_args()
sys.path.insert(0,args.python_libraries);import UnityPy
base=pathlib.Path(args.bundles); cache={}
def load(cab):
 cab=cab.lower()
 if cab not in cache:
  e=UnityPy.load(str(base/(cab+'.bundle')));cache[cab]={o.path_id:o for o in e.objects if o.assets_file.name.lower()==cab}
 return cache[cab]
def resolve(obj,ptr):
 if not ptr['m_PathID']:return None
 cab=obj.assets_file.name.lower() if not ptr['m_FileID'] else obj.assets_file.externals[ptr['m_FileID']-1].path.split('/')[-1].lower()
 return load(cab).get(int(ptr['m_PathID']))
objs=load('cab-01df42e7d2bf3e19beb9f03617cfdd1a');nodes=[]
def walk(o,parent):
 go=o.read_typetree();com=[resolve(o,c['component']) for c in go['m_Component']];tr=next(c for c in com if c.type.name in ['Transform','RectTransform']);t=tr.read_typetree();id=len(nodes)
 data=dict(id=id,parent=parent,name=go['m_Name'],active=bool(go['m_IsActive']),transform=t,transformId=str(tr.path_id),cab=o.assets_file.name,components=[])
 nodes.append(data)
 for c in com:
  if c.type.name!='MonoBehaviour':continue
  try:ct=c.read_typetree(check_read=False)
  except Exception as ex:
   data['components'].append(dict(type='Unresolved',error=str(ex),pathId=str(c.path_id)));continue
  script=resolve(c,ct['m_Script']);name=script.read_typetree().get('m_ClassName','') if script else 'Unknown'
  data['components'].append(dict(type=name,data=ct,cab=c.assets_file.name,pathId=str(c.path_id)))
 for ptr in t['m_Children']:
  ch=resolve(tr,ptr);walk(resolve(ch,ch.read_typetree()['m_GameObject']),id)
 for c in data['components']:
  if c['type'] in ['UIContainer','UICommonContainer'] and c.get('data',{}).get('prefab',{}).get('m_PathID'):
   target=resolve(o,c['data']['prefab']);walk(target,id)
walk(objs[2080607735672150282],-1);walk(objs[6365755860975736115],next(n['id']for n in nodes if n['name']=='Trans_Overview_0'))
out=pathlib.Path('Assets/Game/Content/BarrackSource/SourcePresentation');out.mkdir(exist_ok=True)
def exact(x):
 if isinstance(x,dict):return {k:str(v) if k=='m_PathID'else exact(v) for k,v in x.items()}
 if isinstance(x,list):return [exact(v)for v in x]
 return x
(out/'source-ui.json').write_text(json.dumps(exact(dict(nodes=nodes)),ensure_ascii=False,indent=2),encoding='utf8')




# Apply UIContainer overrides through the original UITemplate component slots.
# Keep source-ui.json as unmodified evidence; only the runtime contract is expanded.
import copy
nodes=copy.deepcopy(nodes)
def descendants(id):
 result=[]
 for n in nodes[id+1:]:
  p=n['parent']
  while p>=0 and p!=id:p=nodes[p]['parent']
  if p==id:result.append(n)
 return result
for n in nodes:
 for c in n['components']:
  if c['type']!='UIContainer':continue
  d=c['data'];children=descendants(n['id']);template=next((x for ch in children for x in ch['components'] if x['type']=='UITemplate'),None)
  if not template:continue
  for slot,kind,field,value in [('Images','Image','m_Sprite',d.get('icon')),('Texts','Text','hintId',d.get('hintId'))]:
   if not value or isinstance(value,dict) and not value.get('m_PathID'):continue
   refs=[str(p['m_PathID']) for p in template['data'].get(slot,[]) if p['m_PathID']]
   if not d.get('ChangeAllIcon'):refs=refs[:1]
   for ch in children:
    for target in ch['components']:
     if target.get('pathId') in refs and target['type']==kind:
      if kind=='Image':target['data'][field]=value;target['cab']=c['cab']
      else:ch['components'].append(dict(type='LocalizedText',data=dict(hintId=value)))
def var(b,i):
 value=shift=0
 while True:
  a=b[i];i+=1;value|=(a&127)<<shift
  if a<128:return value,i
  shift+=7
  if shift>63:raise ValueError('Invalid protobuf varint')
def fields(b):
 out=[];i=0
 while i<len(b):
  tag,i=var(b,i);wire=tag&7
  if wire==0:value,i=var(b,i)
  elif wire==2:n,i=var(b,i);value=b[i:i+n];i+=n
  elif wire in (1,5):n=8 if wire==1 else 4;value=b[i:i+n];i+=n
  else:raise ValueError(wire)
  out.append((tag>>3,value))
 return out
def table(name):
 b=(pathlib.Path(args.tables)/name).read_bytes();rows=fields(b[4+struct.unpack_from('<I',b)[0]:]);out={}
 for _,v in rows:
  if isinstance(v,bytes):r=dict(fields(v));out[r.get(1)]=r
 return out
langs=table('LangPackageTableCnData.bytes');hints=table('HintData.bytes')
for n in nodes:
 for c in n['components']:
  if c['type']!='LocalizedText':continue
  value=hints.get(c['data'].get('hintId'),{}).get(2)
  id=dict(fields(value)).get(1) if isinstance(value,bytes) else value
  label=langs.get(id,{}).get(2,b'').decode('utf8')
  if label:
   for graphic in n['components']:
    if graphic['type']=='Text':graphic['data']['m_Text']=label

# Export an executable layout contract; source script bodies remain evidence only.
from UnityPy.export.SpriteHelper import get_image_from_sprite
sprite_dir=out/'Sprites';sprite_dir.mkdir(exist_ok=True)
images={};fonts={};errors=[];runtime=[]
font_dir=out/'Fonts';font_dir.mkdir(exist_ok=True)
for n in nodes:
 t=n['transform'];entry=dict(id=n['id'],parent=n['parent'],name=n['name'],active=n['active'],anchorMin=t.get('m_AnchorMin',{'x':0,'y':0}),anchorMax=t.get('m_AnchorMax',{'x':1,'y':1}),pivot=t.get('m_Pivot',{'x':.5,'y':.5}),position=t.get('m_AnchoredPosition',{'x':0,'y':0}),size=t.get('m_SizeDelta',{'x':0,'y':0}),scale=t['m_LocalScale'],rotation=t['m_LocalRotation'],graphics=[])
 for c in n['components']:
  d=c.get('data',{});kind=c['type']
  if kind not in ['Image','Text']:continue
  g=dict(kind=kind,color=d.get('m_Color',dict(r=1,g=1,b=1,a=1)),sprite=-1,text=d.get('m_Text',''),fontSize=d.get('m_FontData',{}).get('m_FontSize',24),alignment=d.get('m_FontData',{}).get('m_Alignment',0),imageType=d.get('m_Type',0),preserveAspect=bool(d.get('m_PreserveAspect',False)))
  g['font']=-1
  if kind=='Text':
   fd=d.get('m_FontData',{});fp=fd.get('m_Font',{})
   g.update(fontStyle=fd.get('m_FontStyle',0),lineSpacing=fd.get('m_LineSpacing',1),horizontalOverflow=fd.get('m_HorizontalOverflow',0),verticalOverflow=fd.get('m_VerticalOverflow',0))
   if fp.get('m_PathID'):
    fo=resolve(next(iter(load(c['cab']).values())),fp);key=fo.assets_file.name+'_'+str(fo.path_id)
    if key not in fonts:
     ft=fo.read_typetree();blob=bytes(ft['m_FontData']);extension='.otf' if blob[:4]==b'OTTO' else '.ttf';filename=str(len(fonts))+extension
     if not blob:raise ValueError('Font has no native font data: '+ft['m_Name'])
     (font_dir/filename).write_bytes(blob);fonts[key]=dict(index=len(fonts),file=filename,name=ft['m_Name'],source=key)
    g['font']=fonts[key]['index']
  ptr=d.get('m_Sprite',{})
  if ptr.get('m_PathID',0):
   try:
    # These components belong to the selected UI CAB; resolve the external sprite exactly.
    fake=next(iter(load(c['cab']).values()));sp=resolve(fake,ptr);key=sp.assets_file.name+'_'+str(sp.path_id)
    if key not in images:
     st=sp.read_typetree();rd=st['m_RD'];tex=resolve(sp,rd['texture']);td=tex.read();img=td.image
     rect=rd['textureRect'];x,y,w,h=[int(rect[k])for k in ['x','y','width','height']]
     # Texture images are flipped to image coordinates by UnityPy.
     img=img.crop((x,img.height-y-h,x+w,img.height-y))
     filename=str(len(images))+'.png';img.save(sprite_dir/filename);images[key]=dict(index=len(images),file=filename,name=st['m_Name'],border=st.get('m_Border',{}),source=key)
    g['sprite']=images[key]['index']
   except Exception as ex:errors.append(dict(node=n['name'],error=str(ex)))
  entry['graphics'].append(g)
 runtime.append(entry)
(out/'layout-runtime.json').write_text(json.dumps(dict(nodes=runtime,sprites=list(images.values()),fonts=list(fonts.values()),errors=errors),ensure_ascii=False,indent=2),encoding='utf8')
print('LAYOUT',len(runtime),'SPRITES',len(images),'ERRORS',len(errors))

# Include layout components without interpreting them as executable source code.
raw=nodes
for entry,n in zip(runtime,raw):
 entry['layouts']=[dict(kind=c['type'],data=c['data']) for c in n['components'] if c['type'] in ['HorizontalLayoutGroup','VerticalLayoutGroup','GridLayoutGroup','LayoutElement','ContentSizeFitter']]
 entry['button']=any(c['type'] in ['Button','GFButton'] for c in n['components'])
(out/'layout-runtime.json').write_text(json.dumps(dict(nodes=runtime,sprites=list(images.values()),fonts=list(fonts.values()),errors=errors),ensure_ascii=False,indent=2),encoding='utf8')

# UIResizeElement.fieldType enum recovered from Assembly-CSharp metadata:
# 0 PosX, 1 PosY, 2 Width, 3 Height, 4 Left, 5 Top, 6 Right, 7 Bottom,
# 8 AnchorMin, 9 AnchorMax, 10 Pivot, 11 Scale. Apply only the selected field.
overrides=[]
for n in nodes:
 for c in n['components']:
  if c['type']!='UIResizeAdapter':continue
  for e in c['data'].get('Elements',[]):
   target=str(e['Element']['m_PathID'])
   if target=='0':continue
   dest=next((x for x in nodes if (x['transformId']==target or str(x['transform']['m_GameObject']['m_PathID'])==target) and x['cab']==n['cab']),None)
   if not dest:raise ValueError('Unresolved PC resize target '+target)
   r=runtime[dest['id']];pc=e['PCRectTransform'];field=e['fieldType'];pos=r['position'];size=r['size'];pivot=r['pivot']
   if field in [0,1]:axis='x' if field==0 else 'y';pos[axis]=pc['anchoredPosition'][axis]
   elif field in [2,3]:axis='x' if field==2 else 'y';size[axis]=pc['sizeDelta'][axis]
   elif field in [4,5,6,7]:
    axis='x' if field in [4,6] else 'y';lo=pos[axis]-size[axis]*pivot[axis];hi=pos[axis]+size[axis]*(1-pivot[axis])
    if field in [4,7]:lo=pc['offsetMin'][axis]
    else:hi=pc['offsetMax'][axis]
    size[axis]=hi-lo;pos[axis]=lo+size[axis]*pivot[axis]
   else:
    key={8:'anchorMin',9:'anchorMax',10:'pivot',11:'scale'}[field];r[key]=pc['localScale' if field==11 else key]
   overrides.append(dict(node=dest['id'],field=field,sourcePathId=target))
(out/'layout-runtime.json').write_text(json.dumps(dict(nodes=runtime,sprites=list(images.values()),fonts=list(fonts.values()),errors=errors,pcOverrides=overrides),ensure_ascii=False,indent=2),encoding='utf8')
print('PC_OVERRIDES',len(overrides))
