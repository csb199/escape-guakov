"""Export next-batch native evidence to LocalOnly; never promote unresolved bindings to the roster.
Run Resolve-BarrackMeshBindings.py first. Source bundles and all references remain unmodified.
"""
import hashlib, importlib.util, json, shutil, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('audit', Path(__file__).with_name('Audit-BarrackCharacters.py'))
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)
a = audit.Audit(Path('F:/gfresource'), ROOT, ROOT/'LocalOnly/Barrack/next-batch-audit')
a.scan()
from UnityPy.helpers.MeshHelper import MeshHandler
OUT = ROOT/'LocalOnly/Barrack/next-batch-export'
REPORT = ROOT/'TestResults/barrack-next-batch-export-2026-11-12'

def sha(path):
    with path.open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()

def keys(addr):
    obj = a.reader(addr)
    return list(dict.fromkeys(k for o in obj.assets_file.objects.values() if o.type.name == 'AssetBundle'
                             for k, v in o.read().m_Container if v.asset.path_id == addr[1]))

records = {audit.cab(c): r for r in a.records for c in r.get('cab', [])}
summary = []
for variant in sys.argv[1:] or ['FlorenceSSR01', 'ZhaohuiSSR01', 'ClukaySSR01']:
    d = json.loads((a.out/'variants'/f'{variant}.json').read_text(encoding='utf8'))
    resolution = json.loads((ROOT/'LocalOnly/Barrack/character-import'/variant/'mesh-resolution.json').read_text(encoding='utf8'))
    dest = OUT/variant
    dest.mkdir(parents=True, exist_ok=True)
    used = {d['prefabs'][0]['cab']}
    if resolution['avatar']:
        used.add(resolution['avatar'][0])
    textures, materials, mesh_evidence = {}, [], []
    for mat in d['materials']:
        addr = (mat['cab'], int(mat['pathID']))
        tree = a.tree(addr)
        used.update([addr[0], mat['shaderCab']])
        item = dict(mat, assetKeys=keys(addr), savedProperties=tree['m_SavedProperties'])
        materials.append(item)
        for tx in mat['textures']:
            ta = (tx['cab'], int(tx['pathID']))
            token = f'{ta[0]}_{ta[1]}'
            used.add(ta[0])
            if token in textures:
                continue
            tex = a.reader(ta).read()
            raw = tex.get_image_data()
            if not raw:
                raise ValueError(('Empty texture', variant, token))
            rel = f'textures/{token}.bin'
            path = dest/rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
            textures[token] = dict(tx, assetKeys=keys(ta), rawData=rel, bytes=len(raw), sha256=sha(path),
                                   srgb=tex.m_ColorSpace == 1, format=int(tex.m_TextureFormat), mipCount=tex.m_MipCount)
    for item in resolution['bindings'] + resolution['gaps']:
        for c in item['candidates']:
            addr = (c['meshCab'], int(c['meshPathID']))
            used.add(addr[0])
            mesh = a.reader(addr).read()
            handler = MeshHandler(mesh)
            handler.process()
            indices, weights = handler.m_BoneIndices, handler.m_BoneWeights
            weights_present = indices is not None and weights is not None
            indices, weights = indices or [], weights or []
            outliers = []
            for bone in c['bindposeOutliers']:
                influenced = [sum(w for i, w in zip(bi, bw) if i == bone['index']) for bi, bw in zip(indices, weights)]
                outliers.append(dict(bone, influencedVertices=sum(w > 1e-6 for w in influenced) if weights_present else None, maximumWeight=max(influenced, default=0) if weights_present else None))
            # Compare geometry, weights and indices across ambiguous candidates without changing source data.
            geom = dict(vertices=handler.m_Vertices, indices=handler.m_IndexBuffer,
                        boneIndices=indices, boneWeights=weights, uv=handler.m_UV0)
            digest = hashlib.sha256(json.dumps(geom, separators=(',', ':')).encode()).hexdigest()
            invalid = sum(1 for bi, bw in zip(indices, weights) if any(w > 1e-6 and (i < 0 or i >= len(mesh.m_BindPose)) for i, w in zip(bi, bw)))
            mesh_evidence.append(dict(renderer=item['renderer'], cab=addr[0], pathID=str(addr[1]),
                vertices=handler.m_VertexCount, geometrySkinHash=digest, skinWeightsDecoded=weights_present, invalidWeightedBoneVertices=invalid if weights_present else None,
                maxWeightSumError=max((abs(sum(w)-1) for w in weights), default=0) if weights_present else None, bindposeOutliers=outliers))
    clips = []
    idle = [c for c in d['clips'] if c['name'] == f'c_{variant}_BarrackIdle' and c['available']]
    if len(idle) != 1 or len(idle[0]['available']) != 1:
        raise ValueError(('Ambiguous main animation library', variant, idle))
    main_cab = idle[0]['available'][0]['cab']
    for clip in d['clips']:
        for address in clip['available']:
            used.add(address['cab'])
            name = clip['name']
            role = 'auxiliary-object' if address['cab'] != main_cab else ('camera' if name.endswith('_Camera') else 'character')
            clips.append(dict(name=name, role=role, roleEvidence='library and clip-name convention; binding target validation pending', **address))
    # Preserve available dependency closure; missing external branches are reported, not called irrelevant.
    seen, missing = set(), set()
    def visit(c):
        if c in seen:
            return
        seen.add(c)
        if c not in a.paths:
            missing.add(c)
            return
        for dep in records.get(c, {}).get('deps', []):
            dc = audit.cab(dep)
            if dc.startswith('cab-'):
                visit(dc)
    for c in sorted(used):
        visit(c)
    bundles = []
    for c in sorted(seen - missing):
        src = a.paths[c]
        rel = f'bundles/{c}.bundle'
        target = dest/rel
        target.parent.mkdir(parents=True, exist_ok=True)
        digest = sha(src)
        if not target.exists() or sha(target) != digest:
            shutil.copy2(src, target)
        if sha(target) != digest:
            raise ValueError(('Bundle copy checksum', c))
        bundles.append(dict(cab=c, source=str(src), file=rel, bytes=target.stat().st_size, sha256=digest,
                            selected=c in used))
    manifest = dict(variant=variant, created=time.strftime('%Y-%m-%dT%H:%M:%S%z'),
        readyForRoster=False, status='staged-source-export; unresolved mesh mapping and Unity validation',
        sourcePrefab=d['prefabs'][0], avatar=resolution['avatar'], mainAnimationCab=main_cab,
        bundles=bundles, textures=list(textures.values()), materials=materials, clips=clips,
        meshResolution=resolution, meshGeometryEvidence=mesh_evidence,
        missingTransitiveCabs=sorted(missing), directAuditMissing=d['missing'],
        validation=dict(copiedBundleHashes=True, nonemptyRawTextures=True, skinWeightsDecoded=all(x['skinWeightsDecoded'] for x in mesh_evidence),
                        unityImport=False, renderedComparison=False))
    audit.dump(dest/'export-manifest.json', manifest)
    audit.dump(REPORT/f'{variant}-evidence.json', manifest)
    row = dict(variant=variant, bundles=len(bundles), textures=len(textures), materials=len(materials),
        structuralMeshMatches=len(resolution['bindings']), meshGaps=len(resolution['gaps']),
        characterClips=sum(c['role']=='character' for c in clips), cameraClips=sum(c['role']=='camera' for c in clips),
        auxiliaryClips=[c['name'] for c in clips if c['role']=='auxiliary-object'],
        missingTransitiveCabs=len(missing), manifest=str(dest/'export-manifest.json'), readyForRoster=False)
    summary.append(row)
    print(json.dumps(row), flush=True)
