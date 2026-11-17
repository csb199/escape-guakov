"""Recover camera anchors and touch capsules from the imported source assets."""
import argparse
import json
from pathlib import Path
import sys
import struct


def varint(data, offset):
    value = shift = 0
    while True:
        byte = data[offset]
        offset += 1
        value |= (byte & 127) << shift
        if byte < 128:
            return value, offset
        shift += 7


def fields(data):
    result = {}
    offset = 0
    while offset < len(data):
        tag, offset = varint(data, offset)
        wire = tag & 7
        if wire == 0:
            value, offset = varint(data, offset)
        elif wire == 2:
            length, offset = varint(data, offset)
            value = data[offset:offset + length]
            offset += length
        elif wire in (1, 5):
            length = 8 if wire == 1 else 4
            value = data[offset:offset + length]
            offset += length
        else:
            raise ValueError('Unsupported protobuf wire type')
        result.setdefault(tag >> 3, []).append(value)
    return result


def table(path):
    data = path.read_bytes()
    return [{key: values[-1] for key, values in fields(row).items()} for row in fields(data[4 + struct.unpack_from('<I', data)[0]:]).get(1, [])]


def export(workspace, extraction, characters=None):
    sys.path.insert(0, str(extraction / 'tools/python'))
    import UnityPy
    index = [json.loads(line) for line in (extraction / 'bundle_index.jsonl').read_text(encoding='utf8').splitlines()]
    tables = Path('E:/Games/GF2Exilium/GF2 Game/GF2_Exilium_Data/LocalCache/Data/Table')
    guns = table(tables / 'GunData.bytes')
    interactions = table(tables / 'BarrackInteractData.bytes')
    plans = sorted((workspace / 'LocalOnly/Barrack/character-import').glob('*/import-plan.json'))
    sources = [('GrozaSR01', workspace / 'LocalOnly/Barrack/live/bundles/CAB-8197543835efac73430dc329e0fea24c.bundle', None, '')]
    for plan_path in plans:
        plan = json.loads(plan_path.read_text(encoding='utf-8-sig'))
        if not (workspace / 'Assets/Game/Content/BarrackSource' / plan['variant'] / (plan['variant']+'.prefab')).exists():
            continue
        source = Path(plan['sourceBundlePath']) if plan.get('sourceBundlePath') else None
        if source is None:
            row = next(row for row in index if plan['sourceAssetName'] in row.get('containers', []))
            candidates = [Path(path) for path in plan['paths']]
            source = next((path for path in candidates if path.stem.lower() == Path(row['file']).stem.lower() or path.stem.lower() in [cab.lower() for cab in row['cab']]), None)
        if source is None:
            raise ValueError('Missing source bundle: ' + plan['variant'])
        sources.append((plan['variant'], source, plan['sourceAssetName'], plan.get('sourceTransformPath', '')))
    for character, source, asset_name, transform_path in sources:
        output = workspace / 'Assets/Game/Content/BarrackSource' / character / 'interaction-contract.json'
        if characters is not None and character not in characters:
            continue
        if output.exists() and characters is None:
            continue
        gun = next(row for row in guns if row.get(5, b'').decode('utf8') == character[:-2])
        actions = [dict(id=row[1], event=row.get(3, 0), clip=row.get(9, b'').decode('utf8').replace('{0}', character), weight=row.get(11, 0), horizontal=row.get(14, b'').decode('utf8'), vertical=row.get(15, b'').decode('utf8'), zoom=row.get(16, b'').decode('utf8')) for row in interactions if str(row.get(2, '')).startswith(str(gun[1]))]
        environment = UnityPy.load(str(source))
        if asset_name:
            bundle = next(obj.read() for obj in environment.objects if obj.type.name == 'AssetBundle')
            root = next(info.asset for key, info in bundle.m_Container if key == asset_name).read()
        else:
            root = next(obj.read() for obj in environment.objects if obj.type.name == 'GameObject' and obj.peek_name() == character)
        records = []

        def walk(game_object, path):
            components = [part.component for part in game_object.m_Component]
            transform = next(part.read() for part in components if part.type.name in ('Transform', 'RectTransform'))
            records.append((path, components, transform))
            for child in transform.m_Children:
                child_object = child.read().m_GameObject.read()
                walk(child_object, path + '/' + child_object.m_Name if path else child_object.m_Name)

        walk(root, '')
        if transform_path or root.m_Name != character:
            prefix = transform_path or next(path for path, _, _ in records if path.split('/')[-1] == character)
            records = [(path[len(prefix):].lstrip('/'), parts, transform) for path, parts, transform in records if path == prefix or path.startswith(prefix + '/')]
        capsules = []
        for path, parts, transform in records:
            event = 0
            follow = ''
            for part in parts:
                if part.type.name != 'MonoBehaviour':
                    continue
                try:
                    data = part.read_typetree()
                except ValueError:
                    continue
                event = data.get('facilityBarrackModelExcuteEnum', event)
                if 'source' in data and data['source'].get('m_PathID'):
                    follow = part.read().source.read().m_GameObject.read().m_Name
            for part in parts:
                if part.type.name != 'CapsuleCollider':
                    continue
                data = part.read_typetree()
                capsules.append(dict(path=path, event=event, followBone=follow, center=data['m_Center'], radius=data['m_Radius'], height=data['m_Height'], direction=data['m_Direction'], enabled=data['m_Enabled']))
        if not capsules:
            raise ValueError('No source capsules for ' + character)
        camera_paths = [path for path, _, _ in records if path.rsplit('/', 1)[-1] == 'CameraPoint']
        if len(camera_paths) != 1:
            raise ValueError('Missing or ambiguous source CameraPoint: ' + character)
        camera_path = camera_paths[0]
        output = workspace / 'Assets/Game/Content/BarrackSource' / character / 'interaction-contract.json'
        output.write_text(json.dumps(dict(character=character, sourceBundle=source.name, cameraPoint=camera_path, capsules=capsules, actions=actions), ensure_ascii=False, indent=2) + '\n', encoding='utf8')
        print(character, 'capsules=' + str(len(capsules)), 'camera=' + camera_path)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--extraction', type=Path, default=Path('F:/gfresource'))
    parser.add_argument('--characters', nargs='+', help='Explicitly regenerate these source contracts')
    options = parser.parse_args()
    export(options.workspace, options.extraction, options.characters)
