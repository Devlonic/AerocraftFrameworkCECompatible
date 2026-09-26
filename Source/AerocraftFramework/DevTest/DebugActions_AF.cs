using System.Linq;
using LudeonTK;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>Dev-mode tools, category "Aerocraft Framework".</summary>
    public static class DebugActions_AF
    {
        private const string Category = "Aerocraft Framework";

        [DebugAction(Category, "Fill all aircraft magazines", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FillMagazines()
        {
            foreach (Building_Aerocraft_Base turret in MapComponent_AerocraftTracker.For(Find.CurrentMap).Turrets)
            {
                AerocraftCompat.Ammo.FillMagazines(turret);
            }
        }

        [DebugAction(Category, "Empty all aircraft magazines", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void EmptyMagazines()
        {
            foreach (Building_Aerocraft_Base turret in MapComponent_AerocraftTracker.For(Find.CurrentMap).Turrets)
            {
                foreach (Thing gun in turret.AllGuns)
                {
                    AerocraftCompat.Ammo.DebugSetMagazine(gun, 0);
                }
            }
        }

        [DebugAction(Category, "Log aircraft state", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogState()
        {
            foreach (Building_Aerocraft_Base turret in MapComponent_AerocraftTracker.For(Find.CurrentMap).Turrets)
            {
                string guns = string.Join(", ", turret.AllGuns.Select(g => Building_Aerocraft_Base.GunStatusLine(g) + (AerocraftCompat.Ammo.IsLinkedTo(g, turret) ? "" : " [NOT LINKED]")));
                string extra = turret is Building_Aerocraft_AsBaseThing aircraft
                    ? $" status {aircraft.FlightStatusLabel}, pilots {aircraft.ListPawn.Count}, mounts {aircraft.AllExtraWeapon.Count}, follow {aircraft.FollowTargetThing?.ToString() ?? "-"}"
                    : $" on {(turret as Building_Aerocraft_AsWeapon)?.Building_Aerocraft_AsBaseThing?.ToString() ?? "-"}";
                Log.Message($"[Aerocraft Framework] {turret} ({turret.Faction?.Name}) reloading {turret.isReloading}; {guns};{extra}");
            }
        }
    }
}
