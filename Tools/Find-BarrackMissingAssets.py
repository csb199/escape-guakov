"""Find exact-name native assets in previously unindexed logical segments in logical bundle segments omitted by the physical-file index.
Only matching bundles are exported; source game files are read-only. Incremental scan journal is resumable.
"""
import argparse,concurrent.futures,importlib.util,json,sys,time,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,'F:/gfresource/tools/python');import UnityPy
UnityPy.config.FALLBACK_UNITY_VERSION='2019.4.29f1'
spec=importlib.util.spec_from_file_location('segments',ROOT/'Tools/Index-GfResourceSegments.py');seg=importlib.util.module_from_spec(spec);spec.loader.exec_module(seg)
source=Path('E:/Games/GF2Exilium/GF2 Game/GF2_Exilium_Data/LocalCache/Data/AssetBundles_Windows')
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--kind',choices=['Mesh','GameObject'],required=True);parser.add_argument('--targets',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);options=parser.parse_args()
out=options.output;out.mkdir(parents=True,exist_ok=True);(out/'bundles').mkdir(exist_ok=True)
wanted=set(json.loads(options.targets.read_text(encoding='utf8')))
saved=out/'targets.json'
if saved.exists() and set(json.loads(saved.read_text(encoding='utf8')))!=wanted:raise ValueError('Search targets changed; use a separate output journal')
saved.write_text(json.dumps(sorted(wanted),indent=2),encoding='utf8')
rows=[json.loads(x)for x in (ROOT/'LocalOnly/Barrack/live/segment-index-final.jsonl').open(encoding='utf8')]
journal=out/'scan.jsonl';done=set();found=set()
if journal.exists():
 for line in journal.open(encoding='utf8'):
  try:
   r=json.loads(line);done.add(r['logical']);found.update(x['name']for x in r.get('matches',[]))
  except ValueError:pass
known={c.lower() for p in [ROOT/'LocalOnly/Barrack/next-batch-audit/supplement-index.jsonl',Path('F:/gfresource/bundle_index.jsonl')] for line in p.open(encoding='utf8') for c in json.loads(line).get('cab',[])}
rows=sorted([r for r in rows if not r.get('error') and r['logical']not in done and not any(c.lower() in known for c in r['cab'])],key=lambda r:r['length'])
def inspect(r):
 result=dict(logical=r['logical'],cab=r['cab'],matches=[])
 try:
  data=seg.segment_bytes(r,source);env=UnityPy.load(data)
  for o in env.objects:
   if o.type.name!=options.kind:continue
   name=o.peek_name()
   if name in wanted:result['matches'].append(dict(name=name,pathID=str(o.path_id),cab=o.assets_file.name.lower()))
  if result['matches']:
   target=out/'bundles'/(result['matches'][0]['cab']+'.bundle');target.write_bytes(data);result.update(file=str(target),sha256=hashlib.sha256(data).hexdigest())
 except Exception as e:result['error']=str(e)
 return result
with journal.open('a',encoding='utf8') as f,concurrent.futures.ThreadPoolExecutor(max_workers=3)as pool:
 # Bounded batches avoid retaining thousands of decompressed bundles or futures.
 for base in range(0,len(rows),96):
  for r in pool.map(inspect,rows[base:base+96]):
   f.write(json.dumps(r)+'\n');done.add(r['logical'])
   if r['matches']:found.update(x['name']for x in r['matches']);print(json.dumps(r),flush=True)
  f.flush();print(json.dumps(dict(scanned=len(done),remaining=max(0,len(rows)-base-96),found=sorted(found))),flush=True)
  if wanted<=found:break
(out/'status.json').write_text(json.dumps(dict(scanned=len(done),found=sorted(found),missing=sorted(wanted-found)),indent=2),encoding='utf8')
