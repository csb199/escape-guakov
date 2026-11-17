"""Select native candidates for Unity import, recording exact duplicate and near-equal bindpose ambiguity.
This is a host binding selection, not a claim that source catalog addresses have been recovered.
"""
import importlib.util,json,hashlib,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
s=importlib.util.spec_from_file_location('a',ROOT/'Tools/Audit-BarrackCharacters.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m);a=m.Audit(Path('F:/gfresource'),ROOT,ROOT/'LocalOnly/Barrack/next-batch-audit');a.scan()
override_path=ROOT/'Assets/Game/Content/BarrackSource/mesh-selection-overrides.json'
overrides=json.loads(override_path.read_text(encoding='utf8'))['selections'] if override_path.exists() else []
for v in (sys.argv[1:] or ['ZhaohuiSSR01','ClukaySSR01']):
 p=ROOT/'LocalOnly/Barrack/character-import'/v;r=json.loads((p/'mesh-resolution.json').read_text());bindings=[]
 for item in r['bindings']+r['gaps']:
  good=[c for c in item['candidates']if c['avatarPathsMatchInOrder']and min(c['maxBindposeMatrixError'],c['maxAvatarBindposeMatrixError'])<.001 and len(c['assetKeys'])==1]
  if not good:raise ValueError(('No structural candidate',item['renderer']))
  good.sort(key=lambda c:(min(c['maxBindposeMatrixError'],c['maxAvatarBindposeMatrixError']),c['meshCab']))
  explicit=[e for e in overrides if e['character']==v and e['renderer']==item['renderer']]
  if len(explicit)>1:raise ValueError(('Duplicate mesh evidence override',v,item['renderer']))
  if explicit:
   proof=explicit[0]
   matches=[c for c in good if c['meshCab']==proof['meshCab'] and c['meshPathID']==proof['meshPathID'] and c['assetKeys']==[proof['assetKey']]]
   if len(matches)!=1:raise ValueError(('Evidence selection no longer structurally valid',v,item['renderer']))
   chosen=matches[0];tree=a.tree((chosen['meshCab'],int(chosen['meshPathID'])))
   digest=hashlib.sha256(json.dumps(tree,sort_keys=True,separators=(',',':'),default=lambda x:list(x)).encode()).hexdigest()
   if digest!=proof['sourceTreeSHA256']:raise ValueError(('Evidence source payload changed',v,item['renderer']))
   bindings.append(dict(renderer=item['renderer'],meshCab=chosen['meshCab'],meshPathID=chosen['meshPathID'],assetKey=chosen['assetKeys'][0],authoredBindposesPreserved=True,restError=min(chosen['maxBindposeMatrixError'],chosen['maxAvatarBindposeMatrixError']),restPoseSource='Avatar' if chosen['maxAvatarBindposeMatrixError']<chosen['maxBindposeMatrixError'] else 'Transform',resourceIdentity='unresolved',selectionEvidence=proof,evidence=proof['selectionBasis']))
   continue
  chosen=good[0];trees=[a.tree((c['meshCab'],int(c['meshPathID'])))for c in good];diffs=[]
  for c,t in zip(good[1:],trees[1:]):
   keys=[k for k in t if t[k]!=trees[0][k]]
   if set(keys)-{'m_BindPose','m_BonesAABB','m_MeshUsageFlags'}:raise ValueError(('Different geometry candidate',item['renderer'],keys))
   diffs.append(dict(cab=c['meshCab'],pathID=c['meshPathID'],differentFields=keys,restError=c['maxBindposeMatrixError']))
  bindings.append(dict(renderer=item['renderer'],meshCab=chosen['meshCab'],meshPathID=chosen['meshPathID'],assetKey=chosen['assetKeys'][0],authoredBindposesPreserved=True,restError=min(chosen['maxBindposeMatrixError'],chosen['maxAvatarBindposeMatrixError']),restPoseSource='Avatar' if chosen['maxAvatarBindposeMatrixError']<chosen['maxBindposeMatrixError'] else 'Transform',candidateAlternatives=diffs,evidence='Declared source path + ordered Avatar bone paths + rest bindposes; choose minimum rest error among identical geometry/skin payloads. Source catalog identity remains unverified.'))
 m.dump(p/'verified-bindings.json',dict(variant=v,status='native compatible selection; Unity validation required',bindings=bindings))
 print(v,len(bindings))
