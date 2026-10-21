using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    // Draw caches never replace gameplay meshes, objects, or colliders. Only
    // unchanged stationary props share geometry; interaction restores a source.
    public sealed class ResidentPropRendering : MonoBehaviour
    {
        private const int CellWidth=128;
        private sealed class Source
        {
            public MeshRenderer Renderer;
            public PhysicalDestructible Properties;
            public Action Changed;
            public Batch Batch;
            public int FirstIndex,IndexCount;
            public bool Cached=true;
            public bool Loaded=true;
        }
        private sealed class Batch
        {
            public readonly List<Source> Sources=new();
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public int[] Indices;
            public void RefreshIndices()
            {
                var remaining=new List<int>(Indices.Length);
                foreach(Source source in Sources)
                    if(source.Cached&&source.Loaded)
                        for(int i=0;i<source.IndexCount;i++)
                            remaining.Add(Indices[source.FirstIndex+i]);
                Mesh.SetIndices(remaining,MeshTopology.Triangles,0,false);
                Renderer.enabled=remaining.Count>0;
            }
        }
        private static ResidentPropRendering current;
        private readonly Dictionary<MeshRenderer,Source> sources=new();
        private readonly List<Batch> batches=new();
        private readonly HashSet<Batch> loadingChanges=new();
        private readonly Dictionary<int,Material> materials=new();
        public int CachedSourceCount => sources.Count;
        public int BatchCount => batches.Count;
        internal static void SetLoaded(Renderer renderer,bool loaded)
        {
            if(current!=null&&renderer is MeshRenderer mesh&&current.sources.TryGetValue(mesh,out var source)&&source.Loaded!=loaded) {
                source.Loaded=loaded;current.loadingChanges.Add(source.Batch);
            }
        }
        internal static void FlushLoadingChanges()
        {
            if(current==null) return;
            foreach(var batch in current.loadingChanges) batch.RefreshIndices();
            current.loadingChanges.Clear();
        }
        internal static bool IsCached(Renderer renderer) => current!=null&&
            renderer is MeshRenderer meshRenderer&&current.sources.ContainsKey(meshRenderer);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => current=null;

        public void Initialize()
        {
            var routine=InitializeIncrementally();
            while(routine.MoveNext()) { }
        }

        internal System.Collections.IEnumerator InitializeIncrementally()
        {
            current=this;
            double batchStarted=Time.realtimeSinceStartupAsDouble;
            var groups=new Dictionary<(Vector2Int,int,int,ShadowCastingMode,bool),List<MeshRenderer>>();
            var sourceMaterials=new Dictionary<MeshRenderer,Material>();
            foreach(MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>())
            {
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
                var properties=renderer.GetComponent<PhysicalDestructible>();
                var filter=renderer.GetComponent<MeshFilter>();
                ColorOnlyTextureQuality.GetOriginalAppearance(renderer,out Material[] appearance,
                    out MaterialPropertyBlock overrides);
                Material originalMaterial=appearance.Length==1?appearance[0]:null;
                if(!renderer.enabled||properties==null||properties.IsTerrain||
                    filter==null||filter.sharedMesh==null||!filter.sharedMesh.isReadable||
                    filter.sharedMesh.subMeshCount!=1||originalMaterial==null||!overrides.isEmpty||
                    renderer.GetComponentInParent<Rigidbody>()!=null||
                    originalMaterial.shader.name!="Wyvernland/Unfired Clay"||
                    originalMaterial.GetFloat("_TerrainColorRegions")>.5f||
                    originalMaterial.GetFloat("_UseTerrainPitColor")>.5f) continue;
                sourceMaterials.Add(renderer,originalMaterial);
                Vector3 position=renderer.transform.TransformPoint(filter.sharedMesh.bounds.center);
                Vector2Int cell=new(Mathf.FloorToInt(position.x/CellWidth),Mathf.FloorToInt(position.z/CellWidth));
                var key=(cell,Mathf.RoundToInt(originalMaterial.GetFloat("_Cull")),renderer.gameObject.layer,
                    renderer.shadowCastingMode,renderer.receiveShadows);
                if(!groups.TryGetValue(key,out var group)) groups[key]=group=new();
                group.Add(renderer);
            }
            long cachedBytes=0;
            IEnumerable<KeyValuePair<(Vector2Int,int,int,ShadowCastingMode,bool),List<MeshRenderer>>> ordered=groups;
#if UNITY_WEBGL && !UNITY_EDITOR
            Vector3 playerPosition=GetComponentInChildren<FirstPersonController>()?.transform.position??Vector3.zero;
            ordered=groups.OrderBy(entry=>
                (new Vector2(entry.Key.Item1.x*CellWidth,entry.Key.Item1.y*CellWidth)-
                    new Vector2(playerPosition.x,playerPosition.z)).sqrMagnitude);
            ordered=SplitGroups(ordered);
#endif
            foreach(var entry in ordered)
            {
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
                if(entry.Value.Count<2) continue;
#if UNITY_WEBGL && !UNITY_EDITOR
                // Browser terrain and physics already keep the complete world
                // resident. Bound duplicated prop geometry and temporary batch
                // buffers; skipped sources continue using their own renderers.
                long estimatedBytes=0;int vertices=0;
                foreach(MeshRenderer renderer in entry.Value)
                {
                    Mesh mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    vertices+=mesh.vertexCount;
                    estimatedBytes+=mesh.vertexCount*80L+(long)mesh.GetIndexCount(0)*8;
                }
                if(vertices>32768||cachedBytes+estimatedBytes>64L*1024*1024) continue;
                cachedBytes+=estimatedBytes;
#endif
                var batch=new Batch();
                var combine=new CombineInstance[entry.Value.Count];
                var colors=new List<Color>();
                var flatColors=new List<Color>();
                int firstIndex=0;
                for(int i=0;i<combine.Length;i++)
                {
                    MeshRenderer renderer=entry.Value[i];
                    Mesh original=renderer.GetComponent<MeshFilter>().sharedMesh;
                    combine[i]=new CombineInstance { mesh=original,
                        transform=renderer.transform.localToWorldMatrix };
                    // Uniform prop colors become vertex colors so different
                    // bark and stone shades can share the same draw material.
                    Material sourceMaterial=sourceMaterials[renderer];
                    Color uniform=sourceMaterial.GetColor("_BaseColor");
                    if(QualitySettings.activeColorSpace==ColorSpace.Linear) uniform=uniform.linear;
                    float useVertex=Mathf.Clamp01(sourceMaterial.GetFloat("_UseVertexColor"));
                    Color[] originalColors=useVertex>0f?original.colors:null;
                    Color flat=ColorOnlyTextureQuality.RepresentativeColor(renderer,
                        new[]{sourceMaterial});
                    if(QualitySettings.activeColorSpace==ColorSpace.Linear) flat=flat.linear;
                    for(int vertex=0;vertex<original.vertexCount;vertex++)
                    {
                        Color color=uniform;
                        if(originalColors!=null&&originalColors.Length==original.vertexCount)
                            color=Color.Lerp(uniform,originalColors[vertex],useVertex*originalColors[vertex].a);
                        color.a=1f;
                        colors.Add(color);
                        flatColors.Add(flat);
                    }
                    var source=new Source { Renderer=renderer,
                        Properties=renderer.GetComponent<PhysicalDestructible>(),Batch=batch,
                        FirstIndex=firstIndex,IndexCount=(int)original.GetIndexCount(0) };
                    firstIndex+=source.IndexCount;
                    source.Changed=()=>Release(source.Renderer);
                    source.Properties.GeometryChanged+=source.Changed;
                    batch.Sources.Add(source);
                    sources.Add(renderer,source);
                }
                batch.Mesh=new Mesh { name="Resident prop draw cache",indexFormat=IndexFormat.UInt32 };
                batch.Mesh.CombineMeshes(combine,true,true,false);
                batch.Mesh.SetColors(colors);
                batch.Indices=batch.Mesh.GetIndices(0);
                var visual=new GameObject("Stationary prop draw batch");
                visual.transform.SetParent(transform,false);
                // World-space vertices keep draw caches independent of the
                // original renderers' transform and instancing state.
                visual.transform.position=Vector3.zero;
                visual.layer=entry.Key.Item3;
                visual.AddComponent<ResidentRenderVisual>().Initialize(batch.Mesh,
                    colors.ToArray(),flatColors.ToArray());
                visual.AddComponent<MeshFilter>().sharedMesh=batch.Mesh;
                batch.Renderer=visual.AddComponent<MeshRenderer>();
                if(!materials.TryGetValue(entry.Key.Item2,out Material material))
                {
                    material=new Material(sourceMaterials[entry.Value[0]]) {
                        name="Resident prop vertex colors",enableInstancing=false };
                    material.SetColor("_BaseColor",Color.white);
                    material.SetFloat("_UseVertexColor",1f);
                    material.SetFloat("_ResidentPropDraw",1f);
                    materials.Add(entry.Key.Item2,material);
                }
                batch.Renderer.sharedMaterial=material;
                batch.Renderer.shadowCastingMode=entry.Key.Item4;
                batch.Renderer.receiveShadows=entry.Key.Item5;
                ColorOnlyTextureQuality.ApplyRendererQuality(batch.Renderer);
                foreach(Source source in batch.Sources) source.Renderer.enabled=false;
                batches.Add(batch);
            }
        }

        internal static IEnumerable<List<MeshRenderer>> PartitionGroup(IReadOnlyList<MeshRenderer> renderers)
        {
            var part=new List<MeshRenderer>();int vertices=0;
            foreach(var renderer in renderers) {
                int count=renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                if(part.Count>0&&vertices+count>32768) { yield return part;part=new List<MeshRenderer>();vertices=0; }
                part.Add(renderer);vertices+=count;
            }
            if(part.Count>0) yield return part;
        }
        static IEnumerable<KeyValuePair<(Vector2Int,int,int,ShadowCastingMode,bool),List<MeshRenderer>>> SplitGroups(
            IEnumerable<KeyValuePair<(Vector2Int,int,int,ShadowCastingMode,bool),List<MeshRenderer>>> groups)
        {
            foreach(var group in groups) foreach(var part in PartitionGroup(group.Value))
                yield return new KeyValuePair<(Vector2Int,int,int,ShadowCastingMode,bool),List<MeshRenderer>>(group.Key,part);
        }

        public static void Release(GameObject target)
        {
            if(current==null||target==null) return;
            HashSet<Batch> changed=null;
            foreach(MeshRenderer renderer in target.GetComponentsInChildren<MeshRenderer>(true))
                if(current.sources.TryGetValue(renderer,out Source source))
                {
                    current.Detach(source);
                    (changed??=new()).Add(source.Batch);
                }
            if(changed!=null) foreach(Batch batch in changed) batch.RefreshIndices();
        }
        private static void Release(MeshRenderer renderer)
        {
            if(current==null||!current.sources.TryGetValue(renderer,out Source source)) return;
            current.Detach(source);
            source.Batch.RefreshIndices();
        }
        private void Detach(Source source)
        {
            sources.Remove(source.Renderer);
            source.Cached=false;
            source.Properties.GeometryChanged-=source.Changed;
            if(source.Renderer!=null) source.Renderer.enabled=true;
        }
        private void OnDestroy()
        {
            foreach(Source source in sources.Values)
            {
                if(source.Properties!=null) source.Properties.GeometryChanged-=source.Changed;
                if(source.Renderer!=null) source.Renderer.enabled=true;
            }
            sources.Clear();
            foreach(Batch batch in batches)
                if(Application.isPlaying) Destroy(batch.Mesh); else DestroyImmediate(batch.Mesh);
            foreach(Material material in materials.Values)
                if(Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            if(current==this) current=null;
        }
    }
    public sealed class ResidentRenderVisual : MonoBehaviour
    {
        Mesh mesh;
        Color[] detailedColors,flatColors;
        TextureQualityLevel? appliedQuality;
        internal void Initialize(Mesh drawMesh,Color[] detailed,Color[] flat)
        { mesh=drawMesh;detailedColors=detailed;flatColors=flat; }
        internal void ApplyQuality(TextureQualityLevel level)
        {
            if(mesh==null||appliedQuality==level) return;
            // Match the individual renderer's representative tint in Low and
            // Medium, including the same gamma-to-linear conversion. Authored
            // vertex colors are retained for the detailed High shader path.
            mesh.SetColors(level==TextureQualityLevel.High?detailedColors:flatColors);
            appliedQuality=level;
        }
    }
}
