using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    // Surface data and one shared mesh describe grass for the entire resident map.
    // Visibility changes draw submission only; movement never creates or unloads it.
    public sealed class ProceduralGrass : MonoBehaviour
    {
        private const int PatchWidth=32;
        private struct Patch { public Matrix4x4 Matrix; public Bounds Bounds; }
        private struct Instance { public Matrix4x4 objectToWorld; }
        private readonly List<Patch> patches=new();
        private readonly Instance[] visible=new Instance[1023];
        private readonly Plane[] planes=new Plane[6];
        private Camera playerCamera;
        private Material material;
        private Mesh mesh;
        private RenderTexture surfaceMap;
        private Texture2D densityMap;
        private Rect bounds;
        private int minimumX,minimumZ,columns,rows;
        internal const float BrowserDrawDistance=128f;
        public float Density { get; private set; }=1f;
        public int ResidentPatchCount => patches.Count;
        public int LastDrawnPatchCount { get; private set; }
        public int LastVisitedPatchCount { get; private set; }

        public void Initialize(FirstPersonController player,ProceduralMap map)
        {
            var routine=InitializeIncrementally(player,map);
            while(routine.MoveNext()) { }
        }

        internal System.Collections.IEnumerator InitializeIncrementally(FirstPersonController player,ProceduralMap map)
        {
            playerCamera=player.PlayerCamera;
            bounds=map.Bounds;
            Density=Mathf.Clamp01(PlayerPrefs.GetFloat("Options.GrassDensity",1f));
            material=new Material(Resources.Load<Shader>("Grass"))
                { name="Resident GPU grass",enableInstancing=true };
            material.SetVector("_GrassMapBounds",new Vector4(bounds.xMin,bounds.yMin,
                1f/bounds.width,1f/bounds.height));
            BuildSurfaceMap(map);
            yield return null;
            var distribution=BuildDensityMap(FindFirstObjectByType<ProceduralTreeField>());
            while(distribution.MoveNext()) yield return distribution.Current;
            BuildSharedMesh();
            float vertical=WorldBootstrap.TestGround?2f:WorldConfig.MaximumElevation+2f;
            minimumX=Mathf.FloorToInt(bounds.xMin/PatchWidth);minimumZ=Mathf.FloorToInt(bounds.yMin/PatchWidth);
            columns=Mathf.CeilToInt(bounds.xMax/PatchWidth)-minimumX;
            rows=Mathf.CeilToInt(bounds.yMax/PatchWidth)-minimumZ;
            for(int z=minimumZ;z<minimumZ+rows;z++)
            for(int x=minimumX;x<minimumX+columns;x++)
            {
                Vector3 position=new(x*PatchWidth,0,z*PatchWidth);
                patches.Add(new Patch { Matrix=Matrix4x4.Translate(position),
                    Bounds=new Bounds(position+new Vector3(PatchWidth*.5f,vertical*.5f,PatchWidth*.5f),
                        new Vector3(PatchWidth+2f,vertical,PatchWidth+2f)) });
            }
        }

        private void BuildSurfaceMap(ProceduralMap map)
        {
            int resolution=WorldBootstrap.TestGround?512:2048;
            surfaceMap=new RenderTexture(resolution,resolution,24,RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear)
            { name="Resident grass heights and normals",filterMode=FilterMode.Point,
                wrapMode=TextureWrapMode.Clamp };
            surfaceMap.Create();
            var bake=new Material(Resources.Load<Shader>("GrassSurfaceBake"));
            bake.SetVector("_GrassMapBounds",new Vector4(bounds.xMin,bounds.yMin,
                1f/bounds.width,1f/bounds.height));
            var commands=new CommandBuffer { name="Bake resident grass surface" };
            commands.SetRenderTarget(surfaceMap);
            commands.ClearRenderTarget(true,true,new Color(ProceduralMap.FallLimit,0,0,0));
            map.DrawGrassSurface(commands,bake);
            Graphics.ExecuteCommandBuffer(commands);
            commands.Release();
            Release(bake);
            material.SetTexture("_GrassSurfaceMap",surfaceMap);
            material.SetVector("_GrassSurfaceGrid",new Vector4(resolution,resolution,
                bounds.width/resolution,bounds.height/resolution));
        }

        private System.Collections.IEnumerator BuildDensityMap(ProceduralTreeField trees)
        {
            const int resolution=512;
            var pixels=new Color[resolution*resolution];
            bool test=WorldBootstrap.TestGround;
            double batchStarted=Time.realtimeSinceStartupAsDouble;
            for(int z=0;z<resolution;z++)
            {
                for(int x=0;x<resolution;x++)
                {
                float px=bounds.xMin+(x+.5f)/resolution*bounds.width;
                float pz=bounds.yMin+(z+.5f)/resolution*bounds.height;
                if(test) { pixels[z*resolution+x]=new Color(px<-.5f?1f:0f,0,0,1); continue; }
                float baseDensity=ProceduralMap.GrassDensity(px,pz)*.7f;
                float treeDensity=trees!=null?trees.GrassAroundTrees(px,pz):0f;
                float pile=GroundFeatureLayout.PileGrassDensity(px,pz);
                float density=pile>=0f?pile:Mathf.Max(baseDensity,treeDensity);
                if(ProceduralMonumentField.ContainsInterior(px,pz,3f)||
                    !GroundFeatureLayout.AllowsGrass(px,pz)) density=0f;
                pixels[z*resolution+x]=new Color(density,pile>=0f?1f:0f,
                    treeDensity>baseDensity?1f:0f,1f);
                }
                if(WorldLoading.ShouldYield(ref batchStarted))
                { yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
            }
            densityMap=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true)
                { name="Resident grass distribution",filterMode=FilterMode.Bilinear,
                    wrapMode=TextureWrapMode.Clamp };
            densityMap.SetPixels(pixels);
            densityMap.Apply(false,true);
            material.SetTexture("_GrassDensityMap",densityMap);
        }

        private void BuildSharedMesh()
        {
            var vertices=new Vector3[PatchWidth*PatchWidth*4];
            var uv=new Vector2[vertices.Length];
            var triangles=new int[PatchWidth*PatchWidth*6];
            for(int z=0;z<PatchWidth;z++)
            for(int x=0;x<PatchWidth;x++)
            {
                int first=(z*PatchWidth+x)*4,tri=(z*PatchWidth+x)*6;
                for(int corner=0;corner<4;corner++) vertices[first+corner]=new Vector3(x,0,z);
                uv[first]=Vector2.zero;uv[first+1]=Vector2.up;
                uv[first+2]=Vector2.one;uv[first+3]=Vector2.right;
                triangles[tri]=first;triangles[tri+1]=first+1;triangles[tri+2]=first+2;
                triangles[tri+3]=first;triangles[tri+4]=first+2;triangles[tri+5]=first+3;
            }
            mesh=new Mesh { name="Shared 1024-clump grass patch",vertices=vertices,
                uv=uv,triangles=triangles,
                bounds=new Bounds(new Vector3(16,170,16),new Vector3(34,344,34)) };
            mesh.UploadMeshData(true);
        }

        private void LateUpdate()
        {
            LastDrawnPatchCount=0;
            LastVisitedPatchCount=0;
            // Initialization yields while the density map is being built.
            if(playerCamera==null||Density<=0f||mesh==null||columns<=0||rows<=0||patches.Count<columns*rows) return;
            GeometryUtility.CalculateFrustumPlanes(playerCamera,planes);
            material.SetFloat("_GrassDensity",Density);
            material.SetFloat("_MediumTextureQuality",ColorOnlyTextureQuality.Level==TextureQualityLevel.Medium?1:0);
            float pixelsPerMetre=playerCamera.pixelHeight/(2f*Mathf.Tan(playerCamera.fieldOfView*Mathf.Deg2Rad*.5f));
            material.SetFloat("_GrassPixelsPerMetre",pixelsPerMetre);
            // A blade smaller than half a pixel cannot resolve its silhouette.
            float limit=pixelsPerMetre*2f+PatchWidth;
            if(Application.platform==RuntimePlatform.WebGLPlayer) limit=Mathf.Min(limit,BrowserDrawDistance);
            limit=Mathf.Min(limit,PlayerWorldLoading.LocalRange);
            float squared=limit*limit;
            Vector3 eye=playerCamera.transform.position;
            var parameters=new RenderParams(material) {
                camera=playerCamera,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false,
                worldBounds=new Bounds(new Vector3(bounds.center.x,170,bounds.center.y),
                    new Vector3(bounds.width+64,400,bounds.height+64)) };
            int count=0;
            RectInt window=PatchWindow(bounds,eye,limit);
            for(int z=window.yMin;z<window.yMax;z++)
            for(int x=window.xMin;x<window.xMax;x++)
            {
                Patch patch=patches[(z-minimumZ)*columns+x-minimumX];LastVisitedPatchCount++;
                if(patch.Bounds.SqrDistance(eye)>squared||!GeometryUtility.TestPlanesAABB(planes,patch.Bounds)) continue;
                visible[count++].objectToWorld=patch.Matrix;
                LastDrawnPatchCount++;
                if(count==visible.Length)
                { Graphics.RenderMeshInstanced(parameters,mesh,0,visible,count); count=0; }
            }
            if(count>0) Graphics.RenderMeshInstanced(parameters,mesh,0,visible,count);
        }
        internal static RectInt PatchWindow(Rect area,Vector3 eye,float distance)
        {
            int minX=Mathf.Max(Mathf.FloorToInt(area.xMin/PatchWidth),Mathf.FloorToInt((eye.x-distance)/PatchWidth)-1);
            int minZ=Mathf.Max(Mathf.FloorToInt(area.yMin/PatchWidth),Mathf.FloorToInt((eye.z-distance)/PatchWidth)-1);
            int maxX=Mathf.Min(Mathf.CeilToInt(area.xMax/PatchWidth),Mathf.FloorToInt((eye.x+distance)/PatchWidth)+2);
            int maxZ=Mathf.Min(Mathf.CeilToInt(area.yMax/PatchWidth),Mathf.FloorToInt((eye.z+distance)/PatchWidth)+2);
            return new RectInt(minX,minZ,Mathf.Max(0,maxX-minX),Mathf.Max(0,maxZ-minZ));
        }

        public void SetDensity(float value)
        {
            Density=Mathf.Clamp01(value);
            PlayerPrefs.SetFloat("Options.GrassDensity",Density);
            PlayerPrefs.Save();
        }

        private void OnDestroy()
        {
            if(surfaceMap!=null) { surfaceMap.Release(); Release(surfaceMap); }
            if(densityMap!=null) Release(densityMap);
            if(mesh!=null) Release(mesh);
            if(material!=null) Release(material);
        }
        private static void Release(Object value)
        { if(Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
