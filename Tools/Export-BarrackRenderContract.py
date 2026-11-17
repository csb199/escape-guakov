"""Export source rendering configuration without executing original managed code.
This preserves static configuration; it does not assert runtime quality/profile selection.
"""
import argparse,json,sys,math
from pathlib import Path

def normalize(value,key=''):
    if key=='m_PathID':return str(value)
    if isinstance(value,float) and not math.isfinite(value):return None
    if isinstance(value,dict):return {k:normalize(v,k) for k,v in value.items()}
    if isinstance(value,list):return [normalize(v) for v in value]
    return value

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--closure',type=Path,required=True);p.add_argument('--python-path',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--stage-closure',type=Path);a=p.parse_args()
    sys.path.insert(0,str(a.python_path));import UnityPy
    configs=[];errors=[]
    for cab in ['cab-e20724a6189d970370f521451808eaa8','cab-aa8a0bfb6f054816b7aa6b8f871769b2','cab-63ad59c72d7a68ddc65b6b995c62e870']:
        env=UnityPy.load(str(a.closure/'bundles'/(cab+'.bundle')))
        for obj in env.objects:
            if obj.type.name!='MonoBehaviour':continue
            try:
                tree=obj.read_typetree()
                configs.append(dict(cab=cab,pathId=str(obj.path_id),name=tree.get('m_Name',''),data=normalize(tree),externals=[e.path for e in obj.assets_file.externals]))
            except Exception as ex:errors.append(dict(cab=cab,pathId=str(obj.path_id),error=str(ex)))
    if a.stage_closure:
        cab='cab-29933d6c11d21cd4b00268198a92fda7'
        env=UnityPy.load(str(a.stage_closure/'bundles'/(cab+'.bundle')))
        objects=list(env.objects)
        profile=next(obj for obj in objects if obj.path_id==5645129384652940861).read_typetree()
        selected={5645129384652940861}|{ptr['m_PathID'] for ptr in profile['components']}
        for obj in objects:
            if obj.type.name=='MonoBehaviour' and obj.path_id in selected:
                tree=obj.read_typetree();configs.append(dict(cab=cab,pathId=str(obj.path_id),name=tree.get('m_Name',''),role='default-training-platform-profile',data=normalize(tree),externals=[e.path for e in obj.assets_file.externals]))
    result=dict(schemaVersion=1,status='static-source-configuration-not-runtime-selection',configurations=configs,errors=errors)
    a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps(dict(configurations=len(configs),errors=len(errors))))
    if errors:sys.exit(2)
if __name__=='__main__':main()
