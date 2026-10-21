using System.Collections;
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
            if(WebBrowserSession.SelectedMap>=0) testGround=WebBrowserSession.SelectedMap==1;
            TestGround = testGround;
            EnterAfterBuild = true;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            UnityEngine.SceneManagement.SceneManager.LoadScene("SampleScene");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoading()
        {
            // Keep the loading coroutine advancing after Alt-Tab or minimization.
            Application.runInBackground=true;
            if(Application.platform==RuntimePlatform.WebGLPlayer)
            {
                Time.maximumDeltaTime=1f/15f;
                // Apply once before generating or baking any appearance data.
                // The in-game options can still raise quality afterward.
                if(PlayerPrefs.GetInt("Options.BrowserPerformanceVersion",0)<1)
                {
                    PlayerPrefs.SetInt("Options.BetterLight",0);
                    PlayerPrefs.SetInt("Options.DepthOfField",0);
                    PlayerPrefs.SetInt("Options.TextureQuality",(int)TextureQualityLevel.Low);
                    PlayerPrefs.SetInt("Options.BrowserPerformanceVersion",1);
                    PlayerPrefs.Save();
                }
            }
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
            if (Object.FindFirstObjectByType<ProceduralMap>() != null||
                Object.FindFirstObjectByType<WorldLoading>() != null||
                Object.FindFirstObjectByType<PauseMenu>() != null)
                return;

            if(Application.isPlaying)
            {
                if(WebBrowserSession.SelectedMap>=0&&!EnterAfterBuild) {
                    TestGround=WebBrowserSession.SelectedMap==1;EnterAfterBuild=true;
                }
                if (!EnterAfterBuild)
                {
                    BuildStartMenu();
                    return;
                }
                // Pause simulation until all supports, colliders and menus exist.
                Time.timeScale=0f;
                Cursor.lockState=CursorLockMode.None;
                Cursor.visible=true;
                var loading=new GameObject("World loading").AddComponent<WorldLoading>();
                loading.Begin(BuildWorld(loading));
                return;
            }
            // Editor geometry/physics validations intentionally build immediately.
            IEnumerator immediate=BuildWorld(null);
            while(immediate.MoveNext()) { }
        }

        private static void BuildStartMenu()
        {
            DisableTemplateObjects();
            ConfigureEnvironment();
            var menuRoot = new GameObject("Wyvernland Start Menu");
            CreateLightingVolume(menuRoot);
            FirstPersonController controller = CreatePlayer(menuRoot.transform, null);
            controller.SetPaused(true);
            controller.enabled = false;
            controller.GetComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.Discrete;
            controller.GetComponent<Rigidbody>().isKinematic = true;
            controller.PlayerCamera.clearFlags = CameraClearFlags.SolidColor;
            controller.PlayerCamera.backgroundColor = new Color(.045f, .055f, .065f);
            menuRoot.AddComponent<PauseMenu>().Initialize(controller, true, true);
            Debug.Log("WYVERNLAND_START_MENU_READY: no map generated");
        }

        private static IEnumerator BuildWorld(WorldLoading loading)
        {

            DisableTemplateObjects();
            ConfigureEnvironment();

            GameObject world = new("Wyvernland Prototype");
            world.AddComponent<PhysicalDestructionWorld>();
            world.AddComponent<CachedWorldShading>().Initialize();
            ProceduralMap map = world.AddComponent<ProceduralMap>();
            loading?.SetStage("Building terrain",.05f);
            yield return null;
            Debug.Log("WYVERNLAND_STARTUP map begin");
            IEnumerator terrainCreation=map.BuildIncrementally();
            while(terrainCreation.MoveNext()) yield return terrainCreation.Current;
            terrainCreation=null;
            Debug.Log("WYVERNLAND_STARTUP map complete");
            // Tiles own their geometry; release the large cached source meshes
            // before populating trees, rocks and pit cutters.
            yield return Resources.UnloadUnusedAssets();

            Light sun = CreateSun(world.transform);
            CreateSky(world, sun);
            CreateLightingVolume(world);
            FirstPersonController player = CreatePlayer(world.transform, map);
            int cameraLayers=player.PlayerCamera.cullingMask;
            if(loading!=null)
            {
                player.SetPaused(true);
                // The loading overlay covers the scene; avoid drawing thousands
                // of individual props before their draw cache is ready.
                player.PlayerCamera.cullingMask=0;
            }
            Debug.Log("WYVERNLAND_STARTUP player complete");
            ProceduralTreeField trees=world.AddComponent<ProceduralTreeField>();
            loading?.SetStage("Building monuments",.16f);
            yield return null;
            // Monument interiors must be known before trees, rocks and grass are placed.
            if (!TestGround) world.AddComponent<ProceduralMonumentField>().Initialize(player,map);
            Debug.Log("WYVERNLAND_STARTUP monuments complete");
            loading?.SetStage("Growing trees",.22f);
            yield return null;
            IEnumerator treeCreation=trees.InitializeIncrementally(player,map,(done,total)=>
                loading?.SetPopulationProgress("Growing trees",.22f,.48f,done,total));
            while(treeCreation.MoveNext()) yield return treeCreation.Current;
            Debug.Log("WYVERNLAND_STARTUP trees complete");
            loading?.SetStage("Placing rocks and stones",.48f);
            yield return null;
            var rocks=world.AddComponent<ProceduralRockField>();
            IEnumerator rockCreation=rocks.InitializeIncrementally(player,map,(done,total)=>
                loading?.SetPopulationProgress("Placing rocks and stones",.48f,.62f,done,total));
            while(rockCreation.MoveNext()) yield return rockCreation.Current;
            Debug.Log("WYVERNLAND_STARTUP rocks complete");
            loading?.SetStage("Preparing terrain pits",.62f);
            yield return null;
            IEnumerator pitCreation=rocks.PreloadTerrainPitsIncrementally((done,total)=>
                loading?.SetPopulationProgress("Preparing terrain pits",.62f,.66f,done,total));
            while(pitCreation.MoveNext()) yield return pitCreation.Current;
            pitCreation=null;
            Debug.Log("WYVERNLAND_STARTUP terrain pits complete");
            loading?.SetStage("Growing grass",.72f);
            yield return null;
            IEnumerator grassCreation=world.AddComponent<ProceduralGrass>().InitializeIncrementally(player,map);
            while(grassCreation.MoveNext()) yield return grassCreation.Current;
            Debug.Log("WYVERNLAND_STARTUP grass complete");
            if (TestGround)
            {
                CreateGoldenTestStone(world, map);
                var ironBar=CreateIronTestBar(world, map, player);
                world.AddComponent<TestGroundBars>().Initialize(map,player,ironBar);
                TrainingDummy.Create(world.transform,map);
            }
            loading?.SetStage("Registering world physics",.80f);
            yield return null;
            IEnumerator registration=world.GetComponent<PhysicalDestructionWorld>().RegisterSceneObjectsIncrementally();
            while(registration.MoveNext()) yield return registration.Current;
#if UNITY_WEBGL && !UNITY_EDITOR
            var sharedRoot=new GameObject("Wyvernland Shared World");sharedRoot.transform.SetParent(world.transform,false);
            var shared=sharedRoot.AddComponent<WebWorldSession>();
            var identities=shared.Initialize(world,player);
            while(identities.MoveNext()) yield return identities.Current;
#endif
            loading?.SetStage("Preparing world rendering",.86f);
            yield return null;
            IEnumerator drawCache=world.AddComponent<ResidentPropRendering>().InitializeIncrementally();
            while(drawCache.MoveNext()) yield return drawCache.Current;
            loading?.SetStage("Preparing controls",.97f);
            yield return null;
            world.AddComponent<PrototypeHud>().Initialize(player);
            var menu=world.AddComponent<PauseMenu>();
            menu.Initialize(player,loading!=null);
            loading?.SetStage("Loading nearby objects",.98f);
            var residency=world.AddComponent<PlayerWorldLoading>();
            IEnumerator nearby=residency.Initialize(world,player.transform);
            while(nearby.MoveNext()) yield return nearby.Current;
            if(loading!=null)
            {
                IEnumerator quality=ColorOnlyTextureQuality.RefreshColorOnlyObjectsIncrementally();
                while(quality.MoveNext()) yield return quality.Current;
                menu.FinishInitialize();
            }
            world.AddComponent<HeldObjectUpdates>();
            player.PlayerCamera.cullingMask=cameraLayers;
#if UNITY_WEBGL && !UNITY_EDITOR
            var online = new GameObject("Wyvernland Online");
            online.transform.SetParent(world.transform, false);
            online.AddComponent<WebPlayerPresence>().Initialize(player);
            shared.BeginRoom();
#endif
            EnterAfterBuild = false;
            loading?.SetStage("Ready",1f);
            Debug.Log("WYVERNLAND_STARTUP complete");
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
            spawn.y = map != null ? map.HeightAt(spawn.x, spawn.z) + 0.15f : 0f;

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
            if(Application.platform==RuntimePlatform.WebGLPlayer)
            {
                // Keep the full world resident while bounding browser draw work.
                camera.farClipPlane=1500f;
                camera.allowHDR=false;
                cameraData.renderPostProcessing=false;
                cameraData.antialiasing=AntialiasingMode.None;
            }
            cameraObject.AddComponent<AudioListener>();

            FirstPersonController controller = playerObject.AddComponent<FirstPersonController>();
            controller.Initialize(cameraObject.transform);
            if (map == null) return controller;
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

        internal static GameObject CreateIronTestBar(GameObject world,ProceduralMap map,
            FirstPersonController player,bool stock=true)
        {
            const float diameter=0.05f;
            const float length=1.4f;
            const int sides=8;
            Mesh mesh=new() { name="Eight-sided iron bar" };
            var vertices=new Vector3[sides*2];
            var triangles=new int[sides*6+(sides-2)*6];
            int triangleIndex=0;
            for(int i=0;i<sides;i++)
            {
                float angle=i*Mathf.PI*2f/sides;
                float x=Mathf.Cos(angle)*diameter*0.5f;
                float z=Mathf.Sin(angle)*diameter*0.5f;
                vertices[i]=new Vector3(x,-length*0.5f,z);
                vertices[i+sides]=new Vector3(x,length*0.5f,z);
                int next=(i+1)%sides;
                triangles[triangleIndex++]=i;
                triangles[triangleIndex++]=i+sides;
                triangles[triangleIndex++]=next+sides;
                triangles[triangleIndex++]=i;
                triangles[triangleIndex++]=next+sides;
                triangles[triangleIndex++]=next;
            }
            for(int i=1;i<sides-1;i++)
            {
                triangles[triangleIndex++]=0;
                triangles[triangleIndex++]=i;
                triangles[triangleIndex++]=i+1;
                triangles[triangleIndex++]=sides;
                triangles[triangleIndex++]=sides+i+1;
                triangles[triangleIndex++]=sides+i;
            }
            mesh.vertices=vertices;
            mesh.triangles=triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            world.AddComponent<RuntimeMeshOwner>().Initialize(mesh);

            GameObject bar=new("Iron Bar");
            bar.transform.SetParent(world.transform,false);
            Vector3 forward=Vector3.ProjectOnPlane(player.transform.forward,Vector3.up).normalized;
            Vector3 center=player.transform.position+forward*2.5f;
            center.y=(map!=null?map.HeightAt(center.x,center.z):0)+diameter*0.5f+0.005f;
            bar.transform.SetPositionAndRotation(center,
                Quaternion.FromToRotation(Vector3.up,player.transform.right));
            bar.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=bar.AddComponent<MeshRenderer>();
            var material=new Material(Resources.Load<Shader>("Clay")) { name="Iron Bar" };
            material.SetColor("_BaseColor",new Color(0.32f,0.34f,0.36f));
            material.SetFloat("_TerrainColorRegions",0f);
            renderer.sharedMaterial=material;
            world.AddComponent<RuntimeMaterialOwner>().Initialize(material);
            var collider=bar.AddComponent<MeshCollider>();
            collider.sharedMesh=mesh;
            collider.convex=true;
            var body=bar.AddComponent<Rigidbody>();
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.interpolation=RigidbodyInterpolation.Interpolate;
            var properties=PhysicalDestructible.Ensure(bar);
            properties.ApplyMaterial("Iron");
            bar.AddComponent<PickableObject>().DisplayName="Iron Bar";
            if(stock) bar.AddComponent<TestGroundIronBar>();return bar;
        }
    }

    public sealed class RuntimeMeshOwner : MonoBehaviour
    {
        private Mesh mesh;

        public void Initialize(Mesh value) => mesh=value;

        private void OnDestroy()
        {
            if(mesh!=null)
            {
                if(Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }
    }

    public sealed class RuntimeMaterialOwner : MonoBehaviour
    {
        static readonly System.Collections.Generic.Dictionary<Material,int> references=new();
        private Material material;

        public void Initialize(Material value)
        {
            if(material==value) return;
            ReleaseOwned();material=value;
            if(material!=null) references[material]=references.TryGetValue(material,out int count)?count+1:1;
        }

        // Descendants retain shared fragment materials before their source is
        // removed. Externally owned corpse/network materials must be copied.
        internal static Material RetainFragmentMaterial(GameObject owner,Material source)
        {
            Material value;
            bool valid=source!=null&&source.shader!=null&&source.shader.name!="Hidden/InternalErrorShader";
            if(valid&&references.ContainsKey(source)) value=source;
            else if(valid) value=new Material(source);
            else
            {
                value=new Material(Resources.Load<Shader>("Clay"));
                value.SetColor("_BaseColor",RiggedPlayerAvatar.PlayerColor);
            }
            owner.AddComponent<RuntimeMaterialOwner>().Initialize(value);return value;
        }

        void ReleaseOwned()
        {
            if(material==null) return;
            if(references.TryGetValue(material,out int count)&&count>1) references[material]=count-1;
            else
            {
                references.Remove(material);ColorOnlyTextureQuality.ForgetMaterial(material);
                if(Application.isPlaying) Destroy(material);else DestroyImmediate(material);
            }
            material=null;
        }
        private void OnDestroy() => ReleaseOwned();
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
