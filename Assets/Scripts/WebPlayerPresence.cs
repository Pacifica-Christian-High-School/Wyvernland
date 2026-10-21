using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wyvernland
{
    // Player avatars interpolate room poses; WebWorldSession owns shared props.
    public sealed class WebPlayerPresence : MonoBehaviour
    {
        [Serializable] public sealed class PlayerState
        {
            public string id, room, name;
            public int color;
            public float x, y, z, yaw, pitch, speed;
            public bool crouching, grounded, paused;
            public PlayerDamage.State damage;
        }
        [Serializable] public sealed class Snapshot { public PlayerState[] players; }
        private readonly Dictionary<string, RemotePlayer> remotes = new();
        private FirstPersonController player;
        private float nextPublish, lastSnapshot;
        private GUIStyle labelStyle;
        static WebPlayerPresence active;
        readonly List<(PlayerDamage.Impact impact,float until)> impacts=new();
        readonly Dictionary<string,float> impactCooldowns=new();

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void WyvernlandPublishPlayer(string json);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void WyvernlandLeaveOnline();
#endif
        public int RemotePlayerCount => remotes.Count;
        public void Initialize(FirstPersonController controller) { player = controller;active=this; }
        internal static PlayerDamage.Impact[] CaptureImpacts()
        {
            if(active==null) return null;
            if(!WebWorldSession.IsPhysicsHost) { active.impacts.Clear();return null; }
            active.impacts.RemoveAll(e=>Time.unscaledTime>=e.until);
            return active.impacts.ConvertAll(e=>e.impact).ToArray();
        }
        internal static void QueueImpact(RemotePlayerDamage victim,PhysicalDestructible source,
            PlayerBodyPart part,Transform bone,Vector3 point,Vector3 normal,Vector3 velocity)
        {
            if(active==null||victim==null||!WebWorldSession.IsPhysicsHost||source==null||string.IsNullOrEmpty(victim.PlayerId)) return;
            string id=WebWorldSession.IdOf(source.gameObject);float threshold=PlayerDamage.RequiredSpeed(source,part);
            float mass=Mathf.Min(1e6f,source.GetComponentInParent<Rigidbody>()?.mass??0f);
            if(string.IsNullOrEmpty(id)||!float.IsFinite(velocity.sqrMagnitude)||
                Vector3.Dot(velocity,normal.normalized)<.5f||PlayerDamage.ShockImpulse(mass,Vector3.Dot(velocity,normal.normalized))<2f&&velocity.magnitude<threshold) return;
            string pair=id+victim.PlayerId;
            if(active.impactCooldowns.TryGetValue(pair,out float until)&&Time.unscaledTime<until) return;
            if(active.impactCooldowns.Count>512) active.impactCooldowns.Clear();active.impactCooldowns[pair]=Time.unscaledTime+.25f;
            if(active.impacts.Count>=8) active.impacts.RemoveAt(0);
            active.impacts.Add((new PlayerDamage.Impact { id=Guid.NewGuid().ToString("N"),source=id,target=victim.PlayerId,life=victim.Life,
                part=(int)part,bone=bone!=null?bone.name:"",point=(bone!=null?bone:victim.transform).InverseTransformPoint(point),normal=normal,velocity=velocity,threshold=threshold,mass=mass },Time.unscaledTime+30f));
        }

        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (player != null && Time.unscaledTime >= nextPublish)
            {
                nextPublish = Time.unscaledTime + 0.1f;
                Vector3 p = player.transform.position;
                WyvernlandPublishPlayer(JsonUtility.ToJson(new PlayerState {
                    room = WorldBootstrap.TestGround ? "test-ground" : "island",
                    x = p.x, y = p.y, z = p.z, yaw = player.transform.eulerAngles.y,
                    pitch = player.LookPitch, speed = player.IsPaused ? 0f : Mathf.Min(100f, player.PlanarSpeed),
                    crouching = player.IsCrouching, grounded = player.IsGrounded,
                    paused = player.IsPaused,
                    damage=player.GetComponent<PlayerDamage>()?.Capture(),
                }));
            }
#endif
            if (remotes.Count > 0 && Time.unscaledTime - lastSnapshot > 8f) ClearPlayers();
            foreach (RemotePlayer remote in remotes.Values) { remote.Tick(Time.unscaledDeltaTime);PlayerWorldLoading.SetPlayerProbe(remote.State.id,remote.Root.position); }
        }

        // Called by the page through Unity SendMessage; never accepts executable content.
        [UnityEngine.Scripting.Preserve]
        public void ReceivePlayers(string json)
        {
            Snapshot snapshot;
            try { snapshot = JsonUtility.FromJson<Snapshot>(json); }
            catch (Exception) { return; }
            if (snapshot?.players == null || snapshot.players.Length > 128) return;
            lastSnapshot = Time.unscaledTime;
            var alive = new HashSet<string>();
            foreach (PlayerState state in snapshot.players)
            {
                if (string.IsNullOrEmpty(state.id) || !Finite(state.x) || !Finite(state.y) ||
                    !Finite(state.z) || !Finite(state.yaw) || !Finite(state.pitch) || !Finite(state.speed)) continue;
                alive.Add(state.id);
                if(state.damage!=null) impacts.RemoveAll(e=>e.impact.target==state.id&&(e.impact.life!=state.damage.life||
                    state.damage.received!=null&&Array.IndexOf(state.damage.received,e.impact.id)>=0));
                if(state.damage?.impacts!=null) foreach(var impact in state.damage.impacts)
                    if(impact.target==WebWorldSession.LocalPlayerId) player?.GetComponent<PlayerDamage>()?.ReceiveNetworkImpact(impact);
                if (!remotes.TryGetValue(state.id, out RemotePlayer remote))
                {
                    remote = new RemotePlayer(transform, state);
                    remotes.Add(state.id, remote);
                }
                remote.Apply(state);
            }
            var departed = new List<string>();
            foreach (string id in remotes.Keys) if (!alive.Contains(id)) departed.Add(id);
            foreach (string id in departed) { PlayerWorldLoading.RemovePlayerProbe(id);remotes[id].Dispose(); remotes.Remove(id); }
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void ClearPlayers()
        {
            foreach (RemotePlayer remote in remotes.Values) { PlayerWorldLoading.RemovePlayerProbe(remote.State.id);remote.Dispose(); }
            remotes.Clear();
        }
        private void OnDestroy()
        {
            if(active==this) active=null;
#if UNITY_WEBGL && !UNITY_EDITOR
            WyvernlandLeaveOnline();
#endif
            ClearPlayers();
        }
        private void OnGUI()
        {
            if (player == null || player.IsPaused || player.PlayerCamera == null) return;
            labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
                fontSize = 13, richText = false };
            Camera camera = player.PlayerCamera;
            foreach (RemotePlayer remote in remotes.Values)
            {
                Vector3 point = remote.Root.position + Vector3.up * (remote.State.crouching ? 1.65f : 2.05f);
                if (Vector3.Distance(point, camera.transform.position) > 100f) continue;
                Vector3 screen = camera.WorldToScreenPoint(point);
                if (screen.z <= 0 || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) continue;
                // Terrain and props occlude labels as well as the body.
                if (Physics.Linecast(camera.transform.position, point, out RaycastHit hit,
                    ~(1 << 2 | 1 << 31), QueryTriggerInteraction.Ignore)&&!hit.transform.IsChildOf(remote.Root)) continue;
                var rect = new Rect(screen.x - 85, Screen.height - screen.y - 20, 170, 24);
                GUI.Box(rect, GUIContent.none);
                GUI.Label(rect, remote.State.name + (remote.State.paused ? " · paused" : ""), labelStyle);
            }
        }

        private sealed class RemotePlayer
        {
            internal readonly Transform Root;
            internal PlayerState State;
            private readonly Material material;
            private readonly Transform model;
            private readonly Mesh wristMesh,neckMesh;
            private readonly Transform[] joints = new Transform[6];
            private readonly Quaternion[] rest = new Quaternion[6];
            private readonly Vector3[] axes = new Vector3[6];
            private Vector3 target;
            private float phase;
            private readonly RemotePlayerDamage damage;
            internal RemotePlayer(Transform parent, PlayerState state)
            {
                Root = new GameObject("Online player " + state.id).transform;
                Root.SetParent(parent, false);
                Root.position = new Vector3(state.x, state.y, state.z);
                Root.rotation = Quaternion.Euler(0, state.yaw, 0);
                GameObject asset = Resources.Load<GameObject>("Player/HumanRiggedEdited") ??
                    Resources.Load<GameObject>("Player/HumanRigged");
                if (asset != null)
                {
                    model = UnityEngine.Object.Instantiate(asset, Root, false).transform;
                    foreach (Animator animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
                    foreach (Collider collider in model.GetComponentsInChildren<Collider>()) { collider.enabled=false;Release(collider); }
                    foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>()) { body.detectCollisions=false;Release(body); }
                    wristMesh=RiggedPlayerAvatar.EnsureWristJoints(model.gameObject);
                    neckMesh=PlayerRagdoll.PrepareRemoteNeck(model);
                    string[] names = { "LeftHip", "RightHip", "LeftShoulder", "RightShoulder", "LeftKnee", "RightKnee" };
                    for (int i = 0; i < names.Length; i++)
                        foreach (Transform bone in model.GetComponentsInChildren<Transform>())
                            if (bone.name == names[i]) {
                                joints[i] = bone; rest[i] = bone.localRotation;
                                axes[i] = bone.parent.InverseTransformDirection(Root.right).normalized; break;
                            }
                }
                else
                {
                    model = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
                    model.SetParent(Root, false); model.localPosition = Vector3.up * .9f;
                    model.localScale = new Vector3(.5f,.9f,.5f);
                    Release(model.GetComponent<Collider>());
                }
                material = new Material(Resources.Load<Shader>("Clay"));
                material.SetColor("_BaseColor", RiggedPlayerAvatar.PlayerColor);
                material.SetFloat("_TerrainColorRegions", 0);
                foreach (Transform part in model.GetComponentsInChildren<Transform>()) part.gameObject.layer = 0;
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                {
                    var slots = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < slots.Length; i++) slots[i] = material;
                    renderer.sharedMaterials = slots;
                    if (renderer is SkinnedMeshRenderer skin) {
                        skin.localBounds = new Bounds(Vector3.up * .9f, Vector3.one * 4f);
                    }
                    ColorOnlyTextureQuality.ApplyRendererQuality(renderer);
                }
                var avatarBody=Root.gameObject.AddComponent<Rigidbody>();
                avatarBody.isKinematic=true;avatarBody.useGravity=false;
                avatarBody.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                Root.gameObject.AddComponent<PlayerRigColliders>().Initialize(model,0);
                damage=Root.gameObject.AddComponent<RemotePlayerDamage>();damage.Initialize(model);
                damage.PlayerId=state.id;
                Apply(state);
            }
            internal void Apply(PlayerState state) {
                State = state; target = new Vector3(state.x, state.y, state.z);
                if ((Root.position - target).sqrMagnitude > 400f) Root.position = target;
                damage?.Apply(state.damage);
            }
            internal void Tick(float dt)
            {
                float blend = 1f - Mathf.Exp(-12f * Mathf.Min(dt, .1f));
                Root.position = Vector3.Lerp(Root.position, target, blend);
                Root.rotation = Quaternion.Slerp(Root.rotation, Quaternion.Euler(0, State.yaw, 0), blend);
                float speed = State.paused ? 0 : State.speed;
                phase += dt * Mathf.Clamp(speed * 1.5f, 0, 12);
                float swing = State.grounded ? Mathf.Sin(phase) * Mathf.Clamp(speed * 6f, 0, 38) : 0;
                float bend = State.crouching ? 45 : 10;
                for (int i = 0; i < joints.Length; i++)
                    if (joints[i] != null) {
                        float angle = i < 2 ? bend + (i == 0 ? swing : -swing)
                            : i < 4 ? (i == 2 ? -swing : swing) * .7f : -bend;
                        joints[i].localRotation = Quaternion.AngleAxis(angle, axes[i]) * rest[i];
                    }
                if (model != null && model.name != "Capsule")
                    model.localPosition = Vector3.up * (State.crouching ? -.32f : 0);
                damage?.PoseAfterAnimation();
            }
            internal void Dispose() {
                Release(Root.gameObject);
                Release(material);
                Release(wristMesh);Release(neckMesh);
            }
            static void Release(UnityEngine.Object value)
            { if(Application.isPlaying) UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value); }
        }
    }
}
