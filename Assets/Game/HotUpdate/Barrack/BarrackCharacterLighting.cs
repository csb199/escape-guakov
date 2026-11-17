using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace Game.HotUpdate.Barrack
{
    // Uses the source character probe cells and authored renderer anchors. Unity 6 returns
    // ambient SH immediately outside this legacy probe hull; extend its boundary continuously.
    [DefaultExecutionOrder(1000)]
    public sealed class BarrackCharacterLighting : MonoBehaviour
    {
        [Serializable] sealed class Probe { public float[] coefficients; }
        [Serializable] sealed class Cell { public int[] indices; }
        [Serializable] sealed class Contract { public Vector3[] positions; public Probe[] probes; public Cell[] cells; }
        Contract data;
        readonly List<int[]> hull = new List<int[]>();
        Matrix4x4[] inverse;
        Renderer[] renderers;
        MaterialPropertyBlock block;
        readonly SphericalHarmonicsL2[] value = new SphericalHarmonicsL2[1];
        public void Initialize(TextAsset contract)
        {
            if(contract==null)throw new InvalidOperationException("Source character light probe contract missing");
            block=new MaterialPropertyBlock();
            data=JsonUtility.FromJson<Contract>(contract.text);
            if(data.positions.Length!=data.probes.Length||data.cells.Length==0||data.probes.Any(p=>p.coefficients.Length!=27))throw new InvalidOperationException("Invalid character light probes");
            inverse=new Matrix4x4[data.cells.Length];var faces=new Dictionary<string,(int count,int[] indices)>();
            for(int i=0;i<data.cells.Length;i++)
            {
                var ids=data.cells[i].indices;var m=Matrix4x4.identity;
                for(int j=0;j<3;j++){Vector3 delta=data.positions[ids[j]]-data.positions[ids[3]];m.SetColumn(j,new Vector4(delta.x,delta.y,delta.z,0));}
                if(Mathf.Abs(m.determinant)<1e-8f)throw new InvalidOperationException("Degenerate source light probe cell");inverse[i]=m.inverse;
                for(int skip=0;skip<4;skip++)
                {
                    var face=ids.Where((id,j)=>j!=skip).ToArray();var key=string.Join(",",face.OrderBy(id=>id));
                    faces[key]=(faces.TryGetValue(key,out var old)?old.count+1:1,face);
                }
            }
            hull.AddRange(faces.Values.Where(f=>f.count==1).Select(f=>f.indices));
            renderers=GetComponentsInChildren<Renderer>(true).Where(r=>r.lightProbeUsage==LightProbeUsage.BlendProbes).ToArray();
            foreach(var r in renderers)r.lightProbeUsage=LightProbeUsage.CustomProvided;
            Refresh();
        }
        void LateUpdate()=>Refresh();
        public void Refresh()
        {
            if(data==null)return;
            foreach(var r in renderers)
            {
                value[0]=Sample(r.probeAnchor!=null?r.probeAnchor.position:r.bounds.center);
                r.GetPropertyBlock(block);block.CopySHCoefficientArraysFrom(value);r.SetPropertyBlock(block);
            }
        }
        public SphericalHarmonicsL2 Sample(Vector3 position)
        {
            for(int i=0;i<data.cells.Length;i++)
            {
                var ids=data.cells[i].indices;var w=inverse[i].MultiplyVector(position-data.positions[ids[3]]);float last=1-w.x-w.y-w.z;
                if(Mathf.Min(w.x,Mathf.Min(w.y,Mathf.Min(w.z,last)))>=-1e-6f)return Blend(ids,new Vector4(w.x,w.y,w.z,last));
            }
            // Closest point on the authored convex hull keeps the boundary value. No sky/ambient
            // substitution, time smoothing or cross-character cached coefficients participates.
            float best=float.PositiveInfinity;int[] selected=null;Vector3 weights=default;
            foreach(var face in hull)
            {
                var a=data.positions[face[0]];var b=data.positions[face[1]];var c=data.positions[face[2]];
                var w=ClosestWeights(position,a,b,c);float distance=(position-(a*w.x+b*w.y+c*w.z)).sqrMagnitude;
                if(distance<best){best=distance;selected=face;weights=w;}
            }
            return Blend(selected,new Vector4(weights.x,weights.y,weights.z,0));
        }
        SphericalHarmonicsL2 Blend(int[] ids,Vector4 weights)
        {
            var sh=new SphericalHarmonicsL2();
            for(int i=0;i<ids.Length;i++)for(int c=0;c<3;c++)for(int k=0;k<9;k++)sh[c,k]+=data.probes[ids[i]].coefficients[c*9+k]*weights[i];
            return sh;
        }
        static Vector3 ClosestWeights(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0&&d2<=0)return new Vector3(1,0,0);
            var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0&&d4<=d3)return new Vector3(0,1,0);
            float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0){float v=d1/(d1-d3);return new Vector3(1-v,v,0);}
            var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0&&d5<=d6)return new Vector3(0,0,1);
            float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0){float w=d2/(d2-d6);return new Vector3(1-w,0,w);}
            float va=d3*d6-d5*d4;if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0){float w=(d4-d3)/((d4-d3)+(d5-d6));return new Vector3(0,1-w,w);}
            float inv=1/(va+vb+vc);return new Vector3(1-(vb+vc)*inv,vb*inv,vc*inv);
        }
    }
}
