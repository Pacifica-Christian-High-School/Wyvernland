using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class FullWorldPitValidation
    {
        [Serializable] public class TileData { public Vector3[] vertices; public int[] triangles; public CutData[] cuts; }
        [Serializable] public class CutData { public Vector3[] points; public int[] triangles; }
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;

        // Extract real world geometry so a pathological tile can be replayed
        // without regenerating thousands of props or starting a browser export.
        public static void Capture()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Single);
            typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,false);
            var root=new GameObject("Full world pit validation");
            var map=root.AddComponent<ProceduralMap>(); map.Build();
            var player=new GameObject("Observer").AddComponent<FirstPersonController>();
            player.transform.SetParent(root.transform);
            root.AddComponent<ProceduralMonumentField>().Initialize(player,map);
            var rocks=root.AddComponent<ProceduralRockField>();
            Drain((IEnumerator)typeof(ProceduralRockField).GetMethod("InitializeIncrementally",Flags)
                .Invoke(rocks,new object[]{player,map,null}));
            var pits=(IEnumerable)typeof(ProceduralRockField).GetField("terrainPits",Flags).GetValue(rocks);
            map.BeginStonePitGeneration();
            int count=0;
            foreach(object pit in pits)
            {
                Type type=pit.GetType();
                var source=(Mesh)type.GetField("Item1").GetValue(pit);
                var placement=(Matrix4x4)type.GetField("Item2").GetValue(pit);
                Mesh cutter=StoneTerrainCutout.SimplifyCutter(source);
                map.CarveStoneTerrain(cutter,placement); UnityEngine.Object.DestroyImmediate(cutter);
                if(++count%5000==0) Debug.Log($"PIT_CAPTURE cutters {count}");
            }
            var cutout=typeof(ProceduralMap).GetField("stoneCutouts",Flags).GetValue(map);
            var dirty=(IEnumerable)cutout.GetType().GetField("dirty",Flags).GetValue(cutout);
            Directory.CreateDirectory("Artifacts/PitTiles"); int tile=0;
            foreach(object surface in dirty)
            {
                Type type=surface.GetType();
                var mesh=(Mesh)type.GetField("OriginalMesh",Flags).GetValue(surface);
                var cuts=(IEnumerable)type.GetField("Cuts",Flags).GetValue(surface);
                var list=new List<CutData>();
                foreach(object cut in cuts)
                    list.Add(new CutData { points=(Vector3[])cut.GetType().GetField("Points").GetValue(cut),
                        triangles=(int[])cut.GetType().GetField("Triangles").GetValue(cut) });
                string path=$"Artifacts/PitTiles/{tile++:D4}.json";
                File.WriteAllText(path,JsonUtility.ToJson(new TileData{vertices=mesh.vertices,triangles=mesh.triangles,cuts=list.ToArray()}));
                Debug.Log($"PIT_CAPTURE tile {tile} {mesh.bounds.center} cuts {list.Count}");
            }
            Debug.Log($"PIT_CAPTURE_PASS {count} cutters, {tile} tiles");
        }

        public static void Replay()
        {
            string path=Environment.GetCommandLineArgs().SkipWhile(a=>a!="-pitTile").Skip(1).First();
            var data=JsonUtility.FromJson<TileData>(File.ReadAllText(path));
            var root=new GameObject("Pit tile replay"); var cutout=new StoneTerrainCutout(root.transform);
            var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=data.vertices,triangles=data.triangles};
            mesh.RecalculateBounds(); cutout.AddSurface(mesh,null,"Replay tile",_=>{});cutout.BeginBatch();
            foreach(var cut in data.cuts)
            {
                var cutter=new Mesh{vertices=cut.points,triangles=cut.triangles};
                cutout.Subtract(cutter,Matrix4x4.identity);UnityEngine.Object.DestroyImmediate(cutter);
            }
            Debug.Log($"PIT_REPLAY_BEGIN {path} {data.cuts.Length} cuts {data.triangles.Length/3} faces");
            var watch=System.Diagnostics.Stopwatch.StartNew();
            cutout.EndBatch();
            Debug.Log($"PIT_REPLAY_PASS {watch.Elapsed.TotalSeconds:F2}s");
        }
        static void Drain(IEnumerator routine) { while(routine.MoveNext()) {} }
    }
}
