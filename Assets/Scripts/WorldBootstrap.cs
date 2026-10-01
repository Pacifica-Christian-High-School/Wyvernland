using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Wyvernland
{
    public static class WorldBootstrap
    {
        private const float FogStrength = 0.5f;

        public static bool TestGround { get; private set; }
        public static bool EnterAfterBuild { get; private set; }

        public static void SelectMap(bool testGround)
        {
            TestGround = testGround;
            EnterAfterBuild = true;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            UnityEngine.SceneManagement.SceneManager.LoadScene("SampleScene");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoading()
        {
            TestGround = false;
            EnterAfterBuild = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode) => BuildPrototype();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BuildPrototype()
        {
            if (Object.FindFirstObjectByType<ProceduralMap>() != null)
                return;

            DisableTemplateObjects();
            ConfigureEnvironment();

            GameObject world = new("Wyvernland Prototype");
            world.AddComponent<PhysicalDestructionWorld>();
            ProceduralMap map = world.AddComponent<ProceduralMap>();
            Debug.Log("WYVERNLAND_STARTUP map begin");
            map.Build();
            Debug.Log("WYVERNLAND_STARTUP map complete");

            Light sun = CreateSun(world.transform);
            CreateSky(world, sun);
            CreateLightingVolume(world);
            FirstPersonController player = CreatePlayer(world.transform, map);
            Debug.Log("WYVERNLAND_STARTUP player complete");
            WorldObjectStreaming streaming=world.AddComponent<WorldObjectStreaming>();
            // Monument interiors must be known before trees, rocks and grass are placed.
            if (!TestGround) world.AddComponent<ProceduralMonumentField>().Initialize(player,map);
            Debug.Log("WYVERNLAND_STARTUP monuments complete");
            streaming.Initialize(player, map);
            Debug.Log("WYVERNLAND_STARTUP trees complete");
            world.AddComponent<ProceduralRockField>().Initialize(player, map);
            Debug.Log("WYVERNLAND_STARTUP rocks complete");
            world.AddComponent<ProceduralGrass>().Initialize(player, map);
            Debug.Log("WYVERNLAND_STARTUP grass complete");
            if (TestGround) CreateGoldenTestStone(world, map);
            world.AddComponent<PrototypeHud>().Initialize(player);
            world.AddComponent<PauseMenu>().Initialize(player);
            EnterAfterBuild = false;
        }

        private static void DisableTemplateObjects()
        {
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                camera.gameObject.SetActive(false);

            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                light.gameObject.SetActive(false);

            foreach (Volume volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                volume.gameObject.SetActive(false);
        }

        private static void ConfigureEnvironment()
        {
            Shader.SetGlobalFloat("_WyvernBetterLighting", 1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            // Retain the density value for any material that switches to an
            // exponential fog mode, even though the world currently uses linear fog.
            RenderSettings.fogDensity = 0.005f;
            RenderSettings.fogColor = new Color(0.90f, 0.935f, 0.96f, 1f);
            RenderSettings.fogStartDistance = 135f;
            RenderSettings.fogEndDistance = 1650f;
            ApplyFogState(true);
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.82f;
            RenderSettings.reflectionIntensity = 0f;
        }

        public static void ApplyFogState(bool enabled)
        {
            RenderSettings.fog=enabled;
            Shader.SetGlobalColor("_WyvernFogColor",RenderSettings.fogColor);
            Shader.SetGlobalVector("_WyvernFogParams",new Vector4(RenderSettings.fogStartDistance,
                RenderSettings.fogEndDistance,enabled?FogStrength:0f,0f));
        }

        private static Light CreateSun(Transform parent)
        {
            GameObject sunObject = new("Sun");
            sunObject.transform.SetParent(parent, false);
            sunObject.transform.rotation = Quaternion.Euler(35f, -32f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Color.white;
            sun.useColorTemperature = true;
            sun.colorTemperature = 5600f;
            sun.intensity = 3.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sun.shadowBias = 0.12f;
            sun.shadowNormalBias = 0.35f;
            RenderSettings.sun = sun;
            return sun;
        }

        private static void CreateSky(GameObject world, Light sun)
        {
            Shader shader = Resources.Load<Shader>("AtmosphericSky");
            if (shader == null)
                throw new System.InvalidOperationException("Missing atmospheric sky shader.");

            Material material = new(shader) { name = "Wyvernland Atmospheric Sky" };
            material.SetColor("_ZenithColor", new Color(0.055f, 0.27f, 0.67f, 1f));
            material.SetColor("_HorizonColor", Color.white);
            material.SetColor("_GroundColor", Color.white);
            material.SetColor("_SunColor", new Color(4.6f, 3.55f, 2.35f, 1f));
            material.SetVector("_SunDirection", -sun.transform.forward);
            material.SetFloat("_SunSize", 0.62f);
            material.SetFloat("_Haze", 0.36f);
            RenderSettings.skybox = material;
            world.AddComponent<RuntimeSky>().Initialize(material);
            DynamicGI.UpdateEnvironment();
        }

        private static void CreateLightingVolume(GameObject world)
        {
            Volume volume = world.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            // Volume.profile is owned by this runtime volume and cleaned up by Unity.
            VolumeProfile profile = volume.profile;
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            ColorAdjustments grading = profile.Add<ColorAdjustments>(true);
            grading.postExposure.Override(0.15f);
            grading.contrast.Override(5f);
            DepthOfField depth = profile.Add<DepthOfField>(true);
            depth.mode.Override(DepthOfFieldMode.Off);
            // The map is measured in metres, so only distant terrain receives a
            // subtle blur. Nearby ground and the player's body remain crisp.
            depth.gaussianStart.Override(300f);
            depth.gaussianEnd.Override(1800f);
            // Default High texture quality uses 70% of the reduced 0.325 radius.
            depth.gaussianMaxRadius.Override(0.2275f);
            depth.highQualitySampling.Override(true);
        }

        private static FirstPersonController CreatePlayer(Transform parent, ProceduralMap map)
        {
            // Start in the broad central basin instead of on the central ridge. This gives
            // an immediate ground-level view with the surrounding contour peaks ahead.
            Vector3 spawn = TestGround ? new Vector3(0f, 0f, -35f) : new Vector3(250f, 0f, 350f);
            // Player origin remains at the soles so the animated foot colliders
            // support the Rigidbody directly on the terrain.
            spawn.y = map.HeightAt(spawn.x, spawn.z) + 0.15f;

            GameObject playerObject = new("Player (1.8m)");
            playerObject.transform.SetParent(parent, false);
            playerObject.transform.position = spawn;
            playerObject.transform.rotation = Quaternion.Euler(0f, TestGround ? 0f : -24f, 0f);

            Rigidbody playerBody = playerObject.AddComponent<Rigidbody>();
            playerBody.mass = FirstPersonController.PlayerMassKg;
            playerBody.useGravity = false;
            playerBody.interpolation = RigidbodyInterpolation.Interpolate;
            playerBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            playerBody.constraints = RigidbodyConstraints.FreezeRotation;

            // A short central capsule prevents the fast-moving body from slipping
            // between steep terrain triangles. Animated foot meshes remain the
            // detailed ground-contact colliders.
            GameObject capsuleObject = new("Short Body CapsuleCollider");
            capsuleObject.layer = 2; // Excluded from the player's own ground probes.
            capsuleObject.transform.SetParent(playerObject.transform, false);
            CapsuleCollider capsule = capsuleObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.center = new Vector3(0f, 0.65f, 0f);
            capsule.radius = 0.14f;
            capsule.height = 1.10f;

            GameObject cameraObject = new("Player Camera");
            cameraObject.transform.SetParent(playerObject.transform, false);
            // RiggedPlayerAvatar snaps this to the Waist-bound head centre after
            // it instantiates the player model.
            cameraObject.transform.localPosition = new Vector3(0f, 1.71f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = WorldConfig.SkyColor;
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 12000f;
            camera.allowHDR = true;
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderShadows = true;
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraObject.AddComponent<AudioListener>();

            FirstPersonController controller = playerObject.AddComponent<FirstPersonController>();
            controller.Initialize(cameraObject.transform);
            RiggedPlayerAvatar avatar=playerObject.AddComponent<RiggedPlayerAvatar>();
            avatar.Initialize(controller);
            playerObject.AddComponent<PlayerPickupController>().Initialize(controller,avatar);
            return controller;
        }

        private static void CreateGoldenTestStone(GameObject world,ProceduralMap map)
        {
            GameObject stone=GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.name="Golden Stone";
            stone.transform.SetParent(world.transform,false);
            stone.transform.localScale=new Vector3(0.3f,0.3f,0.3f);
            stone.transform.position=new Vector3(0f,map.HeightAt(0f,0f)+0.15f,0f);

            Material material=new(Resources.Load<Shader>("Clay")) { name="Golden Stone" };
            material.SetColor("_BaseColor",new Color(0.95f,0.68f,0.12f));
            material.SetFloat("_TerrainColorRegions",0f);
            material.SetFloat("_Cull",0f);
            stone.GetComponent<MeshRenderer>().sharedMaterial=material;
            world.AddComponent<RuntimeMaterialOwner>().Initialize(material);

            stone.AddComponent<Rigidbody>();
            stone.AddComponent<PickableObject>().DisplayName="Golden Stone";
            PhysicalDestructible properties=PhysicalDestructible.Ensure(stone);
            properties.Hardness=10f;
            properties.Resilience=10f;
        }
    }

    public sealed class RuntimeMaterialOwner : MonoBehaviour
    {
        private Material material;

        public void Initialize(Material value) => material=value;

        private void OnDestroy()
        {
            if(material!=null) Destroy(material);
        }
    }

    public sealed class RuntimeSky : MonoBehaviour
    {
        private Material material;

        public void Initialize(Material value) => material = value;

        private void OnDestroy()
        {
            if (RenderSettings.skybox == material)
                RenderSettings.skybox = null;
            if (material != null)
                Destroy(material);
        }
    }

}
