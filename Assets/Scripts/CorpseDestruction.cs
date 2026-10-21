using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    internal static class CorpseDestruction
    {
        // Whole bodies split at their anatomical joints, even with crossed or
        // folded limbs. A previously detached limb still breaks into large chunks.
        internal static List<Mesh> Bake(Transform model,Vector3 origin,int count,out Material[] materials,
            bool separateLimbs=false,int missingLimbs=0)
        {
            if(separateLimbs) return BakeSeparated(model,origin,missingLimbs,out materials);
            materials=null;
            var inputs=new List<Mesh>();
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!skin.enabled||skin.sharedMesh==null||skin.name.Contains("Shadow")||skin.shadowCastingMode==ShadowCastingMode.ShadowsOnly) continue;
                var mesh=PlayerDamageMesh.WorldMesh(skin,out _);
                var vertices=mesh.vertices;
                for(int i=0;i<vertices.Length;i++) vertices[i]-=origin;
                mesh.vertices=vertices;mesh.RecalculateBounds();inputs.Add(mesh);
                if(materials==null) ColorOnlyTextureQuality.GetOriginalAppearance(skin,out materials,out _);
            }
            if(inputs.Count==0) return new List<Mesh>();
            var combined=new Mesh { name="Baked corpse",indexFormat=IndexFormat.UInt32 };
            try { combined.CombineMeshes(inputs.SelectMany(mesh=>Enumerable.Range(0,mesh.subMeshCount)
                .Select(slot=>new CombineInstance { mesh=mesh,subMeshIndex=slot })).ToArray(),true,false); }
            finally { foreach(var mesh in inputs) PlayerDamage.Release(mesh); }
            combined.RecalculateBounds();
            var pieces=new List<Mesh>{combined};var rejected=new HashSet<Mesh>();
            double minimum=System.Math.Max(.000001,DestructionMesh.SolidVolume(combined)*.08);
            while(pieces.Count<count)
            {
                var source=pieces.Where(p=>!rejected.Contains(p)).OrderByDescending(DestructionMesh.SolidVolume).FirstOrDefault();
                if(source==null) break;
                bool split=false;
                foreach(int axis in new[]{0,1,2}.OrderByDescending(i=>source.bounds.size[i]))
                {
                    foreach(float fraction in new[]{.5f,.42f,.58f})
                    {
                        Vector3 normal=axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward;
                        Vector3 point=source.bounds.center;point[axis]=Mathf.Lerp(source.bounds.min[axis],source.bounds.max[axis],fraction);
                        if(!DestructionMesh.Split(source,new Plane(normal,point),out var a,out var b))
                        { PlayerDamage.Release(a);PlayerDamage.Release(b);continue; }
                        if(DestructionMesh.SolidVolume(a)<minimum||DestructionMesh.SolidVolume(b)<minimum)
                        { PlayerDamage.Release(a);PlayerDamage.Release(b);continue; }
                        pieces.Remove(source);pieces.Add(a);pieces.Add(b);PlayerDamage.Release(source);split=true;break;
                    }
                    if(split) break;
                }
                if(!split) rejected.Add(source);
            }
            foreach(var piece in pieces)
            {
                var vertices=piece.vertices;
                for(int i=0;i<vertices.Length;i++) vertices[i]+=origin;
                piece.vertices=vertices;piece.RecalculateBounds();
            }
            return pieces;
        }

        static List<Mesh> BakeSeparated(Transform model,Vector3 origin,int missingLimbs,out Material[] materials)
        {
            materials=null;
            var groups=Enumerable.Range(0,5).Select(_=>new List<Mesh>()).ToArray();
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!skin.enabled||skin.sharedMesh==null||skin.name.Contains("Shadow")||skin.shadowCastingMode==ShadowCastingMode.ShadowsOnly) continue;
                if(materials==null) ColorOnlyTextureQuality.GetOriginalAppearance(skin,out materials,out _);
                Mesh remainder=skin.sharedMesh;bool owned=false;
                try
                {
                    for(int limb=1;limb<=4&&remainder!=null;limb++)
                    {
                        if((missingLimbs&(1<<(limb-1)))!=0) continue;
                        var detached=PlayerDamageMesh.LimbMesh(skin,remainder,limb,true);
                        if(detached==null) continue;
                        try { groups[limb].Add(BakeCompact(skin,detached,origin)); }
                        finally { PlayerDamage.Release(detached); }
                        var next=PlayerDamageMesh.LimbMesh(skin,remainder,limb,false);
                        if(owned) PlayerDamage.Release(remainder);
                        remainder=next;owned=true;
                    }
                    if(remainder!=null) groups[0].Add(BakeCompact(skin,remainder,origin));
                }
                finally { if(owned) PlayerDamage.Release(remainder); }
            }
            string[] names={"Corpse torso","Corpse left arm","Corpse right arm","Corpse left leg","Corpse right leg"};
            var pieces=new List<Mesh>();
            for(int group=0;group<groups.Length;group++)
            {
                if(groups[group].Count==0) continue;
                var mesh=new Mesh { name=names[group],indexFormat=IndexFormat.UInt32 };
                try { mesh.CombineMeshes(groups[group].Select(part=>new CombineInstance { mesh=part }).ToArray(),true,false); }
                finally { foreach(var part in groups[group]) PlayerDamage.Release(part); }
                var points=mesh.vertices;for(int i=0;i<points.Length;i++) points[i]+=origin;
                mesh.vertices=points;mesh.RecalculateBounds();pieces.Add(mesh);
            }
            return pieces;
        }

        static Mesh BakeCompact(SkinnedMeshRenderer skin,Mesh source,Vector3 origin)
        {
            var world=PlayerDamageMesh.WorldMesh(skin,source,out _);
            try
            {
                var positions=world.vertices;var sourceUv=world.uv;var indices=world.triangles;
                var remap=new Dictionary<int,int>();var points=new List<Vector3>();var uv=new List<Vector2>();
                for(int i=0;i<indices.Length;i++)
                {
                    int old=indices[i];
                    if(!remap.TryGetValue(old,out int index))
                    {
                        index=points.Count;remap.Add(old,index);points.Add(positions[old]-origin);
                        uv.Add(old<sourceUv.Length?sourceUv[old]:Vector2.zero);
                    }
                    indices[i]=index;
                }
                var mesh=new Mesh { indexFormat=IndexFormat.UInt32 };
                mesh.SetVertices(points);mesh.SetUVs(0,uv);mesh.triangles=indices;
                mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
            }
            finally { PlayerDamage.Release(world); }
        }
    }
}
