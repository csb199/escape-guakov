using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Game.Editor.Barrack
{
    static class BarrackTextureContract
    {
        [Serializable] sealed class Document { public Item[] textures; }
        [Serializable] sealed class Item { public string name,asset,format; public bool srgb; }
        public static void Validate()
        {
            if(PlayerSettings.colorSpace!=ColorSpace.Linear)throw new InvalidDataException("Barrack requires a Linear player color space");
            foreach(var contract in Directory.GetFiles("Assets/Game/Content/BarrackSource", "texture-color-contract.json", SearchOption.AllDirectories))
            {
            var data=JsonUtility.FromJson<Document>(File.ReadAllText(contract));
            foreach(var item in data.textures)
            {
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(item.asset);
                if(texture==null||texture.name!=item.name||texture.isDataSRGB!=item.srgb)
                    throw new InvalidDataException("Captured texture color space differs: "+item.name+" expected "+item.format);
            }
            }
        }
    }
}
