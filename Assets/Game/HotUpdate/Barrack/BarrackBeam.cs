using System;
using UnityEngine;
namespace Game.HotUpdate.Barrack
{
    // Perspective branch reconstructed from event 2932; mesh and constants retain capture provenance.
    sealed class BarrackBeam : MonoBehaviour
    {
        [Serializable] public sealed class Data {public Vector3[] positions;public Vector2[] uv;public int[] indices;public Vector4[] parameters;public Vector3 position,scale;public Quaternion rotation;}
        Mesh mesh;Material material;
        public void Initialize(TextAsset data,Shader shader)
        {
            var d=JsonUtility.FromJson<Data>(data.text);transform.SetPositionAndRotation(d.position,d.rotation);transform.localScale=d.scale;
            mesh=new Mesh{name="Captured volume geometry",vertices=d.positions,uv=d.uv,triangles=d.indices};mesh.RecalculateBounds();
            material=new Material(shader){name="Captured volume perspective adapter"};material.SetVectorArray("_Beam",d.parameters);
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        void OnDestroy(){if(mesh!=null)Destroy(mesh);if(material!=null)Destroy(material);}
    }
}
