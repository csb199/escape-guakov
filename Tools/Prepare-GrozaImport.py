"""Prepare a local decoded-bundle dependency plan for the Groza source importer."""
import argparse,json
from pathlib import Path

def prepare(source):
    source=Path(source)
    rows=[json.loads(line) for line in (source/'bundle_index.jsonl').read_text(encoding='utf8').splitlines()]
    by_cab={cab.lower():r for r in rows for cab in r.get('cab',[])}
    by_stem={Path(r['file']).stem:r for r in rows}
    seen=set(); paths=[]; missing=set(); unavailable=[]
    def visit(row):
        if row['file'] in seen:return
        seen.add(row['file'])
        for dep in row.get('deps',[]):
            cab=dep.rsplit('/',1)[-1].lower()
            if cab in by_cab:visit(by_cab[cab])
            elif cab!='unity default resources':missing.add(cab)
        path=source/'extracted/bundles'/Path(row['file']).name
        if path.is_file():paths.append(str(path.resolve()))
        else:unavailable.append(str(path))
    for stem in ['ef3f19c1bd1006e547ce667d8f91d74c','74805d6a0134834b9bc296cefba5bc13',
                 '00c0785e4c7b8394cd6b4f831ffc4dbc','4ea74c92dc1590b1ceb869cc85d0fdeb']:
        visit(by_stem[stem])
    return dict(schemaVersion=1,status='unverified-source-plan',paths=paths,missing=sorted(missing),unavailable=unavailable)


def prepare_resolved(closure,python_path):
    """Bind the default prefab through exact cutscene Mesh PPtrs and container keys.
    The source prefab's MeshResList is checked before accepting a binding.
    """
    import sys
    sys.path.insert(0,str(python_path));import UnityPy
    closure=Path(closure);plan=json.loads((closure/'plan.json').read_text(encoding='utf8'))
    if plan['missing']:raise ValueError('Incomplete dependency closure')
    source=UnityPy.load(str(closure/'bundles/cab-26f0d4d469754d8405127e034fb5e61c.bundle'))
    default=UnityPy.load(str(closure/'bundles/cab-8197543835efac73430dc329e0fea24c.bundle'))
    declared={}
    for obj in default.objects:
        if obj.type.name=='MonoBehaviour':
            if not any(node.m_Name=='MeshResList' for node in obj.serialized_type.nodes.traverse()):continue
            tree=obj.read_typetree()
            for item in tree.get('MeshResList',[]):declared[item['TransfromPath']]=item['MeshResPath']
    bindings=[]
    for obj in source.objects:
        if obj.type.name!='SkinnedMeshRenderer':continue
        data=obj.read();name=data.m_GameObject.read().m_Name
        if not name.startswith('c_GrozaSR01_') or not name.endswith('lod0'):continue
        if declared.get(name)!='Assets/ArtsResource/Player/Groza/GrozaSR01/Models/'+name+'.mesh':
            raise ValueError('Default prefab does not declare the expected mesh path: '+name)
        ptr=data.m_Mesh;cab=obj.assets_file.externals[ptr.m_FileID-1].path.rsplit('/',1)[-1].lower()
        env=UnityPy.load(str(closure/'bundles'/(cab+'.bundle')));keys=[]
        for item in env.objects:
            if item.type.name=='AssetBundle':keys.extend(k for k,v in item.read().m_Container if v.asset.path_id==ptr.m_PathID)
        mesh=next(item for item in env.objects if item.path_id==ptr.m_PathID)
        if len(keys)!=1 or mesh.peek_name()!=name:raise ValueError('Ambiguous mesh binding: '+name)
        bindings.append(dict(renderer=name,declaredPath=declared[name],cab=cab,assetKey=keys[0],pathId=str(ptr.m_PathID)))
    if len(bindings)!=5 or len({x['renderer'] for x in bindings})!=5:raise ValueError('Expected exactly five LOD0 mesh bindings')
    material_bindings=[];loaded={}
    def get_env(cab):
        if cab not in loaded:loaded[cab]=UnityPy.load(str(closure/'bundles'/(cab+'.bundle')))
        return loaded[cab]
    for cab in ['cab-eae661eceff268e7d3a73ff32a85729f','cab-8dfbd4eec15b760169556bf05d740ade']:
        for obj in get_env(cab).objects:
            if obj.type.name!='Material':continue
            tree=obj.read_typetree();textures=[]
            for prop,value in tree['m_SavedProperties']['m_TexEnvs']:
                ptr=value['m_Texture']
                if not ptr['m_PathID']:continue
                tcab=obj.assets_file.externals[ptr['m_FileID']-1].path.rsplit('/',1)[-1].lower() if ptr['m_FileID'] else cab
                env=get_env(tcab);keys=[]
                for item in env.objects:
                    if item.type.name=='AssetBundle':keys.extend(k for k,v in item.read().m_Container if v.asset.path_id==ptr['m_PathID'])
                tex=next(item for item in env.objects if item.path_id==ptr['m_PathID'])
                if len(keys)!=1:raise ValueError('Ambiguous texture address: '+prop)
                texture=tex.read();raw=texture.get_image_data();raw_dir=closure/'texture-data';raw_dir.mkdir(exist_ok=True)
                raw_path=raw_dir/(tcab+'_'+str(ptr['m_PathID'])+'.bin');raw_path.write_bytes(raw)
                if not raw:raise ValueError('Texture has no pixel data')
                textures.append(dict(property=prop,cab=tcab,assetKey=keys[0],pathId=str(ptr['m_PathID']),name=tex.peek_name(),srgb=texture.m_ColorSpace==1,rawDataPath=str(raw_path.resolve())))
            sp=tree['m_Shader'];scab=obj.assets_file.externals[sp['m_FileID']-1].path.rsplit('/',1)[-1].lower() if sp['m_FileID'] else cab
            shader=next(item for item in get_env(scab).objects if item.path_id==sp['m_PathID']).read_typetree()['m_ParsedForm']['m_Name']
            material_bindings.append(dict(name=tree['m_Name'],shaderName=shader,textures=textures,keywords=tree['m_ShaderKeywords']))
    plan['materialBindings']=material_bindings
    plan['animationLibraryPath']=str((closure/'bundles/cab-5596806505a89f79d104350619fd553e.bundle').resolve())
    plan['shaderLibraryPath']=str((closure/'bundles/cab-c5f876cb9cb4f5c98c84c7b78ba454bf.bundle').resolve())
    plan.update(sourceAssetName='7dc1252d9ecc94a49828b58d0d50a41c',meshBindings=bindings)
    return plan

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',default='F:/gfresource');p.add_argument('--output',type=Path,default=Path('LocalOnly/Barrack/groza-import-plan.json'));p.add_argument('--resolved-closure',type=Path);p.add_argument('--python-path',type=Path,default=Path('F:/gfresource/tools/python'));a=p.parse_args()
    result=prepare_resolved(a.resolved_closure,a.python_path) if a.resolved_closure else prepare(a.source);a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(result,indent=2),encoding='utf8')
    print(json.dumps(dict(bundles=len(result['paths']),missing=len(result['missing']),unavailable=len(result.get('unavailable',[])))))
