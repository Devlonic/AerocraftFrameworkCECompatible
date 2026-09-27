using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Bomb bay: colonists load mortar shells, the aircraft throws them at a target or drops them below itself.
    /// With Combat Extended the shells are CE mortar ammo and the bombs are CE projectiles.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Comp_CanLoadShell : ThingComp
    {
        /// <summary>Launch altitude of bombs (CE uses it for the trajectory).</summary>
        public const float BombAltitude = 3f;

        public Thing CurrentShell;
        public List<Thing> ListShell = new List<Thing>();
        public string LaunchShell_Label;
        public Texture2D LaunchShell_Icon;
        public string DropShell_Label;
        public Texture2D DropShell_Icon;
        public int LaunchShell_ReloadTick;

        /// <summary>Bombs dropped per bombing run; 0 drops every bomb on board.</summary>
        public int BombsPerRun = 3;

        // Bombing run: the cells still to bomb, in order, and the point the aircraft was sent to.
        private List<IntVec3> bombRunCells = new List<IntVec3>();
        private Vector3 bombRunTarget;
        private bool bombRunExiting;
        private int bombRunTicks;
        private IntVec3 bombRunLeadIn = IntVec3.Invalid;
        private float bombRunLastDistance = float.MaxValue;

        public CompProperties_CanLoadShell Props => props as CompProperties_CanLoadShell;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        /// <summary>Shells on board, including the one ready to fire.</summary>
        public int TotalShells
        {
            get
            {
                int count = CurrentShell != null ? 1 : 0;
                for (int i = 0; i < ListShell.Count; i++)
                {
                    count += ListShell[i].stackCount;
                }
                return count;
            }
        }

        public int SpaceLeft => Mathf.Max(0, Props.LoadShell_Max - TotalShells);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref CurrentShell, "CurrentShell");
            Scribe_Collections.Look(ref ListShell, "ListShell", LookMode.Deep);
            Scribe_Values.Look(ref LaunchShell_ReloadTick, "LaunchShell_ReloadTick", 0);
            Scribe_Values.Look(ref BombsPerRun, "AF_BombsPerRun", 3);
            Scribe_Collections.Look(ref bombRunCells, "AF_BombRunCells", LookMode.Value);
            Scribe_Values.Look(ref bombRunTarget, "AF_BombRunTarget");
            Scribe_Values.Look(ref bombRunExiting, "AF_BombRunExiting", false);
            Scribe_Values.Look(ref bombRunTicks, "AF_BombRunTicks", 0);
            Scribe_Values.Look(ref bombRunLeadIn, "AF_BombRunLeadIn", IntVec3.Invalid);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ListShell = ListShell ?? new List<Thing>();
                ListShell.RemoveAll(t => t == null || t.stackCount <= 0);
                bombRunCells = bombRunCells ?? new List<IntVec3>();
            }
        }

        public override string CompInspectStringExtra()
        {
            string text = "AerocraftFramework_ShellsOnBoard".Translate(TotalShells, Props.LoadShell_Max);
            if (BombRunActive && bombRunCells.Count > 0)
            {
                text += "\n" + "AerocraftFramework_BombRun_Status".Translate(bombRunCells.Count.ToString());
            }
            return text;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (aircraft == null || !AerocraftUtility.IsControllable(parent))
            {
                yield break;
            }
            UpdateLabels();
            if (aircraft.Active)
            {
                Command_Action launch = new Command_Action
                {
                    action = DoSomething_LaunchShell,
                    defaultLabel = LaunchShell_Label,
                    defaultDesc = Props.LaunchShell_Description,
                    icon = LaunchShell_Icon
                };
                AcceptanceReport canLaunch = CanActivate();
                if (!canLaunch.Accepted)
                {
                    launch.Disable(canLaunch.Reason.CapitalizeFirst());
                }
                else if (LaunchShell_ReloadTick < Props.LaunchShell_ConsumeTickPerLaunch)
                {
                    launch.Disable("AerocraftFramework_Recharging".Translate((Props.LaunchShell_ConsumeTickPerLaunch - LaunchShell_ReloadTick).ToStringSecondsFromTicks()));
                }
                yield return launch;

                Command_Action drop = new Command_Action
                {
                    action = DoSomething_LaunchShell_Drop,
                    defaultLabel = DropShell_Label,
                    defaultDesc = Props.DropShell_Description,
                    icon = DropShell_Icon,
                    onHover = () =>
                    {
                        float radius = CurrentShellProjectile?.projectile?.explosionRadius ?? 0f;
                        if (radius > 0f)
                        {
                            GenDraw.DrawRadiusRing(aircraft.DrawPos.ToIntVec3(), radius);
                        }
                    }
                };
                if (!canLaunch.Accepted)
                {
                    drop.Disable(canLaunch.Reason.CapitalizeFirst());
                }
                yield return drop;

                Command_BombRun run = new Command_BombRun(this);
                if (!canLaunch.Accepted)
                {
                    run.Disable(canLaunch.Reason.CapitalizeFirst());
                }
                yield return run;
                if (BombRunActive)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "AerocraftFramework_BombRun_Cancel_Label".Translate(),
                        defaultDesc = "AerocraftFramework_BombRun_Cancel_Desc".Translate(),
                        icon = AerocraftWeaponOrders.HaltIcon,
                        action = ClearBombRun
                    };
                }
            }
            if (aircraft.Faction == Faction.OfPlayer && aircraft.Is_Static && SpaceLeft > 0)
            {
                yield return new Command_Action
                {
                    action = () => OrderLoadShells(null),
                    defaultLabel = "AerocraftFramework_LoadShell".Translate(),
                    defaultDesc = "AerocraftFramework_LoadShell_Desc".Translate(),
                    icon = MYDE_TexButton.IconOrDefault(Props.LaunchShell_False_IconPath)
                };
            }
        }

        private void UpdateLabels()
        {
            Texture2D emptyIcon = MYDE_TexButton.IconOrDefault(Props.LaunchShell_False_IconPath);
            if (CurrentShell != null)
            {
                LaunchShell_Label = Props.LaunchShell_Label + ": " + CurrentShell.def.LabelCap + " (" + TotalShells + ")";
                LaunchShell_Icon = LaunchShell_ReloadTick >= Props.LaunchShell_ConsumeTickPerLaunch ? CurrentShell.def.uiIcon : emptyIcon;
                DropShell_Label = Props.DropShell_Label + ": " + CurrentShell.def.LabelCap;
                DropShell_Icon = CurrentShell.def.uiIcon;
            }
            else
            {
                LaunchShell_Label = Props.LaunchShell_Label + ": " + "AerocraftFramework_ShellEmpty".Translate();
                LaunchShell_Icon = emptyIcon;
                DropShell_Label = Props.DropShell_Label + ": " + "AerocraftFramework_ShellEmpty".Translate();
                DropShell_Icon = emptyIcon;
            }
        }

        /// <summary>Original CE build API: the projectile a shell def is fired as.</summary>
        public ThingDef GetAmmo(ThingDef def) => AerocraftCompat.Ammo.ShellProjectile(def);

        public ThingDef CurrentShellProjectile => CurrentShell == null ? null : AerocraftCompat.Ammo.ShellProjectile(CurrentShell.def);

        public virtual AcceptanceReport CanActivate(Pawn activateBy = null)
        {
            if (CurrentShell == null)
            {
                return "AerocraftFramework_NeedShell".Translate();
            }
            if (!Building_Aerocraft_AsBaseThing.Is_Flying)
            {
                return "AerocraftFramework_NoFlying".Translate();
            }
            if (CurrentShellProjectile == null)
            {
                return "AerocraftFramework_ShellNotUsable".Translate(CurrentShell.def.label);
            }
            return true;
        }

        public void DoSomething_LaunchShell()
        {
            if (LaunchShell_ReloadTick < Props.LaunchShell_ConsumeTickPerLaunch || !CanActivate().Accepted)
            {
                return;
            }
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            Map map = aircraft.Map;
            TargetingParameters parms = new TargetingParameters
            {
                canTargetLocations = true,
                validator = target => target.IsValid && target.Cell.InBounds(map)
            };
            Find.Targeter.BeginTargeting(parms, target =>
            {
                if ((aircraft.Position - target.Cell).LengthHorizontal > Props.LaunchShell_Range)
                {
                    Messages.Message("MessageTargetBeyondMaximumRange".Translate(), aircraft, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                aircraft.ChangPosTick = aircraft.ChangPosTickMax;
                aircraft.Change_Position();
                CellRect rect = CellRect.CenteredOn(target.Cell, Props.LaunchShell_ForceRadius);
                rect.ClipInsideMap(map);
                if (FireCurrentShell(rect.RandomCell, 150f))
                {
                    LaunchShell_ReloadTick -= Props.LaunchShell_ConsumeTickPerLaunch;
                    if (CurrentShell != null && LaunchShell_ReloadTick >= Props.LaunchShell_ConsumeTickPerLaunch)
                    {
                        DoSomething_LaunchShell();
                    }
                }
            }, target =>
            {
                if ((aircraft.Position - target.Cell).LengthHorizontal <= Props.LaunchShell_Range)
                {
                    GenDraw.DrawTargetHighlight(target);
                    float radius = CurrentShellProjectile?.projectile?.explosionRadius ?? 0f;
                    if (radius > 0f)
                    {
                        GenDraw.DrawRadiusRing(target.Cell, radius);
                    }
                }
                GenDraw.DrawRadiusRing(aircraft.Position, Props.LaunchShell_Range);
            }, null);
        }

        public void DoSomething_LaunchShell_Drop()
        {
            if (!CanActivate().Accepted)
            {
                return;
            }
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            Map map = aircraft.Map;
            aircraft.ChangPosTick = aircraft.ChangPosTickMax;
            aircraft.Change_Position();
            CellRect rect = CellRect.CenteredOn(aircraft.DrawPos.ToIntVec3(), 1);
            rect.ClipInsideMap(map);
            FireCurrentShell(rect.RandomCell, -1f);
        }

        private bool FireCurrentShell(IntVec3 cell, float speed)
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            ThingDef projectile = CurrentShellProjectile;
            if (projectile == null || !AerocraftCompat.Ammo.LaunchProjectile(aircraft, projectile, aircraft.DrawPos, BombAltitude, cell, speed))
            {
                return false;
            }
            DoSomething_ReloadShell();
            return true;
        }

        /// <summary>Moves the next stored shell into the ready position (or empties it).</summary>
        public void DoSomething_ReloadShell()
        {
            if (ListShell.Count > 0)
            {
                Thing stack = ListShell[0];
                CurrentShell = ThingMaker.MakeThing(stack.def, stack.Stuff);
                stack.stackCount--;
                if (stack.stackCount <= 0)
                {
                    ListShell.RemoveAt(0);
                }
            }
            else
            {
                CurrentShell = null;
            }
        }

        /// <summary>Stores <paramref name="count"/> shells of the thing's kind (the thing itself is not kept). Returns how many were stored.</summary>
        public int StoreShells(ThingDef shellDef, int count)
        {
            int stored = 0;
            while (stored < count && SpaceLeft > 0)
            {
                if (CurrentShell == null)
                {
                    CurrentShell = ThingMaker.MakeThing(shellDef);
                }
                else
                {
                    Thing stack = ListShell.FirstOrDefault(t => t.def == shellDef);
                    if (stack == null)
                    {
                        stack = ThingMaker.MakeThing(shellDef);
                        stack.stackCount = 0;
                        ListShell.Add(stack);
                    }
                    stack.stackCount++;
                }
                stored++;
            }
            return stored;
        }

        /// <summary>Original API: store one shell.</summary>
        public void DoSomething_CarryShell(Thing Thing)
        {
            if (Thing != null)
            {
                StoreShells(Thing.def, 1);
            }
        }

        /// <summary>Puts a shell kind in the ready position; the previous ready shell goes back to storage.</summary>
        public void SetCurrentShell(ThingDef shellDef)
        {
            Thing stack = ListShell.FirstOrDefault(t => t.def == shellDef);
            if (stack == null || (CurrentShell != null && CurrentShell.def == shellDef))
            {
                return;
            }
            ThingDef previous = CurrentShell?.def;
            CurrentShell = ThingMaker.MakeThing(shellDef);
            stack.stackCount--;
            if (stack.stackCount <= 0)
            {
                ListShell.Remove(stack);
            }
            if (previous != null)
            {
                Thing back = ListShell.FirstOrDefault(t => t.def == previous);
                if (back == null)
                {
                    back = ThingMaker.MakeThing(previous);
                    back.stackCount = 0;
                    ListShell.Add(back);
                }
                back.stackCount++;
            }
        }

        /// <summary>Unloads one shell of the given kind next to the aircraft.</summary>
        public void DropShell(ThingDef shellDef)
        {
            if (!parent.Spawned)
            {
                return;
            }
            bool removed = false;
            if (CurrentShell != null && CurrentShell.def == shellDef)
            {
                DoSomething_ReloadShell();
                removed = true;
            }
            else
            {
                Thing stack = ListShell.FirstOrDefault(t => t.def == shellDef);
                if (stack != null)
                {
                    stack.stackCount--;
                    if (stack.stackCount <= 0)
                    {
                        ListShell.Remove(stack);
                    }
                    removed = true;
                }
            }
            if (removed && GenPlace.TryPlaceThing(ThingMaker.MakeThing(shellDef), parent.Position, parent.Map, ThingPlaceMode.Near, out Thing dropped))
            {
                dropped.SetForbidden(true, warnOnFail: false);
            }
        }

        public Thing FindClosestShell(Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableEver), PathEndMode.ClosestTouch, TraverseParms.For(pawn, pawn.NormalMaxDanger()), 9999f,
                t => AerocraftCompat.Ammo.IsLoadableShell(t.def) && !t.IsForbidden(pawn) && pawn.CanReserve(t));
        }

        public Job MakeLoadShellJob(Pawn pawn)
        {
            Thing shell = FindClosestShell(pawn);
            if (shell == null)
            {
                return null;
            }
            Job job = JobMaker.MakeJob(MYDE_JobDefOf.MYDE_AerocraftFramework_Job_LoadShell, parent, shell);
            job.count = Mathf.Min(shell.stackCount, SpaceLeft);
            return job;
        }

        /// <summary>Sends the given pawn, or the closest able colonist, to load shells.</summary>
        public void OrderLoadShells(Pawn pawn)
        {
            if (pawn == null)
            {
                pawn = parent.Map.mapPawns.FreeColonistsSpawned
                    .Where(p => !p.Downed && !p.Drafted && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && p.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly))
                    .OrderBy(p => p.Position.DistanceToSquared(parent.Position))
                    .FirstOrDefault(p => FindClosestShell(p) != null);
            }
            Job job = pawn == null ? null : MakeLoadShellJob(pawn);
            if (job == null)
            {
                Messages.Message("AerocraftFramework_NoShellsAvailable".Translate(), parent, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn myPawn)
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (aircraft == null || aircraft.Faction != Faction.OfPlayer || !aircraft.Is_Static || !myPawn.RaceProps.Humanlike)
            {
                yield break;
            }
            string label = "AerocraftFramework_LoadShell".Translate() + ": " + aircraft.LabelShort;
            if (SpaceLeft <= 0)
            {
                yield return new FloatMenuOption(label + " (" + "AerocraftFramework_ShellBayFull".Translate() + ")", null);
                yield break;
            }
            if (!myPawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                yield break;
            }
            if (FindClosestShell(myPawn) == null)
            {
                yield return new FloatMenuOption(label + " (" + "AerocraftFramework_NoShellsAvailable".Translate() + ")", null);
                yield break;
            }
            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () => OrderLoadShells(myPawn)), myPawn, aircraft);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (CurrentShell != null && LaunchShell_ReloadTick < Props.LaunchShell_ReloadTickMax)
            {
                LaunchShell_ReloadTick++;
            }
            BombRunTick();
        }

        // ------------------------------------------------------------------ bombing run

        /// <summary>Distance at which a bomb is released: over the planned cell, a little ahead for fast aircraft.</summary>
        private const float ReleaseDistanceMin = 1.5f;
        private const int TicksPerDropPointMax = 2500;
        private const float StickSpacing = 1.5f;
        private const float ExitDistance = 10f;

        /// <summary>A bomb is also released when the aircraft passes this close to its cell and moves away again.</summary>
        private const float PassDistance = AerocraftRunPlanner.PassDistance;

        public bool BombRunActive => bombRunCells.Count > 0 || bombRunExiting;

        /// <summary>Cells still to bomb in the current run.</summary>
        public IReadOnlyList<IntVec3> BombRunCells => bombRunCells;

        public int BombsForNextRun => BombsPerRun <= 0 ? TotalShells : Mathf.Min(BombsPerRun, TotalShells);

        /// <summary>
        /// Where the bombs of a run fall: evenly along the line from <paramref name="start"/> to <paramref name="end"/>,
        /// or, for a single target, a short stick along the approach centred on it.
        /// </summary>
        public static List<IntVec3> PlanDropCells(Vector3 from, IntVec3 start, IntVec3? end, int count, Map map)
        {
            List<IntVec3> cells = new List<IntVec3>();
            if (count <= 0)
            {
                return cells;
            }
            Vector3 a = start.ToVector3Shifted();
            if (end.HasValue && end.Value != start)
            {
                Vector3 b = end.Value.ToVector3Shifted();
                for (int i = 0; i < count; i++)
                {
                    cells.Add(Vector3.Lerp(a, b, count == 1 ? 0.5f : (float)i / (count - 1)).ToIntVec3());
                }
            }
            else
            {
                Vector3 direction = a - from;
                direction.y = 0f;
                direction = direction.sqrMagnitude < 0.01f ? Vector3.forward : direction.normalized;
                for (int i = 0; i < count; i++)
                {
                    cells.Add((a + direction * ((i - (count - 1) / 2f) * StickSpacing)).ToIntVec3());
                }
            }
            return cells.Select(c => c.ClampInsideMap(map)).ToList();
        }

        /// <summary>Sends the aircraft over the target (or along the line) to drop <see cref="BombsForNextRun"/> bombs.</summary>
        public AcceptanceReport StartBombRun(IntVec3 start, IntVec3? end)
        {
            AcceptanceReport canActivate = CanActivate();
            if (!canActivate.Accepted)
            {
                return canActivate;
            }
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            List<IntVec3> cells = PlanDropCells(aircraft.DrawPos, start, end, BombsForNextRun, aircraft.Map);
            IntVec3 leadIn = LeadInFor(aircraft, cells);
            if (!SendAircraftTo((leadIn.IsValid ? leadIn : cells[0]).ToVector3Shifted()))
            {
                return "AerocraftFramework_BombRun_Busy".Translate();
            }
            bombRunCells = cells;
            bombRunLeadIn = leadIn;
            bombRunExiting = false;
            bombRunTicks = 0;
            bombRunLastDistance = float.MaxValue;
            return true;
        }

        /// <summary>
        /// For a line met at an angle, a point on the line's extension before its start: the aircraft lines up there
        /// and flies the line straight. A fixed-wing aircraft cannot turn onto a bomb a few cells to its side.
        /// </summary>
        private static IntVec3 LeadInFor(Building_Aerocraft_AsBaseThing aircraft, List<IntVec3> cells)
        {
            return cells.Count < 2 ? IntVec3.Invalid : AerocraftRunPlanner.LeadIn(aircraft.DrawPos, cells[0], cells[cells.Count - 1], aircraft.Map);
        }

        public void ClearBombRun()
        {
            bombRunCells.Clear();
            bombRunLeadIn = IntVec3.Invalid;
            bombRunExiting = false;
            bombRunTicks = 0;
        }

        private bool SendAircraftTo(Vector3 point)
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            aircraft.Set_TargetVPos(point);
            bombRunTarget = aircraft.TargetVPos;
            // Set_TargetVPos ignores the order while the aircraft glides to take off or land.
            return !aircraft.HaveGoToTarget && bombRunTarget == point;
        }

        private void AbortBombRun(string reason)
        {
            ClearBombRun();
            Messages.Message("AerocraftFramework_BombRun_Aborted".Translate(parent.LabelShort, reason), parent, MessageTypeDefOf.CautionInput, historical: false);
        }

        private void BombRunTick()
        {
            if (!BombRunActive)
            {
                return;
            }
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (aircraft == null || !aircraft.Spawned)
            {
                ClearBombRun();
                return;
            }
            // The map edge turned the aircraft round (it sends it elsewhere and raises this flag).
            if (aircraft.If_CheckInMapBoundaryPos && aircraft.TargetVPos != bombRunTarget)
            {
                AbortBombRun("AerocraftFramework_Run_MapEdge".Translate());
                return;
            }
            // Another order (move, follow, go back) ends the run quietly.
            if (aircraft.TargetVPos != bombRunTarget || aircraft.FollowTargetThing != null || aircraft.If_GoBackNow)
            {
                ClearBombRun();
                return;
            }
            if (!aircraft.Is_Flying || !aircraft.If_UpOrDown)
            {
                AbortBombRun("AerocraftFramework_NoFlying".Translate());
                return;
            }
            if (bombRunExiting)
            {
                if (aircraft.HaveGoToTarget)
                {
                    bombRunExiting = false;
                }
                return;
            }
            if (CurrentShell == null)
            {
                AbortBombRun("AerocraftFramework_ShellEmpty".Translate());
                return;
            }
            float release = Mathf.Max(ReleaseDistanceMin, aircraft.MoveSpeed_Max * 3f);
            if (bombRunLeadIn.IsValid)
            {
                float toLeadIn = (aircraft.DrawPos - bombRunLeadIn.ToVector3Shifted()).MagnitudeHorizontal();
                bool passed = toLeadIn <= PassDistance && toLeadIn > bombRunLastDistance;
                bombRunLastDistance = toLeadIn;
                if (toLeadIn > release && !passed && !aircraft.HaveGoToTarget)
                {
                    if (++bombRunTicks > TicksPerDropPointMax)
                    {
                        AbortBombRun("AerocraftFramework_BombRun_CannotReach".Translate());
                    }
                    return;
                }
                bombRunLeadIn = IntVec3.Invalid;
                bombRunTicks = 0;
                bombRunLastDistance = float.MaxValue;
                if (!SendAircraftTo(bombRunCells[0].ToVector3Shifted()))
                {
                    ClearBombRun();
                }
                return;
            }
            IntVec3 next = bombRunCells[0];
            float distance = (aircraft.DrawPos - next.ToVector3Shifted()).MagnitudeHorizontal();
            bool passing = distance <= PassDistance && distance > bombRunLastDistance;
            bombRunLastDistance = distance;
            if (distance > release && !passing && !aircraft.HaveGoToTarget)
            {
                if (++bombRunTicks > TicksPerDropPointMax)
                {
                    AbortBombRun("AerocraftFramework_BombRun_CannotReach".Translate());
                }
                return;
            }
            FireCurrentShell(next, -1f);
            bombRunCells.RemoveAt(0);
            bombRunTicks = 0;
            bombRunLastDistance = float.MaxValue;
            if (bombRunCells.Count > 0)
            {
                if (!SendAircraftTo(bombRunCells[0].ToVector3Shifted()))
                {
                    ClearBombRun();
                }
                return;
            }
            // Fly on past the last bomb instead of circling over the explosions.
            IntVec3 exit = AerocraftRunPlanner.ExitPoint(aircraft, ExitDistance);
            bombRunExiting = SendAircraftTo(exit.ToVector3Shifted());
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (bombRunCells.Count > 0 && parent.Spawned)
            {
                Vector3 from = parent.DrawPos;
                if (bombRunLeadIn.IsValid)
                {
                    Vector3 leadIn = bombRunLeadIn.ToVector3Shifted();
                    Vector3 a = from;
                    a.y = leadIn.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                    GenDraw.DrawLineBetween(a, leadIn, SimpleColor.White);
                    from = leadIn;
                }
                DrawDropCells(from, bombRunCells, CurrentShellProjectile);
            }
        }

        /// <summary>The run's path and the blast radius at every drop cell.</summary>
        public static void DrawDropCells(Vector3 from, IList<IntVec3> cells, ThingDef projectile)
        {
            float radius = projectile?.projectile?.explosionRadius ?? 0f;
            Vector3 previous = from;
            foreach (IntVec3 cell in cells)
            {
                Vector3 point = cell.ToVector3Shifted();
                Vector3 a = previous;
                Vector3 b = point;
                a.y = b.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                GenDraw.DrawLineBetween(a, b, SimpleColor.Red);
                if (radius > 0f && radius < GenRadial.MaxRadialPatternRadius)
                {
                    GenDraw.DrawRadiusRing(cell, radius, new Color(1f, 0.4f, 0.3f));
                }
                else
                {
                    GenDraw.DrawTargetHighlight(cell);
                }
                previous = point;
            }
        }
    }
}
