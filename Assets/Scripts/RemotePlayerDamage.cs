using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wyvernland
{
    // Each player owns their injuries and bone simulation. Room props remain
    // host simulated, including the removable grip joints on replicated corpses.
    public sealed class RemotePlayerDamage : MonoBehaviour
    {
        internal string PlayerId;
        internal bool IsAlive=>state==null||(state.injury&(int)PlayerInjury.Dead)==0;
        internal int Life=>state?.life??0;
        Transform model;PlayerRagdoll rig;PlayerDamage.State state;
        SkinnedMeshRenderer[] skins;Mesh[] originals;
        readonly List<Mesh> meshes=new();readonly Dictionary<string,PlayerCorpse> corpses=new();
        int life=-1,pits;
        int severed;
        public void Initialize(Transform visual)
        {
            model=visual;skins=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            originals=skins.Select(s=>s.sharedMesh).ToArray();
            rig=gameObject.AddComponent<PlayerRagdoll>();rig.Initialize(model,null,true);
        }
        public void Apply(PlayerDamage.State next)
        {
            next??=new PlayerDamage.State();state=next;
            if(life!=next.life) {
                life=next.life;pits=0;severed=0;rig.ResetPose();
                for(int i=0;i<skins.Length;i++) skins[i].sharedMesh=originals[i];
            }
            rig.SetFullBodyStun(next.shockPhase>0);rig.SetInjury((PlayerInjury)next.injury);
            int newMissing=next.severed&~severed;if(newMissing!=0) PlayerDamageMesh.RemoveLimbs(model,newMissing,meshes);
            severed=next.severed;rig.SetMissingLimbs(severed);
            if(next.pits!=null) while(pits<next.pits.Length&&pits<12) { PlayerDamage.CutTorso(model,PlayerDamage.PitPoint(model,transform,next.pits[pits]),meshes,next.pits[pits].id,next.pits[pits].incoming,transform);pits++; }
            foreach(var corpse in (next.corpses??System.Array.Empty<PlayerDamage.CorpseState>()).Concat(next.limbs??System.Array.Empty<PlayerDamage.CorpseState>())) {
                if(string.IsNullOrEmpty(corpse.id)) continue;
                if(!corpses.TryGetValue(corpse.id,out var body)||body==null) {
                    body=PlayerCorpse.Find(corpse.id);
                    if(body==null&&corpse.limb>0) body=PlayerCorpse.Create(corpse,transform.parent);
                    if(body==null) {
                    var node=new GameObject(corpse.id);node.transform.SetParent(transform.parent,false);
                    node.transform.SetPositionAndRotation(corpse.position,corpse.rotation);
                    var visual=Instantiate(model.gameObject,node.transform,false).transform;visual.gameObject.SetActive(true);
                    foreach(var behaviour in visual.GetComponentsInChildren<MonoBehaviour>()) { behaviour.enabled=false;if(behaviour is not PlayerHitShape) PlayerDamage.Release(behaviour); }
                    body=node.AddComponent<PlayerCorpse>();body.Initialize(visual,true,reference:rig,limb:corpse.limb,spawnFragments:corpse.limb==0);
                    } corpses[corpse.id]=body;
                }
                body.ApplyPresence(corpse);
            }
            foreach(var id in corpses.Keys.ToArray()) if(!(next.corpses??System.Array.Empty<PlayerDamage.CorpseState>()).Concat(next.limbs??System.Array.Empty<PlayerDamage.CorpseState>()).Any(c=>c.id==id)) { corpses.Remove(id); }
            model.gameObject.SetActive((next.injury&(int)PlayerInjury.Dead)==0);
        }
        public void PoseAfterAnimation()
        {
            if(state==null) return;rig.ApplyPose(state.pose,entire:state.fullPose||state.shockPhase>0,woundedArms:PlayerDamage.WoundedArms(state),woundedLegs:PlayerDamage.WoundedLegs(state));
        }
        void OnDestroy() { foreach(var mesh in meshes) PlayerDamage.Release(mesh); }
    }
}
