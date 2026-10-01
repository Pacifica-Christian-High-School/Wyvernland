using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    public enum PhysicalDestructEffect { None, Fracture, Shatter, Depression, Slicing, Abrasion }

    [DisallowMultipleComponent]
    public sealed class PhysicalDestructible : MonoBehaviour
    {
        [Min(0)] public float Hardness=5.5f;
        [Min(0)] public float Resilience=5f;
        public string MaterialName="Stone";
        [SerializeField] float massKg;
        [SerializeField] double solidVolumeCubicMetres;
        [SerializeField] bool hasOpenMassSurfaces;
        bool massInitialized,hasMaterialMass;
        string measuredMaterial;
        double localMaterialVolume;
        float measuredVolumeScale;
        Rigidbody measuredBody;
        public float DensityKgPerCubicMetre => MaterialDensity.TryGet(MaterialName,out float density)?density:0f;
        public float MassKg { get { RefreshMassIfNeeded(); return massKg; } }
        public double SolidVolumeCubicMetres { get { RefreshMassIfNeeded(); return solidVolumeCubicMetres; } }
        public bool HasMaterialMass { get { RefreshMassIfNeeded(); return hasMaterialMass; } }
        public bool HasOpenMassSurfaces { get { RefreshMassIfNeeded(); return hasOpenMassSurfaces; } }
        public bool Soft;
        public List<string> Labels=new();
        public bool IsSoft => Soft||Labels.Exists(label=>string.Equals(label,"Soft",StringComparison.OrdinalIgnoreCase));
        public bool IsTerrain;
        [Tooltip("Absolute floor for the material-dependent relative collision speed, in metres per second.")]
        [Min(0)] public float MinimumImpactSpeed=2f;
        [Min(0)] public float SimilarResilienceTolerance=1f;
        [Tooltip("Outward fragment speed gained per metre/second of impact speed.")]
        [Min(0)] public float FragmentImpulsePerSpeed=0.08f;
        public const float LargeShatterImpulseMultiplier=1.6f;
        public const int WoodShatterFacesPerChip=20;
        public const int LargeShatterFragmentsPerImpact=32;
        public const int RockLargeShatterFragmentsPerImpact=22;
        public const int RockFullShatterFragmentsPerImpact=6;
        public const int WoodLargeShatterFragmentsPerImpact=9;
        public const int MaximumFragmentsPerWindow=8;
        public const int MaximumFragmentsPerFrame=2;
        public const int MaximumPendingFragments=64;
        public const int MaximumPendingFragmentVertices=120000;
        public const int MaximumConvexFragmentTriangles=200;
        public const float FragmentSpawnWindowSeconds=1f;
        public const float MinimumQueuedFragmentFps=8f;
        public const float MinimumFragmentAxisSize=0f;
        public const float MinimumFragmentationVolume=0.002f;
        public const double MaximumCompleteDestructionVolume=1d;
        public const double MinimumImpactorVolumeForFullTrunkDamage=0.06d;
        public const float SimilarCollisionDestructionChance=0.4f;
        public const float SimilarPropertyRange=2f;
        public const float CollisionHardnessRetention=0.95f;
        public static readonly PhysicalDestructEffect[] Effects={PhysicalDestructEffect.Fracture,
            PhysicalDestructEffect.Shatter,PhysicalDestructEffect.Depression,PhysicalDestructEffect.Slicing,PhysicalDestructEffect.Abrasion};
        public bool HasBeenDestroyed { get; private set; }
        public bool HasBeenModified { get; private set; }
        public PhysicalDestructEffect LastEffect { get; private set; }
        public static event Action<PhysicalDestructEffect> DestructionOccurred;
        static readonly Queue<float> recentFragmentSpawns=new();
        static readonly List<FragmentSpawnRequest> pendingFragments=new();
        static float smoothedFrameTime;
        static int pendingFragmentVertices,spawnFrame=-1,spawnsThisFrame;
        sealed class FragmentSpawnRequest
        {
            public Mesh Mesh;
            public Vector3 WorldCenter,LinearVelocity,AngularVelocity,Impulse;
            public Material[] Materials;
            public MaterialPropertyBlock[] Appearance;
            public float Mass,Hardness,Resilience,MinimumImpactSpeed,
                SimilarResilienceTolerance,FragmentImpulsePerSpeed,SizeGrowth;
            public double SolidVolume;
            public int FragmentationCount;
            public bool Soft,TreePart,TrunkPart,BranchPart,TemporaryDebris;
            public string Name,MaterialName;
            public List<string> Labels;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetFragmentQueue()
        {
            ClearPendingFragments();
            recentFragmentSpawns.Clear();
            smoothedFrameTime=0f;
            spawnFrame=-1;
            spawnsThisFrame=0;
        }
        internal static void ClearPendingFragments()
        {
            foreach(var request in pendingFragments) Release(request.Mesh);
            pendingFragments.Clear();
            pendingFragmentVertices=0;
        }
        static FragmentSpawnRequest TakeLargestPending()
        {
            int largest=0;
            for(int i=1;i<pendingFragments.Count;i++)
                if(pendingFragments[i].SolidVolume>pendingFragments[largest].SolidVolume)
                    largest=i;
            var request=pendingFragments[largest];
            pendingFragments.RemoveAt(largest);
            pendingFragmentVertices-=request.Mesh.vertexCount;
            return request;
        }
        static bool CanSpawnThisFrame()
        {
            if(spawnFrame!=Time.frameCount)
            { spawnFrame=Time.frameCount; spawnsThisFrame=0; }
            ExpireFragmentSpawns(Time.unscaledTime);
            return spawnsThisFrame<MaximumFragmentsPerFrame&&
                recentFragmentSpawns.Count<MaximumFragmentsPerWindow;
        }
        static void NoteFragmentSpawn()
        {
            spawnsThisFrame++;
            recentFragmentSpawns.Enqueue(Time.unscaledTime);
        }
        static void QueueFragment(FragmentSpawnRequest request)
        {
            int vertices=request.Mesh.vertexCount;
            if(pendingFragments.Count>=MaximumPendingFragments||
                vertices>MaximumPendingFragmentVertices-pendingFragmentVertices)
            { Release(request.Mesh); return; }
            pendingFragments.Add(request);
            pendingFragmentVertices+=vertices;
        }
        static void ExpireFragmentSpawns(float now)
        {
            while(recentFragmentSpawns.Count>0&&
                now-recentFragmentSpawns.Peek()>=FragmentSpawnWindowSeconds)
                recentFragmentSpawns.Dequeue();
        }
        internal static void DrainPendingFragments()
        {
            float dt=Time.unscaledDeltaTime;
            if(dt>0f&&float.IsFinite(dt))
                smoothedFrameTime=smoothedFrameTime<=0f?dt:
                    Mathf.Lerp(smoothedFrameTime,dt,0.08f);
            if(pendingFragments.Count==0||dt<=0f||
                dt>=1f/MinimumQueuedFragmentFps||smoothedFrameTime<=0f||
                1f/smoothedFrameTime<=MinimumQueuedFragmentFps) return;
            if(!CanSpawnThisFrame()) return;
            var request=TakeLargestPending();
            if(request.TemporaryDebris&&!TemporaryShatterDebris.CanSpawn)
            { Release(request.Mesh); return; }
            SpawnPrepared(request,true);
            NoteFragmentSpawn();
        }
        public event Action GeometryChanged;
        public event Action<PhysicalDestructible,IReadOnlyList<PhysicalDestructible>> Fragmented;
        public event Action<float> HardnessChanged;
        float nextImpact;
        float nextGeometryAttempt;
        double lastCollisionStep=double.NegativeInfinity;
        readonly HashSet<int> attemptedCollisionPartners=new();
        readonly HashSet<PhysicalDestructible> collisionRecipients=new();
        Mesh ownedMesh;
        [SerializeField,Min(0)] int fragmentationCount;
        [SerializeField,Range(0f,0.3f)] float fragmentSizeGrowth;
        public const float MinimumFragmentSizeGrowth=0.005f;
        public const float MaximumFragmentSizeGrowth=0.025f;
        public const float MaximumTotalFragmentSizeGrowth=0.3f;
        public const float FragmentSizeGrowthPerCollisionKg=0.0002f;
        public const float MaximumCollisionMassSizeGrowth=0.2f;
        public int FragmentationCount => fragmentationCount;
        public float FragmentSizeMultiplier => 1f+Mathf.Clamp(fragmentSizeGrowth,0f,MaximumTotalFragmentSizeGrowth);

        public static float CollisionMassSizeGrowth(float massKg) =>
            float.IsNaN(massKg)?0f:Mathf.Clamp(massKg*FragmentSizeGrowthPerCollisionKg,0f,MaximumCollisionMassSizeGrowth);

        // The two bonuses are independent and add to the original size:
        // at most 30% from history plus 20% from this collision's other object.
        public float FragmentSizeMultiplierForImpact(float otherMassKg) =>
            FragmentSizeMultiplier+CollisionMassSizeGrowth(otherMassKg);

        public static float FragmentGrowthAfterBreak(float growth,float roll) =>
            Mathf.Min(MaximumTotalFragmentSizeGrowth,
                Mathf.Max(0f,growth)+Mathf.Lerp(MinimumFragmentSizeGrowth,MaximumFragmentSizeGrowth,Mathf.Clamp01(roll)));

        internal void RecordFragmentation()
        {
            if(fragmentationCount<int.MaxValue) fragmentationCount++;
            fragmentSizeGrowth=FragmentGrowthAfterBreak(fragmentSizeGrowth,UnityEngine.Random.value);
        }

        internal void InheritFragmentation(PhysicalDestructible source)
        {
            fragmentationCount=source.fragmentationCount;
            fragmentSizeGrowth=source.fragmentSizeGrowth;
        }

        public static bool AreSimilar(float hardness,float resilience,
            float otherHardness,float otherResilience) =>
            Mathf.Abs(otherHardness-hardness)<=SimilarPropertyRange&&
            Mathf.Abs(otherResilience-resilience)<=SimilarPropertyRange;
        public static bool PassesSimilarityChance(float roll) =>
            roll<SimilarCollisionDestructionChance;
        public static float CollisionWearChance(float resilience) => 0.8f/(1f+Mathf.Max(0f,resilience));
        public static float HardnessAfterCollision(float hardness,float resilience,float roll) =>
            roll>=CollisionWearChance(resilience)?hardness:Mathf.Max(0f,hardness)*CollisionHardnessRetention;
        public static PhysicalDestructEffect Classify(float hardness,float resilience,bool soft,
            float otherHardness,float otherResilience,float tolerance=1f)
        {
            if(AreSimilar(hardness,resilience,otherHardness,otherResilience))
                return soft?PhysicalDestructEffect.Depression:PhysicalDestructEffect.Fracture;
            if(otherHardness<=hardness) return PhysicalDestructEffect.None;
            if(soft) return PhysicalDestructEffect.Depression;
            return otherResilience>resilience+Mathf.Max(0,tolerance)?PhysicalDestructEffect.Shatter:PhysicalDestructEffect.Fracture;
        }
        public float RequiredImpactSpeed(float otherHardness,float otherResilience,
            string otherMaterialName=null)
        {
            if(Classify(Hardness,Resilience,IsSoft,otherHardness,otherResilience,
                SimilarResilienceTolerance)==PhysicalDestructEffect.None)
                return float.PositiveInfinity;
            return InitialImpactSpeed(otherHardness,otherMaterialName);
        }
        // Wear uses the same material-dependent initial speed even when the
        // objects cannot fracture each other or the destruction chance fails.
        public float InitialImpactSpeed(float otherHardness,string otherMaterialName=null)
        {
            // Calibrated at the catalog midpoint for Stone (Hardness 5.5):
            // Wood needs 14 m/s; Limestone needs 8 m/s. Greater hardness
            // advantage lowers the speed needed to start a fracture.
            float threshold=5.8f+1.4f*Resilience-(otherHardness-Hardness);
            if(string.Equals(otherMaterialName,"Stone",StringComparison.OrdinalIgnoreCase))
            {
                if(string.Equals(MaterialName,"Wood",StringComparison.OrdinalIgnoreCase))
                    threshold=Mathf.Max(14f,threshold);
                else if(string.Equals(MaterialName,"Limestone",StringComparison.OrdinalIgnoreCase))
                    threshold=Mathf.Max(8f,threshold);
            }
            return Mathf.Max(MinimumImpactSpeed,threshold);
        }
        public static Vector3 FragmentImpulse(Vector3 sourceCenter,Vector3 fragmentCenter,
            Vector3 fallbackDirection,float relativeSpeed,float fragmentMass,float impulsePerSpeed)
        {
            Vector3 outward=fragmentCenter-sourceCenter;
            if(outward.sqrMagnitude<0.000001f) outward=fallbackDirection;
            if(outward.sqrMagnitude<0.000001f) outward=Vector3.up;
            return outward.normalized*(Mathf.Max(0f,relativeSpeed)*
                Mathf.Max(0f,fragmentMass)*Mathf.Max(0f,impulsePerSpeed));
        }
        public void ApplyMaterial(string material)
        {
            MaterialName=material;
            RefreshMass();
            foreach(var record in WorldConfig.ThermalMaterials)
                if(string.Equals(record.Material,material,StringComparison.OrdinalIgnoreCase))
                {
                    if(material=="Burnt Wood")
                    {
                        uint hash=StableMaterialHash();
                        Hardness=Mathf.Lerp(record.MinimumHardness,record.MaximumHardness,
                            Mix(hash^0x9e3779b9u)/4294967296f);
                        Resilience=Mathf.Lerp(record.MinimumResilience,record.MaximumResilience,
                            Mix(hash^0x85ebca6bu)/4294967296f);
                    }
                    else
                    {
                        Hardness=material=="Stone"?StoneHardness():
                            (record.MinimumHardness+record.MaximumHardness)*0.5f;
                        Resilience=record.Resilience;
                    }
                    return;
                }
            if(material=="Flesh") { Hardness=1f; Resilience=7f; }
            if(material=="Clay"||material=="Mud"||material=="Soil"||material=="Resin")
            { Hardness=0.5f; Resilience=8f; Soft=true; if(!Labels.Contains("Soft")) Labels.Add("Soft"); }
        }
        float StoneHardness()
        {
            // A repeatable sample lets a streamed object retain its hardness when
            // generated again without advancing Unity's global random sequence.
            uint hash=StableMaterialHash();
            float first=Mix(hash^0x9e3779b9u)/4294967296f;
            float second=Mix(hash^0x85ebca6bu)/4294967296f;
            bool rock=HasRockName();
            // Both kinds can span 4–7. Rocks usually draw from the lower half;
            // stones usually draw from the upper half.
            return 4f+3f*(rock?Mathf.Min(first,second):Mathf.Max(first,second));
        }
        bool HasRockName() =>
            gameObject.name.IndexOf("rock",StringComparison.OrdinalIgnoreCase)>=0&&
            gameObject.name.IndexOf("stone",StringComparison.OrdinalIgnoreCase)<0;
        uint StableMaterialHash()
        {
            uint hash=2166136261u;
            foreach(char letter in gameObject.name)
                hash=Mix(hash^(uint)char.ToLowerInvariant(letter));
            Vector3 location=transform.position;
            hash=Mix(hash^(uint)Mathf.RoundToInt(location.x*100f));
            hash=Mix(hash^(uint)Mathf.RoundToInt(location.y*100f));
            return Mix(hash^(uint)Mathf.RoundToInt(location.z*100f));
        }
        static uint Mix(uint value)
        {
            unchecked
            {
                value^=value>>16; value*=0x7feb352du;
                value^=value>>15; value*=0x846ca68bu;
                return value^(value>>16);
            }
        }
        public static PhysicalDestructible Ensure(GameObject target)
        {
            if(IsPlayerObject(target)) return null;
            if(target.TryGetComponent<PhysicalDestructible>(out var existing))
            { existing.RefreshMassIfNeeded(); return existing; }
            var result=target.AddComponent<PhysicalDestructible>();
            string names=target.name;
            var pickable=target.GetComponent<PickableObject>(); if(pickable!=null) names+=" "+pickable.DisplayName;
            var map=target.GetComponentInParent<ProceduralMap>();
            var collider=target.GetComponent<Collider>();
            result.IsTerrain=map!=null&&((collider!=null&&map.IsMapCollider(collider))||names.StartsWith("Terrain render batch",StringComparison.Ordinal));
            if(result.IsTerrain) { result.ApplyMaterial("Clay"); return result; }
            if(names.IndexOf("burnt wood",StringComparison.OrdinalIgnoreCase)>=0||
                names.IndexOf("burnt tree",StringComparison.OrdinalIgnoreCase)>=0||
                names.IndexOf("burnt stump",StringComparison.OrdinalIgnoreCase)>=0)
            { result.ApplyMaterial("Burnt Wood"); return result; }
            if(names.IndexOf("wood",StringComparison.OrdinalIgnoreCase)>=0||names.IndexOf("stump",StringComparison.OrdinalIgnoreCase)>=0)
            { result.ApplyMaterial("Wood"); return result; }
            foreach(string soft in new[]{"Clay","Mud","Soil","Resin","Soft"})
                if(names.IndexOf(soft,StringComparison.OrdinalIgnoreCase)>=0)
                { result.ApplyMaterial(soft=="Soft"?"Clay":soft); result.Soft=true; return result; }
            string best="Stone";
            int length=0;
            foreach(var record in WorldConfig.ThermalMaterials)
                if(names.IndexOf(record.Material,StringComparison.OrdinalIgnoreCase)>=0&&record.Material.Length>length)
                { best=record.Material; length=best.Length; }
            result.ApplyMaterial(best); return result;
        }

        public void RefreshMass()
        {
            massInitialized=true;
            measuredMaterial=MaterialName;
            hasMaterialMass=false; hasOpenMassSurfaces=false;
            localMaterialVolume=0d;
            if(MaterialDensity.TryGet(MaterialName,out _))
            {
                var sources=new List<MeshSolidVolume.Source>();
                var represented=new HashSet<Transform>();
                var ignoredLods=new HashSet<Renderer>();
                foreach(var group in GetComponentsInChildren<LODGroup>(true))
                {
                    var lods=group.GetLODs();
                    for(int i=1;i<lods.Length;i++) foreach(var renderer in lods[i].renderers) ignoredLods.Add(renderer);
                    if(lods.Length>0) foreach(var renderer in lods[0].renderers) ignoredLods.Remove(renderer);
                }
                Matrix4x4 toLocal=transform.worldToLocalMatrix;
                Rigidbody massBody=GetComponent<Rigidbody>();
                bool Owns(Transform source)
                {
                    var owner=source.GetComponentInParent<PhysicalDestructible>(true);
                    if(owner!=null&&owner.HasBeenDestroyed) return false;
                    return owner==this||massBody!=null&&source.GetComponentInParent<Rigidbody>(true)==massBody;
                }
                foreach(var filter in GetComponentsInChildren<MeshFilter>(true))
                {
                    if(filter.sharedMesh==null||!Owns(filter.transform)||
                        ignoredLods.Contains(filter.GetComponent<Renderer>())) continue;
                    sources.Add(new MeshSolidVolume.Source(filter.sharedMesh,toLocal*filter.transform.localToWorldMatrix));
                    represented.Add(filter.transform);
                }
                foreach(var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(skin.sharedMesh==null||!Owns(skin.transform)||
                        ignoredLods.Contains(skin)) continue;
                    sources.Add(new MeshSolidVolume.Source(skin.sharedMesh,toLocal*skin.transform.localToWorldMatrix));
                    represented.Add(skin.transform);
                }
                foreach(var collider in GetComponentsInChildren<MeshCollider>(true))
                {
                    if(collider.sharedMesh==null||collider.isTrigger||represented.Contains(collider.transform)||
                        ignoredLods.Contains(collider.GetComponent<Renderer>())||
                        !Owns(collider.transform)) continue;
                    sources.Add(new MeshSolidVolume.Source(collider.sharedMesh,toLocal*collider.transform.localToWorldMatrix));
                    represented.Add(collider.transform);
                }
                hasMaterialMass=MeshSolidVolume.TryMeasure(sources,out localMaterialVolume,out hasOpenMassSurfaces);
                // Procedural destruction meshes can have sub-millimetre render
                // seams. Only use their established weld tolerance when every
                // input is already verified as a closed solid by destruction.
                if(hasMaterialMass&&hasOpenMassSurfaces&&sources.TrueForAll(source=>DestructionMesh.IsClosedConnected(source.Mesh)))
                    hasMaterialMass=MeshSolidVolume.TryMeasure(sources,out localMaterialVolume,out hasOpenMassSurfaces,0.00011f);
            }
            ApplyMeasuredMass();
        }

        void RefreshMassIfNeeded()
        {
            if(!massInitialized||measuredMaterial!=MaterialName) { RefreshMass(); return; }
            float scale=Mathf.Abs(transform.localToWorldMatrix.determinant);
            if(scale!=measuredVolumeScale||GetComponent<Rigidbody>()!=measuredBody) ApplyMeasuredMass();
        }

        void ApplyMeasuredMass()
        {
            measuredVolumeScale=Mathf.Abs(transform.localToWorldMatrix.determinant);
            solidVolumeCubicMetres=localMaterialVolume*measuredVolumeScale;
            massKg=MaterialDensity.TryGet(MaterialName,out float density)?(float)(solidVolumeCubicMetres*density):0f;
            measuredBody=GetComponent<Rigidbody>();
            // Unity requires a positive Rigidbody mass. Zero-thickness render
            // surfaces retain zero material mass; only the physics solver uses
            // an epsilon if a Rigidbody is attached to one of those surfaces.
            if(hasMaterialMass&&measuredBody!=null&&float.IsFinite(massKg)) measuredBody.mass=Mathf.Max(0.0000001f,massKg);
        }

        void Start() => RefreshMassIfNeeded();
        public static PhysicalDestructible Resolve(Collider collider)
        {
            if(collider==null||collider.isTrigger) return null;
            if(IsPlayerObject(collider.gameObject)) return null;
            var existing=collider.GetComponentInParent<PhysicalDestructible>();
            // A tree section can be parented under a moving section's body.
            // Its own collider still belongs to its own destructible geometry.
            if(existing!=null) return existing;
            if(collider.attachedRigidbody!=null) return Ensure(collider.attachedRigidbody.gameObject);
            return existing!=null?existing:Ensure(collider.gameObject);
        }
        bool Owns(Component component) =>
            component!=null&&component.GetComponentInParent<PhysicalDestructible>(true)==this;
        static bool IsPlayerObject(GameObject target)
        {
            if(target==null) return false;
            // A held item is parented to the player's hand, but remains its own
            // physical object and collision damage source.
            foreach(var item in target.GetComponentsInParent<PickableObject>(true))
                if(item.IsHeld) return false;
            return target.GetComponentInParent<FirstPersonController>(true)!=null;
        }
        void OnCollisionEnter(Collision collision) => ProcessCollision(collision);
        void OnCollisionStay(Collision collision) => ProcessCollision(collision);
        internal void ProcessCollision(Collision collision)
        {
            // Most resting contacts are below both solids' minimum damage
            // speed. Skip them before resolving materials or enumerating the
            // contact manifold. Held items and fragments use special rules.
            var otherCollider=collision.collider;
            float relativeSpeed=collision.relativeVelocity.magnitude;
            if(otherCollider!=null&&relativeSpeed<MinimumImpactSpeed&&
                GetComponent<PickableObject>() is not { IsHeld: true }&&
                otherCollider.GetComponentInParent<PickableObject>() is not { IsHeld: true }&&
                GetComponent<DestructionFragment>()==null&&
                otherCollider.GetComponentInParent<DestructionFragment>()==null)
            {
                var knownOther=otherCollider.GetComponentInParent<PhysicalDestructible>();
                if(knownOther==null||relativeSpeed<knownOther.MinimumImpactSpeed) return;
            }
            var other=Resolve(collision.collider);
            bool held=GetComponent<PickableObject>() is { IsHeld: true }||
                other!=null&&other.GetComponent<PickableObject>() is { IsHeld: true };
            bool handled=false;
            collisionRecipients.Clear();
            for(int i=0;i<collision.contactCount;i++)
            {
                var contact=collision.GetContact(i);
                var recipient=Resolve(contact.thisCollider);
                if(recipient==null) continue;
                // Ordinary contacts in the same manifold share one relative
                // velocity. Process each solid once, even with many contact points.
                // Held objects use point velocity, which can vary by contact.
                if(!held&&!collisionRecipients.Add(recipient)) continue;
                recipient.HandleCollision(collision,contact,other);
                handled=true;
            }
            if(!handled) HandleCollision(collision,default,other);
        }
        void HandleCollision(Collision collision,ContactPoint contact,PhysicalDestructible other)
        {
            if(IsPlayerObject(gameObject)) return;
            if(other==this) return;
            float collisionSpeed=other!=null?
                RelativeCollisionSpeed(collision,other,contact.point):collision.relativeVelocity.magnitude;
            if(other!=null&&GetComponent<DestructionFragment>()==null&&
                other.GetComponent<DestructionFragment>()==null&&
                collisionSpeed<InitialImpactSpeed(other.Hardness,other.MaterialName)&&
                collisionSpeed<other.InitialImpactSpeed(Hardness,MaterialName)) return;
            if(TrySpecialFragmentCollision(other,contact.point,contact.normal,
                collisionSpeed)) return;
            if(other==null)
            {
                // The player has no destructible component, but a sufficiently
                // fast player impact can still wear the object it strikes.
                if(collision.collider!=null&&IsPlayerObject(collision.collider.gameObject)&&
                    RegisterCollision(collision.collider.GetInstanceID())&&
                    collision.relativeVelocity.magnitude>=InitialImpactSpeed(1f,"Flesh"))
                    ApplyCollisionWear();
                return;
            }
            float speed=collisionSpeed;
            // Dispatch both directions, including static colliders. Each recipient
            // handles this pair only once per physics step, so two callbacks do not
            // turn one destruction or wear roll into two chances/losses.
            float ourHardness=Hardness,ourResilience=Resilience;
            float theirHardness=other.Hardness,theirResilience=other.Resilience;
            bool ourWear=speed>=InitialImpactSpeed(theirHardness,other.MaterialName);
            bool theirWear=speed>=other.InitialImpactSpeed(ourHardness,MaterialName);
            if(!ourWear&&!theirWear) return;
            bool ourCollision=RegisterCollision(other.GetInstanceID());
            bool theirCollision=other.RegisterCollision(GetInstanceID());
            if(ourCollision&&ourWear) ApplyCollisionWear();
            if(theirCollision&&theirWear) other.ApplyCollisionWear();
            if(collision.contactCount==0) return;
            if(!ourCollision&&!theirCollision) return;
            var ourEffect=Classify(ourHardness,ourResilience,IsSoft,
                theirHardness,theirResilience,SimilarResilienceTolerance);
            var theirEffect=Classify(theirHardness,theirResilience,other.IsSoft,
                ourHardness,ourResilience,other.SimilarResilienceTolerance);
            bool tryOurs=ourCollision&&!HasBeenDestroyed&&Time.time>=nextImpact&&
                Time.time>=nextGeometryAttempt&&
                (ourEffect==PhysicalDestructEffect.Fracture||ourEffect==PhysicalDestructEffect.Shatter)&&
                speed>=InitialImpactSpeed(theirHardness,other.MaterialName);
            bool tryTheirs=theirCollision&&!other.HasBeenDestroyed&&Time.time>=other.nextImpact&&
                Time.time>=other.nextGeometryAttempt&&
                (theirEffect==PhysicalDestructEffect.Fracture||theirEffect==PhysicalDestructEffect.Shatter)&&
                speed>=other.InitialImpactSpeed(ourHardness,MaterialName);
            if(!tryOurs&&!tryTheirs) return;
            float ourSize=tryTheirs?WorldSize():0f;
            float theirSize=tryOurs?other.WorldSize():0f;
            double ourVolume=tryTheirs?ImpactSolidVolume():0d;
            double theirVolume=tryOurs?other.ImpactSolidVolume():0d;
            // Snapshot the relevant mass before either recipient changes geometry.
            float ourMass=tryTheirs?ImpactMassKg():0f;
            float theirMass=tryOurs?other.ImpactMassKg():0f;
            if(tryOurs)
                ApplyImpact(theirHardness,theirResilience,theirSize,
                    contact.point,contact.normal,speed,other.MaterialName,theirVolume,other.IsTerrain,theirMass);
            if(tryTheirs&&other!=null)
                other.ApplyImpact(ourHardness,ourResilience,ourSize,
                    contact.point,-contact.normal,speed,MaterialName,ourVolume,IsTerrain,ourMass);
        }
        float ImpactMassKg()
        {
            var body=GetComponentInParent<Rigidbody>();
            var assembly=body!=null?body.GetComponent<PhysicalDestructible>():null;
            if(assembly!=null&&assembly!=this) return MassKg;
            if(assembly!=null&&assembly.HasMaterialMass) return assembly.MassKg;
            return body!=null?body.mass:MassKg;
        }
        double ImpactSolidVolume()
        {
            var body=GetComponentInParent<Rigidbody>();
            var assembly=body!=null?body.GetComponent<PhysicalDestructible>():null;
            if(assembly!=null&&assembly!=this) return SolidVolumeCubicMetres;
            return assembly!=null?assembly.SolidVolumeCubicMetres:SolidVolumeCubicMetres;
        }
        bool TrySpecialFragmentCollision(PhysicalDestructible other,Vector3 point,
            Vector3 normal,float speed)
        {
            bool ourFragment=GetComponent<DestructionFragment>()!=null;
            bool theirFragment=other!=null&&other.GetComponent<DestructionFragment>()!=null;
            if(!ourFragment&&!theirFragment) return false;
            if(GetComponent<TemporaryShatterDebris>()!=null||
                other!=null&&other.GetComponent<TemporaryShatterDebris>()!=null)
                return true;
            bool ours=ourFragment&&SolidVolumeCubicMetres<MinimumFragmentationVolume;
            bool theirs=theirFragment&&other.SolidVolumeCubicMetres<MinimumFragmentationVolume;
            if(!ours&&!theirs) return false;
            PhysicalDestructible smaller=ours&&theirs?
                (SolidVolumeCubicMetres<=other.SolidVolumeCubicMetres?this:other):
                ours?this:other;
            if(smaller.HasBeenDestroyed) return true;
            PhysicalDestructible impactor=smaller==this?other:this;
            if(smaller.IsUndersizedBranchFragment&&
                (impactor==null||!smaller.CanShatterUndersizedBranch(
                    impactor.Hardness,impactor.Resilience,impactor.MaterialName,speed)))
                return true;
            smaller.SpecialCompleteShatter(point,smaller==this?normal:-normal,speed);
            return true;
        }

        bool IsUndersizedBranchFragment =>
            GetComponent<DestructionFragment>() is { IsBranchFragment: true }&&
            SolidVolumeCubicMetres<MinimumFragmentationVolume;

        bool CanShatterUndersizedBranch(float otherHardness,float otherResilience,
            string otherMaterialName,float speed) =>
            otherHardness>=Mathf.Min(10f,2f*Hardness)&&
            speed>=RequiredImpactSpeed(otherHardness,otherResilience,otherMaterialName);
        float RelativeCollisionSpeed(Collision collision,PhysicalDestructible other,Vector3 point)
        {
            var held=GetComponent<PickableObject>();
            var otherHeld=other.GetComponent<PickableObject>();
            bool heldImpact=held!=null&&held.IsHeld||otherHeld!=null&&otherHeld.IsHeld;
            if(!heldImpact)
                return collision.relativeVelocity.magnitude;
            Rigidbody ourBody=GetComponentInParent<Rigidbody>(),theirBody=other.GetComponentInParent<Rigidbody>();
            Vector3 ourVelocity=held!=null&&held.IsHeld?held.HeldPointVelocity(point):
                ourBody!=null?ourBody.GetPointVelocity(point):Vector3.zero;
            Vector3 theirVelocity=otherHeld!=null&&otherHeld.IsHeld?otherHeld.HeldPointVelocity(point):
                theirBody!=null?theirBody.GetPointVelocity(point):Vector3.zero;
            float pointSpeed=(ourVelocity-theirVelocity).magnitude;
            return heldImpact?pointSpeed:Mathf.Max(pointSpeed,collision.relativeVelocity.magnitude);
        }
        bool RegisterCollision(int partnerId)
        {
            double step=Time.fixedTimeAsDouble;
            if(step!=lastCollisionStep)
            { attemptedCollisionPartners.Clear(); lastCollisionStep=step; }
            return attemptedCollisionPartners.Add(partnerId);
        }
        void ApplyCollisionWear()
        {
            float remaining=HardnessAfterCollision(Hardness,Resilience,UnityEngine.Random.value);
            if(remaining==Hardness) return;
            Hardness=remaining;
            HardnessChanged?.Invoke(remaining);
        }
        public float WorldSize()
        {
            bool found=false; Bounds bounds=default;
            foreach(var renderer in GetComponentsInChildren<Renderer>())
            { if(!renderer.enabled||!Owns(renderer)) continue; if(!found) { bounds=renderer.bounds; found=true; } else bounds.Encapsulate(renderer.bounds); }
            if(!found) foreach(var collider in GetComponentsInChildren<Collider>())
            { if(collider.isTrigger||!Owns(collider)) continue; if(!found) { bounds=collider.bounds; found=true; } else bounds.Encapsulate(collider.bounds); }
            return Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
        }
        void SpecialCompleteShatter(Vector3 point,Vector3 normal,float speed)
        {
            if(HasBeenDestroyed) return;
            Mesh source=Snapshot(out Material[] materials,out MaterialPropertyBlock[] appearance);
            if(source==null) return;
            Vector3 sourceCenter=transform.TransformPoint(DestructionMesh.VolumeCentroid(source));
            Mesh first=null,second=null;
            bool usedFallback=false;
            for(int attempt=0;attempt<24;attempt++)
            {
                if(!DestructionMesh.Split(source,
                    new Plane(UnityEngine.Random.onUnitSphere,source.bounds.center),
                    out first,out second))
                {
                    if(first!=null) Release(first);
                    if(second!=null) Release(second);
                    continue;
                }
                if(DestructionMesh.IsClosedConnected(first)&&
                    DestructionMesh.IsClosedConnected(second)) break;
                Release(first); Release(second); first=second=null;
            }
            if(first==null||second==null)
            {
                // An unusually thin closed mesh may not admit a reliable cut.
                // Two short-lived scaled copies still give it a visible burst.
                var fallback=TemporaryFallbackMeshes(source);
                first=fallback[0]; second=fallback[1];
                usedFallback=true;
            }
            Mesh[] parts={first,second};
            var centers=new Vector3[2];
            bool prepared=true;
            for(int i=0;i<parts.Length;i++)
                prepared&=PrepareFragmentMesh(parts[i],transform.localToWorldMatrix,out centers[i]);
            if(!prepared)
            {
                foreach(var part in parts) Release(part);
                if(usedFallback) { Release(source); return; }
                parts=TemporaryFallbackMeshes(source);
                prepared=true;
                for(int i=0;i<parts.Length;i++)
                    prepared&=PrepareFragmentMesh(parts[i],transform.localToWorldMatrix,out centers[i]);
                if(!prepared)
                {
                    foreach(var part in parts) Release(part);
                    Release(source);
                    return;
                }
            }
            Rigidbody body=GetComponentInParent<Rigidbody>();
            float totalVolume=0f;
            foreach(var part in parts) totalVolume+=Volume(part);
            float mass=MassKg;
            var requests=new List<FragmentSpawnRequest>(parts.Length);
            for(int i=0;i<parts.Length;i++)
            {
                var request=PrepareSpawn(parts[i],centers[i],materials,appearance,body,
                    mass*Volume(parts[i])/Mathf.Max(totalVolume,0.000001f),
                    sourceCenter,normal,Mathf.Max(speed,1f),LargeShatterImpulseMultiplier);
                request.TemporaryDebris=true;
                requests.Add(request);
            }
            Release(source);
            RecordFragmentation();
            LastEffect=PhysicalDestructEffect.Shatter;
            HasBeenModified=true;
            HasBeenDestroyed=true;
            var spawned=new List<PhysicalDestructible>(requests.Count);
            SpawnOrQueue(requests,spawned);
            gameObject.SetActive(false);
            Release(gameObject);
            Fragmented?.Invoke(this,spawned);
            GeometryChanged?.Invoke();
            DestructionOccurred?.Invoke(PhysicalDestructEffect.Shatter);
        }
        static Mesh[] TemporaryFallbackMeshes(Mesh source)
        {
            Mesh[] pieces={UnityEngine.Object.Instantiate(source),UnityEngine.Object.Instantiate(source)};
            Vector3 center=DestructionMesh.VolumeCentroid(source);
            float scale=Mathf.Pow(0.5f,1f/3f);
            foreach(Mesh mesh in pieces)
            {
                var vertices=mesh.vertices;
                for(int i=0;i<vertices.Length;i++)
                    vertices[i]=center+(vertices[i]-center)*scale;
                mesh.vertices=vertices;
                mesh.RecalculateBounds();
            }
            return pieces;
        }
        public bool ApplyImpact(float otherHardness,float otherResilience,float otherSize,
            Vector3 point,Vector3 normal,float speed,string otherMaterialName=null,double otherSolidVolume=0d,
            bool groundImpact=false,float otherMassKg=0f,Vector3? treeSplitPoint=null)
        {
            if(IsUndersizedBranchFragment)
            {
                if(HasBeenDestroyed||!CanShatterUndersizedBranch(otherHardness,
                    otherResilience,otherMaterialName,speed)) return false;
                SpecialCompleteShatter(point,normal,speed);
                return HasBeenDestroyed;
            }
            bool forcedTreeSplit=treeSplitPoint.HasValue;
            GeneratedTree tree=forcedTreeSplit?null:GetComponentInParent<GeneratedTree>();
            if(IsPlayerObject(gameObject)||HasBeenDestroyed||!forcedTreeSplit&&
                (Time.time<nextImpact||Application.isPlaying&&Time.time<nextGeometryAttempt||
                speed<RequiredImpactSpeed(otherHardness,otherResilience,otherMaterialName))) return false;
            var effect=forcedTreeSplit?PhysicalDestructEffect.Fracture:
                Classify(Hardness,Resilience,IsSoft,otherHardness,otherResilience,SimilarResilienceTolerance);
            bool smallImpactorOnTrunk=!forcedTreeSplit&&!groundImpact&&
                GetComponent<TreeStructuralPart>() is { IsTrunk: true }&&
                otherSolidVolume<MinimumImpactorVolumeForFullTrunkDamage;
            // A small impactor can chip a trunk if it can cause Shatter. It
            // cannot fracture it or replace the whole section with fragments.
            if(smallImpactorOnTrunk&&effect==PhysicalDestructEffect.Fracture)
            {
                if(otherHardness-Hardness<=2f) { LastEffect=effect; return false; }
                effect=PhysicalDestructEffect.Shatter;
            }
            // A Fracture needs an impactor larger than half the target's
            // longest world-space dimension. A hardness lead above two turns
            // a size-blocked Fracture into Shatter instead.
            if(!forcedTreeSplit&&effect==PhysicalDestructEffect.Fracture&&
                WorldSize()>=2f*Mathf.Max(0f,otherSize))
            {
                if(otherHardness-Hardness<=2f) { LastEffect=effect; return false; }
                effect=PhysicalDestructEffect.Shatter;
            }
            LastEffect=effect;
            if(!forcedTreeSplit&&AreSimilar(Hardness,Resilience,otherHardness,otherResilience)&&
                !PassesSimilarityChance(UnityEngine.Random.value)) return false;
            if(effect!=PhysicalDestructEffect.Fracture&&effect!=PhysicalDestructEffect.Shatter) return false;
            TreeStructuralPart structure=GetComponent<TreeStructuralPart>();
            if(!forcedTreeSplit&&structure!=null&&structure.IsAttachedBranch)
            {
                structure.RecordBranchDestruction();
                NotifyGeometryChanged();
                tree?.RecordDestructiveImpact(point,false);
                DestructionOccurred?.Invoke(effect);
                return true;
            }
            // Failed cuts must not retry their full search every physics tick.
            if(Application.isPlaying) nextGeometryAttempt=Time.time+0.15f;
            using var validationScope=DestructionMesh.BeginValidationScope();
            Mesh source=Snapshot(out Material[] materials,out MaterialPropertyBlock[] appearance);
            if(source==null) return false;
            Bounds originalMeshBounds=source.bounds;
            // Capture the original solid's centre before the cut changes or
            // releases its mesh. Every emitted piece receives an outward shock.
            Vector3 sourceCenter=transform.TransformPoint(DestructionMesh.VolumeCentroid(source));
            var sourceParts=new List<Mesh>();
            bool validSource=SeparateAndClose(source,sourceParts);
            if(!validSource)
            { foreach(var part in sourceParts) Release(part); return false; }
            // Use mesh volume in world units, rather than the enclosing bounds:
            // thin or irregular objects must not fragment just because their bounds are large.
            double worldVolume=0;
            foreach(var part in sourceParts) worldVolume+=Volume(part);
            double originalLocalVolume=worldVolume;
            worldVolume*=Math.Abs((double)transform.localToWorldMatrix.determinant);
            if(sourceParts.Count>1)
            {
                var volumeSources=new List<MeshSolidVolume.Source>();
                foreach(var part in sourceParts) volumeSources.Add(new MeshSolidVolume.Source(part,Matrix4x4.identity));
                if(MeshSolidVolume.TryMeasure(volumeSources,out double materialVolume,out bool openVolume,0.00011f)&&!openVolume)
                    worldVolume=materialVolume*Math.Abs((double)transform.localToWorldMatrix.determinant);
            }
            bool preserveRemainder=worldVolume>MaximumCompleteDestructionVolume;
            if(worldVolume<MinimumFragmentationVolume)
            { foreach(var part in sourceParts) Release(part); return false; }
            var pieces=new List<Mesh>(); Mesh remainder=null;
            bool separatedSource=sourceParts.Count>1;
            bool local=!forcedTreeSplit&&(smallImpactorOnTrunk||preserveRemainder||separatedSource||effect==PhysicalDestructEffect.Shatter&&
                WorldSize()>=2f*Mathf.Max(0.001f,otherSize));
            bool wood=string.Equals(MaterialName,"Wood",StringComparison.OrdinalIgnoreCase)||
                string.Equals(MaterialName,"Burnt Wood",StringComparison.OrdinalIgnoreCase);
            bool rock=string.Equals(MaterialName,"Stone",StringComparison.OrdinalIgnoreCase)&&
                HasRockName();
            // Growth changes preferred chip sizing, never the minimum volume
            // needed to damage an object or one of its inherited fragments.
            float sizeGrowth=FragmentSizeMultiplierForImpact(otherMassKg);
            int fragmentBudget=wood?WoodLargeShatterFragmentsPerImpact:
                rock&&effect==PhysicalDestructEffect.Shatter?
                    RockLargeShatterFragmentsPerImpact:LargeShatterFragmentsPerImpact;
            float fragmentSize=sizeGrowth;
            Vector3 localPoint=transform.InverseTransformPoint(point);
            if(separatedSource)
            {
                int lowest=0;
                for(int i=1;i<sourceParts.Count;i++)
                    if(WorldMinY(sourceParts[i],transform.localToWorldMatrix)<
                        WorldMinY(sourceParts[lowest],transform.localToWorldMatrix)) lowest=i;
                remainder=sourceParts[lowest];
                for(int i=0;i<sourceParts.Count;i++)
                    if(i!=lowest) pieces.Add(sourceParts[i]);
            }
            else if(local)
            {
                source=sourceParts[0];
                float scale=Mathf.Max(0.001f,transform.lossyScale.magnitude/Mathf.Sqrt(3));
                float baseSize=1f;
                float baseRadius=Mathf.Max(otherSize*0.75f/scale,source.bounds.size.magnitude*0.03f)*baseSize;
                float radius=baseRadius*sizeGrowth;
                if(preserveRemainder||smallImpactorOnTrunk)
                    radius=Mathf.Min(radius,source.bounds.size.magnitude*0.25f);
                int baseFaces=wood?WoodShatterFacesPerChip:
                    rock&&effect==PhysicalDestructEffect.Shatter?3:2;
                int faces=Mathf.CeilToInt(baseFaces*sizeGrowth);
                float minimumDepth=MinimumFragmentAxisSize*1.2f*fragmentSize/scale;
                pieces=rock&&effect==PhysicalDestructEffect.Shatter?
                    RockPartialShatterChips(source,localPoint,radius,out remainder,
                        faces,fragmentBudget,minimumDepth,Hardness):
                    ChipWithinFragmentBudget(source,localPoint,radius,out remainder,
                        faces,fragmentBudget,minimumDepth,Hardness);
                // Prefer larger chips, but retry the original valid sizing if
                // that preference cannot fit the remaining mesh.
                if(sizeGrowth>1f&&!LocalChipsAreValid(pieces,remainder,transform.localToWorldMatrix))
                {
                    foreach(var chip in pieces) Release(chip);
                    if(remainder!=null) Release(remainder);
                    if(preserveRemainder||smallImpactorOnTrunk)
                        baseRadius=Mathf.Min(baseRadius,source.bounds.size.magnitude*0.25f);
                    pieces=rock&&effect==PhysicalDestructEffect.Shatter?
                        RockPartialShatterChips(source,localPoint,baseRadius,out remainder,
                            baseFaces,fragmentBudget,
                            MinimumFragmentAxisSize*1.2f*baseSize/scale,Hardness):
                        ChipWithinFragmentBudget(source,localPoint,baseRadius,out remainder,
                            baseFaces,fragmentBudget,
                            MinimumFragmentAxisSize*1.2f*baseSize/scale,Hardness);
                }
                // A failed local cut leaves volume-protected objects intact.
                // Smaller objects can fall back to a complete split.
                if(!LocalChipsAreValid(pieces,remainder,transform.localToWorldMatrix))
                {
                    foreach(var chip in pieces) Release(chip);
                    if(remainder!=null) Release(remainder);
                    if(smallImpactorOnTrunk) { Release(source); return false; }
                    if(preserveRemainder)
                    {
                        // Repeated chips make a surface increasingly intricate. A
                        // clean split can still remove material while retaining the
                        // lower portion when the chip budget cannot be satisfied.
                        bool split=TryFractureSplit(source,transform.localToWorldMatrix,
                            false,out var first,out var second,out _,
                            Vector3.Lerp(localPoint,source.bounds.center,0.5f));
                        if(!split)
                            split=TryFractureSplit(source,transform.localToWorldMatrix,
                                false,out first,out second,out _);
                        if(!split) { Release(source); return false; }
                        if(WorldMinY(first,transform.localToWorldMatrix)<=
                            WorldMinY(second,transform.localToWorldMatrix))
                        { remainder=first; pieces=new List<Mesh>{second}; }
                        else { remainder=second; pieces=new List<Mesh>{first}; }
                    }
                    else { pieces=new List<Mesh>(); remainder=null; local=false; }
                }
            }
            if(!local)
            {
                source=sourceParts[0];
                // Draw the fracture plane independently of the contact normal.
                // Retry random orientations because a valid cut of an irregular
                // mesh may not exist at the first orientation.
                Vector3 localNormal=UnityEngine.Random.onUnitSphere;
                bool split;
                Mesh a,b;
                if(forcedTreeSplit)
                {
                    localNormal=transform.InverseTransformDirection(Vector3.up).normalized;
                    split=DestructionMesh.Split(source,new Plane(localNormal,
                        transform.InverseTransformPoint(treeSplitPoint.Value)),out a,out b);
                    if(split&&(!ClosedAfterSeparation(a,transform.localToWorldMatrix)||
                        !ClosedAfterSeparation(b,transform.localToWorldMatrix)))
                    { Release(a); Release(b); a=b=null; split=false; }
                }
                else split=TryFractureSplit(source,transform.localToWorldMatrix,
                    effect==PhysicalDestructEffect.Fracture,
                    out a,out b,out localNormal,
                    effect==PhysicalDestructEffect.Shatter?
                        (Vector3?)ShatterDividingPoint(localPoint,source.bounds.center,
                            Vector3.Distance(localPoint,source.bounds.center),Hardness):null);
                if(split)
                {
                    pieces.Add(a); pieces.Add(b);
                    if(effect==PhysicalDestructEffect.Shatter&&!wood)
                    {
                        // Prefer fewer, larger shards as either bonus rises.
                        // This only skips extra subdivisions; it never vetoes
                        // the initial valid split or makes the source immune.
                        float preferredShardVolume=(float)worldVolume*sizeGrowth/8f;
                        Vector3 tangent=Vector3.Cross(localNormal,Mathf.Abs(localNormal.y)<0.9f?Vector3.up:Vector3.right).normalized;
                        int fullShatterLimit=rock?RockFullShatterFragmentsPerImpact:int.MaxValue;
                        SplitPieces(pieces,tangent,transform.localToWorldMatrix,
                            sizeGrowth>1f?preferredShardVolume:0f,fullShatterLimit);
                        SplitPieces(pieces,Vector3.Cross(localNormal,tangent),transform.localToWorldMatrix,
                            sizeGrowth>1f?preferredShardVolume:0f,fullShatterLimit);
                    }
                }
            }
            if(!separatedSource) Release(source);
            var rawPieces=pieces;
            pieces=new List<Mesh>();
            bool validPieces=true;
            foreach(var piece in rawPieces)
                validPieces&=SeparateAndClose(piece,pieces);
            if(remainder!=null)
            {
                var components=new List<Mesh>();
                validPieces&=SeparateAndClose(remainder,components);
                remainder=null;
                if(components.Count>0)
                {
                    int lowest=0;
                    for(int i=1;i<components.Count;i++)
                        if(WorldMinY(components[i],transform.localToWorldMatrix)<
                            WorldMinY(components[lowest],transform.localToWorldMatrix)) lowest=i;
                    remainder=components[lowest];
                    for(int i=0;i<components.Count;i++)
                        if(i!=lowest) pieces.Add(components[i]);
                }
            }
            // Check every final mesh before changing the source. An open edge,
            // non-manifold edge, or detached face would make an invalid fragment.
            double resultingLocalVolume=remainder!=null?DestructionMesh.SolidVolume(remainder):0d;
            foreach(var piece in pieces) resultingLocalVolume+=DestructionMesh.SolidVolume(piece);
            bool conservesSourceVolume=Math.Abs(resultingLocalVolume-originalLocalVolume)<=
                Math.Max(0.00001d,originalLocalVolume*0.002d);
            if(!forcedTreeSplit&&(preserveRemainder||smallImpactorOnTrunk)&&(!local||remainder==null)||
                !validPieces||pieces.Count==0||local&&!separatedSource&&pieces.Count>
                fragmentBudget||
                !conservesSourceVolume||
                pieces.Exists(piece=>!DestructionMesh.IsClosedConnected(piece))||
                pieces.Exists(piece=>!WithinOriginalBounds(piece,originalMeshBounds))||
                pieces.Exists(piece=>!FragmentHasMinimumSize(piece,transform.localToWorldMatrix))||
                remainder!=null&&(!DestructionMesh.IsClosedConnected(remainder)||
                    !WithinOriginalBounds(remainder,originalMeshBounds)))
            {
                foreach(var piece in pieces) Release(piece);
                if(remainder!=null) Release(remainder);
                return false;
            }
            var body=GetComponent<Rigidbody>();
            var inheritedBody=GetComponentInParent<Rigidbody>();
            float totalVolume=0;
            foreach(var mesh in pieces) totalVolume+=Volume(mesh);
            if(remainder!=null) totalVolume+=Volume(remainder);
            float mass=MaterialDensity.TryGet(MaterialName,out float density)?
                totalVolume*Mathf.Abs(transform.localToWorldMatrix.determinant)*density:
                body!=null?body.mass:1f;
            var centers=new Vector3[pieces.Count];
            var pieceMasses=new float[pieces.Count];
            bool prepared=true;
            for(int i=0;i<pieces.Count;i++)
            {
                pieceMasses[i]=mass*Volume(pieces[i])/Mathf.Max(totalVolume,0.000001f);
                prepared&=PrepareFragmentMesh(pieces[i],transform.localToWorldMatrix,out centers[i]);
            }
            // Transforming and welding can collapse thin geometry. Validate the
            // actual rendered meshes before emitting anything or changing the source.
            if(!prepared)
            {
                foreach(var piece in pieces) Release(piece);
                if(remainder!=null) Release(remainder);
                return false;
            }
            RecordFragmentation();
            nextImpact=Time.time+0.25f; HasBeenModified=true;
            var requests=new List<FragmentSpawnRequest>(pieces.Count);
            for(int i=0;i<pieces.Count;i++)
                requests.Add(PrepareSpawn(pieces[i],centers[i],materials,appearance,
                    inheritedBody,pieceMasses[i],sourceCenter,normal,speed,
                    local?LargeShatterImpulseMultiplier:1f));
            var spawned=new List<PhysicalDestructible>(pieces.Count);
            SpawnOrQueue(requests,spawned);
            if(local&&remainder!=null)
            {
                ReplaceGeometry(remainder,materials,appearance,body);
                RefreshMass();
                if(body!=null) { body.ResetCenterOfMass(); body.ResetInertiaTensor(); }
            }
            else
            {
                if(remainder!=null) Release(remainder);
                HasBeenDestroyed=true;
                gameObject.SetActive(false); Release(gameObject);
            }
            Fragmented?.Invoke(this,spawned);
            GeometryChanged?.Invoke();
            DestructionOccurred?.Invoke(effect);
            if(tree!=null) tree.RecordDestructiveImpact(point,structure!=null&&structure.IsTrunk);
            return true;
        }
        public static bool FragmentHasMinimumSize(Mesh mesh,Matrix4x4 localToWorld)
        {
            if(mesh==null||mesh.vertexCount==0) return false;
            var vertices=mesh.vertices;
            var bounds=new Bounds(localToWorld.MultiplyPoint3x4(vertices[0]),Vector3.zero);
            for(int i=1;i<vertices.Length;i++)
                bounds.Encapsulate(localToWorld.MultiplyPoint3x4(vertices[i]));
            Vector3 size=bounds.size;
            // The former 0.1 m gameplay floor is gone. A tiny but genuinely
            // three-dimensional mesh still needs nonzero axes for physics.
            return float.IsFinite(size.x)&&float.IsFinite(size.y)&&float.IsFinite(size.z)&&
                size.x>0.000001f&&size.y>0.000001f&&size.z>0.000001f;
        }
        static bool WithinOriginalBounds(Mesh mesh,Bounds original)
        {
            if(mesh==null) return false;
            const float tolerance=0.00002f;
            Bounds current=mesh.bounds;
            return current.min.x>=original.min.x-tolerance&&current.max.x<=original.max.x+tolerance&&
                current.min.y>=original.min.y-tolerance&&current.max.y<=original.max.y+tolerance&&
                current.min.z>=original.min.z-tolerance&&current.max.z<=original.max.z+tolerance;
        }
        static bool SeparateAndClose(Mesh mesh,List<Mesh> output)
        {
            if(mesh==null) return false;
            var components=DestructionMesh.SeparateComponents(mesh);
            bool valid=components.Count>0;
            foreach(var component in components)
            {
                Mesh closed=DestructionMesh.CloseOpenBoundaries(component);
                if(closed==null) { valid=false; Release(component); continue; }
                if(closed!=component) Release(component);
                output.Add(closed);
            }
            if(components.Count!=1||components[0]!=mesh) Release(mesh);
            return valid;
        }
        static float WorldMinY(Mesh mesh,Matrix4x4 localToWorld)
        {
            float minimum=float.PositiveInfinity;
            foreach(var vertex in mesh.vertices)
                minimum=Mathf.Min(minimum,localToWorld.MultiplyPoint3x4(vertex).y);
            return minimum;
        }
        static bool LocalChipsAreValid(List<Mesh> chips,Mesh remainder,Matrix4x4 localToWorld)
        {
            if(chips.Count==0||remainder==null||
                chips.Exists(chip=>!DestructionMesh.IsClosedConnected(chip)||
                    !FragmentHasMinimumSize(chip,localToWorld))) return false;
            var components=DestructionMesh.SeparateComponents(remainder);
            int largest=0;
            for(int i=1;i<components.Count;i++)
                if(Volume(components[i])>Volume(components[largest])) largest=i;
            bool valid=components.Count>0;
            for(int i=0;i<components.Count;i++)
            {
                valid&=DestructionMesh.IsClosedConnected(components[i])&&
                    (i==largest||FragmentHasMinimumSize(components[i],localToWorld));
                if(components[i]!=remainder) Release(components[i]);
            }
            return valid;
        }
        // Hardness uses the material catalog's 0–10 scale. Apply the bonus
        // after the base depth cap, and never clamp travel to the center.
        public static float ShatterOffsetMultiplier(float hardness) =>
            1f+0.7f*(1f-Mathf.Clamp01(hardness/10f));

        public static Vector3 ShatterDividingPoint(Vector3 contact,Vector3 center,
            float baseOffset,float hardness) =>
            contact+(center-contact).normalized*baseOffset*ShatterOffsetMultiplier(hardness);

        static List<Mesh> RockPartialShatterChips(Mesh source,Vector3 point,float radius,
            out Mesh remainder,int facesPerChip,int fragmentBudget,float minimumDepth,
            float hardness)
        {
            // Measure this hit's usual chip count first. A fixed maximum does
            // nothing for Rock hits that never reach the global cap.
            var original=ChipWithinFragmentBudget(source,point,radius,out remainder,
                facesPerChip,fragmentBudget,minimumDepth,hardness);
            if(original.Count<=2) return original;
            int target=Mathf.Max(2,Mathf.RoundToInt(original.Count*0.7f));
            int groupedFaces=Mathf.Max(facesPerChip+2,
                Mathf.CeilToInt(facesPerChip/0.7f));
            var reduced=ChipWithinFragmentBudget(source,point,radius,out Mesh reducedRemainder,
                groupedFaces,target,minimumDepth,hardness);
            if(reduced.Count==0)
            {
                // An irregular surface may need larger face groups.
                reduced=ChipWithinFragmentBudget(source,point,radius,out reducedRemainder,
                    groupedFaces+2,target,minimumDepth,hardness);
            }
            if(reduced.Count==0) return original;
            foreach(var chip in original) Release(chip);
            Release(remainder);
            remainder=reducedRemainder;
            return reduced;
        }

        public static List<Mesh> ChipWithinFragmentBudget(Mesh source,Vector3 point,float radius,
            out Mesh remainder,int maxFacesPerChip,int maxFragments,float minimumDepth=0f,
            float hardness=10f,float minimumVolume=0f)
        {
            using var validationScope=DestructionMesh.BeginValidationScope();
            for(int attempt=0;attempt<10;attempt++)
            {
                var chips=DestructionMesh.Chip(source,point,radius,
                    Mathf.Min(Mathf.Max(radius*0.4f,minimumDepth),
                        source.bounds.extents.magnitude*0.3f)*ShatterOffsetMultiplier(hardness),
                    out remainder,maxFacesPerChip);
                bool tooSmall=chips.Exists(chip=>Volume(chip)+0.0000001f<minimumVolume);
                if(chips.Count>0&&chips.Count<=maxFragments&&!tooSmall&&
                    chips.TrueForAll(DestructionMesh.IsClosedConnected)&&ClosedComponents(remainder)) return chips;
                foreach(var chip in chips) Release(chip);
                Release(remainder);
                radius*=tooSmall?1.25f:0.8f;
            }
            remainder=null;
            return new List<Mesh>();
        }
        static bool ClosedComponents(Mesh mesh)
        {
            if(mesh==null) return false;
            var components=DestructionMesh.SeparateComponents(mesh);
            bool closed=components.Count>0;
            foreach(var component in components)
            {
                closed&=DestructionMesh.IsClosedConnected(component);
                if(component!=mesh) Release(component);
            }
            return closed;
        }
        static bool TryFractureSplit(Mesh source,Matrix4x4 localToWorld,
            bool requireSourceWidth,
            out Mesh first,out Mesh second,out Vector3 chosenNormal,Vector3? dividingPoint=null,
            float minimumWorldVolume=0f)
        {
            // A source narrower than two minimum-width pieces cannot fracture
            // into two physically usable solids, even with a diagonal plane.
            Vector3 axisScale=new(localToWorld.GetColumn(0).magnitude,
                localToWorld.GetColumn(1).magnitude,localToWorld.GetColumn(2).magnitude);
            Vector3 sourceSize=Vector3.Scale(source.bounds.size,axisScale);
            if(requireSourceWidth&&(sourceSize.x+0.00001f<2f*MinimumFragmentAxisSize||
                sourceSize.y+0.00001f<2f*MinimumFragmentAxisSize||
                sourceSize.z+0.00001f<2f*MinimumFragmentAxisSize))
            { first=second=null; chosenNormal=Vector3.zero; return false; }
            for(int attempt=0;attempt<48;attempt++)
            {
                Vector3 normal=UnityEngine.Random.onUnitSphere;
                if(!DestructionMesh.Split(source,new Plane(normal,dividingPoint??source.bounds.center),
                    out first,out second))
                {
                    if(first!=null) Release(first);
                    if(second!=null) Release(second);
                    continue;
                }
                if(ClosedAfterSeparation(first,localToWorld)&&
                    Volume(first)*Mathf.Abs(localToWorld.determinant)+0.0000001f>=minimumWorldVolume&&
                    Volume(second)*Mathf.Abs(localToWorld.determinant)+0.0000001f>=minimumWorldVolume&&
                    ClosedAfterSeparation(second,localToWorld)&&
                    FragmentHasMinimumSize(first,localToWorld)&&
                    FragmentHasMinimumSize(second,localToWorld))
                { chosenNormal=normal; return true; }
                Release(first); Release(second);
            }
            first=second=null; chosenNormal=Vector3.zero;
            return false;
        }
        static bool ClosedAfterSeparation(Mesh mesh,Matrix4x4 localToWorld)
        {
            var components=DestructionMesh.SeparateComponents(mesh);
            bool closed=components.Count>0;
            foreach(var part in components)
            {
                var repaired=DestructionMesh.CloseOpenBoundaries(part);
                closed&=repaired!=null&&FragmentHasMinimumSize(repaired,localToWorld)&&
                    PreparedSolidIsValid(repaired,localToWorld);
                if(repaired!=null&&repaired!=part) Release(repaired);
                if(part!=mesh) Release(part);
            }
            return closed;
        }
        static void SplitPieces(List<Mesh> pieces,Vector3 normal,Matrix4x4 localToWorld,
            float minimumWorldVolume=0f,int maximumPieces=int.MaxValue)
        {
            int count=pieces.Count;
            for(int i=count-1;i>=0;i--)
            {
                if(pieces.Count>=maximumPieces) break;
                if(DestructionMesh.Split(pieces[i],new Plane(normal,pieces[i].bounds.center),out var a,out var b))
                {
                    if(ClosedAfterSeparation(a,localToWorld)&&
                        Volume(a)*Mathf.Abs(localToWorld.determinant)+0.0000001f>=minimumWorldVolume&&
                        Volume(b)*Mathf.Abs(localToWorld.determinant)+0.0000001f>=minimumWorldVolume&&
                        ClosedAfterSeparation(b,localToWorld)&&
                        FragmentHasMinimumSize(a,localToWorld)&&
                        FragmentHasMinimumSize(b,localToWorld))
                    { Release(pieces[i]); pieces.RemoveAt(i); pieces.Add(a); pieces.Add(b); }
                    else { Release(a); Release(b); }
                }
            }
        }
        static float Volume(Mesh mesh)
        {
            return Mathf.Max(0.000001f,(float)DestructionMesh.SolidVolume(mesh));
        }
        Mesh Snapshot(out Material[] materials,out MaterialPropertyBlock[] appearance)
        {
            var combines=new List<CombineInstance>(); var slots=new List<Material>();
            var blocks=new List<MaterialPropertyBlock>(); var baked=new List<Mesh>();
            foreach(var renderer in GetComponentsInChildren<Renderer>())
            {
                if(!renderer.enabled||!Owns(renderer)) continue;
                Mesh mesh=null;
                if(renderer is SkinnedMeshRenderer skin) { mesh=new Mesh(); skin.BakeMesh(mesh); baked.Add(mesh); }
                else if(renderer.TryGetComponent<MeshFilter>(out var filter)) mesh=filter.sharedMesh;
                if(mesh==null||!mesh.isReadable) continue;
                ColorOnlyTextureQuality.GetOriginalAppearance(renderer,out var mats,out var overall);
                for(int s=0;s<mesh.subMeshCount;s++)
                {
                    // CombineMeshes omits empty submeshes. Keep the material and
                    // property arrays aligned with the geometry it actually emits.
                    if(mesh.GetIndexCount(s)==0) continue;
                    combines.Add(new CombineInstance { mesh=mesh, subMeshIndex=s,
                        transform=transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix });
                    slots.Add(mats.Length>0?mats[Mathf.Min(s,mats.Length-1)]:null);
                    var perSlot=new MaterialPropertyBlock();
                    if(renderer.HasPropertyBlock()) renderer.GetPropertyBlock(perSlot,s);
                    blocks.Add(!perSlot.isEmpty?perSlot:!overall.isEmpty?overall:null);
                }
            }
            materials=slots.ToArray(); appearance=blocks.ToArray();
            if(combines.Count==0) { foreach(var mesh in baked) Release(mesh); return null; }
            var combined=new Mesh { indexFormat=IndexFormat.UInt32 };
            combined.CombineMeshes(combines.ToArray(),false,true);
            foreach(var mesh in baked) Release(mesh);
            return combined;
        }
        internal void NotifyGeometryChanged()
        {
            HasBeenModified=true;
            nextImpact=Time.time+0.25f;
            GeometryChanged?.Invoke();
        }
        void HideGeometry()
        {
            foreach(var renderer in GetComponentsInChildren<Renderer>())
                if(Owns(renderer)) renderer.enabled=false;
            foreach(var collider in GetComponentsInChildren<Collider>())
                if(Owns(collider)) collider.enabled=false;
        }
        static void ApplyAppearance(MeshRenderer renderer,Material[] materials,
            MaterialPropertyBlock[] appearance)
        {
            renderer.sharedMaterials=materials;
            for(int i=0;i<appearance.Length;i++)
                if(appearance[i]!=null) renderer.SetPropertyBlock(appearance[i],i);
            ColorOnlyTextureQuality.ApplyRendererQuality(renderer);
        }
        void ReplaceGeometry(Mesh mesh,Material[] materials,MaterialPropertyBlock[] appearance,Rigidbody body)
        {
            HideGeometry();
            // Pickup collider preparation must not restore the original uncut shell.
            foreach(var filter in GetComponentsInChildren<MeshFilter>())
                if(Owns(filter)) filter.sharedMesh=null;
            foreach(var oldCollider in GetComponentsInChildren<MeshCollider>())
                if(Owns(oldCollider)) oldCollider.sharedMesh=null;
            var geometry=new GameObject("Shattered remainder"); geometry.transform.SetParent(transform,false);
            geometry.AddComponent<MeshFilter>().sharedMesh=mesh;
            ApplyAppearance(geometry.AddComponent<MeshRenderer>(),materials,appearance);
            var collider=AddFragmentCollider(geometry,mesh,GetComponentInParent<Rigidbody>()!=null);
            GetComponent<DestructionFragment>()?.TrackCollider(collider);
            geometry.AddComponent<PhysicalDestructionCollisionRelay>().Owner=this;
            GetComponent<TreeStructuralPart>()?.RefreshFallingGeometry();
            GetComponent<PickableObject>()?.RefreshDetectionGeometry();
            if(TryGetComponent<PickableObject>(out var held)&&held.IsHeld)
                held.RefreshHeldCollisionGeometry();
            if(ownedMesh!=null) Release(ownedMesh); ownedMesh=mesh;
        }
        internal static bool PrepareFragmentMesh(Mesh mesh,Matrix4x4 localToWorld,out Vector3 worldCenter)
        {
            // Use exactly the same seam groups as the closed-solid validator.
            Vector3[] vertices=DestructionMesh.CanonicalVertices(mesh);
            DestructionMesh.InvalidateValidation(mesh);
            Vector3 localCenter=mesh.bounds.center;
            for(int i=0;i<vertices.Length;i++)
                vertices[i]=localToWorld.MultiplyVector(vertices[i]-localCenter);
            mesh.vertices=vertices;
            mesh.RecalculateBounds();
            Vector3 centerOffset=DestructionMesh.VolumeCentroid(mesh);
            worldCenter=localToWorld.MultiplyPoint3x4(localCenter)+centerOffset;
            for(int i=0;i<vertices.Length;i++) vertices[i]-=centerOffset;
            mesh.vertices=vertices;
            if(localToWorld.determinant<0f)
            {
                for(int slot=0;slot<mesh.subMeshCount;slot++)
                {
                    var indices=mesh.GetTriangles(slot);
                    for(int i=0;i<indices.Length;i+=3) (indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);
                    mesh.SetTriangles(indices,slot);
                }
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            DestructionMesh.InvalidateValidation(mesh);
            return DestructionMesh.IsClosedConnected(mesh);
        }
        static bool PreparedSolidIsValid(Mesh mesh,Matrix4x4 localToWorld)
        {
            var copy=UnityEngine.Object.Instantiate(mesh);
            bool valid=PrepareFragmentMesh(copy,localToWorld,out _);
            Release(copy);
            return valid;
        }
        FragmentSpawnRequest PrepareSpawn(Mesh mesh,Vector3 worldCenter,Material[] materials,
            MaterialPropertyBlock[] appearance,Rigidbody source,
            float mass,Vector3 sourceCenter,Vector3 collisionNormal,float relativeSpeed,
            float impulseMultiplier)
        {
            var request=new FragmentSpawnRequest
            {
                Mesh=mesh,WorldCenter=worldCenter,Materials=materials,Appearance=appearance,
                Mass=Mathf.Max(0.001f,mass),Name=name+" fragment",MaterialName=MaterialName,
                SolidVolume=DestructionMesh.SolidVolume(mesh),
                Hardness=Hardness,Resilience=Resilience,Soft=Soft,
                Labels=new List<string>(Labels),MinimumImpactSpeed=MinimumImpactSpeed,
                SimilarResilienceTolerance=SimilarResilienceTolerance,
                FragmentImpulsePerSpeed=FragmentImpulsePerSpeed,
                FragmentationCount=fragmentationCount,SizeGrowth=fragmentSizeGrowth,
                TreePart=GetComponent<TreeStructuralPart>()!=null,
                TrunkPart=GetComponent<TreeStructuralPart>()?.IsTrunk==true,
                BranchPart=GetComponent<TreeStructuralPart>()?.IsBranch==true||
                    GetComponent<DestructionFragment>()?.IsBranchFragment==true
            };
            if(source!=null)
            {
                var held=GetComponent<PickableObject>();
                request.LinearVelocity=held!=null&&held.IsHeld?
                    held.HeldPointVelocity(worldCenter):source.GetPointVelocity(worldCenter);
                request.AngularVelocity=held!=null&&held.IsHeld?
                    held.HeldAngularVelocity:source.angularVelocity;
            }
            request.Impulse=FragmentImpulse(sourceCenter,worldCenter,collisionNormal,
                relativeSpeed,request.Mass,FragmentImpulsePerSpeed*impulseMultiplier);
            return request;
        }

        static void SpawnOrQueue(List<FragmentSpawnRequest> requests,
            List<PhysicalDestructible> spawned)
        {
            requests.Sort((a,b)=>b.SolidVolume.CompareTo(a.SolidVolume));
            foreach(var request in requests)
            {
                if(!Application.isPlaying)
                {
                    spawned.Add(SpawnPrepared(request));
                }
                else if(request.TemporaryDebris)
                {
                    // A delayed dust burst is no longer useful. Keep only debris
                    // that fits the current spawn and live-object budgets.
                    if(pendingFragments.Count==0&&TemporaryShatterDebris.CanSpawn&&
                        CanSpawnThisFrame())
                    {
                        spawned.Add(SpawnPrepared(request));
                        NoteFragmentSpawn();
                    }
                    else Release(request.Mesh);
                }
                else if(pendingFragments.Count==0&&CanSpawnThisFrame())
                {
                    spawned.Add(SpawnPrepared(request));
                    NoteFragmentSpawn();
                }
                else QueueFragment(request);
            }
        }

        static Collider AddFragmentCollider(GameObject target,Mesh mesh,bool dynamic)
        {
            int triangleCount=0;
            for(int slot=0;slot<mesh.subMeshCount;slot++)
                triangleCount+=(int)mesh.GetIndexCount(slot)/3;
            if(dynamic&&triangleCount>MaximumConvexFragmentTriangles)
            {
                var box=target.AddComponent<BoxCollider>();
                box.center=mesh.bounds.center;
                box.size=mesh.bounds.size;
                return box;
            }
            var collider=target.AddComponent<MeshCollider>();
            collider.sharedMesh=mesh;
            collider.convex=dynamic;
            return collider;
        }

        static PhysicalDestructible SpawnPrepared(FragmentSpawnRequest request,bool deferred=false)
        {
            var piece=new GameObject(request.Name);
            piece.transform.position=request.WorldCenter;
            piece.AddComponent<MeshFilter>().sharedMesh=request.Mesh;
            ApplyAppearance(piece.AddComponent<MeshRenderer>(),request.Materials,request.Appearance);
            AddFragmentCollider(piece,request.Mesh,true);
            var body=piece.AddComponent<Rigidbody>(); body.mass=request.Mass;
            body.centerOfMass=Vector3.zero;
            body.ResetInertiaTensor();
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity=request.LinearVelocity;
            body.angularVelocity=request.AngularVelocity;
            if(Application.isPlaying) body.AddForce(request.Impulse,ForceMode.Impulse);
            var properties=piece.AddComponent<PhysicalDestructible>();
            properties.fragmentationCount=request.FragmentationCount;
            properties.fragmentSizeGrowth=request.SizeGrowth;
            properties.MaterialName=request.MaterialName;
            properties.Hardness=request.Hardness;
            properties.Resilience=request.Resilience;
            properties.Soft=request.Soft;
            properties.Labels=request.Labels;
            properties.MinimumImpactSpeed=request.MinimumImpactSpeed;
            properties.SimilarResilienceTolerance=request.SimilarResilienceTolerance;
            properties.FragmentImpulsePerSpeed=request.FragmentImpulsePerSpeed;
            properties.nextImpact=Time.time+0.25f;
            properties.ownedMesh=request.Mesh;
            properties.RefreshMass();
            if(!request.TemporaryDebris)
            {
                var pickup=piece.AddComponent<PickableObject>();
                pickup.DisplayName=request.MaterialName+" fragment";
            }
            piece.AddComponent<DestructionFragment>().IsBranchFragment=request.BranchPart;
            if(request.TemporaryDebris) piece.AddComponent<TemporaryShatterDebris>();
            if(deferred&&request.TreePart)
                piece.AddComponent<TreeStructuralPart>().SetTrunk(request.TrunkPart);
            return properties;
        }
        static void Release(UnityEngine.Object value) { if(Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        void OnDestroy() { if(ownedMesh!=null) Release(ownedMesh); }
    }
    // Static remainder colliders live on a new child; Unity sends their contact
    // callbacks to that child. Forward them to the original material owner.
    public sealed class PhysicalDestructionCollisionRelay : MonoBehaviour
    {
        public PhysicalDestructible Owner;
        void OnCollisionEnter(Collision collision) { if(Owner!=null) Owner.ProcessCollision(collision); }
        void OnCollisionStay(Collision collision) { if(Owner!=null) Owner.ProcessCollision(collision); }
    }

    // Explicit provenance keeps cleanup independent of object names or materials.
    public sealed class DestructionFragment : MonoBehaviour
    {
        public bool IsBranchFragment { get; set; }
        static readonly HashSet<Collider> activeColliders=new();
        readonly List<Collider> registeredColliders=new();
        internal static IEnumerable<Collider> ActiveColliders => activeColliders;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetActiveColliders() => activeColliders.Clear();
        void Awake()
        {
            foreach(var collider in GetComponentsInChildren<Collider>(true))
                TrackCollider(collider);
        }
        internal void TrackCollider(Collider collider)
        {
            if(collider==null||!activeColliders.Add(collider)) return;
            registeredColliders.Add(collider);
            TemporaryShatterDebris.IgnoreForNewFragment(collider);
        }
        void OnDestroy()
        {
            foreach(var collider in registeredColliders) activeColliders.Remove(collider);
        }
        public static int DeleteAllExceptHeld()
        {
            int deleted=0;
            foreach(var fragment in FindObjectsByType<DestructionFragment>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(fragment==null) continue;
                bool held=false;
                foreach(var item in fragment.GetComponentsInParent<PickableObject>(true)) held|=item.IsHeld;
                foreach(var item in fragment.GetComponentsInChildren<PickableObject>(true)) held|=item.IsHeld;
                if(held) continue;
                fragment.gameObject.SetActive(false);
                if(Application.isPlaying) Destroy(fragment.gameObject);
                else DestroyImmediate(fragment.gameObject);
                deleted++;
            }
            return deleted;
        }
    }

    // Temporary debris from an undersized fragment can hit the world, but never
    // starts another fragment-on-fragment collision cascade.
    public sealed class TemporaryShatterDebris : MonoBehaviour
    {
        static readonly HashSet<Collider> activeColliders=new();
        const int MaximumLiveDebris=32;
        static int liveDebris;
        internal static bool CanSpawn => liveDebris<MaximumLiveDebris;
        static readonly int BaseColorId=Shader.PropertyToID("_BaseColor");
        static readonly int ColorId=Shader.PropertyToID("_Color");
        static readonly int FadeAlphaId=Shader.PropertyToID("_FadeAlpha");
        Collider debrisCollider;
        Material[] fadeMaterials;
        float spawnedAt;
        Vector3 originalScale;
        public const float DebrisLifetimeSeconds=5f;
        public float LifetimeSeconds { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetActiveColliders()
        { activeColliders.Clear(); liveDebris=0; }

        void Awake()
        {
            liveDebris++;
            debrisCollider=GetComponent<Collider>();
            if(debrisCollider!=null)
            {
                foreach(var collider in DestructionFragment.ActiveColliders)
                    IgnorePair(debrisCollider,collider);
                activeColliders.Add(debrisCollider);
            }
            LifetimeSeconds=DebrisLifetimeSeconds;
            spawnedAt=Time.unscaledTime;
            originalScale=transform.localScale;
            PrepareFadeMaterials();
            // Keep the deletion scheduled even if streaming disables Update.
            if(Application.isPlaying) Destroy(gameObject,LifetimeSeconds);
        }

        void PrepareFadeMaterials()
        {
            var renderer=GetComponent<MeshRenderer>();
            Shader shader=Resources.Load<Shader>("FadingFragment")??
                Shader.Find("Wyvernland/Fading Fragment");
            if(renderer==null||shader==null) return;
            var original=renderer.sharedMaterials;
            fadeMaterials=new Material[original.Length];
            var block=new MaterialPropertyBlock();
            var overall=new MaterialPropertyBlock();
            renderer.GetPropertyBlock(overall);
            for(int i=0;i<original.Length;i++)
            {
                Material source=original[i];
                Color color=source!=null&&source.HasProperty(BaseColorId)?
                    source.GetColor(BaseColorId):
                    source!=null&&source.HasProperty(ColorId)?source.GetColor(ColorId):Color.white;
                renderer.GetPropertyBlock(block,i);
                Color overrideColor=block.GetColor(BaseColorId);
                if(HasTint(overrideColor)) color=overrideColor;
                else
                {
                    overrideColor=block.GetColor(ColorId);
                    if(HasTint(overrideColor)) color=overrideColor;
                    else
                    {
                        overrideColor=overall.GetColor(BaseColorId);
                        if(!HasTint(overrideColor)) overrideColor=overall.GetColor(ColorId);
                        if(HasTint(overrideColor)) color=overrideColor;
                    }
                }
                var fade=new Material(shader) { name="Temporary fragment fade" };
                fade.SetColor(BaseColorId,color);
                fade.SetFloat(FadeAlphaId,1f);
                fadeMaterials[i]=fade;
                renderer.SetPropertyBlock(null,i);
                block.Clear();
            }
            renderer.sharedMaterials=fadeMaterials;
        }

        static bool HasTint(Color color) =>
            color.maxColorComponent>0f||color.a>0f;

        void Update()
        {
            float elapsed=Time.unscaledTime-spawnedAt;
            if(elapsed>=LifetimeSeconds)
            {
                GetComponent<TreeStructuralPart>()?.ReleaseChildren();
                Destroy(gameObject);
                return;
            }
            float fadeStart=LifetimeSeconds*0.35f;
            float progress=Mathf.Clamp01((elapsed-fadeStart)/
                Mathf.Max(0.0001f,LifetimeSeconds-fadeStart));
            float opacity=1f-Mathf.SmoothStep(0f,1f,progress);
            transform.localScale=originalScale*Mathf.Max(0.05f,opacity);
            if(fadeMaterials==null) return;
            foreach(var material in fadeMaterials)
                if(material!=null) material.SetFloat(FadeAlphaId,opacity);
        }

        internal static void IgnoreForNewFragment(Collider collider)
        {
            foreach(var debris in activeColliders) IgnorePair(debris,collider);
        }

        static void IgnorePair(Collider a,Collider b)
        {
            if(a!=null&&b!=null&&a!=b&&!a.isTrigger&&!b.isTrigger)
                Physics.IgnoreCollision(a,b,true);
        }

        void OnDestroy()
        {
            liveDebris=Mathf.Max(0,liveDebris-1);
            GetComponent<TreeStructuralPart>()?.ReleaseChildren();
            activeColliders.Remove(debrisCollider);
            if(fadeMaterials==null) return;
            foreach(var material in fadeMaterials)
                if(material!=null)
                {
                    if(Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
        }
    }

    // Discover streamed/generated objects, with collision-time registration closing
    // the gap between spawning a collider and the next discovery pass.
    public sealed class PhysicalDestructionWorld : MonoBehaviour
    {
        float nextScan;
        void Update()
        {
            PhysicalDestructible.DrainPendingFragments();
            if(Time.time<nextScan) return; nextScan=Time.time+1f;
            foreach(var collider in FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                PhysicalDestructible.Resolve(collider);
            foreach(var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if(!renderer.enabled) continue;
                if(renderer.GetComponent<TreeImpactMarker>()!=null) continue;
                if(renderer.GetComponentInParent<FirstPersonController>(true)!=null) continue;
                // A visual assembly containing registered destructible children
                // must not become a second destruction target over those meshes.
                if(renderer.GetComponent<Collider>()==null&&
                    renderer.GetComponentsInChildren<PhysicalDestructible>(true).Length>0) continue;
                // Rock-pile roots are likewise visual-only; each child rock
                // owns the collider and the destructible component.
                if(renderer.TryGetComponent<GeneratedGroundFeature>(out _)&&
                    renderer.GetComponent<Collider>()==null) continue;
                if(renderer.GetComponentInParent<PhysicalDestructible>()!=null) continue;
                PhysicalDestructible.Ensure(renderer.gameObject);
            }
        }
        void OnDestroy() => PhysicalDestructible.ClearPendingFragments();
    }
}

