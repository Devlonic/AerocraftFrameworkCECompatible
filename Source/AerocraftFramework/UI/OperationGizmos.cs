using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Orders that need a place on the map: a strafing run (click a target, or shift-click the start of a line and
    /// click its end) and a troop drop (click where to hover). Every selected aircraft takes part, each on its own
    /// line or spot, so they do not pile up on one cell.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Command_AerocraftRun : Command
    {
        public enum RunKind
        {
            Strafe,
            TroopDrop
        }

        /// <summary>Distance between the lines or drop spots of the aircraft of a group.</summary>
        private const float GroupSpacing = 5f;

        public readonly RunKind kind;
        private List<Building_Aerocraft_AsBaseThing> aircraft;

        public static Texture2D MedevacIcon => ThingDefOf.MedicineIndustrial.uiIcon;

        public Command_AerocraftRun(Building_Aerocraft_AsBaseThing craft, RunKind kind)
        {
            this.kind = kind;
            aircraft = new List<Building_Aerocraft_AsBaseThing> { craft };
            if (kind == RunKind.Strafe)
            {
                defaultLabel = "AerocraftFramework_Strafe_Label".Translate();
                defaultDesc = "AerocraftFramework_Strafe_Desc".Translate();
                icon = TexCommand.SquadAttack;
                Order = -91f;
            }
            else
            {
                defaultLabel = "AerocraftFramework_TroopDrop_Label".Translate();
                defaultDesc = "AerocraftFramework_TroopDrop_Desc".Translate(craft.CrewToKeep.ToString());
                icon = TexCommand.DropCarriedPawn;
                Order = -89f;
            }
        }

        public override bool GroupsWith(Gizmo other) => other is Command_AerocraftRun run && run.kind == kind;

        public override void MergeWith(Gizmo other)
        {
            if (other is Command_AerocraftRun run)
            {
                aircraft.AddRange(run.aircraft.Where(a => !aircraft.Contains(a)));
                run.aircraft = aircraft;
            }
        }

        public override bool InheritInteractionsFrom(Gizmo other) => false;

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            List<Building_Aerocraft_AsBaseThing> able = aircraft.Where(a => a.Spawned && (kind == RunKind.Strafe ? a.CanStrafe : a.CanDropTroops).Accepted).ToList();
            if (able.Count == 0)
            {
                return;
            }
            if (kind == RunKind.Strafe)
            {
                BeginStrafeTargeting(able);
            }
            else
            {
                BeginTroopDropTargeting(able);
            }
        }

        private static TargetingParameters TargetParams => new TargetingParameters
        {
            canTargetLocations = true,
            canTargetPawns = true,
            canTargetBuildings = true,
            canTargetItems = true,
            mapObjectTargetsMustBeAutoAttackable = false
        };

        /// <summary>Aircraft i of a group is moved sideways: 0, +1, -1, +2, -2... spacings.</summary>
        private static float SideOffset(int index)
        {
            int step = (index + 1) / 2;
            return (index % 2 == 1 ? 1f : -1f) * step * GroupSpacing;
        }

        private static IntVec3 Shift(IntVec3 cell, Vector3 side, float offset, Map map)
        {
            return (cell.ToVector3Shifted() + side * offset).ToIntVec3().ClampInsideMap(map);
        }

        // ------------------------------------------------------------------ strafing

        private static void BeginStrafeTargeting(List<Building_Aerocraft_AsBaseThing> group)
        {
            Map map = group[0].Map;
            Find.Targeter.BeginTargeting(TargetParams,
                action: target =>
                {
                    if (Event.current != null && Event.current.shift)
                    {
                        BeginStrafeEndTargeting(group, target.Cell);
                    }
                    else
                    {
                        OrderStrafe(group, target.Cell, null);
                    }
                },
                highlightAction: target => DrawStrafePreview(group, target, null),
                targetValidator: target => target.IsValid && target.Cell.InBounds(map),
                mouseAttachment: TexCommand.SquadAttack,
                onGuiAction: target => Widgets.MouseAttachedLabel("AerocraftFramework_BombRun_MouseTarget".Translate()));
        }

        private static void BeginStrafeEndTargeting(List<Building_Aerocraft_AsBaseThing> group, IntVec3 start)
        {
            Map map = group[0].Map;
            Find.Targeter.BeginTargeting(TargetParams,
                action: target => OrderStrafe(group, start, target.Cell),
                highlightAction: target => DrawStrafePreview(group, new LocalTargetInfo(start), target.IsValid ? target.Cell : (IntVec3?)null),
                targetValidator: target => target.IsValid && target.Cell.InBounds(map),
                mouseAttachment: TexCommand.SquadAttack,
                onGuiAction: target => Widgets.MouseAttachedLabel("AerocraftFramework_BombRun_MouseLineEnd".Translate()));
        }

        /// <summary>The line of each aircraft: the group's line moved sideways by its place in the group.</summary>
        private static void PlanGroupStrafe(List<Building_Aerocraft_AsBaseThing> group, IntVec3 start, IntVec3? end, out List<(Building_Aerocraft_AsBaseThing craft, IntVec3 start, IntVec3 end)> lines)
        {
            lines = new List<(Building_Aerocraft_AsBaseThing, IntVec3, IntVec3)>();
            Building_Aerocraft_AsBaseThing lead = group[0];
            Building_Aerocraft_AsBaseThing.PlanStrafe(lead.DrawPos, start, end, lead.Map, out IntVec3 a, out IntVec3 b, out _);
            Vector3 line = b.ToVector3Shifted() - a.ToVector3Shifted();
            line.y = 0f;
            Vector3 side = line.sqrMagnitude < 0.01f ? Vector3.right : new Vector3(line.z, 0f, -line.x).normalized;
            for (int i = 0; i < group.Count; i++)
            {
                float offset = SideOffset(i);
                lines.Add((group[i], Shift(a, side, offset, lead.Map), Shift(b, side, offset, lead.Map)));
            }
        }

        private static void DrawStrafePreview(List<Building_Aerocraft_AsBaseThing> group, LocalTargetInfo target, IntVec3? end)
        {
            if (!target.IsValid)
            {
                return;
            }
            PlanGroupStrafe(group, target.Cell, end, out List<(Building_Aerocraft_AsBaseThing craft, IntVec3 start, IntVec3 end)> lines);
            float altitude = AltitudeLayer.MetaOverlays.AltitudeFor();
            foreach ((Building_Aerocraft_AsBaseThing craft, IntVec3 a, IntVec3 b) in lines)
            {
                if (craft.Map != Find.CurrentMap)
                {
                    continue;
                }
                IntVec3 leadIn = AerocraftRunPlanner.LeadIn(craft.DrawPos, a, b, craft.Map);
                Vector3 from = craft.DrawPos;
                Vector3 first = (leadIn.IsValid ? leadIn : a).ToVector3Shifted();
                from.y = first.y = altitude;
                GenDraw.DrawLineBetween(from, first, SimpleColor.White);
                Vector3 lineStart = a.ToVector3Shifted();
                Vector3 lineEnd = b.ToVector3Shifted();
                lineStart.y = lineEnd.y = altitude;
                if (leadIn.IsValid)
                {
                    GenDraw.DrawLineBetween(first, lineStart, SimpleColor.White);
                }
                GenDraw.DrawLineBetween(lineStart, lineEnd, SimpleColor.Red, 0.4f);
                GenDraw.DrawTargetHighlight(b);
            }
        }

        private static void OrderStrafe(List<Building_Aerocraft_AsBaseThing> group, IntVec3 start, IntVec3? end)
        {
            PlanGroupStrafe(group, start, end, out List<(Building_Aerocraft_AsBaseThing craft, IntVec3 start, IntVec3 end)> lines);
            foreach ((Building_Aerocraft_AsBaseThing craft, IntVec3 a, IntVec3 b) in lines)
            {
                Report(craft, craft.StartStrafeRun(a, b));
            }
        }

        // ------------------------------------------------------------------ troop drop

        private static bool HasRopeRoom(IntVec3 cell, Map map)
        {
            return GenRadial.RadialCellsAround(cell, 3f, useCenter: true).Any(c => c.InBounds(map) && c.Standable(map) && !c.Roofed(map));
        }

        private static List<IntVec3> DropSpots(List<Building_Aerocraft_AsBaseThing> group, IntVec3 cell)
        {
            Map map = group[0].Map;
            List<IntVec3> spots = new List<IntVec3>();
            for (int i = 0; i < group.Count; i++)
            {
                if (i == 0)
                {
                    spots.Add(cell);
                    continue;
                }
                float angle = 360f * (i - 1) / Mathf.Max(1, group.Count - 1);
                spots.Add(MYDE_ModFront.GetVector3_By_AngleFlat(cell.ToVector3Shifted(), GroupSpacing, angle).ToIntVec3().ClampInsideMap(map));
            }
            return spots;
        }

        private static void BeginTroopDropTargeting(List<Building_Aerocraft_AsBaseThing> group)
        {
            Map map = group[0].Map;
            Find.Targeter.BeginTargeting(TargetParams,
                action: target =>
                {
                    List<IntVec3> spots = DropSpots(group, target.Cell);
                    for (int i = 0; i < group.Count; i++)
                    {
                        Report(group[i], group[i].StartTroopDrop(spots[i]));
                    }
                },
                highlightAction: target =>
                {
                    if (!target.IsValid)
                    {
                        return;
                    }
                    List<IntVec3> spots = DropSpots(group, target.Cell);
                    for (int i = 0; i < group.Count; i++)
                    {
                        GenDraw.DrawRadiusRing(spots[i], 3f, HasRopeRoom(spots[i], map) ? Color.white : Color.red);
                        Vector3 from = group[i].DrawPos;
                        Vector3 to = spots[i].ToVector3Shifted();
                        from.y = to.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                        GenDraw.DrawLineBetween(from, to, SimpleColor.White);
                    }
                },
                targetValidator: target => target.IsValid && target.Cell.InBounds(map) && HasRopeRoom(target.Cell, map),
                mouseAttachment: TexCommand.DropCarriedPawn,
                onGuiAction: target => Widgets.MouseAttachedLabel("AerocraftFramework_TroopDrop_Mouse".Translate()));
        }

        private static void Report(Building_Aerocraft_AsBaseThing craft, AcceptanceReport report)
        {
            if (!report.Accepted && !report.Reason.NullOrEmpty())
            {
                Messages.Message(craft.LabelShort + ": " + report.Reason, craft, MessageTypeDefOf.RejectInput, historical: false);
            }
        }
    }
}
