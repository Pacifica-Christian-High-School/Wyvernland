using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    // Island geometry is divided into 100m tiles. Excavation prepares and
    // rebuilds one affected tile at a time, then releases its temporary faces.
    public sealed class StoneTerrainCutout : IDisposable
    {
        private readonly List<Surface> surfaces=new();
        private readonly Dictionary<Vector2Int,List<Surface>> surfaceCells=new();
        private readonly Transform root;
        private bool batching;
        private readonly HashSet<Surface> dirty=new();
        private const float IndexCellSize=32f;
        public int LastRebuiltSurfaceCount { get; private set; }
        public StoneTerrainCutout(Transform root) { this.root=root; }
        public void BeginBatch() => batching=true;

        // The browser export stores the deterministic pits as finished 100m
        // tiles. Cutter history is retained for original-height queries and
        // any subsequent cuts, without redoing Boolean work during loading.
        public void WriteBrowserPitTiles(Stream destination)
        {
            using var writer=new BinaryWriter(destination,System.Text.Encoding.UTF8,true);
            writer.Write(0x57504954); writer.Write(1); writer.Write(TerrainTiles.Size);
            var changed=surfaces.Where(s=>s.OwnsMesh).ToList();
            var cutters=new List<Cut>(); var ids=new Dictionary<Cut,int>();
            foreach(Surface surface in changed)
                foreach(Cut cut in surface.Cuts)
                    if(!ids.ContainsKey(cut)) { ids.Add(cut,cutters.Count);cutters.Add(cut); }
            writer.Write(cutters.Count);
            foreach(Cut cut in cutters)
            {
                WriteVector(writer,cut.Bounds.center);WriteVector(writer,cut.Bounds.size);
                writer.Write(cut.Points.Length);
                foreach(Vector3 point in cut.Points) WriteVector(writer,point);
                writer.Write(cut.Triangles.Length);
                foreach(int index in cut.Triangles) writer.Write(index);
            }
            writer.Write(changed.Count);
            foreach(Surface surface in changed)
            {
                writer.Write(surface.OriginalMesh.name);
                Vector3[] points=surface.DrawMesh.vertices;
                Color[] colors=surface.DrawMesh.colors;
                int[] triangles=surface.DrawMesh.triangles;
                writer.Write(points.Length);
                for(int i=0;i<points.Length;i++)
                { WriteVector(writer,points[i]);writer.Write((byte)(colors[i].r<.9f?1:0)); }
                writer.Write(triangles.Length);
                foreach(int index in triangles) writer.Write(index);
                writer.Write(surface.Cuts.Count);
                foreach(Cut cut in surface.Cuts) writer.Write(ids[cut]);
            }
        }

        internal System.Collections.IEnumerator ReadBrowserPitTiles(Stream source)
        {
            using var reader=new BinaryReader(source,System.Text.Encoding.UTF8,true);
            if(reader.ReadInt32()!=0x57504954||reader.ReadInt32()!=1||reader.ReadSingle()!=TerrainTiles.Size)
                throw new InvalidDataException("Invalid browser terrain cache. Rebuild the browser game.");
            double batchStarted=Time.realtimeSinceStartupAsDouble;
            var cutters=new Cut[reader.ReadInt32()];
            for(int i=0;i<cutters.Length;i++)
            {
                var cut=new Cut { Bounds=new Bounds(ReadVector(reader),ReadVector(reader)) };
                cut.Points=new Vector3[reader.ReadInt32()];
                for(int j=0;j<cut.Points.Length;j++) cut.Points[j]=ReadVector(reader);
                cut.Triangles=new int[reader.ReadInt32()];
                for(int j=0;j<cut.Triangles.Length;j++) cut.Triangles[j]=reader.ReadInt32();
                cutters[i]=cut;
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
            }
            var byName=surfaces.ToDictionary(s=>s.OriginalMesh.name);
            int total=reader.ReadInt32(); LastRebuiltSurfaceCount=0;
            for(int i=0;i<total;i++)
            {
                string name=reader.ReadString();
                if(!byName.TryGetValue(name,out Surface surface))
                    throw new InvalidDataException($"Browser terrain tile {name} does not match this map.");
                var points=new Vector3[reader.ReadInt32()];var colors=new Color[points.Length];
                for(int j=0;j<points.Length;j++)
                { points[j]=ReadVector(reader);colors[j]=reader.ReadByte()==0?Color.white:new Color(.55f,.55f,.55f,1); }
                var indices=new int[reader.ReadInt32()];
                for(int j=0;j<indices.Length;j++) indices[j]=reader.ReadInt32();
                int count=reader.ReadInt32();
                for(int j=0;j<count;j++)
                {
                    Cut cut=cutters[reader.ReadInt32()];surface.Cuts.Add(cut);Index(surface.CutCells,cut.Bounds,cut);
                }
                var mesh=new Mesh { name=name+" browser pits",indexFormat=IndexFormat.UInt32 };
                mesh.vertices=points;mesh.colors=colors;mesh.triangles=indices;
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                // Shared draw vertices also describe the exact collision solid.
                // The terrain shader derives flat normals from each triangle.
                ReplaceMeshes(surface,mesh,mesh);
                LastRebuiltSurfaceCount++;WorldLoading.ReportTerrainTiles(i+1,total);
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
            }
            if(source.Position!=source.Length) throw new InvalidDataException("Unexpected browser terrain cache data.");
        }

        static void WriteVector(BinaryWriter writer,Vector3 value)
        { writer.Write(value.x);writer.Write(value.y);writer.Write(value.z); }
        static Vector3 ReadVector(BinaryReader reader) => new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
        public void EndBatch()
        {
            var routine=EndBatchIncrementally();
            while(routine.MoveNext()) { }
        }

        internal System.Collections.IEnumerator EndBatchIncrementally()
        {
            batching=false;
            LastRebuiltSurfaceCount=0;
            int completed=0,total=dirty.Count;
            foreach(Surface surface in dirty)
            {
                var rebuild=RebuildIncrementally(surface);
                while(rebuild.MoveNext()) yield return rebuild.Current;
                rebuild=null;
                ReleaseTemporaryFaces(surface);
                WorldLoading.ReportTerrainTiles(++completed,total);
                // Let deferred mesh destruction and WebGL collection finish
                // before allocating preparation geometry for the next tile.
                yield return null;
            }
            dirty.Clear();
        }

        internal System.Collections.IEnumerator PrepareSurfacesIncrementally()
        {
            foreach(Surface surface in surfaces)
            {
                if(surface.Original!=null) continue;
                var preparation=PrepareSurfaceIncrementally(surface);
                while(preparation.MoveNext()) yield return preparation.Current;
            }
        }

        private System.Collections.IEnumerator PrepareSurfaceIncrementally(Surface surface)
        {
            Vector3[] vertices=surface.OriginalMesh.vertices;
            int[] triangles=surface.OriginalMesh.triangles;
            surface.Original=new List<Face>(triangles.Length/3);
            double batchStarted=Time.realtimeSinceStartupAsDouble;
            for(int i=0;i<triangles.Length;i+=3)
            {
                Face face=new(new[]{vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]]});
                if(face.Valid)
                {
                    surface.Original.Add(face);
                    if(face.Normal.y>0.001f) Index(surface.OriginalCells,face.Bounds,face);
                }
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
            }
            foreach (Cut cut in surface.Cuts)
                foreach (Face face in Query(surface.OriginalCells,cut.Bounds))
                    if (face.Bounds.Intersects(cut.Bounds)) surface.AffectedFaces.Add(face);
        }

        private static void Index<T>(Dictionary<Vector2Int,List<T>> cells,Bounds bounds,T item,float cellSize=IndexCellSize)
        {
            for(int z=Mathf.FloorToInt(bounds.min.z/cellSize);z<=Mathf.FloorToInt(bounds.max.z/cellSize);z++)
            for(int x=Mathf.FloorToInt(bounds.min.x/cellSize);x<=Mathf.FloorToInt(bounds.max.x/cellSize);x++)
            {
                var cell=new Vector2Int(x,z);
                if(!cells.TryGetValue(cell,out var items)) cells[cell]=items=new List<T>();
                items.Add(item);
            }
        }

        private static IEnumerable<T> Query<T>(Dictionary<Vector2Int,List<T>> cells,Bounds bounds,float cellSize=IndexCellSize)
        {
            var seen=new HashSet<T>();
            for(int z=Mathf.FloorToInt(bounds.min.z/cellSize);z<=Mathf.FloorToInt(bounds.max.z/cellSize);z++)
            for(int x=Mathf.FloorToInt(bounds.min.x/cellSize);x<=Mathf.FloorToInt(bounds.max.x/cellSize);x++)
                if(cells.TryGetValue(new Vector2Int(x,z),out var items))
                    foreach(T item in items) if(seen.Add(item)) yield return item;
        }

        public bool OriginalSurface(float x,float z,out RaycastHit hit)
        {
            Ray ray=new(new Vector3(x,600f,z),Vector3.down);
            hit=default;
            float nearest=1800f;
            bool found=false;
            Vector3 start=root.InverseTransformPoint(ray.origin);
            Vector3 direction=root.InverseTransformVector(ray.direction);
            var rayBounds=new Bounds(start,Vector3.zero); rayBounds.Encapsulate(start+direction*nearest);
            foreach(Surface surface in Query(surfaceCells,rayBounds,TerrainTiles.Size))
            {
                if(surface.Cuts.Count==0)
                {
                    if(!surface.Collider.Raycast(ray,out var candidate,nearest)) continue;
                    hit=candidate; nearest=candidate.distance; found=true;
                    continue;
                }
                // Original placement queries keep working after temporary pit
                // faces are released, without retaining millions of Face objects.
                Vector3[] vertices=surface.OriginalMesh.vertices;
                int[] indices=surface.OriginalMesh.triangles;
                for (int triangle=0; triangle<indices.Length; triangle+=3)
                {
                    Face face=new(new[]{vertices[indices[triangle]],vertices[indices[triangle+1]],vertices[indices[triangle+2]]});
                    float denominator=Vector3.Dot(face.Normal,direction);
                    if(denominator>=-0.000001f) continue;
                    float distance=Vector3.Dot(face.Normal,face.Vertices[0]-start)/denominator;
                    if(distance<0||distance>=nearest) continue;
                    Vector3 point=start+direction*distance;
                    bool inside=true;
                    for(int i=0;i<face.Vertices.Count;i++)
                        if(Vector3.Dot(Vector3.Cross(face.Vertices[(i+1)%face.Vertices.Count]-face.Vertices[i],
                            point-face.Vertices[i]),face.Normal)<-0.00001f) { inside=false; break; }
                    if(!inside) continue;
                    nearest=distance; found=true;
                    hit.point=root.TransformPoint(point);
                    hit.normal=root.TransformDirection(face.Normal).normalized;
                    hit.distance=distance;
                }
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
            var active=new bool[faces.Count];
            var incident=new HashSet<int>[points.Count];
            for(int i=0;i<incident.Length;i++) incident[i]=new HashSet<int>();
            for(int i=0;i<faces.Count;i++)
            {
                active[i]=true;
                for(int j=0;j<3;j++) incident[faces[i][j]].Add(i);
            }
            var edges=new CutterEdgeHeap();
            var versions=new Dictionary<(int a,int b),int>();
            int nextVersion=0;
            void Schedule(IEnumerable<int> vertices)
            {
                var changed=new HashSet<(int a,int b)>();
                foreach(int vertex in vertices)
                    foreach(int index in incident[vertex])
                    {
                        var f=faces[index];
                        for(int j=0;j<3;j++)
                        {
                            int a=f[j],b=f[(j+1)%3];
                            if(a!=b) changed.Add(a<b?(a,b):(b,a));
                        }
                    }
                foreach(var edge in changed)
                {
                    int version=++nextVersion;
                    versions[edge]=version;
                    edges.Push(new CutterEdge(edge,(points[edge.a]-points[edge.b]).sqrMagnitude,version));
                }
            }
            Schedule(Enumerable.Range(0,points.Count));
            Bounds original=source.bounds;
            var anchors=new bool[points.Count];
            for(int axis=0;axis<3;axis++)
            {
                int low=0,high=0;
                for(int i=1;i<points.Count;i++)
                { if(points[i][axis]<points[low][axis]) low=i;if(points[i][axis]>points[high][axis]) high=i; }
                anchors[low]=anchors[high]=true;
            }
            int remaining=faces.Count;
            // A quarter of the original faces is sufficient for a buried pit.
            // Keep 12 faces for small solids and cap large operands at 64.
            int target=Mathf.Clamp(Mathf.CeilToInt(faces.Count/8f)*2,12,64);
            while(remaining>target&&edges.Count>0)
            {
                CutterEdge candidate=edges.Pop();
                var edge=candidate.Edge;
                if(!versions.TryGetValue(edge,out int current)||current!=candidate.Version) continue;
                var adjacentA=new HashSet<int>(); var adjacentB=new HashSet<int>();
                int shared=0;
                foreach(int index in incident[edge.a])
                {
                    var f=faces[index];
                    if(incident[edge.b].Contains(index)) shared++;
                    for(int j=0;j<3;j++) adjacentA.Add(f[j]);
                }
                foreach(int index in incident[edge.b])
                    for(int j=0;j<3;j++) adjacentB.Add(faces[index][j]);
                adjacentA.Remove(edge.a); adjacentA.Remove(edge.b);
                adjacentB.Remove(edge.a); adjacentB.Remove(edge.b); adjacentA.IntersectWith(adjacentB);
                if(shared!=2||adjacentA.Count!=2) continue;
                Vector3 midpoint=(points[edge.a]+points[edge.b])*0.5f;
                bool anchorA=anchors[edge.a],anchorB=anchors[edge.b];
                if(anchorA&&anchorB) continue;
                if(anchorA) midpoint=points[edge.a];
                else if(anchorB) midpoint=points[edge.b];
                var touched=new HashSet<int>(incident[edge.a]); touched.UnionWith(incident[edge.b]);
                bool valid=true;
                foreach(int index in touched)
                {
                    if(incident[edge.a].Contains(index)&&incident[edge.b].Contains(index)) continue;
                    var f=faces[index];
                    Vector3 p=f.x==edge.a||f.x==edge.b?midpoint:points[f.x];
                    Vector3 q=f.y==edge.a||f.y==edge.b?midpoint:points[f.y];
                    Vector3 r=f.z==edge.a||f.z==edge.b?midpoint:points[f.z];
                    Vector3 oldNormal=Vector3.Cross(points[f.y]-points[f.x],points[f.z]-points[f.x]);
                    Vector3 newNormal=Vector3.Cross(q-p,r-p);
                    if(newNormal.sqrMagnitude<1e-20f||Vector3.Dot(oldNormal,newNormal)<=0f) { valid=false; break; }
                }
                if(!valid) continue;
                var changedVertices=new HashSet<int>();
                foreach(int index in touched)
                {
                    var f=faces[index];
                    for(int j=0;j<3;j++) { incident[f[j]].Remove(index);changedVertices.Add(f[j]); }
                    for(int j=0;j<3;j++) if(f[j]==edge.b) f[j]=edge.a;
                    if(f.x==f.y||f.y==f.z||f.z==f.x) { active[index]=false;remaining--; }
                    else
                    {
                        faces[index]=f;
                        for(int j=0;j<3;j++) incident[f[j]].Add(index);
                    }
                }
                points[edge.a]=midpoint;
                anchors[edge.a]=anchorA||anchorB;anchors[edge.b]=false;
                // Only edges around the changed faces need a new priority or
                // topology check. Old heap entries are discarded by version.
                Schedule(changedVertices);
            }
            var mesh=new Mesh{name="Simple stone pit cutter"};
            var output=new List<int>(remaining*3);
            var compact=new List<Vector3>();var compactIds=new Dictionary<int,int>();
            int Compact(int vertex)
            {
                if(!compactIds.TryGetValue(vertex,out int index))
                { index=compact.Count;compactIds.Add(vertex,index);compact.Add(points[vertex]); }
                return index;
            }
            for(int i=0;i<faces.Count;i++) if(active[i])
            { output.Add(Compact(faces[i].x));output.Add(Compact(faces[i].y));output.Add(Compact(faces[i].z)); }
            if(compact.Count==0) return mesh;
            // Extremal vertices were anchored during collapse, preserving the
            // footprint and depth without stretching the remaining facets.
            mesh.SetVertices(compact); mesh.SetTriangles(output,0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private readonly struct CutterEdge
        {
            public readonly (int a,int b) Edge;
            public readonly float Length;
            public readonly int Version;
            public CutterEdge((int a,int b) edge,float length,int version)
            { Edge=edge;Length=length;Version=version; }
        }

        private sealed class CutterEdgeHeap
        {
            private readonly List<CutterEdge> entries=new();
            public int Count=>entries.Count;
            private static bool Before(CutterEdge a,CutterEdge b) =>
                a.Length<b.Length||a.Length==b.Length&&a.Version<b.Version;
            public void Push(CutterEdge entry)
            {
                int index=entries.Count;
                entries.Add(entry);
                while(index>0)
                {
                    int parent=(index-1)/2;
                    if(!Before(entry,entries[parent])) break;
                    entries[index]=entries[parent];index=parent;
                }
                entries[index]=entry;
            }
            public CutterEdge Pop()
            {
                CutterEdge result=entries[0],last=entries[entries.Count-1];
                entries.RemoveAt(entries.Count-1);
                if(entries.Count==0) return result;
                int index=0;
                while(index*2+1<entries.Count)
                {
                    int child=index*2+1;
                    if(child+1<entries.Count&&Before(entries[child+1],entries[child])) child++;
                    if(!Before(entries[child],last)) break;
                    entries[index]=entries[child];index=child;
                }
                entries[index]=last;
                return result;
            }
        }

        private sealed class Surface
        {
            public Bounds Bounds;
            public List<Face> Original;
            public readonly List<Cut> Cuts=new();
            public readonly Dictionary<Vector2Int,List<Face>> OriginalCells=new();
            public readonly Dictionary<Vector2Int,List<Cut>> CutCells=new();
            public readonly HashSet<Face> AffectedFaces=new();
            public Mesh Mesh;
            public Mesh OriginalMesh;
            public bool OwnsOriginalMesh;
            public Mesh DrawMesh;
            public bool OwnsMesh;
            public MeshFilter Filter;
            public MeshCollider Collider;
        }
        private sealed class Cut
        {
            public Vector3[] Points;
            public int[] Triangles;
            public Bounds Bounds;
            private List<Face> faces;
            public List<Face> Faces
            {
                get
                {
                    if(faces!=null) return faces;
                    faces=new List<Face>(Triangles.Length/3);
                    for(int i=0;i<Triangles.Length;i+=3)
                    {
                        Face face=new(new[]{Points[Triangles[i]],Points[Triangles[i+1]],Points[Triangles[i+2]]},true,true);
                        if(face.Valid) faces.Add(face);
                    }
                    return faces;
                }
            }
            public void ReleaseFaces()=>faces=null;
        }

        private static void ReleaseTemporaryFaces(Surface surface)
        {
            surface.Original=null;
            surface.OriginalCells.Clear();
            surface.AffectedFaces.Clear();
            foreach(Cut cut in surface.Cuts) cut.ReleaseFaces();
        }
        private sealed class Region
        {
            public Face Face;
            public List<Cut> Cuts=new();
        }

        public void AddSurface(Mesh source,Material material,string name,Action<MeshCollider> register)
        {
            if(material!=null)
            {
                material.SetFloat("_UseTerrainPitColor",1f);
                material.SetFloat("_FlatTerrainNormal",1f);
            }
            GameObject go=new(name);
            go.transform.SetParent(root,false);
            // Adding a MeshCollider after a MeshFilter automatically cooks its
            // current mesh. Configure first so the multi-million-face island is
            // cooked once, using the intended source and midphase settings.
            var collider=go.AddComponent<MeshCollider>();
            var filter=go.AddComponent<MeshFilter>();
            Mesh drawMesh=TerrainDrawMesh.Build(source);
            filter.sharedMesh=drawMesh;
            var renderer=go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            // PhysX's fast midphase has a documented triangle limit. The whole
            // main island exceeds it, so use the compatible BVH for that mesh.
            ulong triangles=0;
            for(int slot=0;slot<source.subMeshCount;slot++) triangles+=source.GetIndexCount(slot)/3;
            if(triangles>2097152)
                collider.cookingOptions&=~MeshColliderCookingOptions.UseFastMidphase;
            collider.sharedMesh=source;
            var surface=new Surface{Bounds=source.bounds,Mesh=source,OriginalMesh=source,DrawMesh=drawMesh,Filter=filter,Collider=collider};
            surfaces.Add(surface);
            Index(surfaceCells,surface.Bounds,surface,TerrainTiles.Size);
            register(collider);
            PhysicalDestructible.Ensure(go);
            PlayerWorldLoading.NotifyChanged(go);
        }

        public System.Collections.IEnumerator AddTiledSurfaceIncrementally(Mesh source,Material material,string name,Action<MeshCollider> register)
        {
            var split=TerrainTiles.Split(source,(mesh,cell)=>
            {
                AddSurface(mesh,material,$"{name} tile {cell.x},{cell.y}",register);
                surfaces[surfaces.Count-1].OwnsOriginalMesh=true;
            });
            while(split.MoveNext()) yield return split.Current;
        }

        public void DrawSurfaces(CommandBuffer commands,Material material)
        {
            foreach(Surface surface in surfaces)
                commands.DrawMesh(surface.Mesh,root.localToWorldMatrix,material);
        }

        public bool Raycast(Ray ray,float distance,out RaycastHit hit)
        {
            hit=default;
            bool found=false;
            Vector3 start=root.InverseTransformPoint(ray.origin);
            Vector3 direction=root.InverseTransformVector(ray.direction);
            var bounds=new Bounds(start,Vector3.zero); bounds.Encapsulate(start+direction*distance);
            foreach(Surface surface in Query(surfaceCells,bounds,TerrainTiles.Size))
                if(surface.Collider.Raycast(ray,out var candidate,distance))
                { hit=candidate; distance=candidate.distance; found=true; }
            return found;
        }

        public void Subtract(Mesh stone,Matrix4x4 stoneToWorld)
        {
            LastRebuiltSurfaceCount=0;
            if(stone==null||!stone.isReadable) return;
            Matrix4x4 toMap=root.worldToLocalMatrix*stoneToWorld;
            Vector3[] points=stone.vertices.Select(toMap.MultiplyPoint3x4).ToArray();
            if(points.Length==0) return;
            int[] indices=stone.triangles;
            if(indices.Length<3) return;
            Bounds bounds=new(points[indices[0]],Vector3.zero);
            foreach(int index in indices) bounds.Encapsulate(points[index]);
            bool valid=false;
            bool mirrored=toMap.determinant<0f;
            for(int i=0;i<indices.Length;i+=3)
            {
                if(mirrored) (indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);
                if(Vector3.Cross(points[indices[i+1]]-points[indices[i]],points[indices[i+2]]-points[indices[i]]).sqrMagnitude>=1e-20f)
                    valid=true;
            }
            if(!valid) return;
            // Preserve a compact cutter history. Polygon lists are expanded
            // lazily for the active tile, then released after its rebuild.
            var cutter=new Cut{Points=points,Triangles=indices,Bounds=bounds};
            foreach(Surface surface in Query(surfaceCells,bounds,TerrainTiles.Size))
            {
                if(!surface.Bounds.Intersects(bounds)) continue;
                if (batching)
                {
                    // Queue cutters without allocating this tile's original
                    // faces. EndBatch processes and releases one tile at a time.
                    surface.Cuts.Add(cutter);
                    Index(surface.CutCells,bounds,cutter);
                    dirty.Add(surface);
                    continue;
                }
                if(surface.Original==null)
                {
                    var preparation=PrepareSurfaceIncrementally(surface);
                    while(preparation.MoveNext()) { }
                }
                var touched=Query(surface.OriginalCells,bounds).Where(face=>face.Bounds.Intersects(bounds)).ToList();
                if(touched.Count==0) { ReleaseTemporaryFaces(surface);continue; }
                surface.Cuts.Add(cutter);
                Index(surface.CutCells,bounds,cutter);
                foreach(Face face in touched) surface.AffectedFaces.Add(face);
                if(batching) dirty.Add(surface); else Rebuild(surface);
            }
        }

        private void Rebuild(Surface surface)
        {
            var routine=RebuildIncrementally(surface);
            while(routine.MoveNext()) { }
            ReleaseTemporaryFaces(surface);
        }

        private System.Collections.IEnumerator RebuildIncrementally(Surface surface)
        {
            if (surface.Original==null)
            {
                var prepare=PrepareSurfaceIncrementally(surface);
                while(prepare.MoveNext()) yield return prepare.Current;
            }
            if (surface.AffectedFaces.Count==0) yield break;
            var rebuilt=new List<Face>();
            double batchStarted=Time.realtimeSinceStartupAsDouble;
            foreach(Face face in surface.Original)
            {
                if(!surface.AffectedFaces.Contains(face)) rebuilt.Add(face);
                else
                {
                    var relevant=Query(surface.CutCells,face.Bounds).Where(cut=>cut.Bounds.Intersects(face.Bounds)).ToList();
                    foreach(Region region in Partition(face,relevant))
                    {
                        if(region.Cuts.Count==0) rebuilt.Add(region.Face);
                        else
                        {
                            float bottom=Mathf.Min(region.Face.Bounds.min.y,region.Cuts.Min(cut=>cut.Bounds.min.y))-1f;
                            // Keep all Boolean operations for this region near
                            // zero. Rounding intersections at kilometre-scale
                            // coordinates creates slivers and oversized BSPs.
                            Vector3 origin=region.Face.Vertices[0];
                            List<Face> solid=Prism(region.Face.Translated(-origin),bottom-origin.y);
                            Bounds regionBounds=region.Face.Bounds;
                            regionBounds.SetMinMax(new Vector3(regionBounds.min.x,bottom,regionBounds.min.z),regionBounds.max);
                            foreach(Cut cutter in region.Cuts)
                            {
                                // Group bounds can connect a whole pile. A cutter
                                // outside this prism cannot change its geometry;
                                // sending it through BSP still subdivides faces.
                                if(!regionBounds.Intersects(cutter.Bounds)) continue;
                                solid=Difference(solid,cutter.Faces.Select(part=>part.Translated(-origin)).ToList());
                                // WebGL collects managed garbage between frames.
                                // A pile can contain dozens of overlapping solids;
                                // yield within that pile, not only after its region.
                                if(WorldLoading.ShouldYield(ref batchStarted))
                                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
                            }
                            rebuilt.AddRange(solid.Where(part=>part.Visible).Select(part=>part.Translated(origin)));
                        }
                        if(WorldLoading.ShouldYield(ref batchStarted))
                        { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
                    }
                }
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
            }
            Mesh replacement=ToMesh(rebuilt,surface.Mesh.name);
            Mesh drawMesh=TerrainDrawMesh.Build(replacement);
            ReplaceMeshes(surface,replacement,drawMesh);
            LastRebuiltSurfaceCount++;
        }

        private static void ReplaceMeshes(Surface surface,Mesh collision,Mesh draw)
        {
            PlayerWorldLoading.BeforeChange(surface.Filter.gameObject);
            Mesh old=surface.Mesh,oldDraw=surface.DrawMesh;
            surface.Filter.sharedMesh=draw;surface.Collider.sharedMesh=collision;
            if(oldDraw!=old) DestroyMesh(oldDraw);
            if(surface.OwnsMesh) DestroyMesh(old);
            surface.Mesh=collision;surface.DrawMesh=draw;surface.OwnsMesh=true;
            PlayerWorldLoading.NotifyChanged(surface.Filter.gameObject);
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
            foreach(Surface surface in surfaces)
            {
                if(surface.DrawMesh!=surface.Mesh) DestroyMesh(surface.DrawMesh);
                if(surface.OwnsMesh) DestroyMesh(surface.Mesh);
                if(surface.OwnsOriginalMesh) DestroyMesh(surface.OriginalMesh);
            }
            surfaces.Clear(); surfaceCells.Clear(); dirty.Clear();
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
                // Accumulate once. Recursive AddRange copied every descendant's
                // polygons once per ancestor, which inflated temporary memory.
                var result=new List<Face>();
                var pending=new Stack<Node>(); pending.Push(this);
                while(pending.Count>0)
                {
                    Node node=pending.Pop(); result.AddRange(node.faces);
                    if(node.back!=null) pending.Push(node.back);
                    if(node.front!=null) pending.Push(node.front);
                }
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
