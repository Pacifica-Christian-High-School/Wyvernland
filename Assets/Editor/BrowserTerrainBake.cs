using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class BrowserTerrainBake
    {
        public static void EnsureFresh()
        {
            string[] scripts={"ProceduralMap","ProceduralRockField","StoneTerrainCutout","TerrainTiles",
                "TerrainDrawMesh","GroundFeatureLayout","WorldConfig"};
            string[] inputs=scripts.Select(s=>$"Assets/Scripts/{s}.cs")
                .Concat(new[]{"Assets/Editor/BrowserTerrainBake.cs"})
                .Concat(Directory.GetFiles("Assets/Resources/Terrain","*.asset")).OrderBy(p=>p).ToArray();
            using var hash=SHA256.Create();
            using var fingerprint=new MemoryStream();
            foreach(string input in inputs)
            {
                using var source=File.OpenRead(input);byte[] digest=hash.ComputeHash(source);
                fingerprint.Write(digest,0,digest.Length);
            }
            string signature=BitConverter.ToString(hash.ComputeHash(fingerprint.ToArray())).Replace("-","").ToLowerInvariant();
            string cache=$"Library/Wyvernland/BrowserPitTiles-{signature}.bytes";
            Directory.CreateDirectory("Library/Wyvernland");
            if(!File.Exists(cache)) Bake(cache);
            string asset="Assets/Resources/Terrain/BrowserPitTiles.bytes";
            File.Copy(cache,asset,true);AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Single);
            Debug.Log($"BROWSER_TERRAIN_BAKE_READY {signature} {new FileInfo(cache).Length} bytes");
        }

        private static void Bake(string path)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Single);
            bool previous=WorldBootstrap.TestGround;
            var terrain=new GameObject("Browser terrain bake");
            var props=new GameObject("Browser terrain bake props");
            var watch=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,false);
                var map=terrain.AddComponent<ProceduralMap>();map.Build();
                var player=new GameObject("Bake observer").AddComponent<FirstPersonController>();
                player.transform.SetParent(props.transform);
                props.AddComponent<ProceduralMonumentField>().Initialize(player,map);
                var rocks=props.AddComponent<ProceduralRockField>();
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                Drain((IEnumerator)typeof(ProceduralRockField).GetMethod("InitializeIncrementally",flags)
                    .Invoke(rocks,new object[]{player,map,null}));
                var pits=(IEnumerable)typeof(ProceduralRockField).GetField("terrainPits",flags).GetValue(rocks);
                map.BeginStonePitGeneration();int count=0;
                foreach(object pit in pits)
                {
                    Type type=pit.GetType();var mesh=(Mesh)type.GetField("Item1").GetValue(pit);
                    var placement=(Matrix4x4)type.GetField("Item2").GetValue(pit);
                    Mesh cutter=StoneTerrainCutout.SimplifyCutter(mesh);
                    map.CarveStoneTerrain(cutter,placement);UnityEngine.Object.DestroyImmediate(cutter);
                    if(++count%5000==0) Debug.Log($"BROWSER_TERRAIN_BAKE cutters {count}");
                }
                // Compact cutters already own their coordinates. Source props
                // can be freed before allocating the finished terrain meshes.
                UnityEngine.Object.DestroyImmediate(props);props=null;
                GC.Collect();
                var finish=(IEnumerator)typeof(ProceduralMap).GetMethod("FinishStonePitGenerationIncrementally",flags)
                    .Invoke(map,null);
                int reported=0;
                while(finish.MoveNext())
                    if(map.LastStoneCutoutSurfaceCount>=reported+25)
                    { reported=map.LastStoneCutoutSurfaceCount;Debug.Log($"BROWSER_TERRAIN_BAKE tiles {reported} ({watch.Elapsed.TotalSeconds:F1}s)"); }
                using(var output=File.Create(path+".building")) map.WriteBrowserPitTiles(output);
                File.Move(path+".building",path);
                Debug.Log($"BROWSER_TERRAIN_BAKE_PASS {count} cutters {map.LastStoneCutoutSurfaceCount} tiles ({watch.Elapsed.TotalSeconds:F1}s)");
            }
            finally
            {
                if(props!=null) UnityEngine.Object.DestroyImmediate(props);
                UnityEngine.Object.DestroyImmediate(terrain);
                typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,previous);
            }
        }
        static void Drain(IEnumerator routine) { while(routine.MoveNext()) {} }
    }
}
