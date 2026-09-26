using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Everything that depends on the combat system. The core assembly never references Combat Extended:
    /// this default implementation is the vanilla behaviour, and the optional AerocraftFramework.CE assembly
    /// (loaded only when CE is active) replaces it through <see cref="AerocraftCompat.Ammo"/>.
    /// </summary>
    public class AerocraftAmmoCompat
    {
        public virtual string Name => "vanilla";

        /// <summary>True when guns may run out of ammo and need reloading.</summary>
        public virtual bool UsesAmmo => false;

        // ------------------------------------------------------------------ gun lifecycle

        /// <summary>Called whenever a gun becomes part of a turret (current or stored weapon), including after loading.</summary>
        public virtual void Notify_GunAttached(Building_Aerocraft_Base turret, Thing gun)
        {
        }

        /// <summary>Called when a gun leaves a turret (dropped, returned to a pilot, destroyed).</summary>
        public virtual void Notify_GunDetached(Building_Aerocraft_Base turret, Thing gun)
        {
        }

        /// <summary>Called the first time a turret spawns.</summary>
        public virtual void Notify_FirstSpawn(Building_Aerocraft_Base turret)
        {
        }

        /// <summary>Fills every magazine (drones launched from packs and carriers come armed).</summary>
        public virtual void FillMagazines(Building_Aerocraft_Base turret)
        {
        }

        // ------------------------------------------------------------------ magazine

        public virtual bool TryGetMagazine(Thing gun, out int current, out int capacity)
        {
            current = 0;
            capacity = 0;
            return false;
        }

        public virtual bool CanFireNow(Thing gun) => true;

        public virtual bool NeedsReload(Thing gun) => false;

        public virtual string CurrentAmmoLabel(Thing gun) => null;

        public virtual ThingDef SelectedAmmoDef(Thing gun) => null;

        public virtual ThingDef CurrentAmmoDef(Thing gun) => null;

        /// <summary>Ammo types the gun accepts (empty without an ammo system).</summary>
        public virtual IEnumerable<ThingDef> AmmoTypes(Thing gun)
        {
            yield break;
        }

        public virtual void SetSelectedAmmo(Thing gun, ThingDef ammo)
        {
        }

        /// <summary>Whether the gun's ammo user knows it belongs to this turret (CE needs it to fire and reload).</summary>
        public virtual bool IsLinkedTo(Thing gun, Building_Aerocraft_Base turret) => true;

        public virtual bool IsReloading(Building_Aerocraft_Base turret) => turret.isReloading;

        public virtual ThingDef CurrentProjectile(Thing gun)
        {
            return gun?.TryGetComp<CompEquippable>()?.PrimaryVerb?.verbProps?.defaultProjectile;
        }

        // ------------------------------------------------------------------ firing

        public virtual void PrepareToFire(Building_Aerocraft_Base turret, Verb verb)
        {
        }

        public virtual bool IsOneUseVerb(Verb verb) => verb is Verb_ShootOneUse;

        public virtual float BurstCooldownSeconds(Building_Aerocraft_Base turret, Thing gun)
        {
            return gun.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
        }

        // ------------------------------------------------------------------ UI and orders

        /// <summary>Extra gizmos for the current gun (ammo selection, fire modes...).</summary>
        public virtual IEnumerable<Gizmo> GetGunGizmos(Building_Aerocraft_Base turret, Thing gun)
        {
            yield break;
        }

        /// <summary>Right click options offered to a pawn on a turret (reload orders).</summary>
        public virtual IEnumerable<FloatMenuOption> GetReloadFloatMenuOptions(Building_Aerocraft_Base turret, Pawn pawn)
        {
            yield break;
        }

        public virtual bool CanChooseAmmo(Thing gun) => false;

        public virtual void OpenAmmoMenu(Building_Aerocraft_Base turret, Thing gun)
        {
        }

        /// <summary>Why the gun cannot be reloaded right now, or Accepted.</summary>
        public virtual AcceptanceReport CanReloadNow(Building_Aerocraft_Base turret, Thing gun) => false;

        /// <summary>Orders a colonist (the given one, or the best available) to reload the gun. Returns the pawn that got the job.</summary>
        public virtual Pawn TryOrderReload(Building_Aerocraft_Base turret, Thing gun, Pawn pawn = null, bool showMessages = true) => null;

        // ------------------------------------------------------------------ debug and tests

        public virtual void DebugSetMagazine(Thing gun, int count)
        {
        }

        // ------------------------------------------------------------------ projectiles, shells, explosions

        /// <summary>The projectile a loaded shell (bomb) turns into.</summary>
        public virtual ThingDef ShellProjectile(ThingDef shellDef) => shellDef?.projectileWhenLoaded;

        public virtual bool IsLoadableShell(ThingDef def) => def != null && def.IsShell && ShellProjectile(def) != null;

        /// <summary>
        /// Launches a projectile from the air. <paramref name="height"/> is the launch altitude above the ground;
        /// <paramref name="speed"/> &lt;= 0 uses the projectile's own speed.
        /// </summary>
        public virtual bool LaunchProjectile(Thing launcher, ThingDef projectileDef, Vector3 origin, float height, LocalTargetInfo target, float speed = -1f)
        {
            if (launcher?.Map == null || projectileDef == null)
            {
                return false;
            }
            Map map = launcher.Map;
            IntVec3 cell = origin.ToIntVec3().ClampInsideMap(map);
            if (!(ThingMaker.MakeThing(projectileDef) is Projectile projectile))
            {
                Log.ErrorOnce($"[Aerocraft Framework] {projectileDef.defName} is not a vanilla projectile and cannot be launched without Combat Extended.", projectileDef.shortHash ^ 0x4AF1);
                return false;
            }
            GenSpawn.Spawn(projectile, cell, map);
            projectile.Launch(launcher, origin, target, target, ProjectileHitFlags.All, preventFriendlyFire: false, launcher);
            return true;
        }

        public virtual void DoExplosion(IntVec3 center, Map map, CompProperties_DoExplosion_BySomeWays props, Thing instigator)
        {
            GenExplosion.DoExplosion(center, map, props.explosiveRadius, props.explosiveDamageType ?? DamageDefOf.Bomb, instigator, props.damageAmountBase, props.armorPenetrationBase, props.explosionSound, instigator?.def, null, null, props.postExplosionSpawnThingDef, props.postExplosionSpawnChance, props.postExplosionSpawnThingCount, props.postExplosionGasType, props.applyDamageToExplosionCellsNeighbors, props.preExplosionSpawnThingDef, props.preExplosionSpawnChance, props.preExplosionSpawnThingCount, props.chanceToStartFire, props.damageFalloff, null, null, null, props.doVisualEffects, props.propagationSpeed);
        }
    }

    public static class AerocraftCompat
    {
        private static AerocraftAmmoCompat ammo = new AerocraftAmmoCompat();

        public static AerocraftAmmoCompat Ammo
        {
            get => ammo;
            set
            {
                ammo = value ?? new AerocraftAmmoCompat();
                Log.Message("[Aerocraft Framework] combat system: " + ammo.Name);
            }
        }
    }
}
