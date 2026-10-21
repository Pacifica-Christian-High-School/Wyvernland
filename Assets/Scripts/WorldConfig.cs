using System;
using UnityEngine;

namespace Wyvernland
{
    public enum ThermalTransitionKind { Smoke, Melt }

    /// <summary>Temperature range at which a material begins its thermal transition.</summary>
    public readonly struct ThermalMaterialRecord
    {
        public readonly string Material;
        public readonly ThermalTransitionKind Transition;
        public readonly float MinimumCelsius;
        public readonly float MaximumCelsius;
        public readonly float MinimumHardness;
        public readonly float MaximumHardness;
        public readonly int Resilience;
        public readonly float MinimumResilience;
        public readonly float MaximumResilience;
        public readonly string AppliesTo;

        public ThermalMaterialRecord(string material,ThermalTransitionKind transition,
            float minimumCelsius,float maximumCelsius,float minimumHardness,
            float maximumHardness,int resilience,string appliesTo,
            float? minimumResilience=null,float? maximumResilience=null)
        {
            Material=material;
            Transition=transition;
            MinimumCelsius=minimumCelsius;
            MaximumCelsius=maximumCelsius;
            MinimumHardness=minimumHardness;
            MaximumHardness=maximumHardness;
            Resilience=resilience;
            MinimumResilience=minimumResilience??resilience;
            MaximumResilience=maximumResilience??resilience;
            AppliesTo=appliesTo;
        }
    }

