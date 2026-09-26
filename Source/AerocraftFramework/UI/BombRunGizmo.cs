using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Bombing run: click a target and the aircraft flies over it and drops a stick of bombs along its approach;
    /// shift-click the start of a line and click its end to bomb evenly along the line. Right click: bombs per run.
    /// With several bombers selected, each of them makes the run.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Command_BombRun : Command
    {
        private static readonly int[] BombCounts = { 1, 2, 3, 5, 10, 0 };

        private List<Comp_CanLoadShell> bays;

        public Command_BombRun(Comp_CanLoadShell bay)
        {
            bays = new List<Comp_CanLoadShell> { bay };
            defaultLabel = "AerocraftFramework_BombRun_Label".Translate();
            defaultDesc = "AerocraftFramework_BombRun_Desc".Translate(BombCountLabel(bay.BombsPerRun));
            icon = bay.CurrentShell?.def.uiIcon ?? AerocraftWeaponOrders.AttackIcon;
            Order = -90f;
        }

        public override bool GroupsWith(Gizmo other) => other is Command_BombRun;

        public override void MergeWith(Gizmo other)
        {
            if (other is Command_BombRun command)
            {
                bays.AddRange(command.bays.Where(b => !bays.Contains(b)));
                command.bays = bays;
            }
        }

        public override bool InheritInteractionsFrom(Gizmo other) => false;

        public override bool InheritFloatMenuInteractionsFrom(Gizmo other) => false;

        public override void DrawIcon(Rect rect, Material buttonMat, GizmoRenderParms parms)
        {
            base.DrawIcon(rect, buttonMat, parms);
            // A crosshair over the bomb: this one aims, the drop button next to it does not.
            GUI.DrawTexture(new Rect(rect.xMax - 30f, rect.yMax - 30f, 26f, 26f), AerocraftWeaponOrders.AttackIcon);
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            BeginTargeting(bays.Where(b => b.CanActivate().Accepted).ToList());
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                int current = bays[0].BombsPerRun;
                foreach (int count in BombCounts)
                {
                    int chosen = count;
                    string label = "AerocraftFramework_BombRun_PerRun".Translate(BombCountLabel(count)) + (count == current ? " ✔" : "");
                    yield return new FloatMenuOption(label, () =>
                    {
                        foreach (Comp_CanLoadShell bay in bays)
                        {
                            bay.BombsPerRun = chosen;
                        }
                    });
                }
                if (bays.Any(b => b.BombRunActive))
                {
                    yield return new FloatMenuOption("AerocraftFramework_BombRun_Cancel_Label".Translate(), () => bays.ForEach(b => b.ClearBombRun()));
                }
            }
        }

        private static string BombCountLabel(int count)
        {
            return count <= 0 ? "AerocraftFramework_BombRun_All".Translate().ToString() : count.ToString();
        }

        private static TargetingParameters TargetParams => new TargetingParameters
        {
            canTargetLocations = true,
            canTargetPawns = true,
            canTargetBuildings = true,
            canTargetItems = true,
            mapObjectTargetsMustBeAutoAttackable = false
        };

        /// <summary>First click: the target, or with shift the start of a line (then a second click for its end).</summary>
        public static void BeginTargeting(List<Comp_CanLoadShell> bays)
        {
            if (bays.Count == 0)
            {
                return;
            }
            Map map = bays[0].parent.Map;
            Find.Targeter.BeginTargeting(TargetParams,
                action: target =>
                {
                    if (Event.current != null && Event.current.shift)
                    {
                        BeginLineEndTargeting(bays, target.Cell);
                    }
                    else
                    {
                        OrderRun(bays, target.Cell, null);
                    }
                },
                highlightAction: target => DrawPreview(bays, target, null),
                targetValidator: target => target.IsValid && target.Cell.InBounds(map),
                mouseAttachment: bays[0].CurrentShell?.def.uiIcon,
                onGuiAction: target => Widgets.MouseAttachedLabel("AerocraftFramework_BombRun_MouseTarget".Translate()));
        }

        private static void BeginLineEndTargeting(List<Comp_CanLoadShell> bays, IntVec3 start)
        {
            Map map = bays[0].parent.Map;
            Find.Targeter.BeginTargeting(TargetParams,
                action: target => OrderRun(bays, start, target.Cell),
                highlightAction: target => DrawPreview(bays, new LocalTargetInfo(start), target.IsValid ? target.Cell : (IntVec3?)null),
                targetValidator: target => target.IsValid && target.Cell.InBounds(map),
                mouseAttachment: bays[0].CurrentShell?.def.uiIcon,
                onGuiAction: target => Widgets.MouseAttachedLabel("AerocraftFramework_BombRun_MouseLineEnd".Translate()));
        }

        private static void DrawPreview(List<Comp_CanLoadShell> bays, LocalTargetInfo target, IntVec3? end)
        {
            if (!target.IsValid)
            {
                return;
            }
            foreach (Comp_CanLoadShell bay in bays)
            {
                if (bay.parent.Spawned && bay.parent.Map == Find.CurrentMap)
                {
                    List<IntVec3> cells = Comp_CanLoadShell.PlanDropCells(bay.parent.DrawPos, target.Cell, end, bay.BombsForNextRun, bay.parent.Map);
                    Comp_CanLoadShell.DrawDropCells(bay.parent.DrawPos, cells, bay.CurrentShellProjectile);
                }
            }
        }

        private static void OrderRun(List<Comp_CanLoadShell> bays, IntVec3 start, IntVec3? end)
        {
            foreach (Comp_CanLoadShell bay in bays)
            {
                AcceptanceReport started = bay.StartBombRun(start, end);
                if (!started.Accepted && !started.Reason.NullOrEmpty())
                {
                    Messages.Message(bay.parent.LabelShort + ": " + started.Reason, bay.parent, MessageTypeDefOf.RejectInput, historical: false);
                }
            }
        }
    }
}
