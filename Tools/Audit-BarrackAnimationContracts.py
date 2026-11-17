"""Audit imported character animation payloads against the local source index and tables."""
import argparse
import hashlib
import json
from pathlib import Path
import runpy
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source', type=Path, default=Path('F:/gfresource'))
parser.add_argument('--output', type=Path, default=Path('LocalOnly/Barrack/animation-audit'))
options = parser.parse_args()
root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(options.source / 'tools/python'))
import UnityPy

table = runpy.run_path(str(root / 'Tools/Export-BarrackInteraction.py'))['table']
tables = Path('E:/Games/GF2Exilium/GF2 Game/GF2_Exilium_Data/LocalCache/Data/Table')
guns = table(tables / 'GunData.bytes')
interactions = table(tables / 'BarrackInteractData.bytes')
index = [json.loads(line) for line in (options.source / 'bundle_index.jsonl').open(encoding='utf8')]
supplement = root / 'LocalOnly/Barrack/next-batch-audit/supplement-index.jsonl'
if supplement.exists():
    index.extend(json.loads(line) for line in supplement.open(encoding='utf8'))
roster = json.loads((root / 'Assets/Game/Content/BarrackSource/roster.json').read_text(encoding='utf8'))
report = []
for entry in roster['characters']:
    character = entry['id']
    folder = root / 'Assets/Game/Content/BarrackSource' / character
    payload = folder / 'OriginalBarrackAnimations.bytes'
    environment = UnityPy.load(str(payload))
    clips, controllers, source_environments = [], [], {}
    def compare_source(obj, name):
        matches = [dict(file=row['file'], cab=row.get('cab', []), pathID=str(item[2]))
                   for row in index for item in row.get('names', [])
                   if item[0] == obj.type.name and item[1] == name and item[2] == obj.path_id]
        for match in matches:
            source_path = options.source / 'extracted/bundles' / Path(match['file']).name
            if not source_path.exists():
                source_path = Path(match['file'])
            if source_path not in source_environments:
                source_environments[source_path] = UnityPy.load(str(source_path))
            original = next((item for item in source_environments[source_path].objects
                             if item.path_id == obj.path_id and item.type.name == obj.type.name), None)
            if original is not None and original.get_raw_data() == obj.get_raw_data():
                return matches, True
        return matches, False
    for obj in environment.objects:
        if obj.type.name == 'AnimationClip':
            data = obj.read_typetree()
            settings = data.get('m_AnimationClipSettings', data.get('m_MuscleClip'))
            matches, equal = compare_source(obj, data['m_Name'])
            clips.append(dict(name=data['m_Name'], pathID=str(obj.path_id), length=settings['m_StopTime']-settings['m_StartTime'],
                              loop=settings['m_LoopTime'], sources=matches, rawPayloadMatchesSource=equal, events=data.get('m_Events', [])))
        elif obj.type.name == 'AnimatorController':
            data = obj.read_typetree()
            matches, equal = compare_source(obj, data['m_Name'])
            names = dict(data['m_TOS'])
            states = []
            for machine in data['m_Controller']['m_StateMachineArray']:
                for state in machine['data']['m_StateConstantArray']:
                    value = state['data']
                    states.append(dict(name=names.get(value['m_NameID']), motionIndices=value['m_BlendTreeConstantIndexArray'],
                                       transitions=[transition['data'] for transition in value['m_TransitionConstantArray']]))
            controllers.append(dict(name=data['m_Name'], sources=matches, rawPayloadMatchesSource=equal,
                                    clipBindings=data['m_AnimationClips'], states=states,
                                    playable=bool(data['m_AnimationClips']) and any(any(slot >= 0 for slot in state['motionIndices']) for state in states)))
    gun = next(row for row in guns if row.get(5, b'').decode('utf8') == character[:-2])
    source_actions = [row for row in interactions if str(row.get(2, '')).startswith(str(gun[1]))]
    contract = json.loads((folder / 'interaction-contract.json').read_text(encoding='utf8'))
    expected = {row[1]: row for row in source_actions}
    expected_contract = [dict(id=row[1], event=row.get(3, 0),
                              clip=row.get(9, b'').decode('utf8').replace('{0}', character),
                              weight=row.get(11, 0), horizontal=row.get(14, b'').decode('utf8'),
                              vertical=row.get(15, b'').decode('utf8'), zoom=row.get(16, b'').decode('utf8'))
                         for row in source_actions]
    contract_by_id = {action['id']: action for action in contract['actions']}
    action_errors = [action['id'] for action in expected_contract if contract_by_id.get(action['id']) != action]
    action_errors.extend(action['id'] for action in contract['actions'] if action['id'] not in expected)
    idle = next(clip for clip in clips if clip['name'] == 'c_'+character+'_BarrackIdle')
    report.append(dict(character=character, sha256=hashlib.sha256(payload.read_bytes()).hexdigest(),
                       controllers=controllers, clips=clips, defaultIdle=idle, interactionContractErrors=action_errors,
                       excludedClips=entry.get('excludedClips', []), sourceActions=source_actions,
                       sourceControllers=[dict(file=row['file'], name=name[1], pathID=str(name[2])) for row in index
                                          for name in row.get('names', []) if name[0]=='AnimatorController' and name[1]==character+'_Barrack']))
    print(character, 'idleLoop='+str(idle['loop']), 'controllers='+str(len(controllers)),
          'boundControllers='+str(sum(controller['playable'] for controller in controllers)), 'contractErrors='+str(action_errors), flush=True)
options.output.mkdir(parents=True, exist_ok=True)
(options.output / 'contracts.json').write_text(json.dumps(report, ensure_ascii=False, indent=2,
    default=lambda value: {'hex': value.hex()} if isinstance(value, bytes) else str(value)), encoding='utf8')
if any(not item['defaultIdle']['loop'] or item['interactionContractErrors'] or
       any(not asset['rawPayloadMatchesSource'] for asset in item['clips'] + item['controllers']) for item in report):
    raise SystemExit('Animation contract audit failed')
print('AUDITED', len(report), 'characters')
