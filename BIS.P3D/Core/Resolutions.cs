using System;
using System.Collections.Generic;
using System.Text;

namespace BIS.P3D
{
    public enum LodName
    {
        ViewGunner,
        ViewPilot,
        ViewCargo,
        Geometry,
        Memory,
        LandContact,
        Roadway,
        Paths,
        HitPoints,
        ViewGeometry,
        FireGeometry,
        ViewCargoGeometry,
        ViewCargoFireGeometry,
        ViewCommander,
        ViewCommanderGeometry,
        ViewCommanderFireGeometry,
        ViewPilotGeometry,
        ViewPilotFireGeometry,
        ViewGunnerGeometry,
        ViewGunnerFireGeometry,
        SubParts,
        ShadowVolumeViewCargo,
        ShadowVolumeViewPilot,
        ShadowVolumeViewGunner,
        Wreck,
        PhysX,
        ShadowVolume,
        Resolution,
        Undefined
    }

    public static class Resolution
    {
        private const float specialLod = 1e15f;

        public const float GEOMETRY = 1e13f;
        public const float BUOYANCY = 2e13f;
        public const float PHYSXOLD = 3e13f;
        public const float PHYSX = 4e13f;

        public const float MEMORY = 1e15f;
        public const float LANDCONTACT = 2e15f;
        public const float ROADWAY = 3e15f;
        public const float PATHS = 4e15f;
        public const float HITPOINTS = 5e15f;

        public const float VIEW_GEOMETRY = 6e15f;
        public const float FIRE_GEOMETRY = 7e15f;

        public const float VIEW_GEOMETRY_CARGO = 8e15f;
        public const float VIEW_GEOMETRY_PILOT = 13e15f;
        public const float VIEW_GEOMETRY_GUNNER = 15e15f;
        public const float FIRE_GEOMETRY_GUNNER = 16e15f;

        public const float SUBPARTS = 17e15f;

        public const float SHADOWVOLUME_CARGO = 18e15f;
        public const float SHADOWVOLUME_PILOT = 19e15f;
        public const float SHADOWVOLUME_GUNNER = 20e15f;

        public const float WRECK = 21e15f;

        public const float VIEW_COMMANDER = 10e15f;
        public const float VIEW_GUNNER = 1000f;
        public const float VIEW_PILOT = 1100f;
        public const float VIEW_CARGO = 1200f;

        public const float SHADOWVOLUME = 10000.0f;
        public const float SHADOWBUFFER = 11000.0f;

        public const float SHADOW_MIN = 10000.0f;
        public const float SHADOW_MAX = 20000.0f;

        private static readonly Dictionary<float, LodName> LOD_MAP = new()
        {
            { MEMORY, LodName.Memory },
            { LANDCONTACT, LodName.LandContact },
            { ROADWAY, LodName.Roadway },
            { PATHS, LodName.Paths },
            { HITPOINTS, LodName.HitPoints },
            { 6e15f, LodName.ViewGeometry },
            { FIRE_GEOMETRY, LodName.FireGeometry },
            { 8e15f, LodName.ViewCargoGeometry },
            { 9e15f, LodName.ViewCargoFireGeometry },
            { VIEW_COMMANDER, LodName.ViewCommander },
            { 11e15f, LodName.ViewCommanderGeometry },
            { 12e15f, LodName.ViewCommanderFireGeometry },
            { VIEW_GEOMETRY_PILOT, LodName.ViewPilotGeometry },
            { 14e15f, LodName.ViewPilotFireGeometry },
            { VIEW_GEOMETRY_GUNNER, LodName.ViewGunnerGeometry },
            { FIRE_GEOMETRY_GUNNER, LodName.ViewGunnerFireGeometry },
            { SUBPARTS, LodName.SubParts },
            { SHADOWVOLUME_CARGO, LodName.ShadowVolumeViewCargo },
            { SHADOWVOLUME_PILOT, LodName.ShadowVolumeViewPilot },
            { SHADOWVOLUME_GUNNER, LodName.ShadowVolumeViewGunner },
            { WRECK, LodName.Wreck },
            { VIEW_GUNNER, LodName.ViewGunner },
            { VIEW_PILOT, LodName.ViewPilot },
            { VIEW_CARGO, LodName.ViewCargo },
            { GEOMETRY, LodName.Geometry },
            { PHYSX, LodName.PhysX },
        };

        private static readonly HashSet<float> KEEPS_NAMED_SELECTIONS = new()
        {
            MEMORY, FIRE_GEOMETRY, GEOMETRY, VIEW_GEOMETRY,
            VIEW_GEOMETRY_PILOT, VIEW_GEOMETRY_GUNNER, VIEW_GEOMETRY_CARGO,
            PATHS, HITPOINTS, PHYSX, BUOYANCY
        };

        public static bool KeepsNamedSelections(float r)
        {
            return KEEPS_NAMED_SELECTIONS.Contains(r);
        }

        public static LodName GetLODType(this float res)
        {
            if (LOD_MAP.TryGetValue(res, out var lod))
                return lod;

            if (res >= SHADOW_MIN && res <= SHADOW_MAX)
                return LodName.ShadowVolume;

            return LodName.Resolution;
        }

        public static string GetLODName(this float res)
        {
            var lodType = res.GetLODType();
            string name = "Unknown";

            if (lodType == LodName.Resolution)
                name = $"LOD {res:F1}";
            else if (lodType == LodName.ShadowVolume)
                name = "ShadowVolume" + (res - 10000f).ToString("0.000");
            else
                name = Enum.GetName(typeof(LodName), lodType) ?? "Unknown";

            return $"{name} ({res:F1})";
        }

        public static bool IsResolution(float r)
        {
            return r < SHADOW_MIN;
        }

        public static bool IsShadow(float r)
        {
            return (r >= SHADOW_MIN && r < SHADOW_MAX) ||
                r == SHADOWVOLUME_GUNNER ||
                r == SHADOWVOLUME_PILOT ||
                r == SHADOWVOLUME_CARGO;
        }

        public static bool IsVisual(float r)
        {
            return IsResolution(r) ||
                r == VIEW_CARGO ||
                r == VIEW_GUNNER ||
                r == VIEW_PILOT ||
                r == VIEW_COMMANDER;
        }
    }
}
