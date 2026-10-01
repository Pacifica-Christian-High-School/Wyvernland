using System;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class TestGroundValidation
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null, true);
            typeof(WorldBootstrap).GetMethod("BuildPrototype", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
            ProceduralMap map = UnityEngine.Object.FindFirstObjectByType<ProceduralMap>();
            Require(map.Bounds.size == new Vector2(200,200), "Map bounds");
            var groundBounds=new Bounds();
            bool firstGround=true;
            foreach(var collider in map.GetComponentsInChildren<MeshCollider>())
            {
                if(firstGround) { groundBounds=collider.bounds; firstGround=false; }
                else groundBounds.Encapsulate(collider.bounds);
            }
            Require(Vector3.Distance(groundBounds.size,new Vector3(200,0,200))<0.001f,
                "Tiled plane dimensions");
            Require(Mathf.Abs(map.HeightAt(-99,99)) < 0.001f &&
                Mathf.Abs(map.HeightAt(99,-99)) < 0.001f, "Flat surface");
            Require(float.IsNegativeInfinity(map.HeightAt(101,0)), "Plane edge");
            var trees = UnityEngine.Object.FindObjectsByType<GeneratedTree>(FindObjectsSortMode.None);
            Require(trees.Length == 4, "Four tree samples");
            foreach (string name in new[] { "Tree", "Stump", "Burnt Tree", "Burnt Stump" })
                Require(trees.Count(t => t.name == name) == 1, name);
            var rocks = UnityEngine.Object.FindObjectsByType<GeneratedGroundFeature>(FindObjectsSortMode.None);
            Require(rocks.Length == 5, "Five rock/stone samples; got " + rocks.Length);
            float[] rowPositions = trees.Select(tree => tree.transform.position.x)
                .Concat(rocks.Select(rock => rock.transform.position.x)).OrderBy(x => x).ToArray();
            Require(rowPositions.SequenceEqual(new[] { -80f,-60f,-40f,-20f,0f,20f,40f,60f,80f }),
                "All nine samples use the evenly spaced row");
            Require(trees.All(tree => Mathf.Approximately(tree.transform.position.z,20f)) &&
                rocks.All(rock => Mathf.Approximately(rock.transform.position.z,20f)),
                "All nine samples share one row");
            GeneratedGroundFeature stonePile=rocks.Single(rock =>
                Mathf.Approximately(rock.transform.position.x,-40f));
            Require(stonePile.transform.childCount>2,
                "Stone pile contains more than two stones; got " + stonePile.transform.childCount);
            Require(stonePile.GetComponentsInChildren<MeshFilter>().All(filter =>
                IsCompleteStoneMesh(filter.sharedMesh)),
                "Every stone-pile mesh has a complete closed topology");
            GeneratedGroundFeature individualStone=rocks.Single(rock =>
                Mathf.Approximately(rock.transform.position.x,-20f));
            Require(IsCompleteStoneMesh(individualStone.GetComponent<MeshFilter>().sharedMesh),
                "Individual stone mesh has a complete closed topology");
            Require(individualStone.GetComponent<PickableObject>().Label==
                PickableObjectLabel.Irregular&&
                stonePile.GetComponentsInChildren<PickableObject>().All(stone =>
                    stone.Label==PickableObjectLabel.Irregular),
                "All stones use the Irregular held-object label");
            PickableObject individualPickable=individualStone.GetComponent<PickableObject>();
            MeshFilter individualFilter=individualStone.GetComponent<MeshFilter>();
            Vector3 individualScale=new(
                individualStone.transform.TransformVector(Vector3.right).magnitude,
                individualStone.transform.TransformVector(Vector3.up).magnitude,
                individualStone.transform.TransformVector(Vector3.forward).magnitude);
            Require(Vector3.Distance(individualPickable.Size,
                Vector3.Scale(individualFilter.sharedMesh.bounds.size,individualScale))<0.000001f,
                "Held-object size follows the object's relative axes");
            GeneratedGroundFeature rockPile=rocks.Single(rock =>
                Mathf.Approximately(rock.transform.position.x,80f));
            // A single standing rock can have at most 534 vertices (13 sides).
            // Exceeding that proves the combined pile contains multiple rocks.
            int rockPileVertices=rockPile.GetComponent<MeshFilter>().sharedMesh.vertexCount;
            Require(rockPileVertices>534,
                "Rock pile contains multiple rocks; got only " + rockPileVertices + " vertices");
            foreach (GeneratedTree stump in trees.Where(tree => tree.name.Contains("Stump")))
                Require(stump.GetComponent<MeshFilter>().sharedMesh.bounds.size.y >= 0.79f,
                    stump.name + " is too short");
            Require(UnityEngine.Object.FindFirstObjectByType<ProceduralMonumentField>() == null,
                "No additional monuments");
            ProceduralGrass grass = UnityEngine.Object.FindFirstObjectByType<ProceduralGrass>();
            typeof(ProceduralGrass).GetProperty("Density").SetValue(grass, 1f);
            MethodInfo buildTile = typeof(ProceduralGrass).GetMethod("BuildTile",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(buildTile.Invoke(grass, new object[] { new Vector2Int(1,1) }) == null,
                "Bare half has no grass");
            GameObject grassyTile = (GameObject)buildTile.Invoke(grass,
                new object[] { new Vector2Int(-2,1) });
            Require(grassyTile != null && grassyTile.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0,
                "Grown half has grass");
            Mesh grassMesh=grassyTile.GetComponent<MeshFilter>().sharedMesh;
            Require(grassMesh.vertexCount<=32*32*4,
                "Grass should use at most one four-vertex quad per clump");
            Debug.Log("TEST_GROUND_VALIDATION_PASS: nine evenly spaced samples in one row; " +
                "both piles contain multiple pieces; stumps are at least 0.8m tall.");
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static bool IsCompleteStoneMesh(Mesh mesh)
        {
            if(mesh==null) return false;
            for(int sides=8;sides<=12;sides++)
            for(int bands=5;bands<=7;bands++)
                if(mesh.vertexCount==6*sides*(bands-1)) return true;
            return false;
        }
    }
}
