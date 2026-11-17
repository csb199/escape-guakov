"""Prove Florence source mesh identity against immutable GPU vertex streams and topology."""
import importlib.util,json,sys,struct,hashlib
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
s=importlib.util.spec_from_file_location('audit',ROOT/'Tools/Audit-BarrackCharacters.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
a=m.Audit(Path('F:/gfresource'),ROOT,ROOT/'LocalOnly/Barrack/next-batch-audit');a.scan()
from UnityPy.helpers.MeshHelper import MeshHandler
base=ROOT/'LocalOnly/Barrack/Captures/next-batch-20261112-190845';gpu=base/'native-mesh-evidence';draws=json.loads((gpu/'meshes.json').read_text())['draws']
r=json.loads((ROOT/'LocalOnly/Barrack/character-import/FlorenceSSR01/mesh-resolution.json').read_text());results=[]
for item in r['bindings']+r['gaps']:
 for candidate in item['candidates']:
  addr=(candidate['meshCab'],int(candidate['meshPathID']));mesh=a.reader(addr).read();h=MeshHandler(mesh);h.process();n=h.m_VertexCount
  source_i=np.asarray(h.m_IndexBuffer,dtype=np.uint32);matches=[]
  for d in draws:
   ib=d['indexBuffer'];b=(gpu/ib['file']).read_bytes();gi=np.frombuffer(b,dtype='<u2' if ib['stride']==2 else '<u4')
   if len(gi)!=len(source_i) or not np.array_equal(gi,source_i):continue
   uv=next((x for x in d['inputs'] if x['name']=='TEXCOORD0'),None);color=next((x for x in d['inputs'] if x['name']=='COLOR'),None)
   if not uv or not color:continue
   vb=d['vertexBuffers'][uv['buffer']];raw=(gpu/vb['file']).read_bytes()
   if len(raw)!=n*vb['stride']:continue
   guv=np.ndarray((n,2),dtype='<f2',buffer=raw,offset=uv['offset'],strides=(vb['stride'],2)).astype(np.float32)
   uv_err=float(np.max(np.abs(guv-np.asarray(h.m_UV0)[:,:2])))
   cv=d['vertexBuffers'][color['buffer']];cr=(gpu/cv['file']).read_bytes();gc=np.ndarray((n,4),dtype='<f4',buffer=cr,offset=color['offset'],strides=(cv['stride'],4))
   color_err=float(np.max(np.abs(gc-np.asarray(h.m_Colors)))) if h.m_Colors else None
   weight_match=None
   if h.m_BoneWeights and h.m_BoneIndices:
    for v in d['vertexBuffers']:
     if not v or v['stride']!=32:continue
     data=(gpu/v['file']).read_bytes()
     if len(data)!=n*32:continue
     weights=np.ndarray((n,4),dtype='<f4',buffer=data,strides=(32,4));bones=np.ndarray((n,4),dtype='<u4',buffer=data,offset=16,strides=(32,4))
     if np.allclose(weights,np.asarray(h.m_BoneWeights),atol=1e-6,rtol=0) and np.array_equal(bones,np.asarray(h.m_BoneIndices)):weight_match=True;break
   if uv_err<1e-6 and color_err is not None and color_err<1e-6:
    matches.append(dict(event=d['event'],indexBufferExact=True,vertexCount=n,uvMaximumError=uv_err,colorMaximumError=color_err,skinStreamExact=weight_match))
  results.append(dict(renderer=item['renderer'],meshCab=addr[0],meshPathID=str(addr[1]),assetKey=candidate['assetKeys'][0],orderedAvatarBonesMatch=candidate['avatarPathsMatchInOrder'],authoredBindposeError=candidate['maxBindposeMatrixError'],matches=matches))
report=dict(sourceCapture=str(base/'gf2-next-batch_frame20231.rdc'),captureSha256=hashlib.sha256((base/'gf2-next-batch_frame20231.rdc').read_bytes()).hexdigest(),meshes=results,allMatched=all(x['matches']and x['orderedAvatarBonesMatch']for x in results),note='Mesh identity uses exact full index buffer and immutable UV/color streams; authored bindposes retained. Pose-time deformation and source dynamics require separate validation.')
m.dump(ROOT/'LocalOnly/Barrack/character-import/FlorenceSSR01/gpu-mesh-proof.json',report)
for x in results:print(x['renderer'],len(x['matches']),[v['skinStreamExact']for v in x['matches'][:1]])
