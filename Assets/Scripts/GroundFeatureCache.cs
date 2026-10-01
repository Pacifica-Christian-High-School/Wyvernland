using UnityEngine;

namespace Wyvernland
{
    public sealed class GroundFeatureCache : ScriptableObject
    {
        public GroundFeatureSeed[] Features;
        public float StonePileFootprintScale;
        public float RockPileFootprintScale;
        public float IndividualStoneMinimumSize;
    }
}
