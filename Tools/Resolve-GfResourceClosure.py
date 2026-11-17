"""Resolve exact CAB dependencies through the full logical segment index.
Decoded bundles and evidence are local extraction outputs, not signed runtime assets.
"""
import argparse,json,sys,hashlib,importlib.util
from pathlib import Path

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--index',required=True,type=Path);p.add_argument('--bundle-root',required=True,type=Path)
    p.add_argument('--python-path',required=True,type=Path);p.add_argument('--root-cab',action='append',required=True)
    p.add_argument('--output',required=True,type=Path);a=p.parse_args();sys.path.insert(0,str(a.python_path))
    import UnityPy
    spec=importlib.util.spec_from_file_location('segments',Path(__file__).with_name('Index-GfResourceSegments.py'));module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    rows=[json.loads(line) for line in a.index.read_text(encoding='utf8').splitlines()];by_cab={}
    for row in rows:
        if row.get('error'):continue
        for cab in row['cab']:
            if cab.lower() in by_cab:raise ValueError('Duplicate CAB index: '+cab)
            by_cab[cab.lower()]=row
    a.output.mkdir(parents=True,exist_ok=True);bundles=a.output/'bundles';bundles.mkdir(exist_ok=True)
    queue=list(a.root_cab);seen=set();missing=set();results=[];paths=[]
    while queue:
        cab=queue.pop().rsplit('/',1)[-1].lower()
        if cab in seen or cab=='unity default resources':continue
        seen.add(cab);row=by_cab.get(cab)
        if row is None:missing.add(cab);continue
        data=module.segment_bytes(row,a.bundle_root);env=UnityPy.load(data)
        actual={s.name.lower() for s in env.assets}
        if cab not in actual:raise ValueError('CAB does not match segment: '+cab)
        seen.update(actual)
        deps=sorted({ext.path for s in env.assets for ext in s.externals});queue.extend(deps)
        target=bundles/(cab+'.bundle');target.write_bytes(data);paths.append(str(target.resolve()))
        objects=[dict(pathId=str(o.path_id),type=o.type.name,name=o.peek_name()) for o in env.objects]
        results.append(dict(cab=cab,source=row,sha256=hashlib.sha256(data).hexdigest(),dependencies=deps,objects=objects))
    result=dict(schemaVersion=2,status='dependency-closure-complete' if not missing else 'missing-dependencies',roots=a.root_cab,paths=paths,missing=sorted(missing),resources=results)
    (a.output/'plan.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps(dict(bundles=len(paths),missing=sorted(missing),status=result['status'])))
    if missing:sys.exit(2)
if __name__=='__main__':main()
