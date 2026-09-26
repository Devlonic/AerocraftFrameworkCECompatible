using System;
using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MYDE_AerocraftFramework
{
    [DefOf]
    public static class MYDE_CEJobDefOf
    {
        public static JobDef MYDE_AF_CE_ReloadTurret;

        static MYDE_CEJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MYDE_CEJobDefOf));
        }
    }

    /// <summary>Who can reload which aircraft gun with what ammo, and making the reload jobs.</summary>
    public static class AerocraftReloadUtility
    {
        private const float MaxAmmoSearchRadiusForNonPlayerPawns = 40f;

        public static CompAmmoUser AmmoOf(Thing gun) => gun?.TryGetComp<CompAmmoUser>();

        public static bool GunNeedsReload(Thing gun)
        {
            CompAmmoUser comp = AmmoOf(gun);
            return comp != null && comp.HasMagazine && !comp.FullMagazine;
        }

        /// <summary>Guns of a turret that colonists should keep loaded: the current one, and with the setting the stored ones too.</summary>
        public static IEnumerable<Thing> GunsToService(Building_Aerocraft_Base turret, bool includeStored)
        {
            if (turret.Gun_Now != null)
            {
                yield return turret.Gun_Now;
            }
            if (!includeStored)
            {
                yield break;
            }
            for (int i = 0; i < turret.Gun_InnerList.Count; i++)
            {
                yield return turret.Gun_InnerList[i];
            }
        }

        /// <summary>Checks that do not depend on the pawn: landed, not burning, not already being reloaded, friendly.</summary>
        public static AcceptanceReport TurretServiceable(Building_Aerocraft_Base turret, Faction faction)
        {
            if (turret == null || !turret.Spawned || turret.Destroyed)
            {
                return false;
            }
            AcceptanceReport service = AerocraftUtility.ServiceAllowed(turret);
            if (!service.Accepted)
            {
                return service;
            }
            if (turret.isReloading)
            {
                return "CE_TurretAlreadyReloading".Translate();
            }
            if (turret.IsBurning())
            {
                return "CE_TurretIsBurning".Translate();
            }
            if (faction != null && turret.Faction != faction && (turret.Faction == null || turret.Faction.RelationKindWith(faction) != FactionRelationKind.Ally))
            {
                return "CE_TurretNonAllied".Translate();
            }
            return true;
        }

        public static string AmmoLabel(CompAmmoUser comp)
        {
            return comp?.SelectedAmmo?.LabelCap ?? comp?.Props.ammoSet?.LabelCap ?? "";
        }

        /// <summary>The closest usable ammo for the gun: its selected type (non-player turrets fall back to any type of the set when empty).</summary>
        public static Thing FindAmmo(Pawn pawn, CompAmmoUser comp, Building_Aerocraft_Base turret)
        {
            if (comp == null || !comp.UseAmmo || comp.SelectedAmmo == null)
            {
                return null;
            }
            Thing ammo = FindAmmo(pawn, comp.SelectedAmmo);
            if (ammo == null && comp.EmptyMagazine && turret.Faction != Faction.OfPlayer && comp.Props.ammoSet?.ammoTypes != null)
            {
                foreach (AmmoLink link in comp.Props.ammoSet.ammoTypes)
                {
                    ammo = FindAmmo(pawn, link.ammo);
                    if (ammo != null)
                    {
                        break;
                    }
                }
            }
            return ammo;
        }

        private static Thing FindAmmo(Pawn pawn, ThingDef ammoDef)
        {
            if (ammoDef == null || pawn.Map.listerThings.ThingsOfDef(ammoDef).Count == 0)
            {
                return null;
            }
            float maxDistance = pawn.Faction == Faction.OfPlayer ? 9999f : MaxAmmoSearchRadiusForNonPlayerPawns;
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(ammoDef), PathEndMode.ClosestTouch, TraverseParms.For(pawn), maxDistance,
                t => !(t is AmmoThing ammoThing && ammoThing.IsCookingOff) && !t.IsBurning() && !t.IsForbidden(pawn) && pawn.CanReserve(t));
        }

        /// <summary>Whether any ammo for the gun lies on the map at all (fast check for the UI).</summary>
        public static bool AmmoExistsOnMap(Map map, CompAmmoUser comp)
        {
            if (!comp.UseAmmo)
            {
                return true;
            }
            List<Thing> things = map.listerThings.ThingsOfDef(comp.SelectedAmmo);
            for (int i = 0; i < things.Count; i++)
            {
                if (!things[i].IsForbidden(Faction.OfPlayer))
                {
                    return true;
                }
            }
            return false;
        }

        public static AcceptanceReport CanReload(Pawn pawn, Building_Aerocraft_Base turret, Thing gun, bool forced, out Thing ammo)
        {
            ammo = null;
            CompAmmoUser comp = AmmoOf(gun);
            if (pawn == null || comp == null || !comp.HasMagazine)
            {
                return false;
            }
            if (comp.FullMagazine)
            {
                return "CE_TurretFull".Translate();
            }
            AcceptanceReport serviceable = TurretServiceable(turret, pawn.Faction);
            if (!serviceable.Accepted)
            {
                return serviceable;
            }
            if (pawn.Downed || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                return "Incapable".Translate();
            }
            if (turret.IsForbidden(pawn))
            {
                return "ForbiddenLower".Translate();
            }
            if (!pawn.CanReserveAndReach(turret, PathEndMode.Touch, forced ? Danger.Deadly : pawn.NormalMaxDanger(), 1, -1, null, forced))
            {
                return "AerocraftFramework_CannotReachOrReserved".Translate();
            }
            if (comp.UseAmmo)
            {
                ammo = FindAmmo(pawn, comp, turret);
                if (ammo == null)
                {
                    return "CE_NoAmmoAvailable".Translate() + ": " + AmmoLabel(comp);
                }
            }
            return true;
        }

        public static Job MakeReloadJob(Building_Aerocraft_Base turret, Thing gun, Thing ammo)
        {
            CompAmmoUser comp = AmmoOf(gun);
            Job job = JobMaker.MakeJob(MYDE_CEJobDefOf.MYDE_AF_CE_ReloadTurret, turret, ammo, gun);
            if (ammo != null && comp != null)
            {
                int roundsPerItem = Math.Max(1, (ammo.def as AmmoDef)?.ammoCount ?? 1);
                int itemsNeeded = Mathf.CeilToInt((float)comp.MissingToFullMagazine / roundsPerItem);
                job.count = Mathf.Clamp(itemsNeeded, 1, ammo.stackCount);
            }
            return job;
        }

        /// <summary>The first gun of the turret that can take this ammo (null ammo: no ammo system) and is not full.</summary>
        public static Thing GunForAmmo(Building_Aerocraft_Base turret, ThingDef ammoDef, Thing preferred)
        {
            if (preferred != null && Accepts(preferred, ammoDef) && turret.AllGuns.Contains(preferred))
            {
                return preferred;
            }
            return turret.AllGuns.FirstOrDefault(g => Accepts(g, ammoDef));
        }

        private static bool Accepts(Thing gun, ThingDef ammoDef)
        {
            CompAmmoUser comp = AmmoOf(gun);
            if (comp == null || !comp.HasMagazine || comp.FullMagazine)
            {
                return false;
            }
            if (ammoDef == null)
            {
                return !comp.UseAmmo;
            }
            return comp.UseAmmo && comp.Props.ammoSet?.ammoTypes != null && comp.Props.ammoSet.ammoTypes.Any(l => l.ammo == ammoDef);
        }

        public static bool IsReloadingTurret(Pawn pawn, Building_Aerocraft_Base turret)
        {
            if (pawn.CurJobDef == MYDE_CEJobDefOf.MYDE_AF_CE_ReloadTurret && pawn.CurJob.targetA.Thing == turret)
            {
                return true;
            }
            return pawn.jobs?.jobQueue != null && pawn.jobs.jobQueue.Any(q => q.job.def == MYDE_CEJobDefOf.MYDE_AF_CE_ReloadTurret && q.job.targetA.Thing == turret);
        }

        /// <summary>
        /// Orders a reload of the gun. Without a given pawn, the closest able colonist is chosen, preferring one that is
        /// not reloading already; a colonist that is already reloading gets the new job queued.
        /// </summary>
        public static Pawn TryOrderReload(Building_Aerocraft_Base turret, Thing gun, Pawn pawn, bool showMessages)
        {
            AcceptanceReport lastRefusal = false;
            Thing ammo = null;
            if (pawn == null)
            {
                IEnumerable<Pawn> candidates = turret.Map.mapPawns.FreeColonistsSpawned
                    .Where(p => !p.Downed && !p.InMentalState && !p.WorkTagIsDisabled(WorkTags.Hauling))
                    .OrderBy(p => p.CurJobDef == MYDE_CEJobDefOf.MYDE_AF_CE_ReloadTurret ? 1 : 0)
                    .ThenBy(p => p.Drafted ? 1 : 0)
                    .ThenBy(p => p.Position.DistanceToSquared(turret.Position));
                foreach (Pawn candidate in candidates)
                {
                    AcceptanceReport report = CanReload(candidate, turret, gun, forced: true, out ammo);
                    if (report.Accepted)
                    {
                        pawn = candidate;
                        break;
                    }
                    lastRefusal = report;
                }
            }
            else
            {
                lastRefusal = CanReload(pawn, turret, gun, forced: true, out ammo);
                if (!lastRefusal.Accepted)
                {
                    pawn = null;
                }
            }
            if (pawn == null)
            {
                if (showMessages)
                {
                    string reason = lastRefusal.Reason.NullOrEmpty() ? "AerocraftFramework_NoColonistCanReload".Translate().ToString() : lastRefusal.Reason;
                    Messages.Message(reason, turret, MessageTypeDefOf.RejectInput, historical: false);
                }
                return null;
            }
            Job job = MakeReloadJob(turret, gun, ammo);
            job.playerForced = true;
            bool queue = pawn.CurJobDef == MYDE_CEJobDefOf.MYDE_AF_CE_ReloadTurret;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing: queue);
            return pawn;
        }
    }
}
