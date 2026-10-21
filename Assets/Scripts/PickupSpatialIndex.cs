using System.Collections.Generic;
using UnityEngine;

namespace Wyvernland
{
    // Static pickups enter this index once. Only objects that can move need updates.
    internal static class PickupSpatialIndex
    {
        private const float CellSize=16f;
        private sealed class Membership
        {
            public Renderer Renderer;
            public Vector2Int Minimum,Maximum;
            public readonly List<Vector2Int> Keys=new();
        }
        private static readonly Dictionary<Vector2Int,HashSet<PickableObject>> cells=new();
        private static readonly Dictionary<PickableObject,Membership> memberships=new();
        private static readonly HashSet<PickableObject> moving=new();
        private static readonly HashSet<PickableObject> found=new();

        public static void Clear() { cells.Clear();memberships.Clear();moving.Clear();found.Clear(); }
        public static void Register(PickableObject item)
        {
            if(!memberships.TryGetValue(item,out var membership))
            {
                membership=new Membership { Renderer=item.GetComponentInChildren<Renderer>() };
                memberships.Add(item,membership);
            }
            Refresh(item,membership);
            if(item.GetComponentInParent<Rigidbody>() is { isKinematic: false }) moving.Add(item);
        }
        private static void Refresh(PickableObject item,Membership membership)
        {
            Bounds bounds=membership.Renderer!=null?membership.Renderer.bounds:
                new Bounds(item.transform.position,Vector3.one);
            Vector2Int minimum=new(Mathf.FloorToInt(bounds.min.x/CellSize),Mathf.FloorToInt(bounds.min.z/CellSize));
            Vector2Int maximum=new(Mathf.FloorToInt(bounds.max.x/CellSize),Mathf.FloorToInt(bounds.max.z/CellSize));
            if(membership.Keys.Count>0&&minimum==membership.Minimum&&maximum==membership.Maximum) return;
            RemoveCells(item,membership);
            membership.Minimum=minimum;
            membership.Maximum=maximum;
            for(int z=minimum.y;z<=maximum.y;z++)
            for(int x=minimum.x;x<=maximum.x;x++)
            {
                var key=new Vector2Int(x,z);
                if(!cells.TryGetValue(key,out var bucket)) cells[key]=bucket=new HashSet<PickableObject>();
                bucket.Add(item);membership.Keys.Add(key);
            }
        }
        private static void RemoveCells(PickableObject item,Membership membership)
        {
            foreach(Vector2Int key in membership.Keys)
                if(cells.TryGetValue(key,out var bucket))
                { bucket.Remove(item);if(bucket.Count==0) cells.Remove(key); }
            membership.Keys.Clear();
        }
        public static void Remove(PickableObject item)
        {
            if(memberships.TryGetValue(item,out var membership)) RemoveCells(item,membership);
            memberships.Remove(item);moving.Remove(item);
        }
        public static void TrackMovement(PickableObject item) => moving.Add(item);
        public static void Collect(Vector3 position,float radius,List<PickableObject> result)
        {
            foreach(PickableObject item in moving)
                if(item!=null&&item.isActiveAndEnabled&&memberships.TryGetValue(item,out var membership))
                    Refresh(item,membership);
            found.Clear();
            for(int z=Mathf.FloorToInt((position.z-radius)/CellSize);z<=Mathf.FloorToInt((position.z+radius)/CellSize);z++)
            for(int x=Mathf.FloorToInt((position.x-radius)/CellSize);x<=Mathf.FloorToInt((position.x+radius)/CellSize);x++)
                if(cells.TryGetValue(new Vector2Int(x,z),out var bucket))
                    foreach(PickableObject item in bucket)
                        if(item!=null&&item.isActiveAndEnabled&&found.Add(item)) result.Add(item);
        }
    }
}
