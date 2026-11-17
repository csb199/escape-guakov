"""Quantify Barrack sequence data. Requires numpy; never treats data collection as visual acceptance."""
import argparse,csv,json,math,hashlib,datetime
from pathlib import Path
import numpy as np

parser=argparse.ArgumentParser()
parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
args=parser.parse_args();root=args.root;data=root/'LocalOnly/Barrack/continuous';captures=root/'LocalOnly/Barrack/Captures'
def csvrows(name):
    with (data/name).open(encoding='utf-8-sig') as f:return list(csv.DictReader(f))
def stats(values):
    x=np.array(list(values),dtype=float);x=x[np.isfinite(x)]
    return {'count':len(x),'median':float(np.median(x)),'p95':float(np.quantile(x,.95)),'max':float(np.max(x))} if len(x) else {'count':0}
def matrix(text):return np.array([float(x)for x in text.split(';')]).reshape(4,4).T
def project(points,m):
    q=points@m.T
    with np.errstate(divide='ignore',invalid='ignore'):return q[:,:2]/q[:,3,None]*.5+.5,q[:,3]
frames=csvrows('frames.csv');vertices=csvrows('vertices.csv');buffers=csvrows('skinning-buffers.csv')
if not(data/'status.txt').read_text().startswith('COLLECTED'):raise RuntimeError('Sequence not completed')
ids=np.array([int(r['unityFrame'])for r in frames]);current=set(int(r['sample'])for r in frames)
result={'collectedAt':datetime.datetime.fromtimestamp((data/'frames.csv').stat().st_mtime).isoformat(),'frames':len(frames),'consecutiveUnityFrames':bool(np.all(np.diff(ids)==1)), 'internalSize':[896,504], 'displaySize':[640,360],'simulationHz':60,'nonFinite':sum(int(r['nonFinite'])for r in frames),'scenarios':{},'skinning':{},'coverageNotes':[]}
for scenario in dict.fromkeys(r['scenario']for r in frames):
    rows=[r for r in frames if r['scenario']==scenario];samples={int(r['sample'])for r in rows if r['historyUsed']=='True'}
    visible=[r for r in vertices if int(r['sample'])in samples]
    result['scenarios'][scenario]={'frames':len(rows),'historyUsed':len(samples),'visibleVertexMotionErrorPx':stats(float(r['errorPx'])for r in visible),'colorMAE':stats(float(r['colorMAE'])for r in rows),'headRegionMAE':stats(float(r['hairMAE'])for r in rows),'trailCoefficient':stats(float(r['trailCoefficient'])for r in rows if int(r['changedPixels'])>0),'disoccludedMAE':stats(float(r['disoccludedMAE'])for r in rows if int(r['disoccludedPixels'])>0)}
for renderer in dict.fromkeys(r['renderer']for r in buffers):
    rows=[r for r in buffers if r['renderer']==renderer and int(r['sample'])>128]
    visible=[r for r in vertices if r['renderer']==renderer and 128<int(r['sample'])<224]
    result['skinning'][renderer]={'frames':len(rows),'sampledVerticesPerFrame':stats(int(r['samples'])for r in rows),'previousBufferFrameP95Px':stats(float(r['previousPositionP95Px'])for r in rows),'currentBufferFrameP95World':stats(float(r['currentPositionP95World'])for r in rows),'rasterMotionErrorPx':stats(float(r['errorPx'])for r in visible)}
    if not visible:result['coverageNotes'].append(renderer+': no depth-matched visible raster samples; buffer continuity tested independently.')
