using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class TerrainCacheBuilder
    {
        [MenuItem("Wyvernland/Rebuild Terrain Cache")]
        public static void Run()
        {
            WriteFeatureCache();
            const string directory="Assets/Resources/Terrain";
            MethodInfo buildIsland=typeof(ProceduralMap).GetMethod("BuildIsland",
                BindingFlags.NonPublic|BindingFlags.Instance);
            if(buildIsland==null)
                throw new System.InvalidOperationException("Could not find island mesh builder.");

            GameObject root=new("Terrain Cache Builder");
            try
            {
                ProceduralMap map=root.AddComponent<ProceduralMap>();
                for(int island=0;island<3;island++)
                {
                    Mesh source=(Mesh)buildIsland.Invoke(map,new object[]{island});
                    Mesh copy=UnityEngine.Object.Instantiate(source);
                    copy.name=$"Floating island {island+1} — flat facets";
                    string path=$"{directory}/Island{island+1}.asset";
                    Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if(existing==null) AssetDatabase.CreateAsset(copy,path);
                    else
                    {
                        EditorUtility.CopySerialized(copy,existing);
                        UnityEngine.Object.DestroyImmediate(copy);
                        EditorUtility.SetDirty(existing);
                    }
                    Debug.Log($"WYVERNLAND_TERRAIN_CACHE_CREATED island={island+1} vertices={source.vertexCount}");
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void RepairFeatureCache()
        {
            WriteFeatureCache();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void WriteFeatureCache()
        {
            const string directory="Assets/Resources/Terrain";
            if(!AssetDatabase.IsValidFolder(directory))
                AssetDatabase.CreateFolder("Assets/Resources","Terrain");
            GroundFeatureLayout.Initialize(false);
            const string featurePath=directory+"/GroundFeatures.asset";
            GroundFeatureCache featureCache=AssetDatabase.LoadAssetAtPath<GroundFeatureCache>(featurePath);
            if(featureCache==null)
            {
                featureCache=ScriptableObject.CreateInstance<GroundFeatureCache>();
                AssetDatabase.CreateAsset(featureCache,featurePath);
            }
            featureCache.Features=GroundFeatureLayout.Features.ToArray();
            featureCache.StonePileFootprintScale=GroundFeatureLayout.StonePileFootprintScale;
            featureCache.RockPileFootprintScale=GroundFeatureLayout.RockPileFootprintScale;
            featureCache.IndividualStoneMinimumSize=GroundFeatureLayout.IndividualStoneMinimumSize;
            EditorUtility.SetDirty(featureCache);
        }
    }
}
