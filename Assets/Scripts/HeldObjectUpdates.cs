using UnityEngine;

namespace Wyvernland
{
    // Only held items need animation callbacks; resting pickups remain registered.
    [DefaultExecutionOrder(100)]
    public sealed class HeldObjectUpdates : MonoBehaviour
    {
        private void Awake()
        {
            if(GetComponent<HeldObjectMotionUpdates>()==null)
                gameObject.AddComponent<HeldObjectMotionUpdates>();
        }
        private void LateUpdate()
        {
            for(int i=0;i<PickableObject.HeldObjects.Count;i++)
                PickableObject.HeldObjects[i].UpdateHeldPose();
        }
    }

    // Sample once after both the item pose and wrist contact are finalized.
    // Sampling in each physics step produced zero/spiking speeds when several
    // steps ran between two rendered animation frames.
    [DefaultExecutionOrder(300)]
    public sealed class HeldObjectMotionUpdates : MonoBehaviour
    {
        private void LateUpdate()
        {
            for(int i=0;i<PickableObject.HeldObjects.Count;i++)
                PickableObject.HeldObjects[i].SampleHeldMotion(Time.deltaTime);
        }
    }
}
