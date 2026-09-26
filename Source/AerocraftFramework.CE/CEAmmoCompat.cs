using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>Combat Extended behaviour of aircraft guns: magazines, ammo types, reloading, CE projectiles and explosions.</summary>
    public class CEAmmoCompat : AerocraftAmmoCompat
    {
        public override string Name => "Combat Extended";

        public override bool UsesAmmo => true;

        private static CompAmmoUser Ammo(Thing gun) => AerocraftReloadUtility.AmmoOf(gun);

        // ------------------------------------------------------------------ gun lifecycle

        public override void Notify_GunAttached(Building_Aerocraft_Base turret, Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp != null)
            {
                comp.turret = turret;
            }
        }

        public override void Notify_GunDetached(Building_Aerocraft_Base turret, Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp != null && comp.turret == turret)
            {
                comp.turret = null;
            }
        }

        public override void Notify_FirstSpawn(Building_Aerocraft_Base turret)
        {
            // Like CE turrets: player aircraft built at home start empty, everything else starts loaded.
            if (!turret.Map.IsPlayerHome || turret.Faction != Faction.OfPlayer)
            {
                FillMagazines(turret);
            }
        }

        public override void FillMagazines(Building_Aerocraft_Base turret)
        {
            foreach (Thing gun in turret.AllGuns)
            {
                CompAmmoUser comp = Ammo(gun);
                if (comp != null && comp.HasMagazine)
                {
                    CEApi.ResetAmmoCount(comp);
                }
            }
        }

        // ------------------------------------------------------------------ magazine

        public override bool TryGetMagazine(Thing gun, out int current, out int capacity)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp == null || !comp.HasMagazine)
            {
                current = 0;
                capacity = 0;
                return false;
            }
            current = comp.CurMagCount;
            capacity = comp.MagSize;
            return true;
        }

        public override bool CanFireNow(Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            return comp == null || comp.CanBeFiredNow;
        }

        public override bool NeedsReload(Thing gun) => AerocraftReloadUtility.GunNeedsReload(gun);

        public override string CurrentAmmoLabel(Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp == null || !comp.UseAmmo)
            {
                return null;
            }
            string current = comp.CurrentAmmo?.ammoClass?.LabelCap ?? comp.CurrentAmmo?.LabelCap ?? "";
            if (comp.SelectedAmmo != null && comp.SelectedAmmo != comp.CurrentAmmo)
            {
                string selected = comp.SelectedAmmo.ammoClass?.LabelCap ?? comp.SelectedAmmo.LabelCap;
                return current.NullOrEmpty() ? selected : current + " → " + selected;
            }
            return current;
        }

        public override ThingDef SelectedAmmoDef(Thing gun) => Ammo(gun)?.SelectedAmmo;

        public override ThingDef CurrentAmmoDef(Thing gun) => Ammo(gun)?.CurrentAmmo;

        public override IEnumerable<ThingDef> AmmoTypes(Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp == null || !comp.UseAmmo || comp.Props.ammoSet?.ammoTypes == null)
            {
                yield break;
            }
            foreach (AmmoLink link in comp.Props.ammoSet.ammoTypes)
            {
                if (link.ammo != null)
                {
                    yield return link.ammo;
                }
            }
        }

        public override void SetSelectedAmmo(Thing gun, ThingDef ammo)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp != null && ammo is AmmoDef ammoDef)
            {
                comp.SelectedAmmo = ammoDef;
            }
        }

        public override bool IsLinkedTo(Thing gun, Building_Aerocraft_Base turret)
        {
            CompAmmoUser comp = Ammo(gun);
            return comp == null || comp.turret == turret;
        }

        public override ThingDef CurrentProjectile(Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            return comp?.CurrentAmmo != null ? comp.CurAmmoProjectile : base.CurrentProjectile(gun);
        }

        // ------------------------------------------------------------------ firing

        public override void PrepareToFire(Building_Aerocraft_Base turret, Verb verb)
        {
            // Aircraft fire from above: low walls and sandbags do not block their line of fire (original behaviour).
            if (verb is Verb_LaunchProjectileCE ceVerb && turret.Is_Flying)
            {
                ceVerb.VerbPropsCE.ignorePartialLoSBlocker = true;
            }
        }

        public override bool IsOneUseVerb(Verb verb) => verb is Verb_ShootCEOneUse || verb is Verb_ShootOneUse;

        // ------------------------------------------------------------------ UI and orders

        public override IEnumerable<Gizmo> GetGunGizmos(Building_Aerocraft_Base turret, Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp != null)
            {
                foreach (Gizmo gizmo in comp.CompGetGizmosExtra())
                {
                    // The aircraft has its own ammo gizmo covering all of its guns.
                    if (gizmo is GizmoAmmoStatus && turret is Building_Aerocraft_AsBaseThing)
                    {
                        continue;
                    }
                    yield return gizmo;
                }
                if (comp.HasMagazine && turret.Faction == Faction.OfPlayer)
                {
                    Command_Action reload = new Command_Action
                    {
                        defaultLabel = "AerocraftFramework_Command_ReloadNow".Translate(),
                        defaultDesc = "AerocraftFramework_Command_ReloadNow_Desc".Translate(gun.LabelShort),
                        icon = MYDE_TexButton.Reload,
                        action = () => TryOrderReload(turret, gun)
                    };
                    AcceptanceReport canReload = CanReloadNow(turret, gun);
                    if (!canReload.Accepted)
                    {
                        reload.Disable(canReload.Reason.NullOrEmpty() ? "CE_TurretFull".Translate().ToString() : canReload.Reason);
                    }
                    yield return reload;
                }
            }
            CompFireModes fireModes = gun.TryGetComp<CompFireModes>();
            if (fireModes != null && turret.Faction == Faction.OfPlayer)
            {
                foreach (Command command in fireModes.GenerateGizmos())
                {
                    yield return command;
                }
            }
        }

        public override IEnumerable<FloatMenuOption> GetReloadFloatMenuOptions(Building_Aerocraft_Base turret, Pawn pawn)
        {
            if (pawn == null || !pawn.IsColonistPlayerControlled || !pawn.RaceProps.Humanlike)
            {
                yield break;
            }
            foreach (Thing gun in turret.AllGuns.ToList())
            {
                if (!AerocraftReloadUtility.GunNeedsReload(gun))
                {
                    continue;
                }
                CompAmmoUser comp = Ammo(gun);
                string label = "AerocraftFramework_FloatMenu_Reload".Translate(gun.LabelShort, AerocraftReloadUtility.AmmoLabel(comp));
                AcceptanceReport report = AerocraftReloadUtility.CanReload(pawn, turret, gun, forced: true, out Thing ammo);
                if (!report.Accepted)
                {
                    yield return new FloatMenuOption(label + (report.Reason.NullOrEmpty() ? "" : " (" + report.Reason + ")"), null);
                    continue;
                }
                Thing chosenGun = gun;
                Thing chosenAmmo = ammo;
                yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () =>
                {
                    Verse.AI.Job job = AerocraftReloadUtility.MakeReloadJob(turret, chosenGun, chosenAmmo);
                    job.playerForced = true;
                    pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);
                }), pawn, turret);
            }
        }

        public override bool CanChooseAmmo(Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            return comp != null && comp.UseAmmo && comp.Props.ammoSet?.ammoTypes != null && comp.Props.ammoSet.ammoTypes.Count > 1;
        }

        public override void OpenAmmoMenu(Building_Aerocraft_Base turret, Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp == null || !comp.UseAmmo || comp.Props.ammoSet?.ammoTypes == null)
            {
                return;
            }
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (AmmoLink link in comp.Props.ammoSet.ammoTypes)
            {
                AmmoDef ammo = link.ammo;
                if (ammo == null || ammo.menuHidden)
                {
                    continue;
                }
                int available = turret.Map?.listerThings.ThingsOfDef(ammo).Sum(t => t.stackCount) ?? 0;
                string label = ammo.LabelCap + " (" + available + ")" + (ammo == comp.SelectedAmmo ? " ✔" : "");
                options.Add(new FloatMenuOption(label, () => SetSelectedAmmo(gun, ammo), ammo));
            }
            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        public override AcceptanceReport CanReloadNow(Building_Aerocraft_Base turret, Thing gun)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp == null || !comp.HasMagazine)
            {
                return false;
            }
            if (comp.FullMagazine)
            {
                return "CE_TurretFull".Translate();
            }
            AcceptanceReport serviceable = AerocraftReloadUtility.TurretServiceable(turret, Faction.OfPlayer);
            if (!serviceable.Accepted)
            {
                return serviceable;
            }
            if (!AerocraftReloadUtility.AmmoExistsOnMap(turret.Map, comp))
            {
                return "CE_NoAmmoAvailable".Translate() + ": " + AerocraftReloadUtility.AmmoLabel(comp);
            }
            return true;
        }

        public override Pawn TryOrderReload(Building_Aerocraft_Base turret, Thing gun, Pawn pawn = null, bool showMessages = true)
        {
            return AerocraftReloadUtility.TryOrderReload(turret, gun, pawn, showMessages);
        }

        // ------------------------------------------------------------------ debug

        public override void DebugSetMagazine(Thing gun, int count)
        {
            CompAmmoUser comp = Ammo(gun);
            if (comp == null)
            {
                return;
            }
            if (comp.UseAmmo && comp.CurrentAmmo == null)
            {
                comp.CurrentAmmo = comp.SelectedAmmo;
            }
            comp.CurMagCount = Mathf.Clamp(count, 0, comp.MagSize);
        }

        // ------------------------------------------------------------------ projectiles, shells, explosions

        public override ThingDef ShellProjectile(ThingDef shellDef)
        {
            if (shellDef is AmmoDef ammo)
            {
                ThingDef fallback = null;
                List<AmmoSetDef> sets = ammo.AmmoSetDefs;
                if (sets != null)
                {
                    foreach (AmmoSetDef set in sets)
                    {
                        AmmoLink link = set.ammoTypes?.FirstOrDefault(l => l.ammo == ammo);
                        if (link?.projectile == null)
                        {
                            continue;
                        }
                        if (set.isMortarAmmoSet)
                        {
                            return link.projectile;
                        }
                        fallback = fallback ?? link.projectile;
                    }
                }
                return fallback ?? ammo.detonateProjectile ?? base.ShellProjectile(shellDef);
            }
            return base.ShellProjectile(shellDef);
        }

        public override bool IsLoadableShell(ThingDef def)
        {
            if (def is AmmoDef ammo)
            {
                bool mortar = ammo.isMortarAmmo || (ammo.AmmoSetDefs?.Any(s => s.isMortarAmmoSet) ?? false);
                return mortar && ShellProjectile(def) != null;
            }
            return base.IsLoadableShell(def);
        }

        public override bool LaunchProjectile(Thing launcher, ThingDef projectileDef, Vector3 origin, float height, LocalTargetInfo target, float speed = -1f)
        {
            if (projectileDef == null || !typeof(ProjectileCE).IsAssignableFrom(projectileDef.thingClass) || !(projectileDef.projectile is ProjectilePropertiesCE props))
            {
                return base.LaunchProjectile(launcher, projectileDef, origin, height, target, speed);
            }
            Map map = launcher?.Map;
            if (map == null)
            {
                return false;
            }
            ProjectileCE projectile = (ProjectileCE)ThingMaker.MakeThing(projectileDef);
            Vector3 source = new Vector3(origin.x, height, origin.z);
            Vector3 destination = target.Cell.ToVector3Shifted();
            destination.y = 0f;
            float shotSpeed = speed > 0f ? Mathf.Max(speed, props.speed) : props.speed;
            BaseTrajectoryWorker trajectory = props.TrajectoryWorker;
            float shotAngle = trajectory.ShotAngle(props, source, destination, shotSpeed);
            float shotRotation = trajectory.ShotRotation(props, source, destination);
            GenSpawn.Spawn(projectile, origin.ToIntVec3().ClampInsideMap(map), map);
            projectile.canTargetSelf = false;
            projectile.intendedTarget = target;
            projectile.mount = AerocraftUtility.AircraftOf(launcher as Building_Aerocraft_Base);
            projectile.Launch(launcher, new Vector2(source.x, source.z), shotAngle, shotRotation, height, shotSpeed, null);
            return true;
        }

        public override void DoExplosion(IntVec3 center, Map map, CompProperties_DoExplosion_BySomeWays props, Thing instigator)
        {
            GenExplosionCE.DoExplosion(center, map, props.explosiveRadius, props.explosiveDamageType ?? DamageDefOf.Bomb, instigator, props.damageAmountBase, props.armorPenetrationBase, props.explosionSound, instigator?.def, null, null, props.postExplosionSpawnThingDef, props.postExplosionSpawnChance, props.postExplosionSpawnThingCount, props.postExplosionGasType, props.applyDamageToExplosionCellsNeighbors, props.preExplosionSpawnThingDef, props.preExplosionSpawnChance, props.preExplosionSpawnThingCount, props.chanceToStartFire, props.damageFalloff, null, null, null, props.doVisualEffects, props.propagationSpeed);
        }
    }
}