# Default-view comparison uses actual captured matrices, not material names or screenshot similarity.
points=np.array([[x,y,z,1]for x in np.linspace(-2,2,9)for z in np.linspace(-1,4,9)for y in [0,1,2]])
originalReflection=np.frombuffer((captures/'gpu-contract/727-ShaderStage.Vertex-0.bin').read_bytes(),'<f4',count=16,offset=21520).reshape(4,4).T
hostReflection=matrix(frames[0]['reflectionMatrix']);hostReflection[0]*=-1 # native receiver's horizontal orientation
u,uw=project(points,originalReflection);v,vw=project(points,hostReflection)
valid=(uw>.1)&(vw>.1)&(np.max(abs(u-.5),axis=1)<.5)&(np.max(abs(v-.5),axis=1)<.5)
reflectionError=np.linalg.norm(u[valid]-v[valid],axis=1)*2048
result['defaultReflection']={'gridWorldX':[-2,2],'gridWorldZ':[-1,4],'worldY':[0,1,2],'visibleSamples':int(valid.sum()),'errorTexels':stats(reflectionError),'thresholdTexels':1,'accepted':bool(np.quantile(reflectionError,.95)<=1)}
originalShadow=np.frombuffer((captures/'temporal-audit/2102-0.bin').read_bytes(),'<f4',count=16,offset=22432).reshape(4,4).T
hostShadow=matrix(frames[0]['shadowMatrix']);a=np.linalg.norm(originalShadow[0,:3])*4096;b=np.linalg.norm(hostShadow[0,:3])*4096
result['defaultShadow']={'originalTexelsPerWorldUnit':float(a),'hostTexelsPerWorldUnit':float(b),'relativeDensityError':float(abs(b/a-1)),'thresholdRelativeError':.05,'accepted':bool(abs(b/a-1)<=.05),'rasterDepthBias':0,'rasterSlopeBias':0,'note':'Texel density/coverage differs; this is not a measured screen-space shadow displacement.'}
# For each captured viewpoint, assert finite projection and floor-plane registration independently of source coverage.
floor=np.array([[x,.0015,z,1]for x in np.linspace(-5,5,11)for z in np.linspace(-5,5,11)])
registration=[];shadowStability=[]
for row in frames:
    a=matrix(row['cameraMatrix']);j=np.array([float(row['jitterX']),float(row['jitterY'])]);uv,w=project(floor,a);uv+=j*np.array([1,-1])/np.array([896,504])
    v,vw=project(floor,matrix(row['reflectionMatrix']));mask=(w>.1)&(vw>.1)&(np.max(abs(uv-.5),axis=1)<.5)&(np.max(abs(v-.5),axis=1)<.5)
    if mask.any():registration.extend((np.linalg.norm(uv[mask]-v[mask],axis=1)*2048).tolist())
for i in range(98,128):shadowStability.append(float(np.max(abs(matrix(frames[i]['shadowMatrix'])-matrix(frames[i-1]['shadowMatrix'])))))
result['hostSweep']={'yaw':[-360,360],'pitch':[-12,18.5],'distance':[1,10],'yawStep':2,'frames':1444,'floorRegistrationTexels':stats(registration),'staticShadowMatrixDelta':stats(shadowStability),'note':'Discrete boundary sweep, not a proof of every continuous parameter combination.'}
source=json.loads((captures/'source-sequence-metrics.json').read_text());result['sourceCapture']={'frames':len(source),'groups':{},'cameraMotionObserved':False}
for group in dict.fromkeys(x['group']for x in source):
    rows=[x for x in source if x['group']==group];camera=np.array([x['cameraUnjittered']for x in rows]);delta=float(np.max(abs(camera-camera[0])))
    result['sourceCapture']['groups'][group]={'frames':len(rows),'first':rows[0]['frame'],'last':rows[-1]['frame'],'maxUnjitteredMatrixChange':delta,'hairPreviousVertexP95':stats(r['parts']['hair']['previousPositionErrorPx']['p95']for r in rows if 'previousPositionErrorPx'in r['parts']['hair'])}
    result['sourceCapture']['cameraMotionObserved']|=delta>1e-4
result['acceptance']={'finiteBuffers':result['nonFinite']==0,'consecutiveFrames':result['consecutiveUnityFrames'],'skinningContinuity':all(x['previousBufferFrameP95Px']['max']<.01 for x in result['skinning'].values()),'defaultReflection':result['defaultReflection']['accepted'],'defaultShadowDensity':result['defaultShadow']['accepted'],'sourceRapidCameraComparison':'BLOCKED: no source camera movement in captured sequences','dynamicGhostingEquivalence':'NOT_PROVEN: reference is current frame without TAA; motion/pose/resolution is not matched to source','overall':'NOT_ACCEPTED'}
result['methodology']={'motion':'Current and previous BakeMesh vertices projected with captured jittered matrices, jitter removed; visible samples require depth residual <= .00015. Readback uses bottom-up pixels; D3D motion is top-down. Raster boundaries/coverage introduce sample error.','skinning':'Raw current and previous GPU skin buffers transformed by current and previous rootBone matrices, compared to current/previous CPU BakeMesh positions. Uses normal camera frames, not render-request-only frames.','trail':'Projection of (TAA-current unjittered reference) onto (previous reference-current reference), evaluated on changed pixels. This is a residual-alignment diagnostic, NOT a literal ghost percentage or source-equivalence score.','disocclusion':'Previous raw depth-current raw depth > .002. Jitter and camera movement affect this screen-space proxy; it is not an independently reconstructed visibility mask.'}
files=['Assets/Game/Editor/Barrack/BarrackSequenceValidation.cs','Assets/Game/HotUpdate/Barrack/BarrackNativeTemporal.cs','Assets/Game/HotUpdate/Barrack/BarrackRenderPipelineAsset.cs','Assets/Game/HotUpdate/Barrack/BarrackPreview.cs']
result['sha256']={x:hashlib.sha256((root/x).read_bytes()).hexdigest()for x in files}
(data/'metrics.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({'frames':result['frames'],'acceptance':result['acceptance'],'reflection':result['defaultReflection'],'shadow':result['defaultShadow'],'floor':result['hostSweep']['floorRegistrationTexels']},ensure_ascii=False,indent=2))
