using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    // Runs in Edit mode against temporary scene objects only.
    public static class StonePitLoadingValidation
    {
        [InitializeOnLoadMethod]
        private static void CheckRequest()
        {
            const string request="Artifacts/stone-pit-load-validation.request";
            if(!System.IO.File.Exists(request)) return;
            EditorApplication.delayCall+=()=>
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode) return;
                System.IO.File.Delete(request);
                try
                {
                    Run();
                    System.IO.File.WriteAllText("Artifacts/stone-pit-load-validation-result.txt","STONE_PIT_LOADING_PASS");
                }
                catch(Exception error)
                {
                    System.IO.File.WriteAllText("Artifacts/stone-pit-load-validation-result.txt",error.ToString());
                    Debug.LogException(error);
                }
            };
        }
        [MenuItem("Tools/Wyvernland/Validate Loaded Stone Pits")]
        public static void Run()
        {
            bool previous=WorldBootstrap.TestGround;
            var root=new GameObject("Stone pit loading validation");
            GameObject hand=null;
            try
            {
                typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,true);
                var map=root.AddComponent<ProceduralMap>();
                map.Build();
                Require(Mathf.Abs(map.HeightAt(-20,20))<0.001f,"Initial ground should be flat");
                var observer=new GameObject("Observer"); observer.transform.SetParent(root.transform);
                var player=observer.AddComponent<FirstPersonController>();
                var field=root.AddComponent<ProceduralRockField>();
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var beforeLoading=map.GetComponentsInChildren<MeshCollider>()
                    .Where(map.IsMapCollider).ToDictionary(c=>c,c=>c.sharedMesh);
                var creation=(System.Collections.IEnumerator)typeof(ProceduralRockField)
                    .GetMethod("InitializeIncrementally",flags).Invoke(field,new object[]{player,map,null});
                while(creation.MoveNext()) { }
                Require(beforeLoading.All(pair=>pair.Key.sharedMesh==pair.Value)&&
                    Mathf.Abs(map.HeightAt(-20,20))<0.001f,
                    "All stones and rocks are placed on the original terrain before pit preparation starts");
                var preload=(System.Collections.IEnumerator)typeof(ProceduralRockField)
                    .GetMethod("PreloadTerrainPitsIncrementally",flags).Invoke(field,new object[]{null});
                while(preload.MoveNext()) { }
                Require(map.LastStoneCutoutSurfaceCount>0&&
                    beforeLoading.Count(pair=>pair.Key.sharedMesh!=pair.Value)==map.LastStoneCutoutSurfaceCount,
                    "Pits rebuild only affected terrain tiles during loading");
                Require(map.HeightAt(-20,20)<-0.01f&&map.HeightAt(-60,20)<-0.1f,
                    "Individual stone and embedded rock Boolean pits exist before gameplay");
                Require(root.GetComponentsInChildren<PickableObject>().All(stone=>!stone.HasBeenPickedUp),
                    "Preloading pits does not collect any stones");
                Require(map.TryGetOriginalStoneSurface(-20,20,out var original)&&Mathf.Abs(original.point.y)<.001f,
                    "Original placement surfaces remain available after the terrain is cut");
                Physics.SyncTransforms();
                var rockPile=root.GetComponentsInChildren<GeneratedGroundFeature>()
                    .Single(feature=>feature.name=="Low-poly rock pile");
                var rocks=rockPile.GetComponentsInChildren<PhysicalDestructible>();
                Require(rocks.Length>1&&rockPile.GetComponent<PhysicalDestructible>()==null&&
                    rockPile.GetComponent<Collider>()==null&&
                    rocks.All(rock=>rock.transform.parent==rockPile.transform&&
                        rock.GetComponent<MeshCollider>()?.sharedMesh==
                            rock.GetComponent<MeshFilter>()?.sharedMesh&&
                        DestructionMesh.IsClosedConnected(
                            rock.GetComponent<MeshFilter>().sharedMesh)),
                    "Every rock in a rock pile has its own closed mesh, collider, and destruction target");
                Require(rocks.All(rock=>
                    DestructionMesh.VolumeCentroid(
                        rock.GetComponent<MeshFilter>().sharedMesh).magnitude<0.001f),
                    "Rock pivots are centred on the individual rock meshes");
                PhysicalDestructible struckRock=rocks.OrderBy(rock=>rock.WorldSize()).First();
                string struckName=struckRock.name;
                var terrainBeforeRock=map.GetComponentsInChildren<MeshCollider>()
                    .Where(map.IsMapCollider).ToDictionary(c=>c,c=>c.sharedMesh);
                Require(struckRock.ApplyImpact(10f,struckRock.Resilience,struckRock.WorldSize()*0.6f,
                    struckRock.GetComponent<Collider>().bounds.center,Vector3.up,30f),
                    "An individual pile rock can be destroyed by a hard impact");
                Require(terrainBeforeRock.All(tile=>tile.Key!=null&&tile.Key.sharedMesh==tile.Value),
                    "Rock destruction uses the preloaded pit without rebuilding terrain");
                var rockFragments=UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(
                    FindObjectsSortMode.None).Where(piece=>
                        piece.name==struckName+" fragment").ToArray();
                int minimumFragments=struckRock==null||struckRock.HasBeenDestroyed?2:1;
                Require(rockFragments.Length>=minimumFragments&&rockFragments.All(piece=>
                    DestructionMesh.IsClosedConnected(piece.GetComponent<MeshFilter>().sharedMesh)),
                    "Pile rock destruction creates separate complete fragments");
                foreach(var fragment in rockFragments)
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                float height=map.HeightAt(-20,20);
                Require(height<-0.01f,"The stone's pit is already present after loading");
                var stone=root.GetComponentsInChildren<PickableObject>()
                    .Single(p=>p.name=="Low-poly individual stone");
                // Make this pickup fixture light enough for the one-hand limit.
                stone.transform.localScale=Vector3.one*0.4f;
                Vector3 smallPosition=stone.transform.position;
                smallPosition.y*=0.4f;
                stone.transform.position=smallPosition;
                Require(!stone.HasBeenPickedUp,"Loading must not mark the stone collected");
                Vector3 position=stone.transform.position;
                var meshes=map.GetComponentsInChildren<MeshCollider>()
                    .Where(c=>map.IsMapCollider(c)).ToDictionary(c=>c,c=>c.sharedMesh);
                // Moving the observer must leave the original resident object in place.
                var entries=(System.Collections.IEnumerable)typeof(ProceduralRockField).GetField("entries",flags).GetValue(field);
                object entry=null;
                foreach(var value in entries)
                    if((GameObject)value.GetType().GetField("Instance").GetValue(value)==stone.gameObject) entry=value;
                Require(entry!=null,"Could not find the prepared stone entry");
                observer.transform.position=new Vector3(9000,0,9000);
                typeof(ProceduralRockField).GetMethod("Refresh",flags).Invoke(field,null);
                var recreated=(GameObject)entry.GetType().GetField("Instance").GetValue(entry);
                Require(recreated==stone.gameObject&&recreated.activeInHierarchy&&
                    Vector3.Distance(recreated.transform.position,position)<0.00001f,"Observer movement changed or unloaded a resident stone");
                stone=recreated.GetComponent<PickableObject>();
                hand=new GameObject("Temporary pickup hand"); hand.transform.position=position+Vector3.up;
                Require(stone.PickUp(hand.transform),"Pit fixture must be within the one-hand mass limit");
                Require(meshes.All(pair=>pair.Key.sharedMesh==pair.Value),"First pickup rebuilt terrain instead of using its preloaded pit");
                Require(Mathf.Abs(map.HeightAt(-20,20)-height)<0.001f,"Pickup changed the preloaded pit");
                Require(Mathf.Abs(map.HeightAt(90,90))<0.001f,"Preloading changed unrelated terrain");
                var after=meshes.Keys.ToDictionary(c=>c,c=>c.sharedMesh);
                stone.Release(Vector3.zero,hand.transform);
                stone.PickUp(hand.transform);
                Require(after.All(pair=>pair.Key.sharedMesh==pair.Value),"Repeated pickup cut a second pit");
                preload=(System.Collections.IEnumerator)typeof(ProceduralRockField)
                    .GetMethod("PreloadTerrainPitsIncrementally",flags).Invoke(field,new object[]{null});
                while(preload.MoveNext()) { }
                Require(after.All(pair=>pair.Key.sharedMesh==pair.Value),"Running completed pit preparation again rebuilt terrain");
                StoneTerrainCutoutValidation.Run();
                Debug.Log("STONE_PIT_LOADING_PASS: all stones placed before batched Boolean pits; cuts ready before gameplay; rock destruction and first/repeated pickup leave terrain unchanged; preparation is idempotent.");
            }
            finally
            {
                if(hand!=null) UnityEngine.Object.DestroyImmediate(hand);
                UnityEngine.Object.DestroyImmediate(root);
                typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,previous);
            }
        }
        private static void Require(bool condition,string message)
        { if(!condition) throw new Exception(message); }
    }
}
