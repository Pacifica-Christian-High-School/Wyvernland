using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    internal static class TerrainTiles
    {
        internal const float Size = 100f;

        internal static IEnumerator Split(Mesh source, Action<Mesh, Vector2Int> add)
        {
            Vector3[] vertices = source.vertices;
            int[] indices = source.triangles;
            var tiles = new Dictionary<Vector2Int, Builder>();
            double batch = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                int minX = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / Size);
                int maxX = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / Size);
                int minZ = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / Size);
                int maxZ = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / Size);
                for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    var key = new Vector2Int(x, z);
                    if (minX == maxX && minZ == maxZ)
                    {
                        if (!tiles.TryGetValue(key, out var builder)) tiles[key] = builder = new Builder();
                        builder.Triangle(a, b, c);
                    }
                    else
                    {
                        var polygon = new List<Vector3> { a, b, c };
                        polygon = Clip(polygon, 0, x * Size, true);
                        polygon = Clip(polygon, 0, (x + 1) * Size, false);
                        polygon = Clip(polygon, 2, z * Size, true);
                        polygon = Clip(polygon, 2, (z + 1) * Size, false);
                        for (int j = 1; j + 1 < polygon.Count; j++)
                        {
                            if (Vector3.Cross(polygon[j] - polygon[0], polygon[j + 1] - polygon[0]).sqrMagnitude < 1e-20f) continue;
                            if (!tiles.TryGetValue(key, out var builder)) tiles[key] = builder = new Builder();
                            builder.Triangle(polygon[0], polygon[j], polygon[j + 1]);
                        }
                    }
                }
                if (WorldLoading.ShouldYield(ref batch)) { yield return null; batch = Time.realtimeSinceStartupAsDouble; }
            }
            // Release each builder as its tile is handed to the terrain system.
            var keys = new List<Vector2Int>(tiles.Keys);
            foreach (var key in keys)
            {
                Builder builder = tiles[key];
                tiles.Remove(key);
                add(builder.Build(source.name + $" tile {key.x},{key.y}"), key);
                if (WorldLoading.ShouldYield(ref batch)) { yield return null; batch = Time.realtimeSinceStartupAsDouble; }
            }
        }

        private static List<Vector3> Clip(List<Vector3> input, int axis, float boundary, bool above)
        {
            var output = new List<Vector3>(input.Count + 1);
            for (int i = 0; i < input.Count; i++)
            {
                Vector3 a = input[i], b = input[(i + 1) % input.Count];
                bool insideA = above ? a[axis] >= boundary : a[axis] <= boundary;
                bool insideB = above ? b[axis] >= boundary : b[axis] <= boundary;
                if (insideA) output.Add(a);
                if (insideA != insideB)
                {
                    Vector3 p = Vector3.LerpUnclamped(a, b, (boundary - a[axis]) / (b[axis] - a[axis]));
                    p[axis] = boundary;
                    output.Add(p);
                }
            }
            return output;
        }

        private sealed class Builder
        {
            private readonly Dictionary<Vector3, int> lookup = new();
            private readonly List<Vector3> vertices = new();
            private readonly List<int> indices = new();
            private int Vertex(Vector3 p)
            {
                if (!lookup.TryGetValue(p, out int index))
                { index = vertices.Count; lookup.Add(p, index); vertices.Add(p); }
                return index;
            }
            internal void Triangle(Vector3 a, Vector3 b, Vector3 c)
            { indices.Add(Vertex(a)); indices.Add(Vertex(b)); indices.Add(Vertex(c)); }
            internal Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
