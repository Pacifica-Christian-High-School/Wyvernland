using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wyvernland
{
    [Flags] public enum PlayerInjury { None=0, LeftArm=1, RightArm=2, Legs=4, Dead=8, LeftLeg=16, RightLeg=32 }
    public enum PlayerBodyPart { Torso, LeftArm, RightArm, Legs, Head }

    // The render bones are also the physics bones: no second skeleton can drift
    // away from the skin, hand grips, or fitted collision shapes.
    public sealed class PlayerRagdoll : MonoBehaviour
    {
        internal const float MaximumJointAngularSpeed=.9f;
        internal static readonly string[] BoneNames={"Waist","Pelvis","Neck","LeftShoulder","LeftElbow",
            "LeftWrist","RightShoulder","RightElbow","RightWrist","LeftHip","LeftKnee","LeftAnkle",
            "RightHip","RightKnee","RightAnkle"};
        readonly List<Rigidbody> bodies=new();
        readonly List<Transform> bones=new();
        readonly List<Vector3> restPositions=new(),restScales=new(),jointAxes=new();
        readonly HashSet<int> configuredJoints=new();
        readonly List<Quaternion> restRotations=new();
        readonly List<Vector3> savedPositions=new(),savedScales=new();
        readonly List<Quaternion> savedRotations=new();
        public PlayerInjury Injury { get; private set; }
        public bool Replica { get; private set; }
        internal bool FullBodyStun { get; private set; }
        internal bool SimulationSleeping { get; private set; }
        static readonly HashSet<PlayerRagdoll> movingRigs=new();
        float settledSince=-1,nextActivityCheck;
        Vector3 previousRootPosition;Quaternion previousRootRotation;
        internal int LimbMode,MissingLimbs;
        internal void SetReplica(bool replica) { if(Replica!=replica) WakeSimulation();Replica=replica;SetInjury(Injury); }
        internal IReadOnlyList<Rigidbody> Bodies=>bodies;
        internal Transform Model { get; private set; }
        internal static Mesh PrepareRemoteNeck(Transform model)
        {
            var head=model.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r=>r.name=="HumanHead");
            var waist=model.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="Waist");
            if(head==null||waist==null||model.GetComponentsInChildren<Transform>().Any(t=>t.name=="Neck")) return null;
            var bones=head.bones;int index=Array.IndexOf(bones,waist);if(index<0) return null;
            var neck=new GameObject("Neck").transform;var bounds=head.sharedMesh.bounds;
            neck.SetPositionAndRotation(head.transform.TransformPoint(new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.12f,bounds.center.z)),waist.rotation);
            neck.SetParent(waist,true);
            var mesh=UnityEngine.Object.Instantiate(head.sharedMesh);var poses=mesh.bindposes;
            bones[index]=neck;poses[index]=neck.worldToLocalMatrix*head.transform.localToWorldMatrix;
            mesh.bindposes=poses;head.sharedMesh=mesh;head.bones=bones;return mesh;
        }
        public static PlayerBodyPart Part(string name) => name.Contains("Neck")||name.Contains("Head")?PlayerBodyPart.Head:
            name.StartsWith("Left")&&(name.Contains("Shoulder")||name.Contains("Elbow")||name.Contains("Wrist")||name.Contains("Fingers")||name.Contains("Thumb"))?PlayerBodyPart.LeftArm:
            name.StartsWith("Right")&&(name.Contains("Shoulder")||name.Contains("Elbow")||name.Contains("Wrist")||name.Contains("Fingers")||name.Contains("Thumb"))?PlayerBodyPart.RightArm:
            name.Contains("Pelvis")||name.Contains("Hip")||name.Contains("Knee")||name.Contains("Ankle")?PlayerBodyPart.Legs:PlayerBodyPart.Torso;
        bool IsMissing(string name) { for(int i=1;i<=4;i++) if((MissingLimbs&(1<<(i-1)))!=0&&PlayerDamageMesh.InLimb(name,i)) return true;return false; }
        bool Affected(string name,PlayerInjury injury) => (LimbMode==0||PlayerDamageMesh.InLimb(name,LimbMode))&&!IsMissing(name)&&(FullBodyStun||(injury&PlayerInjury.Dead)!=0||
            Part(name)==PlayerBodyPart.LeftArm&&(injury&PlayerInjury.LeftArm)!=0||
            Part(name)==PlayerBodyPart.RightArm&&(injury&PlayerInjury.RightArm)!=0||
            Part(name)==PlayerBodyPart.Legs&&((injury&PlayerInjury.Legs)!=0||
                name.StartsWith("Left")&&(injury&PlayerInjury.LeftLeg)!=0||name.StartsWith("Right")&&(injury&PlayerInjury.RightLeg)!=0));
        public void Initialize(Transform model,PlayerDamage owner,bool replica=false,PlayerRagdoll reference=null)
        {
            if(bones.Count>0) throw new InvalidOperationException("Ragdoll already initialized");
            Model=model;Replica=replica;if(reference!=null&&LimbMode==0) MissingLimbs=reference.MissingLimbs;
            previousRootPosition=transform.position;previousRootRotation=transform.rotation;
            var all=model.GetComponentsInChildren<Transform>(true);
            foreach(string name in BoneNames) {
                Transform bone=Array.Find(all,t=>t.name==name);
                bones.Add(bone);bodies.Add(null);
                restPositions.Add(bone!=null?bone.localPosition:Vector3.zero);
                restScales.Add(bone!=null?bone.localScale:Vector3.one);
                restRotations.Add(bone!=null?bone.localRotation:Quaternion.identity);
                savedPositions.Add(Vector3.zero);savedScales.Add(Vector3.one);savedRotations.Add(Quaternion.identity);
            }
            foreach(var bone in bones) {
                Vector3 worldAxis=model.right;
                if(bone!=null) {
                    string child=bone.name.Contains("Elbow")?bone.name.Replace("Elbow","Wrist"):bone.name.Contains("Knee")?bone.name.Replace("Knee","Ankle"):bone.name.Contains("Shoulder")?bone.name.Replace("Shoulder","Elbow"):null;
                    var end=child!=null?Array.Find(all,t=>t.name==child):null;
                    if(end!=null) {
                        var limb=(end.position-bone.position).normalized;
                        worldAxis=bone.name.Contains("Shoulder")?limb:Vector3.Cross(limb,bone.name.Contains("Knee")?-model.forward:model.forward).normalized;
                    } else if(bone.name=="Neck") worldAxis=model.up;
                    else if(bone.name.Contains("Wrist")&&bone.parent!=null) worldAxis=(bone.position-bone.parent.position).normalized;
                }
                jointAxes.Add(bone!=null?bone.InverseTransformDirection(worldAxis).normalized:Vector3.right);
            }
            if(reference!=null&&reference.bones.Count==bones.Count) for(int i=0;i<bones.Count;i++) {
                restRotations[i]=reference.restRotations[i];jointAxes[i]=reference.jointAxes[i];
            }
            ConfigureShapes(model,owner);
        }
        void EnsureBodies(PlayerInjury injury)
        {
            for(int index=0;index<bones.Count;index++) {
                var bone=bones[index];if(bone==null||!Affected(bone.name,injury)||bodies[index]!=null) continue;
                string name=bone.name;
                var body=bone.GetComponent<Rigidbody>();if(body==null) body=bone.gameObject.AddComponent<Rigidbody>();
                body.isKinematic=true;body.useGravity=false;body.detectCollisions=true;body.mass=name=="Waist"?22:name=="Pelvis"?9:2.5f;
                body.interpolation=RigidbodyInterpolation.None;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                body.maxDepenetrationVelocity=.5f;body.maxAngularVelocity=MaximumJointAngularSpeed;body.angularDamping=2.5f;body.solverIterations=16;body.solverVelocityIterations=8;
                bodies[index]=body;
                var collision=bone.GetComponent<PlayerBoneCollision>();
                if(collision==null||!collision.enabled) collision=bone.gameObject.AddComponent<PlayerBoneCollision>();
                collision.Owner=Owner;collision.Corpse=GetComponent<PlayerCorpse>();
            }
            foreach(var body in bodies) {
                if(body==null) continue;
                var parent=body.transform.parent;Rigidbody connection=null;
                while(parent!=null) { connection=LimbMode==0||PlayerDamageMesh.InLimb(parent.name,LimbMode)?parent.GetComponent<Rigidbody>():null;if(connection!=null) break;parent=parent.parent; }
                if(connection==body||(GetComponent<PlayerCorpse>()!=null||FullBodyStun)&&connection==GetComponent<Rigidbody>()) connection=null;
                if(connection==null) {
                    var detached=body.GetComponent<CharacterJoint>();
                    if(detached!=null) {
                        // Runtime destruction is deferred; prevent the severed
                        // shoulder/hip from staying constrained to the old body.
                        detached.breakForce=0;detached.breakTorque=0;
                        PlayerDamage.Release(detached);
                    }
                    continue;
                }
                var joint=body.GetComponent<CharacterJoint>();
                if(joint==null) { joint=body.gameObject.AddComponent<CharacterJoint>();joint.anchor=Vector3.zero; }
                if(joint.connectedBody!=connection) { joint.connectedBody=connection;configuredJoints.Remove(joint.GetInstanceID()); }
                // Capture the anatomical pivot once when ragdoll begins. The
                // connected anchor then stays at that local position instead of
                // being recomputed as the limbs move under physics.
                if(configuredJoints.Contains(joint.GetInstanceID())) continue;
                joint.anchor=Vector3.zero;
                joint.connectedAnchor=connection.transform.InverseTransformPoint(body.position);
                joint.autoConfigureConnectedAnchor=false;
                if(configuredJoints.Add(joint.GetInstanceID())) ConfigureJoint(joint,bones.IndexOf(body.transform));
            }
        }
        static SoftJointLimit Limit(float angle)=>new SoftJointLimit { limit=angle,contactDistance=1,bounciness=0 };
        internal static float TwistAngle(Quaternion delta,Vector3 axis)
        {
            float projection=Vector3.Dot(new Vector3(delta.x,delta.y,delta.z),axis.normalized);
            return Mathf.DeltaAngle(0,2*Mathf.Atan2(projection,delta.w)*Mathf.Rad2Deg);
        }
        void ConfigureJoint(CharacterJoint joint,int index)
        {
            string name=bones[index].name;float low=-18,high=18,swing1=14,swing2=14;
            if(name.Contains("Elbow")) { low=0;high=80;swing1=swing2=2; }
            else if(name.Contains("Knee")) { low=0;high=85;swing1=swing2=2; }
            else if(name.Contains("Shoulder")) { low=-35;high=35;swing1=45;swing2=35; }
            else if(name.Contains("Hip")) { low=-55;high=18;swing1=18;swing2=14; }
            else if(name.Contains("Wrist")) { low=-24;high=24;swing1=14;swing2=14; }
            else if(name.Contains("Ankle")) { low=-14;high=20;swing1=7;swing2=6; }
            else if(name=="Neck") { low=-24;high=24;swing1=14;swing2=14; }
            // Limits are measured from the imported anatomical rest pose, not
            // the arbitrary crouch/reach pose at the instant of the impact.
            float offset=TwistAngle(Quaternion.Inverse(restRotations[index])*bones[index].localRotation,jointAxes[index]);
            joint.axis=jointAxes[index];
            joint.swingAxis=Vector3.Cross(joint.axis,Mathf.Abs(Vector3.Dot(joint.axis,Vector3.up))<.9f?Vector3.up:Vector3.forward).normalized;
            // PhysX character-joint twist is opposite to the bone local rotation.
            joint.lowTwistLimit=Limit(Mathf.Clamp(offset-high,-177,177));joint.highTwistLimit=Limit(Mathf.Clamp(offset-low,-177,177));
            joint.swing1Limit=Limit(swing1);joint.swing2Limit=Limit(swing2);
            joint.enableProjection=true;joint.projectionDistance=.008f;joint.projectionAngle=2;
            joint.enablePreprocessing=true;joint.enableCollision=false;
        }
        PlayerDamage Owner;
        void ConfigureShapes(Transform model,PlayerDamage owner)
        {
            Owner=owner;var shapes=model.GetComponentsInChildren<Collider>(true);
            foreach(var shape in shapes) {
                var tag=shape.GetComponent<PlayerHitShape>()??shape.gameObject.AddComponent<PlayerHitShape>();
                tag.Owner=owner;tag.Part=Part(shape.transform.parent!=null?shape.transform.parent.name:shape.name);
                if(LimbMode>0&&!PlayerDamageMesh.InLimb(shape.transform.parent!=null?shape.transform.parent.name:shape.name,LimbMode)) shape.enabled=false;
                foreach(var other in shapes) if(other!=shape) Physics.IgnoreCollision(shape,other);
            }
        }
        internal void SetMissingLimbs(int mask)
        {
            MissingLimbs=mask;
            foreach(var shape in Model.GetComponentsInChildren<Collider>(true)) {
                string bone=shape.transform.parent!=null?shape.transform.parent.name:shape.name;
                bool missing=IsMissing(bone);
                shape.enabled=!missing&&(LimbMode==0||PlayerDamageMesh.InLimb(bone,LimbMode))&&
                    !(GetComponent<PlayerCorpse>()!=null&&Part(bone)==PlayerBodyPart.Head);
            }
            SetInjury(Injury);
        }
        public void SetInjury(PlayerInjury injury,Vector3 impulse=default,Vector3 point=default)
        {
            bool wasSleeping=SimulationSleeping;
            if(injury!=Injury||impulse.sqrMagnitude>0) { WakeSimulation();settledSince=-1;nextActivityCheck=0; }
            Injury=injury;
            if(injury!=PlayerInjury.None||FullBodyStun) EnsureBodies(injury);
            for(int i=0;i<bodies.Count;i++) {
                var body=bodies[i];if(body==null) continue;
                bool active=Affected(bones[i].name,injury),was=body.isKinematic||wasSleeping;
                body.isKinematic=Replica||!active||SimulationSleeping;body.useGravity=!Replica&&active&&!SimulationSleeping;
                if(active&&was&&!Replica&&!SimulationSleeping) { body.linearVelocity=Vector3.ClampMagnitude(impulse*.12f,5f);body.AddForceAtPosition(impulse*.08f,point,ForceMode.Impulse); }
            }
            RefreshSelfCollisions();
        }
        internal void SetFullBodyStun(bool active)
        { FullBodyStun=active;SetInjury(Injury); }
        internal void ApplyShock(Vector3 impulse,Vector3 point)
        {
            WakeSimulation();float mass=bodies.Where(b=>b!=null&&!b.isKinematic).Sum(b=>b.mass);
            foreach(var body in bodies) if(body!=null&&!body.isKinematic)
                body.AddForceAtPosition(impulse*(body.mass/Mathf.Max(.001f,mass)),point,ForceMode.Impulse);
        }
        internal void RefreshSelfCollisions()
        {
            var shapes=Model.GetComponentsInChildren<Collider>(true).Where(c=>c.GetComponent<PlayerHitShape>()!=null).ToArray();
            bool Connected(Rigidbody body,Rigidbody other) {
                if(body==null||other==null) return false;
                var joint=body.GetComponent<CharacterJoint>();
                return joint!=null&&joint.connectedBody==other;
            }
            // The locomotion capsule overlaps the fitted ragdoll at rest. Its
            // solver must never push the body upward from inside itself.
            foreach(var movementShape in GetComponentsInChildren<Collider>(true))
                if(!movementShape.transform.IsChildOf(Model)&&
                    movementShape.GetComponentInParent<PickableObject>()==null)
                    foreach(var shape in shapes) if(movementShape!=shape)
                        Physics.IgnoreCollision(movementShape,shape,true);
            int Region(Collider c) {
                string name=c.transform.parent!=null?c.transform.parent.name:c.name;
                for(int limb=1;limb<=4;limb++) if(PlayerDamageMesh.InLimb(name,limb)) return limb;
                return name.Contains("Neck")||name.Contains("Head")?5:0;
            }
            for(int i=0;i<shapes.Length;i++) for(int j=i+1;j<shapes.Length;j++) {
                var a=shapes[i];var b=shapes[j];var ab=a.attachedRigidbody;var bb=b.attachedRigidbody;
                bool connected=Connected(ab,bb)||Connected(bb,ab);
                bool collide=!a.isTrigger&&!b.isTrigger&&Region(a)!=Region(b)&&
                    ab!=bb&&ab!=null&&bb!=null&&!connected;
                Physics.IgnoreCollision(a,b,!collide);
            }
        }

        internal void WakeSimulation()
        {
            if(!SimulationSleeping) return;
            SimulationSleeping=false;settledSince=-1;nextActivityCheck=0;
            for(int i=0;i<bodies.Count;i++) if(bodies[i]!=null) {
                bool active=!Replica&&Affected(bones[i].name,Injury);
                bodies[i].isKinematic=!active;bodies[i].useGravity=active;
                if(active) bodies[i].WakeUp();
            }
        }

        void FixedUpdate()=>UpdateSimulationActivity(Time.time);
        internal void UpdateSimulationActivity(float now)
        {
            if(Replica||Injury==PlayerInjury.None&&!FullBodyStun||Model==null||!Model.gameObject.activeInHierarchy) { movingRigs.Remove(this);return; }
            bool ownMotion=(transform.position-previousRootPosition).sqrMagnitude>.000001f||
                Quaternion.Angle(transform.rotation,previousRootRotation)>.05f;
            previousRootPosition=transform.position;previousRootRotation=transform.rotation;
            foreach(var body in bodies) if(body!=null&&!body.isKinematic&&
                (body.linearVelocity.sqrMagnitude>.0025f||body.angularVelocity.sqrMagnitude>.0025f)) { ownMotion=true;break; }
            foreach(var body in bodies) if(body!=null&&!body.isKinematic)
                body.angularVelocity=Vector3.ClampMagnitude(body.angularVelocity,MaximumJointAngularSpeed);
            if(ownMotion) movingRigs.Add(this);else movingRigs.Remove(this);
            var pickup=GetComponent<PickableObject>();bool held=pickup!=null&&pickup.IsHeld;
            if(ownMotion||held) { WakeSimulation();settledSince=-1;return; }
            if(now<nextActivityCheck) return;nextActivityCheck=now+.1f;
            bool nearby=MovingImpactMotion.HasMovingObjectWithin(transform.position,10f,GetComponent<Rigidbody>());
            if(!nearby) foreach(var rig in movingRigs) if(rig!=null&&rig!=this&&(rig.transform.position-transform.position).sqrMagnitude<=100f) { nearby=true;break; }
            if(nearby) { WakeSimulation();settledSince=-1;return; }
            if(SimulationSleeping) return;
            if(settledSince<0) { settledSince=now;return; }
            if(now-settledSince<.35f) return;
            SimulationSleeping=true;movingRigs.Remove(this);
            foreach(var body in bodies) if(body!=null) { body.isKinematic=true;body.useGravity=false; }
        }
        void OnDisable()=>movingRigs.Remove(this);
        void OnDestroy()=>movingRigs.Remove(this);
        internal void RememberPhysicsPose()
        {
            for(int i=0;i<bones.Count;i++) if(bones[i]!=null&&Affected(bones[i].name,Injury)) {
                savedPositions[i]=bones[i].position;savedRotations[i]=bones[i].rotation;savedScales[i]=bones[i].localScale;
            }
        }
        internal void RestorePhysicsPose()
        {
            for(int i=0;i<bones.Count;i++) if(bones[i]!=null&&Affected(bones[i].name,Injury)) {
                bones[i].SetPositionAndRotation(savedPositions[i],savedRotations[i]);bones[i].localScale=savedScales[i];
            }
        }
        public float[] CapturePose()
        {
            var pose=new float[bones.Count*7];
            for(int i=0;i<bones.Count;i++) if(bones[i]!=null) { int o=i*7;var p=bones[i].position;var q=bones[i].rotation;
                pose[o]=p.x;pose[o+1]=p.y;pose[o+2]=p.z;pose[o+3]=q.x;pose[o+4]=q.y;pose[o+5]=q.z;pose[o+6]=q.w; }
            return pose;
        }
        internal float[] CaptureMotion()
        {
            var values=new float[bodies.Count*6];for(int i=0;i<bodies.Count;i++) if(bodies[i]!=null&&!bodies[i].isKinematic) {
                var v=bodies[i].linearVelocity;var w=bodies[i].angularVelocity;int o=i*6;
                values[o]=v.x;values[o+1]=v.y;values[o+2]=v.z;values[o+3]=w.x;values[o+4]=w.y;values[o+5]=w.z;
            }return values;
        }
        internal void ApplyMotion(float[] values)
        {
            if(Replica||values==null||values.Length!=bodies.Count*6) return;
            if(values.Any(v=>Mathf.Abs(v)>.05f)) WakeSimulation();
            for(int i=0;i<bodies.Count;i++) if(bodies[i]!=null&&!bodies[i].isKinematic) {
                int o=i*6;var v=new Vector3(values[o],values[o+1],values[o+2]);var w=new Vector3(values[o+3],values[o+4],values[o+5]);
                if(float.IsFinite(v.sqrMagnitude)&&float.IsFinite(w.sqrMagnitude)) { bodies[i].linearVelocity=Vector3.ClampMagnitude(v,1000);bodies[i].angularVelocity=Vector3.ClampMagnitude(w,MaximumJointAngularSpeed); }
            }
        }
        public void ApplyPose(float[] pose,bool entire=false,int woundedArms=0,int woundedLegs=0)
        {
            if(pose==null||pose.Length!=bones.Count*7) return;
            for(int i=0;i<bones.Count;i++) if(bones[i]!=null&&(entire||Affected(bones[i].name,Injury)||
                (woundedArms&1)!=0&&Part(bones[i].name)==PlayerBodyPart.LeftArm||(woundedArms&2)!=0&&Part(bones[i].name)==PlayerBodyPart.RightArm||
                Part(bones[i].name)==PlayerBodyPart.Legs&&((woundedLegs&1)!=0&&bones[i].name.StartsWith("Left")||(woundedLegs&2)!=0&&bones[i].name.StartsWith("Right")))) {
                int o=i*7;var p=new Vector3(pose[o],pose[o+1],pose[o+2]);var q=new Quaternion(pose[o+3],pose[o+4],pose[o+5],pose[o+6]);
                float length=Quaternion.Dot(q,q);
                if(!float.IsFinite(p.sqrMagnitude)||length<.5f||!float.IsFinite(length)) continue;
                bones[i].SetPositionAndRotation(p,q.normalized);
            }
        }
        public void ResetPose()
        {
            FullBodyStun=false;MissingLimbs=0;SetMissingLimbs(0);SetInjury(PlayerInjury.None);configuredJoints.Clear();
            for(int i=0;i<bones.Count;i++) if(bones[i]!=null) {
                PlayerDamage.Release(bones[i].GetComponent<CharacterJoint>());PlayerDamage.Release(bodies[i]);bodies[i]=null;
                bones[i].localPosition=restPositions[i];bones[i].localRotation=restRotations[i];bones[i].localScale=restScales[i];
            }
        }
        Vector3[] recoveryPositions,recoveryScales;
        Quaternion[] recoveryRotations;
        internal void ResetAnimatedBones()
        {
            for(int i=0;i<bones.Count;i++) if(bones[i]!=null&&!Affected(bones[i].name,Injury)) {
                bones[i].localPosition=restPositions[i];bones[i].localRotation=restRotations[i];bones[i].localScale=restScales[i];
            }
        }
        internal void CaptureRecoveryPose()
        {
            recoveryPositions=bones.Select(b=>b!=null?b.localPosition:Vector3.zero).ToArray();
            recoveryScales=bones.Select(b=>b!=null?b.localScale:Vector3.one).ToArray();
            recoveryRotations=bones.Select(b=>b!=null?b.localRotation:Quaternion.identity).ToArray();
        }
        internal void BlendRecoveryPose(float blend)
        {
            if(recoveryPositions==null) return;
            for(int i=0;i<bones.Count;i++) if(bones[i]!=null&&!Affected(bones[i].name,Injury)) {
                bones[i].localPosition=Vector3.Lerp(recoveryPositions[i],bones[i].localPosition,blend);
                bones[i].localRotation=Quaternion.Slerp(recoveryRotations[i],bones[i].localRotation,blend);
                bones[i].localScale=Vector3.Lerp(recoveryScales[i],bones[i].localScale,blend);
            }
        }
        public Rigidbody Hand(bool left) => bodies[left?5:8];
    }
    public sealed class PlayerHitShape : MonoBehaviour { public PlayerDamage Owner;public PlayerBodyPart Part; }
    public sealed class PlayerBoneCollision : MonoBehaviour
    {
        public PlayerDamage Owner;
        public PlayerCorpse Corpse;
        void OnCollisionEnter(Collision collision)=>Process(collision);
        void OnCollisionStay(Collision collision)=>Process(collision);
        void Process(Collision collision) { if(Corpse!=null) Corpse.ProcessCollision(collision);else Owner?.ProcessCollision(collision); }
    }
}
