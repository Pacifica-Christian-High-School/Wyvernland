using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    // Identical positions and colors share vertices. Triangle geometry remains
    // unchanged; the terrain shader reconstructs each face's flat normal.
    internal static class TerrainDrawMesh
    {
        public static Mesh Build(Mesh source)
        {
            Vector3[] positions=source.vertices;
            Color[] colors=source.colors;
            var unique=new Dictionary<(Vector3,Color),int>();
            var vertices=new List<Vector3>();
            var sharedColors=new List<Color>();
            var remap=new int[positions.Length];
            for(int i=0;i<positions.Length;i++)
            {
                Color color=colors.Length==positions.Length?colors[i]:Color.white;
                var key=(positions[i],color);
                if(!unique.TryGetValue(key,out int vertex))
                {
                    vertex=vertices.Count;
                    unique.Add(key,vertex);
                    vertices.Add(positions[i]);
                    sharedColors.Add(color);
                }
                remap[i]=vertex;
            }
            var result=new Mesh { name=source.name+" shared draw vertices",indexFormat=IndexFormat.UInt32 };
            result.SetVertices(vertices);
            result.SetColors(sharedColors);
            result.subMeshCount=source.subMeshCount;
            for(int slot=0;slot<source.subMeshCount;slot++)
            {
                int[] indices=source.GetIndices(slot);
                for(int i=0;i<indices.Length;i++) indices[i]=remap[indices[i]];
                result.SetIndices(indices,MeshTopology.Triangles,slot,false);
            }
            result.RecalculateNormals();
            result.bounds=source.bounds;
            return result;
        }
    }
}
