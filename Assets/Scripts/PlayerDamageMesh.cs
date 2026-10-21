using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wyvernland
{
    // Boolean subtraction is performed in the current world pose. Rebind every
    // resulting surface and cavity vertex so the pit continues to bend with skin.
    public static class PlayerDamageMesh
    {
        static Matrix4x4 Blend(BoneWeight w,Matrix4x4[] matrices)
        {
            var result=new Matrix4x4();
            Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);
            return result;
            void Add(int bone,float weight) { if(weight<=0||bone<0||bone>=matrices.Length) return;for(int i=0;i<16;i++) result[i]+=matrices[bone][i]*weight; }
        }
        internal static Mesh WorldMesh(SkinnedMeshRenderer skin,out Matrix4x4[] matrices)
            => WorldMesh(skin,skin.sharedMesh,out matrices);
        internal static Mesh WorldMesh(SkinnedMeshRenderer skin,Mesh source,out Matrix4x4[] matrices)
        {
            matrices=new Matrix4x4[skin.bones.Length];
            for(int i=0;i<matrices.Length;i++) matrices[i]=skin.bones[i].localToWorldMatrix*source.bindposes[i];
            var world=UnityEngine.Object.Instantiate(source);var points=source.vertices;var weights=source.boneWeights;
            for(int i=0;i<points.Length;i++) points[i]=Blend(weights[i],matrices).MultiplyPoint3x4(points[i]);
            world.vertices=points;world.RecalculateBounds();world.RecalculateNormals();return world;
        }
        public static Mesh CutPit(SkinnedMeshRenderer skin,Vector3 point,float radius,float depth,List<Mesh> debris=null)
        {
            var world=WorldMesh(skin,out var matrices);Mesh remainder=null;
            try {
                // Fitted hitboxes and imported skin baking can put the contact
                // just outside the visible surface. Cut at the actual skin.
                var worldPoints=world.vertices;var triangles=world.triangles;float closestDistance=float.PositiveInfinity;Vector3 surface=point;
                for(int i=0;i+2<triangles.Length;i+=3) {
                    var candidate=PickableObject.ClosestTrianglePoint(point,worldPoints[triangles[i]],worldPoints[triangles[i+1]],worldPoints[triangles[i+2]]);
                    float d=(candidate-point).sqrMagnitude;if(d<closestDistance) { closestDistance=d;surface=candidate; }
                }
                point=surface;
                var chips=DestructionMesh.Chip(world,point,radius,depth,out remainder,8);
                bool cut=chips.Count>0;foreach(var chip in chips) { if(debris!=null) debris.Add(chip);else PlayerDamage.Release(chip); }
                if(!cut||remainder==null) { PlayerDamage.Release(remainder);return null; }
                var source=skin.sharedMesh;var oldPoints=world.vertices;var oldWeights=source.boneWeights;
                var points=remainder.vertices;var weights=new BoneWeight[points.Length];
                // New cavity vertices inherit the nearest original skin weights;
                // inverse blended bind transforms preserve their actual world cut.
                for(int i=0;i<points.Length;i++) {
                    int nearest=0;float distance=float.PositiveInfinity;
                    for(int j=0;j<oldPoints.Length;j++) { float d=(oldPoints[j]-points[i]).sqrMagnitude;if(d<distance) { distance=d;nearest=j; } }
                    weights[i]=oldWeights[nearest];points[i]=Blend(weights[i],matrices).inverse.MultiplyPoint3x4(points[i]);
                }
                remainder.name="Player torso Boolean pit";remainder.vertices=points;
                remainder.boneWeights=weights;remainder.bindposes=source.bindposes;
                remainder.RecalculateNormals();remainder.RecalculateBounds();return remainder;
            } finally { PlayerDamage.Release(world); }
        }
        internal static bool InLimb(string name,int limb)
        {
            bool left=limb==1||limb==3;
            if(!name.StartsWith(left?"Left":"Right",StringComparison.Ordinal)) return false;
            return limb<=2?name.Contains("Shoulder")||name.Contains("Elbow")||name.Contains("Wrist")||name.Contains("Finger")||name.Contains("Thumb"):
                name.Contains("Hip")||name.Contains("Knee")||name.Contains("Ankle")||name.Contains("Toe");
        }
        internal static Mesh LimbMesh(SkinnedMeshRenderer skin,int limb,bool keepLimb)
            => LimbMesh(skin,skin.sharedMesh,limb,keepLimb);
        internal static Mesh LimbMesh(SkinnedMeshRenderer skin,Mesh source,int limb,bool keepLimb)
        {
            if(source==null) return null;
            var points=source.vertices;var weights=source.boneWeights;
            var selected=new bool[points.Length];
            for(int i=0;i<selected.Length;i++) {
                var w=weights[i];float amount=0;
                Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);
                selected[i]=amount>=.5f;
                void Add(int bone,float value) { if(bone>=0&&bone<skin.bones.Length&&skin.bones[bone]!=null&&InLimb(skin.bones[bone].name,limb)) amount+=value; }
            }
            var slots=new List<int[]>();int count=0;
            for(int slot=0;slot<source.subMeshCount;slot++) {
                var triangles=source.GetTriangles(slot);var kept=new List<int>();
                for(int i=0;i+2<triangles.Length;i+=3) {
                    bool inside=(selected[triangles[i]]?1:0)+(selected[triangles[i+1]]?1:0)+(selected[triangles[i+2]]?1:0)>=2;
                    if(inside!=keepLimb) continue;kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);
                }
                count+=kept.Count;slots.Add(kept.ToArray());
            }
            if(count==0) return null;
            // Discard unreferenced vertices before rebinding. Otherwise fitted
            // colliders would still include the removed torso or limb vertices.
            int[] used=slots.SelectMany(indices=>indices).Distinct().OrderBy(i=>i).ToArray();
            var remap=new int[points.Length];for(int i=0;i<used.Length;i++) remap[used[i]]=i;
            var sourceUv=source.uv;
            var uv=used.Select(i=>sourceUv.Length==source.vertexCount?sourceUv[i]:Vector2.zero).ToArray();
            points=used.Select(i=>points[i]).ToArray();weights=used.Select(i=>weights[i]).ToArray();
            for(int slot=0;slot<slots.Count;slot++) slots[slot]=slots[slot].Select(i=>remap[i]).ToArray();
            // Boundary triangles can contain weights from both sides of the cut.
            // Remove all opposite-side influences and compensate in the current
            // world pose, so moving the detached bones cannot stretch a face back.
            var matrices=new Matrix4x4[skin.bones.Length];
            for(int b=0;b<matrices.Length;b++) matrices[b]=skin.bones[b].localToWorldMatrix*source.bindposes[b];
            var clean=new BoneWeight[weights.Length];var valid=new List<int>();
            for(int i=0;i<weights.Length;i++) {
                var w=weights[i];float total=0;var c=new BoneWeight();
                void Add(int bone,float amount,int slot) {
                    if(amount<=0||InLimb(skin.bones[bone].name,limb)!=keepLimb) return;
                    total+=amount;
                    if(slot==0) { c.boneIndex0=bone;c.weight0=amount; }
                    if(slot==1) { c.boneIndex1=bone;c.weight1=amount; }
                    if(slot==2) { c.boneIndex2=bone;c.weight2=amount; }
                    if(slot==3) { c.boneIndex3=bone;c.weight3=amount; }
                }
                Add(w.boneIndex0,w.weight0,0);Add(w.boneIndex1,w.weight1,1);Add(w.boneIndex2,w.weight2,2);Add(w.boneIndex3,w.weight3,3);
                if(total>0) { c.weight0/=total;c.weight1/=total;c.weight2/=total;c.weight3/=total;clean[i]=c;valid.Add(i); }
            }
            if(valid.Count==0) return null;
            var originalPoints=(Vector3[])points.Clone();
            for(int i=0;i<weights.Length;i++) {
                var w=clean[i];
                if(w.weight0+w.weight1+w.weight2+w.weight3<=0) {
                    int nearest=valid.OrderBy(j=>(originalPoints[j]-originalPoints[i]).sqrMagnitude).First();w=clean[nearest];
                }
                Vector3 world=Blend(weights[i],matrices).MultiplyPoint3x4(points[i]);
                points[i]=Blend(w,matrices).inverse.MultiplyPoint3x4(world);clean[i]=w;
            }
            weights=clean;
            var result=new Mesh { name=keepLimb?"Detached limb skin":"Skin with severed limb",indexFormat=source.indexFormat,subMeshCount=slots.Count,bindposes=source.bindposes };
            CapBoundaries(points,weights,uv,slots,out var cappedPoints,out var cappedWeights,out var cappedUv);
            result.vertices=cappedPoints;result.boneWeights=cappedWeights;result.uv=cappedUv;
            for(int slot=0;slot<slots.Count;slot++) result.SetTriangles(slots[slot],slot);
            result.RecalculateNormals();result.RecalculateBounds();return result;
        }
        static void CapBoundaries(Vector3[] points,BoneWeight[] weights,Vector2[] uv,List<int[]> slots,out Vector3[] cappedPoints,out BoneWeight[] cappedWeights,out Vector2[] cappedUv)
        {
            var positions=points.ToList();var bindings=weights.ToList();var texture=uv.Length==points.Length?uv.ToList():Enumerable.Repeat(Vector2.zero,points.Length).ToList();
            var welded=new Dictionary<Vector3,int>();var weld=new int[points.Length];
            for(int i=0;i<points.Length;i++) { if(!welded.TryGetValue(points[i],out int id)) welded.Add(points[i],id=welded.Count);weld[i]=id; }
            var edges=new Dictionary<(int,int),(int A,int B,int Count)>();
            foreach(var triangles in slots) for(int i=0;i+2<triangles.Length;i+=3) { Edge(triangles[i],triangles[i+1]);Edge(triangles[i+1],triangles[i+2]);Edge(triangles[i+2],triangles[i]); }
            void Edge(int a,int b) {
                if(weld[a]==weld[b]) return;var key=(Mathf.Min(weld[a],weld[b]),Mathf.Max(weld[a],weld[b]));
                if(edges.TryGetValue(key,out var old)) edges[key]=(old.A,old.B,old.Count+1);else edges.Add(key,(a,b,1));
            }
            var boundary=edges.Values.Where(e=>e.Count==1).Select(e=>(e.A,e.B)).ToList();var caps=slots[0].ToList();
            while(boundary.Count>0) {
                var first=boundary[0];boundary.RemoveAt(0);var loop=new List<int> { first.A,first.B };int end=weld[first.B];
                while(end!=weld[first.A]&&loop.Count<=points.Length) {
                    int index=boundary.FindIndex(e=>weld[e.A]==end);if(index<0) break;
                    var next=boundary[index];boundary.RemoveAt(index);loop.Add(next.B);end=weld[next.B];
                }
                if(end!=weld[first.A]||loop.Count<4) continue;loop.RemoveAt(loop.Count-1);
                Vector3 center=Vector3.zero;foreach(int i in loop) center+=points[i];center/=loop.Count;
                int pivot=positions.Count;positions.Add(center);bindings.Add(weights[loop[0]]);texture.Add(Vector2.zero);
                for(int i=0;i<loop.Count;i++) { caps.Add(loop[(i+1)%loop.Count]);caps.Add(loop[i]);caps.Add(pivot); }
            }
            slots[0]=caps.ToArray();cappedPoints=positions.ToArray();cappedWeights=bindings.ToArray();cappedUv=texture.ToArray();
        }
        internal static void RemoveLimbs(Transform model,int mask,List<Mesh> owned)
        {
            for(int limb=1;limb<=4;limb++) if((mask&(1<<(limb-1)))!=0) foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var result=LimbMesh(skin,limb,false);
                if(result==null) { skin.enabled=false;continue; }skin.sharedMesh=result;owned.Add(result);
            }
        }
        public static void Shatter(SkinnedMeshRenderer skin,Vector3 velocity,List<Mesh> owned,string identity=null,Transform owner=null)
        {
            var world=WorldMesh(skin,out _);var pieces=new List<Mesh>{world};var bounds=world.bounds;
            foreach(Vector3 normal in new[]{Vector3.right,Vector3.up,Vector3.forward,new Vector3(1,1,.3f).normalized}) {
                var next=new List<Mesh>();
                foreach(var mesh in pieces) {
                    if(DestructionMesh.Split(mesh,new Plane(normal,bounds.center),out var a,out var b)) { next.Add(a);next.Add(b);PlayerDamage.Release(mesh); }
                    else next.Add(mesh);
                }
                pieces=next;
            }
            int index=0;
            ColorOnlyTextureQuality.GetOriginalAppearance(skin,out var materials,out _);
            foreach(var mesh in pieces) if(mesh!=null) BodyFragment.Spawn(mesh,materials,velocity,(identity??Guid.NewGuid().ToString("N"))+":"+index++,owner);
        }
    }
}
