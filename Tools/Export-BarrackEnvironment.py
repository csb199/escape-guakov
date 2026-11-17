"""Adapt the selected source stage profile to explicit Unity 6 renderer settings."""
import argparse,json,pathlib

def main():
 p=argparse.ArgumentParser();p.add_argument('--stage',type=pathlib.Path,required=True);p.add_argument('--contract',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args()
 stage=json.loads(a.stage.read_text(encoding='utf8'));contract=json.loads(a.contract.read_text(encoding='utf8'))['configurations'];profiles={x['name']:x['data'] for x in contract if x.get('role')=='default-training-platform-profile'}
 def get(component,key,default):
  c=profiles.get(component,{});v=c.get(key,{});return v.get('m_Value',default) if c.get('active',False) else default
 pipe=next(x['data'] for x in contract if x['name']=='GFRenderPipelineAsset_DX11_GL');rs=next(x['data'] for x in stage if x['type']=='RenderSettings');adjust=next(x['data'] for x in stage if 'charRimColor'in x['data']);by={x['pathId']:x for x in contract};renderer=next(x['data'] for x in contract if x['name']=='RendererData')
 features=[]
 for ptr in renderer['m_RendererFeatures']:
  x=by[ptr['m_PathID']]['data'];s=x.get('settings',{});f=s.get('filterSettings',{})
  if x.get('m_Active',1) and f.get('PassNames'):features.append(dict(name=x['m_Name'],eventOrder=s.get('Event',300),queue=f.get('RenderQueueType',0),layerMask=f.get('LayerMask',{}).get('m_Bits',4294967295),tags=f['PassNames']))
 features=captured_character_features(features)
 sh=rs['m_AmbientProbe'];ordered=[sh[f'sh[{i:2d}]'] for i in range(27)]
 data=dict(sourceScene='04Aimo_Training_Platform_GFPC',sourceProfile='5645129384652940861',shadowResolution=pipe['m_MainLightShadowmapResolution'],shadowDistance=pipe['m_ShadowDistance'],cascadeSplit=pipe['m_Cascade2Split'],shadowDepthBias=pipe['m_ShadowDepthBias'],shadowNormalBias=pipe['m_ShadowNormalBias'],renderScale=pipe['m_RenderScale'],cameraMask=-524489,ambientSH=ordered,ambientIntensity=rs['m_AmbientIntensity'],reflectionIntensity=rs['m_ReflectionIntensity'],rimColor=adjust['charRimColor'],lightmapShadow=adjust['lmShadow'],sfxIntensity=adjust['globalSfxIntensityScale'],bodyFaceAlpha=adjust['charBodyFaceAlphaScale'],features=features,
 bloomThreshold=get('BloomExt','threshold',0.9),bloomIntensity=get('BloomExt','intensity',0.2),contrast=get('ColorAdjustments','contrast',0),saturation=get('ColorAdjustments','saturation',0),exposure=get('ColorAdjustments','postExposure',0),colorFilter=get('ColorAdjustments','colorFilter',{'r':1,'g':1,'b':1,'a':1}),
 aoIntensity=get('GroundTruthAmbientOcclusion','intensity',0),aoRadius=get('GroundTruthAmbientOcclusion','radius',0.5),aoFalloff=get('GroundTruthAmbientOcclusion','falloffRange',0.4),vignetteIntensity=get('Vignette','intensity',0),vignetteSmoothness=get('Vignette','smoothness',0.12),vignetteColor=get('Vignette','color',{'r':0.2,'g':0.2,'b':0.2,'a':1}),
 filmSlope=get('Tonemapping','filmSlope',0.91),filmToe=get('Tonemapping','filmToe',0.47),filmShoulder=get('Tonemapping','filmShoulder',0.23),filmBlackClip=get('Tonemapping','filmBlackClip',0),filmWhiteClip=get('Tonemapping','filmWhiteClip',0.035),
 fogColor=get('ExponentialHeightFog','FogInScatteringColor',rs['m_FogColor']),fogDensity=get('ExponentialHeightFog','FogDensity',0),fogHeight=get('ExponentialHeightFog','FogHeight',10),fogFalloff=get('ExponentialHeightFog','FogHeightFalloff',2.47),fogOpacity=get('ExponentialHeightFog','FogMaxOpacity',0.066),fogStart=get('ExponentialHeightFog','FogStartDistance',3.5),linearFogColor=get('ExponentialHeightFog','LinearFogColor',rs['m_FogColor']),linearFogStart=get('ExponentialHeightFog','LinearFogStart',10),linearFogEnd=get('ExponentialHeightFog','LinearFogEnd',400),reflectionBlur=get('PlanarReflectionVolume','blurRadius',1.65),reflectionColor=get('PlanarReflectionVolume','reflectionColor',{'r':1,'g':1,'b':1,'a':0.85}))
 a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf8');print('Source environment exported',len(features),'render features',len(ordered),'SH coefficients')
def captured_character_features(features):
 result=[]
 forward=next(feature for feature in features if feature['name']=='CharForward')
 for feature in features:
  if feature['name']=='CharForward':
   result.append(dict(feature,queueStart=0,queueEnd=2450))
  elif feature['name']=='HairShadow':
   result.append(feature)
   result.append(dict(forward,name='CharEyeComposite',queueStart=2451,queueEnd=2500))
  elif feature['name']=='CharHairTrans':
   result.extend(dict(feature,name=name,tags=[tag]) for name,tag in [('CharFaceShadow','GFCharFaceShadow'),('CharFaceSign','GFCharFaceSign'),('CharHairTrans','GFCharHairTransE')])
  else:
   result.append(feature)
 return result

if __name__=='__main__':main()
