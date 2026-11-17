"""Remove source-only MonoBehaviour components from the copied native scene; preserve engine assets and PPtr IDs."""
import argparse,hashlib,json,pathlib,sys

def main():
 p=argparse.ArgumentParser();p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--python-path',type=pathlib.Path,required=True);p.add_argument('--cab',required=True);p.add_argument('--report',type=pathlib.Path,required=True);p.add_argument('--closure',type=pathlib.Path);a=p.parse_args()
 if a.source.resolve()==a.output.resolve():raise ValueError('Use an independent project copy; never patch the original input')
 sys.path.insert(0,str(a.python_path));import UnityPy
 env=UnityPy.load(str(a.source));objects=[o for o in env.objects if o.assets_file.name.lower()==a.cab.lower()];removed={o.path_id for o in objects if o.type.name=='MonoBehaviour'}
 types={}
 if a.closure:
  plan=json.loads(a.closure.read_text(encoding='utf8'))
  for resource in plan['resources']:
   types[resource['cab'].lower()]={int(o['pathId']):o['type'] for o in resource['objects']}
 count=0
 for obj in objects:
  if obj.type.name=='GameObject':
   tree=obj.read_typetree();before=len(tree['m_Component']);tree['m_Component']=[c for c in tree['m_Component'] if c['component']['m_FileID']!=0 or c['component']['m_PathID'] not in removed]
   if len(tree['m_Component'])!=before:obj.save_typetree(tree);count+=1
  elif obj.type.name=='PreloadData':
   tree=obj.read_typetree()
   if 'm_Assets'in tree:tree['m_Assets']=[c for c in tree['m_Assets'] if c['m_FileID']!=0 or c['m_PathID'] not in removed];obj.save_typetree(tree)
 for preload in env.objects:
  if preload.type.name!='PreloadData' or not preload.assets_file.name.lower().startswith(a.cab.lower()):continue
  tree=preload.read_typetree();kept=[]
  for ptr in tree['m_Assets']:
   cab=preload.assets_file.name.lower() if ptr['m_FileID']==0 else preload.assets_file.externals[ptr['m_FileID']-1].path.split('/')[-1].lower()
   if types.get(cab,{}).get(ptr['m_PathID']) not in ['MonoBehaviour','MonoScript']:kept.append(ptr)
  tree['m_Assets']=kept;preload.save_typetree(tree)
 for obj in objects:
  if obj.path_id in removed:del obj.assets_file.objects[obj.path_id]
 data=env.file.save(packer='lz4');a.output.write_bytes(data)
 report=dict(source=str(a.source),sourceSha256=hashlib.sha256(a.source.read_bytes()).hexdigest(),outputSha256=hashlib.sha256(data).hexdigest(),sceneCab=a.cab,removedMonoBehaviours=len(removed),changedGameObjects=count,keptEngineObjects=len(objects)-len(removed))
 a.report.write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps(report))
if __name__=='__main__':main()
