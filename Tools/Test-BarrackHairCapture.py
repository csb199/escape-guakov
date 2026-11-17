import argparse
import hashlib
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--host', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    source = json.loads(args.source.read_text(encoding='utf8'))
    host = json.loads(args.host.read_text(encoding='utf8'))
    results = []

    def check(condition, name):
        results.append({'check': name, 'passed': bool(condition)})

    def pixel_hash(event):
        return next(stage['sha256'] for stage in event['stages'] if stage['stage'] == 'ShaderStage.Pixel')

    sequence = []
    for source_id in (2324, 2349, 2370, 2407, 2430):
        original = next(event for event in source['events'] if event['event'] == source_id)
        candidates = [event for event in host['events'] if pixel_hash(event) == pixel_hash(original)]
        check(len(candidates) == 1, str(source_id) + ': unique matching main-camera pixel bytecode')
        if len(candidates) != 1:
            continue
        actual = candidates[0]
        sequence.append(actual['event'])
        for stage in original['stages']:
            check(any(candidate['stage'] == stage['stage'] and candidate['sha256'] == stage['sha256'] for candidate in actual['stages']), str(source_id) + ': ' + stage['stage'] + ' bytecode')
        for field in ('depthEnable', 'depthFunction', 'depthWrites', 'stencilEnable', 'frontFace', 'backFace'):
            check(actual['outputMerger']['depthStencilState'][field] == original['outputMerger']['depthStencilState'][field], str(source_id) + ': ' + field)
        for field in ('alphaToCoverage', 'independentBlend', 'sampleMask', 'blends'):
            check(actual['outputMerger']['blendState'][field] == original['outputMerger']['blendState'][field], str(source_id) + ': ' + field)
        for field in ('cullMode', 'depthBias', 'slopeScaledDepthBias', 'frontCCW'):
            check(actual['rasterizer'].get(field) == original['rasterizer'].get(field), str(source_id) + ': ' + field)
        check(actual['outputMerger']['depthTarget']['format'] == original['outputMerger']['depthTarget']['format'], str(source_id) + ': depth/stencil format')
    check(len(sequence) == 5 and sequence == sorted(sequence), 'hair stencil, eye add, eye multiply, face shadow, fringe composite order')
    hair = [event['event'] for event in host['events'] if event['outputMerger']['depthStencilState']['frontFace']['reference'] == 203 and event['outputMerger']['depthStencilState']['frontFace']['passOperation'] == 2]
    fringe = [event['event'] for event in host['events'] if event['outputMerger']['depthStencilState']['frontFace']['reference'] == 204 and event['outputMerger']['depthStencilState']['frontFace']['writeMask'] == 248 and event['outputMerger']['depthStencilState']['frontFace']['passOperation'] == 2]
    check(len(hair) == 1 and len(fringe) == 1 and hair[0] < fringe[0] < sequence[0], 'hair forward precedes fringe forward and stencil projection')
    report = {'passed': all(result['passed'] for result in results), 'source': str(args.source), 'host': str(args.host), 'sourceSha256': hashlib.sha256(args.source.read_bytes()).hexdigest(), 'hostSha256': hashlib.sha256(args.host.read_bytes()).hexdigest(), 'checks': results}
    args.output.write_text(json.dumps(report, indent=2), encoding='utf8')
    print('PASS' if report['passed'] else 'FAIL', len(results), 'checks')
    for result in results:
        if not result['passed']:
            print(result['check'])
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
