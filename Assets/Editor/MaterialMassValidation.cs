using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class MaterialMassValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Material Mass")]
        public static void Run()
        {
            var root=new GameObject("Material mass validation");
            var meshes=new List<Mesh>();
            try
            {
                var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);
                var cube=UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
                meshes.Add(cube); UnityEngine.Object.DestroyImmediate(primitive);
                Volume(new[]{Source(cube,Vector3.zero,Vector3.one)},1d,"Unit cube");
                Volume(new[]{Source(cube,new Vector3(1000,-500,900),new Vector3(-2,3,4))},24d,
                    "Translation, nonuniform scale and reflection");
                Volume(new[]{Source(cube,Vector3.zero,Vector3.one),Source(cube,Vector3.right*2,-Vector3.one)},2d,
                    "Separate solids with opposite winding do not cancel");
                Volume(new[]{Source(cube,Vector3.zero,Vector3.one),Source(cube,Vector3.zero,Vector3.one*0.5f)},0.875d,
                    "Enclosed cavity is subtracted even with the same winding");
                Volume(new[]{Source(cube,Vector3.zero,Vector3.one),Source(cube,Vector3.zero,Vector3.one*0.5f),
                    Source(cube,Vector3.zero,Vector3.one*0.2f)},0.883d,"Solid inside a cavity is added back");

                var tetra=new Mesh(); meshes.Add(tetra);
                tetra.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.forward};
                tetra.triangles=new[]{0,2,1,0,1,3,0,3,2,1,2,3};
                Volume(new[]{Source(tetra,Vector3.zero,Vector3.one)},1d/6d,"Tetrahedron uses triangle volume, not its box");
                var cup=BoxCup(); meshes.Add(cup);
                Volume(new[]{Source(cup,Vector3.zero,Vector3.one)},1d-0.8d*0.8d*0.9d,
                    "Open cup excludes its air cavity and includes only walls and bottom");
                var helmet=Helmet(); meshes.Add(helmet);
                Volume(new[]{Source(helmet,Vector3.zero,Vector3.one)},2d*Math.PI/3d*(1d-0.8d*0.8d*0.8d),
                    "Helmet excludes its open interior",0.004d);
                var card=new Mesh(); meshes.Add(card);
                card.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up}; card.triangles=new[]{0,1,2};
                Require(MeshSolidVolume.TryMeasure(card,out double cardVolume,out bool open)&&open&&cardVolume==0d,
                    "Zero-thickness grass cards have zero mass volume");

                var item=new GameObject("Copper cup"); item.transform.SetParent(root.transform);
                item.AddComponent<MeshFilter>().sharedMesh=cup;
                item.AddComponent<MeshRenderer>();
                var body=item.AddComponent<Rigidbody>();
                var properties=item.AddComponent<PhysicalDestructible>();
                properties.ApplyMaterial("Native Copper");
                Near(properties.MassKg,3799.04d,0.01d,"Copper cup mass");
                Near(body.mass,properties.MassKg,0.001d,"Calculated mass reaches Rigidbody");
                item.transform.localScale=new Vector3(-2,3,4);
                Near(properties.MassKg,3799.04d*24d,0.1d,"Mass refreshes after scale changes");
                Near(body.mass,properties.MassKg,0.001d,"Scaled mass reaches Rigidbody");
                properties.ApplyMaterial("Wood");
                Near(properties.MassKg,0.424d*24d*650d,0.02d,"Material changes refresh mass");
                item.GetComponent<MeshFilter>().sharedMesh=card;
                properties.ApplyMaterial("Grass");
                Require(properties.HasMaterialMass&&properties.MassKg==0f&&properties.SolidVolumeCubicMetres==0d,
                    "Grass card retains exactly zero material mass");
                Require(body.mass>0f&&body.mass<0.00001f,"Zero-volume Rigidbody uses solver epsilon instead of default mass");

                string[] names={"Grass","Wood","Quartz","Magnetite","Limestone","Stone","River Rock",
                    "Chalk Stone","Obsidian","Coal","Hematite","Native Copper","Pyrite","Chalcocite",
                    "Limonite","Cassiterite","Galena","Sulfur","Saltpeter","Meteorite","Basalt"};
                float[] densities={150,650,2650,5170,2600,2700,2650,2200,2370,1300,5260,8960,5010,
                    5700,3500,7000,7600,2070,2110,5500,3000};
                for(int i=0;i<names.Length;i++)
                    Require(MaterialDensity.TryGet(names[i].ToLowerInvariant(),out float density)&&density==densities[i],
                        "Density catalog matches "+names[i]);
                var unreadable=UnityEngine.Object.Instantiate(cube); meshes.Add(unreadable);
                unreadable.UploadMeshData(true);
                Require(!MeshSolidVolume.TryMeasure(unreadable,out _,out _),"Unreadable meshes never use box volume");
                Debug.Log("MATERIAL_MASS_VALIDATION_PASS: all 21 densities, tetrahedron, cup, helmet, enclosed/nested cavities, disconnected solids, mirrored/nonuniform scale, zero-thickness surfaces, and Rigidbody mass updates.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach(var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        static MeshSolidVolume.Source Source(Mesh mesh,Vector3 position,Vector3 scale) =>
            new(mesh,Matrix4x4.TRS(position,Quaternion.identity,scale));

        static void Volume(MeshSolidVolume.Source[] sources,double expected,string message,double tolerance=0.00001d)
        {
            Require(MeshSolidVolume.TryMeasure(sources,out double actual,out bool open)&&!open,message+" is a closed solid");
            Near(actual,expected,tolerance,message);
        }
        static void Near(double actual,double expected,double tolerance,string message) =>
            Require(Math.Abs(actual-expected)<=tolerance,message+": expected "+expected+", got "+actual);
        static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }

        sealed class SurfaceBuilder
        {
            readonly List<Vector3> points=new();
            readonly List<int> indices=new();
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal)
            {
                int first=points.Count;
                points.AddRange(new[]{a,b,c,d});
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)<0f)
                    indices.AddRange(new[]{first,first+2,first+1,first,first+3,first+2});
                else indices.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});
            }
            public Mesh Build()
            {
                var mesh=new Mesh(); mesh.SetVertices(points); mesh.SetTriangles(indices,0); mesh.RecalculateBounds();
                return mesh;
            }
        }
        static Mesh BoxCup()
        {
            var builder=new SurfaceBuilder();
            Vector3[] Ring(float radius,float y)=>new[]{new Vector3(-radius,y,-radius),
                new Vector3(radius,y,-radius),new Vector3(radius,y,radius),new Vector3(-radius,y,radius)};
            var bottom=Ring(0.5f,0f); var top=Ring(0.5f,1f);
            var innerBottom=Ring(0.4f,0.1f); var innerTop=Ring(0.4f,1f);
            builder.Quad(bottom[0],bottom[1],bottom[2],bottom[3],Vector3.down);
            builder.Quad(innerBottom[0],innerBottom[1],innerBottom[2],innerBottom[3],Vector3.up);
            for(int i=0;i<4;i++)
            {
                int j=(i+1)%4; Vector3 normal=bottom[i]+bottom[j];
                builder.Quad(bottom[i],bottom[j],top[j],top[i],normal);
                builder.Quad(innerBottom[i],innerBottom[j],innerTop[j],innerTop[i],-normal);
                builder.Quad(top[i],top[j],innerTop[j],innerTop[i],Vector3.up);
            }
            return builder.Build();
        }
        static Mesh Helmet()
        {
            var builder=new SurfaceBuilder(); const int rows=24,columns=64;
            Vector3 Point(int row,int column,float radius)
            {
                float theta=row*Mathf.PI*0.5f/rows,phi=column*Mathf.PI*2f/columns;
                return radius*new Vector3(Mathf.Sin(theta)*Mathf.Cos(phi),Mathf.Cos(theta),Mathf.Sin(theta)*Mathf.Sin(phi));
            }
            for(int row=0;row<rows;row++) for(int column=0;column<columns;column++)
            {
                var a=Point(row,column,1f); var b=Point(row+1,column,1f);
                var c=Point(row+1,column+1,1f); var d=Point(row,column+1,1f);
                builder.Quad(a,b,c,d,a+b+c+d);
                builder.Quad(a*0.8f,b*0.8f,c*0.8f,d*0.8f,-a-b-c-d);
            }
            for(int column=0;column<columns;column++)
                builder.Quad(Point(rows,column,1f),Point(rows,column+1,1f),
                    Point(rows,column+1,0.8f),Point(rows,column,0.8f),Vector3.down);
            return builder.Build();
        }
    }
}
