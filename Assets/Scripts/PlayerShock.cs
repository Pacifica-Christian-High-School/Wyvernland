using System.Linq;
using UnityEngine;

namespace Wyvernland
{
    public sealed partial class PlayerDamage
    {
        internal const float KnockdownImpulse=428f,MinimumRagdollMassKg=100f,MinimumRagdollSpeed=20f,RecoverySeconds=1.25f;
        readonly System.Collections.Generic.Dictionary<int,float> shockCooldowns=new();
        internal void ReceiveSourceShock(int source,float mass,Vector3 incoming,Vector3 normal,Vector3 point)
        {
            if(shockCooldowns.TryGetValue(source,out float until)&&Time.time<until) return;
            if(!CanEnterImpactRagdoll(mass,incoming,normal)) return;
            if(shockCooldowns.Count>512) shockCooldowns.Clear();
            shockCooldowns[source]=Time.time+.25f;ReceiveShock(mass,incoming,normal,point);
        }
        float shockRemaining,recoveryRemaining,recoveryDuration=RecoverySeconds;
        Collider[] shockRootShapes;
        bool[] shockRootEnabled;
        Vector3 recoveryEye;
        Quaternion viewRotationBeforeShock,recoveryViewRotation;
        public bool IsShocked=>replica?replicaState!=null&&replicaState.shockPhase>0:shockRemaining>0f||recoveryRemaining>0f;
        internal bool IsRecoveringShock=>!replica&&recoveryRemaining>0f;
        internal int ShockPhase=>shockRemaining>0f?1:recoveryRemaining>0f?2:0;
        void ResumeReplicaShock(State next)
        {
            if(next==null||next.shockPhase==0) { CancelShock();return; }
            if(shockRootShapes==null) {
                shockRootShapes=GetComponentsInChildren<Collider>(true).Where(c=>!c.transform.IsChildOf(model)).ToArray();
                shockRootEnabled=shockRootShapes.Select(c=>c.enabled).ToArray();
            }
            foreach(var shape in shockRootShapes) if(shape!=null) shape.enabled=false;
            shockRemaining=next.shockPhase==1?Mathf.Clamp(next.shockRemaining>0?next.shockRemaining:2f,.01f,4f):0;
            recoveryRemaining=next.shockPhase==2?Mathf.Clamp(next.recoveryRemaining>0?next.recoveryRemaining:RecoverySeconds,.01f,RecoverySeconds):0;
            controller.SetIncapacitated(true);ragdoll.SetFullBodyStun(next.shockPhase==1);PlayerWorldLoading.NotifyChanged(gameObject);
            if(recoveryRemaining>0) { recoveryDuration=recoveryRemaining;ragdoll.CaptureRecoveryPose();recoveryEye=controller.PlayerCamera.transform.position;recoveryViewRotation=controller.PlayerCamera.transform.rotation; }
        }
        internal static float ShockImpulse(float mass,float speed)
        {
            if(!float.IsFinite(mass)||!float.IsFinite(speed)||mass<=0||speed<1.5f) return 0;
            return Mathf.Min(1200f,(mass*FirstPersonController.PlayerMassKg/(mass+FirstPersonController.PlayerMassKg))*speed);
        }
        internal static float KnockdownSeconds(float impulse)=>Mathf.Lerp(2f,4f,Mathf.InverseLerp(KnockdownImpulse,650f,impulse));
        internal static bool CanEnterImpactRagdoll(float mass,Vector3 incoming,Vector3 normal)
        {
            if(!float.IsFinite(mass)||!float.IsFinite(incoming.sqrMagnitude)||
                mass<=MinimumRagdollMassKg||incoming.magnitude<=MinimumRagdollSpeed) return false;
            return ShockImpulse(mass,Vector3.Dot(incoming,normal.normalized))>=KnockdownImpulse;
        }
        internal void ReceiveShock(float mass,Vector3 incoming,Vector3 normal,Vector3 point)
        {
            if(replica||IsDead||controller.IsPaused||!CanEnterImpactRagdoll(mass,incoming,normal)) return;
            float strength=ShockImpulse(mass,Mathf.Max(0f,Vector3.Dot(incoming,normal.normalized)));
            if(!IsShocked) {
                // Entering ragdoll must not throw or drop equipment. Cancel the
                // active strike/pickup animation, but leave both grip leases intact
                // so the held objects remain attached to the hands while stunned.
                avatar.CancelAllStrikes();avatar.CancelRightHandPickup();
                viewRotationBeforeShock=controller.PlayerCamera.transform.localRotation;
                shockRootShapes=GetComponentsInChildren<Collider>(true).Where(c=>!c.transform.IsChildOf(model)).ToArray();
                shockRootEnabled=shockRootShapes.Select(c=>c.enabled).ToArray();
                foreach(var shape in shockRootShapes) shape.enabled=false;
            }
            shockRemaining=Mathf.Max(shockRemaining,KnockdownSeconds(strength));recoveryRemaining=0f;
            controller.SetIncapacitated(true);ragdoll.SetFullBodyStun(true);
            PlayerWorldLoading.NotifyChanged(gameObject);
        }
        internal void TickShock(float seconds)
        {
            if(replica||IsDead||controller.IsPaused) return;
            if(shockRemaining>0f) {
                shockRemaining=Mathf.Max(0f,shockRemaining-seconds);
                if(shockRemaining>0f) return;
                Vector3 eyeBeforeRebase=controller.PlayerCamera.transform.position;
                var pose=ragdoll.CapturePose();
                var waist=model.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Waist");
                Vector3 stand=transform.position;stand.x=waist.position.x;stand.z=waist.position.z;
                var floor=Physics.RaycastAll(waist.position+Vector3.up*2f,Vector3.down,12f,~0,QueryTriggerInteraction.Ignore)
                    .Where(h=>!h.collider.transform.IsChildOf(transform)&&h.collider.attachedRigidbody==null)
                    .OrderBy(h=>h.distance).FirstOrDefault();
                if(floor.collider!=null) stand.y=floor.point.y+.15f;
                transform.position=stand;ragdoll.ApplyPose(pose,entire:true);
                ragdoll.SetFullBodyStun(false);ragdoll.CaptureRecoveryPose();
                recoveryDuration=RecoverySeconds;recoveryRemaining=RecoverySeconds;recoveryEye=eyeBeforeRebase;
                recoveryViewRotation=controller.PlayerCamera.transform.rotation;
                controller.PlayerCamera.transform.position=eyeBeforeRebase;
                return;
            }
            if(recoveryRemaining>0f) {
                recoveryRemaining=Mathf.Max(0f,recoveryRemaining-seconds);
                if(recoveryRemaining<=0f) { ragdoll.ResetAnimatedBones();CancelShock(); }
            }
        }
        void ApplyShockRecoveryPose()
        {
            if(shockRemaining>0f) {
                controller.PlayerCamera.transform.position=avatar.ShockEyePosition;
                controller.PlayerCamera.transform.rotation=avatar.ShockEyeRotation;
            }
            else if(recoveryRemaining>0f) {
                float blend=Mathf.SmoothStep(0f,1f,1f-recoveryRemaining/recoveryDuration);
                ragdoll.BlendRecoveryPose(blend);
                controller.PlayerCamera.transform.position=Vector3.Lerp(recoveryEye,controller.PlayerCamera.transform.position,blend);
                controller.PlayerCamera.transform.rotation=Quaternion.Slerp(recoveryViewRotation,
                    transform.rotation*viewRotationBeforeShock,blend);
            }
        }
        void CancelShock()
        {
            if(shockRemaining<=0f&&recoveryRemaining<=0f&&shockRootShapes==null) return;
            shockRemaining=recoveryRemaining=0f;ragdoll.SetFullBodyStun(false);
            if(shockRootShapes!=null) for(int i=0;i<shockRootShapes.Length;i++) if(shockRootShapes[i]!=null) shockRootShapes[i].enabled=shockRootEnabled[i];
            shockRootShapes=null;shockRootEnabled=null;controller.SetIncapacitated(false);PlayerWorldLoading.NotifyChanged(gameObject);
        }
    }
}
