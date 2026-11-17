"""Export source-backed barrack layout and dependency evidence; never invent assets.
Requires the existing extraction workspace (tools/extract_assets.py + UnityPy).
"""
import argparse
import json
from pathlib import Path
import sys


def export(source, output):
    source = Path(source).resolve()
    sys.path.insert(0, str(source / 'tools/python'))
    import UnityPy
    index = [json.loads(line) for line in (source / 'bundle_index.jsonl').read_text(encoding='utf-8').splitlines()]
    by_cab = {cab.lower(): row for row in index for cab in row.get('cab', [])}
    stem = '404a5060b818a55aa64205d887fe0a69'
    row = next(row for row in index if Path(row['file']).stem == stem)
    missing, visited = set(), set()
    def dependencies(item):
        if item['file'] in visited:
            return
        visited.add(item['file'])
        for external in item.get('deps', []):
            cab = external.rsplit('/', 1)[-1].lower()
            if cab in by_cab:
                dependencies(by_cab[cab])
            elif cab != 'unity default resources':
                missing.add(cab)
    dependencies(row)
    env = UnityPy.Environment()
    env.load_file(str(source / 'extracted/bundles' / (stem + '.bundle')))
    bundle = next(obj.read() for obj in env.objects if obj.type.name == 'AssetBundle')
    roots = [(key, info.asset) for key, info in bundle.m_Container
             if info.asset.type.name == 'GameObject' and info.asset.read().m_Name == 'BarrackCharacterCanvas']
    if len(roots) != 1:
        raise ValueError('Expected exactly one BarrackCharacterCanvas')
    nodes, components = [], []
    def walk(pointer, parent):
        go = pointer.read()
        objects = [part.component.deref() for part in go.m_Component]
        transform = next(obj for obj in objects if obj.type.name in ('Transform', 'RectTransform'))
        tree = transform.read_typetree()
        node_id = len(nodes)
        node = dict(id=node_id, parent=parent, name=go.m_Name, active=bool(go.m_IsActive),
                    sourcePathId=str(pointer.path_id), position=tree['m_LocalPosition'],
                    rotation=tree['m_LocalRotation'], scale=tree['m_LocalScale'],
                    rect=transform.type.name == 'RectTransform', hasCamera=False, hasLight=False)
        for key in ('m_AnchorMin', 'm_AnchorMax', 'm_AnchoredPosition', 'm_SizeDelta', 'm_Pivot'):
            if key in tree:
                node[key[2:3].lower()+key[3:]] = tree[key]
        nodes.append(node)
        for obj in objects:
            if obj.type.name in ('Transform', 'RectTransform'):
                continue
            record = dict(node=node_id, type=obj.type.name, pathId=str(obj.path_id))
            try:
                record['data'] = obj.read_typetree()
                if obj.type.name == 'Camera':
                    data = record['data']
                    node['hasCamera'] = True
                    node['camera'] = dict(fieldOfView=data.get('field of view', 60), near=data.get('near clip plane', .3),
                        far=data.get('far clip plane', 1000), orthographic=bool(data.get('orthographic', False)),
                        orthographicSize=data.get('orthographic size', 5), background=data.get('m_BackGroundColor', {}),
                        clearFlags=data.get('m_ClearFlags', 2), hdr=bool(data.get('m_HDR', True)))
                elif obj.type.name == 'Light':
                    data = record['data']
                    node['hasLight'] = True
                    node['light'] = dict(type=data['m_Type'], color=data['m_Color'], intensity=data['m_Intensity'],
                        range=data['m_Range'], spotAngle=data.get('m_SpotAngle',30), enabled=bool(data.get('m_Enabled',1)))
            except Exception as error:
                record['error'] = str(error)
            components.append(record)
        for child in transform.read().m_Children:
            walk(child.read().m_GameObject, node_id)
    walk(roots[0][1], -1)
    result = dict(schemaVersion=1, sourceBundle=stem, sourceAsset=roots[0][0], sourceName='BarrackCharacterCanvas',
                  status='source-layout-only', nodes=nodes, missingDependencies=sorted(missing),
                  limitations=['No complete character roster verified', 'Custom rendering and post processing not restored',
                               'Source MonoBehaviours retained as evidence, not executable game logic'])
    validate(result)
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    (output/'layout.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    # PathIDs in the evidence must also remain exact when consumed outside Python.
    def exact(value):
        if isinstance(value,dict):
            return {key:str(item) if key=='m_PathID' else exact(item) for key,item in value.items()}
        if isinstance(value,list):
            return [exact(item) for item in value]
        return value
    (output/'components.json').write_text(json.dumps(exact(components),ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(nodes=len(nodes),components=len(components),missingDependencies=len(missing),status=result['status'])))


def validate(layout):
    if layout.get('schemaVersion') != 1 or not layout.get('nodes'):
        raise ValueError('Unsupported or empty layout')
    for i,node in enumerate(layout['nodes']):
        if node['id'] != i or node['parent'] < -1 or node['parent'] >= i:
            raise ValueError('Invalid parent ordering at node '+str(i))
        if not isinstance(node['sourcePathId'],str):
            raise ValueError('PathID must remain an exact string')
    if sum(n['parent']==-1 for n in layout['nodes']) != 1:
        raise ValueError('Expected one source root')


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',default='F:/gfresource')
    parser.add_argument('--output',default='Assets/Game/Content/BarrackSource')
    parser.add_argument('--validate',type=Path)
    args=parser.parse_args()
    if args.validate:
        validate(json.loads(args.validate.read_text(encoding='utf-8')))
        print('PASS: source layout hierarchy and exact PathIDs')
    else:
        export(args.source,args.output)
