using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class StrikeWaveBoostValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Strike Wave Boost")]
        public static void Run()
        {
            var actor=new GameObject("Strike wave boost validation");
            try
            {
                var avatar=actor.AddComponent<RiggedPlayerAvatar>();
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var update=typeof(RiggedPlayerAvatar).GetMethod("UpdateStrikeWaveBoost",flags);
                void Active()
                {
                    avatar.CancelRightHandStrike();
                    typeof(RiggedPlayerAvatar).GetField("striking",flags).SetValue(avatar,true);
                    typeof(RiggedPlayerAvatar).GetField("strikeAttackHeld",flags).SetValue(avatar,true);
                }
                void Step(float speed,float mouseSpeed,float delta) =>
                    update.Invoke(avatar,new object[]{speed,mouseSpeed,delta});
                void Check(bool value,string reason)
                { if(!value) throw new Exception(reason); }
                foreach(float speed in new[]{0f,1f,RiggedPlayerAvatar.MinimumReboundSpeed})
                {
                    Active();Step(speed,1000f,5f);
                    Check(avatar.StrikeWavingSpeedMultiplier==1f,
                        "Mouse movement alone cannot boost an object at or below the minimum speed");
                }
                Active();Step(4f,100f,5f);
                Check(avatar.StrikeWavingSpeedMultiplier==1f,"Slow mouse input cannot add a boost");
                Step(4f,140f,0.5f);
                float partial=avatar.StrikeWavingSpeedMultiplier;
                Check(partial>1f&&partial<1.5f,"Continued fast movement increases speed gradually");
                Step(4f,140f,0.5f);
                Check(avatar.StrikeWavingSpeedMultiplier>partial&&avatar.StrikeWavingSpeedMultiplier<1.5f,
                    "The bonus continues building while eligible input continues");
                Step(4f,140f,10f);
                Check(avatar.StrikeWavingSpeedMultiplier==1.5f,"Extra waving speed caps at 50 percent");
                Step(1f,200f,.1f);
                Check(avatar.StrikeWavingSpeedMultiplier==1.5f,
                    "A brief speed dip at a wave reversal preserves the earned bonus without adding speed");
                Step(4f,0f,0.3f);
                Check(avatar.StrikeWavingSpeedMultiplier>1f&&avatar.StrikeWavingSpeedMultiplier<1.5f,
                    "Idle mouse movement fades the bonus");
                Step(3f,1000f,1f);
                Check(avatar.StrikeWavingSpeedMultiplier==1f,"Dropping below the speed threshold removes the bonus");
                foreach(float delta in new[]{1f/30f,1f/60f,1f/144f})
                {
                    Active();int steps=Mathf.RoundToInt(1.5f/delta);
                    for(int i=0;i<steps;i++) Step(4f,200f,delta);
                    Check(Mathf.Abs(avatar.StrikeWavingSpeedMultiplier-1.5f)<0.0001f,
                        "Boost build-up is consistent across frame rates");
                }
                typeof(RiggedPlayerAvatar).GetField("strikeAttackHeld",flags).SetValue(avatar,false);
                Check(avatar.StrikeWavingSpeedMultiplier==1f,"Releasing the attack removes the bonus");
                avatar.CancelRightHandStrike();
                Check(avatar.StrikeWavingSpeedMultiplier==1f,"Canceled strikes retain no bonus");
                Active();Step(4f,200f,1.5f);
                typeof(RiggedPlayerAvatar).GetField("strikeRebounding",flags).SetValue(avatar,true);
                Check(avatar.StrikeWavingSpeedMultiplier==1f,"Rebound has no waving bonus");
                Debug.Log("STRIKE_WAVE_BOOST_VALIDATION_PASS: object and mouse thresholds, gradual build, 50 percent cap, fade, reset and frame rates.");
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }
    }
}
