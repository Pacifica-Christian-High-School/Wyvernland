using System;
using System.Linq;
using UnityEngine;

namespace Wyvernland
{
    [DefaultExecutionOrder(-50)]
    public sealed class CombatNpc : MonoBehaviour
    {
        FirstPersonController controller;
        RiggedPlayerAvatar avatar;
        PlayerDamage damage;
        PickableObject weapon;
        Transform target;
        float nextTarget,attackStarted,nextAttack;
        internal Transform RightHand=>avatar.NpcRightHand;
        internal static CombatNpc Create(Transform parent,Vector3 position,bool equip=true)
        {
            var dummy=TrainingDummy.Create(parent,null,mobile:true);
            var npc=dummy.gameObject.AddComponent<CombatNpc>();
            npc.name="Iron bar attacker";npc.controller=npc.GetComponent<FirstPersonController>();
            npc.avatar=npc.GetComponent<RiggedPlayerAvatar>();npc.damage=dummy.Damage;
            npc.transform.position=position;npc.controller.ConfigureAiActor();
            if(equip) npc.Equip();
            return npc;
        }
        void Equip()
        {
            var bar=WorldBootstrap.CreateIronTestBar(transform.parent!=null?transform.parent.gameObject:gameObject,null,controller,stock:false);
            bar.name="Attacker iron bar";bar.transform.position=RightHand.position;
            weapon=bar.GetComponent<PickableObject>();weapon.NpcOwner=this;
            WebWorldSession.RegisterSpawn(bar);
            avatar.BindNpcWeapon(weapon);
            if(!weapon.PickUp(RightHand)) avatar.BindNpcWeapon(null);
            WebWorldSession.Dirty(bar);
        }
        internal void BindWeapon(PickableObject item,Vector3 localPosition,Quaternion localRotation)
        {
            weapon=item;item.NpcOwner=this;
            if(!item.IsHeld) item.PickUp(RightHand);
            avatar.BindNpcWeapon(item);item.transform.SetParent(RightHand,false);
            item.transform.localPosition=localPosition;item.transform.localRotation=localRotation;
        }
        internal Transform FindNearestPlayer()
        {
            Transform nearest=null;float best=float.PositiveInfinity;
            void Consider(Transform candidate) {
                float distance=(candidate.position-transform.position).sqrMagnitude;
                if(distance<best) { best=distance;nearest=candidate; }
            }
            foreach(var player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
                if(player.GetComponent<TrainingDummy>()==null&&!player.IsDead&&!player.IsPaused) Consider(player.transform);
            foreach(var remote in FindObjectsByType<RemotePlayerDamage>(FindObjectsSortMode.None))
                if(remote.IsAlive) Consider(remote.transform);
            return nearest;
        }
        void Update()
        {
            if(!WebWorldSession.IsPhysicsHost||damage.IsDead||damage.IsShocked||Time.deltaTime<=0f) return;
            if(Time.time>=nextTarget) { target=FindNearestPlayer();nextTarget=Time.time+.2f; }
            if(target==null) { controller.DriveAi(Vector2.zero,Vector2.zero);avatar.CancelRightHandStrike();return; }
            Vector3 delta=target.position-transform.position;delta.y=0;
            if(delta.sqrMagnitude>.01f) transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(delta),240f*Time.deltaTime);
            controller.AimAiAt(target.position+Vector3.up*1.1f);
            bool near=delta.sqrMagnitude<2.4f*2.4f;
            if(weapon!=null&&!weapon.IsHeld&&avatar.CanUseHand(false)&&avatar.HeldRightHandItem==null&&
                Vector3.Distance(weapon.transform.position,RightHand.position)<1.8f) {
                weapon.NpcOwner=this;
                if(weapon.PickUp(RightHand)) avatar.BindNpcWeapon(weapon);
            }
            if(near&&avatar.HeldRightHandItem!=null&&Time.time>=nextAttack&&!avatar.IsHoldingStrikePose(weapon)) {
                if(avatar.BeginRightHandStrike()) attackStarted=Time.time;
            }
            Vector2 wave=Vector2.zero;
            if(avatar.IsStrikeMode) {
                float t=Time.time-attackStarted;
                wave=new Vector2(Mathf.Cos(t*11f)*800f,Mathf.Sin(t*8f)*250f)*Time.deltaTime;
                if(t>2.7f||!near) { avatar.UpdateRightHandStrikeInput(false,true);nextAttack=Time.time+.7f;wave=Vector2.zero; }
            }
            bool chasing=delta.sqrMagnitude>1.5f*1.5f;
            controller.DriveAi(chasing?Vector2.up:Vector2.zero,wave,
                chasing&&delta.sqrMagnitude>4f*4f);
        }
    }
}
