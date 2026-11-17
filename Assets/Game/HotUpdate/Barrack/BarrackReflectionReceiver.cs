using System;
using UnityEngine;
namespace Game.HotUpdate.Barrack
{
    // Runtime-created receiver from frame 8378; source scene serialization has no receiver renderer.
    sealed class BarrackReflectionReceiver : MonoBehaviour
    {
        [Serializable] public sealed class Document { public Vector3[] positions; public Vector2[] uv; public int[] indices; }
        Mesh mesh;
        Material material;
        public void Initialize(TextAsset contract)
        {
            var data=JsonUtility.FromJson<Document>(contract.text);
            mesh=new Mesh {name="Captured planar receiver",vertices=data.positions,uv=data.uv,triangles=data.indices};mesh.RecalculateNormals();mesh.RecalculateBounds();
            var shader=Array.Find(Resources.FindObjectsOfTypeAll<Shader>(),s=>s.name=="gf_shader/scene/RerlectionAdd")??Shader.Find("gf_shader/scene/RerlectionAdd");
            if(shader==null)throw new InvalidOperationException("Native reflection receiver shader unavailable");
            material=new Material(shader){name="Source planar reflection",renderQueue=2700};
            material.SetVector("_ReflectionTint",new Vector4(.35f,.35f,.35f,1));material.SetFloat("_AlphaScale",4.3f);material.SetFloat("_AlphaOffset",2);
            material.SetFloat("_BlurSize",0);material.SetFloat("_BlurOffset",2.65f);material.SetTexture("_BlurTex",Texture2D.whiteTexture);
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        void OnDestroy(){if(material!=null)Destroy(material);if(mesh!=null)Destroy(mesh);}
    }
}
