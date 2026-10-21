using System.Linq;
using UnityEngine;

namespace Wyvernland
{
    public sealed class TestGroundIronBar : MonoBehaviour { }
    public sealed class TestGroundBars : MonoBehaviour
    {
        ProceduralMap map;FirstPersonController player;Vector3 anchor,right,forward;float nextCheck;
        public void Initialize(ProceduralMap ground,FirstPersonController controller,GameObject first)
        {
            map=ground;player=controller;anchor=first.transform.position;right=controller.transform.right;forward=controller.transform.forward;
        }
        void Update()
        {
            if(Time.unscaledTime<nextCheck||!WebWorldSession.IsPhysicsHost||WebWorldSession.RoomPlayerCount<1) return;
            nextCheck=Time.unscaledTime+.5f;Reconcile(WebWorldSession.RoomPlayerCount);
        }
        internal void Reconcile(int count)
        {
            if(!WebWorldSession.IsPhysicsHost||count<1||player==null) return;
            var bars=FindObjectsByType<TestGroundIronBar>(FindObjectsSortMode.None)
                .Where(b=>!string.IsNullOrEmpty(WebWorldSession.IdOf(b.gameObject))&&b.GetComponent<PhysicalDestructible>() is { HasBeenDestroyed:false })
                .OrderBy(b=>b.GetComponent<PickableObject>() is { IsHeld:true }?1:0).ToList();
            while(bars.Count>count) {
                var bar=bars[0];bars.RemoveAt(0);
                // World removal also releases a grip if every surplus bar is held.
                WebWorldSession.RemoveRoomObject(bar.gameObject);
            }
            for(int i=bars.Count;i<count;i++) {
                var bar=WorldBootstrap.CreateIronTestBar(gameObject,map,player);
                bar.transform.position=anchor+forward*(i%8*.3f)+right*(i/8*1.65f);
                WebWorldSession.RegisterSpawn(bar);PlayerWorldLoading.NotifyChanged(bar);
            }
        }
    }
}
