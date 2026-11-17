"""Stage remaining default outfits with per-character failures; never publish failed imports."""
import json,subprocess,sys,os,shutil,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'LocalOnly/Barrack/remaining-import'
UNITY=Path('E:/Unity/6000.3.27f2/Editor/Unity.exe')
PROJECT=ROOT/'LocalOnly/Barrack/CharacterImportProject'
def run(args,log,env=None):
 with log.open('w',encoding='utf8') as f:
  return subprocess.run(args,cwd=ROOT,stdout=f,stderr=subprocess.STDOUT,env=env).returncode

def main():
 OUT.mkdir(parents=True,exist_ok=True)
 shutil.copy2(ROOT/'Assets/Game/Editor/Barrack/GrozaSourceImporter.cs',PROJECT/'Assets/Editor/GrozaSourceImporter.cs')
 results=[]
 for v in json.loads((OUT/'candidates.json').read_text(encoding='utf8')):
  dest=OUT/v;dest.mkdir(exist_ok=True);row=dict(character=v,status='pending');results.append(row)
  try:
   audit=json.loads((ROOT/'LocalOnly/Barrack/next-batch-audit/variants'/f'{v}.json').read_text(encoding='utf8'))
   if len(audit['renderers'])<2: raise ValueError('Only partial model hierarchy recovered (fewer than two character renderers)')
   for tool in ['Select-BarrackCompatibleMeshes.py','Prepare-BarrackCharacterImport.py']:
    log=dest/(tool+'.log')
    if run([sys.executable,str(ROOT/'Tools'/tool),v],log):raise ValueError(tool+': '+log.read_text(encoding='utf8',errors='replace')[-1400:])
   report=PROJECT/'Assets/Game/Content/BarrackSource'/v/'import-report.json'
   if not report.exists():
    env=dict(os.environ,GF2_IMPORT_PLAN=str(ROOT/'LocalOnly/Barrack/character-import'/v/'import-plan.json'))
    logfile=dest/'unity-import.log'
    code=run([str(UNITY),'-batchmode','-quit','-force-d3d11','-projectPath',str(PROJECT),'-executeMethod','Game.Editor.Barrack.GrozaSourceImporter.Import','-logFile',str(logfile)],dest/'unity-stdout.log',env)
    if code or not report.exists():raise ValueError('Native Unity import failed: '+logfile.read_text(encoding='utf8',errors='replace')[-6000:])
   row['status']='native_imported_pending_runtime_validation'
   row['report']=json.loads(report.read_text(encoding='utf8'))
  except Exception as e: row['status']='blocked';row['reason']=str(e)
  (OUT/'batch-status.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf8')
  print(v,row['status'],flush=True)
if __name__=='__main__':main()
