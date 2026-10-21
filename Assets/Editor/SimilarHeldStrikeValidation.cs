using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class SimilarHeldStrikeValidation
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Prefix="Similar strike validation ";

        [MenuItem("Tools/Wyvernland/Validate Similar Held Strikes")]
        public static void Run()
        {
            var random=UnityEngine.Random.state;
            var root=new GameObject(Prefix+"root");
            try
            {
                int destroyed=0,expected=0;
                // Exercise actual fracture geometry at evenly spaced rolls,
                // rather than statistically flaky sampling of the constant.
                for(int trial=0;trial<20;trial++)
                {
                    var source=Solid(root,"source "+trial,Vector3.zero);
                    var target=Solid(root,"target "+trial,Vector3.right);
                    source.Resilience=9f;target.Resilience=1f;
                    float roll=(trial+0.5f)/20f;
                    bool shouldDestroy=roll<PhysicalDestructible.SimilarDestructionChance(source.Hardness,source.Resilience,target.Hardness,target.Resilience);
                    if(shouldDestroy) expected++;
                    bool result=source.ResolveHeldStrikeDestruction(target,
                        target.transform.position-Vector3.right*0.2f,Vector3.right*40f,roll);
                    Check(result==shouldDestroy,"One collision uses both objects' hardness and resilience: "+trial);
                    if(result) destroyed++;
                    else Check(!source.HasBeenDestroyed&&!target.HasBeenDestroyed,
                        "A failed probability roll leaves both solids intact");
                    CleanupSolids();
                }
                Check(destroyed==expected&&destroyed==6,"Pairs with a stat below seven retain the base 30% chance");
                ValidateStrengthCurve(root);
                var held=Solid(root,"cached source",Vector3.zero);
                var other=Solid(root,"cached target",Vector3.right);
                float boundary=PhysicalDestructible.SimilarDestructionChance(held.Hardness,held.Resilience,other.Hardness,other.Resilience);
                Check(!held.ResolveHeldStrikeDestruction(other,Vector3.right*0.8f,Vector3.right*40f,boundary),
                    "The exact probability boundary bounces");
                Check(!held.ResolveHeldStrikeDestruction(other,Vector3.right*0.8f,Vector3.right*40f,0f),
                    "Duplicate contact callbacks cannot reroll a failed impact");
                CleanupSolids();
                held=Solid(root,"slow source",Vector3.zero);
                other=Solid(root,"slow target",Vector3.right);
                Check(!held.ResolveHeldStrikeDestruction(other,Vector3.right*0.8f,Vector3.right,0f),
                    "A winning roll still needs the material impact speed");
                other.Hardness=held.Hardness+2f;
                Check(held.IsSimilarHardStrike(other),"The hardness similarity boundary is inclusive");
                other.Hardness+=0.01f;
                Check(!held.IsSimilarHardStrike(other),"A hardness gap greater than two uses normal rules");
                CleanupSolids();
                ValidateBounce(root);
                var command=new WebWorldSession.Command { type="strike",hasStrikeRoll=true,strikeRoll=0.7f };
                var replay=JsonUtility.FromJson<WebWorldSession.Command>(JsonUtility.ToJson(command));
                Check(replay.hasStrikeRoll&&replay.strikeRoll==0.7f,"Multiplayer preserves the original contact roll");
                Debug.Log("SIMILAR_HELD_STRIKE_VALIDATION_PASS: strength-dependent shared collision probability, exact 1% at hardness/resilience ten, real fracture geometry, hardness-only similarity, duplicate-contact protection, speed gate, hand rebound, and network roll preservation.");
            }
            finally
            {
                CleanupSolids();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Random.state=random;
            }
        }

        static void ValidateStrengthCurve(GameObject root)
        {
            for(int value=0;value<7;value++) {
                Check(Mathf.Approximately(PhysicalDestructible.SimilarDestructionChance(value,10,10,10),.3f)&&
                    Mathf.Approximately(PhysicalDestructible.SimilarDestructionChance(10,value,10,10),.3f)&&
                    Mathf.Approximately(PhysicalDestructible.SimilarDestructionChance(10,10,value,10),.3f)&&
                    Mathf.Approximately(PhysicalDestructible.SimilarDestructionChance(10,10,10,value),.3f),
                    "Every stat must reach seven before the chance can decrease");
            }
            Check(Mathf.Approximately(PhysicalDestructible.SimilarDestructionChance(6.999f,10,10,10),.3f),"The seven threshold is strict on all four stats");
            Check(Mathf.Approximately(PhysicalDestructible.SimilarDestructionChance(7,7,7,7),.3f),"All-sevens starts at 30%");
            for(int value=7;value<10;value++) {
                float start=PhysicalDestructible.SimilarDestructionChance(value,value,value,value);
                Check(PhysicalDestructible.SimilarDestructionChance(value+1,value,value,value)<start&&
                    PhysicalDestructible.SimilarDestructionChance(value,value+1,value,value)<start&&
                    PhysicalDestructible.SimilarDestructionChance(value,value,value+1,value)<start&&
                    PhysicalDestructible.SimilarDestructionChance(value,value,value,value+1)<start,
                    "Increasing either object's hardness or resilience lowers destruction chance");
                float next=PhysicalDestructible.SimilarDestructionChance(value+1,value+1,value+1,value+1);
                Check(Mathf.Abs((start-next)-.29f/3f)<.00001f,"The decrease from seven to ten is evenly spaced");
                float delta=start-PhysicalDestructible.SimilarDestructionChance(value+1,value,value,value);
                Check(Mathf.Abs(delta-.29f/12f)<.00001f,"All four stats contribute equally");
            }
            Check(Mathf.Approximately(PhysicalDestructible.SimilarDestructionChance(5,2,6,9),PhysicalDestructible.SimilarDestructionChance(6,9,5,2)),"Pair order cannot change collision chance");
            int destroyed=0;
            for(int trial=0;trial<100;trial++) {
                var source=Solid(root,"maximum source "+trial,Vector3.zero);
                var target=Solid(root,"maximum target "+trial,Vector3.right);
                source.Hardness=target.Hardness=source.Resilience=target.Resilience=10;
                bool result=source.ResolveHeldStrikeDestruction(target,target.transform.position,Vector3.right*40,(trial+.5f)/100);
                Check(result==(trial==0),"Maximum-strength solids have one destruction interval per hundred collision rolls");
                if(result) destroyed++;
                CleanupSolids();
            }
            Check(destroyed==1,"Both hardness and resilience at ten produce exactly a one percent collision chance");
        }

        static void ValidateBounce(GameObject root)
        {
            var actor=new GameObject(Prefix+"actor");actor.transform.SetParent(root.transform);
            var avatar=actor.AddComponent<RiggedPlayerAvatar>();
            var shoulder=new GameObject("Shoulder").transform;shoulder.SetParent(actor.transform);
            var elbow=new GameObject("Elbow").transform;elbow.SetParent(shoulder);
            var hand=new GameObject("Hand").transform;hand.SetParent(elbow);
            var source=Solid(root,"bounce source",Vector3.zero);
            source.transform.localScale=new Vector3(0.03f,0.03f,0.3f);
            var pickup=source.gameObject.AddComponent<PickableObject>();
            Check(pickup.PickUp(hand),"Bounce fixture is held");
            typeof(RiggedPlayerAvatar).GetField("rightShoulder",Hidden).SetValue(avatar,shoulder);
            typeof(RiggedPlayerAvatar).GetField("rightElbow",Hidden).SetValue(avatar,elbow);
            typeof(RiggedPlayerAvatar).GetField("rightHandMarker",Hidden).SetValue(avatar,hand);
            typeof(RiggedPlayerAvatar).GetField("heldRightHandItem",Hidden).SetValue(avatar,pickup);
            typeof(RiggedPlayerAvatar).GetField("striking",Hidden).SetValue(avatar,true);
            var target=Solid(root,"bounce target",Vector3.right);
            int seed=0;
            do { UnityEngine.Random.InitState(seed++); } while(UnityEngine.Random.value<0.3f);
            UnityEngine.Random.InitState(seed-1);
            Check(pickup.TryResolveStrikeImpact(target.GetComponent<Collider>(),Vector3.right*0.8f,
                    Vector3.zero,Vector3.right*40f)&&avatar.IsStrikeMode&&
                (bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",Hidden).GetValue(avatar),
                "A losing equal-hardness hit bounces the hand using its sampled collision speed");
            Check(!source.HasBeenDestroyed&&!target.HasBeenDestroyed,"Bounce does not damage either object");
        }

        static PhysicalDestructible Solid(GameObject root,string name,Vector3 position)
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name=Prefix+name;
            cube.transform.SetParent(root.transform);cube.transform.localScale=Vector3.one*0.4f;
            cube.transform.position=position+Vector3.right*200f;
            var properties=PhysicalDestructible.Ensure(cube);properties.ApplyMaterial("Stone");
            properties.Hardness=5.5f;properties.Resilience=5f;return properties;
        }
        static void CleanupSolids()
        {
            foreach(var item in UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(FindObjectsSortMode.None))
                if(item!=null&&item.name.StartsWith(Prefix,StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(item.gameObject);
        }
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
