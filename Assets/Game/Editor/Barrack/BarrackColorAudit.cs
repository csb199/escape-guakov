using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Game.Editor.Barrack {
public static class BarrackColorAudit {
public static void Run() {
var lines=AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Game/Content/BarrackSource/GrozaSR01/Assets"}).Select(g=>AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g))).Select(t=>t.name+" format="+t.graphicsFormat+" srgb="+t.isDataSRGB).ToList();
foreach(bool linear in new[]{false,true}){var t=new Texture2D(2,2,TextureFormat.RGBA32,false,linear);lines.Add("constructed linear="+linear+" "+EditorJsonUtility.ToJson(t));Object.DestroyImmediate(t);}
File.WriteAllLines("LocalOnly/Barrack/live/color-audit.txt",lines);EditorApplication.Exit(0);
}
}}