    // Traces in the 520 x 697 env.png annotation, registered to the original map photograph.
    public static class EnvironmentRegions
    {
        public static readonly Color DarkBrown=new(0.065f,0.022f,0.009f,1f);
        public static Vector2 World(float x,float y)=>new(
            (x*1368f/520f-40f)/1210f*WorldConfig.MapWidth-WorldConfig.MapWidth*0.5f,
            (1660f-y*1824f/697f)/1510f*WorldConfig.MapLength-WorldConfig.MapLength*0.5f);
        public static readonly Vector4[] Woods={Wood(176,220,39,57),Wood(423,295,27,50),
            Wood(318,361,36,31),Wood(328,467,23,46)};
        private static Vector4 Wood(float x,float y,float rx,float rz)
        {
            Vector2 p=World(x,y);
            return new Vector4(p.x,p.y,rx*1368f/520f/1210f*WorldConfig.MapWidth,
                rz*1824f/697f/1510f*WorldConfig.MapLength);
        }
        public static float WoodsDensity(float x,float z)
        {
            float result=0f;
            foreach(Vector4 wood in Woods)
            {
                float d=new Vector2((x-wood.x)/wood.z,(z-wood.y)/wood.w).magnitude;
                result=Mathf.Max(result,1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0.85f,1f,d)));
            }
            return result;
        }
        private static readonly Vector4[] Pits=BuildPits();
        private static Vector4[] BuildPits()
        {
            var pits=new System.Collections.Generic.List<Vector4>();
            var random=new System.Random(921736);
            foreach(Vector2 mark in new[]{new Vector2(217,278),new Vector2(359,317),
                new Vector2(95,366),new Vector2(245,445)})
            {
                Vector2 centre=World(mark.x,mark.y);
                for(int i=0;i<7;i++)
                {
                    float angle=i*Mathf.PI*2f/7f;
                    float spread=i==0?0f:65f+(float)random.NextDouble()*35f;
                    Vector2 p=centre+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*spread;
                    pits.Add(new Vector4(p.x,p.y,10f+(float)random.NextDouble()*10f,
                        10f+(float)random.NextDouble()*5f));
                }
            }
            return pits.ToArray();
        }
        public static float PitDepth(float x,float z)
        {
            Vector2 centre=World(397,389);
            float d=new Vector2((x-centre.x)/340f,(z-centre.y)/420f).magnitude;
            float depth=30f*(1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0.25f,1f,d)));
            foreach(Vector4 pit in Pits)
            {
                float dx=x-pit.x,dz=z-pit.y;
                if(Mathf.Abs(dx)>pit.z||Mathf.Abs(dz)>pit.z) continue;
                float angle=Mathf.Atan2(dz,dx);
                float radius=pit.z*(0.85f+0.10f*Mathf.Sin(angle*3f+pit.x)+0.05f*Mathf.Sin(angle*5f+pit.y));
                float t=Mathf.Sqrt(dx*dx+dz*dz)/radius;
                depth=Mathf.Max(depth,pit.w*(1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0.2f,1f,t))));
            }
            return depth;
        }
    }

    /// <summary>
    /// Shared real-world measurements for the first playable map prototype.
    /// Unity units are treated as metres throughout the project.
    /// </summary>
    public static class WorldConfig
    {
        public const float MapWidth = 5000f;
        public const float MapLength = 6500f;
        public const float SeaFloorDepth = 35f;
        // The highest drawn contour is 300 m; rounded terminal summits rise up
        // to roughly 340 m, plus at most one metre of surface variation.
        public const float MaximumElevation = 342f;
        public const float TerrainHeight = 342f;

        public const float PlayerHeight = 1.8f;
        public const float PlayerRadius = 0.3f;

        public static readonly Color GroundColor = new(0.43f, 0.16f, 0.075f, 1f);
        public static readonly Color OceanColor = new(0.025f, 0.32f, 0.47f, 1f);
        public static readonly Color SkyColor = new(0.20f, 0.62f, 0.91f, 1f);

        public static readonly string[] MineralMaterials =
        {
            "Quartz",
            "Magnetite",
            "Limestone",
            "Stone",
            "River Rock",
            "Chalk stone",
            "Obsidian",
            "Coal",
            "Hematite",
            "Native Copper",
            "Pyrite",
            "Chalcocite",
            "Limonite",
            "Cassiterite",
            "Galena",
            "Sulfur",
            "Saltpeter",
            "Meteorite",
            "Basalt"
        };

        // Metal names are kept separate from ores and other minerals. New
        // metal materials can be added here without changing impact rules.
        public static readonly string[] MetalMaterials =
        {
            "Iron", "Native Copper", "Steel", "Copper", "Bronze", "Brass",
            "Silver", "Gold", "Tin", "Lead", "Zinc", "Nickel",
            "Aluminum", "Aluminium", "Titanium"
        };

        public static bool IsMetalMaterial(string material) =>
            !string.IsNullOrEmpty(material)&&Array.Exists(MetalMaterials,
                name=>string.Equals(name,material,StringComparison.OrdinalIgnoreCase));

        // Approximate beginning temperatures in degrees Celsius. Ranges are
        // retained so later temperature simulation can choose a value based on
        // moisture, purity, composition or object-specific variation.
        public static readonly ThermalMaterialRecord[] ThermalMaterials =
        {
            new("Grass",ThermalTransitionKind.Smoke,150f,200f,1f,1f,9,"All grass objects"),
            new("Wood",ThermalTransitionKind.Smoke,200f,250f,2f,3f,7,"All trees and stumps"),
            new("Burnt Wood",ThermalTransitionKind.Smoke,200f,250f,1.5f,2.5f,5,
                "Burnt trees and stumps",3f,7f),
            new("Iron",ThermalTransitionKind.Melt,1538f,1538f,6f,8f,8,"All iron objects"),
            new("Quartz",ThermalTransitionKind.Melt,1713f,1713f,7f,7f,3,"All quartz objects"),
            new("Magnetite",ThermalTransitionKind.Melt,1597f,1597f,5.5f,6.5f,4,"All magnetite objects"),
            new("Limestone",ThermalTransitionKind.Melt,825f,825f,3f,4f,3,"All limestone objects"),
            new("Stone",ThermalTransitionKind.Melt,1000f,1300f,2f,5f,2,"All stone objects"),
            new("River Rock",ThermalTransitionKind.Melt,1000f,1300f,5f,7f,6,"All river-rock objects"),
            new("Chalk stone",ThermalTransitionKind.Melt,825f,825f,1f,3f,2,"All chalk-stone objects"),
            new("Obsidian",ThermalTransitionKind.Melt,1000f,1200f,5f,5.5f,1,"All obsidian objects"),
            new("Coal",ThermalTransitionKind.Melt,300f,500f,1f,3f,2,"All coal objects"),
            new("Hematite",ThermalTransitionKind.Melt,1565f,1565f,5f,6.5f,4,"All hematite objects"),
            new("Native Copper",ThermalTransitionKind.Melt,1085f,1085f,2.5f,3f,9,"All native-copper objects"),
            new("Pyrite",ThermalTransitionKind.Melt,1177f,1177f,6f,6.5f,3,"All pyrite objects"),
            new("Chalcocite",ThermalTransitionKind.Melt,1130f,1130f,2.5f,3f,3,"All chalcocite objects"),
            new("Limonite",ThermalTransitionKind.Melt,250f,350f,1f,5f,3,"All limonite objects"),
            new("Cassiterite",ThermalTransitionKind.Melt,1630f,1630f,6f,7f,4,"All cassiterite objects"),
            new("Galena",ThermalTransitionKind.Melt,1114f,1114f,2.5f,2.5f,3,"All galena objects"),
            new("Sulfur",ThermalTransitionKind.Melt,115f,115f,1.5f,2.5f,1,"All sulfur objects"),
            new("Saltpeter",ThermalTransitionKind.Melt,334f,334f,2f,2f,1,"All saltpeter objects"),
            new("Meteorite",ThermalTransitionKind.Melt,1200f,1535f,4f,7f,7,"All meteorite objects"),
            new("Basalt",ThermalTransitionKind.Melt,1000f,1250f,6f,6f,6,"All basalt objects")
        };

        public static readonly Rect MapBounds = new(
            -MapWidth * 0.5f,
            -MapLength * 0.5f,
            MapWidth,
            MapLength);
    }
}
