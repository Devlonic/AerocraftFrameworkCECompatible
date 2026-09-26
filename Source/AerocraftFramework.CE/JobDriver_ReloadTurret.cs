using System.Collections.Generic;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// A colonist brings ammo to a landed aircraft (or one of its weapon mounts) and reloads a gun.
    /// Targets: A = turret, B = ammo stack (absent when CE's ammo system is off), C = the gun (optional hint).
    /// Leftover ammo goes into other guns of the same turret that take it, the rest is put down.
    /// The class name is the one used by the original mod so that saved jobs still load.
    /// </summary>
    public class JobDriver_ReloadTurret : JobDriver
    {
        private const int FallbackDuration = 120;

        private Thing gun;

        private Building_Aerocraft_Base Turret => job.targetA.Thing as Building_Aerocraft_Base;

        private bool UsesAmmoItem => job.targetB.IsValid;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            if (UsesAmmoItem && !pawn.Reserve(job.targetB, job, 10, Mathf.Max(1, job.count), null, errorOnFailed))
            {
                return false;
            }
            return true;
        }

        public override string GetReport()
        {
            Thing reportedGun = gun ?? job.targetC.Thing ?? Turret?.Gun_Now;
            string ammo = UsesAmmoItem ? job.targetB.Thing?.def.label ?? "" : "CE_ReloadingGenericAmmo".Translate().ToString();
            return "AerocraftFramework_ReportReloading".Translate(reportedGun?.LabelShort ?? "", Turret?.LabelShort ?? "", ammo);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (pawn.Faction == Faction.OfPlayer)
            {
                this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            }
            else
            {
                this.FailOnDespawnedOrNull(TargetIndex.A);
            }
            this.FailOn(() => Turret == null || !AerocraftUtility.ServiceAllowed(Turret).Accepted);
            this.FailOnIncapable(PawnCapacityDefOf.Manipulation);
            AddEndCondition(() => pawn.Downed || pawn.Dead || pawn.InMentalState || pawn.IsBurning() ? JobCondition.Incompletable : JobCondition.Ongoing);
            AddFinishAction(_ =>
            {
                if (Turret != null)
                {
                    Turret.isReloading = false;
                }
            });

            if (UsesAmmoItem)
            {
                Toil gotoAmmo = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.B)
                    .FailOnSomeonePhysicallyInteracting(TargetIndex.B)
                    .FailOnBurningImmobile(TargetIndex.B);
                gotoAmmo.AddEndCondition(() => job.targetB.Thing is AmmoThing ammoThing && ammoThing.IsCookingOff ? JobCondition.Incompletable : JobCondition.Ongoing);
                yield return gotoAmmo;
                yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            }

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil wait = ToilMaker.MakeToil("AerocraftReloadWait");
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            wait.defaultDuration = FallbackDuration;
            wait.initAction = () =>
            {
                Building_Aerocraft_Base turret = Turret;
                Thing carried = pawn.carryTracker.CarriedThing;
                if (UsesAmmoItem && carried == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                gun = AerocraftReloadUtility.GunForAmmo(turret, UsesAmmoItem ? carried.def : null, job.targetC.Thing);
                CompAmmoUser comp = AerocraftReloadUtility.AmmoOf(gun);
                if (comp == null)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                turret.isReloading = true;
                pawn.pather.StopDead();
                if (comp.ShouldThrowMote)
                {
                    MoteMaker.ThrowText(turret.DrawPos, turret.Map, "AerocraftFramework_ReloadingMote".Translate(gun.LabelShort), 3f);
                }
                if (UsesAmmoItem && comp.CurrentAmmo != carried.def)
                {
                    CEApi.TryUnload(comp);
                }
                float reloadSpeed = Mathf.Max(0.1f, pawn.GetStatValue(CE_StatDefOf.ReloadSpeed));
                int duration = Mathf.Max(1, Mathf.CeilToInt(comp.Props.reloadTime.SecondsToTicks() / reloadSpeed));
                wait.defaultDuration = duration;
                ticksLeftThisToil = duration;
            };
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            Toil load = ToilMaker.MakeToil("AerocraftReloadLoad");
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            load.initAction = () =>
            {
                Building_Aerocraft_Base turret = Turret;
                if (gun == null)
                {
                    // The gun is not saved with the job: after loading a save mid-reload, find it again.
                    gun = AerocraftReloadUtility.GunForAmmo(turret, UsesAmmoItem ? pawn.carryTracker.CarriedThing?.def : null, job.targetC.Thing);
                }
                CompAmmoUser comp = AerocraftReloadUtility.AmmoOf(gun);
                if (comp == null)
                {
                    return;
                }
                if (UsesAmmoItem)
                {
                    Thing carried = pawn.carryTracker.CarriedThing;
                    if (carried == null)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    CEApi.LoadAmmo(comp, carried);
                }
                else
                {
                    CEApi.LoadAmmo(comp, null);
                }
                turret.isReloading = false;
                MYDE_AerocraftFramework_Setting_Main.DebugLog($"{pawn} reloaded {gun} of {turret}: {comp.CurMagCount}/{comp.MagSize}");
            };
            yield return load;

            yield return Toils_Jump.JumpIf(wait, () =>
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                return carried != null && !carried.Destroyed && carried.stackCount > 0 && AerocraftReloadUtility.GunForAmmo(Turret, carried.def, null) != null;
            });

            Toil putDownRest = ToilMaker.MakeToil("AerocraftReloadPutDown");
            putDownRest.defaultCompleteMode = ToilCompleteMode.Instant;
            putDownRest.initAction = () =>
            {
                if (pawn.carryTracker.CarriedThing != null)
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                }
            };
            yield return putDownRest;
        }
    }
}
