using UnityEngine;

namespace Wyvernland
{
    // Keep small fragments as visible geometry after their simulation stops.
    public sealed class SmallFragmentRest : MonoBehaviour
    {
        public bool IsSettled { get; private set; }
        const float RestSeconds=.15f;
        float restingFor;

        internal static SmallFragmentRest Ensure(GameObject target) =>
            target.GetComponent<SmallFragmentRest>()??target.AddComponent<SmallFragmentRest>();

        void FixedUpdate() => CheckRest(Time.fixedDeltaTime);

        internal void CheckRest(float seconds)
        {
            if(IsSettled||!WebWorldSession.IsPhysicsHost) return;
            var body=GetComponent<Rigidbody>();
            var pickup=GetComponent<PickableObject>();
            if(body==null||body.isKinematic||!body.detectCollisions||
                pickup!=null&&pickup.IsHeld||GetComponent<CorpseGrip>()!=null||GetComponent<Joint>()!=null)
            { restingFor=0;return; }
            // Sleeping bodies no longer move even if PhysX retains a residual
            // velocity. Otherwise both translation and rotation must be zero.
            if(!body.IsSleeping()&&(body.linearVelocity.sqrMagnitude!=0f||body.angularVelocity.sqrMagnitude!=0f))
            { restingFor=0;return; }
            restingFor+=Mathf.Max(0,seconds);
            if(restingFor>=RestSeconds) MakeStatic(true);
        }

        internal void MakeStatic(bool publish,bool force=false)
        {
            if(IsSettled&&!force) return;
            IsSettled=true;
            var body=GetComponent<Rigidbody>();
            var physical=GetComponent<PhysicalDestructible>();
            foreach(var collider in GetComponentsInChildren<Collider>(true))
            {
                if(physical!=null&&collider.GetComponentInParent<PhysicalDestructible>(true)!=physical) continue;
                // A separately simulated child is not part of this fragment.
                var owner=collider.GetComponentInParent<Rigidbody>(true);
                if(body!=null&&owner!=body) continue;
                if(body==null&&owner!=null) continue;
                GetComponent<DestructionFragment>()?.UntrackCollider(collider);
                TemporaryShatterDebris.UntrackCollider(collider);
                collider.enabled=false;Release(collider);
            }
            if(body!=null)
            {
                if(!body.isKinematic) { body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero; }
                body.isKinematic=true;body.detectCollisions=false;Release(body);
            }
            GetComponent<MovingImpactMotion>()?.RetirePhysics();
            if(GetComponent<DynamicObjectSurfaceGuard>() is {} guard) guard.enabled=false;
            if(GetComponent<PickableObject>() is {} pickup) pickup.enabled=false;
            WebWorldSession.RetireFragmentPhysics(gameObject);
            enabled=false;
            PlayerWorldLoading.NotifyChanged(gameObject);
            if(publish) WebWorldSession.Dirty(gameObject);
        }

        static void Release(Object value)
        { if(Application.isPlaying) Destroy(value);else DestroyImmediate(value); }
    }
}
