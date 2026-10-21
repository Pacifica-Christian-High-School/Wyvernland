using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class PlayerNeckMeshSeal
    {
        const string PrefabPath="Assets/Resources/Player/HumanRiggedEdited.prefab";
        readonly struct Edge
        {
            public readonly int A,B,Count;
            public Edge(int a,int b,int count) { A=a;B=b;Count=count; }
            public Edge Increment()=>new(A,B,Count+1);
        }

        static List<List<int>> OpenLoops(Mesh mesh)
        {
            var points=mesh.vertices;var ids=new int[points.Length];
            var welded=new Dictionary<Vector3Int,int>();
            for(int i=0;i<points.Length;i++) {
                Vector3 p=points[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*1000000),
                    Mathf.RoundToInt(p.y*1000000),Mathf.RoundToInt(p.z*1000000));
                if(!welded.TryGetValue(key,out int id)) welded.Add(key,id=welded.Count);
                ids[i]=id;
            }
            var edges=new Dictionary<(int,int),Edge>();
            void Add(int a,int b) {
                int x=ids[a],y=ids[b];if(x==y)return;
                var key=(Math.Min(x,y),Math.Max(x,y));
                edges[key]=edges.TryGetValue(key,out Edge edge)?edge.Increment():new Edge(a,b,1);
            }
            foreach(int[] triangles in Enumerable.Range(0,mesh.subMeshCount).Select(slot=>mesh.GetTriangles(slot)))
                for(int i=0;i+2<triangles.Length;i+=3) {
                    Add(triangles[i],triangles[i+1]);Add(triangles[i+1],triangles[i+2]);Add(triangles[i+2],triangles[i]);
                }
            var remaining=edges.Values.Where(edge=>edge.Count==1).ToList();
            var loops=new List<List<int>>();
            while(remaining.Count>0) {
                Edge first=remaining[0];remaining.RemoveAt(0);
                var loop=new List<int>{first.A};int current=first.B;
                while(ids[current]!=ids[first.A]&&loop.Count<=points.Length) {
                    int next=remaining.FindIndex(edge=>ids[edge.A]==ids[current]);
                    if(next<0) break;
                    Edge edge=remaining[next];remaining.RemoveAt(next);
                    loop.Add(edge.A);current=edge.B;
                }
                if(ids[current]==ids[first.A]&&loop.Count>=3) loops.Add(loop);
            }
            return loops;
        }

        static List<int> NeckLoop(Mesh mesh,SkinnedMeshRenderer body,SkinnedMeshRenderer head)
        {
            Vector3[] points=mesh.vertices;
            foreach(var loop in OpenLoops(mesh)) {
                Vector3 center=Vector3.zero;
                foreach(int index in loop) center+=body.transform.TransformPoint(points[index]);
                center/=loop.Count;
                if(Mathf.Abs(center.y-head.bounds.min.y)<.08f&&
                    Mathf.Abs(center.x-head.bounds.center.x)<.1f&&
                    Mathf.Abs(center.z-head.bounds.center.z)<.12f) return loop;
            }
            return null;
        }

        static void SealHeadUnderside(SkinnedMeshRenderer head)
        {
            Mesh mesh=head.sharedMesh;
            if(mesh.name.Contains("Neck Side Sealed")) return;
            var baked=new Mesh();head.BakeMesh(baked);
            int[] rim;
            try {
                var world=baked.vertices;float low=world.Min(p=>p.z);
                rim=world.Select((point,index)=>(point,index))
                    .Where(pair=>pair.point.z<low+.02f)
                    .GroupBy(pair=>new Vector2Int(Mathf.RoundToInt(pair.point.x*10000f),
                        Mathf.RoundToInt(pair.point.y*10000f)))
                    .Select(group=>group.First().index).ToArray();
                if(rim.Length<3) throw new InvalidOperationException("Head has no neck-side rim to seal");
                Vector2 center=new Vector2(rim.Average(index=>world[index].x),rim.Average(index=>world[index].y));
                rim=rim.OrderBy(index=>Mathf.Atan2(world[index].y-center.y,world[index].x-center.x)).ToArray();
            } finally { UnityEngine.Object.DestroyImmediate(baked); }
            int original=mesh.vertexCount;var points=mesh.vertices.ToList();var weights=mesh.boneWeights.ToList();
            var normals=mesh.normals.ToList();var tangents=mesh.tangents.ToList();var colors=mesh.colors32.ToList();
            var channels=new List<List<Vector4>>();
            for(int channel=0;channel<8;channel++) { var data=new List<Vector4>();mesh.GetUVs(channel,data);channels.Add(data); }
            int[] cap=new int[rim.Length];Vector3 centerPoint=Vector3.zero;
            for(int i=0;i<rim.Length;i++) {
                int source=rim[i];cap[i]=points.Count;
                Vector3 p=points[source]+Vector3.back*.00002f;points.Add(p);centerPoint+=p;
                weights.Add(weights[source]);
                if(normals.Count==original) normals.Add(Vector3.back);
                if(tangents.Count==original) tangents.Add(new Vector4(1,0,0,1));
                if(colors.Count==original) colors.Add(colors[source]);
                foreach(var data in channels) if(data.Count==original) data.Add(data[source]);
            }
            centerPoint/=rim.Length;int middle=points.Count;points.Add(centerPoint);weights.Add(weights[rim[0]]);
            if(normals.Count==original) normals.Add(Vector3.back);
            if(tangents.Count==original) tangents.Add(new Vector4(1,0,0,1));
            if(colors.Count==original) colors.Add(colors[rim[0]]);
            foreach(var data in channels) if(data.Count==original) data.Add(data[rim[0]]);
            var triangles=mesh.GetTriangles(0).ToList();
            for(int i=0;i<cap.Length;i++) { triangles.Add(middle);triangles.Add(cap[(i+1)%cap.Length]);triangles.Add(cap[i]); }
            mesh.vertices=points.ToArray();mesh.boneWeights=weights.ToArray();
            if(normals.Count==points.Count) mesh.normals=normals.ToArray();
            if(tangents.Count==points.Count) mesh.tangents=tangents.ToArray();
            if(colors.Count==points.Count) mesh.colors32=colors.ToArray();
            for(int channel=0;channel<channels.Count;channel++) if(channels[channel].Count==points.Count) mesh.SetUVs(channel,channels[channel]);
            mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.name+=" Neck Side Sealed";
            EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssets();
            Debug.Log($"HEAD_NECK_SEAL_PASS: added a skinned underside cap across {cap.Length} rim vertices.");
        }

        [MenuItem("Tools/Wyvernland/Seal Player Neck Mesh")]
        public static void Apply()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if(prefab==null) throw new InvalidOperationException("Missing player rig prefab");
            var model=UnityEngine.Object.Instantiate(prefab);
            try {
                var body=model.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="HumanBody");
                var head=model.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="HumanHead");
                SealHeadUnderside(head);
                Mesh mesh=body.sharedMesh;var loop=NeckLoop(mesh,body,head);
                if(loop==null) { Validate();return; }
                int originalCount=mesh.vertexCount;
                var points=mesh.vertices.ToList();var weights=mesh.boneWeights.ToList();
                var normals=mesh.normals.ToList();var tangents=mesh.tangents.ToList();
                var colors=mesh.colors32.ToList();
                var channels=new List<List<Vector4>>();
                for(int channel=0;channel<8;channel++) {
                    var data=new List<Vector4>();mesh.GetUVs(channel,data);channels.Add(data);
                }
                Vector3 capNormal=body.transform.InverseTransformDirection(Vector3.up);
                Vector3 capTangent=body.transform.InverseTransformDirection(Vector3.right);
                int[] cap=new int[loop.Count];
                for(int i=0;i<loop.Count;i++) {
                    int source=loop[i];cap[i]=points.Count;points.Add(points[source]);weights.Add(weights[source]);
                    if(normals.Count==originalCount) normals.Add(capNormal);
                    if(tangents.Count==originalCount) tangents.Add(new Vector4(capTangent.x,capTangent.y,capTangent.z,1));
                    if(colors.Count==originalCount) colors.Add(colors[source]);
                    foreach(var data in channels) if(data.Count==originalCount) data.Add(data[source]);
                }
                int[][] triangles=Enumerable.Range(0,mesh.subMeshCount).Select(slot=>mesh.GetTriangles(slot)).ToArray();
                var first=triangles[0].ToList();
                for(int i=1;i+1<cap.Length;i++) {
                    first.Add(cap[0]);first.Add(cap[i+1]);first.Add(cap[i]);
                }
                triangles[0]=first.ToArray();
                mesh.vertices=points.ToArray();mesh.boneWeights=weights.ToArray();
                if(normals.Count==points.Count) mesh.normals=normals.ToArray();
                if(tangents.Count==points.Count) mesh.tangents=tangents.ToArray();
                if(colors.Count==points.Count) mesh.colors32=colors.ToArray();
                for(int channel=0;channel<channels.Count;channel++)
                    if(channels[channel].Count==points.Count) mesh.SetUVs(channel,channels[channel]);
                for(int slot=0;slot<triangles.Length;slot++) mesh.SetTriangles(triangles[slot],slot);
                mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssets();
                if(NeckLoop(mesh,body,head)!=null) throw new InvalidOperationException("Neck ring stayed open after sealing");
                Debug.Log($"PLAYER_NECK_SEAL_PASS: capped {loop.Count} rim vertices and added {loop.Count-2} skinned triangles.");
            } finally { UnityEngine.Object.DestroyImmediate(model); }
        }

        public static void Validate()
        {
            var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try {
                var body=model.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="HumanBody");
                var head=model.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="HumanHead");
                if(NeckLoop(body.sharedMesh,body,head)!=null)
                    throw new InvalidOperationException("Player torso neck opening remains unsealed");
                if(body.sharedMesh.boneWeights.Length!=body.sharedMesh.vertexCount)
                    throw new InvalidOperationException("Neck cap is missing skin weights");
                if(!head.sharedMesh.name.Contains("Neck Side Sealed")||
                    head.sharedMesh.boneWeights.Length!=head.sharedMesh.vertexCount)
                    throw new InvalidOperationException("Head neck-side underside cap is missing or unskinned");
                Debug.Log("PLAYER_NECK_MESH_VALIDATION_PASS");
            } finally { UnityEngine.Object.DestroyImmediate(model); }
        }
    }
}
