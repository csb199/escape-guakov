using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Editor.Barrack
{
    // Native Shader objects cannot be serialized with their platform bytecode by CreateAsset.
    // This reference preserves property names/types and shader identity; runtime always binds the native library.
    public static class BarrackShaderReference
    {
        public static Shader Save(Shader source, string path)
        {
            var text = new StringBuilder("// Editor reference only; runtime binds the original native Shader library.\nShader \"")
                .Append(source.name).Append("\"\n{\nProperties\n{\n");
            string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
            for (int i = 0; i < source.GetPropertyCount(); i++)
            {
                string name = source.GetPropertyName(i), type, value;
                switch (source.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        type = source.GetPropertyType(i) == ShaderPropertyType.Color ? "Color" : "Vector";
                        var vector = source.GetPropertyDefaultVectorValue(i);
                        value = $"({Number(vector.x)},{Number(vector.y)},{Number(vector.z)},{Number(vector.w)})";
                        break;
                    case ShaderPropertyType.Texture:
                        switch (source.GetPropertyTextureDimension(i))
                        {
                            case TextureDimension.Cube: type = "Cube"; break;
                            case TextureDimension.Tex3D: type = "3D"; break;
                            case TextureDimension.Tex2DArray: type = "2DArray"; break;
                            case TextureDimension.CubeArray: type = "CubeArray"; break;
                            default: type = "2D"; break;
                        }
                        value = "\"" + source.GetPropertyTextureDefaultName(i) + "\" {}";
                        break;
                    default: type = "Float"; value = Number(source.GetPropertyDefaultFloatValue(i)); break;
                }
                text.Append(name).Append(" (\"").Append(name).Append("\", ").Append(type).Append(") = ").Append(value).Append('\n');
            }
            text.Append("}\nSubShader { Pass { ColorMask 0 ZWrite Off } }\nFallback Off\n}\n");
            File.WriteAllText(path, text.ToString());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Shader>(path) ?? throw new InvalidDataException("Shader reference import failed: " + path);
        }
    }
}
