"""Prepare exact native import plans for selected audited character subtrees."""
import importlib.util,json,sys,re
from pathlib import Path
s=importlib.util.spec_from_file_location('audit',Path(__file__).with_name('Audit-BarrackCharacters.py'));m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
root=Path(__file__).resolve().parent.parent
out=root/'LocalOnly/Barrack/character-import'
a=m.Audit(Path('F:/gfresource'),root,root/'LocalOnly/Barrack/next-batch-audit');a.scan()
for variant in sys.argv[1:] or ['DusevnyjSSR01','PhaetusaSSR01']:
 d=json.loads((a.out/'variants'/f'{variant}.json').read_text(encoding='utf8'));p=d['prefabs'][0];addr=(p['cab'],int(p['pathID']));dest=out/variant;dest.mkdir(parents=True,exist_ok=True)
 used={addr[0]}
 verified_path=dest/'verified-bindings.json'
 verified=json.loads(verified_path.read_text(encoding='utf8')) if verified_path.exists() else {'bindings':[]}
 selected={b['renderer']:b for b in verified['bindings']}
 if len(selected)!=len(verified['bindings']):raise ValueError('Duplicate verified bindings')
 def ref(owner,ptr):
  r=a.ref(owner,ptr)
  if r:used.add(r[0])
  return r
 def keys(addr):
  reader=a.reader(addr)
  return list(dict.fromkeys(k for o in reader.assets_file.objects.values() if o.type.name=='AssetBundle' for k,v in o.read().m_Container if v.asset.path_id==addr[1]))
 def key(addr):
  k=keys(addr)
  if len(k)!=1:raise ValueError(('Ambiguous asset key',addr,k))
  return k[0]
 def transform(go):return next(ref(go,c['component']) for c in a.tree(go)['m_Component'] if a.reader(ref(go,c['component'])).type.name=='Transform')
 tr=transform(addr);source=addr;parts=[]
 while a.tree(tr)['m_Father']['m_PathID']:
  parts.insert(0,a.tree(source)['m_Name']);tr=ref(tr,a.tree(tr)['m_Father']);source=ref(tr,a.tree(tr)['m_GameObject'])
 mesh=[];mats=set()
 def walk(go):
  if re.search(r"_lod(?:1|2|m\d+)$",a.tree(go)["m_Name"],re.I):return
  for c in a.tree(go)['m_Component']:
   ca=ref(go,c['component']);typ=a.reader(ca).type.name
   if typ not in ('Transform','SkinnedMeshRenderer','MeshRenderer','MeshFilter','Animator'):continue
   t=a.tree(ca)
   if typ=='Transform':
    for child in t['m_Children']:
     ch=ref(ca,child);walk(ref(ch,a.tree(ch)['m_GameObject']))
   elif typ=='SkinnedMeshRenderer':
    ma=ref(ca,t['m_Mesh'])
    if not ma:
     recovery=selected.get(a.tree(go)['m_Name'])
     if recovery is None:recovery=next((r.get('meshRecovery') for r in d['renderers'] if r['name']==a.tree(go)['m_Name']),None)
     if recovery is None:raise ValueError(('Unselected mesh',variant,a.tree(go)['m_Name']))
     ma=(recovery['meshCab'],int(recovery['meshPathID']));used.add(ma[0])
    mesh.append(dict(renderer=a.tree(go)['m_Name'],assetKey=key(ma),bundlePath=str(a.paths[ma[0]])))
    for v in t['m_Materials']:mats.add(ref(ca,v))
   elif typ=='MeshRenderer':
    for v in t['m_Materials']:mats.add(ref(ca,v))
   elif typ=='MeshFilter':ref(ca,t['m_Mesh'])
   elif typ=='Animator':ref(ca,t['m_Avatar'])
 walk(addr)
 if None in mats:raise ValueError(('Unresolved source material slot',variant))
 bindings=[];contracts=[];shadercabs=set()
 for ma in sorted(mats):
  t=a.tree(ma);sh=ref(ma,t['m_Shader']);shadercabs.add(sh[0]);textures=[]
  for prop,value in t['m_SavedProperties']['m_TexEnvs']:
   tx=ref(ma,value['m_Texture'])
   if not tx:continue
   obj=a.reader(tx);tex=obj.read();raw=tex.get_image_data();rawpath=dest/(tx[0]+'_'+str(tx[1])+'.bin');rawpath.write_bytes(raw)
   if not raw:raise ValueError('Empty texture '+tex.m_Name)
   textures.append(dict(property=prop,assetKey=key(tx),name=tex.m_Name,rawDataPath=str(rawpath),srgb=tex.m_ColorSpace==1))
  keywords=t.get('m_ShaderKeywords','');disabled=t.get('disabledShaderPasses',[])
  bindings.append(dict(name=t['m_Name'],keywords=keywords,textures=textures))
  contracts.append(dict(name=t['m_Name'],keywords=keywords.split(),disabledPasses=disabled))
  if variant=='GrozaSR01' and t['m_Name'] in ('c_Groza_slg_hairuber','c_GrozaSR0105_slg_hairuber','c_Groza_slg_fringeuber'):
   contracts[-1]['hostRenderQueue']=2441 if t['m_Name']=='c_Groza_slg_fringeuber' else 2440
 allclips=[c for c in d['clips'] if c['available']]
 idle=[c for c in allclips if c['name']=='c_'+variant+'_BarrackIdle']
 if len(idle)!=1 or len(idle[0]['available'])!=1:raise ValueError(('Ambiguous idle library',variant))
 maincab=idle[0]['available'][0]['cab']
 clips=[c for c in allclips if sum(av['cab']==maincab for av in c['available'])==1 and not c['name'].endswith('_Camera')]
 for recovery in selected.values():
  proof=recovery.get('selectionEvidence')
  if proof:
   unverified=set(c['name'] for c in clips)-set(proof['inspectedClips'])-set(proof.get('excludedClips',[]))
   if unverified:raise ValueError(('Evidence selection does not cover new clips',variant,sorted(unverified)))
 auxiliaries=[c for c in allclips if c not in clips]
 m.dump(dest/'auxiliary-clips.json',dict(status='not bound to character graph',clips=auxiliaries))
 clipcabs={maincab};used.update(clipcabs);used.update(shadercabs)
 if len(clipcabs)!=1 or not shadercabs:raise ValueError(('Invalid libraries',clipcabs,shadercabs))
 # Load available transitive bundle dependencies; missing unrelated cutscene branches are not selected.
 records={m.cab(c):r for r in a.records for c in r.get('cab',[])};seen=set();paths=[];missing=set()
 def visit(c):
  if c in seen:return
  seen.add(c)
  if c not in a.paths:missing.add(c);return
  for dep in records.get(c,{}).get('deps',[]):
   dc=m.cab(dep)
   if dc.startswith('cab-'):visit(dc)
  paths.append(str(a.paths[c]))
 for c in sorted(used):visit(c)
 plan=dict(variant=variant,sourceBundlePath=str(a.paths[source[0]]),sourceTransformPath="/".join(parts),sourceAssetName=key(source),sourceCharacterName=variant,paths=list(dict.fromkeys(paths)),shaderLibraryPath=str(a.paths[sorted(shadercabs)[0]]),additionalShaderLibraryPaths=[str(a.paths[c]) for c in sorted(shadercabs)[1:]],animationLibraryPath=str(a.paths[next(iter(clipcabs))]),meshBindings=mesh,materialBindings=bindings,animationNames=[c['name']for c in clips],unrelatedMissingCabs=sorted(missing))
 m.dump(dest/'import-plan.json',plan);m.dump(dest/'material-contract.json',dict(materials=contracts));m.dump(dest/'clips.json',[c['name'] for c in clips]);print(variant,'bundles',len(paths),'meshes',len(mesh),'materials',len(bindings),'unused missing',len(missing))

 # Once native assets have been persisted, emit a portable color-space verification contract.
 assetroot=root/'Assets/Game/Content/BarrackSource'/variant
 if (assetroot/'Assets').exists():
  expected={t['name']:t['srgb'] for b in bindings for t in b['textures']};items=[]
  for asset in sorted((assetroot/'Assets').glob('*_Texture2D.asset')):
   with asset.open(encoding='utf8') as f:header=''.join(next(f) for _ in range(12))
   name=re.search(r'  m_Name: (.+)',header).group(1)
   items.append(dict(name=name,srgb=expected[name],asset=asset.relative_to(root).as_posix(),format='native Texture2D metadata'))
  if not items:raise ValueError('No persisted texture inventory')
  m.dump(assetroot/'texture-color-contract.json',dict(source='Exact source Texture2D m_ColorSpace and native raw texture payload',textures=items))
