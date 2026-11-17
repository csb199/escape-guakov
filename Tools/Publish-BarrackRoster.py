"""Publish explicitly staged native character assets and source-localized roster entries.
Run the Unity roster validation after publishing; this tool does not claim runtime acceptance.
"""
import importlib.util,json,re,shutil,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
CONTENT=ROOT/'Assets/Game/Content/BarrackSource'
OUT=ROOT/'LocalOnly/Barrack/remaining-import'
s=importlib.util.spec_from_file_location('interaction',ROOT/'Tools/Export-BarrackInteraction.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
tables=Path('E:/Games/GF2Exilium/GF2 Game/GF2_Exilium_Data/LocalCache/Data/Table')
langs=json.loads((ROOT/'LocalOnly/Barrack/live/ui-localization.json').read_text(encoding='utf8'))['langs']
names={r[5].decode('utf8'):langs[str(m.fields(r[2])[1][0])]['2'] for r in m.table(tables/'GunData.bytes') if 5 in r and 2 in r}
base=['GrozaSR01','DusevnyjSSR01','PhaetusaSSR01','FlorenceSSR01','ZhaohuiSSR01','ClukaySSR01','TololoSSR01','AndorisSSR01','PeriSSR01','FaelynnSSR01']
manifest=CONTENT/'roster.json'
entries=json.loads(manifest.read_text(encoding='utf8'))['characters'] if manifest.exists() else [dict(id=v,displayName=names.get(v[:-2],v)) for v in base]
for row in json.loads((OUT/'batch-status.json').read_text(encoding='utf8')):
 if row['status']!='native_imported_pending_runtime_validation':continue
 v=row['character'];target=CONTENT/v
 group=re.sub(r'(SSR|SR|R)\d+$','',v).lower()
 portraits=list((Path('F:/gfresource/extracted/characters')/group/'textures').glob('Avatar_Head_'+v[:-2]+'__*.png'))
 if not portraits:portraits=list((Path('F:/gfresource/extracted/characters')/group/'textures').glob('Avatar_Head_'+v+'__*.png'))
 # Some parsed sets expose only the native UP head portrait. Keep the exact-name lookup unique.
 if not portraits:portraits=list((Path('F:/gfresource/extracted/characters')/group/'textures').glob('Avatar_Head_'+v[:-2]+'UP__*.png'))
 if len(portraits)!=1:raise ValueError(('Missing or ambiguous portrait',v,portraits))
 if not target.exists():
  source=ROOT/'LocalOnly/Barrack/CharacterImportProject/Assets/Game/Content/BarrackSource'/v
  shutil.copytree(source,target);shutil.copy2(source.with_suffix('.meta'),target.with_suffix('.meta'))
 shutil.copy2(ROOT/'LocalOnly/Barrack/character-import'/v/'material-contract.json',target/'material-contract.json')
 shutil.copy2(portraits[0],target/'Portrait.png')
 overrides=CONTENT/'mesh-selection-overrides.json'
 evidence=[e for e in json.loads(overrides.read_text(encoding='utf8'))['selections'] if e['character']==v] if overrides.exists() else []
 if evidence:
  contract_path=target/'mesh-selection-contract.json'
  contract=json.loads(contract_path.read_text(encoding='utf8')) if contract_path.exists() else {}
  contract.update(character=v,resourceIdentity='unresolved',selections=evidence)
  contract_path.write_text(json.dumps(contract,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
 plan=json.loads((ROOT/'LocalOnly/Barrack/character-import'/v/'import-plan.json').read_text(encoding='utf8'))
 expected={t['name']:t['srgb'] for b in plan['materialBindings'] for t in b['textures']}
 items=[]
 for asset in sorted((target/'Assets').glob('*_Texture2D.asset')):
  with asset.open(encoding='utf8') as f:header=''.join(next(f) for _ in range(12))
  name=re.search(r'  m_Name: (.+)',header).group(1)
  items.append(dict(name=name,srgb=expected[name],asset=asset.relative_to(ROOT).as_posix(),format='native Texture2D metadata'))
 if not items:raise ValueError('No persisted texture inventory: '+v)
 (target/'texture-color-contract.json').write_text(json.dumps(dict(source='Source Texture2D m_ColorSpace and native raw payload',textures=items),indent=2),encoding='utf8')
 if v not in {e['id'] for e in entries}:entries.append(dict(id=v,displayName=names.get(v[:-2],v)))
 if evidence:
  entry=next(e for e in entries if e['id']==v)
  excluded=set(entry.get('excludedClips',[]))|{c for proof in evidence for c in proof.get('excludedClips',[])}
  if excluded:entry['excludedClips']=sorted(excluded)
manifest.write_text(json.dumps(dict(characters=entries),ensure_ascii=False,indent=2)+'\n',encoding='utf8')
print('Staged roster entries:',len(entries))
