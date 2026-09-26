using System.Collections.Generic;
using CombatExtended;
using RimWorld;
using Verse;
using Verse.AI;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Haulers reload the guns of landed aircraft and their weapon mounts (Combat Extended ammo).
    /// The class name is the one used by the original mod's WorkGiverDef.
    /// </summary>
    public class WorkGiver_ReloadTurret : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool Prioritized => true;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            MapComponent_AerocraftTracker tracker = MapComponent_AerocraftTracker.For(pawn.Map);
            if (tracker == null)
            {
                yield break;
            }
            List<Building_Aerocraft_Base> turrets = tracker.Turrets;
            for (int i = 0; i < turrets.Count; i++)
            {
                yield return turrets[i];
            }
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (!forced && !MYDE_AerocraftFramework_Setting.If_AutoReload)
            {
                return true;
            }
            MapComponent_AerocraftTracker tracker = MapComponent_AerocraftTracker.For(pawn.Map);
            return tracker == null || tracker.Turrets.Count == 0;
        }

        public override float GetPriority(Pawn pawn, TargetInfo t)
        {
            if (!(t.Thing is Building_Aerocraft_Base turret))
            {
                return 0f;
            }
            CompAmmoUser comp = AerocraftReloadUtility.AmmoOf(turret.Gun_Now);
            if (comp != null && comp.EmptyMagazine)
            {
                return 9f;
            }
            return 5f;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return FindGun(pawn, t, forced, out _, out _);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!FindGun(pawn, t, forced, out Thing gun, out Thing ammo))
            {
                return null;
            }
            return AerocraftReloadUtility.MakeReloadJob((Building_Aerocraft_Base)t, gun, ammo);
        }

        private static bool FindGun(Pawn pawn, Thing t, bool forced, out Thing gun, out Thing ammo)
        {
            gun = null;
            ammo = null;
            if (!(t is Building_Aerocraft_Base turret))
            {
                return false;
            }
            AcceptanceReport serviceable = AerocraftReloadUtility.TurretServiceable(turret, pawn.Faction);
            if (!serviceable.Accepted)
            {
                if (forced && !serviceable.Reason.NullOrEmpty())
                {
                    JobFailReason.Is(serviceable.Reason);
                }
                return false;
            }
            AcceptanceReport lastRefusal = true;
            foreach (Thing candidate in AerocraftReloadUtility.GunsToService(turret, MYDE_AerocraftFramework_Setting.If_ReloadAllWeapons || forced))
            {
                if (!AerocraftReloadUtility.GunNeedsReload(candidate))
                {
                    continue;
                }
                AcceptanceReport report = AerocraftReloadUtility.CanReload(pawn, turret, candidate, forced, out ammo);
                if (report.Accepted)
                {
                    gun = candidate;
                    return true;
                }
                lastRefusal = report;
            }
            if (forced && !lastRefusal.Accepted && !lastRefusal.Reason.NullOrEmpty())
            {
                JobFailReason.Is(lastRefusal.Reason);
            }
            return false;
        }
    }
}
