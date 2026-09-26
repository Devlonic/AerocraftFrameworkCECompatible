using System;
using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using CombatExtended.Compatibility;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Plugs Combat Extended into the framework: ammo behaviour, CE's turret registry and a few CE patches.
    /// This assembly is only loaded when Combat Extended is active.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CEBootstrap
    {
        public const string HarmonyId = "MYDE.AerocraftFramework.CE";

        static CEBootstrap()
        {
            AerocraftCompat.Ammo = new CEAmmoCompat();
            RegisterTurretTypes();
            new Harmony(HarmonyId).PatchAll(typeof(CEBootstrap).Assembly);
            if (!CEApi.AllBound)
            {
                Log.Error("[Aerocraft Framework] some Combat Extended methods were not found; reloading aircraft may not work with this CE version.");
            }
        }

        /// <summary>
        /// CE asks turrets that are not its own Building_TurretGunCE for their gun, ammo and reload state through
        /// TurretRegistry. The original mod never registered, so CE could not see the aircraft's ammo at all.
        /// </summary>
        private static void RegisterTurretTypes()
        {
            List<Type> types = typeof(Building_Aerocraft_Base).AllSubclassesNonAbstract().ToList();
            types.Add(typeof(Building_Aerocraft_Base));
            foreach (Type type in types.Distinct())
            {
                TurretRegistry.RegisterReloadableTurret(type,
                    (turret, reloading) => ((Building_Aerocraft_Base)turret).isReloading = reloading,
                    turret => ((Building_Aerocraft_Base)turret).isReloading,
                    turret => ((Building_Aerocraft_Base)turret).Gun_Now,
                    turret => AerocraftReloadUtility.AmmoOf(((Building_Aerocraft_Base)turret).Gun_Now));
            }
        }
    }

    /// <summary>
    /// CE verbs keep their burst warm-up in Building_TurretGunCE; for any other turret they logged
    /// "Verb caster is not a turret and does not have a WarmupStance" on every aimed shot. Aircraft now provide it.
    /// </summary>
    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.BurstWarmupTicksLeft), MethodType.Getter)]
    internal static class Patch_Verb_LaunchProjectileCE_BurstWarmupTicksLeft_Get
    {
        private static bool Prefix(Verb_LaunchProjectileCE __instance, ref int __result)
        {
            if (__instance.caster is Building_Aerocraft_Base turret)
            {
                __result = turret.WarmupTicks;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.BurstWarmupTicksLeft), MethodType.Setter)]
    internal static class Patch_Verb_LaunchProjectileCE_BurstWarmupTicksLeft_Set
    {
        private static bool Prefix(Verb_LaunchProjectileCE __instance, int value)
        {
            if (__instance.caster is Building_Aerocraft_Base turret)
            {
                if (turret.WarmupTicks > 0)
                {
                    turret.WarmupTicks = value;
                }
                return false;
            }
            return true;
        }
    }

    /// <summary>Projectiles fired by a weapon mount do not collide with the aircraft carrying it.</summary>
    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.Launch), new Type[] { typeof(Thing), typeof(UnityEngine.Vector2), typeof(Thing) })]
    internal static class Patch_ProjectileCE_Launch
    {
        private static void Postfix(ProjectileCE __instance, Thing launcher)
        {
            if (__instance.mount == null && launcher is Building_Aerocraft_AsWeapon mount && mount.Building_Aerocraft_AsBaseThing != null)
            {
                __instance.mount = mount.Building_Aerocraft_AsBaseThing;
            }
        }
    }
}
