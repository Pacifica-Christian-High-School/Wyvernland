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
                field.Initialize(player,map);
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
                PhysicalDestructible struckRock=rocks[0];
                string struckName=struckRock.name;
                var terrainBeforeRock=map.GetComponentsInChildren<MeshCollider>()
                    .Where(map.IsMapCollider).ToDictionary(c=>c,c=>c.sharedMesh);
                Require(struckRock.ApplyImpact(10f,10f,struckRock.WorldSize()*0.6f,
                    struckRock.GetComponent<Collider>().bounds.center,Vector3.up,30f),
                    "An individual pile rock can be destroyed by a hard impact");
                Require(map.LastStoneCutoutTileCount>0&&terrainBeforeRock.Any(tile=>
                    tile.Key!=null&&tile.Key.sharedMesh!=tile.Value),
                    "A rock's first destruction subtracts its original shape from terrain");
                var rockFragments=UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(
                    FindObjectsSortMode.None).Where(piece=>
                        piece.name==struckName+" fragment").ToArray();
                Require(rockFragments.Length>=2&&rockFragments.All(piece=>
                    DestructionMesh.IsClosedConnected(piece.GetComponent<MeshFilter>().sharedMesh)),
                    "Pile rock destruction creates separate complete fragments");
                foreach(var fragment in rockFragments)
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                float height=map.HeightAt(-20,20);
                Require(Mathf.Abs(height)<0.001f,"Loading must not generate any pits");
                var stone=root.GetComponentsInChildren<PickableObject>()
                    .Single(p=>p.name=="Low-poly individual stone");
                Require(!stone.HasBeenPickedUp,"Loading must not mark the stone collected");
                Vector3 position=stone.transform.position;
                var meshes=map.GetComponentsInChildren<MeshCollider>()
                    .Where(c=>map.IsMapCollider(c)).ToDictionary(c=>c,c=>c.sharedMesh);
                // Recreate the same deterministic entry without querying the carved floor.
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var entries=(System.Collections.IEnumerable)typeof(ProceduralRockField).GetField("entries",flags).GetValue(field);
                object entry=null;
                foreach(var value in entries)
                    if((GameObject)value.GetType().GetField("Instance").GetValue(value)==stone.gameObject) entry=value;
                Require(entry!=null,"Could not find the prepared stone entry");
                typeof(ProceduralRockField).GetMethod("Release",flags).Invoke(field,new[]{entry});
                typeof(ProceduralRockField).GetMethod("Create",flags).Invoke(field,new[]{entry});
                var recreated=(GameObject)entry.GetType().GetField("Instance").GetValue(entry);
                Require(Vector3.Distance(recreated.transform.position,position)<0.00001f,"Streaming moved the stone into its pit");
                stone=recreated.GetComponent<PickableObject>();
                hand=new GameObject("Temporary pickup hand"); hand.transform.position=position+Vector3.up;
                stone.PickUp(hand.transform);
                Require(meshes.Any(pair=>pair.Key.sharedMesh!=pair.Value),"First pickup did not generate a pit");
                Require(map.HeightAt(-20,20)<-0.01f,"Picked stone's pit is missing");
                Require(Mathf.Abs(map.HeightAt(-40,20))<0.001f,"Pickup excavated an unrelated stone pile");
                var after=meshes.Keys.ToDictionary(c=>c,c=>c.sharedMesh);
                stone.Release(Vector3.zero,hand.transform);
                stone.PickUp(hand.transform);
                Require(after.All(pair=>pair.Key.sharedMesh==pair.Value),"Repeated pickup cut a second pit");
                Debug.Log("STONE_PIT_LOADING_PASS: separate closed pile rocks destruct; no pits at load; first pickup creates only its own pit; repeated pickup does not rebuild terrain.");
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
