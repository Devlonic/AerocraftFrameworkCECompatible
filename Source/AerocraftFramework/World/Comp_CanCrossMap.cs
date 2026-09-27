using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MYDE_AerocraftFramework
{
    /// <summary>Flying to another world tile. Fuel is paid per tile; half the fuel range is the safe (return) range.</summary>
    [StaticConstructorOnStartup]
    public class Comp_CanCrossMap : ThingComp
    {
        /// <summary>World range of aircraft that do not use fuel.</summary>
        private const int UnlimitedRange = 300;

        public float Fuel;
        public int SafeRange;
        public int NoBackRange;
        public List<Building> WeaponBuilding = new List<Building>();

        public CompProperties_CanCrossMap Props => props as CompProperties_CanCrossMap;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (aircraft == null || aircraft.Faction != Faction.OfPlayer)
            {
                yield break;
            }
            Command_Action command = new Command_Action
            {
                action = DoSomething_GoTo,
                defaultLabel = Props.CrossMap_Label,
                defaultDesc = Props.CrossMap_Description,
                icon = MYDE_TexButton.IconOrDefault(Props.CrossMap_IconPath)
            };
            if (!aircraft.HasEnoughPilots)
            {
                command.Disable("AerocraftFramework_NeedPawnToControl".Translate());
            }
            else if (aircraft.If_DropingNow)
            {
                command.Disable("AerocraftFramework_Status_Crashing".Translate());
            }
            yield return command;
        }

        public static void ComputeRanges(CompRefuelable refuelable, float fuelConsumeBase, out float fuel, out int safeRange, out int noBackRange)
        {
            if (refuelable == null || fuelConsumeBase <= 0f)
            {
                fuel = 0f;
                safeRange = UnlimitedRange / 2;
                noBackRange = UnlimitedRange;
                return;
            }
            fuel = refuelable.Fuel;
            safeRange = (int)(fuel / (fuelConsumeBase * 2f));
            noBackRange = (int)(fuel / fuelConsumeBase);
        }

        public void DoSomething_GoTo()
        {
            Map map = parent.Map;
            ComputeRanges(parent.TryGetComp<CompRefuelable>(), Props.FuelConsumeBase, out Fuel, out SafeRange, out NoBackRange);
            float fuelConsumeBase = Props.FuelConsumeBase;
            int originTile = map.Tile;
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(parent));
            Find.WorldSelector.ClearSelection();
            Find.WorldTargeter.BeginTargeting(ChoseWorldTarget, canTargetTiles: true, null, closeWorldTabWhenFinished: true, () =>
            {
                GenDraw.DrawWorldRadiusRing(originTile, SafeRange);
                GenDraw.DrawWorldRadiusRing(originTile, NoBackRange);
            }, target => TargetingLabelGetter(target, originTile, SafeRange, NoBackRange, Fuel, fuelConsumeBase));
        }

        public static string TargetingLabelGetter(GlobalTargetInfo Target, int OriginTile, int SafeLaunchDistance, int MaxLaunchDistance, float Fuel, float FuelConsumeSpeedBase)
        {
            if (!Target.IsValid)
            {
                return null;
            }
            if (Target.WorldObject == null)
            {
                return "AerocraftFramework_TargetNotValid".Translate();
            }
            int distance = Find.WorldGrid.TraversalDistanceBetween(OriginTile, Target.Tile);
            if (distance > MaxLaunchDistance)
            {
                GUI.color = ColorLibrary.RedReadable;
                return "AerocraftFramework_OutOfRange".Translate();
            }
            string fuelLeft = FuelConsumeSpeedBase > 0f && Fuel > 0f ? " " + "AerocraftFramework_FuelSurplus".Translate() + ": " + (Fuel - FuelConsumeSpeedBase * distance).ToString("0") : "";
            if (distance > SafeLaunchDistance)
            {
                GUI.color = ColorLibrary.Yellow;
                return Target.WorldObject.Label + ": " + "AerocraftFramework_NoBack".Translate() + fuelLeft;
            }
            return Target.WorldObject.Label + ":" + fuelLeft;
        }

        public bool ChoseWorldTarget(GlobalTargetInfo Target)
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            Map map = parent.Map;
            if (!Target.IsValid || Target.Tile == map.Tile)
            {
                return false;
            }
            if (!aircraft.HasEnoughPilots)
            {
                aircraft.ThrowNeedPilots();
                return false;
            }
            if (Target.WorldObject == null)
            {
                Messages.Message("AerocraftFramework_ChoseWorldTargetError".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            int distance = Find.WorldGrid.TraversalDistanceBetween(map.Tile, Target.Tile);
            if (distance > NoBackRange)
            {
                Messages.Message("AerocraftFramework_OutOfRange".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            // It flies off the map edge towards the destination (see Building_Aerocraft_AsBaseThing.BeginDeparture);
            // the fuel is paid when it leaves the map. Skyfaller_Aerocraft_Leaving stays for older saves.
            aircraft.BeginDeparture(Target.Tile, Props.WorldObjectDef, Props.TravelSpeed, distance * Props.FuelConsumeBase);
            if (!aircraft.IsDeparting)
            {
                return false;
            }
            CameraJumper.TryJump(aircraft);
            return true;
        }

        /// <summary>Pawns loaded through the transporter travel in a separate list (their lord is dissolved).</summary>
        internal static void StowTransporterPawns(Building_Aerocraft_AsBaseThing aircraft, Map map)
        {
            CompTransporter transporter = aircraft.TryGetComp<CompTransporter>();
            if (transporter == null)
            {
                return;
            }
            List<Pawn> pawns = new List<Pawn>();
            foreach (Thing thing in transporter.innerContainer)
            {
                if (thing is Pawn pawn)
                {
                    pawns.Add(pawn);
                }
            }
            for (int i = 0; i < pawns.Count; i++)
            {
                transporter.innerContainer.Remove(pawns[i]);
            }
            aircraft.ListCompTransporterPawn.AddRange(pawns);
            Lord lord = TransporterUtility.FindLord(transporter.groupID, map);
            if (lord != null)
            {
                map.lordManager.RemoveLord(lord);
            }
        }
    }
}
