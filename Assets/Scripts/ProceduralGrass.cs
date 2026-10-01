using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    /// <summary>Seeded, terrain-aligned pixel silhouettes, streamed in small meshes.</summary>
    public sealed class ProceduralGrass : MonoBehaviour
    {
        private const float TileSize = 32f;
        private const float DefaultDrawDistance = 320f;
        private const float MaximumDrawDistance = 240f;
        private const float FadeInDuration = 1.25f;
        private readonly Dictionary<Vector2Int, GameObject> tiles = new();
        private readonly List<Vector2Int> pending = new();
        private readonly List<Vector2Int> expired = new();
        private Transform observer;
        private ProceduralMap map;
        private WorldObjectStreaming streaming;
        private Material material;
        private float drawDistance=DefaultDrawDistance;
        private Vector2Int lastTile = new(int.MinValue, int.MinValue);
        public float Density { get; private set; } = 1f;

        // The shader clips one quad into each of these original pixel dimensions.
        private static readonly float[] ShapeHeights = { 9f, 9f, 13f };
        private static readonly float[] ShapeWidths = { 11f, 13f, 14f };
        private static readonly Color[] Palette =
        {
            new Color32(190, 122, 86, 255),
            new Color32(176, 79, 31, 255),
            new Color32(128, 55, 25, 255)
        };

        public void Initialize(FirstPersonController player, ProceduralMap terrain)
        {
            observer = player.transform;
            map = terrain;
            streaming = FindFirstObjectByType<WorldObjectStreaming>();
            drawDistance=Mathf.Min(streaming!=null?streaming.LoadingRange:DefaultDrawDistance,
                MaximumDrawDistance);
            Density=Mathf.Clamp01(PlayerPrefs.GetFloat("Options.GrassDensity",1f));
            Shader shader = Resources.Load<Shader>("Grass");
            if (shader == null) throw new System.InvalidOperationException("Missing grass shader.");
            material = new Material(shader) { name = "Three-color translucent grass" };
            material.SetFloat("_DrawDistance",drawDistance);
            material.SetFloat("_FadeStart",drawDistance*0.45f);
            material.SetFloat("_FadeDuration", FadeInDuration);
        }

        private static int Seed(int x, int z)
        {
            unchecked { return ((x * 73856093) ^ (z * 19349663) ^ 51797) & int.MaxValue; }
        }

        private static uint Hash(int x,int z,int salt)
        {
            unchecked
            {
                uint value=(uint)(x*73856093)^(uint)(z*19349663)^(uint)salt;
                value^=value>>16;
                value*=0x7feb352du;
                value^=value>>15;
                value*=0x846ca68bu;
                return value^(value>>16);
            }
        }

        private static float Hash01(int x,int z,int salt) =>
            (Hash(x,z,salt)&0x00ffffffu)/16777216f;

        private static Color RegionalColor(float x, float z, System.Random grassRandom)
        {
            const float regionSize = 266f;
            const float blendWidth = 198f;
            int cellX = Mathf.FloorToInt(x/regionSize);
            int cellZ = Mathf.FloorToInt(z/regionSize);
            float nearest = float.PositiveInfinity, second = float.PositiveInfinity;
            int nearestColor = 0, secondColor = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cellX+dx, gz = cellZ+dz;
                Vector2 centre = new((gx+0.18f+Hash01(gx,gz,0x35a17)*0.64f)*regionSize,
                    (gz+0.18f+Hash01(gx,gz,0x4c921)*0.64f)*regionSize);
                float distance = Vector2.Distance(new Vector2(x,z),centre);
                int color = (int)(Hash(gx,gz,0x72bd1)%Palette.Length);
                if (distance < nearest)
                {
                    second = nearest; secondColor = nearestColor;
                    nearest = distance; nearestColor = color;
                }
                else if (distance < second)
                {
                    second = distance; secondColor = color;
                }
            }
            float neighborMix = nearestColor == secondColor ? 0f :
                0.5f*(1f-Mathf.SmoothStep(0f,1f,(second-nearest)/blendWidth));
            Color colorValue = Color.Lerp(Palette[nearestColor],Palette[secondColor],neighborMix);
            // Keep variation inside a region small enough to read as one color.
            Color.RGBToHSV(colorValue,out float hue,out float saturation,out float value);
            hue = Mathf.Repeat(hue+((float)grassRandom.NextDouble()-0.5f)*0.0192f,1f);
            saturation = Mathf.Clamp01(saturation*Mathf.Lerp(0.952f,1.048f,(float)grassRandom.NextDouble()));
            value = Mathf.Clamp01(value*Mathf.Lerp(0.936f,1.064f,(float)grassRandom.NextDouble()));
            return Color.HSVToRGB(hue,saturation,value);
        }

        private void Update()
        {
            if (observer == null) return;
            float requestedRange=Mathf.Min(
                streaming!=null?streaming.LoadingRange:DefaultDrawDistance,MaximumDrawDistance);
            if(!Mathf.Approximately(requestedRange,drawDistance))
            {
                drawDistance=requestedRange;
                material.SetFloat("_DrawDistance",drawDistance);
                material.SetFloat("_FadeStart",drawDistance*0.45f);
                lastTile=new Vector2Int(int.MinValue,int.MinValue);
            }
            Vector2Int current = new(Mathf.FloorToInt(observer.position.x / TileSize),
                Mathf.FloorToInt(observer.position.z / TileSize));
            if (current != lastTile)
            {
                int tileRadius = Mathf.CeilToInt((drawDistance+TileSize)/TileSize);
                int tileRadiusSquared = tileRadius*tileRadius;
                lastTile = current;
                pending.Clear();
                expired.Clear();
                foreach (var tile in tiles)
                    if ((tile.Key-current).sqrMagnitude > tileRadiusSquared) expired.Add(tile.Key);
                foreach (Vector2Int key in expired)
                {
                    Release(tiles[key]);
                    tiles.Remove(key);
                }
                // Extra tile margin keeps the 80 m fade band fully populated.
                for (int z = -tileRadius; z <= tileRadius; z++)
                for (int x = -tileRadius; x <= tileRadius; x++)
                {
                    Vector2Int key = current + new Vector2Int(x,z);
                    if (x*x+z*z <= tileRadiusSquared && !tiles.ContainsKey(key)&&
                        TileIntersectsMap(key)) pending.Add(key);
                }
                pending.Sort((a,b) => (b-current).sqrMagnitude.CompareTo((a-current).sqrMagnitude));
            }
            // Near tiles first. Larger tiles substantially reduce renderer count;
            // limiting construction to one per frame avoids a generation spike.
            for (int i = 0; i < 1 && pending.Count > 0; i++)
            {
                int index = pending.Count-1;
                Vector2Int key = pending[index];
                pending.RemoveAt(index);
                tiles[key] = BuildTile(key);
            }
        }

        private bool TileIntersectsMap(Vector2Int key)
        {
            Rect bounds=map.Bounds;
            float minX=key.x*TileSize,minZ=key.y*TileSize;
            return minX<bounds.xMax&&minX+TileSize>bounds.xMin&&
                minZ<bounds.yMax&&minZ+TileSize>bounds.yMin;
        }

        private GameObject BuildTile(Vector2Int key)
        {
            var random = new System.Random(Seed(key.x,key.y));
            float Next() => (float)random.NextDouble();
            Vector3 origin = new(key.x*TileSize,0,key.y*TileSize);
            // Preserve the original density while batching four times the land area.
            // Stratification keeps density even without a visibly regular grid.
            int rows = random.Next(Mathf.RoundToInt(TileSize*0.9375f),
                Mathf.RoundToInt(TileSize)+1);
            int maximumClumps=rows*rows;
            var vertices = new List<Vector3>(maximumClumps*4);
            var colors = new List<Color>(maximumClumps*4);
            var uvs = new List<Vector3>(maximumClumps*4);
            var indices = new List<int>(maximumClumps*6);
            float spacing = TileSize/rows;
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < rows; x++)
            {
                float px = origin.x + (x+0.25f+Next()*0.5f)*spacing;
                float pz = origin.z + (z+0.25f+Next()*0.5f)*spacing;
                bool testGround = WorldBootstrap.TestGround;
                if (testGround && (px < -99.5f || px >= -0.5f || Mathf.Abs(pz) > 99.5f)) continue;
                float mapDensity=testGround ? 1f : ProceduralMap.GrassDensity(px,pz)*0.7f;
                float treeDensity=streaming!=null?streaming.GrassAroundTrees(px,pz):0f;
                float pileDensity=testGround ? -1f : GroundFeatureLayout.PileGrassDensity(px,pz);
                float areaDensity=pileDensity>=0f?pileDensity:Mathf.Max(mapDensity,treeDensity);
                if (Density<=0f||Next()>areaDensity*Density||
                    (!testGround && (ProceduralMap.IsRiverbed(px,pz)||
                    ProceduralMonumentField.ContainsInterior(px,pz,3f)||
                    !GroundFeatureLayout.AllowsGrass(px,pz)))||
                    !map.TryGetSurface(px,pz,out RaycastHit surface)) continue;
                if (surface.normal.y <= 0.25f) continue;
                // Independent 7 m patches choose single, paired or triple mixes.
                int patchX=Mathf.FloorToInt(px/7f),patchZ=Mathf.FloorToInt(pz/7f);
                int first=(int)(Hash(patchX,patchZ,0x2a531)%3u);
                int count=1+(int)(Hash(patchX,patchZ,0x61c47)%3u);
                int step=(Hash(patchX,patchZ,0x17d39)&1u)==0u?1:2;
                int type = (first+step*random.Next(count))%3;
                float height=pileDensity>=0f?Mathf.Lerp(0.18f,0.45f,Next()):
                    Mathf.Lerp(treeDensity>mapDensity?0.5f:0.4f,1f,Next());
                float scale = height/ShapeHeights[type];
                Color color = RegionalColor(px,pz,random);
                if (QualitySettings.activeColorSpace == ColorSpace.Linear) color = color.linear;
                color.a = Mathf.Lerp(0.4f,0.6f,Next());
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up,surface.normal) *
                    Quaternion.Euler(0,Next()*360f,0);
                Vector3 up = rotation*Vector3.up;
                Vector3 right = rotation*Vector3.right;
                Vector3 root = surface.point-origin+surface.normal*0.002f;
                float halfWidth=ShapeWidths[type]*scale*0.5f;
                int start = vertices.Count;
                vertices.Add(root-right*halfWidth);
                vertices.Add(root-right*halfWidth+up*height);
                vertices.Add(root+right*halfWidth+up*height);
                vertices.Add(root+right*halfWidth);
                for (int n = 0; n < 4; n++) colors.Add(color);
                uvs.Add(new Vector3(0f,0f,type)); uvs.Add(new Vector3(0f,1f,type));
                uvs.Add(new Vector3(1f,1f,type)); uvs.Add(new Vector3(1f,0f,type));
                indices.Add(start); indices.Add(start+1); indices.Add(start+2);
                indices.Add(start); indices.Add(start+2); indices.Add(start+3);
            }
            if (vertices.Count == 0) return null;
            Mesh mesh = new() { name = $"Grass patch {key}" };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices,0); mesh.SetColors(colors);
            mesh.SetUVs(0,uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject tile = new($"Grass {key.x}, {key.y}");
            tile.transform.SetParent(transform,false);
            tile.transform.position = origin;
            tile.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = tile.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            MaterialPropertyBlock properties = new();
            properties.SetFloat("_SpawnTime", Time.timeSinceLevelLoad);
            renderer.SetPropertyBlock(properties);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return tile;
        }

        public void SetDensity(float value)
        {
            value=Mathf.Clamp01(value);
            if(Mathf.Approximately(value,Density)) return;
            Density=value;
            PlayerPrefs.SetFloat("Options.GrassDensity",Density);
            PlayerPrefs.Save();
            foreach(GameObject tile in tiles.Values) Release(tile);
            tiles.Clear();
            pending.Clear();
            expired.Clear();
            lastTile=new Vector2Int(int.MinValue,int.MinValue);
        }

        private void Release(GameObject tile)
        {
            if (tile == null) return;
            tile.SetActive(false);
            Destroy(tile.GetComponent<MeshFilter>().sharedMesh);
            Destroy(tile);
        }

        private void OnDestroy()
        {
            foreach (GameObject tile in tiles.Values) Release(tile);
            if (material != null) Destroy(material);
        }
    }
}
