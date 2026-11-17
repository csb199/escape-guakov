using System;
using System.IO;
using System.Linq;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Game.Editor.Barrack
{
    public static class BarrackRosterImporter
    {
        const string Root="Assets/Game/Content/BarrackSource/";
        [Serializable] sealed class Report { public string[] actions; }
        [Serializable] public sealed class RosterDocument { public RosterEntry[] characters; }
        [Serializable] public sealed class RosterEntry { public string id,displayName; public string[] excludedClips; }
        public static RosterEntry[] Entries() => JsonUtility.FromJson<RosterDocument>(File.ReadAllText(Root+"roster.json")).characters;
        // These source actions require auxiliary props/cameras that are not yet bound.
        // Keep their native clips in the archive while hiding unsupported interaction entries.
        static string[] SupportedClips(string id,string[] clips) => clips.Where(c =>
            !((id=="FlorenceSSR01"||id=="ZhaohuiSSR01"||id=="ClukaySSR01") && c.EndsWith("_Touch_Pocket")) &&
            !(id=="ZhaohuiSSR01" && c.EndsWith("_Touch_Head"))).ToArray();
        // Repair imports from source containers whose wrapper and actual character share a name.
        public static void NormalizeNestedSourceRoots()
        {
            foreach(var entry in Entries())
            {
                string path=Root+entry.id+"/"+entry.id+".prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var child=root.transform.Find(entry.id);
                    if(child==null||child.GetComponent<Animator>()==null||child.GetComponent<Animator>().avatar==null)continue;
                    var copy=UnityEngine.Object.Instantiate(child.gameObject);copy.name=entry.id;
                    copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);copy.transform.localScale=Vector3.one;
                    try { PrefabUtility.SaveAsPrefabAsset(copy,path); }
                    finally { UnityEngine.Object.DestroyImmediate(copy); }
                    Debug.Log("BARRACK_SOURCE_SUBTREE_NORMALIZED "+entry.id);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Game Framework/Barrack/Prepare Character Roster")]
        public static void Prepare()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before preparing the roster");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var entries=Entries();
            if(entries.Length==0||entries.Select(e=>e.id).Distinct().Count()!=entries.Length)throw new InvalidDataException("Empty or duplicate roster");
            var roster=entries.Select(entry=>
            {
                string id=entry.id,folder=Root+id+"/";
                var report=JsonUtility.FromJson<Report>(File.ReadAllText(folder+"import-report.json"));
                Texture2D portrait;
                if(id=="GrozaSR01")portrait=AssetDatabase.LoadAssetAtPath<BarrackSourceUiDefinition>(Root+"SourcePresentation/SourceUi.asset")?.capturedTextures.FirstOrDefault(t=>t.name=="Avatar_Bust_GrozaSR");
                else
                {
                    var importer=(TextureImporter)AssetImporter.GetAtPath(folder+"Portrait.png");
                    importer.sRGBTexture=true;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
                    portrait=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"Portrait.png");
                }
                var definition=new BarrackCharacterDefinition {id=id,displayName=entry.displayName,prefab=AssetDatabase.LoadAssetAtPath<GameObject>(folder+id+".prefab"),shaders=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"OriginalCharacterShaders.bytes"),additionalShaders=Directory.GetFiles(folder,"OriginalCharacterShadersExtra*.bytes").OrderBy(p=>p,StringComparer.Ordinal).Select(p=>AssetDatabase.LoadAssetAtPath<TextAsset>(p.Replace('\\','/'))).ToArray(),animations=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"OriginalBarrackAnimations.bytes"),materialContract=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"material-contract.json"),clips=SupportedClips(id,report.actions).Except(entry.excludedClips??Array.Empty<string>()).ToArray(),portrait=portrait};
                definition.interactionContract=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"interaction-contract.json");
                if(definition.portrait==null&&id!="GrozaSR01")throw new InvalidDataException("Missing portrait: "+id);
                if(definition.prefab==null||definition.shaders==null||definition.animations==null||definition.materialContract==null||definition.interactionContract==null)throw new InvalidDataException("Incomplete roster asset: "+id);
                return definition;
            }).ToArray();
            var scene=EditorSceneManager.OpenScene(BarrackStartup.ScenePath);
            var host=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();
            if(roster[0].portrait==null)roster[0].portrait=host.stageDefinition.sourceUi.capturedTextures.First(t=>t.name=="Avatar_Bust_GrozaSR");
            host.stageDefinition.characterLayer=roster[0].prefab.layer;
            var forward=JsonUtility.FromJson<BarrackEnvironment>(host.stageDefinition.environment.text).features.Single(f=>f.name=="CharForward");
            if((forward.layerMask&(1L<<host.stageDefinition.characterLayer))==0)throw new InvalidDataException("Stage character layer is outside source CharForward mask");
            EditorUtility.SetDirty(host.stageDefinition);
            host.characters=roster;
            host.interactionData=roster[0].interactionContract;
            PrefabUtility.SaveAsPrefabAsset(host.gameObject,Root+"Stage/BarrackPresentation.prefab");
            EditorUtility.SetDirty(host);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("BARRACK_ROSTER_PREPARED "+string.Join(",",roster.Select(c=>c.id)));
        }
    }
}
