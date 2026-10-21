using UnityEngine;

namespace Wyvernland
{
    // Procedural shading is evaluated once and sampled by the GPU.
    public sealed class CachedWorldShading : MonoBehaviour
    {
        private Texture3D noise;
        private RenderTexture regions;
        public void Initialize()
        {
            const int side=64;
            var samples=new byte[side*side*side];
            var random=new System.Random(734119);
            random.NextBytes(samples);
            noise=new Texture3D(side,side,side,TextureFormat.R8,false)
            { name="Cached clay noise",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear };
            noise.SetPixelData(samples,0);
            noise.Apply(false,true);
            Shader.SetGlobalTexture("_ClayNoise",noise);
            Physics.reuseCollisionCallbacks=true;
        }
        public void BakeTerrainRegions(Material terrain,Vector4[] riverbedPoints)
        {
            var shader=Resources.Load<Shader>("TerrainRegionBake");
            if(shader==null) throw new System.InvalidOperationException("Missing terrain region bake shader.");
            var bake=new Material(shader);
            bake.SetColor("_BaseColor",terrain.GetColor("_BaseColor"));
            bake.SetColor("_DarkWoodColor",EnvironmentRegions.DarkBrown);
            bake.SetVectorArray("_DarkWoods",EnvironmentRegions.Woods);
            bake.SetVectorArray("_RiverbedPoints",riverbedPoints);
            bake.SetInt("_RiverbedPointCount",riverbedPoints.Length);
            regions=new RenderTexture(1024,1024,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
            { name="Cached terrain colors",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear };
            regions.Create();
            Graphics.Blit(null,regions,bake);
            Shader.SetGlobalTexture("_TerrainRegionMap",regions);
            if(Application.isPlaying) Destroy(bake); else DestroyImmediate(bake);
        }
        private void OnDestroy()
        {
            if(noise!=null) Destroy(noise);
            if(regions!=null) { regions.Release(); Destroy(regions); }
        }
    }
}
