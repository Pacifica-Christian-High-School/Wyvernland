using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    // Operates on private mesh copies. UVs, vertex colors and material slots survive cuts.
    public static class DestructionMesh
    {
        private const float Epsilon = 0.00001f;
        sealed class ValidationCache
        {
            public readonly Dictionary<Mesh,bool> closed=new();
            public readonly Dictionary<Mesh,int[]> welded=new();
        }
        static ValidationCache validationCache;
        sealed class ValidationScope : IDisposable
        {
            readonly ValidationCache previous;
            public ValidationScope()
            {
                previous=validationCache;
                validationCache??=new ValidationCache();
            }
            public void Dispose() { validationCache=previous; }
        }
        internal static IDisposable BeginValidationScope() => new ValidationScope();
        internal static void InvalidateValidation(Mesh mesh)
        {
            if(validationCache==null) return;
            validationCache.closed.Remove(mesh);
            validationCache.welded.Remove(mesh);
        }
        static int[] WeldedVertexIds(Mesh mesh,Vector3[] vertices)
        {
            if(validationCache!=null&&validationCache.welded.TryGetValue(mesh,out var cached)) return cached;
            var result=WeldedVertexIds(vertices);
            if(validationCache!=null) validationCache.welded[mesh]=result;
            return result;
        }
        public struct Vertex
        {
            public Vector3 P;
            public Vector2 UV;
            public Color Color;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
                { P=Vector3.Lerp(a.P,b.P,t), UV=Vector2.Lerp(a.UV,b.UV,t), Color=Color.Lerp(a.Color,b.Color,t) };
        }
        public sealed class Builder
        {
            readonly List<Vector3> positions=new();
            readonly List<Vector2> uvs=new();
            readonly List<Color> colors=new();
            readonly List<int>[] triangles;
            public Builder(int slots) { triangles=new List<int>[Math.Max(1,slots)]; for(int i=0;i<triangles.Length;i++) triangles[i]=new(); }
            public void Triangle(Vertex a,Vertex b,Vertex c,int slot)
            {
                if(Vector3.Cross(b.P-a.P,c.P-a.P).sqrMagnitude<1e-16f) return;
                Add(a,slot); Add(b,slot); Add(c,slot);
            }
            void Add(Vertex vertex,int slot)
            {
                triangles[slot].Add(positions.Count);
                positions.Add(vertex.P); uvs.Add(vertex.UV); colors.Add(vertex.Color);
            }
            public Mesh Build()
            {
                if(positions.Count==0) return null;
                var mesh=new Mesh { name="Physical destruction mesh", indexFormat=IndexFormat.UInt32 };
                mesh.SetVertices(positions); mesh.SetUVs(0,uvs); mesh.SetColors(colors);
                mesh.subMeshCount=triangles.Length;
                for(int i=0;i<triangles.Length;i++) mesh.SetTriangles(triangles[i],i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
        static Vertex[] Read(Mesh mesh)
        {
            var p=mesh.vertices; var uv=mesh.uv; var colors=mesh.colors; var result=new Vertex[p.Length];
            for(int i=0;i<p.Length;i++) result[i]=new Vertex { P=p[i], UV=uv.Length==p.Length?uv[i]:Vector2.zero, Color=colors.Length==p.Length?colors[i]:Color.white };
            return result;
        }
        internal static Vector3Int WeldKey(Vector3 point) => new(
            Mathf.RoundToInt(point.x*10000f),Mathf.RoundToInt(point.y*10000f),
            Mathf.RoundToInt(point.z*10000f));
        static (Vector3Int,Vector3Int) WeldEdge(Vector3 a,Vector3 b)
        {
            var first=WeldKey(a); var second=WeldKey(b);
            bool ordered=first.x<second.x||first.x==second.x&&
                (first.y<second.y||first.y==second.y&&first.z<=second.z);
            return ordered?(first,second):(second,first);
        }
        public static bool IsClosedConnected(Mesh mesh)
        {
            if(mesh==null||!mesh.isReadable) return false;
            if(validationCache!=null&&validationCache.closed.TryGetValue(mesh,out bool cached)) return cached;
            bool result=CheckClosedConnected(mesh);
            if(validationCache!=null) validationCache.closed[mesh]=result;
            return result;
        }
        static bool CheckClosedConnected(Mesh mesh)
        {
            if(mesh==null||!mesh.isReadable) return false;
            var vertices=mesh.vertices;
            if(vertices.Length==0) return false;
            foreach(var vertex in vertices)
                if(!float.IsFinite(vertex.x)||!float.IsFinite(vertex.y)||!float.IsFinite(vertex.z)) return false;
            // Absolute rounded coordinates can put two equivalent seam
            // vertices into different buckets after a fragment is translated
            // to its centre. Cluster by distance in neighbouring cells instead.
            int[] welded=WeldedVertexIds(mesh,vertices);
            var edges=new Dictionary<(int,int),(int face,int count,int winding)>();
            var uniqueFaces=new HashSet<(int,int,int)>();
            var parents=new List<int>();
            int Root(int face)
            {
                while(parents[face]!=face)
                { parents[face]=parents[parents[face]]; face=parents[face]; }
                return face;
            }
            for(int slot=0;slot<mesh.subMeshCount;slot++)
            {
                var indices=mesh.GetTriangles(slot);
                if(indices.Length%3!=0) return false;
                for(int i=0;i<indices.Length;i+=3)
                {
                    int a=welded[indices[i]];
                    int b=welded[indices[i+1]];
                    int c=welded[indices[i+2]];
                    if(a==b||b==c||c==a||
                        Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],
                            vertices[indices[i+2]]-vertices[indices[i]]).sqrMagnitude<1e-16f)
                        return false;
                    int first=a,second=b,third=c;
                    if(first>second) (first,second)=(second,first);
                    if(second>third) (second,third)=(third,second);
                    if(first>second) (first,second)=(second,first);
                    // Opposite copies of a triangle pair every edge but enclose
                    // no solid. Reject those folded, double-sided sheets.
                    if(!uniqueFaces.Add((first,second,third))) return false;
                    int face=parents.Count; parents.Add(face);
                    for(int edgeIndex=0;edgeIndex<3;edgeIndex++)
                    {
                        var edge=edgeIndex==0?(a,b):edgeIndex==1?(b,c):(c,a);
                        var key=edge.Item1<=edge.Item2?edge:(edge.Item2,edge.Item1);
                        int winding=edge.Item1==key.Item1?1:-1;
                        if(edges.TryGetValue(key,out var state))
                        {
                            if(state.count==2) return false;
                            parents[Root(face)]=Root(state.face);
                            edges[key]=(state.face,state.count+1,state.winding+winding);
                        }
                        else edges.Add(key,(face,1,winding));
                    }
                }
            }
            if(parents.Count<4) return false;
            foreach(var state in edges.Values)
                if(state.count!=2||state.winding!=0) return false;
            int connectedRoot=Root(0);
            for(int face=1;face<parents.Count;face++)
                if(Root(face)!=connectedRoot) return false;
            double scale=mesh.bounds.size.magnitude;
            return SolidVolume(mesh)>Math.Max(1e-12,scale*scale*scale*1e-9);
        }
        internal static Vector3[] CanonicalVertices(Mesh mesh)
        {
            var vertices=mesh.vertices;
            var welded=WeldedVertexIds(mesh,vertices);
            for(int i=0;i<vertices.Length;i++) vertices[i]=vertices[welded[i]];
            return vertices;
        }
        static int[] WeldedVertexIds(Vector3[] vertices)
        {
            const float cell=0.0001f;
            const float tolerance=0.00011f;
            var buckets=new Dictionary<Vector3Int,List<int>>();
            var exact=new Dictionary<Vector3,int>(vertices.Length);
            var parent=new int[vertices.Length];
            Vector3 origin=vertices[0];
            int Root(int index)
            {
                while(parent[index]!=index)
                { parent[index]=parent[parent[index]]; index=parent[index]; }
                return index;
            }
            for(int i=0;i<vertices.Length;i++)
            {
                parent[i]=i;
                // Flat-shaded meshes repeat the same position for each face.
                // Only distinct points need the neighbouring-cell search.
                if(exact.TryGetValue(vertices[i],out int duplicate))
                { parent[i]=Root(duplicate); continue; }
                exact.Add(vertices[i],i);
                Vector3 position=(vertices[i]-origin)/cell;
                var key=new Vector3Int(Mathf.FloorToInt(position.x),
                    Mathf.FloorToInt(position.y),Mathf.FloorToInt(position.z));
                for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++) for(int z=-1;z<=1;z++)
                {
                    if(!buckets.TryGetValue(key+new Vector3Int(x,y,z),out var nearby)) continue;
                    foreach(int other in nearby)
                    {
                        Vector3 delta=vertices[i]-vertices[other];
                        if(Mathf.Abs(delta.x)<=tolerance&&Mathf.Abs(delta.y)<=tolerance&&
                            Mathf.Abs(delta.z)<=tolerance) parent[Root(i)]=Root(other);
                    }
                }
                if(!buckets.TryGetValue(key,out var bucket))
                    buckets.Add(key,bucket=new List<int>());
                bucket.Add(i);
            }
            for(int i=0;i<parent.Length;i++) parent[i]=Root(i);
            return parent;
        }
        static (Vector3Int,Vector3Int) WeldEdgeFromKeys(Vector3Int first,Vector3Int second)
        {
            bool ordered=first.x<second.x||first.x==second.x&&
                (first.y<second.y||first.y==second.y&&first.z<=second.z);
            return ordered?(first,second):(second,first);
        }
        public static List<Mesh> SeparateComponents(Mesh mesh)
        {
            var vertices=Read(mesh);
            var welded=WeldedVertexIds(mesh,mesh.vertices);
            var faces=new List<(int a,int b,int c,int slot)>();
            for(int slot=0;slot<mesh.subMeshCount;slot++)
            {
                var indices=mesh.GetTriangles(slot);
                for(int i=0;i<indices.Length;i+=3)
                {
                    int a=indices[i],b=indices[i+1],c=indices[i+2];
                    if(Vector3.Cross(vertices[b].P-vertices[a].P,
                        vertices[c].P-vertices[a].P).sqrMagnitude<1e-16f) continue;
                    faces.Add((a,b,c,slot));
                }
            }
            if(faces.Count==0) return new List<Mesh>();
            var parents=new int[faces.Count];
            for(int i=0;i<parents.Length;i++) parents[i]=i;
            int Root(int index)
            {
                while(parents[index]!=index)
                { parents[index]=parents[parents[index]]; index=parents[index]; }
                return index;
            }
            var edgeOwners=new Dictionary<(int,int),int>();
            for(int i=0;i<faces.Count;i++)
            {
                var face=faces[i];
                var corners=new[]{face.a,face.b,face.c};
                for(int edge=0;edge<3;edge++)
                {
                    int a=welded[corners[edge]],b=welded[corners[(edge+1)%3]];
                    var key=a<=b?(a,b):(b,a);
                    if(edgeOwners.TryGetValue(key,out int owner)) parents[Root(i)]=Root(owner);
                    else edgeOwners.Add(key,i);
                }
            }
            var groups=new Dictionary<int,Builder>();
            for(int i=0;i<faces.Count;i++)
            {
                int root=Root(i);
                if(!groups.TryGetValue(root,out var builder))
                    groups.Add(root,builder=new Builder(mesh.subMeshCount));
                var face=faces[i];
                builder.Triangle(vertices[face.a],vertices[face.b],vertices[face.c],face.slot);
            }
            if(groups.Count==1) return new List<Mesh>{mesh};
            var result=new List<Mesh>(groups.Count);
            foreach(var builder in groups.Values) result.Add(builder.Build());
            return result;
        }
        // Close simple cut contours on one face-connected shell. The source is
        // left untouched when a hole cannot be closed into a solid mesh.
        public static Mesh CloseOpenBoundaries(Mesh mesh)
        {
            if(mesh==null||!mesh.isReadable) return null;
            if(IsClosedConnected(mesh)) return SolidVolume(mesh)>1e-10f?mesh:null;
            var vertices=Read(mesh);
            var canonical=CanonicalVertices(mesh);
            for(int i=0;i<vertices.Length;i++) vertices[i].P=canonical[i];
            var edges=new Dictionary<(Vector3Int,Vector3Int),(int count,Vertex from,Vertex to)>();
            var builder=new Builder(mesh.subMeshCount);
            for(int slot=0;slot<mesh.subMeshCount;slot++)
            {
                var indices=mesh.GetTriangles(slot);
                for(int i=0;i+2<indices.Length;i+=3)
                {
                    var face=new[]{vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]]};
                    // A cut beside an existing ring can leave a sliver whose
                    // corners weld together. It has no area and must not add
                    // phantom boundary edges to the cap contour.
                    if(Vector3.Cross(face[1].P-face[0].P,face[2].P-face[0].P).sqrMagnitude<1e-16f)
                        continue;
                    builder.Triangle(face[0],face[1],face[2],slot);
                    for(int j=0;j<3;j++)
                    {
                        var from=face[j]; var to=face[(j+1)%3];
                        var key=WeldEdge(from.P,to.P);
                        if(edges.TryGetValue(key,out var edge))
                        {
                            if(edge.count>=2) return null;
                            edge.count++; edges[key]=edge;
                        }
                        else edges.Add(key,(1,from,to));
                    }
                }
            }
            var cleaned=builder.Build();
            if(cleaned!=null&&IsClosedConnected(cleaned)) return cleaned;
            if(cleaned!=null)
            { if(Application.isPlaying) UnityEngine.Object.Destroy(cleaned); else UnityEngine.Object.DestroyImmediate(cleaned); }
            var boundary=new Dictionary<Vector3Int,(Vertex from,Vertex to)>();
            var incoming=new HashSet<Vector3Int>();
            foreach(var edge in edges.Values)
            {
                if(edge.count!=1) continue;
                var start=WeldKey(edge.from.P); var end=WeldKey(edge.to.P);
                if(!boundary.TryAdd(start,(edge.from,edge.to))||!incoming.Add(end)) return null;
            }
            if(boundary.Count<3||incoming.Count!=boundary.Count) return null;
            var used=new HashSet<Vector3Int>();
            foreach(var start in boundary.Keys)
            {
                if(used.Contains(start)) continue;
                var loop=new List<Vertex>(); var cursor=start;
                while(cursor!=start||loop.Count==0)
                {
                    if(!boundary.TryGetValue(cursor,out var edge)||!used.Add(cursor)) return null;
                    loop.Add(edge.from); cursor=WeldKey(edge.to.P);
                }
                if(loop.Count<3) return null;
                loop.Reverse();
                Vector3 normal=Vector3.zero;
                for(int i=0;i<loop.Count;i++)
                    normal+=Vector3.Cross(loop[i].P,loop[(i+1)%loop.Count].P);
                if(normal.sqrMagnitude<1e-12f) return null;
                Cap(loop,normal.normalized,new Builder(mesh.subMeshCount),builder);
            }
            var repaired=builder.Build();
            if(repaired!=null&&IsClosedConnected(repaired)&&SolidVolume(repaired)>1e-10f)
                return repaired;
            if(repaired!=null)
            { if(Application.isPlaying) UnityEngine.Object.Destroy(repaired); else UnityEngine.Object.DestroyImmediate(repaired); }
            return null;
        }

        internal static double SolidVolume(Mesh mesh)
        {
            var vertices=mesh.vertices; double volume=0;
            Vector3 origin=mesh.bounds.center;
            for(int slot=0;slot<mesh.subMeshCount;slot++)
            {
                var indices=mesh.GetTriangles(slot);
                for(int i=0;i<indices.Length;i+=3)
                {
                    Vector3 a=vertices[indices[i]]-origin,
                        b=vertices[indices[i+1]]-origin,c=vertices[indices[i+2]]-origin;
                    // Do the products in double precision and near the mesh,
                    // so translation cannot give a sheet an apparent volume.
                    volume+=((double)a.x*((double)b.y*c.z-(double)b.z*c.y)+
                        (double)a.y*((double)b.z*c.x-(double)b.x*c.z)+
                        (double)a.z*((double)b.x*c.y-(double)b.y*c.x))/6d;
                }
            }
            return Math.Abs(volume);
        }
        public static Vector3 VolumeCentroid(Mesh mesh)
        {
            var points=mesh.vertices;
            Vector3 origin=mesh.bounds.center;
            double signedSixVolume=0,weightedX=0,weightedY=0,weightedZ=0;
            double area=0,areaX=0,areaY=0,areaZ=0;
            for(int slot=0;slot<mesh.subMeshCount;slot++)
            {
                var triangles=mesh.GetTriangles(slot);
                for(int i=0;i<triangles.Length;i+=3)
                {
                    Vector3 a=points[triangles[i]]-origin,b=points[triangles[i+1]]-origin,
                        c=points[triangles[i+2]]-origin;
                    double sixVolume=Vector3.Dot(a,Vector3.Cross(b,c));
                    signedSixVolume+=sixVolume;
                    weightedX+=(a.x+b.x+c.x)*sixVolume;
                    weightedY+=(a.y+b.y+c.y)*sixVolume;
                    weightedZ+=(a.z+b.z+c.z)*sixVolume;
                    double triangleArea=Vector3.Cross(b-a,c-a).magnitude;
                    area+=triangleArea;
                    areaX+=(a.x+b.x+c.x)*triangleArea;
                    areaY+=(a.y+b.y+c.y)*triangleArea;
                    areaZ+=(a.z+b.z+c.z)*triangleArea;
                }
            }
            double scale=mesh.bounds.size.magnitude;
            if(Math.Abs(signedSixVolume)>Math.Max(1e-12,scale*scale*scale*1e-9))
                return origin+new Vector3((float)(weightedX/(4*signedSixVolume)),
                    (float)(weightedY/(4*signedSixVolume)),
                    (float)(weightedZ/(4*signedSixVolume)));
            if(area>1e-12)
                return origin+new Vector3((float)(areaX/(3*area)),
                    (float)(areaY/(3*area)),(float)(areaZ/(3*area)));
            return origin;
        }
        public static bool Split(Mesh mesh,Plane plane,out Mesh positive,out Mesh negative)
        {
            var vertices=Read(mesh); var front=new Builder(mesh.subMeshCount); var back=new Builder(mesh.subMeshCount);
            var canonical=CanonicalVertices(mesh);
            for(int i=0;i<vertices.Length;i++)
            {
                vertices[i].P=canonical[i];
                float distance=plane.GetDistanceToPoint(vertices[i].P);
                if(Mathf.Abs(distance)<=Epsilon) vertices[i].P-=plane.normal*distance;
            }
            var segments=new List<(Vertex a,Vertex b)>(); bool hasFront=false,hasBack=false;
            foreach(var v in vertices) { hasFront|=plane.GetDistanceToPoint(v.P)>Epsilon; hasBack|=plane.GetDistanceToPoint(v.P)<-Epsilon; }
            positive=negative=null; if(!hasFront||!hasBack) return false;
            for(int slot=0;slot<mesh.subMeshCount;slot++)
            {
                var indices=mesh.GetTriangles(slot);
                for(int i=0;i<indices.Length;i+=3)
                {
                    var face=new[]{vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]]};
                    Clip(face,plane,true,front,slot); Clip(face,plane,false,back,slot);
                    var cut=new List<Vertex>();
                    for(int j=0;j<3;j++)
                    {
                        var a=face[j]; var b=face[(j+1)%3]; float da=plane.GetDistanceToPoint(a.P),db=plane.GetDistanceToPoint(b.P);
                        if(Mathf.Abs(da)<=Epsilon) AddUnique(cut,a);
                        if(da> Epsilon&&db< -Epsilon||da< -Epsilon&&db> Epsilon) AddUnique(cut,Vertex.Lerp(a,b,da/(da-db)));
                    }
                    if(cut.Count==2&&(cut[0].P-cut[1].P).sqrMagnitude>Epsilon*Epsilon&&
                       !segments.Exists(s=>Near(s.a,cut[0])&&Near(s.b,cut[1])||Near(s.a,cut[1])&&Near(s.b,cut[0])))
                        segments.Add((cut[0],cut[1]));
                }
            }
            // Stitch each disconnected cut contour separately, then triangulate concave contours.
            while(segments.Count>0)
            {
                var loop=new List<Vertex>{segments[0].a,segments[0].b}; segments.RemoveAt(0);
                bool closed=false;
                while(segments.Count>0)
                {
                    int found=-1; Vertex next=default;
                    for(int i=0;i<segments.Count;i++)
                    {
                        if(Near(loop[loop.Count-1],segments[i].a)) { found=i; next=segments[i].b; break; }
                        if(Near(loop[loop.Count-1],segments[i].b)) { found=i; next=segments[i].a; break; }
                    }
                    if(found<0) break;
                    segments.RemoveAt(found);
                    if(Near(next,loop[0])) { closed=true; break; }
                    loop.Add(next);
                }
                if(closed) Cap(loop,plane.normal,front,back);
            }
            positive=front.Build(); negative=back.Build(); return positive!=null&&negative!=null;
        }
        static bool Near(Vertex a,Vertex b)=>(a.P-b.P).sqrMagnitude<Epsilon*Epsilon*4;
        static void AddUnique(List<Vertex> list,Vertex v) { if(!list.Exists(x=>Near(x,v))) list.Add(v); }
        static void Clip(Vertex[] face,Plane plane,bool positive,Builder builder,int slot)
        {
            var polygon=new List<Vertex>();
            for(int i=0;i<3;i++)
            {
                var a=face[i]; var b=face[(i+1)%3]; float da=plane.GetDistanceToPoint(a.P),db=plane.GetDistanceToPoint(b.P);
                bool inside=positive?da>=-Epsilon:da<=Epsilon,
                    next=positive?db>=-Epsilon:db<=Epsilon;
                if(inside) AddUnique(polygon,a);
                // A vertex on the plane already belongs to both halves. Adding
                // an interpolation at that same point creates zero-length edges.
                if(inside!=next&&Mathf.Abs(da)>Epsilon&&Mathf.Abs(db)>Epsilon)
                    AddUnique(polygon,Vertex.Lerp(a,b,da/(da-db)));
            }
            for(int i=1;i+1<polygon.Count;i++) builder.Triangle(polygon[0],polygon[i],polygon[i+1],slot);
        }
        static void Cap(List<Vertex> loop,Vector3 normal,Builder front,Builder back)
        {
            Vector3 area=Vector3.zero;
            for(int i=0;i<loop.Count;i++) area+=Vector3.Cross(loop[i].P,loop[(i+1)%loop.Count].P);
            if(Vector3.Dot(area,normal)<0) loop.Reverse();
            int guard=loop.Count*loop.Count;
            while(loop.Count>=3&&guard-->0)
            {
                bool clipped=false;
                for(int i=0;i<loop.Count;i++)
                {
                    int prev=(i+loop.Count-1)%loop.Count,next=(i+1)%loop.Count;
                    var a=loop[prev]; var b=loop[i]; var c=loop[next];
                    if(Vector3.Dot(Vector3.Cross(b.P-a.P,c.P-b.P),normal)<=1e-10f) continue;
                    bool contains=false;
                    for(int j=0;j<loop.Count;j++)
                    {
                        if(j==prev||j==i||j==next) continue;
                        Vector3 p=loop[j].P;
                        if(Vector3.Dot(Vector3.Cross(b.P-a.P,p-a.P),normal)>=-1e-9f&&
                           Vector3.Dot(Vector3.Cross(c.P-b.P,p-b.P),normal)>=-1e-9f&&
                           Vector3.Dot(Vector3.Cross(a.P-c.P,p-c.P),normal)>=-1e-9f) { contains=true; break; }
                    }
                    if(contains) continue;
                    back.Triangle(a,b,c,0); front.Triangle(c,b,a,0); loop.RemoveAt(i); clipped=true; break;
                }
                if(!clipped) break;
            }
        }

        readonly struct TriangleFace
        {
            public readonly Vertex A,B,C;
            public readonly int Slot;
            public TriangleFace(Vertex a,Vertex b,Vertex c,int slot) { A=a; B=b; C=c; Slot=slot; }
        }
        static (Vector3,Vector3) Edge(Vector3 a,Vector3 b)
        {
            bool ordered=a.x<b.x||a.x==b.x&&(a.y<b.y||a.y==b.y&&a.z<=b.z);
            return ordered?(a,b):(b,a);
        }
        static List<TriangleFace> Faces(Mesh mesh)
        {
            var vertices=Read(mesh); var faces=new List<TriangleFace>();
            var canonical=CanonicalVertices(mesh);
            for(int i=0;i<vertices.Length;i++) vertices[i].P=canonical[i];
            for(int slot=0;slot<mesh.subMeshCount;slot++)
            {
                var indices=mesh.GetTriangles(slot);
                for(int i=0;i<indices.Length;i+=3)
                    faces.Add(new TriangleFace(vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]],slot));
            }
            return faces;
        }
        static List<TriangleFace> RefineNear(Mesh mesh,Vector3 point,float radius)
        {
            var faces=Faces(mesh);
            float maximumEdge=radius*1.25f;
            for(int pass=0;pass<16&&faces.Count<60000;pass++)
            {
                var marked=new HashSet<(Vector3,Vector3)>();
                foreach(var f in faces)
                {
                    if(DistanceToTriangle(point,f.A.P,f.B.P,f.C.P)>radius) continue;
                    float ab=(f.A.P-f.B.P).sqrMagnitude,bc=(f.B.P-f.C.P).sqrMagnitude,
                        ca=(f.C.P-f.A.P).sqrMagnitude;
                    float largest=Mathf.Max(ab,Mathf.Max(bc,ca));
                    if(largest<=maximumEdge*maximumEdge) continue;
                    marked.Add(largest==ab?Edge(f.A.P,f.B.P):largest==bc?Edge(f.B.P,f.C.P):Edge(f.C.P,f.A.P));
                }
                if(marked.Count==0) break;
                var refined=new List<TriangleFace>(faces.Count*2);
                foreach(var f in faces)
                {
                    bool ab=marked.Contains(Edge(f.A.P,f.B.P)),bc=marked.Contains(Edge(f.B.P,f.C.P)),
                        ca=marked.Contains(Edge(f.C.P,f.A.P));
                    if(!ab&&!bc&&!ca) { refined.Add(f); continue; }
                    var mab=Midpoint(f.A,f.B); var mbc=Midpoint(f.B,f.C);
                    var mca=Midpoint(f.C,f.A);
                    void Add(Vertex a,Vertex b,Vertex c)=>refined.Add(new TriangleFace(a,b,c,f.Slot));
                    if(ab&&bc&&ca)
                    { Add(f.A,mab,mca); Add(mab,f.B,mbc); Add(mca,mbc,f.C); Add(mab,mbc,mca); }
                    else if(ab&&bc) { Add(f.A,mab,f.C); Add(mab,mbc,f.C); Add(mab,f.B,mbc); }
                    else if(bc&&ca) { Add(f.B,mbc,f.A); Add(mbc,mca,f.A); Add(mbc,f.C,mca); }
                    else if(ca&&ab) { Add(f.C,mca,f.B); Add(mca,mab,f.B); Add(mca,f.A,mab); }
                    else if(ab) { Add(f.A,mab,f.C); Add(mab,f.B,f.C); }
                    else if(bc) { Add(f.B,mbc,f.A); Add(mbc,f.C,f.A); }
                    else { Add(f.C,mca,f.B); Add(mca,f.A,f.B); }
                }
                faces=refined;
            }
            return faces;
        }
        static Vertex Midpoint(Vertex a,Vertex b)
        {
            var midpoint=Vertex.Lerp(a,b,0.5f);
            // Shared edges are visited in opposite directions. Use symmetric
            // arithmetic so refinement never opens a floating-point seam.
            midpoint.P=(a.P+b.P)*0.5f;
            return midpoint;
        }

        // Group neighboring faces into closed chips. Wood uses larger patches;
        // shared edges have no internal walls, while the remainder keeps its perimeter.
        public static List<Mesh> Chip(Mesh source,Vector3 point,float radius,float depth,
            out Mesh remainder,int maxFacesPerChip=2)
        {
            var faces=RefineNear(source,point,radius);
            Bounds originalBounds=source.bounds;
            var keep=new Builder(source.subMeshCount); var pieces=new List<Mesh>();
            var edges=new Dictionary<(Vector3,Vector3),(Vertex a,Vertex b,Vertex ia,Vertex ib,int slot,int count)>();
            var selected=new List<TriangleFace>();
            var neighbors=new Dictionary<(Vector3,Vector3),List<int>>();
            // Cut into the material behind the contacted surface. A newly
            // exposed cavity floor can lie past the bounds centre, so aiming
            // toward that centre would cut into empty space and reject the hit.
            Vector3 surfaceNormal=Vector3.zero;
            float nearest=float.PositiveInfinity;
            foreach(var face in faces)
            {
                float distance=DistanceToTriangle(point,face.A.P,face.B.P,face.C.P);
                if(distance>=nearest) continue;
                nearest=distance;
                surfaceNormal=Vector3.Cross(face.B.P-face.A.P,face.C.P-face.A.P).normalized;
            }
            Vector3 towardCenter=(source.bounds.center-point).normalized;
            Vector3 inward=(Vector3.Dot(towardCenter,surfaceNormal)<-0.05f?
                towardCenter:-surfaceNormal)*depth;
            foreach(var face in faces)
            {
                var a=face.A; var b=face.B; var c=face.C; int slot=face.Slot;
                if(DistanceToTriangle(point,a.P,b.P,c.P)>radius||Vector3.Dot(Vector3.Cross(b.P-a.P,c.P-a.P),inward)>=0)
                { keep.Triangle(a,b,c,slot); continue; }
                float edge=Mathf.Max((a.P-b.P).magnitude,Mathf.Max((b.P-c.P).magnitude,(c.P-a.P).magnitude));
                if(edge>radius*1.251f) { keep.Triangle(a,b,c,slot); continue; }
                // The cavity floor and its walls are made from translated
                // surface vertices. Reject a face if that translation would
                // put any new vertex beyond the source mesh's own envelope.
                if(!InsideBounds(originalBounds,a.P+inward)||
                   !InsideBounds(originalBounds,b.P+inward)||
                   !InsideBounds(originalBounds,c.P+inward))
                { keep.Triangle(a,b,c,slot); continue; }
                int index=selected.Count;
                selected.Add(face);
                var ia=a; var ib=b; var ic=c; ia.P+=inward; ib.P+=inward; ic.P+=inward;
                keep.Triangle(ia,ib,ic,slot);
                var outer=new[]{a,b,c}; var inner=new[]{ia,ib,ic};
                for(int j=0;j<3;j++)
                {
                    int k=(j+1)%3;
                    var key=Edge(outer[j].P,outer[k].P);
                    if(!neighbors.TryGetValue(key,out var touching))
                    { touching=new List<int>(2); neighbors.Add(key,touching); }
                    touching.Add(index);
                    if(edges.TryGetValue(key,out var e)) { e.count++; edges[key]=e; }
                    else edges[key]=(outer[j],outer[k],inner[j],inner[k],slot,1);
                }
            }
            foreach(var e in edges.Values) if(e.count==1) Wall(keep,e.b,e.a,e.ib,e.ia,e.slot);
            var used=new bool[selected.Count];
            maxFacesPerChip=Mathf.Max(1,maxFacesPerChip);
            float minimumNormalDot=maxFacesPerChip>2?0.3f:0.98f;
            for(int i=0;i<selected.Count;i++)
            {
                if(used[i]) continue;
                used[i]=true;
                var patch=new List<int>{i};
                for(int cursor=0;cursor<patch.Count&&patch.Count<maxFacesPerChip;cursor++)
                {
                    var face=selected[patch[cursor]];
                    Vector3 normal=Vector3.Cross(face.B.P-face.A.P,face.C.P-face.A.P).normalized;
                    foreach(var key in new[]{Edge(face.A.P,face.B.P),Edge(face.B.P,face.C.P),Edge(face.C.P,face.A.P)})
                    {
                        foreach(int candidate in neighbors[key])
                        {
                            if(used[candidate]||selected[candidate].Slot!=face.Slot) continue;
                            var next=selected[candidate];
                            Vector3 nextNormal=Vector3.Cross(next.B.P-next.A.P,next.C.P-next.A.P).normalized;
                            if(Vector3.Dot(normal,nextNormal)<minimumNormalDot) continue;
                            used[candidate]=true; patch.Add(candidate);
                            if(patch.Count>=maxFacesPerChip) break;
                        }
                        if(patch.Count>=maxFacesPerChip) break;
                    }
                }
                var chip=new Builder(source.subMeshCount);
                var patchEdges=new Dictionary<(Vector3,Vector3),(Vertex a,Vertex b,Vertex ia,Vertex ib,int slot,int count)>();
                foreach(int member in patch) AddChipFace(selected[member]);
                foreach(var e in patchEdges.Values)
                    if(e.count==1) Wall(chip,e.a,e.b,e.ia,e.ib,e.slot);
                pieces.Add(chip.Build());

                void AddChipFace(TriangleFace face)
                {
                    var outer=new[]{face.A,face.B,face.C};
                    var inner=new[]{face.A,face.B,face.C};
                    for(int j=0;j<3;j++) inner[j].P+=inward;
                    chip.Triangle(outer[0],outer[1],outer[2],face.Slot);
                    chip.Triangle(inner[2],inner[1],inner[0],face.Slot);
                    for(int j=0;j<3;j++)
                    {
                        int k=(j+1)%3;
                        var key=Edge(outer[j].P,outer[k].P);
                        if(patchEdges.TryGetValue(key,out var e)) { e.count++; patchEdges[key]=e; }
                        else patchEdges[key]=(outer[j],outer[k],inner[j],inner[k],face.Slot,1);
                    }
                }
            }
            remainder=keep.Build(); return pieces;
        }
        static bool InsideBounds(Bounds bounds,Vector3 point)
        {
            const float tolerance=0.00001f;
            return point.x>=bounds.min.x-tolerance&&point.x<=bounds.max.x+tolerance&&
                point.y>=bounds.min.y-tolerance&&point.y<=bounds.max.y+tolerance&&
                point.z>=bounds.min.z-tolerance&&point.z<=bounds.max.z+tolerance;
        }
        static void Wall(Builder b,Vertex a,Vertex c,Vertex ia,Vertex ic,int slot)
        { b.Triangle(c,a,ia,slot); b.Triangle(c,ia,ic,slot); }
        static float DistanceToTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
            Vector3 projected=p-normal*Vector3.Dot(p-a,normal);
            if(Vector3.Dot(Vector3.Cross(b-a,projected-a),normal)>=0&&
               Vector3.Dot(Vector3.Cross(c-b,projected-b),normal)>=0&&
               Vector3.Dot(Vector3.Cross(a-c,projected-c),normal)>=0)
                return Mathf.Abs(Vector3.Dot(p-a,normal));
            float result=float.PositiveInfinity;
            var triangle=new[]{a,b,c};
            for(int i=0;i<3;i++)
            { Vector3 edge=triangle[(i+1)%3]-triangle[i]; float t=Mathf.Clamp01(Vector3.Dot(p-triangle[i],edge)/Mathf.Max(1e-12f,edge.sqrMagnitude)); result=Mathf.Min(result,Vector3.Distance(p,triangle[i]+edge*t)); }
            return result;
        }
    }
}
