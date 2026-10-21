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
            foreach(var collider in map.GetComponentsInChildren<MeshCollider>().Where(map.IsMapCollider))
            {
                if(firstGround) { groundBounds=collider.bounds; firstGround=false; }
                else groundBounds.Encapsulate(collider.bounds);
            }
            Require(Vector3.Distance(groundBounds.size,new Vector3(200,0,200))<0.001f,
                "Whole plane dimensions");
            GameObject ironBar=GameObject.Find("Iron Bar");
            FirstPersonController player=UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            Require(ironBar!=null&&player!=null,"Iron bar and player are present");
            ThermalMaterialRecord ironMaterial=WorldConfig.ThermalMaterials.Single(
                record=>record.Material=="Iron");
            Require(ironMaterial.MinimumHardness==6f&&ironMaterial.MaximumHardness==8f,
                "Iron material hardness spans 6 through 8");
            Mesh ironMesh=ironBar.GetComponent<MeshFilter>().sharedMesh;
            Require(ironMesh.vertexCount==16&&ironMesh.triangles.Length==84&&
                Mathf.Abs(ironMesh.bounds.size.y-1.4f)<0.00001f&&
                Mathf.Abs(ironMesh.bounds.size.x-0.05f)<0.00001f&&
                Mathf.Abs(ironMesh.bounds.size.z-0.05f)<0.00001f,
                "Iron bar is a closed eight-sided cylinder, 1.4 m long and 5 cm wide");
            Require(MeshSolidVolume.TryMeasure(ironMesh,out double barVolume,
                    out bool openBar)&&!openBar&&
                Mathf.Abs(ironBar.GetComponent<PhysicalDestructible>().MassKg-
                    (float)(barVolume*7870d))<0.001f&&
                ironBar.GetComponent<PickableObject>().CanPickUpWithOneHand&&
                Mathf.Abs(ironBar.GetComponent<PhysicalDestructible>().Hardness-7f)<0.0001f&&
                Mathf.Abs(ironBar.GetComponent<PhysicalDestructible>().Resilience-8f)<0.0001f,
                "Iron bar has its catalog hardness, iron mass, and can be picked up");
            Vector3 ahead=ironBar.transform.position-player.transform.position;
            Require(Vector3.Dot(ahead,player.transform.forward)>2.4f&&
                Vector3.Dot(ahead,player.transform.forward)<2.6f,
                "Iron bar is placed directly ahead of the Test Ground spawn");
            Require(Mathf.Abs(map.HeightAt(-99,99)) < 0.001f &&
                Mathf.Abs(map.HeightAt(99,-99)) < 0.001f, "Flat surface");
            GameObject breakableBar=UnityEngine.Object.Instantiate(ironBar);
            breakableBar.name="Iron Bar Destruction Probe";
            breakableBar.transform.position+=Vector3.up*5f;
            var breakableProperties=breakableBar.GetComponent<PhysicalDestructible>();
            Require(breakableProperties.SolidVolumeCubicMetres>=
                PhysicalDestructible.MinimumFragmentationVolume&&
                breakableProperties.ApplyImpact(10f,10f,1f,breakableBar.transform.position,
                    Vector3.right,30f)&&
                UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(FindObjectsSortMode.None)
                    .Count(part=>part.name=="Iron Bar Destruction Probe fragment")>=2,
                "The resized iron bar has enough volume to shatter under a strong impact");
            foreach(var fragment in UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(
                FindObjectsSortMode.None).Where(part=>
                    part.name=="Iron Bar Destruction Probe fragment"))
                UnityEngine.Object.DestroyImmediate(fragment.gameObject);
            if(breakableBar!=null) UnityEngine.Object.DestroyImmediate(breakableBar);
            Require(float.IsNegativeInfinity(map.HeightAt(101,0)), "Plane edge");
            var trees = UnityEngine.Object.FindObjectsByType<GeneratedTree>(FindObjectsSortMode.None);
            Require(trees.Length == 6, "Six tree samples");
            foreach (string name in new[] { "Tree", "Stump", "Burnt Tree", "Burnt Stump",
                "Short Tree", "Tall Tree" })
                Require(trees.Count(t => t.name == name) == 1, name);
            GeneratedTree shortTree=trees.Single(tree=>tree.name=="Short Tree");
            GeneratedTree tallTree=trees.Single(tree=>tree.name=="Tall Tree");
            Require(shortTree.transform.position==new Vector3(0f,0f,45f)&&
                tallTree.transform.position==new Vector3(20f,0f,45f)&&
                tallTree.GetComponentsInChildren<MeshFilter>().Max(filter=>
                    filter.sharedMesh.bounds.max.y)-
                shortTree.GetComponentsInChildren<MeshFilter>().Max(filter=>
                    filter.sharedMesh.bounds.max.y)>6f,
                "Second-row trees have distinct heights");
            foreach(GeneratedTree fullTree in trees.Where(tree=>!tree.name.Contains("Stump")))
            {
                Transform midpoint=fullTree.transform.Find("Tree midpoint break point");
                var trunk=fullTree.GetComponentsInChildren<TreeStructuralPart>()
                    .Where(part=>part.IsTrunk).Select(part=>part.GetComponent<Renderer>().bounds)
                    .ToArray();
                float halfway=(trunk.Min(bounds=>bounds.min.y)+
                    trunk.Max(bounds=>bounds.max.y))*0.5f;
                Require(midpoint!=null&&Mathf.Abs(midpoint.position.y-halfway)<0.001f&&
                    fullTree.GetComponentsInChildren<TextMesh>().Any(label=>
                        label.text=="Breaking Point"),
                    fullTree.name+" shows Breaking Point at its trunk halfway height");
            }
            var rocks = UnityEngine.Object.FindObjectsByType<GeneratedGroundFeature>(FindObjectsSortMode.None);
            Require(rocks.Length == 5, "Five rock/stone samples; got " + rocks.Length);
            float[] rowPositions = trees.Where(tree=>Mathf.Approximately(tree.transform.position.z,20f))
                .Select(tree => tree.transform.position.x)
                .Concat(rocks.Select(rock => rock.transform.position.x)).OrderBy(x => x).ToArray();
            float[] expectedRow={ -80f,-60f,-40f,-20f,0f,20f,40f,60f,80f };
            Require(rowPositions.Length==expectedRow.Length&&
                rowPositions.Zip(expectedRow,(actual,expected)=>Mathf.Abs(actual-expected)<0.001f).All(equal=>equal),
                "All nine samples use the evenly spaced row: "+string.Join(", ",rowPositions));
            Require(trees.Where(tree=>tree!=shortTree&&tree!=tallTree).All(tree =>
                    Mathf.Approximately(tree.transform.position.z,20f)) &&
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
                Vector3.Scale(individualFilter.sharedMesh.bounds.size,individualScale))<0.0001f,
                "Held-object size follows the object's relative axes");
            GeneratedGroundFeature rockPile=rocks.Single(rock =>
                Mathf.Approximately(rock.transform.position.x,80f));
            // A single standing rock can have at most 534 vertices (13 sides).
            // Exceeding that across its physical children proves the pile contains multiple rocks.
            int rockPileVertices=rockPile.GetComponentsInChildren<MeshFilter>().Sum(filter=>filter.sharedMesh.vertexCount);
            Require(rockPileVertices>534,
                "Rock pile contains multiple rocks; got only " + rockPileVertices + " vertices");
            foreach (GeneratedTree stump in trees.Where(tree => tree.name.Contains("Stump")))
                Require(stump.GetComponentsInChildren<MeshFilter>().Max(filter=>
                    filter.sharedMesh.bounds.max.y) >= 0.79f,
                    stump.name + " is too short");
            Require(UnityEngine.Object.FindFirstObjectByType<ProceduralMonumentField>() == null,
                "No additional monuments");
            ProceduralGrass grass = UnityEngine.Object.FindFirstObjectByType<ProceduralGrass>();
            Require(grass.ResidentPatchCount==64,"Grass retains all patches for the test ground");
            Mesh grassMesh=(Mesh)typeof(ProceduralGrass).GetField("mesh",
                BindingFlags.Instance|BindingFlags.NonPublic).GetValue(grass);
            Require(grassMesh.vertexCount==32*32*4,
                "All resident grass patches share one compact mesh");
            Debug.Log("TEST_GROUND_VALIDATION_PASS: nine evenly spaced samples in one row, " +
                "two trees of different heights behind them; both piles contain multiple pieces; " +
                "stumps are at least 0.8m tall.");
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
