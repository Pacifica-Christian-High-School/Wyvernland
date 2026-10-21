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
        internal static int ClosestHeadFace(SkinnedMeshRenderer skin,Mesh source,Vector3 hit,IReadOnlyCollection<int> removed)
        {
            if(skin==null||source==null||!source.isReadable) return -1;
            var world=WorldMesh(skin,source,out _);
            try {
                var points=world.vertices;Vector3 center=world.bounds.center;
                Vector3 ray=(hit-center).normalized;
                float best=float.PositiveInfinity;int selected=-1,ordinal=0;
                for(int slot=0;slot<world.subMeshCount;slot++) {
                    var triangles=world.GetTriangles(slot);
                    for(int t=0;t+2<triangles.Length;t+=3,ordinal++) {
                        if(removed!=null&&removed.Contains(ordinal)) continue;
                        Vector3 a=points[triangles[t]],b=points[triangles[t+1]],c=points[triangles[t+2]];
                        Vector3 nearest=PickableObject.ClosestTrianglePoint(hit,a,b,c);
                        Vector3 midpoint=(a+b+c)/3f;
                        float score=(nearest-hit).sqrMagnitude+
                            .05f*Vector3.Cross(midpoint-center,ray).sqrMagnitude;
                        if(score<best) { best=score;selected=ordinal; }
                    }
                }
                return selected;
            } finally { PlayerDamage.Release(world); }
        }

        internal static Mesh RemoveHeadFaces(Mesh source,IReadOnlyCollection<int> removed)
        {
            if(source==null) return null;
            var result=UnityEngine.Object.Instantiate(source);result.name="Head with separated faces";
            var points=source.vertices.ToList();var weights=source.boneWeights.ToList();
            var uv=source.uv.Length==source.vertexCount?source.uv.ToList():
                Enumerable.Repeat(Vector2.zero,source.vertexCount).ToList();
            var slots=new List<int[]>();
            int ordinal=0;
            for(int slot=0;slot<source.subMeshCount;slot++) {
                var triangles=source.GetTriangles(slot);var remaining=new List<int>(triangles.Length);
                for(int t=0;t+2<triangles.Length;t+=3,ordinal++) {
                    int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
                    if(removed==null||!removed.Contains(ordinal)) {
                        remaining.Add(a);remaining.Add(b);remaining.Add(c);continue;
                    }
                    // Leave a shallow, closed wound rather than a transparent
                    // triangle through the head. The inset shares skin weights.
                    Vector3 center=(points[a]+points[b]+points[c])/3f;
                    Vector3 inward=(source.bounds.center-center).normalized*.012f;
                    if(inward.sqrMagnitude<.000001f)
                        inward=-Vector3.Cross(points[b]-points[a],points[c]-points[a]).normalized*.012f;
                    int d=points.Count;
                    foreach(int index in new[]{a,b,c}) {
                        points.Add(points[index]+inward);weights.Add(weights[index]);uv.Add(uv[index]);
                    }
                    remaining.AddRange(new[]{d,d+1,d+2,
                        a,b,d+1,a,d+1,d,
                        b,c,d+2,b,d+2,d+1,
                        c,a,d,c,d,d+2});
                }
                slots.Add(remaining.ToArray());
            }
            result.SetVertices(points);result.boneWeights=weights.ToArray();result.SetUVs(0,uv);
            for(int slot=0;slot<slots.Count;slot++) result.SetTriangles(slots[slot],slot,false);
            result.RecalculateNormals();result.RecalculateBounds();return result;
        }

        internal static void SpawnHeadFace(SkinnedMeshRenderer skin,Mesh source,int face,Vector3 incoming,string identity,Transform owner)
        {
            if(face<0||skin==null||source==null) return;
            var world=WorldMesh(skin,source,out _);
            try {
                int ordinal=0;
                for(int slot=0;slot<world.subMeshCount;slot++) {
                    var triangles=world.GetTriangles(slot);var points=world.vertices;var uv=world.uv;
                    for(int t=0;t+2<triangles.Length;t+=3,ordinal++) if(ordinal==face) {
                        Vector3 a=points[triangles[t]],b=points[triangles[t+1]],c=points[triangles[t+2]];
                        Vector3 inward=(world.bounds.center-(a+b+c)/3f).normalized*.012f;
                        var piece=new Mesh { name="Separated head face" };
                        piece.vertices=new[]{a,b,c,a+inward,b+inward,c+inward};
                        Vector2 U(int index)=>uv.Length==world.vertexCount?uv[index]:Vector2.zero;
                        piece.uv=new[]{U(triangles[t]),U(triangles[t+1]),U(triangles[t+2]),
                            U(triangles[t]),U(triangles[t+1]),U(triangles[t+2])};
                        piece.triangles=new[]{0,1,2,5,4,3,0,3,4,0,4,1,1,4,5,1,5,2,2,5,3,2,3,0};
                        piece.RecalculateNormals();piece.RecalculateBounds();
                        ColorOnlyTextureQuality.GetOriginalAppearance(skin,out var materials,out _);
                        var material=materials!=null&&materials.Length>0?materials[Mathf.Min(slot,materials.Length-1)]:null;
                        BodyFragment.Spawn(piece,new[]{material},incoming,identity+":head-face",owner);
                        return;
                    }
                }
            } finally { PlayerDamage.Release(world); }
        }
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
        internal static bool PartiallyShatterHead(SkinnedMeshRenderer skin,Vector3 velocity,List<Mesh> owned,string identity,Transform owner,bool spawnFragments)
        {
            var source=skin.sharedMesh;if(source==null||!source.isReadable||source.boneWeights.Length!=source.vertexCount) return false;
            var bounds=source.bounds;
            // Keep the lower 65% attached to the neck. Cut in bind space so
            // remote reconstruction retains the same head regardless of ragdoll pose.
            var plane=new Plane(Vector3.up,new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.65f,bounds.center.z));
            if(!DestructionMesh.Split(source,plane,out var a,out var b)) { PlayerDamage.Release(a);PlayerDamage.Release(b);return false; }
            var remainder=a.bounds.center.y<b.bounds.center.y?a:b;
            var detached=remainder==a?b:a;
            if(remainder.vertexCount<3||remainder.triangles.Length<3) { PlayerDamage.Release(a);PlayerDamage.Release(b);return false; }
            void Bind(Mesh mesh) {
                var original=source.vertices;var originalWeights=source.boneWeights;var points=mesh.vertices;
                var exact=new Dictionary<Vector3,BoneWeight>();for(int j=0;j<original.Length;j++) exact[original[j]]=originalWeights[j];
                var weights=new BoneWeight[points.Length];
                for(int i=0;i<points.Length;i++) {
                    if(exact.TryGetValue(points[i],out var existing)) { weights[i]=existing;continue; }
                    int nearest=0;float distance=float.PositiveInfinity;
                    for(int j=0;j<original.Length;j++) { float d=(original[j]-points[i]).sqrMagnitude;if(d<distance) { distance=d;nearest=j; } }
                    weights[i]=originalWeights[nearest];
                }
                mesh.boneWeights=weights;mesh.bindposes=source.bindposes;
            }
            Bind(remainder);Bind(detached);remainder.name="Partially shattered head remainder";
            try {
                if(spawnFragments) { skin.sharedMesh=detached;Shatter(skin,velocity,owned,identity,owner); }
            } finally { skin.sharedMesh=remainder;owned.Add(remainder);PlayerDamage.Release(detached); }
            skin.enabled=true;
            return true;
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
