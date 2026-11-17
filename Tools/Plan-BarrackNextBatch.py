"""Read-only next-batch evidence: current audit, exact declarations and available mesh metadata."""
import argparse,collections,datetime,hashlib,importlib.util,json,re
from pathlib import Path
root=Path(__file__).resolve().parents[1]
s=importlib.util.spec_from_file_location('audit',Path(__file__).with_name('Audit-BarrackCharacters.py'));m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
a=m.Audit(Path('F:/gfresource'),root,root/'LocalOnly/Barrack/next-batch-audit');a.scan()
raw=json.loads((a.out/'raw-audit.json').read_text(encoding='utf8'));by={d['variant']:d for d in raw['variants']}
selected=['ZhaohuiSSR01','ClukaySSR01','FlorenceSSR01','TololoSSR01','KseniaSR01']
known=set()
for variant in ['GrozaSR01','DusevnyjSSR01','PhaetusaSSR01']:
 for mat in by[variant]['materials']:known.add((mat['shader'],tuple(sorted(mat['keywords'])),tuple(sorted(mat['disabledPasses']))))
mesh_names=collections.defaultdict(list)
for addr,(typ,name) in a.names.items():
 if typ=='Mesh':mesh_names[name].append(addr)
evidence=[]
for v in selected:
 d=by[v];p=d['prefabs'][0];addr=(p['cab'],int(p['pathID']));declarations=[]
 for c in a.tree(addr)['m_Component']:
  ca=a.ref(addr,c['component'])
  if a.reader(ca).type.name!='MonoBehaviour':continue
  try:t=a.tree(ca)
  except Exception:continue
  declarations+=t.get('MeshResList',[])
 unresolved=[]
 for issue in d['missing']:
  if issue['reason']!='Null mesh reference':continue
  name=issue['label'].removesuffix('/Mesh');declared=[x['MeshResPath'] for x in declarations if x['TransfromPath'].rsplit('/',1)[-1]==name];candidates=[]
  for ma in mesh_names[name]:
   obj=a.reader(ma);md=obj.read();keys=list(dict.fromkeys(k for ob in obj.assets_file.objects.values() if ob.type.name=='AssetBundle' for k,value in ob.read().m_Container if value.asset.path_id==ma[1]))
   candidates.append(dict(cab=ma[0],pathID=str(ma[1]),file=str(a.paths[ma[0]]),assetKeys=keys,vertices=md.m_VertexData.m_VertexCount,bindposes=len(md.m_BindPose)))
  unresolved.append(dict(renderer=name,declaredPaths=declared,indexedNameCandidates=candidates,expectedBones=next(r['bones'] for r in d['renderers'] if r['name']==name)))
 clipnames={c['name'] for c in d['clips'] if c['available']}
 suffixes={'Idle':'BarrackIdle','Entrance':'BarrackEntrance_Normal','Return':'ToIdle','Stretch':'Barracksp_Behave_Stretch','Inspect':'Barracksp_Behave_InspectNormal','InspectWeapon':'Barracksp_Behave_InspectExclusive','Head':'Barracksp_Touch_Head','Weapon':'Barracksp_Touch_Gun','Pocket':'Barracksp_Touch_Pocket','Around':'Barracksp_Camera_Aroud','Look':'Barracksp_Camera_Look'}
 actions={action:'c_'+v+'_'+suffix in clipnames for action,suffix in suffixes.items()}
 item=dict(variant=v,prefab=p,renderers=len(d['renderers']),materials=len(d['materials']),textureReferences=len({(t['cab'],t['pathID']) for mat in d['materials'] for t in mat['textures']}),unresolvedMeshes=unresolved,missingReferences=[x for x in d['missing'] if x['reason']!='Null mesh reference'],relocations=d['relocations'],actions=actions,animationCabs=sorted({av['cab'] for c in d['clips'] for av in c['available']}),shaderNames=sorted({x['shader'] for x in d['materials']}),unverifiedMaterialCombinations=[dict(name=x['name'],shader=x['shader'],keywords=x['keywords'],disabledPasses=x['disabledPasses']) for x in d['materials'] if (x['shader'],tuple(sorted(x['keywords'])),tuple(sorted(x['disabledPasses']))) not in known],behaviours=d['behaviours'])
 groups=collections.defaultdict(list)
 for clip in d['clips']:
  for av in clip['available']:groups[av['cab']].append(clip['name'])
 required={'c_'+v+'_'+suffixes[action] for action,present in actions.items() if present}
 item['animationGroups']=[dict(cab=c,names=sorted(set(names)),coversAllAvailableHostActions=required.issubset(names)) for c,names in sorted(groups.items())]
 item['newShaderNames']=sorted(set(item['shaderNames'])-{x[0] for x in known})
 evidence.append(item);print(v,'meshes',len(unresolved),'named candidates',sum(bool(x['indexedNameCandidates']) for x in unresolved),'actions',sum(actions.values()),'relocations',len(d['relocations']),'unverifiedMaterialCombinations',len(item['unverifiedMaterialCombinations']))
out=root/'TestResults/barrack-next-batch-2026-11-12';out.mkdir(parents=True,exist_ok=True)
m.dump(out/'candidate-evidence.json',dict(created=datetime.datetime.now().astimezone().isoformat(),auditCreated=raw['created'],auditSHA256=hashlib.sha256((a.out/'raw-audit.json').read_bytes()).hexdigest(),baseCabCount=raw['baseCabCount'],combinedCabCount=raw['combinedCabCount'],candidates=evidence))

queue=[]
for d in raw['variants']:
 v=d['variant'];issues=d['missing'];cab_missing=sorted({x['cab'] for x in issues if 'CAB not available' in x['reason']})
 if v in ('GrozaSR01','DusevnyjSSR01','PhaetusaSSR01'):state='integrated_presentation'
 elif not d['prefabs']:state='model_entry_unlocated'
 elif cab_missing:state='direct_cab_missing'
 elif issues and all(x['reason']=='Null mesh reference' for x in issues):state='mesh_mapping_required'
 elif issues:state='structure_verification_required'
 else:state='static_assets_ready_runtime_unverified'
 queue.append(dict(variant=v,state=state,renderers=len(d['renderers']),missingCabs=cab_missing,missingMeshBindings=sum(x['reason']=='Null mesh reference' for x in issues),missingReferences=issues))
m.dump(out/'all-variants-status.json',dict(auditCreated=raw['created'],counts=dict(collections.Counter(x['state'] for x in queue)),variants=queue))
