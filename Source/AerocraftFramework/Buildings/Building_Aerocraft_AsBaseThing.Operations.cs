using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Operations of an aircraft body besides flying and shooting: dropping troops from a hovering aircraft,
    /// strafing runs along a line, and carrying the wounded aboard (medevac).
    /// </summary>
    public partial class Building_Aerocraft_AsBaseThing
    {
        // ------------------------------------------------------------------ state

        private const int RopeDownRadius = 3;
        private const int RopeDownIntervalTicks = 40;
        private const float TroopDropRetargetTolerance = 8f;

        private const float StrafeLength = 12f;
        private const float StrafeAimLead = 5f;
        private const int StrafeTicksMax = 3000;

        public const float MedevacRadius = 25f;

        private IntVec3 troopDropCell = IntVec3.Invalid;
        private Vector3 troopDropTarget;
        private int troopDropNextTick;

        private enum StrafeStage
        {
            None,
            LeadIn,
            Approach,
            Run,
            Exit
        }

        private StrafeStage strafeStage;
        private IntVec3 strafeStart = IntVec3.Invalid;
        private IntVec3 strafeEnd = IntVec3.Invalid;
        private IntVec3 strafeLeadIn = IntVec3.Invalid;
        private Vector3 strafeTarget;
        private float strafeLastDistance = float.MaxValue;
        private int strafeTicks;

        private void ExposeOperations()
        {
            ExposeCrossMap();
            Scribe_Values.Look(ref troopDropCell, "AF_TroopDropCell", IntVec3.Invalid);
            Scribe_Values.Look(ref troopDropTarget, "AF_TroopDropTarget");
            Scribe_Values.Look(ref troopDropNextTick, "AF_TroopDropNextTick", 0);
            Scribe_Values.Look(ref strafeStage, "AF_StrafeStage", StrafeStage.None);
            Scribe_Values.Look(ref strafeStart, "AF_StrafeStart", IntVec3.Invalid);
            Scribe_Values.Look(ref strafeEnd, "AF_StrafeEnd", IntVec3.Invalid);
            Scribe_Values.Look(ref strafeLeadIn, "AF_StrafeLeadIn", IntVec3.Invalid);
            Scribe_Values.Look(ref strafeTarget, "AF_StrafeTarget");
            Scribe_Values.Look(ref strafeTicks, "AF_StrafeTicks", 0);
        }

        private void OperationsTick()
        {
            DepartureTick();
            ArrivalTick();
            TroopDropTick();
            StrafeTick();
        }

        /// <summary>Sends the aircraft to a point; false when it ignores the order (gliding to take off or land).</summary>
        private bool SendTo(Vector3 point, out Vector3 sentTo)
        {
            Set_TargetVPos(point);
            sentTo = TargetVPos;
            return !HaveGoToTarget && TargetVPos == point;
        }

        // ------------------------------------------------------------------ troop drop

        public bool TroopDropActive => troopDropCell.IsValid;

        public IntVec3 TroopDropCell => troopDropCell;

        /// <summary>Pawns the aircraft needs on board to fly.</summary>
        public int CrewToKeep => If_NeedPawnToControl ? NeedPawnToControl_Number : 0;

        /// <summary>Able pawns beyond the crew (the crew are the first who boarded): they can be dropped.</summary>
        public List<Pawn> Troops => CrewCapable.Skip(CrewToKeep).ToList();

        /// <summary>Only an aircraft that takes off vertically can hold still in the air.</summary>
        public bool CanHoverInPlace => !If_NeedGlidingWhenTakeOff;

        /// <summary>Whether a pawn can rope down now: a vertical take-off aircraft hovering in flight.</summary>
        public AcceptanceReport CanRopeDown
        {
            get
            {
                if (!CanHoverInPlace)
                {
                    return "AerocraftFramework_TroopDrop_NotVtol".Translate();
                }
                if (!Is_Flying || !If_UpOrDown || If_DropingNow)
                {
                    return "AerocraftFramework_NoFlying".Translate();
                }
                if (!HaveGoToTarget)
                {
                    return "AerocraftFramework_TroopDrop_NotHovering".Translate();
                }
                return true;
            }
        }

        public AcceptanceReport CanDropTroops
        {
            get
            {
                if (!CanHoverInPlace)
                {
                    return "AerocraftFramework_TroopDrop_NotVtol".Translate();
                }
                if (!Is_Flying || !If_UpOrDown || If_DropingNow)
                {
                    return "AerocraftFramework_NoFlying".Translate();
                }
                if (Troops.Count == 0)
                {
                    return "AerocraftFramework_TroopDrop_NoTroops".Translate(CrewToKeep.ToString());
                }
                return true;
            }
        }

        /// <summary>The aircraft flies to the cell, hovers and lets every pawn beyond its crew rope down, drafted.</summary>
        public AcceptanceReport StartTroopDrop(IntVec3 cell)
        {
            AcceptanceReport canDrop = CanDropTroops;
            if (!canDrop.Accepted)
            {
                return canDrop;
            }
            ClearStrafeRun();
            if (!SendTo(cell.ToVector3Shifted(), out troopDropTarget))
            {
                return "AerocraftFramework_BombRun_Busy".Translate();
            }
            troopDropCell = cell;
            troopDropNextTick = 0;
            return true;
        }

        public void CancelTroopDrop()
        {
            troopDropCell = IntVec3.Invalid;
        }

        private void TroopDropTick()
        {
            if (!TroopDropActive)
            {
                return;
            }
            if (!Is_Flying || !If_UpOrDown || FollowTargetThing != null || If_GoBackNow)
            {
                CancelTroopDrop();
                return;
            }
            if (TargetVPos != troopDropTarget)
            {
                // On arrival an aircraft moves off another one standing there: still close enough to drop.
                if ((TargetVPos - troopDropCell.ToVector3Shifted()).MagnitudeHorizontal() > TroopDropRetargetTolerance)
                {
                    CancelTroopDrop();
                    return;
                }
                troopDropTarget = TargetVPos;
            }
            if (!HaveGoToTarget || Find.TickManager.TicksGame < troopDropNextTick)
            {
                return;
            }
            Pawn trooper = Troops.FirstOrDefault();
            if (trooper == null)
            {
                CancelTroopDrop();
                return;
            }
            if (!ReleasePawn(trooper, drafted: true))
            {
                Messages.Message("AerocraftFramework_TroopDrop_NoRoom".Translate(LabelShort), this, MessageTypeDefOf.RejectInput, historical: false);
                CancelTroopDrop();
                return;
            }
            troopDropNextTick = Find.TickManager.TicksGame + RopeDownIntervalTicks;
        }

        // ------------------------------------------------------------------ strafing run

        public bool StrafeRunActive => strafeStage != StrafeStage.None;

        public IntVec3 StrafeStart => strafeStart;

        public IntVec3 StrafeEnd => strafeEnd;

        public AcceptanceReport CanStrafe
        {
            get
            {
                if (!Is_Flying || !If_UpOrDown || If_DropingNow)
                {
                    return "AerocraftFramework_NoFlying".Translate();
                }
                if (!AllTurrets.Any(AerocraftWeaponOrders.CanFire))
                {
                    return "AerocraftFramework_Strafe_NoWeapon".Translate();
                }
                return true;
            }
        }

        /// <summary>
        /// A strafing run along the line from <paramref name="start"/> to <paramref name="end"/>, or along a short line
        /// through <paramref name="start"/> on the approach: every weapon that reaches fires at a point walking along it.
        /// </summary>
        public AcceptanceReport StartStrafeRun(IntVec3 start, IntVec3? end)
        {
            AcceptanceReport canStrafe = CanStrafe;
            if (!canStrafe.Accepted)
            {
                return canStrafe;
            }
            PlanStrafe(DrawPos, start, end, Map, out IntVec3 a, out IntVec3 b, out IntVec3 leadIn);
            CancelTroopDrop();
            if (!SendTo((leadIn.IsValid ? leadIn : a).ToVector3Shifted(), out strafeTarget))
            {
                return "AerocraftFramework_BombRun_Busy".Translate();
            }
            strafeStart = a;
            strafeEnd = b;
            strafeLeadIn = leadIn;
            strafeStage = leadIn.IsValid ? StrafeStage.LeadIn : StrafeStage.Approach;
            strafeLastDistance = float.MaxValue;
            strafeTicks = 0;
            // An attack order: weapons told to hold fire open fire for the run.
            AerocraftWeaponOrders.SetHoldFire(AllTurrets.Where(t => t.HoldFire).ToList(), false);
            return true;
        }

        /// <summary>The line of a strafing run and, when the aircraft meets it at an angle, where it lines up first.</summary>
        public static void PlanStrafe(Vector3 from, IntVec3 start, IntVec3? end, Map map, out IntVec3 lineStart, out IntVec3 lineEnd, out IntVec3 leadIn)
        {
            if (end.HasValue && end.Value != start)
            {
                lineStart = start;
                lineEnd = end.Value;
            }
            else
            {
                AerocraftRunPlanner.LineThrough(from, start, StrafeLength, map, out lineStart, out lineEnd);
            }
            leadIn = AerocraftRunPlanner.LeadIn(from, lineStart, lineEnd, map);
        }

        public void ClearStrafeRun()
        {
            if (strafeStage == StrafeStage.None)
            {
                return;
            }
            strafeStage = StrafeStage.None;
            StopStrafeFire();
        }

        /// <summary>Weapons forget the points of the run (a target the player gave them, a thing, is kept).</summary>
        private void StopStrafeFire()
        {
            foreach (Building_Aerocraft_Base turret in AllTurrets.ToList())
            {
                if (turret.ForcedTarget.IsValid && !turret.ForcedTarget.HasThing)
                {
                    turret.ResetForcedTarget();
                }
            }
        }

        private void StrafeTick()
        {
            if (strafeStage == StrafeStage.None)
            {
                return;
            }
            // The map edge turned the aircraft round (it sends it elsewhere and raises this flag).
            if (If_CheckInMapBoundaryPos && TargetVPos != strafeTarget && strafeStage != StrafeStage.Exit)
            {
                ClearStrafeRun();
                Messages.Message("AerocraftFramework_Strafe_Aborted".Translate(LabelShort, "AerocraftFramework_Run_MapEdge".Translate()), this, MessageTypeDefOf.CautionInput, historical: false);
                return;
            }
            if (TargetVPos != strafeTarget || FollowTargetThing != null || If_GoBackNow || !Is_Flying || !If_UpOrDown)
            {
                ClearStrafeRun();
                return;
            }
            if (++strafeTicks > StrafeTicksMax)
            {
                ClearStrafeRun();
                Messages.Message("AerocraftFramework_Strafe_Aborted".Translate(LabelShort, "AerocraftFramework_BombRun_CannotReach".Translate()), this, MessageTypeDefOf.CautionInput, historical: false);
                return;
            }
            switch (strafeStage)
            {
                case StrafeStage.LeadIn:
                    // Weapons that reach warm up on the start of the line already (a CE machine gun needs two seconds).
                    AimStrafe(strafeStart);
                    if (AerocraftRunPlanner.IsOver(this, strafeLeadIn, ref strafeLastDistance))
                    {
                        strafeStage = StrafeStage.Approach;
                        if (!SendTo(strafeStart.ToVector3Shifted(), out strafeTarget))
                        {
                            ClearStrafeRun();
                        }
                    }
                    break;
                case StrafeStage.Approach:
                    AimStrafe(strafeStart);
                    if (AerocraftRunPlanner.IsOver(this, strafeStart, ref strafeLastDistance))
                    {
                        strafeStage = StrafeStage.Run;
                        if (!SendTo(strafeEnd.ToVector3Shifted(), out strafeTarget))
                        {
                            ClearStrafeRun();
                        }
                    }
                    break;
                case StrafeStage.Run:
                    AimStrafe(StrafeAimPoint());
                    if (AerocraftRunPlanner.IsOver(this, strafeEnd, ref strafeLastDistance))
                    {
                        StopStrafeFire();
                        strafeStage = StrafeStage.Exit;
                        if (!SendTo(AerocraftRunPlanner.ExitPoint(this).ToVector3Shifted(), out strafeTarget))
                        {
                            strafeStage = StrafeStage.None;
                        }
                    }
                    break;
                case StrafeStage.Exit:
                    if (HaveGoToTarget)
                    {
                        strafeStage = StrafeStage.None;
                    }
                    break;
            }
        }

        /// <summary>A point on the line a few cells ahead of the aircraft, so the fire walks along it.</summary>
        private IntVec3 StrafeAimPoint()
        {
            Vector3 a = strafeStart.ToVector3Shifted();
            Vector3 line = strafeEnd.ToVector3Shifted() - a;
            line.y = 0f;
            float length = line.magnitude;
            if (length < 0.5f)
            {
                return strafeStart;
            }
            Vector3 fromStart = DrawPos - a;
            fromStart.y = 0f;
            float t = Vector3.Dot(fromStart, line) / (length * length) + StrafeAimLead / length;
            return (a + line * Mathf.Clamp01(t)).ToIntVec3();
        }

        private void AimStrafe(IntVec3 cell)
        {
            if (!this.IsHashIntervalTick(10))
            {
                return;
            }
            foreach (Building_Aerocraft_Base turret in AllTurrets)
            {
                if (AerocraftWeaponOrders.CanAttack(turret, cell))
                {
                    turret.RetargetForced(cell);
                }
            }
        }

        // ------------------------------------------------------------------ medevac

        /// <summary>Downed pawns a colonist may carry aboard: colonists, slaves, prisoners and guests, not enemies.</summary>
        public static bool CanBeEvacuated(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || !pawn.Downed || pawn.Dead || !pawn.RaceProps.Humanlike)
            {
                return false;
            }
            Faction player = Faction.OfPlayer;
            return pawn.Faction == player || pawn.IsPrisonerOfColony || pawn.HostFaction == player || (pawn.Faction != null && !pawn.Faction.HostileTo(player));
        }

        public int FreeSeats => Mathf.Max(0, CarryPawnNumMax - ListPawn.Count);

        /// <summary>Wounded around the aircraft that nobody is carrying yet, closest first.</summary>
        public List<Pawn> WoundedAround()
        {
            return Map.mapPawns.AllPawnsSpawned.ToList()
                .Where(p => CanBeEvacuated(p) && p.Position.InHorDistOf(Position, MedevacRadius) && !IsBeingCarriedAboard(p))
                .OrderBy(p => p.Position.DistanceToSquared(Position))
                .ToList();
        }

        /// <summary>A copy: the game refills the same list at every call, so nested calls would change it under a loop.</summary>
        private List<Pawn> Colonists => Map.mapPawns.FreeColonistsSpawned.ToList();

        private bool IsBeingCarriedAboard(Pawn wounded)
        {
            return Colonists.Any(p => p.CurJobDef == MYDE_JobDefOf.MYDE_AF_CarryToAerocraft && p.CurJob.targetB.Thing == wounded);
        }

        private int SeatsPromised => Colonists.Count(p => p.CurJobDef == MYDE_JobDefOf.MYDE_AF_CarryToAerocraft && p.CurJob.targetA.Thing == this);

        public AcceptanceReport CanCarryAboard(Pawn carrier, Pawn wounded)
        {
            if (!Is_Static)
            {
                return "AerocraftFramework_AerocraftIsNotStatic".Translate();
            }
            if (FreeSeats - SeatsPromised <= 0)
            {
                return "AerocraftFramework_MaxInnerPawn".Translate();
            }
            if (carrier.Downed || !carrier.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                return "AerocraftFramework_Medevac_CannotCarry".Translate(carrier.LabelShort);
            }
            if (!carrier.CanReserveAndReach(wounded, PathEndMode.ClosestTouch, Danger.Deadly) || !carrier.CanReach(this, PathEndMode.Touch, Danger.Deadly))
            {
                return "NoPath".Translate();
            }
            return true;
        }

        public Job MakeCarryAboardJob(Pawn wounded)
        {
            Job job = JobMaker.MakeJob(MYDE_JobDefOf.MYDE_AF_CarryToAerocraft, this, wounded);
            job.count = 1;
            return job;
        }

        /// <summary>Sends the closest able colonists to carry the wounded around the aircraft aboard, as many as there are seats.</summary>
        public int OrderMedevac()
        {
            int ordered = 0;
            List<Pawn> busy = new List<Pawn>();
            foreach (Pawn wounded in WoundedAround())
            {
                if (FreeSeats - SeatsPromised <= 0)
                {
                    break;
                }
                Pawn carrier = Colonists
                    .Where(p => !busy.Contains(p) && !p.InMentalState && CanCarryAboard(p, wounded).Accepted)
                    .OrderBy(p => p.Drafted ? 1 : 0)
                    .ThenBy(p => p.Position.DistanceToSquared(wounded.Position))
                    .FirstOrDefault();
                if (carrier == null)
                {
                    continue;
                }
                if (carrier.jobs.TryTakeOrderedJob(MakeCarryAboardJob(wounded), JobTag.Misc))
                {
                    busy.Add(carrier);
                    ordered++;
                }
            }
            if (ordered == 0)
            {
                Messages.Message("AerocraftFramework_Medevac_Nobody".Translate(), this, MessageTypeDefOf.RejectInput, historical: false);
            }
            return ordered;
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
            if (Faction != Faction.OfPlayer || !selPawn.IsColonistPlayerControlled || GetComp<Comp_CarryPawn>() == null)
            {
                yield break;
            }
            foreach (Pawn wounded in WoundedAround().Take(6))
            {
                string label = "AerocraftFramework_Medevac_CarryAboard".Translate(wounded.LabelShort, LabelShort);
                AcceptanceReport canCarry = CanCarryAboard(selPawn, wounded);
                if (!canCarry.Accepted)
                {
                    yield return new FloatMenuOption(label + " (" + canCarry.Reason + ")", null);
                    continue;
                }
                Pawn chosen = wounded;
                yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () => selPawn.jobs.TryTakeOrderedJob(MakeCarryAboardJob(chosen), JobTag.Misc)), selPawn, chosen);
            }
        }

        // ------------------------------------------------------------------ gizmos

        private IEnumerable<Gizmo> GetOperationGizmos()
        {
            if (Faction != Faction.OfPlayer && !MYDE_AerocraftFramework_Setting.If_CanControlNonPlayer)
            {
                yield break;
            }
            if (IsDeparting)
            {
                yield return new Command_Action
                {
                    defaultLabel = "AerocraftFramework_Departure_Cancel_Label".Translate(),
                    defaultDesc = "AerocraftFramework_Departure_Cancel_Desc".Translate(),
                    icon = AerocraftWeaponOrders.HaltIcon,
                    action = CancelDeparture
                };
            }
            bool carriesPawns = GetComp<Comp_CarryPawn>() != null;
            if (Is_Flying && AllTurrets.Any(t => t.Gun_Now != null))
            {
                Command_AerocraftRun strafe = new Command_AerocraftRun(this, Command_AerocraftRun.RunKind.Strafe);
                AcceptanceReport canStrafe = CanStrafe;
                if (!canStrafe.Accepted)
                {
                    strafe.Disable(canStrafe.Reason);
                }
                yield return strafe;
            }
            if (StrafeRunActive)
            {
                yield return new Command_Action
                {
                    defaultLabel = "AerocraftFramework_Strafe_Cancel_Label".Translate(),
                    defaultDesc = "AerocraftFramework_Strafe_Cancel_Desc".Translate(),
                    icon = AerocraftWeaponOrders.HaltIcon,
                    action = ClearStrafeRun
                };
            }
            if (carriesPawns && CanHoverInPlace && Is_Flying)
            {
                Command_AerocraftRun drop = new Command_AerocraftRun(this, Command_AerocraftRun.RunKind.TroopDrop);
                AcceptanceReport canDrop = CanDropTroops;
                if (!canDrop.Accepted)
                {
                    drop.Disable(canDrop.Reason);
                }
                yield return drop;
            }
            if (carriesPawns && Is_Static && Faction == Faction.OfPlayer)
            {
                Command_Action medevac = new Command_Action
                {
                    defaultLabel = "AerocraftFramework_Medevac_Label".Translate(),
                    defaultDesc = "AerocraftFramework_Medevac_Desc".Translate(MedevacRadius.ToString("0")),
                    icon = Command_AerocraftRun.MedevacIcon,
                    action = () => OrderMedevac(),
                    onHover = () => GenDraw.DrawRadiusRing(Position, MedevacRadius)
                };
                if (FreeSeats - SeatsPromised <= 0)
                {
                    medevac.Disable("AerocraftFramework_MaxInnerPawn".Translate());
                }
                else if (WoundedAround().Count == 0)
                {
                    medevac.Disable("AerocraftFramework_Medevac_NoWounded".Translate(MedevacRadius.ToString("0")));
                }
                yield return medevac;
            }
        }
    }
}
