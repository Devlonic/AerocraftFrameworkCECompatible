using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MYDE_AerocraftFramework
{
    /// <summary>A pawn walks to a landed aircraft and boards it.</summary>
    public class JobDriver_Enter_Building_Aerocraft_AsBaseThing : JobDriver
    {
        private Building_Aerocraft_AsBaseThing Aircraft => job.targetA.Thing as Building_Aerocraft_AsBaseThing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Several pawns may board at once, and boarding must not block reloading or refuelling.
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Aircraft == null || !Aircraft.CanBoard(pawn).Accepted);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil wait = Toils_General.Wait(60);
            wait.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;
            Toil enter = ToilMaker.MakeToil("EnterAerocraft");
            enter.initAction = () =>
            {
                Building_Aerocraft_AsBaseThing aircraft = Aircraft;
                Pawn actor = enter.actor;
                AcceptanceReport canBoard = aircraft.CanBoard(actor);
                if (!canBoard.Accepted)
                {
                    AerocraftUtility.ThrowText(aircraft, canBoard.Reason);
                    return;
                }
                aircraft.DoSomething_CarryPawn(actor);
            };
            enter.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return enter;
        }
    }

    /// <summary>A colonist installs his ranged weapon into the turret; the previous one is kept on board.</summary>
    public class JobDriver_ReplaceCurrentWeapon : JobDriver
    {
        private Building_Aerocraft_Base Turret => job.targetA.Thing as Building_Aerocraft_Base;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.equipment?.Primary == null || Turret == null || !Turret.Is_Static);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil wait = Toils_General.Wait(500);
            wait.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            wait.activeSkill = () => SkillDefOf.Construction;
            yield return wait;
            Toil install = ToilMaker.MakeToil("InstallWeapon");
            install.initAction = () =>
            {
                Pawn actor = install.actor;
                ThingWithComps primary = actor.equipment.Primary;
                if (primary == null)
                {
                    return;
                }
                actor.equipment.Remove(primary);
                Turret.Change_NowWeapon(primary);
            };
            install.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return install;
        }
    }

    /// <summary>A colonist carries mortar shells (bombs) into an aircraft's bomb bay.</summary>
    public class JobDriver_LoadShell : JobDriver
    {
        private const TargetIndex BuildingInd = TargetIndex.A;
        private const TargetIndex ShellInd = TargetIndex.B;

        private Building_Aerocraft_AsBaseThing Building => (Building_Aerocraft_AsBaseThing)job.GetTarget(BuildingInd).Thing;

        private Thing Shells => job.GetTarget(ShellInd).Thing;

        private Comp_CanLoadShell Bay => Building?.TryGetComp<Comp_CanLoadShell>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Building, job, 1, -1, null, errorOnFailed) && pawn.Reserve(Shells, job, 1, Mathf.Max(1, job.count), null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(BuildingInd);
            this.FailOn(() => Bay == null || Bay.SpaceLeft <= 0 || !Building.Is_Static);
            yield return Toils_Goto.GotoThing(ShellInd, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(ShellInd).FailOnSomeonePhysicallyInteracting(ShellInd);
            yield return Toils_Haul.StartCarryThing(ShellInd, putRemainderInQueue: false, subtractNumTakenFromJobCount: false);
            yield return Toils_Goto.GotoThing(BuildingInd, PathEndMode.Touch);
            Toil wait = Toils_General.Wait(100);
            wait.FailOnCannotTouch(BuildingInd, PathEndMode.Touch);
            wait.WithProgressBarToilDelay(BuildingInd);
            yield return wait;
            Toil load = ToilMaker.MakeToil("LoadShells");
            load.initAction = () =>
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null)
                {
                    return;
                }
                int stored = Bay.StoreShells(carried.def, carried.stackCount);
                if (stored >= carried.stackCount)
                {
                    pawn.carryTracker.DestroyCarriedThing();
                }
                else
                {
                    carried.stackCount -= stored;
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                }
                pawn.records.Increment(RecordDefOf.ThingsHauled);
                if (Bay.SpaceLeft > 0 && job.playerForced)
                {
                    Job next = Bay.MakeLoadShellJob(pawn);
                    if (next != null)
                    {
                        next.playerForced = true;
                        pawn.jobs.jobQueue.EnqueueFirst(next);
                    }
                }
            };
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return load;
        }
    }
}
