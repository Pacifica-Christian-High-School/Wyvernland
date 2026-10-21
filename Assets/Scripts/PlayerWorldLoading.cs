using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wyvernland
{
    // Local residency never destroys a shared identity or changes its physics
    // properties. Retained geometry/state lets changed objects return intact.
    [DefaultExecutionOrder(450)]
    public sealed class PlayerWorldLoading : MonoBehaviour
    {
        public const float CollisionRange=20f,DefaultRange=200f,MinimumRange=50f,MaximumRange=2000f;
        public const float TerrainRangeMultiplier=3f;
        const float CellSize=32f;
        sealed class Switch<T> where T:Behaviour { public T Value;public bool Enabled; }
        sealed class ColliderSwitch { public Collider Value;public bool Enabled; }
        sealed class Entry
        {
            public GameObject Target;
            public PhysicalDestructible Physical;
            public bool Terrain;
            public PickableObject Pickup;
            public RopeObject Rope;
            public TreeStructuralPart Tree;
            public readonly List<Renderer> Renderers=new();
            public readonly List<ColliderSwitch> Colliders=new();
            public readonly List<Switch<MonoBehaviour>> Scripts=new();
            public readonly List<Rigidbody> Bodies=new();
            public readonly List<Vector2Int> Cells=new();
            public Bounds Bounds;
            public bool Loaded=true,WasMoving;
            public float PinnedUntil;
            public Action Changed;
        }
        static PlayerWorldLoading current;
        readonly Dictionary<GameObject,Entry> entries=new();
        readonly Dictionary<Vector2Int,HashSet<Entry>> cells=new();
        readonly HashSet<Entry> dynamicEntries=new(),loaded=new(),wanted=new(),candidates=new();
        readonly HashSet<GameObject> pending=new();
        readonly List<GameObject> pendingWork=new();
        readonly List<Entry> work=new(),changes=new();
        readonly List<Rigidbody> keepSleeping=new();
        readonly HashSet<Entry> affected=new();
        readonly Dictionary<string,Vector3> playerProbes=new();
        Transform world,observer;
        bool initialized,changed=true,closing;
        int refreshedFrame=-1;
        Vector3 lastObserver;
        float nextPinExpiry=float.PositiveInfinity;
        public float LoadingRange { get; private set; }=DefaultRange;
        public int LoadedObjectCount=>loaded.Count;
        public int RegisteredObjectCount=>entries.Count;
        public int LastCandidateCount { get; private set; }
        public static float SavedRange=>Mathf.Clamp(PlayerPrefs.GetFloat("Options.LoadingRange",DefaultRange),MinimumRange,MaximumRange);
        public static float LocalRange=>current!=null?current.LoadingRange:SavedRange;

        public static void SetSavedRange(float range)
        {
            range=Mathf.Clamp(range,MinimumRange,MaximumRange);
            PlayerPrefs.SetFloat("Options.LoadingRange",range);PlayerPrefs.Save();
            if(current!=null) current.SetRange(range);
        }
        public void SetRange(float range) { LoadingRange=Mathf.Clamp(range,MinimumRange,MaximumRange);changed=true;if(initialized) Refresh(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics()=>current=null;

        internal IEnumerator Initialize(GameObject root,Transform viewer,float range=-1f,bool makeCurrent=true)
        {
            world=root.transform;observer=viewer;LoadingRange=range<0?SavedRange:Mathf.Clamp(range,MinimumRange,MaximumRange);
            if(makeCurrent) current=this;
            double batch=Time.realtimeSinceStartupAsDouble;
            foreach(var physical in root.GetComponentsInChildren<PhysicalDestructible>(true)) Add(physical.gameObject);
            foreach(var rope in root.GetComponentsInChildren<RopeObject>(true)) Add(rope.gameObject);
            foreach(var pickup in root.GetComponentsInChildren<PickableObject>(true)) Add(pickup.gameObject);
            foreach(var tree in root.GetComponentsInChildren<GeneratedTree>(true)) Add(tree.gameObject);
            foreach(var entry in entries.Values) {
                Populate(entry);
                if(WorldLoading.ShouldYield(ref batch)) { yield return null;batch=Time.realtimeSinceStartupAsDouble; }
            }
            initialized=true;Refresh();
        }
        Entry Add(GameObject target)
        {
            if(entries.TryGetValue(target,out var found)) return found;
            var entry=new Entry { Target=target,Physical=target.GetComponent<PhysicalDestructible>(),
                Pickup=target.GetComponent<PickableObject>(),Rope=target.GetComponent<RopeObject>(),Tree=target.GetComponent<TreeStructuralPart>() };
            entry.Terrain=entry.Physical!=null&&entry.Physical.IsTerrain;
            entries.Add(target,entry);loaded.Add(entry);
            if(entry.Physical!=null) { entry.Changed=()=>NotifyChanged(target);entry.Physical.GeometryChanged+=entry.Changed; }
            return entry;
        }
        Entry Owner(Transform target)
        {
            for(var node=target;node!=null&&node!=world;node=node.parent)
                if(entries.TryGetValue(node.gameObject,out var entry)) return entry;
            return null;
        }
        void Populate(Entry entry)
        {
            Unindex(entry);
            entry.Renderers.Clear();entry.Colliders.Clear();entry.Scripts.Clear();entry.Bodies.Clear();
            entry.Pickup=entry.Target.GetComponent<PickableObject>();entry.Rope=entry.Target.GetComponent<RopeObject>();
            entry.Tree=entry.Target.GetComponent<TreeStructuralPart>();
            foreach(var renderer in entry.Target.GetComponentsInChildren<Renderer>(true))
                if(Owner(renderer.transform)==entry) entry.Renderers.Add(renderer);
            foreach(var collider in entry.Target.GetComponentsInChildren<Collider>(true)) if(Owner(collider.transform)==entry) {
                entry.Colliders.Add(new ColliderSwitch { Value=collider,Enabled=collider.enabled });
                AddBody(entry,collider.attachedRigidbody);
            }
            foreach(var body in entry.Target.GetComponentsInChildren<Rigidbody>(true)) if(Owner(body.transform)==entry) AddBody(entry,body);
            AddBody(entry,entry.Target.GetComponentInParent<Rigidbody>());
            foreach(var script in entry.Target.GetComponents<MonoBehaviour>())
                if(script is PhysicalDestructible or PickableObject or RopeObject or TreeStructuralPart or GeneratedTree)
                    entry.Scripts.Add(new Switch<MonoBehaviour> { Value=script,Enabled=script.enabled });
            if((entry.Physical==null||!entry.Physical.HasBeenDestroyed)&&
                (entry.Bodies.Count>0||entry.Pickup!=null&&entry.Pickup.IsHeld||WebWorldSession.LoadingMotion(entry.Target,out _))) dynamicEntries.Add(entry);
            else dynamicEntries.Remove(entry);
            UpdateBounds(entry);Index(entry);
        }
        static void AddBody(Entry entry,Rigidbody body) { if(body!=null&&!entry.Bodies.Contains(body)) entry.Bodies.Add(body); }
        static Bounds TransformBounds(Bounds local,Transform target)
        {
            var m=target.localToWorldMatrix;var e=local.extents;
            var x=m.MultiplyVector(new Vector3(e.x,0,0));var y=m.MultiplyVector(new Vector3(0,e.y,0));var z=m.MultiplyVector(new Vector3(0,0,e.z));
            return new Bounds(m.MultiplyPoint3x4(local.center),new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),
                Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z))*2f);
        }
        static void UpdateBounds(Entry entry)
        {
            bool found=false;Bounds bounds=new(entry.Target.transform.position,Vector3.zero);
            void Include(Bounds value) { if(!found) { bounds=value;found=true; }else bounds.Encapsulate(value); }
            foreach(var renderer in entry.Renderers) if(renderer!=null) {
                var filter=renderer.GetComponent<MeshFilter>();
                if(filter!=null&&filter.sharedMesh!=null) Include(TransformBounds(filter.sharedMesh.bounds,renderer.transform));
                else Include(renderer.bounds);
            }
            foreach(var state in entry.Colliders) if(state.Value!=null&&state.Enabled) {
                var collider=state.Value;
                if(collider is MeshCollider mesh&&mesh.sharedMesh!=null) Include(TransformBounds(mesh.sharedMesh.bounds,mesh.transform));
                else if(collider is BoxCollider box) Include(TransformBounds(new Bounds(box.center,box.size),box.transform));
                else if(collider is SphereCollider sphere) Include(TransformBounds(new Bounds(sphere.center,Vector3.one*sphere.radius*2f),sphere.transform));
                else if(collider is CapsuleCollider capsule) {
                    var size=Vector3.one*capsule.radius*2f;size[capsule.direction]=Mathf.Max(capsule.height,capsule.radius*2f);
                    Include(TransformBounds(new Bounds(capsule.center,size),capsule.transform));
                } else if(collider.enabled) Include(collider.bounds);
            }
            entry.Bounds=bounds;
        }
        void Index(Entry entry)
        {
            var min=entry.Bounds.min;var max=entry.Bounds.max;
            for(int z=Mathf.FloorToInt(min.z/CellSize);z<=Mathf.FloorToInt(max.z/CellSize);z++)
            for(int x=Mathf.FloorToInt(min.x/CellSize);x<=Mathf.FloorToInt(max.x/CellSize);x++) {
                var key=new Vector2Int(x,z);if(!cells.TryGetValue(key,out var bucket)) cells[key]=bucket=new();
                bucket.Add(entry);entry.Cells.Add(key);
            }
        }
        void Unindex(Entry entry) { foreach(var key in entry.Cells) if(cells.TryGetValue(key,out var bucket)) { bucket.Remove(entry);if(bucket.Count==0) cells.Remove(key); }entry.Cells.Clear(); }
        void Need(Entry entry) { if(entry!=null&&entry.Target!=null&&(entry.Physical==null||!entry.Physical.HasBeenDestroyed)&&wanted.Add(entry)) work.Add(entry); }
        void Sphere(Vector3 center,float radius,bool terrainOnly=false)
        {
            candidates.Clear();float squared=radius*radius;
            for(int z=Mathf.FloorToInt((center.z-radius)/CellSize);z<=Mathf.FloorToInt((center.z+radius)/CellSize);z++)
            for(int x=Mathf.FloorToInt((center.x-radius)/CellSize);x<=Mathf.FloorToInt((center.x+radius)/CellSize);x++)
                if(cells.TryGetValue(new Vector2Int(x,z),out var bucket)) foreach(var entry in bucket) candidates.Add(entry);
            LastCandidateCount+=candidates.Count;
            foreach(var entry in candidates) if(entry.Target!=null&&
                (!terrainOnly||entry.Terrain)&&entry.Bounds.SqrDistance(center)<=squared) Need(entry);
        }
        void MovingSphere(Vector3 point,Vector3 velocity)
        {
            Sphere(point,CollisionRange);
            Vector3 travel=velocity*Time.fixedDeltaTime;
            int steps=Mathf.CeilToInt(travel.magnitude/10f);
            for(int step=1;step<=steps;step++) Sphere(point+travel*(step/(float)steps),CollisionRange);
        }
        bool Moving(Entry entry,out Vector3 velocity)
        {
            velocity=Vector3.zero;
            if(!entry.Target.activeInHierarchy||entry.Physical!=null&&entry.Physical.HasBeenDestroyed) return false;
            if(entry.Pickup!=null&&entry.Pickup.IsHeld) { velocity=entry.Pickup.HeldPointVelocity(entry.Target.transform.position);return true; }
            foreach(var body in entry.Bodies) if(body!=null) {
                if(!body.isKinematic&&!body.IsSleeping()) { velocity=body.GetPointVelocity(entry.Bounds.center);return true; }
                if(body.isKinematic&&body.gameObject!=entry.Target) {
                    var carrier=body.GetComponent<PickableObject>();
                    if(carrier!=null&&carrier.IsHeld) { velocity=carrier.HeldPointVelocity(entry.Bounds.center);return true; }
                    if(WebWorldSession.LoadingMotion(body.gameObject,out velocity)) return true;
                }
            }
            return WebWorldSession.LoadingMotion(entry.Target,out velocity);
        }
        internal void Refresh()
        {
            if(!initialized||closing||observer==null) return;
            pendingWork.Clear();pendingWork.AddRange(pending);pending.Clear();
            // Falling trees and spawned debris deliberately leave the world root.
            // Explicit lifecycle notifications still belong to this local world.
            foreach(var target in pendingWork) if(target!=null&&target.scene==world.gameObject.scene) RefreshSubtree(target);
            if(pendingWork.Count>0) changed=true;
            bool rebuild=changed||Time.realtimeSinceStartup>=nextPinExpiry||(observer.position-lastObserver).sqrMagnitude>.0001f;
            changes.Clear();
            foreach(var entry in dynamicEntries) {
                if(entry.Target==null) { changes.Add(entry);continue; }
                bool moving=Moving(entry,out _);rebuild|=moving||entry.WasMoving;
                if(moving||entry.WasMoving) { Unindex(entry);UpdateBounds(entry);Index(entry); }
                entry.WasMoving=moving;
            }
            foreach(var entry in changes) { Remove(entry);rebuild=true; }
            if(!rebuild) { refreshedFrame=Time.frameCount;return; }
            changed=false;lastObserver=observer.position;wanted.Clear();work.Clear();LastCandidateCount=0;
            Sphere(observer.position,LoadingRange);
            Sphere(observer.position,LoadingRange*TerrainRangeMultiplier,true);
            foreach(var point in playerProbes.Values) Sphere(point,CollisionRange);
            foreach(var entry in dynamicEntries) if(entry.Target!=null&&entry.WasMoving) {
                Need(entry);Moving(entry,out Vector3 velocity);MovingSphere(entry.Bounds.center,velocity);
                if(entry.Rope!=null) foreach(var body in entry.Rope.Bodies) if(body!=null) MovingSphere(body.position,body.isKinematic?velocity:body.linearVelocity);
            }
            nextPinExpiry=float.PositiveInfinity;
            foreach(var entry in loaded) if(entry.Target!=null&&entry.PinnedUntil>Time.realtimeSinceStartup) { Need(entry);nextPinExpiry=Mathf.Min(nextPinExpiry,entry.PinnedUntil); }
            // Structural/ancestor dependencies retain anchors, independently of
            // another player's graphics range. GameObjects themselves stay valid.
            for(int i=0;i<work.Count;i++) {
                var entry=work[i];Need(Owner(entry.Target.transform.parent));
                if(entry.Rope!=null&&entry.Rope.LoadingSupport!=null) Need(Owner(entry.Rope.LoadingSupport));
                if(entry.Tree!=null&&entry.Tree.LoadingSupport!=null) Need(Owner(entry.Tree.LoadingSupport));
            }
            changes.Clear();foreach(var entry in loaded) if(!wanted.Contains(entry)) changes.Add(entry);
            foreach(var entry in changes) SetLoaded(entry,false);
            foreach(var entry in work) if(!entry.Loaded) SetLoaded(entry,true);
            RestoreSleeping();
            ResidentPropRendering.FlushLoadingChanges();refreshedFrame=Time.frameCount;
        }
        void SetLoaded(Entry entry,bool value)
        {
            if(entry.Target==null) { loaded.Remove(entry);return; }
            foreach(var body in entry.Bodies) if(body!=null&&!body.isKinematic&&body.IsSleeping()) keepSleeping.Add(body);
            foreach(var renderer in entry.Renderers) if(renderer!=null) { renderer.forceRenderingOff=!value;ResidentPropRendering.SetLoaded(renderer,value); }
            foreach(var state in entry.Colliders) if(state.Value!=null) {
                if(!value) state.Enabled=state.Value.enabled;
                state.Value.enabled=value&&state.Enabled;
            }
            foreach(var state in entry.Scripts) if(state.Value!=null) {
                if(!value) state.Enabled=state.Value.enabled;
                state.Value.enabled=value&&state.Enabled;
            }
            entry.Loaded=value;if(value) loaded.Add(entry);else loaded.Remove(entry);
        }
        void RefreshSubtree(GameObject target)
        {
            CollectAffected(target,true);
            foreach(var entry in affected) if(entry.Target!=null) {
                if(!entry.Loaded) SetLoaded(entry,true);
                Populate(entry);
            }
            RestoreSleeping();
        }
        void CollectAffected(GameObject target,bool add)
        {
            affected.Clear();var owner=Owner(target.transform);if(owner!=null) affected.Add(owner);
            void Include(GameObject value) { if(add) affected.Add(Add(value));else if(entries.TryGetValue(value,out var entry)) affected.Add(entry); }
            foreach(var physical in target.GetComponentsInChildren<PhysicalDestructible>(true)) Include(physical.gameObject);
            foreach(var rope in target.GetComponentsInChildren<RopeObject>(true)) Include(rope.gameObject);
            foreach(var pickup in target.GetComponentsInChildren<PickableObject>(true)) Include(pickup.gameObject);
            foreach(var tree in target.GetComponentsInChildren<GeneratedTree>(true)) Include(tree.gameObject);
        }
        void RestoreSleeping() { foreach(var body in keepSleeping) if(body!=null&&!body.isKinematic) body.Sleep();keepSleeping.Clear(); }
        public static void NotifyChanged(GameObject target)
        {
            if(current==null||!current.initialized||current.closing||target==null) return;
            current.pending.Add(target);
        }
        public static void BeforeChange(GameObject target)
        {
            if(current==null||!current.initialized||current.closing||target==null) return;
            current.CollectAffected(target,false);
            current.work.Clear();current.work.AddRange(current.affected);
            void Support(Entry entry) { if(entry!=null&&current.affected.Add(entry)) current.work.Add(entry); }
            for(int i=0;i<current.work.Count;i++) {
                var entry=current.work[i];Support(current.Owner(entry.Target.transform.parent));
                if(entry.Rope!=null&&entry.Rope.LoadingSupport!=null) Support(current.Owner(entry.Rope.LoadingSupport));
                if(entry.Tree!=null&&entry.Tree.LoadingSupport!=null) Support(current.Owner(entry.Tree.LoadingSupport));
            }
            foreach(var entry in current.affected) if(entry.Target!=null) {
                if(!entry.Loaded) current.SetLoaded(entry,true);
                entry.PinnedUntil=Time.realtimeSinceStartup+.3f;
            }
            current.RestoreSleeping();
            current.changed=true;
        }
        public static void TrackMovement(GameObject target)
        {
            if(current==null||!current.initialized||current.closing||target==null) return;
            if(current.entries.TryGetValue(target,out var entry)) {
                current.dynamicEntries.Add(entry);
                if(entry.Bodies.Count==0&&!entry.WasMoving) current.pending.Add(target);
            }else current.pending.Add(target);
            current.changed=true;
        }
        public static void Forget(GameObject target)
        {
            if(current==null||current.closing||target==null||!current.entries.TryGetValue(target,out var entry)) return;
            current.Remove(entry);
        }
        void Remove(Entry entry)
        {
            Unindex(entry);loaded.Remove(entry);dynamicEntries.Remove(entry);pending.Remove(entry.Target);entries.Remove(entry.Target);
            if(entry.Physical!=null) entry.Physical.GeometryChanged-=entry.Changed;
            changed=true;
        }
        public static void SetPlayerProbe(string id,Vector3 position) {
            if(current==null) return;
            if(!current.playerProbes.TryGetValue(id,out var previous)||(previous-position).sqrMagnitude>.0001f) {
                current.playerProbes[id]=position;current.changed=true;
            }
        }
        public static void RemovePlayerProbe(string id) { if(current!=null) { current.playerProbes.Remove(id);current.changed=true; } }
        internal bool IsLoaded(GameObject target)=>entries.TryGetValue(target,out var entry)&&entry.Loaded;
        void FixedUpdate()=>Refresh();
        void LateUpdate() { if(refreshedFrame!=Time.frameCount||pending.Count>0||changed) Refresh(); }
        void OnDestroy()
        {
            closing=true;
            foreach(var entry in entries.Values) {
                if(entry.Physical!=null) entry.Physical.GeometryChanged-=entry.Changed;
                if(!entry.Loaded) SetLoaded(entry,true);
            }
            RestoreSleeping();ResidentPropRendering.FlushLoadingChanges();
            if(current==this) current=null;
        }
    }
}
