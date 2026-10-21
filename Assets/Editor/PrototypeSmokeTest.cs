using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Wyvernland.Editor
{
    /// <summary>
    /// Headless construction test used to verify the generated world without changing the scene asset.
    /// Run with: -executeMethod Wyvernland.Editor.PrototypeSmokeTest.Run
    /// </summary>
    public static class PrototypeSmokeTest
    {
        [MenuItem("Wyvernland/Validate Floating Islands")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

            // Simulate an interrupted paused session. Entering from the main
            // menu must recover even when Unity begins with time frozen.
            Time.timeScale=0f;

            MethodInfo bootstrap = typeof(WorldBootstrap).GetMethod(
                "BuildPrototype",
                BindingFlags.NonPublic | BindingFlags.Static);
            Require(bootstrap != null, "Could not locate the world bootstrap method.");
            bootstrap.Invoke(null, null);

            MeshCollider[] allMeshColliders = UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None);
            MeshCollider[] islands = allMeshColliders.Where(collider => collider.name.StartsWith("Floating Island ")).ToArray();
            MeshCollider[] feet = allMeshColliders.Where(collider => collider.name.Contains("Foot MeshCollider")).ToArray();
            CapsuleCollider safetyCapsule = UnityEngine.Object.FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None)
                .SingleOrDefault(collider => collider.name == "Short Body CapsuleCollider");
            Rigidbody player = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)
                .Single(body => body.GetComponent<FirstPersonController>() != null);
            FirstPersonController controller=player.GetComponent<FirstPersonController>();
            PauseMenu pauseMenu=UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
            Camera camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            ProceduralMap map = UnityEngine.Object.FindFirstObjectByType<ProceduralMap>();
            Volume volume = UnityEngine.Object.FindFirstObjectByType<Volume>();
            GeneratedGroundFeature[] groundFeatures = UnityEngine.Object.FindObjectsByType<GeneratedGroundFeature>(FindObjectsSortMode.None);
            GeneratedGroundFeature[] monuments = groundFeatures.Where(feature =>
                feature.name == "Ancient Lowland Monument").ToArray();

            Require(islands.Length>=3&&islands.All(c=>c.enabled)&&
                islands.Select(c=>c.name.Split(new[]{" tile "},System.StringSplitOptions.None)[0]).Distinct().Count()==3&&
                islands.All(c=>c.sharedMesh.bounds.size.x<=100.001f&&c.sharedMesh.bounds.size.z<=100.001f),
                "Expected three islands divided into permanently collidable 100m tiles.");
            Require(UnityEngine.Object.FindFirstObjectByType<Terrain>() == null, "Legacy square terrain still exists.");
            Require(player != null, "Player Rigidbody was not created.");
            Require(pauseMenu!=null,"Pause menu was not created.");
            MethodInfo enterGame=typeof(PauseMenu).GetMethod("EnterGame",
                BindingFlags.NonPublic|BindingFlags.Instance);
            Require(enterGame!=null,"Could not locate main-menu Enter action.");
            enterGame.Invoke(pauseMenu,null);
            Require(Time.timeScale>0f&&!controller.IsPaused,
                "Entering the game did not restore movement from a zero time scale.");
            Require(feet.Length == 2 && feet.All(foot => foot.convex),
                "Expected two convex player foot MeshColliders.");
            Require(safetyCapsule != null && safetyCapsule.height < 1.8f,
                "Expected a shortened player safety capsule.");
            Require(camera != null, "Player camera was not created.");
            Require(camera.transform.localPosition.y >= 1.6f, "Player camera is below the model's head position.");
            Require(map != null, "ProceduralMap component was not created.");
            Require(volume != null && volume.profile.TryGet<DepthOfField>(out _), "Depth of Field volume effect was not created.");
            Require(GroundFeatureLayout.Features.Any(feature => feature.Kind == GroundFeatureKind.StandingRock) &&
                GroundFeatureLayout.Features.Any(feature => feature.Kind == GroundFeatureKind.EmbeddedRock) &&
                GroundFeatureLayout.Features.Any(feature => feature.Kind == GroundFeatureKind.IndividualStone),
                "Ground feature layout is missing a rock or stone type.");
            Require(groundFeatures.Length > 0, "No resident ground features were generated.");
            Require(monuments.Length == 5, "Expected exactly five ancient monuments.");
            Require(monuments.All(monument => ProceduralMap.IsOnMainIsland(
                monument.transform.position.x,monument.transform.position.z)),
                "A monument was placed outside the main island.");
            Require(monuments.All(monument => monument.GetComponentInChildren<MeshCollider>() != null),
                "An ancient monument is missing its mesh collider.");
            Require(groundFeatures.All(feature => feature.GetComponent<Rigidbody>() == null),
                "Ground features must not have rigidbodies.");
            foreach (MeshCollider island in islands)
            {
                Require(island.GetComponent<MeshRenderer>().sharedMaterial.shader.name == "Wyvernland/Unfired Clay", "Clay shader missing.");
                Require(island.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_FlatTerrainNormal")>.5f,
                    "Terrain rendering must retain flat triangle shading.");
            }
            foreach(var group in islands.GroupBy(c=>c.name.Split(new[]{" tile "},System.StringSplitOptions.None)[0]))
                Require(group.Min(c=>c.sharedMesh.bounds.min.y)<-60f,"Island has no underside.");
            Require(float.IsNegativeInfinity(map.HeightAt(-2490f, -3240f)), "The void still has a floor.");
            Require(float.IsFinite(map.HeightAt(player.transform.position.x, player.transform.position.z)), "Spawn is outside the islands.");

            float highestSample = 0f;
            for (int z = 0; z <= 20; z++)
            {
                for (int x = 0; x <= 20; x++)
                {
                    float worldX = Mathf.Lerp(WorldConfig.MapBounds.xMin, WorldConfig.MapBounds.xMax, x / 20f);
                    float worldZ = Mathf.Lerp(WorldConfig.MapBounds.yMin, WorldConfig.MapBounds.yMax, z / 20f);
                    highestSample = Mathf.Max(highestSample, map.HeightAt(worldX, worldZ));
                }
            }
            Require(highestSample >= 250f && highestSample <= WorldConfig.MaximumElevation, "Terrain elevation is outside the expected contour range.");
            Require(GameObject.Find("Ocean (Sea Level 0m)") == null, "Ocean surface should be removed.");

            RenderOverview(camera);
            Debug.Log($"WYVERNLAND_SMOKE_TEST_PASS islands={islands.Length} footColliders={feet.Length} sampledPeak={highestSample:0.0}m");
        }

        private static void RenderOverview(Camera camera)
        {
            const int width = 1280;
            const int height = 720;
            camera.gameObject.SetActive(true);
            camera.transform.SetParent(null);
            camera.transform.position = new Vector3(0f, 5200f, -4700f);
            camera.transform.LookAt(new Vector3(0f, 180f, 300f));
            camera.fieldOfView = 58f;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 15000f;

            RenderTexture target = new(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;

            Texture2D image = new(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();

            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts"));
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(Path.Combine(outputDirectory, "terrain-overview.png"), image.EncodeToPNG());

            camera.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
