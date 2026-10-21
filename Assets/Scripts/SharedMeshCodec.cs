using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    internal static class SharedMeshCodec
    {
        internal static WebWorldSession.MeshData Encode(Mesh mesh,Material[] materials)
        {
            using var buffer=new MemoryStream();
            using(var compressed=new DeflateStream(buffer,System.IO.Compression.CompressionLevel.Fastest,true))
            using(var writer=new BinaryWriter(compressed)) {
                writer.Write(1);writer.Write(mesh.vertexCount);writer.Write(mesh.subMeshCount);
                Vector3[] vertices=mesh.vertices,normals=mesh.normals;Vector2[] uv=mesh.uv;Color32[] colors=mesh.colors32;
                for(int i=0;i<vertices.Length;i++) {
                    Write(writer,vertices[i]);Write(writer,normals.Length==vertices.Length?normals[i]:Vector3.up);
                    Vector2 tex=uv.Length==vertices.Length?uv[i]:Vector2.zero;writer.Write(tex.x);writer.Write(tex.y);
                    Color32 color=colors.Length==vertices.Length?colors[i]:new Color32(255,255,255,255);
                    writer.Write(color.r);writer.Write(color.g);writer.Write(color.b);writer.Write(color.a);
                }
                for(int slot=0;slot<mesh.subMeshCount;slot++) {
                    int[] indices=mesh.GetTriangles(slot);writer.Write(indices.Length);foreach(int index in indices) writer.Write(index);
                    Material material=materials!=null&&slot<materials.Length?materials[slot]:null;
                    Color color=material!=null&&material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):Color.white;
                    writer.Write(color.r);writer.Write(color.g);writer.Write(color.b);writer.Write(color.a);
                    writer.Write(material!=null&&material.HasProperty("_Cull")?material.GetFloat("_Cull"):2f);
                    writer.Write(material!=null&&material.HasProperty("_UseVertexColor")?material.GetFloat("_UseVertexColor"):0f);
                }
            }
            byte[] bytes=buffer.ToArray();
            return new WebWorldSession.MeshData { hash=WebWorldSession.Digest(bytes),data=Convert.ToBase64String(bytes) };
        }
        internal static Mesh Decode(WebWorldSession.MeshData data,out Material[] materials)
        {
            if(data.data.Length>8_388_608) throw new InvalidDataException("Shared mesh is too large");
            byte[] bytes=Convert.FromBase64String(data.data);
            if(WebWorldSession.Digest(bytes)!=data.hash) throw new InvalidDataException("Shared mesh checksum mismatch");
            using var input=new MemoryStream(bytes);
            using var compressed=new DeflateStream(input,CompressionMode.Decompress);
            using var reader=new BinaryReader(compressed);
            if(reader.ReadInt32()!=1) throw new InvalidDataException("Unknown mesh format");
            int count=reader.ReadInt32(),slots=reader.ReadInt32();
            if(count<1||count>250000||slots<1||slots>64) throw new InvalidDataException("Invalid mesh dimensions");
            var vertices=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];var colors=new Color32[count];
            for(int i=0;i<count;i++) {
                vertices[i]=Read(reader);normals[i]=Read(reader);uv[i]=new Vector2(Number(reader),Number(reader));
                colors[i]=new Color32(reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),reader.ReadByte());
            }
            var triangles=new int[slots][];materials=new Material[slots];int total=0;
            for(int slot=0;slot<slots;slot++) {
                int indices=reader.ReadInt32();total+=indices;
                if(indices<0||indices%3!=0||total>1500000) throw new InvalidDataException("Invalid triangles");
                triangles[slot]=new int[indices];
                for(int i=0;i<indices;i++) { int index=reader.ReadInt32();if(index<0||index>=count) throw new InvalidDataException("Invalid vertex index");triangles[slot][i]=index; }
                var color=new Color(Number(reader),Number(reader),Number(reader),Number(reader));
                float cull=Number(reader),vertexColor=Number(reader);
                var material=new Material(Resources.Load<Shader>("Clay"));material.SetColor("_BaseColor",color);
                material.SetFloat("_Cull",cull);material.SetFloat("_UseVertexColor",vertexColor);materials[slot]=material;
            }
            var mesh=new Mesh { name="Shared room geometry",indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16 };
            mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.colors32=colors;mesh.subMeshCount=slots;
            for(int slot=0;slot<slots;slot++) mesh.SetTriangles(triangles[slot],slot,false);
            mesh.RecalculateBounds();return mesh;
        }
        static float Number(BinaryReader reader) { float value=reader.ReadSingle();if(!float.IsFinite(value)||Mathf.Abs(value)>100000f) throw new InvalidDataException("Invalid mesh coordinate");return value; }
        static Vector3 Read(BinaryReader reader) => new(Number(reader),Number(reader),Number(reader));
        static void Write(BinaryWriter writer,Vector3 value) { writer.Write(value.x);writer.Write(value.y);writer.Write(value.z); }
    }
}
