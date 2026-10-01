using System;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class FragmentCleanupValidation
    {
        public static void Run()
        {
            var source=GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.name="Wood cleanup source";
            var ordinary=GameObject.CreatePrimitive(PrimitiveType.Cube);
            ordinary.name="Ordinary object fragment";
            var hand=new GameObject("Cleanup validation hand");
            try
            {
                var properties=PhysicalDestructible.Ensure(source);
                Require(properties.ApplyImpact(10f,7f,1f,source.transform.position,Vector3.right,30f),
                    "Cleanup sample must produce actual destruction fragments");
                var fragments=UnityEngine.Object.FindObjectsByType<DestructionFragment>(FindObjectsSortMode.None);
                Require(fragments.Length==2,"Both spawned fragments must carry cleanup provenance");
                var held=fragments[0].GetComponent<PickableObject>();
                hand.transform.position=held.transform.position;
                held.PickUp(hand.transform);
                fragments[1].gameObject.SetActive(false);
                Require(DestructionFragment.DeleteAllExceptHeld()==1&&held!=null&&held.IsHeld&&ordinary!=null,
                    "Cleanup removes inactive fragments, preserves held fragments and leaves ordinary objects intact");
                held.Release(Vector3.zero,hand.transform);
                Require(DestructionFragment.DeleteAllExceptHeld()==1&&ordinary!=null,
                    "A previously held fragment becomes eligible for cleanup after dropping");
                Debug.Log("FRAGMENT_CLEANUP_VALIDATION_PASS");
            }
            finally
            {
                if(source!=null) UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(ordinary);
                UnityEngine.Object.DestroyImmediate(hand);
            }
        }
        static void Require(bool condition,string message)
        { if(!condition) throw new Exception(message); }
    }
}
