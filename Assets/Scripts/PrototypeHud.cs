using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Wyvernland
{
    // Preserve the existing saved Low=0 and High=1 values.
    public enum TextureQualityLevel { Low=0, High=1, Medium=2 }
    /// <summary>
    /// Low texture quality is a color-only mode. Texture assignments are kept
    /// in memory so High quality can restore them without recreating objects.
    /// </summary>
    public static class ColorOnlyTextureQuality
    {
        private const float HighTextureScale=0.7f;
        private const float MediumTextureScale=0.5f;
        private sealed class RendererState
        {
            public Material[] materials;
            public MaterialPropertyBlock properties;
            public Color color;
        }

        private static readonly System.Collections.Generic.Dictionary<Material,
            System.Collections.Generic.Dictionary<string,Texture>> hiddenTextures=new();
        private static readonly System.Collections.Generic.Dictionary<Renderer,RendererState>
            rendererStates=new();
        private static Material flatColorMaterial;
        private static Material twoColorMaterial;
        private static readonly System.Collections.Generic.Dictionary<Texture,(float bias,FilterMode filter)>
            textureSampling=new();

        public static TextureQualityLevel Level { get; private set; } = TextureQualityLevel.High;
        public static bool IsHighQuality => Level==TextureQualityLevel.High;

        internal static void ForgetRenderer(Renderer renderer) => rendererStates.Remove(renderer);
        internal static void ForgetMaterial(Material material) => hiddenTextures.Remove(material);
        internal static int TrackedRendererCount => rendererStates.Count;

        public static void GetOriginalAppearance(Renderer renderer,out Material[] materials,
            out MaterialPropertyBlock properties)
        {
            if(rendererStates.TryGetValue(renderer,out var state))
            {
                materials=state.materials;
                properties=state.properties;
                return;
            }
            materials=renderer.sharedMaterials;
            properties=new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
        }

        public static void SetHighQuality(bool highQuality) =>
            SetQuality(highQuality?TextureQualityLevel.High:TextureQualityLevel.Low);

        public static void SetQuality(TextureQualityLevel level,bool refreshRenderers=true)
        {
            foreach(var entry in textureSampling)
                if(entry.Key!=null)
                {
                    entry.Key.mipMapBias=entry.Value.bias;
                    entry.Key.filterMode=entry.Value.filter;
                }
            textureSampling.Clear();
            Level=level;
            // One skipped mip gives Medium exactly 50% linear texture resolution.
            QualitySettings.globalTextureMipmapLimit=
                level==TextureQualityLevel.Medium?Mathf.RoundToInt(Mathf.Log(1f/MediumTextureScale,2f)):
                level==TextureQualityLevel.Low?1:0;
            if(level==TextureQualityLevel.High)
            {
                foreach(var rendererEntry in rendererStates)
                {
                    if(rendererEntry.Key==null) continue;
                    rendererEntry.Key.sharedMaterials=rendererEntry.Value.materials;
                    rendererEntry.Key.SetPropertyBlock(rendererEntry.Value.properties);
                }
                rendererStates.Clear();
                if(flatColorMaterial!=null)
                {
                    if(Application.isPlaying) Object.Destroy(flatColorMaterial);
                    else Object.DestroyImmediate(flatColorMaterial);
                    flatColorMaterial=null;
                }
                if(twoColorMaterial!=null)
                {
                    if(Application.isPlaying) Object.Destroy(twoColorMaterial);
                    else Object.DestroyImmediate(twoColorMaterial);
                    twoColorMaterial=null;
                }
                foreach(var materialEntry in hiddenTextures)
                {
                    if(materialEntry.Key==null) continue;
                    foreach(var textureEntry in materialEntry.Value)
                        materialEntry.Key.SetTexture(textureEntry.Key,textureEntry.Value);
                }
                hiddenTextures.Clear();
                if(refreshRenderers) RefreshColorOnlyObjects();
                return;
            }

            if(refreshRenderers) RefreshColorOnlyObjects();
        }

        public static void RefreshColorOnlyObjects()
        {
            var routine=RefreshColorOnlyObjectsIncrementally();
            while(routine.MoveNext()) { }
        }

        internal static System.Collections.IEnumerator RefreshColorOnlyObjectsIncrementally()
        {
            double batchStarted=Time.realtimeSinceStartupAsDouble;
            foreach(Renderer renderer in Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                ApplyRendererQuality(renderer);
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
            }
            Apply(RenderSettings.skybox);
        }

        // Apply immediately when destruction creates a renderer, before it can
        // be drawn with its restored original materials for even one frame.
        public static void ApplyRendererQuality(Renderer renderer)
        {
            if(renderer==null) return;
            var resident=renderer.GetComponent<ResidentRenderVisual>();
            if(resident!=null) resident.ApplyQuality(Level);
            if(Level==TextureQualityLevel.High)
            {
                ApplyRendererShadows(renderer);
                foreach(Material material in renderer.sharedMaterials) Apply(material);
                return;
            }
            if(renderer.GetComponent<ResidentRenderVisual>()!=null)
            {
                ApplyRendererShadows(renderer);
                foreach(Material material in renderer.sharedMaterials) Apply(material);
                return;
            }
            if(Level==TextureQualityLevel.Low&&flatColorMaterial==null)
            {
                Shader shader=Resources.Load<Shader>("FlatColor");
                if(shader==null) throw new System.InvalidOperationException(
                    "Missing FlatColor shader for Low texture quality.");
                flatColorMaterial=new Material(shader){name="Low quality flat color"};
            }
            if(Level==TextureQualityLevel.Medium&&twoColorMaterial==null)
            {
                Shader shader=Resources.Load<Shader>("TwoColor");
                if(shader==null) throw new System.InvalidOperationException(
                    "Missing TwoColor shader for Medium texture quality.");
                twoColorMaterial=new Material(shader){name="Medium quality two-color shadowed"};
            }
            ApplyRendererShadows(renderer);
            bool tracked=rendererStates.TryGetValue(renderer,out RendererState state);
            Material[] originals=tracked?state.materials:renderer.sharedMaterials;
            if(UsesProceduralColorShader(originals))
            {
                // Terrain biome colors and grass silhouettes are procedural, so
                // retain their shaders and select their quality paths in Apply.
                if(tracked)
                {
                    renderer.sharedMaterials=state.materials;
                    renderer.SetPropertyBlock(state.properties);
                    rendererStates.Remove(renderer);
                }
                foreach(Material material in originals) Apply(material);
                return;
            }
            if(!tracked)
            {
                if(originals.Length==0) return;
                var properties=new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                state=new RendererState
                {
                    materials=originals,
                    properties=properties,
                    color=RepresentativeColor(renderer,originals)
                };
                rendererStates.Add(renderer,state);
                var lifetime=renderer.GetComponent<TextureQualityRendererLifetime>()??renderer.gameObject.AddComponent<TextureQualityRendererLifetime>();
                lifetime.Track(renderer);
            }
            Material replacement=Level==TextureQualityLevel.Medium?
                twoColorMaterial:flatColorMaterial;
            var replacementMaterials=new Material[state.materials.Length];
            for(int i=0;i<replacementMaterials.Length;i++)
                replacementMaterials[i]=replacement;
            renderer.sharedMaterials=replacementMaterials;
            var replacementProperties=new MaterialPropertyBlock();
            replacementProperties.SetColor("_BaseColor",state.color);
            if(Level==TextureQualityLevel.Medium)
                replacementProperties.SetColor("_ShadowColor",
                    Color.Lerp(state.color,Color.black,0.45f));
            renderer.SetPropertyBlock(replacementProperties);
        }

        private static void ApplyRendererShadows(Renderer renderer)
        {
            if(renderer is not MeshRenderer&&renderer is not SkinnedMeshRenderer) return;
            bool isGrass=false;
            foreach(Material material in renderer.sharedMaterials)
                if(material!=null&&material.shader.name.Contains("Grass"))
                {
                    isGrass=true;
                    break;
                }
            if(isGrass)
            {
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.receiveShadows=false;
                return;
            }
            if(renderer.shadowCastingMode!=ShadowCastingMode.ShadowsOnly)
                renderer.shadowCastingMode=ShadowCastingMode.On;
            renderer.receiveShadows=true;
        }

        private static bool UsesProceduralColorShader(Material[] materials)
        {
            foreach(Material material in materials)
                if(material!=null&&
                    ((material.HasProperty("_TerrainColorRegions")&&
                      material.GetFloat("_TerrainColorRegions")>0.5f)||
                     material.shader.name.Contains("Grass"))) return true;
            return false;
        }

        internal static Color RepresentativeColor(Renderer renderer,Material[] materials)
        {
            Material source=null;
            foreach(Material material in materials)
                if(material!=null) { source=material; break; }
            Color color=Color.white;
            if(source!=null)
            {
                if(source.HasProperty("_BaseColor")) color=source.GetColor("_BaseColor");
                else if(source.HasProperty("_Color")) color=source.GetColor("_Color");
            }

            bool usesVertexColor=source!=null&&
                ((source.HasProperty("_UseVertexColor")&&source.GetFloat("_UseVertexColor")>0.5f)||
                 source.shader.name.Contains("Grass"));
            MeshFilter meshFilter=renderer.GetComponent<MeshFilter>();
            // Unity components use a native-object null state that the C# ?.
            // operator does not reliably detect. Particle renderers such as the
            // slide smoke have no MeshFilter, so use Unity's explicit null test.
            Mesh mesh=renderer is SkinnedMeshRenderer skinned?skinned.sharedMesh:
                meshFilter!=null?meshFilter.sharedMesh:null;
            if(usesVertexColor&&mesh!=null)
            {
                Color[] colors=mesh.colors;
                if(colors.Length>0)
                {
                    color=Color.clear;
                    foreach(Color vertexColor in colors) color+=vertexColor;
                    color/=colors.Length;
                    if(QualitySettings.activeColorSpace==ColorSpace.Linear) color=color.gamma;
                }
            }
            color.a=1f;
            return color;
        }

        // Call this after assigning textures to a material that may be created
        // while Low quality is already active.
        public static void Apply(Material material)
        {
            if(material==null) return;
            if(material.HasProperty("_LowTextureQuality"))
                material.SetFloat("_LowTextureQuality",
                    Level==TextureQualityLevel.Low?1f:0f);
            if(material.HasProperty("_MediumTextureQuality"))
                material.SetFloat("_MediumTextureQuality",
                    Level==TextureQualityLevel.Medium?1f:0f);
            if(Level!=TextureQualityLevel.Low)
            {
                foreach(string property in material.GetTexturePropertyNames())
                {
                    Texture texture=material.GetTexture(property);
                    if(texture is not Texture2D texture2D||texture2D.mipmapCount<=1||
                        textureSampling.ContainsKey(texture)) continue;
                    textureSampling.Add(texture,(texture.mipMapBias,texture.filterMode));
                    // Fractional mip selection gives High 70% effective linear
                    // detail without resampling or duplicating texture assets.
                    // Medium uses the global half-resolution mip limit.
                    if(Level==TextureQualityLevel.High)
                        texture.mipMapBias+=Mathf.Log(1f/HighTextureScale,2f);
                    texture.filterMode=FilterMode.Trilinear;
                }
                return;
            }
            if(!hiddenTextures.TryGetValue(material,out var textures))
                hiddenTextures[material]=textures=new System.Collections.Generic.Dictionary<string,Texture>();
            foreach(string property in material.GetTexturePropertyNames())
            {
                Texture texture=material.GetTexture(property);
                if(texture==null) continue;
                if(!textures.ContainsKey(property)) textures[property]=texture;
                material.SetTexture(property,null);
            }
        }
    }

    // Versioned and grouped so future health, inventory and quest data can be
    // added without changing the menu/controller save flow.
    [System.Serializable]
    public sealed class PlayerSaveData
    {
        public int version = 1;
        public PlayerPoseData pose = new();
        public PlayerProgressData progress = new();
        public string savedAtUtc;
    }

    [System.Serializable]
    public sealed class PlayerPoseData
    {
        public Vector3 position;
        public float yaw;
        public float pitch;
        public float lookYaw;
    }

    [System.Serializable]
    public sealed class PlayerProgressData
    {
        // Future status such as health, inventory and quests belongs here.
    }

    public static class PlayerSaveSystem
    {
        private const string SaveKey = "PlayerSave.Data";

        public static void Save(FirstPersonController player)
        {
            if (player == null || WorldBootstrap.TestGround) return;
            PlayerSaveData data = player.CaptureSaveData();
            data.savedAtUtc = System.DateTime.UtcNow.ToString("O");
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public static bool TryLoad(FirstPersonController player)
        {
            if (player == null || WorldBootstrap.TestGround || !PlayerPrefs.HasKey(SaveKey)) return false;
            try
            {
                PlayerSaveData data = JsonUtility.FromJson<PlayerSaveData>(PlayerPrefs.GetString(SaveKey));
                if (data == null || data.version != 1 || data.pose == null) return false;
                return player.RestoreSaveData(data);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Could not load the player save: {exception.Message}");
                return false;
            }
        }
    }

    public static class GameplayConsole
    {
        public static bool IsOpen { get; private set; }
        public static event System.Action VisibilityChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            IsOpen=false;
            VisibilityChanged=null;
        }

        public static void SetOpen(bool open)
        {
            if(IsOpen==open) return;
            IsOpen=open;
            VisibilityChanged?.Invoke();
        }
    }

    public sealed class PrototypeHud : MonoBehaviour
    {
        private FirstPersonController player;
        private PlayerPickupController pickup;
        private GUIStyle labelStyle;
        private GUIStyle titleStyle;
        private GUIStyle fpsStyle;
        private GUIStyle destructionStyle;
        private GUIStyle treeReasonStyle;
        private string destructionName;
        private float destructionUntil;
        private float smoothedFrameTime=1f/60f;
        private readonly RaycastHit[] aimHits=new RaycastHit[64];
        private PhysicalDestructible aimedProperties;
        private RopeObject aimedRope;
        private string aimedName;

        private void OnEnable() => PhysicalDestructible.DestructionOccurred+=ShowDestruction;
        private void OnDisable() => PhysicalDestructible.DestructionOccurred-=ShowDestruction;

        private void ShowDestruction(PhysicalDestructEffect effect)
        {
            destructionName=effect.ToString();
            destructionUntil=Time.unscaledTime+2f;
        }

        public void Initialize(FirstPersonController controller)
        {
            GameplayConsole.SetOpen(false);
            player = controller;
            pickup=controller.GetComponent<PlayerPickupController>();
        }

        private void Update()
        {
            if(player!=null&&!player.IsPaused&&Application.isFocused&&Keyboard.current!=null&&
                Keyboard.current[player.Bindings[FirstPersonController.ConsoleBindingIndex]].wasPressedThisFrame)
                GameplayConsole.SetOpen(!GameplayConsole.IsOpen);
            if(!GameplayConsole.IsOpen)
            {
                aimedProperties=null;
                aimedRope=null;
                aimedName=null;
                return;
            }
            if(Time.unscaledDeltaTime>0f)
                smoothedFrameTime=Mathf.Lerp(smoothedFrameTime,Time.unscaledDeltaTime,0.08f);
            UpdateAimedProperties();
        }

        private void UpdateAimedProperties()
        {
            aimedProperties=null;
            aimedRope=null;
            aimedName=null;
            if(player==null||player.IsPaused||player.PlayerCamera==null) return;

            Transform view=player.PlayerCamera.transform;
            int count=Physics.RaycastNonAlloc(new Ray(view.position,view.forward),aimHits,100f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            float nearest=float.PositiveInfinity;
            Collider target=null;
            for(int i=0;i<count;i++)
            {
                Collider collider=aimHits[i].collider;
                if(collider==null||aimHits[i].distance>=nearest||
                    collider.GetComponentInParent<FirstPersonController>()!=null) continue;
                PickableObject pickable=collider.GetComponentInParent<PickableObject>();
                if(pickable!=null&&pickable.IsHeld) continue;
                target=collider;
                nearest=aimHits[i].distance;
            }
            if(target==null) return;
            aimedRope=target.GetComponentInParent<RopeObject>();
            if(aimedRope!=null) { aimedName=aimedRope.MaterialName;return; }
            aimedProperties=PhysicalDestructible.Resolve(target);
            if(aimedProperties==null) return;
            PickableObject item=target.GetComponentInParent<PickableObject>();
            aimedName=item!=null?item.DisplayName:
                aimedProperties.IsTerrain?"Terrain":aimedProperties.gameObject.name;
        }

        private void OnGUI()
        {
            if (player == null || player.IsPaused || !GameplayConsole.IsOpen) return;
            EnsureStyles();

            bool showAim=aimedProperties!=null||aimedRope!=null;
            bool showRight=pickup!=null&&pickup.HeldObject!=null;
            bool showLeft=pickup!=null&&pickup.HeldLeftObject!=null&&
                pickup.HeldLeftObject!=pickup.HeldObject;
            TreeStructuralPart aimedStructure=aimedProperties!=null?
                aimedProperties.GetComponent<TreeStructuralPart>():null;
            GeneratedTree aimedTree=aimedStructure!=null?aimedStructure.Tree:null;
            int extraRows=(showAim?3:0)+(showRight?5:0)+(showLeft?5:0)+(aimedTree!=null?3:0);
            GUI.Box(new Rect(18f,18f,400f,134f+extraRows*22f),GUIContent.none);
            GUI.Label(new Rect(32f,28f,220f,24f),"CONSOLE",titleStyle);
            GUI.Label(new Rect(340f,28f,65f,24f),$"{1f/Mathf.Max(0.0001f,smoothedFrameTime):0} FPS",fpsStyle);
            GUI.Label(new Rect(32f, 54f, 290f, 22f), "Move, sprint and jump — controls in Options", labelStyle);
            Key consoleKey=player.Bindings[FirstPersonController.ConsoleBindingIndex];
            string consoleShortcut=consoleKey==Key.Backquote?"`":consoleKey.ToString();
            GUI.Label(new Rect(32f,76f,370f,22f),
                $"Mouse  Look    Esc  Pause menu    {consoleShortcut}  Console",labelStyle);

            if (player != null)
            {
                float altitude = Mathf.Max(0f, player.transform.position.y);
                string state = player.IsSprinting ? "SPRINTING" : "WALKING";
                GUI.Label(new Rect(32f, 98f, 290f, 22f), $"{state}  {player.Speed:0.0} m/s    Altitude {altitude:0} m", labelStyle);
                Vector3 position=player.transform.position;
                GUI.Label(new Rect(32f,120f,290f,22f),
                    $"Coordinates  X {position.x:0.0}   Y {position.y:0.0}   Z {position.z:0.0}",labelStyle);
            }
            float detailY=142f;
            if(showRight||showLeft)
            {
                PickableObject held=showRight?pickup.HeldObject:pickup.HeldLeftObject;
                Vector3 size=held.Size;
                GUI.Label(new Rect(32f,detailY,370f,22f),
                    $"Holding {(pickup.HeldObject==pickup.HeldLeftObject?"both hands":showRight?"right":"left")}: {held.DisplayName}   Size {size.x:0.00} × {size.y:0.00} × {size.z:0.00} m",
                    labelStyle);
                detailY+=22f;
                DrawPhysicalProperties("Held",held,detailY);
                detailY+=22f;
                DrawMass(held.GetComponent<PhysicalDestructible>(),detailY,held.Rope);
                detailY+=22f;
                DrawHeldSpeed(held,detailY);
                detailY+=44f;
            }
            if(showRight&&showLeft)
            {
                PickableObject held=pickup.HeldLeftObject;
                Vector3 size=held.Size;
                GUI.Label(new Rect(32f,detailY,370f,22f),
                    $"Holding left: {held.DisplayName}   Size {size.x:0.00} × {size.y:0.00} × {size.z:0.00} m",
                    labelStyle);
                detailY+=22f;
                DrawPhysicalProperties("Left",held,detailY);
                detailY+=22f;
                DrawMass(held.GetComponent<PhysicalDestructible>(),detailY,held.Rope);
                detailY+=22f;
                DrawHeldSpeed(held,detailY);
                detailY+=44f;
            }
            if(showAim)
            {
                bool canPickUp=pickup!=null&&pickup.AimedObject!=null&&
                    pickup.AimedObject.gameObject==(aimedRope!=null?aimedRope.gameObject:aimedProperties.gameObject);
                bool twoHands=canPickUp&&pickup.AimedObject.RequiresTwoHands;
                bool tooHeavy=twoHands&&!pickup.AimedObject.CanPickUpWithTwoHands;
                bool handsBusy=canPickUp&&(twoHands&&!tooHeavy?
                    pickup.HasRightHandItem||pickup.HasLeftHandItem:
                    pickup.HasRightHandItem&&pickup.HasLeftHandItem);
                float dragSpeed=tooHeavy?PickableObject.DragSpeed(pickup.AimedObject.PickupMassKg,
                    (pickup.HasRightHandItem?0:1)+(pickup.HasLeftHandItem?0:1)):0f;
                GUI.Label(new Rect(32f,detailY,370f,22f),
                    tooHeavy?(handsBusy?$"Free a hand to drag {aimedName}":
                        $"Drag: {aimedName}  —  up to {dragSpeed:0.##} m/s"):
                    handsBusy?$"Free {(twoHands?"both hands":"a hand")} to pick up {aimedName}":
                    canPickUp?$"Pick up: {aimedName}  —  Interact + Left Mouse / {player.AimBindingName}":
                    $"Aiming: {aimedName}",labelStyle);
                detailY+=22f;
                if(aimedRope!=null) DrawRopeShape("Aimed",aimedRope,detailY);
                else GUI.Label(new Rect(32f,detailY,370f,22f),
                    $"Aimed  Hardness {aimedProperties.Hardness:0.##}   Resilience {aimedProperties.Resilience:0.##}",labelStyle);
                detailY+=22f;
                DrawMass(aimedProperties,detailY,aimedRope);
                detailY+=22f;
                if(aimedTree!=null)
                {
                    GUI.Label(new Rect(32f,detailY,370f,22f),
                        aimedTree.IsBroken?"Trunk break: already split":
                        $"Trunk break: {aimedTree.TrunkBreakProgress}/{aimedTree.ImpactsToBreak} damaging hits within {aimedTree.TrunkBreakRadius:0.0} m",
                        labelStyle);
                    GUI.Label(new Rect(32f,detailY+22f,370f,44f),
                        $"Last destruction: {aimedTree.LastBreakReason}",treeReasonStyle);
                }
            }

            if(!string.IsNullOrEmpty(destructionName)&&Time.unscaledTime<destructionUntil)
            {
                var banner=new Rect((Screen.width-260f)*0.5f,18f,260f,42f);
                GUI.Box(banner,GUIContent.none);
                GUI.Label(banner,destructionName,destructionStyle);
            }

        }

        private void DrawHeldSpeed(PickableObject held,float y)
        {
            var avatar=player.GetComponent<RiggedPlayerAvatar>();
            float bonus=avatar!=null&&avatar.IsStrikingHeldItem(held)?
                (avatar.StrikeWavingSpeedMultiplier-1f)*100f:0f;
            GUI.Label(new Rect(32f,y,370f,22f),
                $"Object speed {held.HeldSpeed:0.00} m/s   Wave boost +{bonus:0}%",labelStyle);
            GUI.Label(new Rect(32f,y+22f,370f,22f),
                $"Rebound: > {RiggedPlayerAvatar.MinimumReboundSpeed:0.0} m/s against harder objects",labelStyle);
        }

        private void DrawPhysicalProperties(string label,PickableObject item,float y)
        {
            if(item.Rope!=null) { DrawRopeShape(label,item.Rope,y);return; }
            PhysicalDestructible properties=item.GetComponent<PhysicalDestructible>() ??
                item.GetComponentInParent<PhysicalDestructible>() ??
                PhysicalDestructible.Ensure(item.gameObject);
            if(properties==null) return;
            GUI.Label(new Rect(32f,y,370f,22f),
                $"{label}  Hardness {properties.Hardness:0.##}   Resilience {properties.Resilience:0.##}",
                labelStyle);
        }

        private void DrawRopeShape(string label,RopeObject rope,float y) =>
            GUI.Label(new Rect(32f,y,370f,22f),
                $"{label}  Rope   Radius {rope.RadiusMetres*100f:0.0} cm   Length {rope.LengthMetres:0.00} m",labelStyle);

        private void DrawMass(PhysicalDestructible properties,float y,RopeObject rope=null)
        {
            if(rope!=null)
            {
                GUI.Label(new Rect(32f,y,370f,22f),
                    $"Mass {rope.MassKg:#,0.###} kg   {(rope.IsAttached?"Attached to branch":"Free rope")}",labelStyle);
                return;
            }
            if(properties==null) return;
            string text=properties.HasMaterialMass?
                $"Mass {properties.MassKg:#,0.###} kg   Solid volume {properties.SolidVolumeCubicMetres:0.####} m³":
                "Mass unavailable";
            GUI.Label(new Rect(32f,y,370f,22f),text,labelStyle);
        }

        private void EnsureStyles()
        {
            if (labelStyle != null)
                return;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };
            titleStyle = new GUIStyle(labelStyle)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            fpsStyle=new GUIStyle(labelStyle)
            {
                alignment=TextAnchor.UpperRight,
                fontStyle=FontStyle.Bold
            };
            treeReasonStyle=new GUIStyle(labelStyle) { wordWrap=true };
            destructionStyle=new GUIStyle(labelStyle)
            {
                alignment=TextAnchor.MiddleCenter,
                fontSize=20,
                fontStyle=FontStyle.Bold
            };
        }
    }

    // Runs before movement so pressing Escape cannot also move or rotate the player.
    [DefaultExecutionOrder(-100)]
    public sealed class PauseMenu : MonoBehaviour
    {
        private const float OriginalDepthOfFieldRadius = 0.325f;
        private const float HighTextureDepthOfFieldScale = 0.7f;
        private const float LowTextureDepthOfFieldScale = 0.4f;
        private enum Page { Closed, Main, Pause, Options, Graphic, Keybinding, Audio }
        private Page page;
        private Page optionsRoot = Page.Pause;
        private FirstPersonController player;
        private UniversalAdditionalCameraData cameraData;
        private int binding = -1;
        private float previousTimeScale;
        private bool previousAudioPause;
        private float originalVolume;
        private int originalVSync;
        private float volume;
        private bool muted;
        private bool initialized;
        private bool sessionStarted;
        private bool startMenuOnly;
#if UNITY_WEBGL && !UNITY_EDITOR
        private float browserResumeTime;
#endif
        private bool betterLight;
        private bool depthOfField;
        private bool fog;
        private TextureQualityLevel textureQuality;
        private float loadingRange;
        private ProceduralGrass grass;
        private Vector2 graphicScroll;
        private Vector2 keybindingScroll;
        private DepthOfField depthOfFieldEffect;
        private readonly System.Collections.Generic.List<Vector2Int> resolutions = new();
        private int resolutionIndex;
        private GUIStyle heading;
        private GUIStyle button;
        private GUIStyle label;

        public void Initialize(FirstPersonController controller,bool deferRendererQuality=false,bool startMenu=false)
        {
            startMenuOnly = startMenu;
            player = controller;
            cameraData = player.PlayerCamera.GetUniversalAdditionalCameraData();
            grass = Object.FindFirstObjectByType<ProceduralGrass>();
            originalVolume = AudioListener.volume;
            originalVSync = QualitySettings.vSyncCount;
            volume = Mathf.Clamp01(PlayerPrefs.GetFloat("Options.Volume", 1f));
            muted = PlayerPrefs.GetInt("Options.Mute", 0) != 0;
            ApplyVolume();
            player.PlayerCamera.fieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat("Options.Fov", 75f), 60f, 100f);
            betterLight = PlayerPrefs.GetInt("Options.BetterLight", 1) != 0;
            depthOfField = PlayerPrefs.GetInt("Options.DepthOfField", 1) != 0;
            fog = PlayerPrefs.GetInt("Options.Fog", 1) != 0;
            loadingRange=PlayerWorldLoading.SavedRange;
            int savedTextureQuality=PlayerPrefs.GetInt("Options.TextureQuality",1);
            textureQuality=savedTextureQuality>=0&&savedTextureQuality<=2?
                (TextureQualityLevel)savedTextureQuality:TextureQualityLevel.High;
            ApplyTextureQuality(!deferRendererQuality);
            Volume sceneVolume = Object.FindFirstObjectByType<Volume>();
            if (sceneVolume != null)
                sceneVolume.profile.TryGet(out depthOfFieldEffect);
            ApplyLighting();
            ApplyDepthOfField();
            ApplyFog();
            foreach (Resolution mode in Screen.resolutions)
            {
                Vector2Int size = new(mode.width, mode.height);
                if (!resolutions.Contains(size)) resolutions.Add(size);
            }
            if (resolutions.Count == 0)
                resolutions.Add(new Vector2Int(Mathf.Max(640, Screen.width), Mathf.Max(480, Screen.height)));
            resolutions.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            Vector2Int savedResolution = new(PlayerPrefs.GetInt("Options.Width", Screen.width),
                PlayerPrefs.GetInt("Options.Height", Screen.height));
            resolutionIndex = resolutions.IndexOf(savedResolution);
            if (resolutionIndex < 0) resolutionIndex = resolutions.Count - 1;
            if (!Application.isEditor && Application.platform != RuntimePlatform.WebGLPlayer && PlayerPrefs.HasKey("Options.Width"))
                ApplyResolution();
            QualitySettings.vSyncCount = PlayerPrefs.GetInt("Options.VSync", originalVSync) != 0 ? 1 : 0;
            if(!deferRendererQuality || startMenuOnly) FinishInitialize();
        }

        internal void FinishInitialize()
        {
            initialized = true;
            OpenMain();
            if (WorldBootstrap.EnterAfterBuild) EnterGame();
        }

        private void Update()
        {
            if (!initialized) return;
            // Room synchronization may resume the controller while a menu is
            // open. Keep input and the cursor consistent with the visible page.
            if(page!=Page.Closed&&player!=null&&!player.IsPaused) player.SetPaused(true);
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browsers consume Escape when releasing pointer lock. Pause even
            // when that key never reaches Unity's keyboard input.
            if (page == Page.Closed && Cursor.lockState != CursorLockMode.Locked &&
                Time.unscaledTime - browserResumeTime > 0.5f)
            {
                OpenPause();
                return;
            }
#endif
            if(Keyboard.current==null) return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (binding >= 0) { binding = -1; return; }
                if (page == Page.Closed) OpenPause();
                else if (page == Page.Pause) Resume();
                else if (page == Page.Main) return;
                else Back();
                return;
            }
            if (binding == FirstPersonController.AimBindingIndex&&
                Mouse.current!=null&&Mouse.current.rightButton.wasPressedThisFrame)
            {
                player.RebindAim(Key.None);
                binding=-1;
            }
            if (binding >= 0)
                foreach (var key in Keyboard.current.allKeys)
                    if (key.wasPressedThisFrame)
                    {
                        if(binding==FirstPersonController.AimBindingIndex)
                        {
                            if(player.RebindAim(key.keyCode)) binding=-1;
                        }
                        else
                        {
                            player.Rebind(binding, key.keyCode);
                            binding=-1;
                        }
                        break;
                    }
        }

        private void OpenMain()
        {
            previousTimeScale = PlayableTimeScale(Time.timeScale);
            previousAudioPause = AudioListener.pause;
            optionsRoot = Page.Main;
            page = Page.Main;
            Time.timeScale = 0f;
            AudioListener.pause = false;
            player.SetPaused(true);
        }

        private void OpenPause()
        {
            previousTimeScale = PlayableTimeScale(Time.timeScale);
            previousAudioPause = AudioListener.pause;
            optionsRoot = Page.Pause;
            page = Page.Pause;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            player.SetPaused(true);
        }

        private void Resume()
        {
            Save();
            page = Page.Closed;
            binding = -1;
            Time.timeScale = PlayableTimeScale(previousTimeScale);
            AudioListener.pause = previousAudioPause;
            player.SetPaused(false);
#if UNITY_WEBGL && !UNITY_EDITOR
            browserResumeTime = Time.unscaledTime;
#endif
        }

        private static float PlayableTimeScale(float value) => value>0f?value:1f;

        private void EnterGame()
        {
            if (!sessionStarted)
            {
                PlayerSaveSystem.TryLoad(player);
                sessionStarted = true;
            }
            Resume();
        }

        private void ReturnToMainMenu()
        {
            if (sessionStarted) PlayerSaveSystem.Save(player);
            Save();
            binding = -1;
            optionsRoot = Page.Main;
            page = Page.Main;
            Time.timeScale = 0f;
            AudioListener.pause = false;
            player.SetPaused(true);
        }

        private void Back()
        {
            Save();
            binding = -1;
            page = page == Page.Options ? optionsRoot : Page.Options;
        }

        private void ApplyVolume() => AudioListener.volume = muted ? 0f : volume;

        private void ApplyLighting()
        {
            // Medium's two-color presentation always keeps its shadow layer,
            // even when the optional richer lighting effect is disabled.
            cameraData.renderShadows = betterLight||
                textureQuality==TextureQualityLevel.Medium;
            cameraData.renderPostProcessing = betterLight || depthOfField;
            Shader.SetGlobalFloat("_WyvernBetterLighting", betterLight ? 1f : 0f);
        }

        private void ApplyDepthOfField()
        {
            cameraData.requiresDepthTexture = depthOfField;
            cameraData.renderPostProcessing = betterLight || depthOfField;
            if (depthOfFieldEffect != null)
            {
                depthOfFieldEffect.mode.Override(depthOfField ? DepthOfFieldMode.Gaussian : DepthOfFieldMode.Off);
                float strength=textureQuality==TextureQualityLevel.High?HighTextureDepthOfFieldScale:
                    textureQuality==TextureQualityLevel.Medium?0.55f:LowTextureDepthOfFieldScale;
                depthOfFieldEffect.gaussianMaxRadius.Override(OriginalDepthOfFieldRadius*strength);
            }
        }

        private void ApplyFog() => WorldBootstrap.ApplyFogState(fog);

        private void ApplyTextureQuality(bool refreshRenderers=true)
        {
            ColorOnlyTextureQuality.SetQuality(textureQuality,refreshRenderers);
            if(cameraData!=null) ApplyLighting();
            // Texture quality also controls the requested Depth of Field strength.
            // During initialization the volume is assigned immediately afterward.
            if(depthOfFieldEffect!=null) ApplyDepthOfField();
        }

        private void ApplyResolution()
        {
            if (Application.isEditor) return;
            Vector2Int size = resolutions[resolutionIndex];
            Screen.SetResolution(size.x, size.y, Screen.fullScreenMode);
            PlayerPrefs.SetInt("Options.Width", size.x);
            PlayerPrefs.SetInt("Options.Height", size.y);
            PlayerPrefs.Save();
        }

        private void Save()
        {
            player.SaveControls();
            PlayerPrefs.SetFloat("Options.Volume", volume);
            PlayerPrefs.SetInt("Options.Mute", muted ? 1 : 0);
            PlayerPrefs.SetFloat("Options.Fov", player.PlayerCamera.fieldOfView);
            PlayerPrefs.SetInt("Options.BetterLight", betterLight ? 1 : 0);
            PlayerPrefs.SetInt("Options.DepthOfField", depthOfField ? 1 : 0);
            PlayerPrefs.SetInt("Options.Fog", fog ? 1 : 0);
            PlayerPrefs.SetInt("Options.TextureQuality",(int)textureQuality);
            PlayerPrefs.SetFloat("Options.LoadingRange",loadingRange);
            PlayerPrefs.SetInt("Options.VSync", QualitySettings.vSyncCount != 0 ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void Quit()
        {
            if (sessionStarted) PlayerSaveSystem.Save(player);
#if UNITY_WEBGL && !UNITY_EDITOR
            ReturnToMainMenu();
            WebBrowserSession.ReturnToWebsite();
#else
            Resume(); // Restore time and audio before leaving Play mode.
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void OnApplicationFocus(bool focused)
        {
            if (!focused && initialized)
            {
                if (sessionStarted) PlayerSaveSystem.Save(player);
                Save();
                if (page == Page.Closed) OpenPause();
            }
        }
#endif

        private void OnApplicationQuit()
        {
            if (sessionStarted) PlayerSaveSystem.Save(player);
        }

        private void OnDisable()
        {
            if (!initialized) return;
            if (page != Page.Closed)
            {
                Time.timeScale = PlayableTimeScale(previousTimeScale);
                AudioListener.pause = previousAudioPause;
                if (player != null) player.SetPaused(false);
            }
            page = Page.Closed;
            AudioListener.volume = originalVolume;
            Shader.SetGlobalFloat("_WyvernBetterLighting", 1f);
            QualitySettings.vSyncCount = originalVSync;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            if (page == Page.Closed || !initialized) return;
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                button = new GUIStyle(GUI.skin.button) { fontSize = 20, fixedHeight = 44 };
                label = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            }
            Color savedColor = GUI.color;
            Matrix4x4 savedMatrix = GUI.matrix;
            GUI.depth = -100;
            GUI.color = new Color(0.02f, 0.035f, 0.05f, 0.82f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float scale = Mathf.Min(Screen.width / 720f, Screen.height / 760f);
            GUI.matrix = Matrix4x4.TRS(new Vector3(Screen.width / 2f - 250f * scale,
                Screen.height / 2f - 345f * scale, 0), Quaternion.identity, Vector3.one * scale);
            GUILayout.BeginArea(new Rect(0, 0, 500, 690), GUI.skin.box);
            GUILayout.Space(24);
            GUILayout.Label(page == Page.Main ? "WYVERNLAND" : page == Page.Pause ? "Paused" : page.ToString(), heading);
            GUILayout.Space(24);
            if (page == Page.Main)
            {
                if (Button("ENTER WYVERNLAND"))
                {
                    if (startMenuOnly || WorldBootstrap.TestGround) WorldBootstrap.SelectMap(false);
                    else EnterGame();
                }
                if (Button("TEST GROUND"))
                {
                    WorldBootstrap.SelectMap(true);
                }
                if (Button("OPTIONS"))
                {
                    optionsRoot = Page.Main;
                    page = Page.Options;
                }
#if UNITY_WEBGL && !UNITY_EDITOR
                if (Button("BACK TO WEBSITE")) Quit();
#else
                if (Button("QUIT")) Quit();
#endif
            }
            else if (page == Page.Pause)
            {
                if (Button("Resume")) Resume();
                if (Button("Options"))
                {
                    optionsRoot = Page.Pause;
                    page = Page.Options;
                }
                if (Button("Return to Menu")) ReturnToMainMenu();
#if UNITY_WEBGL && !UNITY_EDITOR
                if (Button("Back to Website")) Quit();
#else
                if (Button("Quit Game")) Quit();
#endif
            }
            else if (page == Page.Options)
            {
                if (Button("Graphic")) page = Page.Graphic;
                if (Button("Keybinding")) page = Page.Keybinding;
                if (Button("Audio")) page = Page.Audio;
                if (Button("Back")) Back();
            }
            else
            {
                if (page == Page.Graphic)
                {
                    graphicScroll = GUILayout.BeginScrollView(graphicScroll, GUILayout.Height(485));
                    GUILayout.Label($"Object Loading Range: {loadingRange:0} m",label);
                    float range=Mathf.Round(GUILayout.HorizontalSlider(loadingRange,
                        PlayerWorldLoading.MinimumRange,PlayerWorldLoading.MaximumRange)/25f)*25f;
                    if(!Mathf.Approximately(range,loadingRange)) { loadingRange=range;PlayerWorldLoading.SetSavedRange(range); }
                    GUILayout.Label($"Terrain Loading Range: {loadingRange*PlayerWorldLoading.TerrainRangeMultiplier:0} m",label);
                    GUILayout.Label("Moving objects stay loaded, with a 20 m collision area around them.",label);
                    if(grass!=null)
                    {
                        GUILayout.Label($"Grass Density: {grass.Density*100f:0}%",label);
                        float density=Mathf.Round(GUILayout.HorizontalSlider(
                            grass.Density*100f,0f,100f)/5f)*0.05f;
                        if(!Mathf.Approximately(density,grass.Density)) grass.SetDensity(density);
                    }
                    if (Button("Fullscreen: " + (Screen.fullScreen ? "On" : "Off")))
                        Screen.fullScreen = !Screen.fullScreen;
                    if (Button("VSync: " + (QualitySettings.vSyncCount != 0 ? "On" : "Off")))
                        QualitySettings.vSyncCount = QualitySettings.vSyncCount == 0 ? 1 : 0;
                    if(Button("Texture Quality: "+textureQuality))
                    {
                        textureQuality=textureQuality==TextureQualityLevel.High?TextureQualityLevel.Medium:
                            textureQuality==TextureQualityLevel.Medium?TextureQualityLevel.Low:TextureQualityLevel.High;
                        ApplyTextureQuality();
                    }
                    if (Button("Better Light Effect: " + (betterLight ? "On" : "Off")))
                    {
                        betterLight = !betterLight;
                        ApplyLighting();
                    }
                    if (Button("Depth of Field: " + (depthOfField ? "On" : "Off")))
                    {
                        depthOfField = !depthOfField;
                        ApplyDepthOfField();
                    }
                    if (Button("Fog: " + (fog ? "On" : "Off")))
                    {
                        fog = !fog;
                        ApplyFog();
                    }
#if UNITY_WEBGL && !UNITY_EDITOR
                    GUILayout.Label("The game fits your browser window. Use Fullscreen for more space.", label);
#else
                    Vector2Int size = resolutions[resolutionIndex];
                    GUILayout.Label($"Resolution: {size.x} × {size.y}", label);
                    GUILayout.BeginHorizontal();
                    if (Button("Previous")) resolutionIndex = (resolutionIndex + resolutions.Count - 1) % resolutions.Count;
                    if (Button("Next")) resolutionIndex = (resolutionIndex + 1) % resolutions.Count;
                    GUILayout.EndHorizontal();
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = !Application.isEditor;
                    if (Button("Apply Resolution")) ApplyResolution();
                    GUI.enabled = wasEnabled;
                    if (Application.isEditor)
                        GUILayout.Label("Resolution is controlled by Unity's Game view while in the Editor.", label);
#endif
                    GUILayout.Label($"Field of view: {player.PlayerCamera.fieldOfView:0}°", label);
                    player.PlayerCamera.fieldOfView = GUILayout.HorizontalSlider(player.PlayerCamera.fieldOfView, 60f, 100f);
                    GUILayout.EndScrollView();
                }
                else if (page == Page.Keybinding)
                {
                    keybindingScroll=GUILayout.BeginScrollView(keybindingScroll,GUILayout.Height(485));
                    for (int i = 0; i < player.Bindings.Length; i++)
                        if (Button(FirstPersonController.BindingNames[i] + "  —  " +
                            (binding == i ? "Press a key…" : (player.Bindings[i]==Key.Backquote?"`":player.Bindings[i].ToString())+
                            (i==6&&player.Bindings[i]==Key.C?" / LeftCtrl":""))))
                            binding = i;
                    if(Button("Aim / Left pickup  —  "+
                        (binding==FirstPersonController.AimBindingIndex?
                            "Press a key or Right Mouse…":player.AimBindingName)))
                        binding=FirstPersonController.AimBindingIndex;
                    GUILayout.Label(binding >= 0 ? "Esc cancels. Duplicate keys swap actions." : "Select an action to change its key.", label);
                    GUILayout.Label($"Mouse sensitivity: {player.Sensitivity:0.00}", label);
                    player.Sensitivity = GUILayout.HorizontalSlider(player.Sensitivity, 0.03f, 0.4f);
                    if (Button("Reset bindings")) { player.ResetBindings(); binding = -1; }
                    GUILayout.EndScrollView();
                }
                else if (page == Page.Audio)
                {
                    GUILayout.Label($"Master volume: {volume * 100f:0}%", label);
                    volume = GUILayout.HorizontalSlider(volume, 0f, 1f);
                    if (Button("Mute: " + (muted ? "On" : "Off"))) muted = !muted;
                    ApplyVolume();
                }
                GUILayout.Space(16);
                if (Button("Back")) Back();
            }
            GUILayout.EndArea();
            GUI.matrix = savedMatrix;
            GUI.color = savedColor;
        }

        private bool Button(string text)
        {
            bool clicked = GUILayout.Button(text, button);
            GUILayout.Space(8);
            return clicked;
        }
    }
}
