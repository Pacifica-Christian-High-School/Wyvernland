using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    [DefaultExecutionOrder(1000)]
    public sealed class MovingImpactMotion : MonoBehaviour
    {
        static readonly HashSet<MovingImpactMotion> movingObjects=new();
        static float lastFixedSync=float.NegativeInfinity;
        internal static int FixedSyncCount { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSampling() { movingObjects.Clear();lastFixedSync=float.NegativeInfinity;FixedSyncCount=0; }
        internal void RetirePhysics()
        {
            enabled=false;body=null;probes.Clear();previousValid=false;velocity=angular=Vector3.zero;movingObjects.Remove(this);
        }
        Rigidbody body;Vector3 velocity,angular,center;
        Vector3 previousPosition;Quaternion previousRotation;bool previousValid;
        readonly List<(Collider Shape,Vector3 Point,float Radius)> probes=new();
        public static MovingImpactMotion Ensure(Rigidbody target)
        {
            if(target==null) return null;
            var motion=target.GetComponent<MovingImpactMotion>();if(motion==null) motion=target.gameObject.AddComponent<MovingImpactMotion>();
            bool initialize=motion.body!=target;motion.body=target;motion.enabled=true;if(initialize) motion.Sample();return motion;
        }
        void FixedUpdate()
        {
            Sample();
            if(body==null||body.isKinematic||body.IsSleeping()||GetComponent<PhysicalDestructible>()==null||velocity.sqrMagnitude<4f) return;
            if(lastFixedSync!=Time.fixedTime)
            { Physics.SyncTransforms();lastFixedSync=Time.fixedTime;FixedSyncCount++; }
            var distance=velocity.magnitude*Time.fixedDeltaTime;
            var hits=body.SweepTestAll(velocity.normalized,distance,QueryTriggerInteraction.Ignore);
            if(FirstSolid(hits,out var hit)&&IsActor(hit.collider)) {
                HitActor(hit.collider,SourceShape(),hit.point,-hit.normal,PointVelocity(hit.point));
                // Preserve the collision response even when discrete physics would
                // cross the complete limb between two solver steps.
                body.position+=velocity.normalized*Mathf.Max(0,hit.distance-.003f);
                body.linearVelocity=Vector3.Reflect(velocity,hit.normal)*.15f;
            }
        }
        bool FirstSolid(RaycastHit[] hits,out RaycastHit nearest)
        {
            nearest=default;float best=float.PositiveInfinity;
            var source=SourceShape();
            var holder=GetComponent<PickableObject>()?.HeldHand?.GetComponentInParent<FirstPersonController>();
            foreach(var hit in hits) {
                var c=hit.collider;
                if(c==null||!c.enabled||c.isTrigger||c.transform.IsChildOf(transform)||
                    holder!=null&&c.transform.IsChildOf(holder.transform)||hit.distance>=best||
                    source!=null&&IgnoresContact(source,c)) continue;
                nearest=hit;best=hit.distance;
            }
            return best<float.PositiveInfinity;
        }
        Collider SourceShape()=>GetComponentsInChildren<Collider>().FirstOrDefault(c=>c.enabled&&!c.isTrigger&&c.attachedRigidbody==body);
        static bool IsActor(Collider c)=>c.GetComponent<PlayerHitShape>()!=null||c.GetComponentInParent<RemotePlayerDamage>()!=null;
        internal static bool IgnoresContact(Collider source,Collider target)=>source==null||target==null||
            Physics.GetIgnoreCollision(source,target)||Physics.GetIgnoreLayerCollision(source.gameObject.layer,target.gameObject.layer);
        internal static void HitActor(Collider target,Collider source,Vector3 point,Vector3 normal,Vector3 motion)
        {
            if(IgnoresContact(source,target)) return;
            if(target.GetComponentInParent<PlayerCorpse>() is {} corpse)
            {
                Vector3 relative=motion-(target.attachedRigidbody!=null?target.attachedRigidbody.GetPointVelocity(point):Vector3.zero);
                corpse.TryShatterFromImpact(source.GetComponentInParent<PhysicalDestructible>(),point,relative);return;
            }
            target.GetComponentInParent<PlayerRagdoll>()?.WakeSimulation();
            var tag=target.GetComponent<PlayerHitShape>();
            if(tag?.Owner!=null) tag.Owner.ReceiveImpact(tag.Part,source,point,normal,motion,PlayerDamage.LegFromBone(tag.transform));
            else if(target.GetComponentInParent<RemotePlayerDamage>() is {} remote)
                WebPlayerPresence.QueueImpact(remote,source.GetComponentInParent<PhysicalDestructible>(),tag!=null?tag.Part:PlayerBodyPart.Torso,
                    tag!=null?tag.transform.parent:remote.transform,point,normal,motion);
        }
        void LateUpdate()
        {
            if(body==null||!body.isKinematic||GetComponent<PhysicalDestructible>()==null) { previousValid=false;return; }
            Sample();
            var item=GetComponent<PickableObject>();var now=transform.position;var rotation=transform.rotation;
            if(previousValid&&(now-previousPosition).sqrMagnitude<.00000001f&&Quaternion.Angle(rotation,previousRotation)<.001f) return;
            Vector3 motion=item!=null&&item.IsHeld?item.HeldPointVelocity(now):WebWorldSession.ReplicaMotion(gameObject,out var v,out _)?v:Vector3.zero;
            if(previousValid&&Time.deltaTime>0) {
                Physics.SyncTransforms();
                var travel=now-previousPosition;float length=travel.magnitude;
                if(length>.0001f) {
                    try {
                        body.position=previousPosition;body.rotation=previousRotation;Physics.SyncTransforms();
                        if(FirstSolid(body.SweepTestAll(travel/length,length,QueryTriggerInteraction.Ignore),out var hit)&&IsActor(hit.collider))
                            HitActor(hit.collider,SourceShape(),hit.point,-hit.normal,
                                item!=null&&item.IsHeld?travel/Time.deltaTime:motion);
                    } finally { body.position=now;body.rotation=rotation;Physics.SyncTransforms(); }
                }
                // Sweep individual cross sections as well, so rotating a long
                // held object detects the tip's arc even with no root translation.
                foreach(var probe in probes) if(probe.Shape!=null&&probe.Shape.enabled) {
                    var end=transform.TransformPoint(probe.Point);var start=previousPosition+previousRotation*Vector3.Scale(probe.Point,transform.lossyScale);
                    var delta=end-start;float distance=delta.magnitude;if(distance<.0001f) continue;
                    if(FirstSolid(Physics.SphereCastAll(start,probe.Radius,delta/distance,distance,~0,QueryTriggerInteraction.Ignore),out var hit)&&IsActor(hit.collider))
                        HitActor(hit.collider,probe.Shape,hit.point,-hit.normal,
                            item!=null&&item.IsHeld?delta/Time.deltaTime:PointVelocity(hit.point));
                }
            }
            probes.Clear();
            foreach(var c in GetComponentsInChildren<Collider>()) if(c.enabled&&!c.isTrigger&&c.attachedRigidbody==body) {
                var b=c.bounds;float radius=Mathf.Max(.002f,Mathf.Min(b.extents.x,b.extents.y,b.extents.z)*.75f);
                probes.Add((c,transform.InverseTransformPoint(b.center),radius));
                var axis=b.extents.x>b.extents.y?(b.extents.x>b.extents.z?Vector3.right:Vector3.forward):(b.extents.y>b.extents.z?Vector3.up:Vector3.forward);
                var offset=Vector3.Scale(axis,b.extents)-axis*radius;
                probes.Add((c,transform.InverseTransformPoint(b.center+offset),radius));probes.Add((c,transform.InverseTransformPoint(b.center-offset),radius));
            }
            previousPosition=now;previousRotation=rotation;previousValid=true;
        }
        void Sample() {
            if(body==null) { movingObjects.Remove(this);return; }
            velocity=body.linearVelocity;angular=body.angularVelocity;center=body.worldCenterOfMass;
            if(body.isKinematic) {
                var held=GetComponent<PickableObject>();
                if(held!=null&&held.IsHeld) { velocity=held.HeldPointVelocity(center);angular=held.HeldAngularVelocity; }
                else if(WebWorldSession.ReplicaMotion(gameObject,out var v,out var w)) { velocity=v;angular=w; }
            }
            if(gameObject.activeInHierarchy&&(velocity.sqrMagnitude>.0025f||angular.sqrMagnitude>.0025f)) movingObjects.Add(this);
            else movingObjects.Remove(this);
        }
        void OnDisable()=>movingObjects.Remove(this);
        void OnDestroy()=>movingObjects.Remove(this);
        internal static bool HasMovingObjectWithin(Vector3 point,float radius,Rigidbody excluded)
        {
            foreach(var motion in movingObjects) if(motion!=null&&motion.body!=null&&motion.body!=excluded&&
                motion.gameObject.activeInHierarchy&&(motion.body.worldCenterOfMass-point).sqrMagnitude<=radius*radius) return true;
            return false;
        }
        public Vector3 PointVelocity(Vector3 point)=>velocity+Vector3.Cross(angular,point-center);
    }
    [DefaultExecutionOrder(250),DisallowMultipleComponent]
    public sealed partial class PlayerDamage : MonoBehaviour
    {
        public const float RespawnDelay=2f;
        [Serializable] public sealed class Pit { public string id,bone;public Vector3 point,incoming; }
        [Serializable] public sealed class State {
            public int injury,life,hit,severed,shockPhase,torsoHits;public bool fullPose;public float shockRemaining,recoveryRemaining;public int[] limbHits,armHits,legHits;public CorpseState[] limbs; public Pit[] pits; public float[] pose; public CorpseState[] corpses;public Impact[] impacts;public string[] received;
        }
        [Serializable] public sealed class Impact {
            public string id,target,source,bone;public int life,part;public Vector3 point,normal,velocity;public float threshold,mass;
        }
        [Serializable] public sealed class CorpseState {
            public string id,left,right;public int limb,missing;public Vector3 position;public Quaternion rotation;
            public float[] pose,motion;public Vector3 lp,rp;public Quaternion lq,rq;
        }
        struct Motion { public Vector3 v,w,center; public float time; }
        readonly Dictionary<int,Motion> motions=new();
        readonly Dictionary<int,float> cooldowns=new();
        readonly HashSet<string> receivedImpacts=new();
        readonly List<string> acknowledgements=new();
        readonly List<Pit> pits=new();
        readonly List<PlayerCorpse> corpses=new();
        readonly int[] limbHits=new int[4],armHits=new int[2],legHits=new int[2];readonly PlayerCorpse[] severedObjects=new PlayerCorpse[4];int severed;
        FirstPersonController controller;RiggedPlayerAvatar avatar;PlayerRagdoll ragdoll;
        Transform model;Mesh[] initialMeshes;
        SkinnedMeshRenderer[] skins;readonly List<Mesh> privateMeshes=new();
        PlayerHitShape[] hitShapes;
        PlayerInjury injury;int life,hit,torsoHits;float hitBlinkStartedAt=float.NegativeInfinity,respawnAt;
        Texture2D upperEyelid,lowerEyelid;
        bool replica;
        State replicaState;
        internal bool SuppressPlayerOverlay;
        internal void SetReplica(bool value)
        {
            if(replica==value) return;
            replica=value;ragdoll.SetReplica(value);
            if(!value) ResumeReplicaShock(replicaState);
            // Corpses have independent shared-world authority.
            if(!value&&IsDead) respawnAt=Time.unscaledTime+RespawnDelay;
        }
        internal void ApplyReplicaState(State next)
        {
            if(!replica||next==null) return;
            if(life!=next.life) {
                for(int i=0;i<skins.Length;i++) if(skins[i]!=null) { skins[i].sharedMesh=initialMeshes[i];skins[i].enabled=true; }
                ragdoll.ResetPose();pits.Clear();torsoHits=0;severed=0;Array.Clear(armHits,0,2);Array.Clear(legHits,0,2);
            }
            life=next.life;hit=next.hit;torsoHits=next.torsoHits;injury=(PlayerInjury)next.injury;replicaState=next;
            if(next.armHits?.Length==2) Array.Copy(next.armHits,armHits,2);
            if(next.legHits?.Length==2) Array.Copy(next.legHits,legHits,2);
            controller.RefreshInjuryLocomotion();
            ragdoll.SetFullBodyStun(next.shockPhase>0);ragdoll.SetInjury(injury);ragdoll.ApplyPose(next.pose,entire:next.fullPose||next.shockPhase>0,woundedArms:WoundedArms(next),woundedLegs:WoundedLegs(next));
            int newMissing=next.severed&~severed;if(newMissing!=0) PlayerDamageMesh.RemoveLimbs(model,newMissing,privateMeshes);
            severed=next.severed;ragdoll.SetMissingLimbs(severed);
            if(next.limbHits?.Length==4) Array.Copy(next.limbHits,limbHits,4);
            if(next.pits!=null) while(pits.Count<next.pits.Length&&pits.Count<12) {
                var pit=next.pits[pits.Count];pits.Add(pit);CutTorso(model,PitPoint(model,transform,pit),privateMeshes);
            }
            foreach(var state in (next.corpses??Array.Empty<CorpseState>()).Concat(next.limbs??Array.Empty<CorpseState>())) {
                if(WebWorldSession.IsCorpseRemoved(state.id)) continue;
                var corpse=PlayerCorpse.Find(state.id);
                if(corpse==null) {
                    if(state.limb>0) corpse=PlayerCorpse.Create(state,transform.parent);
                    else {
                        var node=new GameObject(state.id);node.transform.SetPositionAndRotation(state.position,state.rotation);
                        var visual=Instantiate(model.gameObject,node.transform,false).transform;visual.gameObject.SetActive(true);
                        foreach(var behaviour in visual.GetComponentsInChildren<MonoBehaviour>()) { behaviour.enabled=false;if(behaviour is not PlayerHitShape) Release(behaviour); }
                        corpse=node.AddComponent<PlayerCorpse>();corpse.Initialize(visual,true,reference:ragdoll,spawnFragments:false);corpses.Add(corpse);
                    }
                }
if(state.limb>0) severedObjects[state.limb-1]=corpse;
                corpse.ApplyPresence(state);
            }
            for(int i=corpses.Count-1;i>=0;i--) if(corpses[i]==null||next.corpses==null||!next.corpses.Any(c=>c.id==corpses[i].name)) {
                corpses.RemoveAt(i);
            }
            model.gameObject.SetActive(!IsDead);avatar.enabled=!IsDead;
        }
        public bool IsDead=>(injury&PlayerInjury.Dead)!=0;
        public bool CanMove=>!IsShocked&&(injury&(PlayerInjury.Legs|PlayerInjury.Dead))==0;
        public bool IsLegDisabled(bool left)=>(injury&(PlayerInjury.Legs|PlayerInjury.Dead|(left?PlayerInjury.LeftLeg:PlayerInjury.RightLeg)))!=0;
        public bool SingleLegSupport=>CanMove&&(IsLegDisabled(true)!=IsLegDisabled(false));
        public float MovementSpeedScale=>!CanMove?0f:
            Mathf.Max(.25f,1f-.25f*(Mathf.Min(3,legHits[0])+Mathf.Min(3,legHits[1])))*
            Mathf.Max(.4f,Mathf.Pow(.9f,torsoHits));
        public float LegAnimationSpeed(bool left)=>IsLegDisabled(left)?0f:1f;
        public float LegAnimationSwingAmount(bool left)=>IsLegDisabled(left)?0f:
            Mathf.Max(.5f,1f-.2f*legHits[left?0:1]);
        public bool CanUseHand(bool left)=>!IsDead&&!IsShocked&&(injury&(left?PlayerInjury.LeftArm:PlayerInjury.RightArm))==0;
        public float ArmMovementSpeed(bool left)=>CanUseHand(left)?1f:0f;
        public float ArmAnimationSwingAmount(bool left)=>!CanUseHand(left)?0f:
            armHits[left?0:1]>=2?.5f:armHits[left?0:1]==1?.8f:1f;
        internal static int WoundedArms(State state)=>(state?.armHits?.Length==2?((state.armHits[0]>0?1:0)|(state.armHits[1]>0?2:0)):0);
        internal static int WoundedLegs(State state)=>state?.legHits?.Length==2?((state.legHits[0]>0?1:0)|(state.legHits[1]>0?2:0)|((state.injury&48)!=0?3:0)):0;
        internal static Vector3 PitPoint(Transform model,Transform root,Pit pit) {
            var bone=string.IsNullOrEmpty(pit.bone)?null:model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==pit.bone);
            return (bone!=null?bone:root).TransformPoint(pit.point);
        }
        internal PlayerRagdoll Ragdoll=>ragdoll;
        internal PlayerInjury Injury=>injury;
        public static float RequiredSpeed(PhysicalDestructible source)=>source==null?float.PositiveInfinity:
            Mathf.Max(18f,source.InitialImpactSpeed(1f,"Flesh")+5f);
        internal static float RequiredSpeed(PhysicalDestructible source,PlayerBodyPart part)=>
            part==PlayerBodyPart.Head&&source!=null&&source.MaterialName=="Iron"&&
            source.GetComponent<PickableObject>() is { IsHeld:true }?5f:RequiredSpeed(source);
        public void Initialize(FirstPersonController player,RiggedPlayerAvatar rig,Transform visual)
        {
            controller=player;avatar=rig;model=visual;
            skins=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            initialMeshes=skins.Select(s=>s.sharedMesh).ToArray();
            ragdoll=gameObject.AddComponent<PlayerRagdoll>();ragdoll.Initialize(model,this);
            foreach(var shape in GetComponentsInChildren<Collider>(true)) {
                if(shape.GetComponentInParent<PickableObject>()!=null) continue;
                var tag=shape.GetComponent<PlayerHitShape>()??shape.gameObject.AddComponent<PlayerHitShape>();
                tag.Owner=this;
                if(shape.name.Contains("Foot")||shape.name.Contains("Ground Contact")) tag.Part=PlayerBodyPart.Legs;
            }
            IgnoreOwnColliders();
            hitShapes=model.GetComponentsInChildren<PlayerHitShape>();
        }
        void IgnoreOwnColliders()
        {
            var shapes=GetComponentsInChildren<Collider>(true);
            foreach(var a in shapes) foreach(var b in shapes) if(a!=b) Physics.IgnoreCollision(a,b);
        }
        void FixedUpdate()
        {
            if(replica||IsDead||controller==null||controller.IsPaused) return;
            if(motions.Count>512) motions.Clear();
            // Capture the attacker's own velocity before the solver stops it.
            // A player's run speed is never added to this measurement.
            foreach(var shape in Physics.OverlapSphere(transform.position+Vector3.up,4f,~0,QueryTriggerInteraction.Ignore)) {
                if(shape.GetComponentInParent<PlayerRagdoll>()!=null&&shape.GetComponentInParent<PickableObject>()==null) continue;
                var body=shape.attachedRigidbody;if(body==null) continue;
                Vector3 v=body.linearVelocity,w=body.angularVelocity;
                var item=shape.GetComponentInParent<PickableObject>();
                if(item!=null&&item.IsHeld) { v=item.HeldPointVelocity(body.worldCenterOfMass);w=item.HeldAngularVelocity; }
                else if(body.isKinematic&&WebWorldSession.ReplicaMotion(body.gameObject,out var net,out var angular)) { v=net;w=angular; }
                motions[body.GetInstanceID()]=new Motion { v=v,w=w,center=body.worldCenterOfMass,time=Time.time };
                if(v.sqrMagnitude<4f&&w.sqrMagnitude<4f) continue;
                // Guest props and remote held objects are kinematic replicas.
                // Unity does not dispatch kinematic/kinematic collision callbacks.
                if(body.isKinematic) foreach(var target in hitShapes) {
                    var c=target.GetComponent<Collider>();
                    if(c==null||!c.enabled||c.isTrigger||!c.bounds.Intersects(shape.bounds)) continue;
                    if(Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,shape,shape.transform.position,shape.transform.rotation,out var normal,out _))
                        ReceiveImpact(target.Part,shape,c.ClosestPoint(shape.bounds.center),normal,SourceVelocity(shape,c.bounds.center),LegFromBone(target.transform));
                }
            }
        }
        Vector3 SourceVelocity(Collider source,Vector3 point)
        {
            var item=source.GetComponentInParent<PickableObject>();
            if(item!=null&&item.IsHeld) return item.HeldPointVelocity(point);
            var body=source.attachedRigidbody;if(body==null) return Vector3.zero;
            if(body.isKinematic&&WebWorldSession.ReplicaMotion(body.gameObject,out var net,out var angular)) return net+Vector3.Cross(angular,point-body.transform.position);
            if(body.GetComponent<MovingImpactMotion>() is {} sampled&&!body.isKinematic) return sampled.PointVelocity(point);
            if(motions.TryGetValue(body.GetInstanceID(),out var m)&&Time.time-m.time<.12f)
                return m.v+Vector3.Cross(m.w,point-m.center);
            return body.GetPointVelocity(point);
        }
        void OnCollisionEnter(Collision collision)=>ProcessCollision(collision);
        void OnCollisionStay(Collision collision)=>ProcessCollision(collision);
        internal void ProcessCollision(Collision collision)
        {
            if(IsDead||collision.collider==null) return;
            foreach(var contact in collision.contacts) {
                var shape=contact.thisCollider.GetComponent<PlayerHitShape>();
                if(shape!=null&&shape.Owner!=this) continue;
                ReceiveImpact(shape!=null?shape.Part:PlayerBodyPart.Torso,collision.collider,contact.point,contact.normal,SourceVelocity(collision.collider,contact.point),LegFromBone(contact.thisCollider.transform));
            }
        }
        internal bool ReceiveImpact(PlayerBodyPart part,Collider source,Vector3 point,Vector3 normal,Vector3 ownVelocity,int leg=0)
        {
            if(replica||IsDead||source==null||controller!=null&&controller.IsPaused||source.attachedRigidbody==null||
                source.transform.IsChildOf(transform)||!float.IsFinite(ownVelocity.sqrMagnitude)||IgnoresSource(part,source)) return false;
            var properties=source.GetComponentInParent<PhysicalDestructible>();
            if(properties==null) return false;
            float incomingSpeed=Vector3.Dot(ownVelocity,normal.normalized);
            if(incomingSpeed<.5f) return false;
            // Require motion into the surface, rather than a grazing contact or
            // an object moving away when the player walks into it.

            int id=properties.GetInstanceID();
            ReceiveSourceShock(id,source.attachedRigidbody.mass,ownVelocity,normal,point);
            float required=RequiredSpeed(properties,part);
            if(ownVelocity.magnitude<required||incomingSpeed<required*.35f) return false;
            if(cooldowns.TryGetValue(id,out float until)&&Time.time<until) return false;
            cooldowns[id]=Time.time+.25f;
            if(cooldowns.Count>512) cooldowns.Clear();
            return ApplyDamage(part,point,ownVelocity,leg);
        }
        bool IgnoresSource(PlayerBodyPart part,Collider source)
        {
            // Sweeps, replica overlaps and host reports must respect the same
            // holder/body exclusions as the physics solver after release.
            bool found=false;
            if(hitShapes!=null) foreach(var tag in hitShapes) if(tag!=null&&tag.Part==part) {
                var target=tag.GetComponent<Collider>();if(target==null||!target.enabled||target.isTrigger) continue;
                if(!MovingImpactMotion.IgnoresContact(source,target)) return false;
                found=true;
            }
            return found;
        }
        internal bool ReceiveNetworkImpact(Impact impact)
        {
            if(impact==null||IsDead||controller!=null&&controller.IsPaused||impact.life!=life||string.IsNullOrEmpty(impact.id)||receivedImpacts.Contains(impact.id)||
                impact.part<0||impact.part>4||!float.IsFinite(impact.velocity.sqrMagnitude)||!float.IsFinite(impact.point.sqrMagnitude)||!float.IsFinite(impact.normal.sqrMagnitude)||
                !float.IsFinite(impact.threshold)||impact.threshold<5f||!float.IsFinite(impact.mass)||impact.mass<0f||impact.mass>1e6f||
                Vector3.Dot(impact.velocity,impact.normal.normalized)<.5f) return false;
            if(receivedImpacts.Count>2048) receivedImpacts.Clear();receivedImpacts.Add(impact.id);
            acknowledgements.Add(impact.id);if(acknowledgements.Count>32) acknowledgements.RemoveAt(0);
            // A guest may already have observed this impact through a moving
            // replica. The host event also covers projectiles stopped by the
            // solver before their next world snapshot is sent.
            var source=WebWorldSession.ObjectOf(impact.source)?.GetComponent<PhysicalDestructible>();
            if(impact.threshold<18f&&(impact.part!=(int)PlayerBodyPart.Head||
                source==null||source.MaterialName!="Iron")) return false;
            if(source!=null) {
                if(source.GetComponentsInChildren<Collider>().Any(c=>c.enabled&&!c.isTrigger&&IgnoresSource((PlayerBodyPart)impact.part,c))) return false;

            }
            Transform hitBone=string.IsNullOrEmpty(impact.bone)?null:model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==impact.bone);
            Vector3 contact=(hitBone!=null?hitBone:transform).TransformPoint(impact.point);
            float mass=source!=null?source.GetComponentInParent<Rigidbody>()?.mass??impact.mass:impact.mass;
            ReceiveSourceShock(source!=null?source.GetInstanceID():impact.source.GetHashCode(),mass,impact.velocity,impact.normal,contact);
            if(impact.velocity.magnitude<impact.threshold||Vector3.Dot(impact.velocity,impact.normal.normalized)<impact.threshold*.35f) return false;
            if(source!=null) {
                int id=source.GetInstanceID();if(cooldowns.TryGetValue(id,out float until)&&Time.time<until) return false;
                cooldowns[id]=Time.time+.25f;
            }
            return ApplyDamage((PlayerBodyPart)impact.part,contact,impact.velocity,LegFromBone(hitBone));
        }
        internal static int LegFromBone(Transform bone) {
            for(var node=bone;node!=null;node=node.parent) {
                if(node.name.StartsWith("Left")&&(node.name.Contains("Hip")||node.name.Contains("Knee")||node.name.Contains("Ankle")||node.name.Contains("Foot"))) return 3;
                if(node.name.StartsWith("Right")&&(node.name.Contains("Hip")||node.name.Contains("Knee")||node.name.Contains("Ankle")||node.name.Contains("Foot"))) return 4;
            }return 0;
        }
        int LimbAt(PlayerBodyPart part,Vector3 point)
        {
            if(part==PlayerBodyPart.LeftArm) return 1;if(part==PlayerBodyPart.RightArm) return 2;
            if(part!=PlayerBodyPart.Legs) return 0;
            var shapes=hitShapes.Where(s=>s!=null&&s.Part==PlayerBodyPart.Legs&&s.GetComponent<Collider>().enabled&&
                (s.transform.parent.name.StartsWith("Left")||s.transform.parent.name.StartsWith("Right")));
            var nearest=shapes.OrderBy(s=>(s.GetComponent<Collider>().ClosestPoint(point)-point).sqrMagnitude)
                .ThenBy(s=>(s.GetComponent<Collider>().bounds.center-point).sqrMagnitude).FirstOrDefault();
            return nearest!=null?(nearest.transform.parent.name.StartsWith("Left")?3:4):(transform.InverseTransformPoint(point).x<0?3:4);
        }
        void SeverLimb(int limb,Vector3 incoming,Vector3 point)
        {
            if(limb<1||limb>4||(severed&(1<<(limb-1)))!=0) return;
            var node=new GameObject("Detached limb "+Guid.NewGuid().ToString("N"));node.transform.SetParent(transform.parent,true);
            node.transform.SetPositionAndRotation(transform.position,transform.rotation);
            var visual=Instantiate(model.gameObject,node.transform,false).transform;visual.gameObject.SetActive(true);
            foreach(var behaviour in visual.GetComponentsInChildren<MonoBehaviour>()) { behaviour.enabled=false;if(behaviour is not PlayerHitShape) Release(behaviour); }
            var piece=node.AddComponent<PlayerCorpse>();piece.Initialize(visual,false,incoming,point,ragdoll,limb,false);
            if(limb<=2) {
                var held=limb==1?avatar.HeldLeftHandItem:avatar.HeldRightHandItem;
                if(held!=null) {
                    if(limb==1) avatar.ReleaseLeftHandItem(Vector3.zero);else avatar.ReleaseRightHandItem(Vector3.zero,false);
                    piece.Grip(held,limb==1);
                }
            }
            severedObjects[limb-1]=piece;piece.RegisterWorld();
            severed|=1<<(limb-1);PlayerDamageMesh.RemoveLimbs(model,1<<(limb-1),privateMeshes);ragdoll.SetMissingLimbs(severed);
            if(limb>=3) foreach(var shape in GetComponentsInChildren<Collider>(true))
                if(shape.name.Contains("Foot BoxCollider")&&shape.name.StartsWith(limb==3?"Left":"Right")) shape.enabled=false;
        }
        bool ApplyDamage(PlayerBodyPart part,Vector3 point,Vector3 ownVelocity,int leg=0)
        {
            avatar.SampleArmClocks();
            hit++;hitBlinkStartedAt=Time.unscaledTime;
            int limb=part==PlayerBodyPart.Legs&&leg>=3&&leg<=4?leg:LimbAt(part,point);
            bool alreadyDisabled=limb==1?(injury&PlayerInjury.LeftArm)!=0:limb==2?(injury&PlayerInjury.RightArm)!=0:limb>=3?IsLegDisabled(limb==3):false;
            if(limb>0&&alreadyDisabled&&++limbHits[limb-1]>3) SeverLimb(limb,ownVelocity,point);
            switch(part) {
                case PlayerBodyPart.LeftArm:
                case PlayerBodyPart.RightArm:
                    bool left=part==PlayerBodyPart.LeftArm;
                    if(++armHits[left?0:1]<3) AddWound(part,point,ownVelocity);
                    else if(!alreadyDisabled) { injury|=left?PlayerInjury.LeftArm:PlayerInjury.RightArm;avatar.DisableDamagedHand(left); }
                    break;
                case PlayerBodyPart.Legs:
                    if(++legHits[limb==3?0:1]>=4) injury|=limb==3?PlayerInjury.LeftLeg:PlayerInjury.RightLeg;
                    if(IsLegDisabled(true)&&IsLegDisabled(false)) injury|=PlayerInjury.Legs;
                    controller.RefreshInjuryLocomotion();
                    foreach(var c in GetComponentsInChildren<Collider>()) if(c.name.Contains("Foot BoxCollider")&&IsLegDisabled(c.name.StartsWith("Left"))) {
                        var ankle=model.GetComponentsInChildren<Transform>().First(t=>t.name==(c.name.StartsWith("Left")?"LeftAnkle":"RightAnkle"));
                        c.transform.SetParent(ankle,true);
                    }
                    break;
                case PlayerBodyPart.Torso:
                    torsoHits++;AddWound(part,point,ownVelocity);controller.RefreshInjuryLocomotion();break;
                case PlayerBodyPart.Head:
                    Die(ownVelocity,point);
                    return true;
            }
            ragdoll.SetInjury(injury,ownVelocity,point);return true;
        }
        void AddWound(PlayerBodyPart part,Vector3 point,Vector3 incoming)
        {
            if(pits.Count>=12) return;
            var shape=hitShapes.Where(s=>s!=null&&s.Part==part).OrderBy(s=>(s.GetComponent<Collider>().ClosestPoint(point)-point).sqrMagnitude).FirstOrDefault();
            var bone=shape!=null?shape.transform.parent:transform;
            var pit=new Pit { id=Guid.NewGuid().ToString("N"),bone=bone!=transform?bone.name:null,point=bone.InverseTransformPoint(point),incoming=incoming };
            pits.Add(pit);CutTorso(model,point,privateMeshes,pit.id,incoming,transform);
        }
        void Update()
        {
            if(model==null) return;
            if(!replica&&IsDead&&GetComponent<CombatNpc>()==null&&Time.unscaledTime>=respawnAt) Respawn();
            if(!replica) { TickShock(Time.deltaTime);ragdoll.RememberPhysicsPose(); }
            for(int i=corpses.Count-1;i>=0;i--) if(corpses[i]==null) corpses.RemoveAt(i);
        }
        void LateUpdate()
        {
            if(model==null) return;
            if(replica) ragdoll.ApplyPose(replicaState?.pose,entire:replicaState!=null&&(replicaState.fullPose||replicaState.shockPhase>0),woundedArms:WoundedArms(replicaState),woundedLegs:WoundedLegs(replicaState));
            else { ragdoll.RestorePhysicsPose();ApplyShockRecoveryPose(); }
        }
        internal static float HitEyelidClosure(float elapsed)
        {
            if(elapsed<0f||elapsed>=.3f) return 0f;
            if(elapsed<.07f) return Mathf.SmoothStep(0f,1f,elapsed/.07f);
            if(elapsed<.11f) return 1f;
            return 1f-Mathf.SmoothStep(0f,1f,(elapsed-.11f)/.19f);
        }
        internal static Texture2D CreateEyelidTexture(bool upper)
        {
            const int size=128;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false) {
                name=upper?"Upper hit eyelid":"Lower hit eyelid",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear
            };
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++) {
                float horizontal=2f*x/(size-1)-1f;
                float edge=.75f+.25f*horizontal*horizontal;
                float fromEdge=upper?1f-(float)y/(size-1):(float)y/(size-1);
                pixels[y*size+x]=new Color(0f,0f,0f,Mathf.Clamp01((edge-fromEdge)*size+1f));
            }
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
        void Die(Vector3 incoming,Vector3 point)
        {
            CancelShock();injury|=PlayerInjury.Dead;respawnAt=Time.unscaledTime+RespawnDelay;
            var left=avatar.HeldLeftHandItem;var right=avatar.HeldRightHandItem;
            bool keepLeft=left!=null&&!ShouldDropHeldItem(UnityEngine.Random.value);
            bool keepRight=right!=null&&(right==left?keepLeft:!ShouldDropHeldItem(UnityEngine.Random.value));
            // Release the room lease, then constrain the same objects to the
            // corpse hands if the death did not make the grip fail.
            if(left!=null) avatar.ReleaseLeftHandItem(Vector3.zero);
            if(right!=null&&right.IsHeld) avatar.ReleaseRightHandItem(Vector3.zero,false);
            var node=new GameObject("Player corpse "+Guid.NewGuid().ToString("N"));node.transform.SetParent(transform.parent,true);
            node.transform.SetPositionAndRotation(transform.position,transform.rotation);
            var visual=Instantiate(model.gameObject,node.transform,false).transform;
            visual.localPosition=model.localPosition;visual.localRotation=model.localRotation;visual.localScale=model.localScale;
            foreach(var behaviour in visual.GetComponentsInChildren<MonoBehaviour>()) {
                behaviour.enabled=false;if(behaviour is not PlayerHitShape) Release(behaviour);
            }
            foreach(var tag in visual.GetComponentsInChildren<PlayerHitShape>()) tag.Owner=null;
            foreach(var collider in visual.GetComponentsInChildren<Collider>()) { collider.gameObject.layer=0;collider.enabled=!collider.isTrigger; }
            var corpse=node.AddComponent<PlayerCorpse>();corpse.Initialize(visual,false,incoming,point,ragdoll);
            if(keepLeft) corpse.Grip(left,true);
            if(keepRight&&right!=left) corpse.Grip(right,false);
            corpses.Add(corpse);while(corpses.Count>3) corpses.RemoveAt(0);
            corpse.RegisterWorld();
            avatar.enabled=false;avatar.CancelRightHandPickup();avatar.CancelRightHandStrike();
            model.gameObject.SetActive(false);controller.SetIncapacitated(true);
        }
        internal static bool ShouldDropHeldItem(float roll)=>roll<.7f;
        internal void Respawn()
        {
            CancelShock();shockCooldowns.Clear();injury=PlayerInjury.None;life++;pits.Clear();torsoHits=0;cooldowns.Clear();severed=0;Array.Clear(limbHits,0,4);Array.Clear(armHits,0,2);Array.Clear(legHits,0,2);Array.Clear(severedObjects,0,4);
            for(int i=0;i<skins.Length;i++) if(skins[i]!=null) { skins[i].sharedMesh=initialMeshes[i];skins[i].enabled=true; }
            ragdoll.ResetPose();model.gameObject.SetActive(true);avatar.enabled=true;
            foreach(var c in GetComponentsInChildren<Collider>()) if(c.name.Contains("Foot BoxCollider")) { c.transform.SetParent(transform,true);c.enabled=true; }
            controller.RespawnAfterDeath();hitBlinkStartedAt=float.NegativeInfinity;
        }
        public State Capture() => new State { shockRemaining=shockRemaining,recoveryRemaining=recoveryRemaining,shockPhase=ShockPhase,fullPose=GetComponent<CombatNpc>()!=null,injury=(int)injury,life=life,hit=hit,severed=severed,torsoHits=torsoHits,limbHits=(int[])limbHits.Clone(),armHits=(int[])armHits.Clone(),legHits=(int[])legHits.Clone(),limbs=severedObjects.Where(c=>c!=null&&!c.IsShattered).Select(c=>c.Capture(false)).ToArray(),pits=pits.ToArray(),
            pose=IsShocked||GetComponent<CombatNpc>()!=null||injury!=PlayerInjury.None||armHits.Any(n=>n>0)||legHits.Any(n=>n>0)?ragdoll.CapturePose():null,corpses=corpses.Where(c=>c!=null&&!c.IsShattered).Select(c=>c.Capture(false)).ToArray(),impacts=WebPlayerPresence.CaptureImpacts(),received=acknowledgements.ToArray() };
        internal static bool CutTorso(Transform model,Vector3 point,List<Mesh> owned,string identity=null,Vector3 incoming=default,Transform owner=null)
        {
            var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s=>s.name=="HumanBody");
            if(skin==null) return false;
            var chips=identity!=null?new List<Mesh>():null;
            var result=PlayerDamageMesh.CutPit(skin,point,.15f,.0375f,chips);
            if(chips!=null) {
                ColorOnlyTextureQuality.GetOriginalAppearance(skin,out var materials,out _);
                for(int i=0;i<chips.Count;i++) BodyFragment.Spawn(chips[i],materials,incoming,identity+":"+i,owner);
            }
            if(result==null) return false;skin.sharedMesh=result;owned.Add(result);return true;
        }
        void OnGUI()
        {
            if(SuppressPlayerOverlay) return;
            if(IsDead) GUI.Box(new Rect(Screen.width/2-120,Screen.height/2-25,240,50),"Respawning…");
            else if(!replica) {
                float closure=HitEyelidClosure(Time.unscaledTime-hitBlinkStartedAt);if(closure<=0f) return;
                if(upperEyelid==null) upperEyelid=CreateEyelidTexture(true);
                if(lowerEyelid==null) lowerEyelid=CreateEyelidTexture(false);
                var color=GUI.color;int depth=GUI.depth;GUI.color=Color.white;GUI.depth=-1000;
                float height=Screen.height*.68f*closure;
                GUI.DrawTexture(new Rect(0,0,Screen.width,height),upperEyelid,ScaleMode.StretchToFill,true);
                GUI.DrawTexture(new Rect(0,Screen.height-height,Screen.width,height),lowerEyelid,ScaleMode.StretchToFill,true);
                GUI.color=color;GUI.depth=depth;
            }
        }
        void OnDestroy() { foreach(var mesh in privateMeshes) Release(mesh);Release(upperEyelid);Release(lowerEyelid); }
        internal static void Release(UnityEngine.Object value) { if(value==null) return;if(Application.isPlaying) Destroy(value);else DestroyImmediate(value); }
    }

    public sealed class CorpseGrip : MonoBehaviour
    {
        FixedJoint joint;
        RopeObject rope;PlayerCorpse bodyCorpse;
        public bool IsAttached=>joint!=null||rope!=null||bodyCorpse!=null;
        public void Attach(Rigidbody hand,Vector3 localPoint,Quaternion localRotation,bool snap)
        {
            Detach();if(hand==null) return;
            bodyCorpse=GetComponent<PlayerCorpse>();
            if(bodyCorpse!=null) { if(!bodyCorpse.BeginGrab(hand.transform,hand.transform.TransformPoint(localPoint))) bodyCorpse=null;return; }
            rope=GetComponent<RopeObject>();
            if(rope!=null) { if(!rope.BeginGrab(hand.transform,hand.transform.TransformPoint(localPoint))) rope=null;return; }
            var body=GetComponent<Rigidbody>();if(body==null) return;
            var corpse=hand.GetComponentInParent<PlayerCorpse>();
            if(corpse!=null) foreach(var itemShape in GetComponentsInChildren<Collider>()) foreach(var bodyShape in corpse.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(itemShape,bodyShape);
            body.solverIterations=32;body.solverVelocityIterations=12;
            hand.solverIterations=Mathf.Max(hand.solverIterations,32);
            hand.solverVelocityIterations=Mathf.Max(hand.solverVelocityIterations,12);
            if(snap) transform.SetPositionAndRotation(hand.transform.TransformPoint(localPoint),hand.rotation*localRotation);
            joint=gameObject.AddComponent<FixedJoint>();joint.connectedBody=hand;joint.autoConfigureConnectedAnchor=true;
            joint.connectedMassScale=.25f;
            joint.enableCollision=false;joint.enablePreprocessing=true;
        }
        public void Detach() {
            if(bodyCorpse!=null) { bodyCorpse.EndGrab(Vector3.zero);bodyCorpse=null; }
            if(rope!=null) { rope.EndGrab(Vector3.zero);rope=null; }
            if(joint!=null) { PlayerDamage.Release(joint);joint=null; }
        }
    }
    public sealed class PlayerCorpse : MonoBehaviour
    {
        PlayerRagdoll rig;bool replica;
        public bool IsShattered { get; private set; }
        float destructibleAfter;
        internal static float DestructionSpeed(PhysicalDestructible source)=>source==null?float.PositiveInfinity:Mathf.Max(8f,source.InitialImpactSpeed(1f,"Flesh"));
        public int Limb { get; private set; }
        public bool IsLimb=>Limb>0;
        public float MassKg=>!IsLimb?FirstPersonController.PlayerMassKg:Limb<=2?4.5f:10f;
        Rigidbody gripAnchor;FixedJoint heldJoint;
        readonly List<Material> materials=new();
        static readonly Dictionary<string,PlayerCorpse> existing=new();
        internal bool WorldRegistered;bool receivedPose;
        public static PlayerCorpse Find(string id)=>!string.IsNullOrEmpty(id)&&existing.TryGetValue(id,out var result)?result:null;
        internal void SetReplica(bool value) { replica=value;rig.SetReplica(value); }
        PlayerDamage.CorpseState state;readonly List<CorpseGrip> grips=new();readonly List<Mesh> meshes=new();
        public void Initialize(Transform model,bool remote,Vector3 incoming=default,Vector3 point=default,PlayerRagdoll reference=null,int limb=0,bool spawnFragments=true)
        {
            Limb=limb;replica=remote;existing[name]=this;
            destructibleAfter=Time.time+.25f;
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if(skin.sharedMesh!=null) { skin.sharedMesh=Instantiate(skin.sharedMesh);meshes.Add(skin.sharedMesh); }
                var sourceSkin=reference!=null?reference.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s=>s.name==skin.name):null;
                ColorOnlyTextureQuality.GetOriginalAppearance(sourceSkin!=null?sourceSkin:skin,out var originals,out _);
                skin.sharedMaterials=originals.Select(m=> { if(m==null) return null;var owned=Instantiate(m);if(owned.HasProperty("_BaseColor")) owned.SetColor("_BaseColor",RiggedPlayerAvatar.PlayerColor);materials.Add(owned);return owned; }).ToArray();
            }
            if(IsLimb) foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var selected=PlayerDamageMesh.LimbMesh(skin,Limb,true);
                if(selected==null) { skin.enabled=false;continue; }skin.sharedMesh=selected;meshes.Add(selected);
            }
            if(IsLimb) {
                foreach(var joint in model.GetComponentsInChildren<CharacterJoint>())
                    if(!PlayerDamageMesh.InLimb(joint.name,Limb)) PlayerDamage.Release(joint);
                foreach(var body in model.GetComponentsInChildren<Rigidbody>()) { body.isKinematic=true;body.useGravity=false;body.detectCollisions=false; }
            }
            rig=gameObject.AddComponent<PlayerRagdoll>();rig.LimbMode=Limb;rig.Initialize(model,null,remote,reference);
            rig.SetMissingLimbs(rig.MissingLimbs);
            rig.SetInjury(PlayerInjury.Dead,incoming,point);
            state=new PlayerDamage.CorpseState { id=gameObject.name,limb=Limb,missing=rig.MissingLimbs,position=transform.position,rotation=transform.rotation };
            var rootBody=gameObject.AddComponent<Rigidbody>();rootBody.mass=MassKg;rootBody.isKinematic=true;rootBody.useGravity=false;
            var pickup=gameObject.AddComponent<PickableObject>();pickup.DisplayName=IsLimb?(Limb==1?"Left arm":Limb==2?"Right arm":Limb==3?"Left leg":"Right leg"):"Dead body";
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                renderer.enabled=IsLimb?renderer.enabled:true;renderer.gameObject.layer=0;renderer.SetPropertyBlock(null);
                if(renderer.name.Contains("Head")) {
                    if(IsLimb||renderer.name.Contains("Shadow")) renderer.enabled=false;
                    else {
                        if(spawnFragments) PlayerDamageMesh.Shatter(renderer,incoming,meshes,name+":"+renderer.name,transform);
                        renderer.enabled=false;
                    }
                }
                else if(renderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly) renderer.enabled=false;
                ColorOnlyTextureQuality.ApplyRendererQuality(renderer);
            }
            if(!IsLimb) foreach(var tag in model.GetComponentsInChildren<PlayerHitShape>())
                if(tag.Part==PlayerBodyPart.Head) foreach(var shape in tag.GetComponents<Collider>()) shape.enabled=false;
            foreach(var body in rig.Bodies) if(body!=null) FragmentMassCenter.Align(body,model);
            FragmentMassCenter.Align(rootBody,model,true);
        }

        static void FitRetainedHeadColliders(Transform model)
        {
            var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s=>s.name=="HumanHead"&&s.enabled);
            if(skin==null) return;
            var baked=new Mesh();
            try {
                skin.BakeMesh(baked,true);var points=baked.vertices;
                foreach(var tag in model.GetComponentsInChildren<PlayerHitShape>()) if(tag.Part==PlayerBodyPart.Head&&tag.GetComponent<BoxCollider>() is {} box) {
                    bool valid=false;var bounds=new Bounds();
                    foreach(var point in points) {
                        var local=box.transform.InverseTransformPoint(skin.transform.TransformPoint(point));
                        if(valid) bounds.Encapsulate(local);else { bounds=new Bounds(local,Vector3.zero);valid=true; }
                    }
                    if(valid) { box.center=bounds.center;box.size=bounds.size;box.enabled=true; }
                }
            } finally { PlayerDamage.Release(baked); }
        }
        public void Grip(PickableObject item,bool left)
        {
            var hand=rig.Hand(left);if(hand==null) return;
            var grip=item.GetComponent<CorpseGrip>()??item.gameObject.AddComponent<CorpseGrip>();
            var p=hand.transform.InverseTransformPoint(item.Rope!=null?item.Rope.GripPoint:item.transform.position);var q=Quaternion.Inverse(hand.rotation)*item.transform.rotation;
            if(left) { state.left=WebWorldSession.IdOf(item.gameObject);state.lp=p;state.lq=q; }
            else { state.right=WebWorldSession.IdOf(item.gameObject);state.rp=p;state.rq=q; }
            // Attach immediately for the dying actor, including a guest player.
            // The host recreates this constraint from the corpse state in the room.
            grip.Attach(hand,p,q,false);grips.Add(grip);
        }
        internal void ProcessCollision(Collision collision)
        {
            if(IsShattered||collision.collider==null||collision.collider.transform.IsChildOf(transform)) return;
            var source=PhysicalDestructible.Resolve(collision.collider);
            if(source==null) return;
            Vector3 point=collision.contactCount>0?collision.GetContact(0).point:transform.position;
            TryShatterFromImpact(source,point,collision.relativeVelocity);
        }
        internal bool TryShatterFromImpact(PhysicalDestructible source,Vector3 point,Vector3 relativeVelocity)
        {
            if(IsShattered||rig==null||Time.time<destructibleAfter||source==null||source.transform.IsChildOf(transform)||
                !float.IsFinite(point.sqrMagnitude)||!float.IsFinite(relativeVelocity.sqrMagnitude)||relativeVelocity.magnitude<DestructionSpeed(source)) return false;
            if(WebWorldSession.ForwardCorpseImpact(source,this,point,relativeVelocity)) return true;
            if(!WebWorldSession.IsPhysicsHost) return false;
            var pieces=CorpseDestruction.Bake(rig.Model,transform.position,IsLimb?3:4,out var appearance,
                separateLimbs:!IsLimb,missingLimbs:rig.MissingLimbs);
            if(pieces.Count==0) return false;
            RegisterWorld();IsShattered=true;
            var pickup=GetComponent<PickableObject>();
            if(pickup!=null&&pickup.IsHeld) pickup.Release(Vector3.zero,pickup.HeldHand);
            EndGrab(Vector3.zero);
            GetComponent<CorpseGrip>()?.Detach();
            foreach(var grip in grips) if(grip!=null) { grip.Detach();PlayerDamage.Release(grip); }
            grips.Clear();
            rig.enabled=false;
            foreach(var joint in GetComponentsInChildren<Joint>()) { joint.connectedBody=null;joint.breakForce=0;PlayerDamage.Release(joint); }
            foreach(var collider in GetComponentsInChildren<Collider>()) collider.enabled=false;
            foreach(var body in GetComponentsInChildren<Rigidbody>()) { body.detectCollisions=false;body.isKinematic=true; }
            foreach(var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled=false;
            for(int i=0;i<pieces.Count;i++) BodyFragment.Spawn(pieces[i],appearance,relativeVelocity,name+":complete-shatter:"+i,transform);
            WebWorldSession.RemoveRoomObject(gameObject);
            return true;
        }
        public Vector3 GripPoint=>gripAnchor!=null?gripAnchor.position:NearestPoint(transform.position);
        public Vector3 LimbEnd=>rig.Bodies.LastOrDefault(b=>b!=null)?.position??transform.position;
        internal void ApplyMotion(float[] motion)=>rig.ApplyMotion(motion);
        public PlayerDamage.CorpseState Capture(bool includeMotion=true) {
            if(!string.IsNullOrEmpty(state.left)&&WebWorldSession.ObjectOf(state.left)?.GetComponent<CorpseGrip>()==null) state.left="";
            if(!string.IsNullOrEmpty(state.right)&&WebWorldSession.ObjectOf(state.right)?.GetComponent<CorpseGrip>()==null) state.right="";
            return new PlayerDamage.CorpseState { id=state.id,left=state.left,right=state.right,lp=state.lp,rp=state.rp,lq=state.lq,rq=state.rq,
                limb=Limb,missing=rig.MissingLimbs,position=transform.position,rotation=transform.rotation,pose=rig.CapturePose(),motion=includeMotion?rig.CaptureMotion():null };
        }
        public void Apply(PlayerDamage.CorpseState value)
        {
            state=value;receivedPose=true;rig.ApplyPose(state.pose,true);rig.ApplyMotion(state.motion);
            TryRemoteGrip(value.left,true,value.lp,value.lq);TryRemoteGrip(value.right,false,value.rp,value.rq);
        }
        internal void RegisterWorld()=>WebWorldSession.RegisterCorpse(this);
        internal void ApplyPresence(PlayerDamage.CorpseState value)
        {
            if(!WorldRegistered||!receivedPose) Apply(value);
            RegisterWorld();
        }
        internal bool Changed(PlayerDamage.CorpseState previous)
        {
            var next=Capture();if(previous?.pose==null||next.pose.Length!=previous.pose.Length||previous.left!=next.left||previous.right!=next.right) return true;
            for(int i=0;i<next.pose.Length;i++) if(Mathf.Abs(next.pose[i]-previous.pose[i])>.003f) return true;
            return false;
        }
        public Vector3 NearestPoint(Vector3 point)
        {
            Vector3 closest=transform.position;float best=float.PositiveInfinity;
            foreach(var c in GetComponentsInChildren<Collider>()) if(c.enabled&&!c.isTrigger) {
                var p=c.ClosestPoint(point);float d=(p-point).sqrMagnitude;if(d<best) { best=d;closest=p; }
            }
            return closest;
        }
        public bool BeginGrab(Transform hand,Vector3 point)
        {
            EndGrab(Vector3.zero);SetReplica(false);
            rig.WakeSimulation();
            var bone=rig.Bodies.Where(b=>b!=null).OrderBy(b=>(b.worldCenterOfMass-point).sqrMagnitude).FirstOrDefault();
            if(bone==null) return false;
            var anchor=new GameObject("Corpse hand constraint");anchor.transform.SetParent(hand,false);anchor.transform.position=point;
            gripAnchor=anchor.AddComponent<Rigidbody>();gripAnchor.isKinematic=true;gripAnchor.useGravity=false;
            heldJoint=bone.gameObject.AddComponent<FixedJoint>();heldJoint.connectedBody=gripAnchor;
            heldJoint.anchor=bone.transform.InverseTransformPoint(point);heldJoint.autoConfigureConnectedAnchor=true;
            heldJoint.enablePreprocessing=false;heldJoint.enableCollision=false;
            var owner=hand.GetComponentInParent<FirstPersonController>();
            if(owner!=null) foreach(var a in GetComponentsInChildren<Collider>()) foreach(var b in owner.GetComponentsInChildren<Collider>()) if(a!=b) Physics.IgnoreCollision(a,b);
            return true;
        }
        public void TransferGrab(Transform hand) { if(gripAnchor!=null) gripAnchor.transform.SetParent(hand,true); }
        public void EndGrab(Vector3 velocity)
        {
            if(heldJoint!=null) { PlayerDamage.Release(heldJoint);heldJoint=null; }
            if(gripAnchor!=null) { PlayerDamage.Release(gripAnchor.gameObject);gripAnchor=null; }
            if(!replica&&rig!=null) {
                if(velocity.sqrMagnitude>0) rig.WakeSimulation();
                foreach(var b in rig.Bodies) if(b!=null&&!b.isKinematic) b.linearVelocity+=Vector3.ClampMagnitude(velocity,60);
            }
        }
        internal static PlayerCorpse Create(PlayerDamage.CorpseState value,Transform parent)
        {
            var found=Find(value.id);if(found!=null) return found;
            var node=new GameObject(value.id);node.transform.SetParent(parent,false);node.transform.SetPositionAndRotation(value.position,value.rotation);
            var asset=Resources.Load<GameObject>("Player/HumanRiggedEdited")??Resources.Load<GameObject>("Player/HumanRigged");
            var model=Instantiate(asset,node.transform,false).transform;
            foreach(var animator in model.GetComponentsInChildren<Animator>()) animator.enabled=false;
            foreach(var c in model.GetComponentsInChildren<Collider>()) { c.enabled=false;PlayerDamage.Release(c); }
            foreach(var b in model.GetComponentsInChildren<Rigidbody>()) { b.detectCollisions=false;PlayerDamage.Release(b); }
            RiggedPlayerAvatar.EnsureWristJoints(model.gameObject);PlayerRagdoll.PrepareRemoteNeck(model);
            node.AddComponent<PlayerRigColliders>().Initialize(model,0);
            var corpse=node.AddComponent<PlayerCorpse>();corpse.Initialize(model,true,limb:value.limb,spawnFragments:false);
            if(value.missing!=0) { PlayerDamageMesh.RemoveLimbs(model,value.missing,corpse.meshes);corpse.rig.SetMissingLimbs(value.missing); }
            corpse.Apply(value);return corpse;
        }
        void TryRemoteGrip(string id,bool left,Vector3 p,Quaternion q)
        {
            if(!WebWorldSession.IsPhysicsHost||string.IsNullOrEmpty(id)) return;
            var item=WebWorldSession.ObjectOf(id)?.GetComponent<PickableObject>();
            if(item==null||item.IsHeld||!WebWorldSession.IsUnclaimed(item.gameObject)) return;
            var grip=item.GetComponent<CorpseGrip>();if(grip!=null&&grip.IsAttached) return;
            if(grip==null) grip=item.gameObject.AddComponent<CorpseGrip>();grip.Attach(rig.Hand(left),p,q,true);if(!grips.Contains(grip)) grips.Add(grip);
        }
        void Update() {
            RegisterWorld();
            if(!replica) {
                // Rebase the network/pickup root without moving any ragdoll bone.
                var waist=rig.Bodies.FirstOrDefault(b=>b!=null);
                if(waist!=null) { var pose=rig.CapturePose();transform.position=waist.position;rig.ApplyPose(pose,true); }
                GetComponent<PickableObject>()?.TrackMovement();
            }
            TryRemoteGrip(state.left,true,state.lp,state.lq);TryRemoteGrip(state.right,false,state.rp,state.rq);
        }
        void OnDestroy() {
            if(existing.TryGetValue(name,out var found)&&found==this) existing.Remove(name);
            EndGrab(Vector3.zero);foreach(var material in materials) PlayerDamage.Release(material);
            foreach(var grip in grips) if(grip!=null) { grip.Detach();PlayerDamage.Release(grip); }foreach(var mesh in meshes) PlayerDamage.Release(mesh); }
    }
}