audit.dump(REPORT/'export-summary.json', summary)

# Publish actionable gaps separately from the source-export manifest.
gaps = []
for row in summary:
    manifest = json.loads(Path(row['manifest']).read_text(encoding='utf8'))
    v = row['variant']
    gaps.append(dict(variant=v, category='resource-mapping', status='needs-evidence',
        requirement='MeshResList source path to CAB/PathID/container-key mapping; structural compatibility alone is not original address proof'))
    for mesh in manifest['meshResolution']['gaps']:
        gaps.append(dict(variant=v, category='mesh', renderer=mesh['renderer'], sourcePath=mesh['declaredPath'],
            reason='no indexed exact-name candidate' if not mesh['candidates'] else 'bind-pose mismatch or multiple structurally compatible candidates',
            candidates=mesh['candidates']))
    undecoded = [x for x in manifest['meshGeometryEvidence'] if not x['skinWeightsDecoded']]
    if undecoded:
        gaps.append(dict(variant=v, category='skin-weight-verification',
            requirement='Native Unity mesh inspection required; Python absent weight channels do not prove zero weights',
            meshes=[dict(renderer=x['renderer'], cab=x['cab'], pathID=x['pathID']) for x in undecoded]))
    gaps.append(dict(variant=v, category='integration', requirement='Unity 6 native import, deformation/actions, cancellation/resource ownership and roster switching verification pending'))
    gaps.append(dict(variant=v, category='render-capture', shaders=sorted({x['shader'] for x in manifest['materials']}),
        requirement='Source-game same-camera UI on/off and quality settings; inspect transparency, outlines, shadows/reflections and temporal motion'))
    gaps.append(dict(variant=v, category='auxiliary-animation', clips=row['auxiliaryClips'],
        requirement='Resolve prop prefab, attachment, animation binding, event timing, source camera and cleanup lifecycle'))
audit.dump(REPORT/'gap-list.json', gaps)
