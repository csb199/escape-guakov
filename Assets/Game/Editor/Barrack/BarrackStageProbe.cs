using System;
using System.IO;
using System.Linq;
using System.Text;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Game.Editor.Barrack
{
    [InitializeOnLoad]
    public static class BarrackStageProbe
    {
        const string Key="Barrack.StageProbe";
        static BarrackStageProbe(){EditorApplication.update+=Tick;}
        public static void Run(){EditorSceneManager.OpenScene(BarrackStartup.ScenePath); SessionState.SetBool(Key,true);SessionState.SetString(Key+".deadline",DateTime.UtcNow.AddMinutes(4).ToString("O"));EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;}
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
            try{
                var host=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();
                if(host!=null&&host.Failure!=null)throw new Exception(host.Failure);
                if(DateTime.UtcNow>DateTime.Parse(SessionState.GetString(Key+".deadline","")))throw new TimeoutException();
                if(host==null||!host.Ready)return;
                var scene=SceneManager.GetActiveScene();var roots=scene.GetRootGameObjects();var renderers=roots.SelectMany(x=>x.GetComponentsInChildren<Renderer>(true)).ToArray();
                var text=new StringBuilder("SCENE "+scene.path+"\nRENDERERS "+renderers.Length+"\n");
                foreach(var root in roots)text.AppendLine("ROOT "+root.name+" "+root.transform.position);
                foreach(var light in roots.SelectMany(x=>x.GetComponentsInChildren<Light>(true)).Where(l=>l.enabled&&l.gameObject.activeInHierarchy&&l.type==LightType.Directional))text.AppendLine("SUN "+light.name+" "+light.transform.forward+" "+light.intensity);
                text.AppendLine("LIGHTMAPS "+LightmapSettings.lightmaps.Length+" PROBES "+(LightmapSettings.lightProbes?.count??0)+" REFLECTION "+RenderSettings.customReflectionTexture);
                foreach(var material in renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct())text.AppendLine("MAT "+material.name+" SHADER "+material.shader?.name+" PASSES "+string.Join(",",Enumerable.Range(0,material.passCount).Select(i=>material.GetPassName(i))));
                File.WriteAllText("LocalOnly/Barrack/live/stage-native-report.txt",text.ToString());SessionState.SetBool(Key,false);EditorApplication.Exit(0);
            }catch(Exception e){SessionState.SetBool(Key,false);File.WriteAllText("LocalOnly/Barrack/live/stage-native-report.txt","FAIL "+e);Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
