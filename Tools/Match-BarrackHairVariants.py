import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--audit', type=Path, required=True)
    parser.add_argument('--bundle', type=Path, required=True)
    parser.add_argument('--unitypy', type=Path)
    args = parser.parse_args()
    if args.unitypy:
        sys.path.insert(0, str(args.unitypy))
    import UnityPy
    from UnityPy.export.ShaderConverter import ShaderSubProgram
    from UnityPy.helpers import CompressionHelper
    from UnityPy.streams import EndianBinaryReader

    audit = json.loads((args.audit / 'passes.json').read_text(encoding='utf8'))
    wanted = {stage['sha256'] for event in audit['events'] for stage in event['stages']}
    matches = {digest: [] for digest in wanted}
    for asset in UnityPy.load(str(args.bundle)).objects:
        if asset.type.name != 'Shader':
            continue
        shader = asset.read()
        tree = asset.read_typetree()
        blob = bytes(shader.compressedBlob)
        offsets, lengths, sizes = shader.offsets, shader.compressedLengths, shader.decompressedLengths
        if isinstance(offsets[0], list):
            offsets, lengths, sizes = offsets[0], lengths[0], sizes[0]
        segments = [CompressionHelper.decompress_lz4(blob[offset:offset + length], size) for offset, length, size in zip(offsets, lengths, sizes)]
        programs = {}
        for index in range(struct.unpack_from('<I', segments[0], 0)[0]):
            position, size, segment = struct.unpack_from('<III', segments[0], 4 + index * 12)
            reader = EndianBinaryReader(segments[segment], endian='<')
            reader.Position = position
            program = ShaderSubProgram(reader)
            code = bytes(program.m_ProgramCode)
            start = code.find(b'DXBC')
            if start < 0:
                continue
            size = struct.unpack_from('<I', code, start + 24)[0]
            digest = hashlib.sha256(code[start:start + size]).hexdigest()
            if digest in wanted:
                programs[index] = (program, digest)
        for subshader in tree['m_ParsedForm']['m_SubShaders']:
            for shader_pass in subshader['m_Passes']:
                names = {value: key for key, value in shader_pass['m_NameIndices']}
                for stage_name in ('progVertex', 'progFragment'):
                    for subprogram in shader_pass.get(stage_name, {}).get('m_SubPrograms', []):
                        match = programs.get(subprogram['m_BlobIndex'])
                        if match is None:
                            continue
                        program, digest = match
                        bindings = {binding['m_NameIndex']: binding['m_Index'] for binding in subprogram.get('m_ConstantBufferBindings', [])}
                        fields = []
                        for buffer in subprogram.get('m_ConstantBuffers', []):
                            for parameter in buffer['m_VectorParams'] + buffer['m_MatrixParams']:
                                fields.append({'name': names[parameter['m_NameIndex']], 'slot': bindings[buffer['m_NameIndex']], 'offset': parameter['m_Index'], 'count': parameter.get('m_Dim', 4) * parameter.get('m_RowCount', 1) * max(1, parameter['m_ArraySize'])})
                        matches[digest].append({'shader': tree['m_ParsedForm']['m_Name'], 'pass': shader_pass['m_State']['m_Name'], 'stage': stage_name, 'keywords': program.m_Keywords, 'localKeywords': program.m_LocalKeywords, 'fields': fields})
    for event in audit['events']:
        for stage in event['stages']:
            variants = matches[stage['sha256']]
            stage['variants'] = variants
            for variant in variants:
                values = {}
                for field in variant['fields']:
                    name = field['name']
                    if not any(token in name.lower() for token in ('hair', 'face', 'outline', 'shadow', 'alpha', 'stencil')):
                        continue
                    path = args.audit / (str(event['event']) + '-' + stage['stage'] + '-' + str(field['slot']) + '.bin')
                    if not path.exists():
                        continue
                    data = path.read_bytes()
                    if field['offset'] + field['count'] * 4 <= len(data):
                        values[name] = struct.unpack_from('<' + 'f' * field['count'], data, field['offset'])
                variant['values'] = values
            print(event['event'], stage['stage'], 'matching variants:', len(variants))
    (args.audit / 'matched.json').write_text(json.dumps(audit, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
