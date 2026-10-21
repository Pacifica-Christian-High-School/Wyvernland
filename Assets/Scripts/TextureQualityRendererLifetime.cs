using System.Collections.Generic;
using UnityEngine;

namespace Wyvernland
{
    public sealed class TextureQualityRendererLifetime : MonoBehaviour
    {
        readonly List<Renderer> tracked=new();
        internal void Track(Renderer renderer) { if(!tracked.Contains(renderer)) tracked.Add(renderer); }
        void OnDestroy()
        {
            foreach(var renderer in tracked) ColorOnlyTextureQuality.ForgetRenderer(renderer);
        }
    }
}
