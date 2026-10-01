using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    // Runtime-only tiles retain their original triangles. Cuts are replayed only
    // on intersecting triangles, so overlapping excavations cannot fill old pits.
    public sealed class StoneTerrainCutout : IDisposable
    {
        private const float TileSize=64f;
        private readonly List<Tile> tiles=new();
        private readonly Dictionary<Vector2Int,List<Tile>> columns=new();
        private readonly Transform root;
        private bool batching;
        private readonly HashSet<Tile> dirty=new();
        private readonly List<RenderBatch> renderBatches=new();
        private readonly HashSet<RenderBatch> dirtyRenderBatches=new();
        public void BeginBatch() { batching=true; }
        public void EndBatch()
        {
            batching=false;
            LastRebuiltTileCount=0;
            foreach(var tile in dirty) Rebuild(tile);
            dirty.Clear();
            FlushRenderBatches();
        }
        public int LastRebuiltTileCount { get; private set; }
        public StoneTerrainCutout(Transform root) { this.root=root; }

        // Sample immutable source faces so later stone batches never settle into
        // cavities prepared by an earlier frame.
        public bool OriginalSurface(float x,float z,out RaycastHit hit)
        {
            hit=default;
            Vector3 start=root.InverseTransformPoint(new Vector3(x,600f,z));
            Vector3 direction=root.InverseTransformVector(Vector3.down);
            if(!columns.TryGetValue(new Vector2Int(Mathf.FloorToInt(start.x/TileSize),
                Mathf.FloorToInt(start.z/TileSize)),out var column)) return false;
            float closest=1800f;
            bool found=false;
            foreach(var tile in column)
            foreach(var face in tile.Original)
            {
                float denominator=Vector3.Dot(face.Normal,direction);
                if(denominator>=-0.000001f) continue;
                Vector3 localStart=start-tile.Origin;
                float distance=Vector3.Dot(face.Normal,face.Vertices[0]-localStart)/denominator;
                if(distance<0||distance>closest) continue;
                Vector3 point=localStart+direction*distance;
                bool inside=true;
                for(int i=0;i<face.Vertices.Count;i++)
                    if(Vector3.Dot(Vector3.Cross(face.Vertices[(i+1)%face.Vertices.Count]-face.Vertices[i],
                        point-face.Vertices[i]),face.Normal)<-0.00001f) { inside=false; break; }
                if(!inside) continue;
                closest=distance; found=true;
                hit.point=root.TransformPoint(point+tile.Origin);
                hit.normal=root.TransformDirection(face.Normal).normalized;
                hit.distance=distance;
            }
            return found;
        }

        // Collapse manifold edges rather than deleting triangles: the Boolean
        // operand must remain closed or the resulting pit can have missing faces.
        public static Mesh SimplifyCutter(Mesh source)
        {
            var points=new List<Vector3>(); var lookup=new Dictionary<Vector3,int>();
            var remap=new int[source.vertexCount]; var sourcePoints=source.vertices;
            for(int i=0;i<sourcePoints.Length;i++)
            {
                if(!lookup.TryGetValue(sourcePoints[i],out int index))
                { index=points.Count; lookup.Add(sourcePoints[i],index); points.Add(sourcePoints[i]); }
                remap[i]=index;
            }
            var faces=new List<Vector3Int>(); int[] triangles=source.triangles;
            for(int i=0;i<triangles.Length;i+=3)
                faces.Add(new Vector3Int(remap[triangles[i]],remap[triangles[i+1]],remap[triangles[i+2]]));
            int target=Mathf.Max(4,((faces.Count/2+1)/2)*2);
            while(faces.Count>target)
            {
                var edges=new HashSet<(int a,int b)>();
                foreach(var f in faces) for(int j=0;j<3;j++)
                { int a=f[j],b=f[(j+1)%3]; edges.Add(a<b?(a,b):(b,a)); }
                bool collapsed=false;
                foreach(var edge in edges.OrderBy(e=>(points[e.a]-points[e.b]).sqrMagnitude))
                {
                    var adjacentA=new HashSet<int>(); var adjacentB=new HashSet<int>();
                    int shared=0;
                    foreach(var f in faces)
                    {
                        bool a=f.x==edge.a||f.y==edge.a||f.z==edge.a;
                        bool b=f.x==edge.b||f.y==edge.b||f.z==edge.b;
                        if(a&&b) shared++;
                        for(int j=0;j<3;j++) { if(a) adjacentA.Add(f[j]); if(b) adjacentB.Add(f[j]); }
                    }
                    adjacentA.Remove(edge.a); adjacentA.Remove(edge.b);
                    adjacentB.Remove(edge.a); adjacentB.Remove(edge.b); adjacentA.IntersectWith(adjacentB);
                    if(shared!=2||adjacentA.Count!=2) continue;
                    Vector3 midpoint=(points[edge.a]+points[edge.b])*0.5f;
                    bool valid=true;
                    foreach(var f in faces)
                    {
                        bool a=f.x==edge.a||f.y==edge.a||f.z==edge.a;
                        bool b=f.x==edge.b||f.y==edge.b||f.z==edge.b;
                        if((!a&&!b)||(a&&b)) continue;
                        Vector3 p=f.x==edge.a||f.x==edge.b?midpoint:points[f.x];
                        Vector3 q=f.y==edge.a||f.y==edge.b?midpoint:points[f.y];
                        Vector3 r=f.z==edge.a||f.z==edge.b?midpoint:points[f.z];
                        Vector3 oldNormal=Vector3.Cross(points[f.y]-points[f.x],points[f.z]-points[f.x]);
                        Vector3 newNormal=Vector3.Cross(q-p,r-p);
                        if(newNormal.sqrMagnitude<1e-20f||Vector3.Dot(oldNormal,newNormal)<=0f) { valid=false; break; }
                    }
                    if(!valid) continue;
                    for(int i=faces.Count-1;i>=0;i--)
                    {
                        var f=faces[i]; for(int j=0;j<3;j++) if(f[j]==edge.b) f[j]=edge.a;
                        if(f.x==f.y||f.y==f.z||f.z==f.x) faces.RemoveAt(i); else faces[i]=f;
                    }
                    points[edge.a]=midpoint; collapsed=true; break;
                }
                if(!collapsed) break;
            }
            var mesh=new Mesh{name="Stone pit cutter (50% faces)"};
            mesh.SetVertices(points); mesh.SetTriangles(faces.SelectMany(f=>new[]{f.x,f.y,f.z}).ToArray(),0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class Tile
        {
            public Vector3 Origin;
            public Bounds Bounds;
            public List<Face> Original;
            public readonly List<Cut> Cuts=new();
            public Mesh Mesh;
            public MeshCollider Collider;
            public RenderBatch Rendering;
        }

        private sealed class RenderBatch
        {
            public Vector3 Origin;
            public readonly List<Tile> Tiles=new();
            public MeshFilter Filter;
            public Mesh Mesh;
        }

        private void FlushRenderBatches()
        {
            foreach(var batch in dirtyRenderBatches)
            {
                var combine=new CombineInstance[batch.Tiles.Count];
                for(int i=0;i<combine.Length;i++)
                    combine[i]=new CombineInstance{mesh=batch.Tiles[i].Mesh,
                        transform=Matrix4x4.Translate(batch.Tiles[i].Origin-batch.Origin)};
                var mesh=new Mesh{name="Batched terrain surface",indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(combine,true,true);
                batch.Filter.sharedMesh=mesh;
                if(batch.Mesh!=null) DestroyMesh(batch.Mesh);
                batch.Mesh=mesh;
            }
            dirtyRenderBatches.Clear();
        }

        private sealed class Cut
        {
            public List<Face> Faces;
            public Bounds Bounds;
        }
        private sealed class Region
        {
            public Face Face;
            public List<Cut> Cuts=new();
        }

        // Split at tile boundaries without changing the original surface planes.
        public void AddSurface(Mesh source,Material material,string name,Action<MeshCollider> register)
        {
            if(material!=null) material.SetFloat("_UseTerrainPitColor",1f);
            var groups=new Dictionary<Vector2Int,List<Face>>();
            Vector3[] vertices=source.vertices;
            int[] indices=source.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                var triangle=new Face(new[]{vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]]});
                if(!triangle.Valid) continue;
                Bounds bounds=triangle.Bounds;
                int minX=Mathf.FloorToInt(bounds.min.x/TileSize),maxX=Mathf.FloorToInt(bounds.max.x/TileSize);
                int minZ=Mathf.FloorToInt(bounds.min.z/TileSize),maxZ=Mathf.FloorToInt(bounds.max.z/TileSize);
                for(int x=minX;x<=maxX;x++)
                for(int z=minZ;z<=maxZ;z++)
                {
                    Face clipped=KeepBehind(triangle,Vector3.left,-x*TileSize);
                    clipped=KeepBehind(clipped,Vector3.right,(x+1)*TileSize);
                    clipped=KeepBehind(clipped,Vector3.back,-z*TileSize);
                    clipped=KeepBehind(clipped,Vector3.forward,(z+1)*TileSize);
                    if(clipped==null) continue;
                    var key=new Vector2Int(x,z);
                    if(!groups.TryGetValue(key,out var faces)) groups[key]=faces=new List<Face>();
                    faces.Add(clipped);
                }
            }
            // Share a renderer across sixteen collision tiles. Collision and
            // Boolean edits remain local; only the touched render batch is uploaded.
            var batches=new Dictionary<Vector2Int,RenderBatch>();
            foreach(var pair in groups)
            {
                Vector3 origin=new((pair.Key.x+0.5f)*TileSize,0f,(pair.Key.y+0.5f)*TileSize);
                var faces=pair.Value.Select(f=>new Face(f.Vertices.Select(v=>v-origin))).ToList();
                Mesh mesh=ToMesh(faces,name+" tile");
                var go=new GameObject(name+" ["+pair.Key.x+","+pair.Key.y+"]");
                go.transform.SetParent(root,false);
                go.transform.localPosition=origin;
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var collider=go.AddComponent<MeshCollider>();
                collider.cookingOptions&=~MeshColliderCookingOptions.UseFastMidphase;
                collider.sharedMesh=mesh;
                var bounds=mesh.bounds; bounds.center+=origin;
                var tile=new Tile{Origin=origin,Bounds=bounds,Original=faces,Mesh=mesh,Collider=collider};
                var batchKey=new Vector2Int(Mathf.FloorToInt(pair.Key.x/4f),Mathf.FloorToInt(pair.Key.y/4f));
                if(!batches.TryGetValue(batchKey,out var batch))
                {
                    var renderObject=new GameObject("Terrain render batch "+name+" "+batchKey);
                    renderObject.transform.SetParent(root,false);
                    var renderOrigin=new Vector3(batchKey.x*TileSize*4,0,batchKey.y*TileSize*4);
                    renderObject.transform.localPosition=renderOrigin;
                    batch=new RenderBatch{Origin=renderOrigin,Filter=renderObject.AddComponent<MeshFilter>()};
                    renderObject.AddComponent<MeshRenderer>().sharedMaterial=material;
                    batches.Add(batchKey,batch); renderBatches.Add(batch);
                }
                batch.Tiles.Add(tile); tile.Rendering=batch; dirtyRenderBatches.Add(batch);
                tiles.Add(tile);
                if(!columns.TryGetValue(pair.Key,out var column)) columns[pair.Key]=column=new List<Tile>();
                column.Add(tile);
                register(collider);
                PhysicalDestructible.Ensure(go);
            }
            FlushRenderBatches();
        }

        // Height queries visit only the column under the ray, rather than every
        // tile across all islands. Fall back for a tilted map or nonvertical ray.
        public bool Raycast(Ray ray,float distance,out RaycastHit hit)
        {
            hit=default;
            float nearest=distance;
            bool found=false;
            Vector3 direction=root.InverseTransformVector(ray.direction);
            if(Mathf.Abs(direction.x)>0.000001f||Mathf.Abs(direction.z)>0.000001f)
            {
                foreach(var tile in tiles) Visit(tile,ray,ref nearest,ref hit,ref found);
                return found;
            }
            Vector3 point=root.InverseTransformPoint(ray.origin);
            int minX=Mathf.FloorToInt((point.x-0.001f)/TileSize),maxX=Mathf.FloorToInt((point.x+0.001f)/TileSize);
            int minZ=Mathf.FloorToInt((point.z-0.001f)/TileSize),maxZ=Mathf.FloorToInt((point.z+0.001f)/TileSize);
            for(int x=minX;x<=maxX;x++)
            for(int z=minZ;z<=maxZ;z++)
                if(columns.TryGetValue(new Vector2Int(x,z),out var column))
                    foreach(var tile in column) Visit(tile,ray,ref nearest,ref hit,ref found);
            return found;
        }

        private static void Visit(Tile tile,Ray ray,ref float nearest,ref RaycastHit hit,ref bool found)
        {
            if(!tile.Collider.Raycast(ray,out var candidate,nearest)) return;
            hit=candidate; nearest=candidate.distance; found=true;
        }

        public void Subtract(Mesh stone,Matrix4x4 stoneToWorld)
        {
            LastRebuiltTileCount=0;
            if(stone==null||!stone.isReadable) return;
            Matrix4x4 toMap=root.worldToLocalMatrix*stoneToWorld;
            Vector3[] points=stone.vertices.Select(toMap.MultiplyPoint3x4).ToArray();
            if(points.Length==0) return;
            var bounds=new Bounds(points[0],Vector3.zero);
            foreach(var p in points) bounds.Encapsulate(p);
            int[] indices=stone.triangles;
            var candidates=new List<Tile>();
            for(int x=Mathf.FloorToInt(bounds.min.x/TileSize);x<=Mathf.FloorToInt(bounds.max.x/TileSize);x++)
            for(int z=Mathf.FloorToInt(bounds.min.z/TileSize);z<=Mathf.FloorToInt(bounds.max.z/TileSize);z++)
                if(columns.TryGetValue(new Vector2Int(x,z),out var column)) candidates.AddRange(column);
            foreach(Tile tile in candidates)
            {
                if(!tile.Bounds.Intersects(bounds)) continue;
                var cut=new List<Face>();
                bool mirrored=toMap.determinant<0f;
                for(int i=0;i<indices.Length;i+=3)
                {
                    var face=new Face(new[]{points[indices[i]]-tile.Origin,
                        points[indices[i+(mirrored?2:1)]]-tile.Origin,
                        points[indices[i+(mirrored?1:2)]]-tile.Origin},true,true);
                    if(face.Valid) cut.Add(face);
                }
                if(cut.Count==0) continue;
                var localBounds=bounds; localBounds.center-=tile.Origin;
                if(!tile.Original.Any(f=>f.Normal.y>0.001f&&f.Bounds.Intersects(localBounds))) continue;
                tile.Cuts.Add(new Cut{Faces=cut,Bounds=localBounds});
                if(batching) dirty.Add(tile);
                else Rebuild(tile);
            }
            if(!batching) FlushRenderBatches();
        }

        private void Rebuild(Tile tile)
        {
                var rebuilt=new List<Face>();
                foreach(var face in tile.Original)
                {
                    var relevant=tile.Cuts.Where(c=>c.Bounds.Intersects(face.Bounds)).ToList();
                    if(face.Normal.y<=0.001f||relevant.Count==0) { rebuilt.Add(face); continue; }
                    // Isolate each small footprint before Boolean clipping. Large
                    // terrain triangles otherwise produce extremely thin slivers
                    // and every unrelated stone fragments the same BSP tree.
                    foreach(var region in Partition(face,relevant))
                    {
                        if(region.Cuts.Count==0) { rebuilt.Add(region.Face); continue; }
                        float bottom=Mathf.Min(region.Face.Bounds.min.y,region.Cuts.Min(c=>c.Bounds.min.y))-1f;
                        List<Face> solid=Prism(region.Face,bottom);
                        foreach(var cutter in region.Cuts) solid=Difference(solid,cutter.Faces);
                        rebuilt.AddRange(solid.Where(f=>f.Visible));
                    }
                }
                Mesh replacement=ToMesh(rebuilt,tile.Mesh.name);
                tile.Collider.sharedMesh=null;
                tile.Collider.GetComponent<MeshFilter>().sharedMesh=replacement;
                tile.Collider.sharedMesh=replacement;
                DestroyMesh(tile.Mesh);
                tile.Mesh=replacement;
                dirtyRenderBatches.Add(tile.Rendering);
                LastRebuiltTileCount++;
        }

        private static List<Region> Partition(Face face,List<Cut> cuts)
        {
            var regions=new List<Region>{new Region{Face=face}};
            // Keep overlapping stones in one Boolean solid. Splitting through
            // an existing cavity introduces artificial coplanar interior walls.
            var groups=new List<List<Cut>>();
            foreach(var cut in cuts)
            {
                var group=new List<Cut>{cut};
                Bounds combined=cut.Bounds; combined.Expand(0.002f);
                for(int i=0;i<groups.Count;)
                {
                    Bounds other=groups[i][0].Bounds;
                    foreach(var member in groups[i]) other.Encapsulate(member.Bounds);
                    other.Expand(0.002f);
                    if(combined.min.x>other.max.x||combined.max.x<other.min.x||
                        combined.min.z>other.max.z||combined.max.z<other.min.z) { i++; continue; }
                    group.AddRange(groups[i]); combined.Encapsulate(other); groups.RemoveAt(i); i=0;
                }
                groups.Add(group);
            }
            foreach(var group in groups)
            {
                var next=new List<Region>();
                Bounds bounds=group[0].Bounds;
                foreach(var member in group) bounds.Encapsulate(member.Bounds);
                bounds.Expand(0.002f);
                foreach(var region in regions)
                {
                    if(!region.Face.Bounds.Intersects(bounds)) { next.Add(region); continue; }
                    Face inside=region.Face;
                    Vector3[] normals={Vector3.left,Vector3.right,Vector3.back,Vector3.forward};
                    float[] distances={-bounds.min.x,bounds.max.x,-bounds.min.z,bounds.max.z};
                    for(int i=0;i<4&&inside!=null;i++)
                    {
                        Face outside=KeepBehind(inside,-normals[i],-distances[i]);
                        if(outside!=null) next.Add(new Region{Face=outside,Cuts=region.Cuts});
                        inside=KeepBehind(inside,normals[i],distances[i]);
                    }
                    if(inside!=null)
                    {
                        var affected=new List<Cut>(region.Cuts);
                        affected.AddRange(group);
                        next.Add(new Region{Face=inside,Cuts=affected});
                    }
                }
                regions=next;
            }
            return regions;
        }

        private static Bounds BoundsOf(List<Face> faces)
        {
            Bounds bounds=faces[0].Bounds;
            foreach(var face in faces) bounds.Encapsulate(face.Bounds);
            return bounds;
        }

        private static List<Face> Prism(Face top,float bottom)
        {
            var result=new List<Face>{top.Clone()};
            var lower=top.Vertices.Select(v=>new Vector3(v.x,bottom,v.z)).ToList();
            result.Add(new Face(lower.AsEnumerable().Reverse(),false));
            for(int i=0;i<lower.Count;i++)
            {
                int j=(i+1)%lower.Count;
                result.Add(new Face(new[]{top.Vertices[i],lower[i],lower[j],top.Vertices[j]},false));
            }
            return result;
        }

        private static List<Face> Difference(List<Face> terrain,List<Face> stone)
        {
            // Work around the stone, not at the island's elevation. This keeps
            // sub-millimetre clipped facets stable on high terrain.
            Vector3 origin=BoundsOf(stone).center;
            var a=new Node(terrain.Select(f=>f.Translated(-origin)).ToList());
            var b=new Node(stone.Select(f=>f.Translated(-origin)).ToList());
            a.Invert(); a.ClipTo(b); b.ClipTo(a); b.Invert(); b.ClipTo(a); b.Invert();
            a.Build(b.All()); a.Invert();
            return a.All().Select(f=>f.Translated(origin)).ToList();
        }

        private static Face KeepBehind(Face face,Vector3 normal,float distance)
        {
            if(face==null) return null;
            var result=new List<Vector3>();
            for(int i=0;i<face.Vertices.Count;i++)
            {
                Vector3 a=face.Vertices[i],b=face.Vertices[(i+1)%face.Vertices.Count];
                float da=Vector3.Dot(normal,a)-distance,db=Vector3.Dot(normal,b)-distance;
                if(da<=0f) result.Add(a);
                if((da<0f&&db>0f)||(da>0f&&db<0f)) result.Add(Vector3.LerpUnclamped(a,b,da/(da-db)));
            }
            var clipped=new Face(result,face.Visible,face.Pit);
            return clipped.Valid?clipped:null;
        }

        private static Mesh ToMesh(List<Face> faces,string name)
        {
            var vertices=new List<Vector3>(); var indices=new List<int>();
            var normals=new List<Vector3>(); var colors=new List<Color>();
            foreach(var face in faces)
            for(int i=1;i+1<face.Vertices.Count;i++)
            {
                Vector3 a=face.Vertices[0],b=face.Vertices[i],c=face.Vertices[i+1];
                if(Vector3.Cross(b-a,c-a).sqrMagnitude<1e-20f) continue;
                int start=vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                for(int j=0;j<3;j++) { normals.Add(face.Normal); colors.Add(face.Pit?new Color(0.55f,0.55f,0.55f,1f):Color.white); }
                indices.Add(start); indices.Add(start+1); indices.Add(start+2);
            }
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices); mesh.SetTriangles(indices,0);
            mesh.SetNormals(normals); mesh.SetColors(colors); mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            foreach(var tile in tiles) DestroyMesh(tile.Mesh);
            foreach(var batch in renderBatches) if(batch.Mesh!=null) DestroyMesh(batch.Mesh);
            tiles.Clear(); columns.Clear(); renderBatches.Clear(); dirtyRenderBatches.Clear(); dirty.Clear();
        }
        private static void DestroyMesh(Mesh mesh)
        {
            if(Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }

        private sealed class Face
        {
            public readonly List<Vector3> Vertices;
            public Vector3 Normal;
            public float Distance;
            public readonly bool Visible;
            public readonly bool Pit;
            public bool Valid=>Normal.sqrMagnitude>0.5f;
            public Bounds Bounds
            {
                get { var b=new Bounds(Vertices[0],Vector3.zero); foreach(var v in Vertices) b.Encapsulate(v); return b; }
            }
            public Face(IEnumerable<Vector3> vertices,bool visible=true,bool pit=false)
            {
                Vertices=vertices.ToList(); Visible=visible; Pit=pit;
                Vector3 strongest=Vector3.zero;
                for(int i=1;i+1<Vertices.Count;i++)
                {
                    Vector3 cross=Vector3.Cross(Vertices[i]-Vertices[0],Vertices[i+1]-Vertices[0]);
                    if(cross.sqrMagnitude>strongest.sqrMagnitude) strongest=cross;
                }
                if(strongest.sqrMagnitude<1e-20f) return;
                Normal=strongest/Mathf.Sqrt(strongest.sqrMagnitude);
                Distance=Vector3.Dot(Normal,Vertices[0]);
            }
            public Face Clone()=>new(Vertices,Visible,Pit){Normal=Normal,Distance=Distance};
            public Face Translated(Vector3 offset)=>new(Vertices.Select(v=>v+offset),Visible,Pit){Normal=Normal,Distance=Distance+Vector3.Dot(Normal,offset)};
            public void Flip() { Vertices.Reverse(); Normal=-Normal; Distance=-Distance; }
        }

        // BSP clipping computes a mesh difference using the stone's real facets,
        // including concave faces; no sphere/bounds approximation of the cavity.
        private sealed class Node
        {
            private const float Epsilon=0.00001f;
            private Vector3 normal;
            private Vector3 planePoint;
            private bool hasPlane;
            private List<Face> faces=new();
            private Node front,back;
            public Node(List<Face> source) { Build(source); }
            public void Build(List<Face> source)
            {
                if(source.Count==0) return;
                int start=0;
                if(!hasPlane)
                {
                    normal=source[0].Normal; planePoint=source[0].Vertices[0]; hasPlane=true;
                    // Consume the plane-defining polygon explicitly. Rounding on
                    // tiny irregular facets must never send it into its own child.
                    faces.Add(source[0]); start=1;
                }
                var f=new List<Face>(); var b=new List<Face>();
                for(int i=start;i<source.Count;i++) Split(source[i],faces,faces,f,b);
                if(f.Count>0) { if(front==null) front=new Node(new List<Face>()); front.Build(f); }
                if(b.Count>0) { if(back==null) back=new Node(new List<Face>()); back.Build(b); }
            }
            public List<Face> All()
            {
                var result=new List<Face>(faces);
                if(front!=null) result.AddRange(front.All());
                if(back!=null) result.AddRange(back.All());
                return result;
            }
            public void Invert()
            {
                foreach(var face in faces) face.Flip(); normal=-normal;
                front?.Invert(); back?.Invert(); (front,back)=(back,front);
            }
            private List<Face> Clip(List<Face> source)
            {
                if(!hasPlane) return new List<Face>(source);
                var f=new List<Face>(); var b=new List<Face>();
                foreach(var face in source) Split(face,f,b,f,b);
                if(front!=null) f=front.Clip(f);
                if(back!=null) b=back.Clip(b); else b.Clear();
                f.AddRange(b); return f;
            }
            public void ClipTo(Node other)
            { faces=other.Clip(faces); front?.ClipTo(other); back?.ClipTo(other); }
            private void Split(Face face,List<Face> coplanarFront,List<Face> coplanarBack,
                List<Face> outFront,List<Face> outBack)
            {
                int type=0; var types=new int[face.Vertices.Count];
                for(int i=0;i<types.Length;i++)
                {
                    float d=Vector3.Dot(normal,face.Vertices[i]-planePoint);
                    types[i]=d>Epsilon?1:d< -Epsilon?2:0; type|=types[i];
                }
                if(type==0) { (Vector3.Dot(normal,face.Normal)>0f?coplanarFront:coplanarBack).Add(face); return; }
                if(type==1) { outFront.Add(face); return; }
                if(type==2) { outBack.Add(face); return; }
                var f=new List<Vector3>(); var b=new List<Vector3>();
                for(int i=0;i<types.Length;i++)
                {
                    int j=(i+1)%types.Length;
                    Vector3 vi=face.Vertices[i],vj=face.Vertices[j];
                    if(types[i]!=2) f.Add(vi);
                    if(types[i]!=1) b.Add(vi);
                    if((types[i]|types[j])!=3) continue;
                    float t=Vector3.Dot(normal,planePoint-vi)/Vector3.Dot(normal,vj-vi);
                    Vector3 v=Vector3.LerpUnclamped(vi,vj,t); f.Add(v); b.Add(v);
                }
                var ff=new Face(f,face.Visible,face.Pit); if(ff.Valid) { ff.Normal=face.Normal; outFront.Add(ff); }
                var bf=new Face(b,face.Visible,face.Pit); if(bf.Valid) { bf.Normal=face.Normal; outBack.Add(bf); }
            }
        }
    }
}
