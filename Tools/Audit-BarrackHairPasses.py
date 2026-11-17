import hashlib
import json
import os
from pathlib import Path
import traceback

import renderdoc as rd


def state_value(value, depth=0):
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    if depth > 9:
        return str(value)
    if hasattr(value, '__iter__') or (hasattr(value, '__len__') and hasattr(value, '__getitem__')):
        return [state_value(item, depth + 1) for item in value]
    fields = {}
    for name in dir(value):
        if name.startswith('_') or name in ('this', 'thisown'):
            continue
        item = getattr(value, name)
        if not callable(item):
            fields[name] = state_value(item, depth + 1)
    return fields or str(value)


root = Path(os.environ.get('BARRACK_PROJECT', 'F:/escape-guakov'))
output = Path(os.environ.get('BARRACK_HAIR_OUTPUT', str(root / 'LocalOnly/Barrack/hair-source')))
output.mkdir(parents=True, exist_ok=True)
capture_path = Path(os.environ.get('BARRACK_HAIR_CAPTURE', str(root / 'LocalOnly/Barrack/Captures/barrack-direct-valid.rdc')))
event_selection = os.environ.get('BARRACK_HAIR_EVENTS', '2302,2313,2324,2349,2370,2407,2430,2524')
events = [] if event_selection == 'auto' else [int(value) for value in event_selection.split(',')]
report = {'capture': str(capture_path), 'events': []}
capture = None
controller = None
try:
    capture = rd.OpenCaptureFile()
    status = capture.OpenFile(str(capture_path), '', None)
    if status != rd.ResultCode.Succeeded:
        raise RuntimeError(str(status))
    status, controller = capture.OpenCapture(rd.ReplayOptions(), None)
    if status != rd.ResultCode.Succeeded:
        raise RuntimeError(str(status))
    names = {str(resource.resourceId): resource.name for resource in controller.GetResources()}
    def walk(actions, parents=()):
        for action in actions:
            yield action, parents
            yield from walk(action.children, parents + (action.customName,))
    actions = list(walk(controller.GetRootActions()))
    report['allActions'] = [{'event': action.eventId, 'name': action.customName, 'flags': str(action.flags), 'parents': parents} for action, parents in actions]
    if event_selection == 'auto':
        source = json.loads((root / 'LocalOnly/Barrack/hair-source/passes.json').read_text(encoding='utf8'))
        hashes = {stage['sha256'] for event in source['events'] for stage in event['stages'] if stage['stage'] == 'ShaderStage.Pixel'}
        report['drawShaders'] = []
        for action, parents in actions:
            if not action.flags & rd.ActionFlags.Drawcall:
                continue
            controller.SetFrameEvent(action.eventId, False)
            reflection = controller.GetPipelineState().GetShaderReflection(rd.ShaderStage.Pixel)
            report['drawShaders'].append({'event': action.eventId, 'shader': names.get(str(controller.GetPipelineState().GetShader(rd.ShaderStage.Pixel))), 'hash': hashlib.sha256(bytes(reflection.rawBytes)).hexdigest() if reflection else None})
            if reflection and hashlib.sha256(bytes(reflection.rawBytes)).hexdigest() in hashes:
                events.append(action.eventId)
    if not events:
        raise RuntimeError('No matching source character draws')
    report['actions'] = [{'event': action.eventId, 'name': action.customName, 'parents': parents} for action, parents in actions if min(events) <= action.eventId <= max(events)]
    textures = {texture.resourceId: texture for texture in controller.GetTextures()}
    for event in events:
        controller.SetFrameEvent(event, True)
        pipeline = controller.GetPipelineState()
        direct = controller.GetD3D11PipelineState()
        record = {'event': event, 'rasterizer': state_value(direct.rasterizer.state), 'outputMerger': state_value(direct.outputMerger), 'stages': []}
        record['targets'] = [{'name': names.get(str(target.resource)), 'format': target.format.Name(), 'width': textures[target.resource].width, 'height': textures[target.resource].height, 'samples': textures[target.resource].msSamp} for target in list(direct.outputMerger.renderTargets) + [direct.outputMerger.depthTarget] if target.resource in textures]
        report['events'].append(record)
        for stage in (rd.ShaderStage.Vertex, rd.ShaderStage.Pixel):
            reflection = pipeline.GetShaderReflection(stage)
            if reflection is None:
                continue
            code = bytes(reflection.rawBytes)
            stage_record = {'stage': str(stage), 'shader': names.get(str(pipeline.GetShader(stage))), 'sha256': hashlib.sha256(code).hexdigest(), 'buffers': []}
            record['stages'].append(stage_record)
            (output / (str(event) + '-' + str(stage) + '.dxbc')).write_bytes(code)
            (output / (str(event) + '-' + str(stage) + '.asm')).write_text(controller.DisassembleShader(rd.ResourceId.Null(), reflection, ''), encoding='utf8')
            for index, block in enumerate(reflection.constantBlocks):
                descriptor = pipeline.GetConstantBlock(stage, index, 0).descriptor
                data = bytes(controller.GetBufferData(descriptor.resource, descriptor.byteOffset, descriptor.byteSize))
                filename = str(event) + '-' + str(stage) + '-' + str(index) + '.bin'
                (output / filename).write_bytes(data)
                stage_record['buffers'].append({'slot': index, 'name': block.name, 'path': filename, 'size': len(data)})
        for index, target in enumerate(direct.outputMerger.renderTargets):
            if os.environ.get('BARRACK_HAIR_SAVE_IMAGES', '1') != '1':
                break
            if target.resource == rd.ResourceId.Null():
                continue
            save = rd.TextureSave()
            save.resourceId = target.resource
            save.destType = rd.FileType.PNG
            controller.SaveTexture(save, str(output / (str(event) + '-target-' + str(index) + '.png')))
except Exception:
    report['error'] = traceback.format_exc()
finally:
    if controller:
        controller.Shutdown()
    if capture:
        capture.Shutdown()
    (output / 'passes.json').write_text(json.dumps(report, indent=2), encoding='utf8')
raise SystemExit(1 if 'error' in report else 0)
