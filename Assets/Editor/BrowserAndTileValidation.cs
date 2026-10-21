using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class BrowserAndTileValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Startup Menu and Terrain Tiles")]
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            float time = Time.timeScale;
            string save = PlayerPrefs.GetString("PlayerSave.Data", "");
            try
            {
                typeof(WorldBootstrap).GetMethod("BuildStartMenu", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Require(UnityEngine.Object.FindFirstObjectByType<PauseMenu>() != null, "Startup menu exists");
                Require(UnityEngine.Object.FindFirstObjectByType<ProceduralMap>() == null &&
                    UnityEngine.Object.FindFirstObjectByType<WorldLoading>() == null &&
                    UnityEngine.Object.FindFirstObjectByType<ProceduralRockField>() == null &&
                    UnityEngine.Object.FindFirstObjectByType<RiggedPlayerAvatar>() == null, "Startup must not build a map or player rig");
                Require(PlayerPrefs.GetString("PlayerSave.Data", "") == save, "Opening the menu must preserve the existing player save");
            }
            finally
            {
                var menu = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
                if (menu != null) UnityEngine.Object.DestroyImmediate(menu.gameObject);
                Time.timeScale = time;
            }
            StoneTerrainCutoutValidation.Run();
            ValidateCutters();
            ValidateTiles();
            ValidateDistantPits();
            ValidateBakedTiles();
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            Debug.Log("BROWSER_STARTUP_AND_TILES_VALIDATION_PASS");
        }

        private static void ValidateCutters()
        {
            Type builderType=typeof(ProceduralRockField).GetNestedType("MeshBuilder",BindingFlags.NonPublic);
            var watch=System.Diagnostics.Stopwatch.StartNew();
            for(int seed=0;seed<32;seed++)
            {
                object builder=Activator.CreateInstance(builderType,true);
                float scale=seed%3==0?.064f:seed%3==1?1f:6f;
                if(seed%2==0)
                    builderType.GetMethod("AddEllipsoid").Invoke(builder,new object[]{Vector3.zero,Quaternion.identity,
                        scale*.5f,scale*.4f,scale*.6f,new System.Random(seed),8+seed%5,5+seed%3});
                else
                    builderType.GetMethod("AddStandingRock",new[]{typeof(float),typeof(System.Random)}).
                        Invoke(builder,new object[]{scale,new System.Random(seed)});
                Mesh source=(Mesh)builderType.GetMethod("Build").Invoke(builder,new object[]{"Cutter validation"});
                Mesh cutter=null;
                try
                {
                    cutter=StoneTerrainCutout.SimplifyCutter(source);
                    int[] triangles=cutter.triangles;
                    Vector3[] points=cutter.vertices;
                    Require(triangles.Length>=36&&triangles.Length<=Mathf.Max(36,source.triangles.Length*.25f+6),$"Simple pits use roughly one quarter of stone faces (seed {seed}: {triangles.Length/3} / {source.triangles.Length/3})");
                    Require(points.Length==triangles.Distinct().Count(),"Pit cache excludes unused cutter vertices");
                    Require(Vector3.Distance(cutter.bounds.min,source.bounds.min)<.0001f&&
                        Vector3.Distance(cutter.bounds.max,source.bounds.max)<.0001f,"Simpler pits preserve stone footprint and depth");
                    var edges=new Dictionary<(int a,int b),(int count,int balance)>();
                    double volume=0;
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        Vector3 a=points[triangles[i]],b=points[triangles[i+1]],c=points[triangles[i+2]];
                        Require(Vector3.Cross(b-a,c-a).sqrMagnitude>1e-20f,"Cutter faces are nondegenerate");
                        volume+=Vector3.Dot(a,Vector3.Cross(b,c))/6d;
                        for(int j=0;j<3;j++)
                        {
                            int x=triangles[i+j],y=triangles[i+(j+1)%3];
                            var key=x<y?(x,y):(y,x);
                            edges.TryGetValue(key,out var value);
                            edges[key]=(value.count+1,value.balance+(x<y?1:-1));
                        }
                    }
                    Require(edges.Values.All(e=>e.count==2&&e.balance==0),"Every cutter is closed with consistently wound faces");
                    Require(Math.Abs(volume)>1e-12,"Simplified cutters retain solid volume");
                    if(seed==0)
                    {
                        var root=new GameObject("Simplified cutter pit validation");
                        var ground=new Mesh();
                        ground.vertices=new[]{new Vector3(-1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,1),new Vector3(1,0,-1)};
                        ground.triangles=new[]{0,1,2,0,2,3};
                        using var terrain=new StoneTerrainCutout(root.transform);
                        try
                        {
                            terrain.AddSurface(ground,null,"Ground",_=>{});
                            terrain.Subtract(cutter,Matrix4x4.identity);
                            Physics.SyncTransforms();
                            Require(terrain.Raycast(new Ray(new Vector3(0,1,0),Vector3.down),2,out var hit)&&
                                hit.point.y<-.005f,"A simplified small stone still produces a pit floor");
                        }
                        finally
                        {
                            terrain.Dispose();
                            UnityEngine.Object.DestroyImmediate(root);
                            UnityEngine.Object.DestroyImmediate(ground);
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(cutter);
                    UnityEngine.Object.DestroyImmediate(source);
                }
            }
            Debug.Log($"CUTTER_SIMPLIFICATION_VALIDATION_PASS: 32 varied stones and rocks, closed solids, small pit floor ({watch.Elapsed.TotalSeconds:F2}s)");
        }

        private static void ValidateTiles()
        {
            var root = new GameObject("Tile validation");
            var source = new Mesh { name = "300m square crossing tile boundaries" };
            source.vertices = new[] { new Vector3(-150,0,-150), new Vector3(-150,0,150), new Vector3(150,0,150), new Vector3(150,0,-150) };
            source.triangles = new[] { 0,1,2,0,2,3 };
            source.RecalculateNormals(); source.RecalculateBounds();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh cutter = cube.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(cube);
            var colliders = new List<MeshCollider>();
            using var terrain = new StoneTerrainCutout(root.transform);
            try
            {
                var split = terrain.AddTiledSurfaceIncrementally(source, null, "Tile", colliders.Add);
                while (split.MoveNext()) { }
                Require(colliders.Count == 16, "A 300m square spanning negative and positive coordinates creates 16 clipped tiles");
                Require(colliders.All(c => c.sharedMesh.bounds.size.x <= 100.001f && c.sharedMesh.bounds.size.z <= 100.001f), "Tiles are at most 100m on each horizontal axis");
                float area = colliders.Sum(c => Area(c.sharedMesh));
                Require(Mathf.Abs(area - 90000f) < 1f, "Clipping preserves the complete source area without duplicate triangles");
                Physics.SyncTransforms();
                Height(terrain, -149, -149, 0); Height(terrain, 149, 149, 0);
                Height(terrain, -100, 10, 0); Height(terrain, 0, 10, 0); Height(terrain, 100, 10, 0);
                var before = colliders.ToDictionary(c => c, c => c.sharedMesh);
                terrain.BeginBatch();
                terrain.Subtract(cutter, Matrix4x4.TRS(new Vector3(0,0,10), Quaternion.identity, new Vector3(4,1,4)));
                RequireUnexpandedCutters(terrain);
                Height(terrain, 0, 10, 0);
                terrain.EndBatch();
                Physics.SyncTransforms();
                Require(terrain.LastRebuiltSurfaceCount == 2 && colliders.Count(c => c.sharedMesh != before[c]) == 2, "A pit crossing a tile boundary rebuilds only its two touched tiles");
                Height(terrain, -.2f, 10, -.5f); Height(terrain, .2f, 10, -.5f); Height(terrain, 2.2f, 10, 0);
                Require(terrain.OriginalSurface(0,10,out var original) && Mathf.Abs(original.point.y) < .001f, "Original placement height survives the cut");
                var surfaces = (IEnumerable)typeof(StoneTerrainCutout).GetField("surfaces", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(terrain);
                Require(surfaces.Cast<object>().All(s => s.GetType().GetField("Original").GetValue(s) == null), "Completed batches release every temporary tile face cache");
                RequireUnexpandedCutters(terrain);
                terrain.Subtract(cutter, Matrix4x4.TRS(new Vector3(1.5f,-.2f,10), Quaternion.identity, Vector3.one));
                Physics.SyncTransforms();
                Height(terrain, 1.5f, 10, -.7f); Height(terrain, -.2f, 10, -.5f);
                RequireUnexpandedCutters(terrain);
                terrain.Subtract(cutter,Matrix4x4.TRS(new Vector3(110,0,10),Quaternion.identity,new Vector3(-1,.5f,1)));
                Physics.SyncTransforms();
                Height(terrain,110,10,-.25f);
                RequireUnexpandedCutters(terrain);
            }
            finally
            {
                terrain.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static void RequireUnexpandedCutters(StoneTerrainCutout terrain)
        {
            var surfaces=(IEnumerable)typeof(StoneTerrainCutout).GetField("surfaces",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(terrain);
            foreach(object surface in surfaces)
                foreach(object cut in (IEnumerable)surface.GetType().GetField("Cuts").GetValue(surface))
                    Require(cut.GetType().GetField("faces",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cut)==null,
                        "Queued and completed cutters retain only compact mesh data");
        }

        private static void ValidateDistantPits()
        {
            var nearRoot=new GameObject("Nearby pit validation");
            var farRoot=new GameObject("Distant pit validation");
            Vector3 offset=new(7000,1250,9000);
            Vector3[] vertices={new(-50,-15,-50),new(-50,5,50),new(50,15,50),new(50,-5,-50)};
            var nearMesh=new Mesh { vertices=vertices,triangles=new[]{0,1,2,0,2,3} };
            var farMesh=new Mesh { vertices=vertices.Select(v=>v+offset).ToArray(),triangles=nearMesh.triangles };
            using var near=new StoneTerrainCutout(nearRoot.transform);
            using var far=new StoneTerrainCutout(farRoot.transform);
            MeshCollider nearCollider=null,farCollider=null;
            var cutters=new List<Mesh>();
            try
            {
                near.AddSurface(nearMesh,null,"Near terrain",c=>nearCollider=c);
                far.AddSurface(farMesh,null,"Far terrain",c=>farCollider=c);
                Type builderType=typeof(ProceduralRockField).GetNestedType("MeshBuilder",BindingFlags.NonPublic);
                near.BeginBatch();far.BeginBatch();
                for(int seed=0;seed<8;seed++)
                {
                    object builder=Activator.CreateInstance(builderType,true);
                    builderType.GetMethod("AddEllipsoid").Invoke(builder,new object[]{Vector3.zero,Quaternion.identity,
                        .5f,.4f,.7f,new System.Random(500+seed),10,6});
                    Mesh source=(Mesh)builderType.GetMethod("Build").Invoke(builder,new object[]{"Distant pit stone"});
                    Mesh cutter=StoneTerrainCutout.SimplifyCutter(source);
                    UnityEngine.Object.DestroyImmediate(source);
                    cutters.Add(cutter);
                    float x=10+Mathf.Cos(seed)*.18f,z=10+Mathf.Sin(seed)*.18f;
                    Vector3 position=new(x,.1f*x+.2f*z-seed*.02f,z);
                    Quaternion rotation=Quaternion.FromToRotation(Vector3.up,new Vector3(-.1f,1,-.2f).normalized)*Quaternion.Euler(0,29*seed,0);
                    near.Subtract(cutter,Matrix4x4.TRS(position,rotation,Vector3.one));
                    far.Subtract(cutter,Matrix4x4.TRS(position+offset,rotation,Vector3.one));
                }
                near.EndBatch();far.EndBatch();Physics.SyncTransforms();
                for(int i=0;i<3;i++)
                {
                    Vector3 point=new(9.9f+.1f*i,100,10);
                    Require(near.Raycast(new Ray(point,Vector3.down),200,out var nearHit)&&nearHit.point.y<2.95f,
                        "Overlapping irregular stones produce a pit on sloped terrain");
                    bool farFound=far.Raycast(new Ray(point+offset,Vector3.down),200,out var farHit);
                    Require(farFound&&
                        Mathf.Abs(farHit.point.y-offset.y-nearHit.point.y)<.004f,
                        $"Pit depth remains stable at kilometre-scale map coordinates (sample {i}, near {nearHit.point.y:R}, far {farHit.point.y-offset.y:R}, hit {farFound})");
                }
                Require(farCollider.sharedMesh.triangles.Length<nearCollider.sharedMesh.triangles.Length*2+100,
                    "Distant coordinates must not multiply Boolean fragments");
                RequireUnexpandedCutters(near);RequireUnexpandedCutters(far);
                Debug.Log("DISTANT_PIT_VALIDATION_PASS: overlapping irregular cutters, sloped terrain, 7km/9km coordinates");
            }
            finally
            {
                near.Dispose();far.Dispose();
                foreach(Mesh cutter in cutters) UnityEngine.Object.DestroyImmediate(cutter);
                UnityEngine.Object.DestroyImmediate(nearRoot);UnityEngine.Object.DestroyImmediate(farRoot);
                UnityEngine.Object.DestroyImmediate(nearMesh);UnityEngine.Object.DestroyImmediate(farMesh);
            }
        }

        private static void ValidateBakedTiles()
        {
            var root=new GameObject("Bake roundtrip source");
            var loadedRoot=new GameObject("Bake roundtrip destination");
            var source=new StoneTerrainCutout(root.transform);
            var loaded=new StoneTerrainCutout(loadedRoot.transform);
            var mesh=new Mesh { name="Baked terrain tile",vertices=new[]{new Vector3(-50,0,-50),
                new Vector3(-50,0,50),new Vector3(50,0,50),new Vector3(50,0,-50)},triangles=new[]{0,1,2,0,2,3} };
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                source.AddSurface(mesh,null,"Bake source",_=>{});
                source.Subtract(cube.GetComponent<MeshFilter>().sharedMesh,Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(2,1,2)));
                using var bytes=new System.IO.MemoryStream();source.WriteBrowserPitTiles(bytes);bytes.Position=0;
                loaded.AddSurface(mesh,null,"Bake loaded",_=>{});
                var read=(IEnumerator)typeof(StoneTerrainCutout).GetMethod("ReadBrowserPitTiles",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(loaded,new object[]{bytes});
                while(read.MoveNext()) {}
                Physics.SyncTransforms();Height(loaded,0,0,-.5f);Height(loaded,3,0,0);
                Require(loaded.OriginalSurface(0,0,out var original)&&Mathf.Abs(original.point.y)<.002f,
                    "Baked tiles retain their original placement surface");
                RequireUnexpandedCutters(loaded);
                loaded.Subtract(cube.GetComponent<MeshFilter>().sharedMesh,
                    Matrix4x4.TRS(new Vector3(3,0,0),Quaternion.identity,new Vector3(1,.8f,1)));
                Physics.SyncTransforms();Height(loaded,0,0,-.5f);Height(loaded,3,0,-.4f);
                Debug.Log("BROWSER_PIT_TILE_ROUNDTRIP_PASS: cached mesh, original height, cutter history, subsequent cut");
            }
            finally
            {
                source.Dispose();loaded.Dispose();UnityEngine.Object.DestroyImmediate(cube);
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(loadedRoot);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static float Area(Mesh mesh)
        {
            Vector3[] points = mesh.vertices;
            int[] triangles = mesh.triangles;
            float result = 0;
            for (int i=0; i<triangles.Length; i+=3)
                result += Vector3.Cross(points[triangles[i+1]]-points[triangles[i]], points[triangles[i+2]]-points[triangles[i]]).magnitude * .5f;
            return result;
        }
        private static void Height(StoneTerrainCutout terrain, float x, float z, float expected)
        {
            Require(terrain.Raycast(new Ray(new Vector3(x,10,z), Vector3.down), 100, out var hit) &&
                Mathf.Abs(hit.point.y-expected)<.002f, $"Ground at {x},{z} must be {expected}");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
