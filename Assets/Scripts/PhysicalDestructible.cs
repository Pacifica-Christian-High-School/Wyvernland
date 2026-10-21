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
        // Unity convex MeshColliders support at most 255 faces.
        public const int MaximumConvexFragmentTriangles=255;
        public const float MinimumFragmentAxisSize=0f;
        public const float MinimumFragmentationVolume=0.002f;
        public const double MaximumCompleteDestructionVolume=1d;
        public const double MaximumPartialShatterFragmentVolume=0.1d;
        public const double MinimumImpactorVolumeForFullTrunkDamage=0.06d;
        public const float MaximumSimilarDestructionChance=0.3f;
        public const float MinimumSimilarDestructionChance=0.01f;
        public const float WoodFragmentImpulseScale=0.5f;
        public const float WoodFragmentUpwardImpulseScale=0.25f;
        public const float WoodFragmentMaximumUpwardSpeed=2.5f;
        public const float SimilarPropertyRange=2f;
        public const float CollisionHardnessRetention=0.97f;
        public static readonly PhysicalDestructEffect[] Effects={PhysicalDestructEffect.Fracture,
            PhysicalDestructEffect.Shatter,PhysicalDestructEffect.Depression,PhysicalDestructEffect.Slicing,PhysicalDestructEffect.Abrasion};
        public bool HasBeenDestroyed { get; private set; }
        public bool HasBeenModified { get; private set; }
        public PhysicalDestructEffect LastEffect { get; private set; }
        public static event Action<PhysicalDestructEffect> DestructionOccurred;
        sealed class FragmentSpawnRequest
        {
            public Mesh Mesh;
            public Vector3 WorldCenter,LinearVelocity,AngularVelocity,Impulse;
            public Quaternion WorldRotation=Quaternion.identity;
            public Material[] Materials;
            public MaterialPropertyBlock[] Appearance;
            public float Mass,Hardness,Resilience,MinimumImpactSpeed,
                SimilarResilienceTolerance,FragmentImpulsePerSpeed,SizeGrowth;
            public double SolidVolume;
            public int FragmentationCount;
            public bool Soft,BranchPart,TemporaryDebris,BodyDebris;
            public string Name,MaterialName;
            public List<string> Labels;
        }
        public event Action GeometryChanged;
        public event Action<PhysicalDestructible,IReadOnlyList<PhysicalDestructible>> Fragmented;
        public event Action<float> HardnessChanged;
        float nextImpact;
        float nextGeometryAttempt;
        int failedGeometryAttempts;
        Vector3 geometryAttemptLocalPoint;
        bool GeometryAttemptReady(Vector3 point) => Time.time>=nextGeometryAttempt||
            failedGeometryAttempts>0&&transform.TransformVector(
                transform.InverseTransformPoint(point)-geometryAttemptLocalPoint).sqrMagnitude>0.01f;
        double lastCollisionStep=double.NegativeInfinity;
        readonly HashSet<int> attemptedCollisionPartners=new();
        double lastHeldStrikeStep=double.NegativeInfinity;
        readonly Dictionary<int,bool> heldStrikeOutcomes=new();
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
        public static float SimilarDestructionChance(float hardness,float resilience,
            float otherHardness,float otherResilience)
        {
            if(hardness<7f||resilience<7f||otherHardness<7f||otherResilience<7f)
                return MaximumSimilarDestructionChance;
            float strength=(Mathf.Clamp(hardness,7,10)+Mathf.Clamp(resilience,7,10)+
                Mathf.Clamp(otherHardness,7,10)+Mathf.Clamp(otherResilience,7,10)-28f)/12f;
            return Mathf.Max(MinimumSimilarDestructionChance,
                Mathf.Lerp(MaximumSimilarDestructionChance,MinimumSimilarDestructionChance,strength));
        }
        public static bool IsLowStrengthPair(float hardness,float resilience,
            float otherHardness,float otherResilience) =>
            hardness<4f&&resilience<4f&&otherHardness<4f&&otherResilience<4f;
        public static float CollisionDestructionChance(float hardness,float resilience,
            float otherHardness,float otherResilience,float size,float otherSize)
        {
            return Mathf.Abs(hardness-otherHardness)<=SimilarPropertyRange?
                SimilarDestructionChance(hardness,resilience,otherHardness,otherResilience):1f;
        }
        public static float CollisionWearChance(float resilience) => 0.8f/(1f+Mathf.Max(0f,resilience));
        public static float MetalDestructionChanceScale(string material,float resilience) =>
            WorldConfig.IsMetalMaterial(material)?Mathf.Clamp01((8f-resilience)/4f):1f;
        public static float CollisionHardnessLossFraction(float resilience) =>
            (1f-CollisionHardnessRetention)*Mathf.Pow(0.95f,Mathf.Max(0f,resilience-6f));
        public static float HardnessAfterCollision(float hardness,float resilience,float roll) =>
            roll>=CollisionWearChance(resilience)?hardness:
                Mathf.Max(0f,hardness)*(1f-CollisionHardnessLossFraction(resilience));
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
            // Original calibration used Stone at Hardness 5.5:
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
        public static void LimitWoodFragmentFlight(ref Vector3 velocity,ref Vector3 impulse,float mass)
        {
            velocity.y=Mathf.Min(velocity.y,WoodFragmentMaximumUpwardSpeed);
            impulse*=WoodFragmentImpulseScale;
            if(impulse.y>0f)
            {
                impulse.y*=WoodFragmentUpwardImpulseScale;
                impulse.y=Mathf.Min(impulse.y,
                    Mathf.Max(0f,mass)*(WoodFragmentMaximumUpwardSpeed-velocity.y));
            }
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
            if(material=="Clay"||material=="Mud"||material=="Soil"||
                material=="Resin"||material=="Black Resin")
            { Hardness=0.5f; Resilience=8f; Soft=true; if(!Labels.Contains("Soft")) Labels.Add("Soft"); }
        }
        float StoneHardness()
        {
            // A repeatable sample gives an object consistent hardness when
            // generated again without advancing Unity's global random sequence.
            uint hash=StableMaterialHash();
            float first=Mix(hash^0x9e3779b9u)/4294967296f;
            float second=Mix(hash^0x85ebca6bu)/4294967296f;
            bool rock=HasRockName();
            // Both kinds can span 4–7. Rocks usually draw from the lower half;
            // stones usually draw from the upper half.
            return 2f+3f*(rock?Mathf.Min(first,second):Mathf.Max(first,second));
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
            // Ropes own a chain of bodies and analytic material volume. A rigid
            // mesh destruction target would replace that chain with a solid hull.
            if(target.GetComponentInParent<RopeObject>(true)!=null) return null;
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
            if(names.IndexOf("Black Resin",StringComparison.OrdinalIgnoreCase)>=0)
            { result.ApplyMaterial("Black Resin"); return result; }
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
                    if(source.GetComponentInParent<TreeImpactMarker>(true)!=null) return false;
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
            if(GetComponent<DestructionFragment>()!=null||GetComponent<BodyFragment>()!=null||
                GetComponent<TreeStructuralPart>() is { CanDrag:true }) FragmentMassCenter.Align(GetComponent<Rigidbody>());
            GetComponent<TreeStructuralPart>()?.RefreshTrunkCenterOfMass();
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
            if(measuredBody!=null) MovingImpactMotion.Ensure(measuredBody);
            // Unity requires a positive Rigidbody mass. Zero-thickness render
            // surfaces retain zero material mass; only the physics solver uses
            // an epsilon if a Rigidbody is attached to one of those surfaces.
            if(hasMaterialMass&&measuredBody!=null&&float.IsFinite(massKg)) measuredBody.mass=Mathf.Max(0.0000001f,massKg);
        }

        void Start()
        {
            RefreshMassIfNeeded();
            DynamicObjectSurfaceGuard.Ensure(GetComponent<Rigidbody>());
        }
        public static PhysicalDestructible Resolve(Collider collider)
        {
            if(collider==null||collider.isTrigger) return null;
            if(collider.GetComponentInParent<RopeObject>()!=null) return null;
            if(IsPlayerObject(collider.gameObject)) return null;
            var existing=collider.GetComponentInParent<PhysicalDestructible>();
            // A tree section can be parented under a moving section's body.
            // Its own collider still belongs to its own destructible geometry.
            if(existing!=null) return existing;
            if(collider.attachedRigidbody!=null) return Ensure(collider.attachedRigidbody.gameObject);
            return existing!=null?existing:Ensure(collider.gameObject);
        }
        bool Owns(Component component) =>
            component!=null&&component.GetComponentInParent<TreeImpactMarker>(true)==null&&
            component.GetComponentInParent<PhysicalDestructible>(true)==this;
        static bool IsPlayerObject(GameObject target)
        {
            if(target==null) return false;
            if(target.GetComponentInParent<PlayerCorpse>()!=null) return true;
            // A held item is parented to the player's hand, but remains its own
            // physical object and collision damage source.
            foreach(var item in target.GetComponentsInParent<PickableObject>(true))
                if(item.IsHeld) return false;
            return target.GetComponentInParent<FirstPersonController>(true)!=null||target.GetComponentInParent<PlayerRagdoll>(true)!=null;
        }
        void OnCollisionEnter(Collision collision) => ProcessCollision(collision);
        void OnCollisionStay(Collision collision) => ProcessCollision(collision);
        internal void ProcessCollision(Collision collision)
        {
            if(WebWorldSession.IsPhysicsHost&&WebWorldSession.CanSimulate(gameObject)&&collision.collider.GetComponentInParent<RemotePlayerDamage>() is {} victim) {
                var heldSource=GetComponent<PickableObject>();
                var sourceBody=GetComponentInParent<Rigidbody>();
                var motion=sourceBody!=null?sourceBody.GetComponent<MovingImpactMotion>():null;
                foreach(var contact in collision.contacts) {
                    var tag=contact.otherCollider.GetComponent<PlayerHitShape>();
                    Vector3 velocity=heldSource!=null&&heldSource.IsHeld?heldSource.HeldPointVelocity(contact.point):
                        motion!=null?motion.PointVelocity(contact.point):sourceBody!=null?sourceBody.GetPointVelocity(contact.point):Vector3.zero;
                    WebPlayerPresence.QueueImpact(victim,this,tag!=null?tag.Part:PlayerBodyPart.Torso,tag!=null?tag.transform.parent:null,contact.point,-contact.normal,velocity);
                }
            }
            if(collision.contactCount>0) {
                var otherSpark=collision.collider.GetComponentInParent<PhysicalDestructible>();
                if(otherSpark!=null) { var contact=collision.GetContact(0);CollisionSparks.TryEmit(this,otherSpark,contact.point,contact.normal,RelativeCollisionSpeed(collision,otherSpark,contact.point)); }
            }
            if(!WebWorldSession.CanSimulate(gameObject)) return;
            TreeStructuralPart.ProcessTerrainContact(collision);
            // A held strike tries the normal material destruction rules first.
            // A non-destructive held strike can rebound from harder solids
            // above the measured object-speed threshold. Terrain ends it normally.
            PickableObject ourHeld=GetComponentInParent<PickableObject>();
            PickableObject theirHeld=collision.collider!=null?
                collision.collider.GetComponentInParent<PickableObject>():null;
            Vector3 contactPoint=collision.contactCount>0?
                collision.GetContact(0).point:transform.position;
            Rigidbody ourBody=GetComponentInParent<Rigidbody>();
            Rigidbody theirBody=collision.collider!=null?
                collision.collider.attachedRigidbody:null;
            Collider ourCollider=collision.contactCount>0?
                collision.GetContact(0).thisCollider:GetComponent<Collider>();
            if(ourHeld!=null&&ourHeld.TryResolveStrikeImpact(collision.collider,contactPoint,
                    theirBody!=null?theirBody.GetPointVelocity(contactPoint):Vector3.zero)||
                theirHeld!=null&&theirHeld.TryResolveStrikeImpact(ourCollider,contactPoint,
                    ourBody!=null?ourBody.GetPointVelocity(contactPoint):Vector3.zero))
                return;
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
            bool similarHard=IsSimilarHardStrike(other);
            if(!similarHard&&TrySpecialFragmentCollision(other,contact.point,contact.normal,
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
            // One shared roll for both recipients; two independent 1% rolls
            // would almost double the maximum-strength collision probability.
            if(similarHard&&UnityEngine.Random.value>=SimilarDestructionChance(
                ourHardness,ourResilience,theirHardness,theirResilience)) return;
            if(similarHard&&TrySpecialFragmentCollision(other,contact.point,contact.normal,speed)) return;
            var ourEffect=similarHard?PhysicalDestructEffect.Fracture:Classify(ourHardness,ourResilience,IsSoft,
                theirHardness,theirResilience,SimilarResilienceTolerance);
            var theirEffect=similarHard?PhysicalDestructEffect.Fracture:Classify(theirHardness,theirResilience,other.IsSoft,
                ourHardness,ourResilience,other.SimilarResilienceTolerance);
            bool tryOurs=ourCollision&&!HasBeenDestroyed&&Time.time>=nextImpact&&
                GeometryAttemptReady(contact.point)&&
                (ourEffect==PhysicalDestructEffect.Fracture||ourEffect==PhysicalDestructEffect.Shatter)&&
                speed>=InitialImpactSpeed(theirHardness,other.MaterialName);
            bool tryTheirs=theirCollision&&!other.HasBeenDestroyed&&Time.time>=other.nextImpact&&
                other.GeometryAttemptReady(contact.point)&&
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
                    contact.point,contact.normal,speed,other.MaterialName,theirVolume,other.IsTerrain,theirMass,similarHeldStrike:similarHard);
            if(tryTheirs&&other!=null)
                other.ApplyImpact(ourHardness,ourResilience,ourSize,
                    contact.point,-contact.normal,speed,MaterialName,ourVolume,IsTerrain,ourMass,similarHeldStrike:similarHard);
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
            bool ours=ourFragment&&SolidVolumeCubicMetres<MinimumFragmentationVolume&&
                HasElongatedGeometry();
            bool theirs=theirFragment&&other.SolidVolumeCubicMetres<MinimumFragmentationVolume&&
                other.HasElongatedGeometry();
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
            float metalChance=MetalDestructionChanceScale(smaller.MaterialName,smaller.Resilience);
            if(metalChance<1f&&UnityEngine.Random.value>=metalChance) return true;
            smaller.SpecialCompleteShatter(point,smaller==this?normal:-normal,speed);
            return true;
        }

        bool IsUndersizedBranchFragment =>
            GetComponent<DestructionFragment>() is { IsBranchFragment: true }&&
            SolidVolumeCubicMetres<MinimumFragmentationVolume&&HasElongatedGeometry();

        bool HasElongatedGeometry()
        {
            foreach(var filter in GetComponentsInChildren<MeshFilter>())
            {
                if(filter.sharedMesh==null||!Owns(filter)) continue;
                if(IsElongatedMesh(filter.sharedMesh,filter.transform.localToWorldMatrix))
                    return true;
            }
            return false;
        }

        static bool IsElongatedMesh(Mesh mesh,Matrix4x4 localToWorld)
        {
            if(mesh==null) return false;
            Vector3 size=mesh.bounds.size;
            size=Vector3.Scale(size,new Vector3(localToWorld.GetColumn(0).magnitude,
                localToWorld.GetColumn(1).magnitude,localToWorld.GetColumn(2).magnitude));
            return PickableObject.ChooseGrabMode(size)==PickableGrabMode.Stick;
        }

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
        internal bool TryHeldStrikeDestruction(PhysicalDestructible target,
            Vector3 point,Vector3 incoming) => ResolveHeldStrikeDestruction(target,point,incoming);

        internal bool IsSimilarHardStrike(PhysicalDestructible target) =>
            target!=null&&!IsSoft&&!target.IsSoft&&!IsTerrain&&!target.IsTerrain&&
            Mathf.Abs(Hardness-target.Hardness)<=SimilarPropertyRange;

        internal bool CanDestroySimilarHeldStrike(PhysicalDestructible target,float speed) =>
            IsSimilarHardStrike(target)&&(speed>=InitialImpactSpeed(target.Hardness,target.MaterialName)||
                speed>=target.InitialImpactSpeed(Hardness,MaterialName));

        internal bool ResolveHeldStrikeDestruction(PhysicalDestructible target,
            Vector3 point,Vector3 incoming,float similarityRoll=-1f)
        {
            if(target==null||target==this) return false;
            CollisionSparks.TryEmit(this,target,point,-incoming,incoming.magnitude);
            var localGrip=GetComponentInParent<PickableObject>();
            if(localGrip!=null&&localGrip.IsHeld&&!WebWorldSession.HasConfirmedClaim(localGrip)) return false;
            if(target.HasBeenDestroyed) return true;
            double step=Time.fixedTimeAsDouble;
            if(step!=lastHeldStrikeStep)
            { heldStrikeOutcomes.Clear(); lastHeldStrikeStep=step; }
            int partnerId=target.GetInstanceID();
            if(heldStrikeOutcomes.TryGetValue(partnerId,out bool previousOutcome))
                return previousOutcome;

            // Resolve the held object's damage first. Snapshot both sides so
            // the target still receives the original impact after a fracture.
            float speed=incoming.magnitude;
            float heldHardness=Hardness,heldResilience=Resilience;
            string heldMaterial=MaterialName;
            bool heldIsTerrain=IsTerrain;
            float targetHardness=target.Hardness,targetResilience=target.Resilience;
            string targetMaterial=target.MaterialName;
            bool targetIsTerrain=target.IsTerrain;
            bool similarHard=IsSimilarHardStrike(target);
            bool tryTarget=speed>=(similarHard?target.InitialImpactSpeed(
                heldHardness,heldMaterial):target.RequiredImpactSpeed(heldHardness,heldResilience,heldMaterial));
            bool tryHeld=speed>=(similarHard?InitialImpactSpeed(targetHardness,targetMaterial):
                RequiredImpactSpeed(targetHardness,targetResilience,targetMaterial));
            if(similarHard&&(tryTarget||tryHeld)&&
                !(similarityRoll>=0f&&similarityRoll<=1f)) similarityRoll=UnityEngine.Random.value;
            // One decision for the contact, shared with the physics host. Do not
            // compound two independent per-object rolls or reroll collider callbacks.
            bool passed=!similarHard||(tryTarget||tryHeld)&&
                similarityRoll<SimilarDestructionChance(heldHardness,heldResilience,targetHardness,targetResilience);
            if(WebWorldSession.ForwardStrike(this,target,point,incoming,similarHard?similarityRoll:-1f))
            { heldStrikeOutcomes[partnerId]=passed; return passed; }
            RegisterCollision(partnerId);
            target.RegisterCollision(GetInstanceID());
            if(!passed)
            { heldStrikeOutcomes[partnerId]=false; return false; }
            Vector3 hitDirection=speed>0.0001f?incoming/speed:transform.forward;
            if((GetComponent<DestructionFragment>()!=null||
                target.GetComponent<DestructionFragment>()!=null)&&
                (speed>=InitialImpactSpeed(targetHardness,targetMaterial)||
                 speed>=target.InitialImpactSpeed(Hardness,MaterialName))&&
                TrySpecialFragmentCollision(target,point,hitDirection,speed))
            {
                bool fragmentDestroyed=HasBeenDestroyed||target==null||target.HasBeenDestroyed;
                heldStrikeOutcomes[partnerId]=fragmentDestroyed;
                return fragmentDestroyed;
            }
            if(!tryTarget&&!tryHeld)
            { heldStrikeOutcomes[partnerId]=false; return false; }
            float heldSize=tryTarget?WorldSize():0f;
            float targetSize=tryHeld?target.WorldSize():0f;
            double heldVolume=tryTarget?ImpactSolidVolume():0d;
            double targetVolume=tryHeld?target.ImpactSolidVolume():0d;
            float heldMass=tryTarget?ImpactMassKg():0f;
            float targetMass=tryHeld?target.ImpactMassKg():0f;
            bool destroyedHeld=tryHeld&&!HasBeenDestroyed&&ApplyImpact(targetHardness,
                targetResilience,targetSize,point,hitDirection,speed,targetMaterial,
                targetVolume,targetIsTerrain,targetMass,similarHeldStrike:similarHard);
            bool destroyedTarget=tryTarget&&target!=null&&!target.HasBeenDestroyed&&
                target.ApplyImpact(heldHardness,heldResilience,heldSize,
                    point,-hitDirection,speed,heldMaterial,heldVolume,heldIsTerrain,heldMass,
                    similarHeldStrike:similarHard);
            bool destroyed=destroyedTarget||destroyedHeld;
            heldStrikeOutcomes[partnerId]=destroyed;
            return destroyed;
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
            if(IsTerrain) return;
            float remaining=HardnessAfterCollision(Hardness,Resilience,UnityEngine.Random.value);
            if(remaining==Hardness) return;
            Hardness=remaining;
            HardnessChanged?.Invoke(remaining);
        }
        public float WorldSize()
        {
            bool found=false; Bounds bounds=default;
            foreach(var renderer in GetComponentsInChildren<Renderer>())
            { if(!renderer.enabled&&!ResidentPropRendering.IsCached(renderer)||!Owns(renderer)) continue; if(!found) { bounds=renderer.bounds; found=true; } else bounds.Encapsulate(renderer.bounds); }
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
                    new Plane(GetComponent<TreeStructuralPart>()!=null?
                        Quaternion.AngleAxis((attempt%2==0?1f:-1f)*
                            ((attempt+1)/2%6),Vector3.right)*
                            (StickFractureNormal(source)??Vector3.up):
                        UnityEngine.Random.onUnitSphere,source.bounds.center),
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
                // Two scaled copies still give it a visible burst.
                var fallback=TemporaryFallbackMeshes(source);
                first=fallback[0]; second=fallback[1];
                usedFallback=true;
            }
            Mesh[] parts={first,second};
            var centers=new Vector3[2];
            bool prepared=true;
            for(int i=0;i<parts.Length;i++)
                prepared&=PrepareObjectFragmentMesh(parts[i],out centers[i]);
            if(!prepared)
            {
                foreach(var part in parts) Release(part);
                if(usedFallback) { Release(source); return; }
                parts=TemporaryFallbackMeshes(source);
                prepared=true;
                for(int i=0;i<parts.Length;i++)
                    prepared&=PrepareObjectFragmentMesh(parts[i],out centers[i]);
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
            SpawnFragmentsKeepingHeld(requests,spawned,true);
            gameObject.SetActive(false);
            Fragmented?.Invoke(this,spawned);
            GeometryChanged?.Invoke();
            DestructionOccurred?.Invoke(PhysicalDestructEffect.Shatter);
            Release(gameObject);
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
            bool groundImpact=false,float otherMassKg=0f,Vector3? treeSplitPoint=null,bool similarHeldStrike=false)
        {
            PlayerWorldLoading.BeforeChange(gameObject);
            if(!WebWorldSession.CanSimulate(gameObject)) return false;
            if(IsUndersizedBranchFragment)
            {
                if(HasBeenDestroyed||!CanShatterUndersizedBranch(otherHardness,
                    otherResilience,otherMaterialName,speed)) return false;
                SpecialCompleteShatter(point,normal,speed);
                return HasBeenDestroyed;
            }
            bool forcedTreeSplit=treeSplitPoint.HasValue;
            GeneratedTree tree=forcedTreeSplit?null:
                GetComponent<TreeStructuralPart>()?.Tree??GetComponentInParent<GeneratedTree>();
            if(IsPlayerObject(gameObject)||HasBeenDestroyed||!forcedTreeSplit&&
                (Time.time<nextImpact||Application.isPlaying&&!GeometryAttemptReady(point)||
                speed<(similarHeldStrike?InitialImpactSpeed(otherHardness,otherMaterialName):
                    RequiredImpactSpeed(otherHardness,otherResilience,otherMaterialName)))) return false;
            var effect=forcedTreeSplit||similarHeldStrike?PhysicalDestructEffect.Fracture:
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
            float targetSize=WorldSize();
            if(!forcedTreeSplit&&effect==PhysicalDestructEffect.Fracture&&
                targetSize>=2f*Mathf.Max(0f,otherSize))
            {
                if(otherHardness-Hardness<=2f) { LastEffect=effect; return false; }
                effect=PhysicalDestructEffect.Shatter;
            }
            LastEffect=effect;
            float destructionChance=forcedTreeSplit||similarHeldStrike?1f:
                CollisionDestructionChance(Hardness,Resilience,otherHardness,otherResilience,
                    targetSize,otherSize);
            if(!forcedTreeSplit)
                destructionChance*=MetalDestructionChanceScale(MaterialName,Resilience);
            if(destructionChance<1f&&UnityEngine.Random.value>=destructionChance) return false;
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
            // Compact fragments below the gameplay volume floor cannot break.
            // Their cached mass and mesh bounds answer this without copying,
            // welding and validating the entire previously damaged solid.
            if(!forcedTreeSplit&&(GetComponent<DestructionFragment>()!=null||GetComponent<BodyFragment>()!=null)&&
                HasMaterialMass&&SolidVolumeCubicMetres<MinimumFragmentationVolume&&
                !HasElongatedGeometry()) return false;
            // Failed cuts must not retry their full search every physics tick.
            // Assume failure until geometry is committed below; all early
            // returns then share the same bounded backoff.
            if(!forcedTreeSplit)
            {
                Vector3 localGeometryPoint=transform.InverseTransformPoint(point);
                if(transform.TransformVector(localGeometryPoint-geometryAttemptLocalPoint).sqrMagnitude>0.01f)
                    failedGeometryAttempts=0;
                geometryAttemptLocalPoint=localGeometryPoint;
                failedGeometryAttempts=Mathf.Min(failedGeometryAttempts+1,4);
                if(Application.isPlaying)
                    nextGeometryAttempt=Time.time+Mathf.Min(2f,0.25f*(1<<failedGeometryAttempts));
            }
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
            // Use mesh volume in world units. A low-volume source is protected
            // only when its object-relative shape is also compact.
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
            if(worldVolume<MinimumFragmentationVolume&&
                !IsElongatedMesh(source,transform.localToWorldMatrix))
            { foreach(var part in sourceParts) Release(part); return false; }
            var pieces=new List<Mesh>(); Mesh remainder=null;
            bool separatedSource=sourceParts.Count>1;
            bool local=!forcedTreeSplit&&(smallImpactorOnTrunk||preserveRemainder||separatedSource||effect==PhysicalDestructEffect.Shatter&&
                WorldSize()>=2f*Mathf.Max(0.001f,otherSize));
            bool wood=string.Equals(MaterialName,"Wood",StringComparison.OrdinalIgnoreCase)||
                string.Equals(MaterialName,"Burnt Wood",StringComparison.OrdinalIgnoreCase);
            bool rock=string.Equals(MaterialName,"Stone",StringComparison.OrdinalIgnoreCase)&&
                HasRockName();
            // Growth changes preferred chip sizing, not the compact-shape gate.
            float sizeGrowth=FragmentSizeMultiplierForImpact(otherMassKg);
            double localShatterVolumeLimit=effect==PhysicalDestructEffect.Shatter?
                MaximumPartialShatterFragmentVolume/
                    Math.Max(Math.Abs((double)transform.localToWorldMatrix.determinant),0.000000001d):
                double.PositiveInfinity;
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
                        faces,fragmentBudget,minimumDepth,Hardness,localShatterVolumeLimit):
                    ChipWithinFragmentBudget(source,localPoint,radius,out remainder,
                        faces,fragmentBudget,minimumDepth,Hardness,0f,localShatterVolumeLimit);
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
                            MinimumFragmentAxisSize*1.2f*baseSize/scale,Hardness,
                            localShatterVolumeLimit):
                        ChipWithinFragmentBudget(source,localPoint,baseRadius,out remainder,
                            baseFaces,fragmentBudget,
                            MinimumFragmentAxisSize*1.2f*baseSize/scale,Hardness,0f,
                            localShatterVolumeLimit);
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
                        bool split=TryFractureSplit(source,FragmentPreparationMatrix(),
                            false,out var first,out var second,out _,
                            Vector3.Lerp(localPoint,source.bounds.center,0.5f),
                            fixedNormal:structure!=null?StickFractureNormal(source):null);
                        if(!split)
                            split=TryFractureSplit(source,FragmentPreparationMatrix(),
                                false,out first,out second,out _,
                                fixedNormal:structure!=null?StickFractureNormal(source):null);
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
                    // Keep cuts across the piece's local Y axis. Retry shallow
                    // angles around its local X, so fallen trunks retain their
                    // own division frame instead of using a world-horizontal plane.
                    Vector3[] normals={Vector3.up,
                        Quaternion.AngleAxis(5f,Vector3.right)*Vector3.up,
                        Quaternion.AngleAxis(-5f,Vector3.right)*Vector3.up};
                    Vector3 cutPoint=transform.InverseTransformPoint(treeSplitPoint.Value);
                    a=b=null;
                    split=false;
                    foreach(Vector3 planeNormal in normals)
                    {
                        localNormal=planeNormal;
                        if(!DestructionMesh.Split(source,new Plane(localNormal,cutPoint),out a,out b))
                        { if(a!=null) Release(a); if(b!=null) Release(b); a=b=null; continue; }
                        if(ClosedAfterSeparation(a,FragmentPreparationMatrix())&&
                            ClosedAfterSeparation(b,FragmentPreparationMatrix()))
                        { split=true; break; }
                        Release(a); Release(b); a=b=null;
                    }
                }
                else split=TryFractureSplit(source,FragmentPreparationMatrix(),
                    effect==PhysicalDestructEffect.Fracture,
                    out a,out b,out localNormal,
                    effect==PhysicalDestructEffect.Shatter?
                        (Vector3?)ShatterDividingPoint(localPoint,source.bounds.center,
                            Vector3.Distance(localPoint,source.bounds.center),Hardness):null,
                    fixedNormal:structure!=null||effect==PhysicalDestructEffect.Fracture?
                        StickFractureNormal(source):null);
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
                local&&effect==PhysicalDestructEffect.Shatter&&
                    pieces.Exists(piece=>Volume(piece)*
                        Math.Abs(transform.localToWorldMatrix.determinant)>
                        MaximumPartialShatterFragmentVolume+0.0000001d)||
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
                prepared&=PrepareObjectFragmentMesh(pieces[i],out centers[i]);
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
            failedGeometryAttempts=0;
            nextGeometryAttempt=Time.time+0.15f;
            nextImpact=Time.time+0.25f; HasBeenModified=true;
            var requests=new List<FragmentSpawnRequest>(pieces.Count);
            for(int i=0;i<pieces.Count;i++)
                requests.Add(PrepareSpawn(pieces[i],centers[i],materials,appearance,
                    inheritedBody,pieceMasses[i],sourceCenter,normal,speed,
                    local?LargeShatterImpulseMultiplier:1f));
            var spawned=new List<PhysicalDestructible>(pieces.Count);
            tree?.DropNearbyResin(point);
            if(local&&remainder!=null)
            {
                ReplaceGeometry(remainder,materials,appearance,body);
                RefreshMass();
                if(body!=null)
                {
                    FragmentMassCenter.Align(body);
                    structure?.RefreshTrunkCenterOfMass();
                }
            }
            else
            {
                if(remainder!=null) Release(remainder);
                HasBeenDestroyed=true;
            }
            SpawnFragmentsKeepingHeld(requests,spawned,HasBeenDestroyed);
            if(HasBeenDestroyed) gameObject.SetActive(false);
            Fragmented?.Invoke(this,spawned);
            if(forcedTreeSplit&&structure!=null&&structure.IsTrunk)
            {
                TreeStructuralPart upper=null;
                float highest=float.NegativeInfinity;
                foreach(var piece in spawned)
                    if(piece!=null&&piece.GetComponent<TreeStructuralPart>() is { } part&&
                        part.TryGetSolidBoundsInFrame(transform,out Bounds bounds)&&bounds.center.y>highest)
                    { upper=part; highest=bounds.center.y; }
                upper?.AddTrunkSplitSphere(treeSplitPoint.Value);
            }
            GeometryChanged?.Invoke();
            DestructionOccurred?.Invoke(effect);
            if(tree!=null) tree.RecordDestructiveImpact(point,
                structure!=null&&structure.IsTrunk);
            // Support callbacks need the source's transform while reconnecting
            // the remaining tree. Edit-mode deletion is immediate.
            // Reaching the tree threshold can recursively split and remove this
            // source during RecordDestructiveImpact (immediately in the editor).
            if(this!=null&&HasBeenDestroyed) Release(gameObject);
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
            float hardness,double maximumVolume)
        {
            // Measure this hit's usual chip count first. A fixed maximum does
            // nothing for Rock hits that never reach the global cap.
            var original=ChipWithinFragmentBudget(source,point,radius,out remainder,
                facesPerChip,fragmentBudget,minimumDepth,hardness,0f,maximumVolume);
            if(original.Count<=2) return original;
            int target=Mathf.Max(2,Mathf.RoundToInt(original.Count*0.7f));
            int groupedFaces=Mathf.Max(facesPerChip+2,
                Mathf.CeilToInt(facesPerChip/0.7f));
            var reduced=ChipWithinFragmentBudget(source,point,radius,out Mesh reducedRemainder,
                groupedFaces,target,minimumDepth,hardness,0f,maximumVolume);
            if(reduced.Count==0)
            {
                // An irregular surface may need larger face groups.
                reduced=ChipWithinFragmentBudget(source,point,radius,out reducedRemainder,
                    groupedFaces+2,target,minimumDepth,hardness,0f,maximumVolume);
            }
            if(reduced.Count==0) return original;
            foreach(var chip in original) Release(chip);
            Release(remainder);
            remainder=reducedRemainder;
            return reduced;
        }

        public static List<Mesh> ChipWithinFragmentBudget(Mesh source,Vector3 point,float radius,
            out Mesh remainder,int maxFacesPerChip,int maxFragments,float minimumDepth=0f,
            float hardness=10f,float minimumVolume=0f,
            double maximumVolume=double.PositiveInfinity)
        {
            // A box proxy can report contacts outside the remaining mesh.
            // Smaller retry radii cannot reach that material either.
            if(source.bounds.SqrDistance(point)>radius*radius)
            { remainder=null; return new List<Mesh>(); }
            using var validationScope=DestructionMesh.BeginValidationScope();
            int attempts=DestructionMesh.FaceCount(source)>=DestructionMesh.MaximumRefinedChipFaces/2?2:10;
            for(int attempt=0;attempt<attempts;attempt++)
            {
                var chips=DestructionMesh.Chip(source,point,radius,
                    Mathf.Min(Mathf.Max(radius*0.4f,minimumDepth),
                        source.bounds.extents.magnitude*0.3f)*ShatterOffsetMultiplier(hardness),
                    out remainder,maxFacesPerChip);
                bool tooSmall=chips.Exists(chip=>Volume(chip)+0.0000001f<minimumVolume);
                double largestChipVolume=0d;
                foreach(var chip in chips)
                    largestChipVolume=Math.Max(largestChipVolume,Volume(chip));
                bool tooLarge=largestChipVolume>maximumVolume+0.0000001d;
                if(chips.Count>0&&chips.Count<=maxFragments&&!tooSmall&&!tooLarge&&
                    chips.TrueForAll(DestructionMesh.IsClosedConnected)&&ClosedComponents(remainder)) return chips;
                foreach(var chip in chips) Release(chip);
                Release(remainder);
                radius*=tooLarge?
                    Mathf.Clamp(Mathf.Pow((float)(maximumVolume/largestChipVolume),
                        1f/3f)*0.9f,0.2f,0.8f):
                    tooSmall?1.25f:0.8f;
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
        Vector3? StickFractureNormal(Mesh source)
        {
            var treePart=GetComponent<TreeStructuralPart>();
            if(treePart!=null&&treePart.IsTrunk) return Vector3.up;
            Matrix4x4 matrix=transform.localToWorldMatrix;
            Vector3 size=Vector3.Scale(source.bounds.size,new Vector3(
                matrix.GetColumn(0).magnitude,matrix.GetColumn(1).magnitude,
                matrix.GetColumn(2).magnitude));
            if(treePart==null&&PickableObject.ChooseGrabMode(size,MaterialName)!=PickableGrabMode.Stick)
                return null;
            // A plane normal to the long axis splits the stick across its
            // shortest dimension instead of peeling it lengthwise.
            return size.x>=size.y&&size.x>=size.z?Vector3.right:
                size.y>=size.z?Vector3.up:Vector3.forward;
        }
        static bool TryFractureSplit(Mesh source,Matrix4x4 localToWorld,
            bool requireSourceWidth,
            out Mesh first,out Mesh second,out Vector3 chosenNormal,Vector3? dividingPoint=null,
            float minimumWorldVolume=0f,Vector3? fixedNormal=null)
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
            int attempts=fixedNormal.HasValue?1:
                DestructionMesh.FaceCount(source)>=DestructionMesh.MaximumRefinedChipFaces/2?4:48;
            for(int attempt=0;attempt<attempts;attempt++)
            {
                Vector3 normal=fixedNormal??UnityEngine.Random.onUnitSphere;
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
            PlayerWorldLoading.BeforeChange(gameObject);
            ResidentPropRendering.Release(gameObject);
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
                        transform=renderer.transform==transform?Matrix4x4.identity:
                            transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix });
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
            failedGeometryAttempts=0;
            nextGeometryAttempt=0f;
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
            // Each partial hit installs one new shell. Remove the superseded
            // shells rather than accumulating disabled colliders and relays
            // that every later collision/pickup/mass scan has to visit.
            var fragment=GetComponent<DestructionFragment>();
            foreach(var relay in GetComponentsInChildren<PhysicalDestructionCollisionRelay>(true))
                if(relay.Owner==this)
                {
                    if(fragment!=null)
                        foreach(var obsolete in relay.GetComponentsInChildren<Collider>(true))
                            fragment.UntrackCollider(obsolete);
                    Release(relay.gameObject);
                }
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
        Matrix4x4 FragmentPreparationMatrix()
        {
            if(GetComponent<TreeStructuralPart>()==null) return transform.localToWorldMatrix;
            float parentScale=1f;
            bool uniformParents=true;
            for(Transform parent=transform.parent;parent!=null;parent=parent.parent)
            {
                Vector3 scale=parent.localScale;
                if(!Mathf.Approximately(scale.x,scale.y)||!Mathf.Approximately(scale.x,scale.z))
                { uniformParents=false;break; }
                parentScale*=scale.x;
            }
            if(uniformParents) return Matrix4x4.Scale(transform.localScale*parentScale);
            // Bake scale and shear in the tree's frame without rotating the mesh
            // to world space and back. Repeated cuts retain their local seams.
            var frame=Matrix4x4.Rotate(Quaternion.Inverse(transform.rotation))*transform.localToWorldMatrix;
            frame.SetColumn(3,new Vector4(0f,0f,0f,1f));
            return frame;
        }
        bool PrepareObjectFragmentMesh(Mesh mesh,out Vector3 worldCenter)
        {
            bool valid=PrepareFragmentMesh(mesh,FragmentPreparationMatrix(),out worldCenter);
            if(GetComponent<TreeStructuralPart>()!=null)
                worldCenter=transform.position+transform.rotation*worldCenter;
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
                WorldRotation=GetComponent<TreeStructuralPart>()!=null?transform.rotation:Quaternion.identity,
                Mass=Mathf.Max(0.001f,mass),Name=name+" fragment",MaterialName=MaterialName,
                SolidVolume=DestructionMesh.SolidVolume(mesh),
                Hardness=Hardness,Resilience=Resilience,Soft=Soft,BodyDebris=GetComponent<BodyFragment>()!=null,
                Labels=new List<string>(Labels),MinimumImpactSpeed=MinimumImpactSpeed,
                SimilarResilienceTolerance=SimilarResilienceTolerance,
                FragmentImpulsePerSpeed=FragmentImpulsePerSpeed,
                FragmentationCount=fragmentationCount,SizeGrowth=fragmentSizeGrowth,
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
            if(string.Equals(MaterialName,"Wood",StringComparison.OrdinalIgnoreCase)||
                string.Equals(MaterialName,"Burnt Wood",StringComparison.OrdinalIgnoreCase)||
                request.BranchPart)
                LimitWoodFragmentFlight(ref request.LinearVelocity,ref request.Impulse,request.Mass);
            return request;
        }

        void SpawnFragmentsKeepingHeld(List<FragmentSpawnRequest> requests,
            List<PhysicalDestructible> spawned,bool sourceWillBeDestroyed)
        {
            var sourcePickup=GetComponent<PickableObject>();
            if(WebWorldSession.TryGetHeldGrip(sourcePickup,out Transform hand,out Vector3 grip)&&requests.Count>0)
            {
                FragmentSpawnRequest closest=null;
                // A partial break leaves a valid candidate on the source too.
                float bestDistance=sourceWillBeDestroyed?float.PositiveInfinity:
                    (sourcePickup.NearestMeshSurfacePoint(grip)-grip).sqrMagnitude;
                foreach(var request in requests)
                {
                    float distance=FragmentDistanceSquared(request,grip);
                    if(distance>=bestDistance) continue;
                    bestDistance=distance;
                    closest=request;
                }
                if(closest!=null)
                {
                    // The piece remaining in the hand needs a usable grip.
                    requests.Remove(closest);
                    closest.TemporaryDebris=false;
                    closest.LinearVelocity=Vector3.zero;
                    closest.AngularVelocity=Vector3.zero;
                    closest.Impulse=Vector3.zero;
                    PhysicalDestructible retained=SpawnPrepared(closest);
                    spawned.Add(retained);
                    PickableObject fragment=retained.GetComponent<PickableObject>();
                    WebWorldSession.TransferFragmentGrip(sourcePickup,fragment,hand,grip);
                }
            }
            SpawnImmediately(requests,spawned);
        }

        static float FragmentDistanceSquared(FragmentSpawnRequest request,Vector3 hand)
        {
            Vector3 localHand=Quaternion.Inverse(request.WorldRotation)*(hand-request.WorldCenter);
            Vector3[] vertices=request.Mesh.vertices;
            int[] triangles=request.Mesh.triangles;
            float best=float.PositiveInfinity;
            for(int i=0;i+2<triangles.Length;i+=3)
            {
                Vector3 point=PickableObject.ClosestTrianglePoint(localHand,
                    vertices[triangles[i]],vertices[triangles[i+1]],
                    vertices[triangles[i+2]]);
                best=Mathf.Min(best,(point-localHand).sqrMagnitude);
            }
            if(!float.IsFinite(best))
                best=(request.Mesh.bounds.ClosestPoint(localHand)-localHand).sqrMagnitude;
            return best;
        }

        static void SpawnImmediately(List<FragmentSpawnRequest> requests,
            List<PhysicalDestructible> spawned)
        {
            foreach(var request in requests) spawned.Add(SpawnPrepared(request));
        }

        internal static Collider AddFragmentCollider(GameObject target,Mesh mesh,bool dynamic)
        {
            int triangleCount=0;
            for(int slot=0;slot<mesh.subMeshCount;slot++)
                triangleCount+=(int)mesh.GetIndexCount(slot)/3;
            if(dynamic&&triangleCount>MaximumConvexFragmentTriangles)
            {
                // A bounds box can be far from an angular chip's actual surface.
                // Cook a small convex hull from the source's outer faces instead.
                Mesh proxy=FragmentColliderProxy(mesh);
                if(proxy!=null) {
                    var precise=target.AddComponent<MeshCollider>();
                    precise.sharedMesh=proxy;precise.convex=true;
                    target.AddComponent<RuntimeMeshOwner>().Initialize(proxy);
                    return precise;
                }
            }
            var collider=target.AddComponent<MeshCollider>();
            collider.sharedMesh=mesh;
            collider.convex=dynamic;
            return collider;
        }

        static Mesh FragmentColliderProxy(Mesh source)
        {
            Vector3[] vertices=source.vertices;
            var faces=new List<(int a,int b,int c)>();
            int[] vertexFace=new int[vertices.Length];Array.Fill(vertexFace,-1);
            for(int slot=0;slot<source.subMeshCount;slot++) {
                int[] triangles=source.GetTriangles(slot);
                for(int t=0;t+2<triangles.Length;t+=3) {
                    int face=faces.Count;faces.Add((triangles[t],triangles[t+1],triangles[t+2]));
                    if(vertexFace[triangles[t]]<0) vertexFace[triangles[t]]=face;
                    if(vertexFace[triangles[t+1]]<0) vertexFace[triangles[t+1]]=face;
                    if(vertexFace[triangles[t+2]]<0) vertexFace[triangles[t+2]]=face;
                }
            }
            var selected=new HashSet<int>();
            void Support(Vector3 direction) {
                int best=-1;float score=float.NegativeInfinity;
                for(int i=0;i<vertices.Length;i++) if(vertexFace[i]>=0) {
                    float dot=Vector3.Dot(vertices[i],direction);
                    if(dot>score) { score=dot;best=i; }
                }
                if(best>=0) selected.Add(vertexFace[best]);
            }
            Support(Vector3.right);Support(Vector3.left);Support(Vector3.up);
            Support(Vector3.down);Support(Vector3.forward);Support(Vector3.back);
            for(int i=0;i<32;i++) {
                float y=1f-2f*(i+.5f)/32f;
                float radius=Mathf.Sqrt(Mathf.Max(0f,1f-y*y));
                float angle=i*2.39996323f;
                Support(new Vector3(Mathf.Cos(angle)*radius,y,Mathf.Sin(angle)*radius));
            }
            if(selected.Count<4) return null;
            var points=new List<Vector3>(selected.Count*3);
            var indices=new List<int>(selected.Count*3);
            foreach(int face in selected) {
                var entry=faces[face];int start=points.Count;
                points.Add(vertices[entry.a]);points.Add(vertices[entry.b]);points.Add(vertices[entry.c]);
                indices.Add(start);indices.Add(start+1);indices.Add(start+2);
            }
            var proxy=new Mesh { name="Fragment convex collision proxy" };
            proxy.SetVertices(points);proxy.SetTriangles(indices,0);proxy.RecalculateBounds();
            return proxy;
        }

        static PhysicalDestructible SpawnPrepared(FragmentSpawnRequest request)
        {
            if(request.BodyDebris) request.TemporaryDebris=false;
            var piece=new GameObject(request.Name);
            piece.transform.SetPositionAndRotation(request.WorldCenter,request.WorldRotation);
            piece.AddComponent<MeshFilter>().sharedMesh=request.Mesh;
            var appearanceMaterials=request.BodyDebris?
                BodyFragment.OwnAppearance(piece,request.Materials,request.Mesh.subMeshCount):request.Materials;
            ApplyAppearance(piece.AddComponent<MeshRenderer>(),appearanceMaterials,request.Appearance);
            AddFragmentCollider(piece,request.Mesh,true);
            var body=piece.AddComponent<Rigidbody>(); body.mass=request.Mass;
            FragmentMassCenter.Align(body);
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            DynamicObjectSurfaceGuard.Ensure(body);
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
                pickup.ForceRegularGrip=Array.Exists(WorldConfig.MineralMaterials,name=>string.Equals(name,request.MaterialName,StringComparison.OrdinalIgnoreCase))||request.MaterialName.IndexOf("stone",StringComparison.OrdinalIgnoreCase)>=0;
            }
            piece.AddComponent<DestructionFragment>().IsBranchFragment=request.BranchPart;
            if(request.BodyDebris) { piece.AddComponent<BodyFragment>().WorldId="d"+Guid.NewGuid().ToString("N");
                if(piece.GetComponent<PickableObject>() is {} bodyPickup) { bodyPickup.DisplayName="Body fragment";bodyPickup.ForceRegularGrip=true; } }
            if(request.TemporaryDebris) piece.AddComponent<TemporaryShatterDebris>();
            if(properties.SolidVolumeCubicMetres<MinimumFragmentationVolume)
                SmallFragmentRest.Ensure(piece);
            PlayerWorldLoading.NotifyChanged(piece);
            return properties;
        }
        static void Release(UnityEngine.Object value) { if(Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        void OnDestroy() { WebWorldSession.Removed(gameObject); if(ownedMesh!=null) Release(ownedMesh); }

        [Serializable] public sealed class NetworkProperties
        {
            public float hardness,resilience,minimumSpeed,tolerance,impulse,growth;
            public int count,effect;
            public string material;
            public string[] labels;
            public bool soft,modified,fragment,branchFragment;
        }
        internal NetworkProperties CaptureNetworkProperties() => new() {
            hardness=Hardness,resilience=Resilience,minimumSpeed=MinimumImpactSpeed,
            tolerance=SimilarResilienceTolerance,impulse=FragmentImpulsePerSpeed,
            growth=fragmentSizeGrowth,count=fragmentationCount,effect=(int)LastEffect,
            material=MaterialName,labels=Labels.ToArray(),soft=Soft,modified=HasBeenModified,
            fragment=GetComponent<DestructionFragment>()!=null,
            branchFragment=GetComponent<DestructionFragment>()?.IsBranchFragment??false,
        };
        internal void ApplyNetworkProperties(NetworkProperties value)
        {
            if(value==null) return;
            Hardness=IsTerrain?Mathf.Max(Hardness,value.hardness):value.hardness;Resilience=value.resilience;MinimumImpactSpeed=value.minimumSpeed;
            SimilarResilienceTolerance=value.tolerance;FragmentImpulsePerSpeed=value.impulse;
            fragmentSizeGrowth=value.growth;fragmentationCount=value.count;LastEffect=(PhysicalDestructEffect)value.effect;
            MaterialName=value.material;Soft=value.soft;HasBeenModified=value.modified;HasBeenDestroyed=false;
            Labels=new List<string>(value.labels??Array.Empty<string>());
            if(value.fragment) {
                var fragment=GetComponent<DestructionFragment>()??gameObject.AddComponent<DestructionFragment>();
                fragment.IsBranchFragment=value.branchFragment;
            }
            massInitialized=false;RefreshMass();
        }
        internal Mesh CaptureNetworkMesh(out Material[] materials) => Snapshot(out materials,out _);
        internal void ApplyNetworkMesh(Mesh mesh,Material[] materials)
        {
            PlayerWorldLoading.BeforeChange(gameObject);
            ReplaceGeometry(mesh,materials,new MaterialPropertyBlock[materials.Length],GetComponent<Rigidbody>());
            massInitialized=false;RefreshMass();
        }
        internal void RefreshNetworkColliders()
        {
            bool dynamic=GetComponentInParent<Rigidbody>()!=null;
            foreach(var collider in GetComponentsInChildren<MeshCollider>(true)) {
                if(!Owns(collider)||collider.sharedMesh==null) continue;
                if(!dynamic) { collider.convex=false;continue; }
                int faces=0;var mesh=collider.sharedMesh;
                for(int slot=0;slot<mesh.subMeshCount;slot++) faces+=(int)mesh.GetIndexCount(slot)/3;
                if(faces<=MaximumConvexFragmentTriangles) collider.convex=true;
                else {
                    Mesh proxy=FragmentColliderProxy(mesh);
                    if(proxy!=null) {
                        collider.sharedMesh=proxy;collider.convex=true;
                        collider.gameObject.AddComponent<RuntimeMeshOwner>().Initialize(proxy);
                    }
                }
            }
        }
    }

    // Dynamic fragments and dropped objects can begin a physics step partly
    // inside a static mesh. Resolve that overlap before the solver lets them
    // settle below the visible surface.
    public sealed class DynamicObjectSurfaceGuard : MonoBehaviour
    {
        private const float ContactTolerance=0.02f;
        private const float SeparationSkin=0.005f;
        private const float MaximumCorrectionPerStep=0.5f;
        private readonly Collider[] nearby=new Collider[64];
        private Rigidbody body;
        private float nextCheck;

        public static void Ensure(Rigidbody target)
        {
            if(target!=null) ResidentPropRendering.Release(target.gameObject);
            if(target!=null&&target.GetComponent<DynamicObjectSurfaceGuard>()==null)
            {
                target.gameObject.AddComponent<DynamicObjectSurfaceGuard>();
            }
        }

        private void Awake() => body=GetComponent<Rigidbody>();

        private void FixedUpdate()
        {
            if(body==null||body.isKinematic||!body.detectCollisions||body.IsSleeping()||
                Time.fixedTime<nextCheck) return;
            nextCheck=Time.fixedTime+0.04f;
            ResolveStaticOverlap();
        }

        public bool ResolveStaticOverlap()
        {
            if(body==null||body.isKinematic||!body.detectCollisions) return false;
            Collider[] own=GetComponentsInChildren<Collider>();
            Vector3 correction=Vector3.zero;
            Vector3 separatingNormal=Vector3.zero;
            float largestDepth=ContactTolerance;
            foreach(Collider shape in own)
            {
                if(shape==null||!shape.enabled||shape.isTrigger||
                    shape.attachedRigidbody!=body||
                    shape is MeshCollider { convex:false }) continue;
                Bounds bounds=shape.bounds;
                if(bounds.extents.sqrMagnitude<0.000001f) continue;
                int count=Physics.OverlapBoxNonAlloc(bounds.center,
                    bounds.extents+Vector3.one*0.05f,nearby,Quaternion.identity,
                    ~0,QueryTriggerInteraction.Ignore);
                for(int i=0;i<count;i++)
                {
                    Collider surface=nearby[i];
                    if(surface==null||surface==shape||!surface.enabled||
                        surface.attachedRigidbody!=null||
                        surface.transform.IsChildOf(transform)) continue;
                    if(!Physics.ComputePenetration(shape,
                        shape.transform.position,shape.transform.rotation,surface,
                        surface.transform.position,surface.transform.rotation,
                        out Vector3 direction,out float depth)) continue;
                    if(direction.y< -0.25f&&
                        surface.bounds.max.y>bounds.center.y&&
                        surface.Raycast(new Ray(new Vector3(bounds.center.x,
                            surface.bounds.max.y+0.5f,bounds.center.z),Vector3.down),
                            out RaycastHit topHit,surface.bounds.size.y+1f))
                    {
                        float lift=topHit.point.y-bounds.min.y+SeparationSkin;
                        if(lift>depth)
                        { direction=Vector3.up; depth=lift; }
                    }
                    if(depth<=largestDepth) continue;
                    largestDepth=depth;
                    separatingNormal=direction;
                    correction=direction*Mathf.Min(depth+SeparationSkin,
                        MaximumCorrectionPerStep);
                }
            }
            if(correction.sqrMagnitude<0.000001f) return false;
            body.position+=correction;
            float inwardSpeed=Vector3.Dot(body.linearVelocity,separatingNormal);
            if(inwardSpeed<0f)
                body.linearVelocity-=separatingNormal*inwardSpeed;
            body.WakeUp();
            return true;
        }
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
            FragmentMassCenter.Align(GetComponent<Rigidbody>());
            foreach(var collider in GetComponentsInChildren<Collider>(true))
                TrackCollider(collider);
        }
        internal void TrackCollider(Collider collider)
        {
            if(collider==null||!activeColliders.Add(collider)) return;
            registeredColliders.Add(collider);
            TemporaryShatterDebris.IgnoreForNewFragment(collider);
        }
        internal void UntrackCollider(Collider collider)
        {
            activeColliders.Remove(collider);
            registeredColliders.Remove(collider);
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
                if(fragment.GetComponent<SmallFragmentRest>()!=null||
                    fragment.GetComponent<PhysicalDestructible>() is {} physical&&
                    physical.SolidVolumeCubicMetres<PhysicalDestructible.MinimumFragmentationVolume) continue;
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

    // Small Shatter pieces keep their appearance and ignore other fragments
    // to avoid collision cascades. SmallFragmentRest retires only their physics.
    public sealed class TemporaryShatterDebris : MonoBehaviour
    {
        static readonly HashSet<Collider> activeColliders=new();
        Collider debrisCollider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetActiveColliders() => activeColliders.Clear();

        void Awake()
        {
            SmallFragmentRest.Ensure(gameObject);
            debrisCollider=GetComponent<Collider>();
            if(debrisCollider==null) return;
            foreach(var collider in DestructionFragment.ActiveColliders)
                IgnorePair(debrisCollider,collider);
            activeColliders.Add(debrisCollider);
        }

        internal static void UntrackCollider(Collider collider) => activeColliders.Remove(collider);
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
            GetComponent<TreeStructuralPart>()?.ReleaseChildren();
            activeColliders.Remove(debrisCollider);
        }
    }

    // Register generated scene objects once, with collision-time registration closing
    // the gap between spawning a collider and the next discovery pass.
    public sealed class PhysicalDestructionWorld : MonoBehaviour
    {
        public void RegisterSceneObjects()
        {
            var routine=RegisterSceneObjectsIncrementally();
            while(routine.MoveNext()) { }
        }

        internal System.Collections.IEnumerator RegisterSceneObjectsIncrementally()
        {
            double batchStarted=Time.realtimeSinceStartupAsDouble;
            foreach(var collider in FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                PhysicalDestructible.Resolve(collider);
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
            }
            foreach(var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
                if(!renderer.enabled) continue;
                if(renderer.GetComponent<ResidentRenderVisual>()!=null) continue;
                if(renderer.GetComponent<TreeImpactMarker>()!=null) continue;
                if(renderer.GetComponentInParent<RopeObject>()!=null) continue;
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
    }
}

