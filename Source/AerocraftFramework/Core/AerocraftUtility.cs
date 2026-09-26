using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    public static class AerocraftUtility
    {
        public static bool IsControllable(Thing thing)
        {
            return thing != null && (thing.Faction == Faction.OfPlayer || MYDE_AerocraftFramework_Setting.If_CanControlNonPlayer);
        }

        /// <summary>The aircraft body a turret belongs to (itself for a body, the parent for a weapon mount).</summary>
        public static Building_Aerocraft_AsBaseThing AircraftOf(Building_Aerocraft_Base turret)
        {
            if (turret is Building_Aerocraft_AsBaseThing body)
            {
                return body;
            }
            return (turret as Building_Aerocraft_AsWeapon)?.Building_Aerocraft_AsBaseThing;
        }

        /// <summary>
        /// Whether colonists may refuel or reload this turret now: it must be landed and, with the matching setting,
        /// inside the home area.
        /// </summary>
        public static AcceptanceReport ServiceAllowed(Building_Aerocraft_Base turret)
        {
            if (turret == null || !turret.Spawned)
            {
                return false;
            }
            if (!turret.Is_Static)
            {
                return "AerocraftFramework_Service_NotLanded".Translate();
            }
            if (MYDE_AerocraftFramework_Setting.If_ServiceOnlyInHomeArea && !turret.Map.areaManager.Home[turret.Position])
            {
                return "AerocraftFramework_Service_NotInHomeArea".Translate();
            }
            return true;
        }

        /// <summary>
        /// A point along the aircraft's current heading, up to <paramref name="maxRange"/> cells away, that is away from
        /// thick roofs and the map edge. Used for forced (fuel, stun, damage) landings of fixed-wing aircraft.
        /// </summary>
        public static Vector3 FindEmergencyLandingSpot(Building_Aerocraft_AsBaseThing aircraft, int maxRange)
        {
            Map map = aircraft.Map;
            Vector3 drawPos = aircraft.DrawPos;
            float angle = aircraft.Angle_Fly_Now - 90f;
            float range = 2f;
            for (int i = 1; i <= maxRange; i++)
            {
                IntVec3 c = MYDE_ModFront.GetVector3_By_AngleFlat(drawPos, i, angle).ToIntVec3();
                if (c.x >= map.Size.x - 5 || c.x <= 5 || c.z >= map.Size.z - 5 || c.z <= 5)
                {
                    break;
                }
                RoofDef roof = c.GetRoof(map);
                if (roof != null && roof.isThickRoof)
                {
                    break;
                }
                range = i;
            }
            return MYDE_ModFront.GetVector3_By_AngleFlat(drawPos, range, angle);
        }

        /// <summary>Free colonists sitting in aircraft on any map or flying between maps.</summary>
        public static bool AnyFreeColonistInAerocraft()
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                MapComponent_AerocraftTracker tracker = MapComponent_AerocraftTracker.For(maps[i]);
                if (tracker == null)
                {
                    continue;
                }
                foreach (Building_Aerocraft_AsBaseThing aircraft in tracker.Aircraft)
                {
                    if (HasFreeColonist(aircraft))
                    {
                        return true;
                    }
                }
            }
            List<WorldObject> worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i] is WorldObject_CrossMapThing_Flying flying && flying.LinkToAerocraft != null && HasFreeColonist(flying.LinkToAerocraft))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasFreeColonist(Building_Aerocraft_AsBaseThing aircraft)
        {
            foreach (Pawn pawn in aircraft.ListPawn)
            {
                if (pawn != null && !pawn.Dead && pawn.IsFreeColonist)
                {
                    return true;
                }
            }
            foreach (Pawn pawn in aircraft.ListCompTransporterPawn)
            {
                if (pawn != null && !pawn.Dead && pawn.IsFreeColonist)
                {
                    return true;
                }
            }
            return false;
        }

        public static void ThrowText(Thing thing, string text)
        {
            if (thing?.Map != null)
            {
                MoteMaker.ThrowText(thing.DrawPos, thing.Map, text, 3f);
            }
        }
    }
}
