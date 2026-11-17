"""Read-only inventory of parsed GF2 character assets and host-recovered dependencies."""
import argparse,collections,csv,gc,hashlib,json,re,sys,time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

CAB_RE=re.compile(r'cab-[0-9a-f]{32}',re.I)
def cab(value):
 m=CAB_RE.search(str(value));return m.group(0).lower() if m else str(value).lower()
def dump(path,value):
 path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf8')
def progress(message):print(message,flush=True)

class Audit:
 def __init__(self,source,project,out):
  self.source=source;self.project=project;self.out=out;out.mkdir(parents=True,exist_ok=True)
  sys.path.insert(0,str(source/'tools/python'));import UnityPy
  self.unity=UnityPy;self.unity.config.FALLBACK_UNITY_VERSION='2019.4.29f1'
  self.records=[];self.paths={};self.base_cabs=set();self.names={};self.ids=collections.defaultdict(list);self.cache=collections.OrderedDict();self.trees={};self.errors=[]
 def scan(self):
  for line in (self.source/'bundle_index.jsonl').open(encoding='utf8'):
   r=json.loads(line)
   if r.get('error'):continue
   self.records.append(r)
   path=self.source/'extracted/bundles'/(Path(r['file'].replace('\\','/')).stem+'.bundle')
   for c in r.get('cab',[]):
    if path.exists():self.paths[cab(c)]=path;self.base_cabs.add(cab(c))
  known={cab(c)for r in self.records for c in r.get('cab',[])}
  extras={}
  for p in sorted((self.project/'LocalOnly/Barrack/live').rglob('cab-*.bundle')):extras[cab(p.stem)]=p
  for p in (self.project/'Assets/Game/Content/BarrackSource/Stage/Bundles').glob('cab-*.bytes'):extras[cab(p.stem)]=p
  for c,p in extras.items():
   if c not in self.paths:self.paths[c]=p
  new=[(c,p)for c,p in extras.items()if c not in known]
  cached={}
  cachefile=self.out/'supplement-index.jsonl'
  if cachefile.exists():
   for line in cachefile.open(encoding='utf8'):
    try:
     r=json.loads(line);cached[r['file']]=r
    except Exception:pass
  def scan_one(pair):
   c,p=pair;key=str(p);st=p.stat();old=cached.get(key)
   if old and old.get('size')==st.st_size and old.get('mtime')==st.st_mtime_ns:return old
   try:
    e=self.unity.load(str(p));names=[]
    for o in e.objects:
     if o.type.name in ('GameObject','Mesh','Material','Shader','AnimationClip','Avatar','Texture2D','MonoScript'):
      try:n=o.peek_name()
      except Exception:n=None
      names.append([o.type.name,n or '',o.path_id])
    return dict(file=key,size=st.st_size,mtime=st.st_mtime_ns,cab=[sf.name for sf in e.assets],names=names,deps=[x.path for sf in e.assets for x in sf.externals])
   except Exception as ex:return dict(file=key,size=st.st_size,mtime=st.st_mtime_ns,error=str(ex),cab=[c],names=[])
  progress(f'Supplement metadata: {len(new)} bundles beyond original index')
  with cachefile.open('w',encoding='utf8') as writer,ThreadPoolExecutor(max_workers=3) as pool:
   for i,r in enumerate(pool.map(scan_one,new)):
    writer.write(json.dumps(r,ensure_ascii=False)+'\n');writer.flush();self.records.append(r)
    if r.get('error'):self.errors.append(r)
    if (i+1)%100==0:progress(f'Supplement {i+1}/{len(new)}')
  # The native character library is already integrated in the host; index its actual CAB.
  for p in (self.project/'Assets/Game/Content/BarrackSource/GrozaSR01').glob('Original*.bytes'):
   e=self.unity.load(str(p))
   for sf in e.assets:
    self.paths.setdefault(cab(sf.name),p)
    self.records.append(dict(file=str(p),cab=[sf.name],names=[[o.type.name,o.peek_name() or '',o.path_id]for o in sf.objects.values()if o.type.name in ('Shader','AnimationClip')],deps=[x.path for x in sf.externals]))
  for r in self.records:
   if len(r.get('cab',[]))!=1:continue
   c=cab(r['cab'][0])
   if c not in self.paths:continue
   for typ,name,pid in r.get('names',[]):
    addr=(c,int(pid));self.names[addr]=(typ,name)
    if addr not in self.ids[int(pid)]:self.ids[int(pid)].append(addr)
  progress(f'Available CABs: parsed={len(self.base_cabs)} combined={len(self.paths)} named objects={len(self.names)}')
 def reader(self,addr):
  c,pid=addr;p=self.paths.get(c)
  if p is None:raise KeyError('CAB not available: '+c)
  key=str(p)
  if key in self.cache:e=self.cache.pop(key);self.cache[key]=e
  else:
   e=self.unity.load(key);self.cache[key]=e
   if len(self.cache)>7:self.cache.popitem(last=False)
  sf=next((s for s in e.assets if cab(s.name)==c),None)
  if sf is None:raise KeyError('CAB absent from indexed bundle: '+c)
  if pid not in sf.objects:raise KeyError('PathID absent: '+str(pid))
  return sf.objects[pid]
 def tree(self,addr):
  if addr not in self.trees:self.trees[addr]=self.reader(addr).read_typetree()
  return self.trees[addr]
 def ref(self,owner,ptr,expected=None,result=None,label=''):
  if not ptr or int(ptr.get('m_PathID',0))==0:return None
  pid=int(ptr['m_PathID']);fid=int(ptr.get('m_FileID',0));c=owner[0]
  try:
   if fid:
    sf=self.reader(owner).assets_file;c=cab(sf.externals[fid-1].path)
   addr=(c,pid)
   obj=self.reader(addr)
   if expected and obj.type.name not in expected:raise ValueError('type '+obj.type.name)
   if result is not None and c not in self.base_cabs:result['supplement_cabs'].add(c)
   return addr
  except Exception as ex:
   candidates=[a for a in self.ids.get(pid,[])if not expected or self.names[a][0]in expected]
   candidates=list(dict.fromkeys(candidates))
   if len(candidates)==1:
    alt=candidates[0]
    try:
     self.reader(alt)
     if result is not None:
      result['relocations'].append(dict(label=label,declaredCab=c,pathID=str(pid),resolvedCab=alt[0],type=self.names[alt][0],name=self.names[alt][1]))
      if alt[0]not in self.base_cabs:result['supplement_cabs'].add(alt[0])
     return alt
    except Exception:pass
   if result is not None:result['missing'].append(dict(label=label,cab=c,pathID=str(pid),reason=str(ex),candidates=len(candidates)))
   return None
 def variants(self):
  variants={};clips=collections.defaultdict(list);gos=collections.defaultdict(list)
  for a,(typ,n)in self.names.items():
   if typ=='AnimationClip':
    m=re.match(r'^c_(.+?)_Barrack',n)
    if m and re.fullmatch(r'[A-Za-z]+(?:SSR|SR|R)\d+',m[1]):variants[m[1]]=True
    clips[n].append(a)
   if typ=='GameObject':gos[n].append(a)
  self.clips=clips;self.gos=gos
  return sorted(variants)
 def inspect(self,variant,selected=None):
  r=dict(variant=variant,hasRootAvatar=False,group=re.sub(r'(SSR|SR|R)\d+$','',variant).lower(),prefabs=[],missing=[],relocations=[],supplement_cabs=set(),materials=[],renderers=[],behaviours=set(),components=collections.Counter(),clips=[],clip_coverage={},notes=[],new_features=[],transforms=set())
  candidates=[selected] if selected else self.gos.get(variant,[])
  for addr in candidates:
   try:
    d=self.tree(addr);comps=[self.ref(addr,x['component'])for x in d.get('m_Component',[])];comps=[x for x in comps if x]
    transforms=[x for x in comps if self.reader(x).type.name=='Transform']
    if transforms:r['prefabs'].append(addr)
    r['hasRootAvatar']=any(self.reader(c).type.name=='Animator' and self.tree(c).get('m_Avatar',{}).get('m_PathID',0) for c in comps)
   except Exception as ex:r['notes'].append('prefab scan: '+str(ex))
  if not r['prefabs']:r['missing'].append(dict(label='Prefab',cab='',pathID='',reason='No exact-name model hierarchy in available parsed objects; runtime model/skin mapping not verified',candidates=len(candidates)))
  elif len(r['prefabs'])>1:r['notes'].append('Multiple root prefabs: inspect each; no arbitrary selection')
  seen=set();material_seen=set();render_list=[];mesh_declarations={}
  def walk(addr):
   if addr in seen:return
   seen.add(addr);d=self.tree(addr);name=d.get('m_Name','');comps=[]
   for x in d.get('m_Component',[]):
    a=self.ref(addr,x['component'],result=r,label=name+'/component')
    if a:comps.append((a,self.reader(a).type.name))
   for a,typ in comps:
    r['components'][typ]+=1
    if typ in ('Transform','RectTransform'):
     r['transforms'].add(a);t=self.tree(a)
     for ptr in t.get('m_Children',[]):
      child=self.ref(a,ptr,('Transform','RectTransform'),r,name+'/child')
      if child:
       go=self.ref(child,self.tree(child).get('m_GameObject'),('GameObject',),r,name+'/childGO')
       if go:walk(go)
    elif typ=='MonoBehaviour':
     try:
      t=self.tree(a)
      for item in t.get('MeshResList',[]):mesh_declarations[item['TransfromPath'].rsplit('/',1)[-1]]=item['MeshResPath']
      script=self.ref(a,t.get('m_Script'),('MonoScript',))
      if script:r['behaviours'].add(self.tree(script).get('m_ClassName',self.tree(script).get('m_Name','unknown')))
      else:r['behaviours'].add('unresolved-source-script')
     except Exception:r['behaviours'].add('unreadable-source-script')
    elif typ=='Animator':
     t=self.tree(a)
     if t.get('m_Avatar',{}).get('m_PathID'):self.ref(a,t['m_Avatar'],('Avatar',),r,name+'/Avatar')
    elif typ in ('SkinnedMeshRenderer','MeshRenderer') and not re.search(r'_lod(?:1|2|m\d+)$',name,re.I):
     render_list.append((addr,name,a,typ,comps))
  for root in r['prefabs']:
   try:walk(root)
   except Exception as ex:r['missing'].append(dict(label='Hierarchy',cab=root[0],pathID=str(root[1]),reason=str(ex),candidates=0))
  for go,name,a,typ,comps in render_list:
   try:
    t=self.tree(a);entry=dict(name=name,type=typ,enabled=t.get('m_Enabled'),bones=len(t.get('m_Bones',[])),materials=len(t.get('m_Materials',[])))
    r['renderers'].append(entry)
    meshptr=t.get('m_Mesh');meshowner=a
    if typ=='MeshRenderer':
     filt=next((x for x,k in comps if k=='MeshFilter'),None)
     if filt:meshptr=self.tree(filt).get('m_Mesh');meshowner=filt
    mesh=self.ref(meshowner,meshptr,('Mesh',),r,name+'/Mesh')
    if mesh is None and (not meshptr or not meshptr.get('m_PathID')) and name in mesh_declarations:
     declared=mesh_declarations[name]
     if Path(declared).stem==name:
      candidates=self.mesh_evidence.get((variant,name),[])
      unique=list(dict.fromkeys(m for m,origin in candidates))
      if len(unique)==1:
       mesh=unique[0];entry['meshRecovery']=dict(declaredPath=declared,meshCab=mesh[0],meshPathID=str(mesh[1]),sourceRenderer=list(candidates[0][1]))
       if mesh[0]not in self.base_cabs:r['supplement_cabs'].add(mesh[0])
    if mesh:
     md=self.reader(mesh).read();entry.update(meshName=md.m_Name,vertices=md.m_VertexData.m_VertexCount,bindposes=len(md.m_BindPose))
     if typ=='SkinnedMeshRenderer' and len(md.m_BindPose)!=entry['bones']:r['missing'].append(dict(label=name+'/bindposes',cab=mesh[0],pathID=str(mesh[1]),reason='Mesh bindpose count differs from renderer bone count',candidates=0))
    elif not meshptr or not meshptr.get('m_PathID'):r['missing'].append(dict(label=name+'/Mesh',cab=a[0],pathID='',reason='Null mesh reference',candidates=0))
    for i,ptr in enumerate(t.get('m_Bones',[])):
     bone=self.ref(a,ptr,('Transform',),r,name+'/bone'+str(i))
     if bone and bone not in r['transforms']:r['missing'].append(dict(label=name+'/bone',cab=bone[0],pathID=str(bone[1]),reason='Bone is outside prefab ownership',candidates=0))
     if not ptr.get('m_PathID'):r['missing'].append(dict(label=name+'/bone'+str(i),cab=a[0],pathID='',reason='Null bone',candidates=0))
    for i,ptr in enumerate(t.get('m_Materials',[])):
     mat=self.ref(a,ptr,('Material',),r,name+'/material'+str(i))
     if not mat:
      if not ptr.get('m_PathID'):r['missing'].append(dict(label=name+'/material'+str(i),cab=a[0],pathID='',reason='Null material',candidates=0))
      continue
     if mat in material_seen:continue
     material_seen.add(mat);mt=self.tree(mat);m=dict(name=mt.get('m_Name'),cab=mat[0],pathID=str(mat[1]),shader='',keywords=mt.get('m_ShaderKeywords','').split(),disabledPasses=mt.get('disabledShaderPasses',[]),queue=mt.get('m_CustomRenderQueue'),textures=[])
     r['materials'].append(m);shader=self.ref(mat,mt.get('m_Shader'),('Shader',),r,m['name']+'/Shader')
     if shader:
      sh=self.tree(shader);m['shader']=sh.get('m_ParsedForm',{}).get('m_Name',sh.get('m_Name',''))
      m['shaderCab']=shader[0];m['shaderPathID']=str(shader[1])
     for prop,value in mt.get('m_SavedProperties',{}).get('m_TexEnvs',[]):
      if not value.get('m_Texture',{}).get('m_PathID'):continue
      tex=self.ref(mat,value['m_Texture'],('Texture2D','Cubemap','Texture2DArray','Texture3D'),r,m['name']+'/'+prop)
      if tex:
       td=self.reader(tex).read();rec=dict(property=prop,cab=tex[0],pathID=str(tex[1]),name=getattr(td,'m_Name',''),width=getattr(td,'m_Width',0),height=getattr(td,'m_Height',0));m['textures'].append(rec)
       if hasattr(td,'get_image_data'):
        try:
         if not td.get_image_data():raise ValueError('empty texture payload')
        except Exception as ex:r['missing'].append(dict(label=m['name']+'/'+prop,cab=tex[0],pathID=str(tex[1]),reason='Texture payload: '+str(ex),candidates=0))
     floats=dict(mt.get('m_SavedProperties',{}).get('m_Floats',[]));m['specialFlags']={k:v for k,v in floats.items()if re.search(r'Fur|Shell|Displace|Dissolve|Stencil',k,re.I)and v!=0}
   except Exception as ex:r['missing'].append(dict(label=name+'/renderer',cab=a[0],pathID=str(a[1]),reason=str(ex),candidates=0))
  for name,addresses in self.clips.items():
   if name.startswith('c_'+variant+'_') and ('Barrack'in name or 'ToIdle'in name):
    available=[]
    for addr in addresses:
     try:
      obj=self.reader(addr)
      if obj.byte_size>0:available.append(dict(cab=addr[0],pathID=str(addr[1]),bytes=obj.byte_size))
     except Exception:pass
    r['clips'].append(dict(name=name,available=available))
  r['clip_coverage']={k:any(x['available']and test(x['name'])for x in r['clips'])for k,test in dict(idle=lambda n:'_BarrackIdle'in n,entrance=lambda n:'_BarrackEntrance'in n,return_to_idle=lambda n:'ToIdle'in n,touch=lambda n:'_Touch_'in n,behave=lambda n:'_Behave_'in n).items()}
  for k in ['idle','entrance']:
   if not r['clip_coverage'][k]:r['missing'].append(dict(label='Animation/'+k,cab='',pathID='',reason='No available own-variant core clip; inheritance mapping not verified',candidates=0))
  if r['prefabs'] and not r['renderers']:r['missing'].append(dict(label='Renderer',cab='',pathID='',reason='No LOD0 character renderer recovered',candidates=0))
  r['supplement_cabs']=sorted(r['supplement_cabs']);r['behaviours']=sorted(r['behaviours']);r['transforms']=len(r['transforms']);r['components']=dict(r['components']);r['prefabs']=[dict(cab=c,pathID=str(p),file=str(self.paths[c]))for c,p in r['prefabs']]
  return r
 def run(self):
  self.scan();variants=self.variants();progress(f'Barrack variants: {len(variants)}')
  self.mesh_evidence=collections.defaultdict(list)
  source_cabs={a[0]for v in variants for a in self.gos.get(v,[])}
  # MeshResList entries are resolved only from another actual renderer PPtr, not a name-only guess.
  for c in sorted(source_cabs):
   addr=next(a for a in self.names if a[0]==c);sf=self.reader(addr).assets_file
   for obj in list(sf.objects.values()):
    if obj.type.name!='SkinnedMeshRenderer':continue
    a=(c,obj.path_id)
    try:
     t=obj.read_typetree();go=self.ref(a,t.get('m_GameObject'),('GameObject',))
     if not go:continue
     name=self.tree(go).get('m_Name','');match=re.match(r'^c_(.+?)_slg_',name)
     if not match or match[1]not in variants:continue
     mesh=self.ref(a,t.get('m_Mesh'),('Mesh',))
     if mesh and self.reader(mesh).peek_name()==name:self.mesh_evidence[(match[1],name)].append((mesh,a))
    except Exception:pass
  progress(f'Exact renderer mesh evidence: {len(self.mesh_evidence)}')
  results=[]
  for i,v in enumerate(variants):
   alternatives=[self.inspect(v,a)for a in self.gos.get(v,[])]
   r=min(alternatives,key=lambda x:(not any('_face_' in q['name'].lower() for q in x['renderers']),not x['hasRootAvatar'],len(x['missing']),-len(x['renderers']),x['transforms'])) if alternatives else self.inspect(v)
   r['alternatives']=[dict(prefabs=x['prefabs'],renderers=len(x['renderers']),missing=len(x['missing']))for x in alternatives]
   r['mesh_recoveries']=sum('meshRecovery'in x for x in r['renderers'])
   results.append(r);dump(self.out/'variants'/f'{v}.json',r)
   progress(f'{i+1}/{len(variants)} {v}: prefab={len(r["prefabs"])} renderer={len(r["renderers"])} missing={len(r["missing"])}')
   if i%10==0:gc.collect()
  dump(self.out/'raw-audit.json',dict(created=time.strftime('%Y-%m-%dT%H:%M:%S%z'),source=str(self.source),baseCabCount=len(self.base_cabs),combinedCabCount=len(self.paths),variants=results,scanErrors=self.errors))
  progress('RAW_AUDIT_COMPLETE')

if __name__=='__main__':
 sys.stdout.reconfigure(encoding='utf8')
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--source',type=Path,default=Path('F:/gfresource'));ap.add_argument('--project',type=Path,default=Path(__file__).resolve().parents[1]);ap.add_argument('--output',type=Path)
 args=ap.parse_args();Audit(args.source,args.project,args.output or args.project/'LocalOnly/Barrack/character-audit').run()
