"""Export the original default barrack character probe positions, SH and interior cells.
The source is _ArtNode.lightProbes in the parsed scene, not an inferred lighting preset.
"""
import importlib.util,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
s=importlib.util.spec_from_file_location('audit',ROOT/'Tools/Audit-BarrackCharacters.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
a=m.Audit(Path('F:/gfresource'),ROOT,ROOT/'LocalOnly/Barrack/next-batch-audit');a.scan()
scene=json.loads((ROOT/'LocalOnly/Barrack/live/stage-rendering.json').read_text(encoding='utf8'))
owner=next(x for x in scene if x.get('name')=='_ArtNode' and 'lightProbes' in x.get('data',{}))
ref=owner['data']['lightProbes'];cab=m.cab(owner['externals'][ref['m_FileID']-1]);pid=int(ref['m_PathID']);p=a.tree((cab,pid))
contract=dict(sourceCab=cab,sourcePathId=str(pid),positions=p['m_Data']['m_Positions'],probes=[dict(coefficients=[v[f'sh[{i:2}]'] for i in range(27)]) for v in p['m_BakedCoefficients']],cells=[dict(indices=[t[f'indices[{i}]'] for i in range(4)]) for t in p['m_Data']['m_Tetrahedralization']['m_Tetrahedra'] if all(t[f'indices[{i}]']>=0 for i in range(4))])
output=ROOT/'Assets/Game/Content/BarrackSource/Stage/character-light-probes.json'
output.write_text(json.dumps(contract,indent=2),encoding='utf8');print(output,len(contract['probes']),'probes',len(contract['cells']),'interior cells')
