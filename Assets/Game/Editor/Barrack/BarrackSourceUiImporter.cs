using System;
using System.IO;
using System.Linq;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEngine;
namespace Game.Editor.Barrack
{
    static class BarrackSourceUiImporter
    {
        const string Root="Assets/Game/Content/BarrackSource/SourcePresentation/";
        [Serializable] sealed class Data { public Image[] sprites; public SourceFont[] fonts; }
        [Serializable] sealed class SourceFont { public int index; public string file; }
        [Serializable] sealed class Image { public int index; public string file; public Vector4 border; }
        public static BarrackSourceUiDefinition Prepare()
        {
            var path=Root+"SourceUi.asset"; var asset=AssetDatabase.LoadAssetAtPath<BarrackSourceUiDefinition>(path);
            if(asset==null) { asset=ScriptableObject.CreateInstance<BarrackSourceUiDefinition>();AssetDatabase.CreateAsset(asset,path); }
            asset.uiShader=Shader.Find("Game/Barrack/UI");
            asset.layout=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"layout-runtime.json");
            var data=JsonUtility.FromJson<Data>(asset.layout.text); asset.sprites=new Sprite[data.sprites.Length];
            foreach(var image in data.sprites)
            {
                var png=Root+"Sprites/"+image.file;var importer=(TextureImporter)AssetImporter.GetAtPath(png);
                if(importer.textureType!=TextureImporterType.Sprite || importer.textureCompression!=TextureImporterCompression.Uncompressed || importer.spriteBorder!=image.border)
                { importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single; importer.spritePixelsPerUnit=100;importer.mipmapEnabled=false;importer.sRGBTexture=true;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.spriteBorder=image.border;importer.SaveAndReimport(); }
                asset.sprites[image.index]=AssetDatabase.LoadAssetAtPath<Sprite>(png);
            }
            asset.fonts=data.fonts.Select(f=>AssetDatabase.LoadAssetAtPath<Font>(Root+"Fonts/"+f.file) ?? throw new FileNotFoundException(f.file)).ToArray();
            asset.presentationSnapshot=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"presentation-snapshot.json");
            asset.linearCapturedTextures=JsonUtility.FromJson<BarrackCalibratedUi.Snapshot>(asset.presentationSnapshot.text).linearCapturedTextures;
            asset.capturedTextures=Directory.GetFiles(Root+"CapturedSprites","*.png").Select(p=>{
                var importer=(TextureImporter)AssetImporter.GetAtPath(p);
                bool srgb=!asset.linearCapturedTextures.Contains(Path.GetFileNameWithoutExtension(p));
                if(importer.textureCompression!=TextureImporterCompression.Uncompressed||!importer.mipmapEnabled||importer.sRGBTexture!=srgb) {importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.sRGBTexture=srgb;importer.alphaIsTransparency=true;importer.SaveAndReimport();}
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }).ToArray();
            asset.extraSprites=Directory.GetFiles(Root+"ExtraSprites","*.png").Select(p=>{
                var importer=(TextureImporter)AssetImporter.GetAtPath(p);
                if(importer.textureCompression!=TextureImporterCompression.Uncompressed||!importer.alphaIsTransparency) {importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.alphaIsTransparency=true;importer.SaveAndReimport();}
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }).ToArray();
            EditorUtility.SetDirty(asset);return asset;
        }
    }
}

