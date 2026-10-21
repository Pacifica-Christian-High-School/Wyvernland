using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wyvernland
{
    public enum VineKind { Black, Yellow }

    /// <summary>A flexible, constant-radius rope with an independently simulated body per link.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(150)]
    public sealed class RopeObject : MonoBehaviour
    {
        const int Sides=12;
        const float TargetLinkLength=0.25f;
        readonly List<Rigidbody> bodies=new();
        readonly List<CapsuleCollider> colliders=new();
        readonly List<Collider> mountingColliders=new();
        readonly List<Collider> ignoredPlayerColliders=new();
        Rigidbody supportAnchor,handAnchor;
        ConfigurableJoint attachmentJoint,gripJoint;
        Transform support,hand;
        PhysicalDestructible supportProperties;
        Vector3 supportLocalPoint;
        Vector3 gripLocalPoint;
        int gripIndex;
        float linkLength;
        float restorePlayerCollisionAfter;
        bool wasAttached,meshDirty=true;
        Mesh mesh;
        PhysicsMaterial contactMaterial;
        Vector3[] points,vertices,normals;
        Vector2[] uv;
        bool[] sleepingBeforeFreeze;
        internal int VisualUpdateCount { get; private set; }
        internal int NetworkCaptureCount { get; private set; }

        public VineKind Kind { get; private set; }
        public string MaterialName => Kind==VineKind.Black?"Black Vine":"Yellow Vine";
        public float RadiusMetres { get; private set; }
        public float LengthMetres { get; private set; }
        public float MassKg { get; private set; }
        public double SolidVolumeCubicMetres => Math.PI*RadiusMetres*RadiusMetres*LengthMetres;
        public bool IsAttached => support!=null&&attachmentJoint!=null;
        public IReadOnlyList<Rigidbody> Bodies => bodies;
        public IReadOnlyList<CapsuleCollider> Colliders => colliders;
        public Vector3 GripPoint => bodies.Count==0?transform.position:
            bodies[gripIndex].transform.TransformPoint(gripLocalPoint);
        public Vector3 GripDirection => bodies.Count==0?Vector3.down:-bodies[gripIndex].transform.up;
        public Vector3 GripVelocity => bodies.Count==0?Vector3.zero:
            bodies[gripIndex].GetPointVelocity(GripPoint);

        public static Vector2 RadiusRange(VineKind kind) => kind==VineKind.Black?
            new Vector2(0.01f,0.05f):new Vector2(0.03f,0.07f);

        public static RopeObject Create(Transform parent,VineKind kind,float radius,float length,
            Transform branch,Vector3 attachmentPoint,Material appearance)
        {
            if(branch==null||appearance==null) throw new ArgumentNullException();
            if(!float.IsFinite(radius)||!float.IsFinite(length)||length<=0f)
                throw new ArgumentOutOfRangeException(nameof(length));
            var root=new GameObject(kind==VineKind.Black?"Black Vine":"Yellow Vine");
            root.transform.SetParent(parent,false);
            root.transform.position=attachmentPoint;
            var rope=root.AddComponent<RopeObject>();
            rope.Initialize(kind,radius,length,branch,attachmentPoint,appearance);
            var pickup=root.AddComponent<PickableObject>();
            pickup.DisplayName=rope.MaterialName;
            pickup.TrackMovement();
            return rope;
        }

        void Initialize(VineKind kind,float radius,float length,Transform branch,
            Vector3 attachmentPoint,Material appearance)
        {
            Kind=kind;
            Vector2 range=RadiusRange(kind);
            RadiusMetres=Mathf.Clamp(radius,range.x,range.y);
            LengthMetres=length;
            MaterialDensity.TryGet(MaterialName,out float density);
            MassKg=(float)SolidVolumeCubicMetres*density;
            int count=Mathf.Clamp(Mathf.CeilToInt(length/TargetLinkLength),2,40);
            linkLength=length/count;
            contactMaterial=new PhysicsMaterial("Vine friction") {
                dynamicFriction=0.55f,staticFriction=0.65f,bounciness=0f,
                frictionCombine=PhysicsMaterialCombine.Average,
                bounceCombine=PhysicsMaterialCombine.Minimum };
            supportAnchor=CreateAnchor("Branch attachment",attachmentPoint);
            SetSupport(branch,attachmentPoint);
            for(int i=0;i<count;i++)
            {
                var segment=new GameObject($"Rope link {i+1}");
                segment.transform.SetParent(transform,false);
                segment.transform.position=attachmentPoint+Vector3.down*linkLength*(i+0.5f);
                var collider=segment.AddComponent<CapsuleCollider>();
                collider.direction=1;
                collider.radius=RadiusMetres;
                // End spheres cover the joint so bent links never open collision gaps.
                collider.height=linkLength+2f*RadiusMetres;
                collider.contactOffset=Mathf.Min(0.005f,RadiusMetres*0.2f);
                collider.sharedMaterial=contactMaterial;
                colliders.Add(collider);
                var body=segment.AddComponent<Rigidbody>();
                body.mass=MassKg/count;
                body.useGravity=true;
                body.linearDamping=0.08f;
                body.angularDamping=0.12f;
                body.interpolation=RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                body.solverIterations=24;
                body.solverVelocityIterations=12;
                body.maxDepenetrationVelocity=2f;
                bodies.Add(body);
                var joint=Connect(body,i==0?supportAnchor:bodies[i-1],
                    Vector3.up*linkLength*0.5f,i==0?Vector3.zero:Vector3.down*linkLength*0.5f);
                if(i==0) attachmentJoint=joint;
                if(i>0) Physics.IgnoreCollision(collider,colliders[i-1]);
            }
            IgnoreMountCollisions();
            wasAttached=true;
            BuildMesh(appearance);
            // A vertical hanging chain is already at rest. Contacts, grabbing and
            // movement of its support wake the island without distance-based streaming.
            foreach(var body in bodies) body.Sleep();
        }

        Rigidbody CreateAnchor(string label,Vector3 position)
        {
            var anchor=new GameObject(label);
            anchor.transform.SetParent(transform,false);
            anchor.transform.position=position;
            var body=anchor.AddComponent<Rigidbody>();
            body.isKinematic=true;
            body.useGravity=false;
            body.detectCollisions=false;
            return body;
        }

        ConfigurableJoint Connect(Rigidbody body,Rigidbody connected,Vector3 local,Vector3 otherLocal)
        {
            var joint=body.gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor=false;
            joint.connectedBody=connected;
            joint.anchor=local;
            joint.connectedAnchor=otherLocal;
            // Spherical joints preserve length but transmit no artificial bending
            // spring or twist torque: the rope can sag, swing, fold and coil.
            joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Locked;
            joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Free;
            joint.projectionMode=JointProjectionMode.PositionAndRotation;
            joint.projectionDistance=0.01f;
            joint.enableCollision=false;
            joint.enablePreprocessing=false;
            return joint;
        }

        internal Transform LoadingSupport=>support;
        void SetSupport(Transform branch,Vector3 worldPoint)
        {
            RestoreMountCollisions();
            if(supportProperties!=null) supportProperties.Fragmented-=OnSupportFragmented;
            support=branch;
            supportLocalPoint=branch.InverseTransformPoint(worldPoint);
            supportProperties=branch.GetComponent<PhysicalDestructible>();
            if(supportProperties!=null) supportProperties.Fragmented+=OnSupportFragmented;
            IgnoreMountCollisions();
        }

        void IgnoreMountCollisions()
        {
            if(support==null||colliders.Count==0) return;
            foreach(var collider in support.GetComponentsInChildren<Collider>())
            {
                if(collider.isTrigger||supportProperties!=null&&
                    collider.GetComponentInParent<PhysicalDestructible>()!=supportProperties) continue;
                // The mounted end overlaps its branch like two adjacent links;
                // all other links still collide with that branch normally.
                Physics.IgnoreCollision(colliders[0],collider);
                mountingColliders.Add(collider);
            }
        }

        void RestoreMountCollisions()
        {
            if(colliders.Count>0)
                foreach(var collider in mountingColliders)
                    if(collider!=null) Physics.IgnoreCollision(colliders[0],collider,false);
            mountingColliders.Clear();
        }

        void OnSupportFragmented(PhysicalDestructible source,IReadOnlyList<PhysicalDestructible> pieces)
        {
            Vector3 mount=support.TransformPoint(supportLocalPoint);
            PhysicalDestructible best=null;
            float distance=RadiusMetres+0.06f;
            var candidates=new List<PhysicalDestructible>();
            if(!source.HasBeenDestroyed) candidates.Add(source);
            candidates.AddRange(pieces);
            foreach(var piece in candidates)
            {
                if(piece==null||!piece.gameObject.activeInHierarchy) continue;
                foreach(var collider in piece.GetComponentsInChildren<Collider>())
                {
                    if(!collider.enabled||collider.isTrigger||
                        collider.GetComponentInParent<PhysicalDestructible>()!=piece) continue;
                    Vector3 closest=ClosestSupportSurface(collider,mount);
                    float candidate=Vector3.Distance(closest,mount);
                    if(candidate>distance) continue;
                    best=piece;distance=candidate;
                }
            }
            if(best!=null) SetSupport(best.transform,mount);
            else DetachFromBranch();
        }

        static Vector3 ClosestSupportSurface(Collider collider,Vector3 point)
        {
            if(collider is not MeshCollider { convex:false,sharedMesh:{ isReadable:true } } meshCollider)
                return collider.ClosestPoint(point);
            Mesh shape=meshCollider.sharedMesh;
            Vector3[] vertices=shape.vertices;
            int[] triangles=shape.triangles;
            Vector3 nearest=point;
            float distance=float.PositiveInfinity;
            for(int i=0;i<triangles.Length;i+=3)
            {
                Vector3 candidate=PickableObject.ClosestTrianglePoint(point,
                    meshCollider.transform.TransformPoint(vertices[triangles[i]]),
                    meshCollider.transform.TransformPoint(vertices[triangles[i+1]]),
                    meshCollider.transform.TransformPoint(vertices[triangles[i+2]]));
                float squared=(candidate-point).sqrMagnitude;
                if(squared>=distance) continue;
                distance=squared;nearest=candidate;
            }
            return float.IsFinite(distance)?nearest:collider.bounds.ClosestPoint(point);
        }

        public void DetachFromBranch()
        {
            RestoreMountCollisions();
            if(supportProperties!=null) supportProperties.Fragmented-=OnSupportFragmented;
            supportProperties=null;
            support=null;
            wasAttached=false;
            if(attachmentJoint!=null) Remove(attachmentJoint);
            attachmentJoint=null;
            Wake();
        }

        void FixedUpdate() => StepAnchors();

        // Also used by the editor's manually stepped physics validation.
        public void StepAnchors()
        {
            if(!WebWorldSession.CanSimulateRope(gameObject)) return;
            if(bodies.Count==0) return;
            if(wasAttached&&(support==null||!support.gameObject.activeInHierarchy||
                supportProperties!=null&&supportProperties.HasBeenDestroyed)) DetachFromBranch();
            if(IsAttached)
            {
                Vector3 mount=support.TransformPoint(supportLocalPoint);
                Rigidbody branchBody=support.GetComponentInParent<Rigidbody>();
                Rigidbody connected=branchBody!=null?branchBody:supportAnchor;
                // A falling or held branch becomes the real connected body. The
                // vine's weight and tension then act on that branch as well.
                if(attachmentJoint.connectedBody!=connected)
                {
                    attachmentJoint.connectedBody=connected;
                    Wake();
                }
                Vector3 otherAnchor=connected==supportAnchor?Vector3.zero:
                    connected.transform.InverseTransformPoint(mount);
                if((attachmentJoint.connectedAnchor-otherAnchor).sqrMagnitude>1e-10f)
                    attachmentJoint.connectedAnchor=otherAnchor;
                if(connected==supportAnchor&&(mount-supportAnchor.position).sqrMagnitude>1e-10f)
                {
                    supportAnchor.MovePosition(mount);
                    Wake();
                }
            }
            if(hand!=null&&handAnchor!=null&&gripJoint!=null)
            {
                Vector3 target=hand.position;
                if(IsAttached)
                {
                    // Do not ask two kinematic anchors to stretch a taut rope.
                    float reach=linkLength*(gripIndex+0.5f)-gripLocalPoint.y;
                    Vector3 mount=support.TransformPoint(supportLocalPoint);
                    target=mount+Vector3.ClampMagnitude(target-mount,Mathf.Max(RadiusMetres,reach));
                }
                handAnchor.MovePosition(target);
                Wake();
            }
            else if(handAnchor!=null) EndGrab(Vector3.zero);
            if(hand==null&&ignoredPlayerColliders.Count>0)
            {
                restorePlayerCollisionAfter-=Time.fixedDeltaTime;
                if(restorePlayerCollisionAfter<=0f)
                {
                    foreach(var playerCollider in ignoredPlayerColliders)
                        if(playerCollider!=null)
                            foreach(var collider in colliders) Physics.IgnoreCollision(collider,playerCollider,false);
                    ignoredPlayerColliders.Clear();
                }
            }
        }

        public Vector3 NearestPoint(Vector3 worldPoint)
        {
            Vector3 nearest=transform.position;
            float distance=float.PositiveInfinity;
            foreach(var collider in colliders)
            {
                Vector3 point=collider.ClosestPoint(worldPoint);
                float candidate=(point-worldPoint).sqrMagnitude;
                if(candidate>=distance) continue;
                nearest=point;distance=candidate;
            }
            return nearest;
        }

        public bool BeginGrab(Transform targetHand,Vector3 worldPoint)
        {
            if(targetHand==null||hand!=null||bodies.Count==0) return false;
            float distance=float.PositiveInfinity;
            for(int i=0;i<colliders.Count;i++)
            {
                float candidate=(colliders[i].ClosestPoint(worldPoint)-worldPoint).sqrMagnitude;
                if(candidate>=distance) continue;
                distance=candidate;gripIndex=i;
            }
            gripLocalPoint=bodies[gripIndex].transform.InverseTransformPoint(
                colliders[gripIndex].ClosestPoint(worldPoint));
            hand=targetHand;
            handAnchor=CreateAnchor("Hand grip",GripPoint);
            gripJoint=Connect(bodies[gripIndex],handAnchor,gripLocalPoint,Vector3.zero);
            Transform playerRoot=targetHand.GetComponentInParent<FirstPersonController>()?.transform;
            if(playerRoot!=null)
                foreach(var playerCollider in playerRoot.GetComponentsInChildren<Collider>())
                {
                    if(playerCollider.isTrigger||playerCollider.GetComponentInParent<PickableObject>()!=null) continue;
                    if(!ignoredPlayerColliders.Contains(playerCollider)) ignoredPlayerColliders.Add(playerCollider);
                    foreach(var collider in colliders) Physics.IgnoreCollision(collider,playerCollider);
                }
            Wake();
            return true;
        }

        public void TransferGrip(Transform targetHand) { hand=targetHand;Wake(); }

        public void EndGrab(Vector3 releaseVelocity)
        {
            if(gripJoint!=null) Remove(gripJoint);
            gripJoint=null;
            if(handAnchor!=null) Remove(handAnchor.gameObject);
            handAnchor=null;
            hand=null;
            restorePlayerCollisionAfter=0.2f;
            if(bodies.Count>0&&releaseVelocity.sqrMagnitude>0f)
                bodies[gripIndex].linearVelocity=releaseVelocity;
            // Other links retain their simulated momentum when the grip opens.
            Wake();
        }

        void Wake() { foreach(var body in bodies) body.WakeUp();meshDirty=true; }

        void BuildMesh(Material appearance)
        {
            int rings=bodies.Count+1;
            points=new Vector3[rings];
            vertices=new Vector3[rings*Sides+2];
            normals=new Vector3[vertices.Length];
            uv=new Vector2[vertices.Length];
            var triangles=new List<int>((rings-1)*Sides*6+Sides*6);
            for(int ring=0;ring<rings;ring++)
            for(int side=0;side<Sides;side++)
            {
                int a=ring*Sides+side,b=ring*Sides+(side+1)%Sides;
                uv[a]=new Vector2(side/(float)Sides,ring*linkLength);
                if(ring==rings-1) continue;
                triangles.Add(a);triangles.Add(b+Sides);triangles.Add(a+Sides);
                triangles.Add(a);triangles.Add(b);triangles.Add(b+Sides);
            }
            for(int side=0;side<Sides;side++)
            {
                int next=(side+1)%Sides,last=(rings-1)*Sides;
                triangles.Add(vertices.Length-2);triangles.Add(next);triangles.Add(side);
                triangles.Add(vertices.Length-1);triangles.Add(last+side);triangles.Add(last+next);
            }
            mesh=new Mesh { name=MaterialName+" continuous rope" };
            mesh.MarkDynamic();
            UpdateVisual();
            mesh.triangles=triangles.ToArray();
            mesh.uv=uv;
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=appearance;
            ColorOnlyTextureQuality.ApplyRendererQuality(renderer);
        }

        void LateUpdate()
        {
            // Paused loading cannot move the links. Newly registered colliders
            // can wake PhysX islands even without a simulation step, so sleeping
            // state alone must not trigger hundreds of unnecessary mesh rebuilds.
            if(WorldLoading.IsLoading||Time.timeScale<=0f&&!meshDirty) return;
            if(!meshDirty)
                foreach(var body in bodies) if(!body.isKinematic&&!body.IsSleeping()) { meshDirty=true;break; }
            if(meshDirty) UpdateVisual();
        }

        public void UpdateVisual()
        {
            if(mesh==null||bodies.Count==0) return;
            VisualUpdateCount++;
            float half=linkLength*0.5f;
            points[0]=transform.InverseTransformPoint(bodies[0].transform.TransformPoint(Vector3.up*half));
            for(int i=1;i<points.Length;i++)
            {
                Vector3 bottom=bodies[i-1].transform.TransformPoint(Vector3.down*half);
                if(i<bodies.Count) bottom=(bottom+
                    bodies[i].transform.TransformPoint(Vector3.up*half))*0.5f;
                points[i]=transform.InverseTransformPoint(bottom);
            }
            Vector3 radial=Vector3.right;
            for(int i=0;i<points.Length;i++)
            {
                Vector3 tangent=(points[Mathf.Min(i+1,points.Length-1)]-
                    points[Mathf.Max(0,i-1)]).normalized;
                if(tangent.sqrMagnitude<0.5f) tangent=Vector3.down;
                radial=Vector3.ProjectOnPlane(radial,tangent).normalized;
                if(radial.sqrMagnitude<0.5f)
                    radial=Vector3.Cross(tangent,Mathf.Abs(tangent.y)<0.9f?Vector3.up:Vector3.forward).normalized;
                Vector3 binormal=Vector3.Cross(tangent,radial).normalized;
                for(int side=0;side<Sides;side++)
                {
                    float angle=side*2f*Mathf.PI/Sides;
                    Vector3 normal=radial*Mathf.Cos(angle)+binormal*Mathf.Sin(angle);
                    int index=i*Sides+side;
                    vertices[index]=points[i]+normal*RadiusMetres;
                    normals[index]=normal;
                }
            }
            vertices[^2]=points[0];vertices[^1]=points[^1];
            normals[^2]=(points[0]-points[1]).normalized;
            normals[^1]=(points[^1]-points[^2]).normalized;
            mesh.vertices=vertices;
            mesh.normals=normals;
            mesh.RecalculateBounds();
            meshDirty=false;
        }

        static void Remove(UnityEngine.Object value)
        { if(Application.isPlaying) Destroy(value);else DestroyImmediate(value); }

        void OnDestroy()
        {
            WebWorldSession.Removed(gameObject);
            if(supportProperties!=null) supportProperties.Fragmented-=OnSupportFragmented;
            if(mesh!=null) Remove(mesh);
            if(contactMaterial!=null) Remove(contactMaterial);
        }

        [Serializable] public sealed class NetworkRope
        {
            public string support;
            public Vector3 localSupport,localGrip;
            public int grip;
            public bool attached;
            public Vector3[] positions,velocities,angular;
            public Quaternion[] rotations;
            public bool[] sleeping;
        }
        internal NetworkRope CaptureNetworkRope()
        {
            NetworkCaptureCount++;
            var state=new NetworkRope { support=WebWorldSession.IdOf(support!=null?support.gameObject:null),
                localSupport=supportLocalPoint,localGrip=gripLocalPoint,grip=gripIndex,attached=IsAttached,
                positions=new Vector3[bodies.Count],rotations=new Quaternion[bodies.Count],
                velocities=new Vector3[bodies.Count],angular=new Vector3[bodies.Count],sleeping=new bool[bodies.Count] };
            for(int i=0;i<bodies.Count;i++) { state.positions[i]=bodies[i].position;
                state.rotations[i]=bodies[i].rotation;state.velocities[i]=bodies[i].linearVelocity;
                state.angular[i]=bodies[i].angularVelocity;state.sleeping[i]=bodies[i].IsSleeping(); }
            return state;
        }
        internal void ApplyNetworkRope(NetworkRope state)
        {
            if(state==null||state.positions?.Length!=bodies.Count||state.rotations?.Length!=bodies.Count) return;
            var next=WebWorldSession.ObjectOf(state.support)?.transform;
            if(state.attached&&next!=null) {
                if(support!=next||!IsAttached) SetSupport(next,next.TransformPoint(state.localSupport));
                supportLocalPoint=state.localSupport;
            } else if(IsAttached) DetachFromBranch();
            gripIndex=Mathf.Clamp(state.grip,0,Mathf.Max(0,bodies.Count-1));gripLocalPoint=state.localGrip;
            for(int i=0;i<bodies.Count;i++) {
                bodies[i].position=state.positions[i];bodies[i].rotation=state.rotations[i];
                if(!bodies[i].isKinematic) {
                    bodies[i].linearVelocity=state.velocities[i];bodies[i].angularVelocity=state.angular[i];
                }
            }
            // Position/kinematic writes wake connected islands. Restore sleep
            // after every link has been updated, not between linked writes.
            for(int i=0;i<bodies.Count;i++) if(!bodies[i].isKinematic) {
                    bool resting=state.sleeping?.Length==bodies.Count?state.sleeping[i]:
                        state.attached&&hand==null&&support!=null&&support.GetComponentInParent<Rigidbody>()==null&&
                        state.velocities[i].sqrMagnitude<1e-8f&&state.angular[i].sqrMagnitude<1e-8f;
                    if(resting) bodies[i].Sleep();else bodies[i].WakeUp();
            }
            UpdateVisual();
        }
        internal void SetNetworkPhysics(bool simulate)
        {
            sleepingBeforeFreeze??=new bool[bodies.Count];
            for(int i=0;i<bodies.Count;i++) {
                var body=bodies[i];if(body.isKinematic==!simulate) continue;
                if(!simulate) sleepingBeforeFreeze[i]=body.IsSleeping();
                body.isKinematic=!simulate;
            }
            if(simulate) for(int i=0;i<bodies.Count;i++) if(sleepingBeforeFreeze[i]) bodies[i].Sleep();
        }
        internal bool NetworkChanged(NetworkRope state)
        {
            if(state==null||state.positions?.Length!=bodies.Count||state.rotations?.Length!=bodies.Count||
                state.attached!=IsAttached||state.grip!=gripIndex||state.localGrip!=gripLocalPoint||
                state.localSupport!=supportLocalPoint||state.support!=WebWorldSession.IdOf(support!=null?support.gameObject:null)) return true;
            for(int i=0;i<bodies.Count;i++) {
                var body=bodies[i];
                if((body.position-state.positions[i]).sqrMagnitude>.000025f||Quaternion.Angle(body.rotation,state.rotations[i])>.3f||
                    (body.linearVelocity-state.velocities[i]).sqrMagnitude>.000025f||
                    (body.angularVelocity-state.angular[i]).sqrMagnitude>.000025f||
                    state.sleeping?.Length==bodies.Count&&body.IsSleeping()!=state.sleeping[i]) return true;
            }
            return false;
        }
    }
}
