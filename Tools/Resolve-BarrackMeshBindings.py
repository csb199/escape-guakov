import zlib
"""Resolve declared mesh candidates with Avatar path hashes and bind-pose matrices, never name alone."""
import importlib.util,json,re,sys
from pathlib import Path
import numpy as np
root=Path(__file__).resolve().parents[1]
s=importlib.util.spec_from_file_location('audit',Path(__file__).with_name('Audit-BarrackCharacters.py'));m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
a=m.Audit(Path('F:/gfresource'),root,root/'LocalOnly/Barrack/next-batch-audit');a.scan()
for variant in sys.argv[1:] or ['FlorenceSSR01','ZhaohuiSSR01','ClukaySSR01']:
 d=json.loads((a.out/'variants'/f'{variant}.json').read_text(encoding='utf8'));p=d['prefabs'][0];go=(p['cab'],int(p['pathID']));paths={};worlds={};renderers={};declarations={};avatar=None
 def matrix(t):
  q=t['m_LocalRotation'];x,y,z,w=[q[k] for k in 'xyzw'];rot=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
  out=np.eye(4);out[:3,:3]=rot@np.diag([t['m_LocalScale'][k] for k in 'xyz']);out[:3,3]=[t['m_LocalPosition'][k] for k in 'xyz'];return out
 def walk(g,path,parent):
  nonlocal_dummy=None
  comps=[a.ref(g,c['component']) for c in a.tree(g)['m_Component']];tr=next(c for c in comps if a.reader(c).type.name=='Transform');t=a.tree(tr);world=parent@matrix(t) if path else np.eye(4);paths[tr]=path;worlds[tr]=world
  for c in comps:
   typ=a.reader(c).type.name
   if typ=='SkinnedMeshRenderer' and not re.search(r'_lod(?:1|2|m\d+)$',a.tree(g)['m_Name'],re.I):renderers[a.tree(g)['m_Name']]=(c,tr)
   if typ=='MonoBehaviour':
    try:
     for item in a.tree(c).get('MeshResList',[]):declarations[item['TransfromPath']]=item['MeshResPath']
    except:pass
  for child in t['m_Children']:
   ch=a.ref(tr,child);cg=a.ref(ch,a.tree(ch)['m_GameObject']);name=a.tree(cg)['m_Name'];walk(cg,(path+'/' if path else '')+name,world)
 walk(go,'',np.eye(4))
 for comp in a.tree(go)['m_Component']:
  c=a.ref(go,comp['component'])
  if a.reader(c).type.name=='Animator':avatar=a.ref(c,a.tree(c)['m_Avatar'])
 tos=dict(a.tree(avatar)['m_TOS']) if avatar else {}
 avatar_worlds={}
 if avatar:
  av=a.tree(avatar)['m_Avatar'];sk=av['m_AvatarSkeleton']['data'];poses=av['m_AvatarSkeletonPose']['data']['m_X'];ids=sk.get('m_ID',av['m_SkeletonNameIDArray']);ordered_worlds=[]
  for i,(node,pose) in enumerate(zip(sk['m_Node'],poses)):
   local=matrix(dict(m_LocalRotation=pose['q'],m_LocalPosition=pose['t'],m_LocalScale=pose['s']));parent=node['m_ParentId'];world=local if parent<0 else ordered_worlds[parent]@local;ordered_worlds.append(world);avatar_worlds[tos.get(ids[i],'')]=world

 result=dict(variant=variant,sourcePrefab=p,avatar=list(avatar) if avatar else None,bindings=[],gaps=[])
 for name,(ra,tr) in renderers.items():
  rt=a.tree(ra)
  if rt['m_Mesh']['m_PathID']:continue
  declared=declarations.get(name);candidates=[]
  for ma,info in a.names.items():
   if info!=('Mesh',name):continue
   mt=a.tree(ma);bones=[a.ref(ra,b) for b in rt['m_Bones']];hashes=mt['m_BoneNameHashes'];ordered=len(hashes)==len(bones)==len(mt['m_BindPose']) and all(paths.get(b) is not None and (tos.get(h)==paths[b] or (h not in tos and zlib.crc32(paths[b].encode('utf8'))==h)) for h,b in zip(hashes,bones))
   errs=[];avatar_errors=[];bone_errors=[]
   for b,bind in zip(bones,mt['m_BindPose']):
    expected=np.linalg.inv(worlds[b])@worlds[tr];actual=np.array([[bind[f'e{i}{j}'] for j in range(4)] for i in range(4)]);errs.append(float(np.max(np.abs(expected-actual))));bone_errors.append(dict(path=paths.get(b),error=errs[-1],index=len(errs)-1))
    if paths[b] in avatar_worlds and paths[tr] in avatar_worlds:avatar_errors.append(float(np.max(np.abs(np.linalg.inv(avatar_worlds[paths[b]])@avatar_worlds[paths[tr]]-actual))))
   reader=a.reader(ma);keys=list(dict.fromkeys(k for o in reader.assets_file.objects.values() if o.type.name=='AssetBundle' for k,v in o.read().m_Container if v.asset.path_id==ma[1]))
   candidates.append(dict(meshCab=ma[0],meshPathID=str(ma[1]),assetKeys=keys,boneCount=len(hashes),avatarPathsMatchInOrder=ordered,bonePathMismatches=[dict(hash=h,avatarPath=tos.get(h),rendererPath=paths.get(b)) for h,b in zip(hashes,bones) if tos.get(h)!=paths.get(b)],maxBindposeMatrixError=max(errs,default=999),maxAvatarBindposeMatrixError=max(avatar_errors,default=999) if len(avatar_errors)==len(bones)==len(mt['m_BindPose']) else 999,bindposeOutliers=[b for b in bone_errors if b['error']>=.001]))
  accepted=[c for c in candidates if c['avatarPathsMatchInOrder'] and min(c['maxBindposeMatrixError'],c['maxAvatarBindposeMatrixError'])<.001 and len(c['assetKeys'])==1]
  item=dict(renderer=name,declaredPath=declared,candidates=candidates)
  if declared and Path(declared).stem==name and len(accepted)==1:
   item.update(accepted[0]);item['evidence']='Declared source mesh path + unique candidate matching ordered Avatar bone-path hashes and all rest bind-pose matrices; catalog path mapping remains unverified';result['bindings'].append(item)
  else:result['gaps'].append(item)
 m.dump(root/'LocalOnly/Barrack/character-import'/variant/'mesh-resolution.json',result)
 print(variant,'resolved',len(result['bindings']),'gaps',len(result['gaps']))
 for item in result['bindings']+result['gaps']:print(item['renderer'],[(c['boneCount'],c['avatarPathsMatchInOrder'],round(c['maxBindposeMatrixError'],6),round(c['maxAvatarBindposeMatrixError'],6))for c in item['candidates']])
