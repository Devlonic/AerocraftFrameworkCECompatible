using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    internal static class MapHasPlayerAerocraft
    {
        public static bool Check(Map map)
        {
            if (map == null)
            {
                return false;
            }
            MapComponent_AerocraftTracker tracker = MapComponent_AerocraftTracker.For(map);
            return tracker != null && tracker.Aircraft.Any(a => a.Faction == Faction.OfPlayer);
        }
    }

    /// <summary>An enemy settlement map is kept while a player aircraft is on it.</summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.ShouldRemoveMapNow))]
    internal static class Patch_Settlement_ShouldRemoveMapNow
    {
        private static bool Prefix(Settlement __instance, ref bool __result)
        {
            if (__instance.Faction != Faction.OfPlayer && __instance.HasMap && MapHasPlayerAerocraft.Check(__instance.Map))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>A site map is kept while a player aircraft is on it.</summary>
    [HarmonyPatch(typeof(Site), nameof(Site.ShouldRemoveMapNow))]
    internal static class Patch_Site_ShouldRemoveMapNow
    {
        private static bool Prefix(Site __instance, ref bool __result)
        {
            if (__instance.Faction != Faction.OfPlayer && __instance.HasMap && MapHasPlayerAerocraft.Check(__instance.Map))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>A settlement is not defeated while a player aircraft is still on its map.</summary>
    [HarmonyPatch(typeof(SettlementDefeatUtility), "IsDefeated")]
    internal static class Patch_SettlementDefeatUtility_IsDefeated
    {
        private static bool Prefix(Map map, ref bool __result)
        {
            if (map != null && map.ParentFaction != Faction.OfPlayer && MapHasPlayerAerocraft.Check(map))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The transporter cargo stays on board when the aircraft leaves the map; it is still dropped when the aircraft
    /// is destroyed or uninstalled.
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.PostDeSpawn))]
    internal static class Patch_CompTransporter_PostDeSpawn
    {
        private static bool Prefix(CompTransporter __instance)
        {
            return !(__instance.parent is Building_Aerocraft_AsBaseThing aircraft && aircraft.DepartingCrossMap);
        }
    }

    /// <summary>The turret gun of an aircraft points where its (vanilla) projectile really goes.</summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new Type[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    internal static class Patch_Projectile_Launch
    {
        private static void Postfix(Projectile __instance, Vector3 ___destination, Vector3 ___origin, LocalTargetInfo intendedTarget)
        {
            if (__instance.Launcher is Building_Aerocraft_Base turret && intendedTarget.IsValid)
            {
                turret.Top.ExtraRotation = (___destination - ___origin).AngleFlat() - (intendedTarget.CenterVector3 - ___origin).AngleFlat();
            }
        }
    }

    /// <summary>Colonists refuel (rearm) aircraft only when they are landed (and, with the setting, in the home area).</summary>
    [HarmonyPatch(typeof(WorkGiver_Refuel_Turret), nameof(WorkGiver_Refuel_Turret.CanRefuelThing))]
    internal static class Patch_WorkGiver_Refuel_Turret_CanRefuelThing
    {
        private static bool Prefix(Thing t, ref bool __result)
        {
            if (t is Building_Aerocraft_Base turret && !AerocraftUtility.ServiceAllowed(turret).Accepted)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>Colonists sitting in aircraft (on a map or flying between maps) keep the colony alive.</summary>
    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    internal static class Patch_GameEnder_CheckOrUpdateGameOver
    {
        private static bool Prefix(GameEnder __instance)
        {
            if (Find.TickManager.TicksGame > 300 && AerocraftUtility.AnyFreeColonistInAerocraft())
            {
                __instance.gameEnding = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Right click with an aircraft selected: move to the clicked cell, follow a friendly thing or attack a hostile one.
    /// A selected weapon mount attacks the clicked thing.
    /// </summary>
    internal static class AerocraftOrders
    {
        public static void IssueOrder(object selected, IntVec3 cell, Map map)
        {
            if (!(selected is Building_Aerocraft_Base turret) || turret.Map != map || !AerocraftUtility.IsControllable(turret))
            {
                return;
            }
            Thing target = MYDE_ModFront.Get_TargetThing(cell.GetThingList(map));
            if (turret is Building_Aerocraft_AsBaseThing aircraft)
            {
                if (target == null || target == aircraft || (target is Building_Aerocraft_AsWeapon mount && mount.Building_Aerocraft_AsBaseThing == aircraft))
                {
                    aircraft.Set_TargetVPos(cell.ToVector3Shifted());
                }
                else if (target.Faction == aircraft.Faction)
                {
                    aircraft.Set_Target_FollowTargetThing(target);
                }
                else
                {
                    aircraft.Set_ForceTarget(target);
                }
            }
            else if (turret is Building_Aerocraft_AsWeapon weapon && target != null && target.Faction != weapon.Faction)
            {
                weapon.Set_ForceTarget(target);
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.TryMakeFloatMenu_NonPawn))]
    internal static class Patch_FloatMenuMakerMap_TryMakeFloatMenu_NonPawn
    {
        private static void Prefix(Thing selectedThing)
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = UI.MouseCell();
            if (map != null && cell.InBounds(map))
            {
                AerocraftOrders.IssueOrder(selectedThing, cell, map);
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.TryMakeMultiSelectFloatMenu))]
    internal static class Patch_FloatMenuMakerMap_TryMakeMultiSelectFloatMenu
    {
        private static readonly List<object> tmpSelected = new List<object>();

        private static void Prefix()
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = UI.MouseCell();
            if (map == null || !cell.InBounds(map))
            {
                return;
            }
            tmpSelected.Clear();
            tmpSelected.AddRange(Find.Selector.SelectedObjects);
            foreach (object selected in tmpSelected)
            {
                AerocraftOrders.IssueOrder(selected, cell, map);
            }
            tmpSelected.Clear();
        }
    }
}
