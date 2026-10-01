using System;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class TextureQualityValidation
    {
        private static void ValidateDestroyedAppearance()
        {
            var previous=ColorOnlyTextureQuality.Level;
            var randomState=UnityEngine.Random.state;
            var material=new Material(Resources.Load<Shader>("Clay"));
            material.SetColor("_BaseColor",new Color(0.7f,0.25f,0.1f));
            try
            {
                foreach(var quality in new[]{TextureQualityLevel.High,TextureQualityLevel.Medium,TextureQualityLevel.Low})
                foreach(bool local in new[]{false,true})
                {
                    ColorOnlyTextureQuality.SetHighQuality(true);
                    var source=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    source.name="Destruction appearance regression";
                    source.transform.localScale=Vector3.one*(local?2f:1f);
                    var mesh=UnityEngine.Object.Instantiate(source.GetComponent<MeshFilter>().sharedMesh);
                    var indices=mesh.triangles;
                    mesh.subMeshCount=2;
                    mesh.SetTriangles(Array.Empty<int>(),0);
                    mesh.SetTriangles(indices,1);
                    source.GetComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=source.GetComponent<MeshRenderer>();
                    renderer.sharedMaterials=new[]{(Material)null,material};
                    var block=new MaterialPropertyBlock();
                    block.SetColor("_BaseColor",Color.cyan);
                    renderer.SetPropertyBlock(block);
                    ColorOnlyTextureQuality.SetQuality(quality);
                    var properties=source.AddComponent<PhysicalDestructible>();
                    properties.Hardness=5f; properties.Resilience=local?1f:5f;
                    UnityEngine.Random.InitState(7331);
                    if(!properties.ApplyImpact(8f,local?8f:5f,1f,
                        Vector3.up*(local?1f:0.5f),Vector3.up,30f))
                        throw new InvalidOperationException("Appearance regression failed to destruct.");
                    // No discovery pass may be needed before the very first frame.
                    foreach(var result in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    {
                        if(!result.enabled||!result.transform.root.name.StartsWith("Destruction appearance regression")) continue;
                        if(quality!=TextureQualityLevel.High&&result.sharedMaterial.shader.name!=
                            (quality==TextureQualityLevel.Low?"Wyvernland/Flat Color":"Wyvernland/Two Color Shadowed"))
                            throw new InvalidOperationException("Destruction briefly restored High quality before discovery.");
                    }
                    ColorOnlyTextureQuality.RefreshColorOnlyObjects();
                    ColorOnlyTextureQuality.SetHighQuality(true);
                    int visible=0;
                    foreach(var result in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    {
                        if(!result.enabled||!result.transform.root.name.StartsWith("Destruction appearance regression")) continue;
                        visible++;
                        if(result.sharedMaterials.Length!=1||result.sharedMaterial!=material)
                            throw new InvalidOperationException("Destruction lost or shifted its High quality material.");
                        result.GetPropertyBlock(block,0);
                        if(block.GetColor("_BaseColor")!=Color.cyan)
                            throw new InvalidOperationException("Destruction lost its original color override.");
                    }
                    if(visible<2) throw new InvalidOperationException("Missing destruction appearance results.");
                    foreach(var result in UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(FindObjectsSortMode.None))
                        if(result.name.StartsWith("Destruction appearance regression"))
                            UnityEngine.Object.DestroyImmediate(result.gameObject);
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
                Debug.Log("DESTRUCTION_APPEARANCE_VALIDATION_PASS: High materials, Low/Medium-to-High restoration, empty material slots, chips, remainders, and color overrides.");
            }
            finally
            {
                ColorOnlyTextureQuality.SetQuality(previous);
                UnityEngine.Random.state=randomState;
                foreach(var result in UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(FindObjectsSortMode.None))
                    if(result.name.StartsWith("Destruction appearance regression"))
                        UnityEngine.Object.DestroyImmediate(result.gameObject);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        public static void Run()
        {
            ValidateDestroyedAppearance();
            Shader clayShader=Resources.Load<Shader>("Clay");
            if(clayShader==null) throw new InvalidOperationException("Missing Clay shader.");
            GameObject terrain=new("Low-quality terrain gradient validation");
            MeshRenderer renderer=terrain.AddComponent<MeshRenderer>();
            Material material=new(clayShader);
            material.SetFloat("_TerrainColorRegions",1f);
            renderer.sharedMaterial=material;
            GameObject grass=new("Low-quality procedural grass validation");
            MeshRenderer grassRenderer=grass.AddComponent<MeshRenderer>();
            Material grassMaterial=new(Resources.Load<Shader>("Grass"));
            grassRenderer.sharedMaterial=grassMaterial;
            GameObject objectRoot=GameObject.CreatePrimitive(PrimitiveType.Cube);
            objectRoot.name="Medium two-color object validation";
            MeshRenderer objectRenderer=objectRoot.GetComponent<MeshRenderer>();
            objectRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            objectRenderer.receiveShadows=false;
            try
            {
                ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.Medium);
                if(QualitySettings.globalTextureMipmapLimit!=1)
                    throw new InvalidOperationException(
                        "Medium texture quality is not using 50% linear resolution.");
                if(renderer.sharedMaterial!=material||
                    material.GetFloat("_MediumTextureQuality")<0.5f)
                    throw new InvalidOperationException(
                        "Medium texture quality did not retain the terrain biome blend.");
                if(grassMaterial.GetFloat("_MediumTextureQuality")<0.5f)
                    throw new InvalidOperationException(
                        "Medium texture quality did not limit procedural grass colors.");
                if(grassRenderer.shadowCastingMode!=
                    UnityEngine.Rendering.ShadowCastingMode.Off||grassRenderer.receiveShadows)
                    throw new InvalidOperationException(
                        "Grass must neither cast nor receive shadows in Medium quality.");
                if(objectRenderer.sharedMaterial.shader.name!="Wyvernland/Two Color Shadowed")
                    throw new InvalidOperationException(
                        "Medium texture quality did not apply the two-color object shader.");
                if(objectRenderer.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.On||
                    !objectRenderer.receiveShadows||renderer.shadowCastingMode!=
                    UnityEngine.Rendering.ShadowCastingMode.On||!renderer.receiveShadows)
                    throw new InvalidOperationException(
                        "Medium texture quality did not enable terrain and object shadows.");
                ColorOnlyTextureQuality.SetHighQuality(false);
                if(renderer.sharedMaterial!=material)
                    throw new InvalidOperationException(
                        "Low texture quality replaced the terrain gradient material.");
                if(renderer.sharedMaterial.GetFloat("_TerrainColorRegions")<0.5f)
                    throw new InvalidOperationException(
                        "Low texture quality disabled terrain color regions.");
                if(renderer.sharedMaterial.GetFloat("_LowTextureQuality")<0.5f)
                    throw new InvalidOperationException(
                        "Low texture quality did not disable terrain surface detail.");
                if(grassRenderer.sharedMaterial!=grassMaterial)
                    throw new InvalidOperationException(
                        "Low texture quality replaced the procedural grass shader.");
                if(grassRenderer.shadowCastingMode!=
                    UnityEngine.Rendering.ShadowCastingMode.Off||grassRenderer.receiveShadows)
                    throw new InvalidOperationException(
                        "Grass must neither cast nor receive shadows in Low quality.");
                ColorOnlyTextureQuality.SetHighQuality(true);
                if(grassRenderer.shadowCastingMode!=
                    UnityEngine.Rendering.ShadowCastingMode.Off||grassRenderer.receiveShadows)
                    throw new InvalidOperationException(
                        "Grass must neither cast nor receive shadows in High quality.");
                Debug.Log("TEXTURE_QUALITY_VALIDATION_PASS: terrain gradient and procedural grass retained; grass shadows remain disabled in every quality mode.");
            }
            finally
            {
                ColorOnlyTextureQuality.SetHighQuality(true);
                UnityEngine.Object.DestroyImmediate(terrain);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(grass);
                UnityEngine.Object.DestroyImmediate(grassMaterial);
                UnityEngine.Object.DestroyImmediate(objectRoot);
            }
        }
    }
}
