using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wyvernland
{
    public static class MaterialDensity
    {
        // Kilograms per cubic metre of material, excluding empty space.
        static readonly Dictionary<string,float> values=new(StringComparer.OrdinalIgnoreCase)
        {
            ["Grass"]=150f, ["Wood"]=650f, ["Burnt Wood"]=650f,
            ["Quartz"]=2650f,
            ["Magnetite"]=5170f, ["Limestone"]=2600f, ["Stone"]=2700f,
            ["River Rock"]=2650f, ["Chalk Stone"]=2200f, ["Obsidian"]=2370f,
            ["Coal"]=1300f, ["Hematite"]=5260f, ["Native Copper"]=8960f,
            ["Pyrite"]=5010f, ["Chalcocite"]=5700f, ["Limonite"]=3500f,
            ["Cassiterite"]=7000f, ["Galena"]=7600f, ["Sulfur"]=2070f,
            ["Saltpeter"]=2110f, ["Meteorite"]=5500f, ["Basalt"]=3000f
        };

        public static bool TryGet(string material,out float kilogramsPerCubicMetre) =>
            values.TryGetValue(material??string.Empty,out kilogramsPerCubicMetre);
    }

    // Integrates the actual triangle surfaces, never collider hulls or bounds.
    // A cup/helmet with inner walls and a rim is one closed material shell.
    // Separate enclosed shells alternate solid/cavity/solid by nesting depth.
    public static class MeshSolidVolume
    {
        public readonly struct Source
        {
            public readonly Mesh Mesh;
            public readonly Matrix4x4 Transform;
            public Source(Mesh mesh,Matrix4x4 transform) { Mesh=mesh; Transform=transform; }
        }

        struct Face
        {
            public int A,B,C;
            public Face(int a,int b,int c) { A=a; B=b; C=c; }
        }

        sealed class Shell
        {
            public readonly List<Face> Faces=new();
            public Bounds Bounds;
            public bool Closed=true;
            public double Volume;
        }

        public static bool TryMeasure(Mesh mesh,out double cubicMetres,out bool hasOpenSurfaces) =>
            TryMeasure(new[]{new Source(mesh,Matrix4x4.identity)},out cubicMetres,out hasOpenSurfaces);

        public static bool TryMeasure(IReadOnlyList<Source> sources,out double cubicMetres,
            out bool hasOpenSurfaces,float seamTolerance=0f)
        {
            cubicMetres=0d; hasOpenSurfaces=false;
            var vertices=new List<Vector3>();
            var rawFaces=new List<Face>();
            foreach(var source in sources)
            {
                Mesh mesh=source.Mesh;
                if(mesh==null) continue;
                // Missing CPU geometry is unknown volume, not a bounding-box fallback.
                if(!mesh.isReadable) return false;
                int first=vertices.Count;
                foreach(Vector3 vertex in mesh.vertices)
                {
                    Vector3 point=source.Transform.MultiplyPoint3x4(vertex);
                    if(!float.IsFinite(point.x)||!float.IsFinite(point.y)||!float.IsFinite(point.z)) return false;
                    vertices.Add(point);
                }
                for(int slot=0;slot<mesh.subMeshCount;slot++)
                {
                    if(mesh.GetTopology(slot)!=MeshTopology.Triangles) continue;
                    var triangles=mesh.GetTriangles(slot);
                    for(int i=0;i+2<triangles.Length;i+=3)
                        rawFaces.Add(new Face(first+triangles[i],first+triangles[i+1],first+triangles[i+2]));
                }
            }
            if(vertices.Count==0||rawFaces.Count==0) return false;

            // Weld render seams so UV seams and separate material slots do not
            // look like holes. Positions are object-local to retain precision.
            var bounds=new Bounds(vertices[0],Vector3.zero);
            foreach(Vector3 point in vertices) bounds.Encapsulate(point);
            double tolerance=Math.Max(seamTolerance,Math.Max(1e-7,bounds.size.magnitude*1e-7));
            var buckets=new Dictionary<(long,long,long),List<int>>();
            var welded=new List<Vector3>();
            var ids=new int[vertices.Count];
            for(int i=0;i<vertices.Count;i++)
            {
                Vector3 point=vertices[i];
                long x=(long)Math.Floor(point.x/tolerance),y=(long)Math.Floor(point.y/tolerance),
                    z=(long)Math.Floor(point.z/tolerance);
                int found=-1;
                for(int dx=-1;dx<=1&&found<0;dx++)
                for(int dy=-1;dy<=1&&found<0;dy++)
                for(int dz=-1;dz<=1&&found<0;dz++)
                    if(buckets.TryGetValue((x+dx,y+dy,z+dz),out var candidates))
                        foreach(int candidate in candidates)
                        {
                            Vector3 delta=welded[candidate]-point;
                            if(Math.Abs(delta.x)<=tolerance&&Math.Abs(delta.y)<=tolerance&&Math.Abs(delta.z)<=tolerance)
                            { found=candidate; break; }
                        }
                if(found<0)
                {
                    found=welded.Count; welded.Add(point);
                    if(!buckets.TryGetValue((x,y,z),out var bucket))
                        buckets[(x,y,z)]=bucket=new List<int>();
                    bucket.Add(found);
                }
                ids[i]=found;
            }

            var faces=new List<Face>();
            var parents=new List<int>();
            var edges=new Dictionary<(int,int),(int face,int count,int winding)>();
            int Root(int index)
            {
                while(parents[index]!=index)
                { parents[index]=parents[parents[index]]; index=parents[index]; }
                return index;
            }
            foreach(var raw in rawFaces)
            {
                var face=new Face(ids[raw.A],ids[raw.B],ids[raw.C]);
                if(face.A==face.B||face.B==face.C||face.C==face.A) continue;
                if(Vector3.Cross(welded[face.B]-welded[face.A],welded[face.C]-welded[face.A]).sqrMagnitude<1e-24f)
                    continue;
                int index=faces.Count; faces.Add(face); parents.Add(index);
                Edge(face.A,face.B,index); Edge(face.B,face.C,index); Edge(face.C,face.A,index);
            }
            void Edge(int a,int b,int index)
            {
                var key=a<b?(a,b):(b,a);
                int direction=a<b?1:-1;
                if(edges.TryGetValue(key,out var edge))
                {
                    parents[Root(index)]=Root(edge.face);
                    edges[key]=(edge.face,edge.count+1,edge.winding+direction);
                }
                else edges[key]=(index,1,direction);
            }

            var shellsByRoot=new Dictionary<int,Shell>();
            for(int i=0;i<faces.Count;i++)
            {
                int root=Root(i); var face=faces[i];
                if(!shellsByRoot.TryGetValue(root,out var shell))
                    shellsByRoot[root]=shell=new Shell { Bounds=new Bounds(welded[face.A],Vector3.zero) };
                shell.Faces.Add(face);
                shell.Bounds.Encapsulate(welded[face.A]); shell.Bounds.Encapsulate(welded[face.B]);
                shell.Bounds.Encapsulate(welded[face.C]);
            }
            foreach(var edge in edges.Values)
                if(edge.count!=2||edge.winding!=0) shellsByRoot[Root(edge.face)].Closed=false;

            var solids=new List<Shell>();
            foreach(var shell in shellsByRoot.Values)
            {
                // An open, zero-thickness surface contains no material volume.
                // In particular, do not close a cup opening with an invented lid.
                if(!shell.Closed) { hasOpenSurfaces=true; continue; }
                Vector3 origin=shell.Bounds.center;
                double sixVolume=0d;
                foreach(var face in shell.Faces)
                    sixVolume+=Triple(welded[face.A]-origin,welded[face.B]-origin,welded[face.C]-origin);
                shell.Volume=Math.Abs(sixVolume)/6d;
                if(shell.Volume>0d) solids.Add(shell);
            }
            foreach(var shell in solids)
            {
                int depth=0;
                var face=shell.Faces[0];
                Vector3 sample=(welded[face.A]+welded[face.B]+welded[face.C])/3f;
                foreach(var outer in solids)
                {
                    if(outer==shell||outer.Volume<=shell.Volume||
                        !outer.Bounds.Contains(shell.Bounds.min)||!outer.Bounds.Contains(shell.Bounds.max)) continue;
                    if(Contains(outer,welded,sample)) depth++;
                }
                cubicMetres+=(depth&1)==0?shell.Volume:-shell.Volume;
            }
            cubicMetres=Math.Max(0d,cubicMetres);
            return true;
        }

        static double Triple(Vector3 a,Vector3 b,Vector3 c) =>
            (double)a.x*((double)b.y*c.z-(double)b.z*c.y)+
            (double)a.y*((double)b.z*c.x-(double)b.x*c.z)+
            (double)a.z*((double)b.x*c.y-(double)b.y*c.x);

        static double Dot(Vector3 a,Vector3 b) => (double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z;

        static bool Contains(Shell shell,List<Vector3> vertices,Vector3 point)
        {
            // Solid-angle winding avoids counting a ray's shared triangle edge
            // twice, and accepts either overall winding orientation.
            double angle=0d;
            foreach(var face in shell.Faces)
            {
                Vector3 a=vertices[face.A]-point,b=vertices[face.B]-point,c=vertices[face.C]-point;
                double la=Math.Sqrt(Dot(a,a)),lb=Math.Sqrt(Dot(b,b)),lc=Math.Sqrt(Dot(c,c));
                angle+=2d*Math.Atan2(Triple(a,b,c),la*lb*lc+Dot(a,b)*lc+Dot(b,c)*la+Dot(c,a)*lb);
            }
            return Math.Abs(angle)>Math.PI*2d;
        }
    }
}
