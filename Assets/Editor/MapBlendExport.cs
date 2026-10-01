using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    [InitializeOnLoad]
    public static class MapBlendExport
    {
        private static readonly string RequestPath=Path.GetFullPath(
            Path.Combine(Application.dataPath,"..","Artifacts","export-map.request"));

        static MapBlendExport()
        {
            if(File.Exists(RequestPath)) EditorApplication.delayCall+=Export;
        }

        [MenuItem("Wyvernland/Export Map OBJ")]
        public static void Export()
        {
            GameObject temporaryRoot=null;
            if(Object.FindFirstObjectByType<ProceduralMap>()==null)
            {
                temporaryRoot=new GameObject("Temporary Map Export");
                ProceduralMap map=temporaryRoot.AddComponent<ProceduralMap>();
                GroundFeatureLayout.Initialize();
                MethodInfo buildIsland=typeof(ProceduralMap).GetMethod("BuildIsland",
                    BindingFlags.NonPublic|BindingFlags.Instance);
                if(buildIsland==null) throw new System.InvalidOperationException(
                    "Could not locate the island mesh builder.");
                for(int island=0;island<3;island++) buildIsland.Invoke(map,new object[]{island});
            }

            MeshFilter[] islands=Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                .Where(filter => filter.name.StartsWith("Floating Island ")&&filter.sharedMesh!=null)
                .OrderBy(filter => filter.name).ToArray();
            if(islands.Select(filter=>filter.name.Split('[')[0].Trim()).Distinct().Count()!=3)
                throw new System.InvalidOperationException("Expected terrain tiles from three islands.");

            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Artifacts"));
            Directory.CreateDirectory(directory);
            string objPath=Path.Combine(directory,"Wyvernland-Map.obj");
            string mtlPath=Path.Combine(directory,"Wyvernland-Map.mtl");
            CultureInfo invariant=CultureInfo.InvariantCulture;
            using(var writer=new StreamWriter(objPath,false,new System.Text.UTF8Encoding(false)))
            {
                writer.WriteLine("# Wyvernland generated map terrain");
                writer.WriteLine("mtllib Wyvernland-Map.mtl");
                int offset=1;
                foreach(MeshFilter island in islands)
                {
                    Mesh mesh=island.sharedMesh;
                    writer.WriteLine($"o {island.name.Replace(' ','_')}");
                    writer.WriteLine("usemtl Wyvernland_Clay");
                    foreach(Vector3 local in mesh.vertices)
                    {
                        Vector3 point=island.transform.TransformPoint(local);
                        writer.WriteLine(string.Format(invariant,"v {0:R} {1:R} {2:R}",
                            -point.x,point.y,point.z));
                    }
                    foreach(Vector3 local in mesh.normals)
                    {
                        Vector3 normal=island.transform.TransformDirection(local).normalized;
                        writer.WriteLine(string.Format(invariant,"vn {0:R} {1:R} {2:R}",
                            -normal.x,normal.y,normal.z));
                    }
                    int[] triangles=mesh.triangles;
                    // Mirroring X changes handedness, so reverse each triangle.
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        int a=offset+triangles[i],b=offset+triangles[i+2],c=offset+triangles[i+1];
                        writer.WriteLine($"f {a}//{a} {b}//{b} {c}//{c}");
                    }
                    offset+=mesh.vertexCount;
                }
            }
            File.WriteAllText(mtlPath,
                "newmtl Wyvernland_Clay\nKd 0.43 0.16 0.075\nKs 0 0 0\nNs 1\n");
            AssetDatabase.Refresh();
            if(temporaryRoot!=null) Object.DestroyImmediate(temporaryRoot);
            if(File.Exists(RequestPath)) File.Delete(RequestPath);
            Debug.Log($"WYVERNLAND_MAP_OBJ_EXPORTED {objPath}");
        }
    }
}
