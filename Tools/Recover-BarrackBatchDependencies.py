"""Recover batch-export missing CABs using the existing exact logical-segment resolver."""
import json, subprocess, sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
missing=set()
for p in (root/'LocalOnly/Barrack/next-batch-export').glob('*/export-manifest.json'):
    missing.update(json.loads(p.read_text(encoding='utf8'))['missingTransitiveCabs'])
if missing:
    args=[sys.executable,str(root/'Tools/Resolve-GfResourceClosure.py'),
        '--index',str(root/'LocalOnly/Barrack/live/segment-index-final.jsonl'),
        '--bundle-root','E:/Games/GF2Exilium/GF2 Game/GF2_Exilium_Data/LocalCache/Data/AssetBundles_Windows',
        '--python-path','F:/gfresource/tools/python',
        '--output',str(root/'LocalOnly/Barrack/live/next-batch-closure')]
    for cab in sorted(missing):args.extend(['--root-cab',cab])
    subprocess.run(args,check=True)
else:
    print('No unresolved transitive CABs in staged manifests.')
