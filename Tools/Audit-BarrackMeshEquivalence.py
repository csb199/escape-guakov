"""Audit exact native mesh differences; never selects a differing visible payload automatically."""
import importlib.util,json,sys,struct,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
s=importlib.util.spec_from_file_location('audit',ROOT/'Tools/Audit-BarrackCharacters.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
IGNORE={'m_BindPose','m_BonesAABB','m_MeshUsageFlags','m_IsReadable','m_KeepVertices','m_KeepIndices'}
def digest(t):return hashlib.sha256(json.dumps(t,sort_keys=True,separators=(',',':'),default=lambda x:list(x)).encode()).hexdigest()
def curve_values(t,attribute):
 c=t['m_MuscleClip']['m_Clip']['data'];bs=t['m_ClipBindingConstant']['genericBindings'];offset=0;targets=[]
 for b in bs:
  if b['typeID']==137 and b['customType']==20 and b['attribute']==attribute:targets.append(offset)
  offset+=({1:3,2:4,3:3,4:3}.get(b['attribute'],1)if b['typeID']==4 else 1)
 stream=c['m_StreamedClip'];dense=c['m_DenseClip'];constant=c['m_ConstantClip']['data'];ns=stream['curveCount'];nd=dense['m_CurveCount']
 if offset!=ns+nd+len(constant):raise ValueError('Animation binding scalar count mismatch')
 result=[]
 for target in targets:
  if target<ns:
   raw=struct.pack('<%dI'%len(stream['data']),*stream['data']);pos=0
   while pos<len(raw):
    time,count=struct.unpack_from('<fi',raw,pos);pos+=8
    for _ in range(count):
     index,*coeff=struct.unpack_from('<i4f',raw,pos);pos+=20
     if index==target:result.extend(coeff)
  elif target<ns+nd:result.extend(dense['m_SampleArray'][target-ns::nd])
  else:result.append(constant[target-ns-nd])
 if not result:raise ValueError('Differing expression has no explicit zero curve; prefab defaults unverified')
 return result

def main():
 out=ROOT/'LocalOnly/Barrack/equivalence-batch';out.mkdir(exist_ok=True);a=m.Audit(Path('F:/gfresource'),ROOT,ROOT/'LocalOnly/Barrack/next-batch-audit');a.scan();result=[]
 for v in (sys.argv[1:]or json.loads((out/'candidates.json').read_text())):
  row=dict(character=v,meshes=[],status='pending');result.append(row)
  try:
   audit=json.loads((a.out/'variants'/f'{v}.json').read_text(encoding='utf8'));resolution=json.loads((ROOT/'LocalOnly/Barrack/character-import'/v/'mesh-resolution.json').read_text());idle=next(c for c in audit['clips']if c['name']=='c_'+v+'_BarrackIdle');
   if len(idle['available'])!=1:raise ValueError('Multiple idle animation libraries; source selection unverified: '+str(len(idle['available'])))
   cab=idle['available'][0]['cab'];clips=[c for c in audit['clips']if sum(av['cab']==cab for av in c['available'])==1 and not c['name'].endswith('_Camera')];cliptrees={}
   for item in resolution['bindings']+resolution['gaps']:
    good=[c for c in item['candidates']if c['avatarPathsMatchInOrder']and min(c['maxBindposeMatrixError'],c['maxAvatarBindposeMatrixError'])<.001 and len(c['assetKeys'])==1];good.sort(key=lambda c:(min(c['maxBindposeMatrixError'],c['maxAvatarBindposeMatrixError']),c['meshCab']))
    rec=dict(renderer=item['renderer'],candidateCount=len(good));row['meshes'].append(rec)
    if not good:rec.update(status='blocked',reason='no structural candidate');continue
    trees=[a.tree((c['meshCab'],int(c['meshPathID'])))for c in good];base=trees[0];diffs=[sorted(k for k in t if t[k]!=base[k])for t in trees[1:]];rec['differentFields']=diffs;visible=set(k for diff in diffs for k in diff)-IGNORE
    if 'm_VertexData' in visible and not (visible-{'m_Shapes','m_VertexData'}):
     import numpy as np
     from UnityPy.helpers.MeshHelper import MeshHandler
     decoded=[]
     for candidate in good:
      handler=MeshHandler(a.reader((candidate['meshCab'],int(candidate['meshPathID']))).read());handler.process();decoded.append(handler)
     semantic=True;stats=[]
     for handler in decoded[1:]:
      differences={}
      for attr in ['m_Vertices','m_Normals','m_Tangents','m_Colors','m_UV0','m_UV1','m_UV2','m_UV3','m_UV4','m_UV5','m_UV6','m_UV7','m_BoneWeights','m_BoneIndices','m_IndexBuffer']:
       x=getattr(decoded[0],attr);y=getattr(handler,attr)
       if x is None or y is None:
        if x is not y:semantic=False;differences[attr]='missing channel'
        continue
       x=np.asarray(x,dtype=float);y=np.asarray(y,dtype=float)
       if x.shape!=y.shape:semantic=False;differences[attr]='shape mismatch';continue
       delta=float(np.max(np.abs(x-y),initial=0));differences[attr]=delta
       limit=5e-7 if attr in ('m_Vertices','m_Normals','m_Tangents','m_Colors') else 0
       if not np.isfinite(delta) or delta>limit:semantic=False
      stats.append(differences)
     rec['decodedAttributeDifferences']=stats
     if semantic:visible.remove('m_VertexData');rec['numericTolerance']=5e-7
    if visible-{'m_Shapes'}:rec.update(status='blocked',reason='different static geometry/attributes',visibleFields=sorted(visible));continue
    changed={};uncertain=False
    for t in trees[1:]:
     x=base['m_Shapes'];y=t['m_Shapes']
     if x==y:continue
     if x['channels']!=y['channels'] or x['fullWeights']!=y['fullWeights']:uncertain=True;break
     for ch in x['channels']:
      for fi in range(ch['frameIndex'],ch['frameIndex']+ch['frameCount']):
       sx=x['shapes'][fi];sy=y['shapes'][fi];vx=x['vertices'][sx['firstVertex']:sx['firstVertex']+sx['vertexCount']];vy=y['vertices'][sy['firstVertex']:sy['firstVertex']+sy['vertexCount']]
       if sx!=sy or vx!=vy:changed[ch['name']]=ch['nameHash']
    if uncertain:rec.update(status='blocked',reason='different blendshape channel mapping or weights');continue
    checks=[];excluded=set()
    try:
     for name,h in changed.items():
      for clip in clips:
       if clip['name']not in cliptrees:
        av=next(av for av in clip['available']if av['cab']==cab);cliptrees[clip['name']]=a.tree((cab,int(av['pathID'])))
       values=curve_values(cliptrees[clip['name']],h)
       if any(x!=0 for x in values):
        if clip['name'].endswith(('_BarrackIdle','_BarrackEntrance_Normal','_ToIdle')):raise ValueError('Differing expression active in mandatory action: '+name+' / '+clip['name'])
        excluded.add(clip['name']);continue
       checks.append(dict(clip=clip['name'],expression=name,allWeightsZero=True))
    except Exception as e:rec.update(status='blocked',reason=str(e),changedExpressions=changed);continue
    rec.update(status=('numeric_payload_equivalent'if rec.get('numericTolerance') else 'exact_payload_equivalent')if not changed else 'scoped_zero_expression_equivalent',selected=good[0],sourceTreeSHA256=digest(base),changedExpressions=changed,clipChecks=checks,inspectedClips=[c['name']for c in clips if c['name']not in excluded],excludedClips=sorted(excluded))
   row['status']='eligible'if all(x['status']!='blocked'for x in row['meshes'])else 'blocked'
  except Exception as e:row.update(status='blocked',error=str(e))
  a.trees.clear();(out/'equivalence-audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8');print(v,row['status'],[(x['renderer'],x.get('reason',x['status']))for x in row['meshes']if x.get('status')=='blocked'],flush=True)
if __name__=='__main__':main()
