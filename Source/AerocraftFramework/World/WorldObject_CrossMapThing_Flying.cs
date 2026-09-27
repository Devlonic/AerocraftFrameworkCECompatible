using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>An aircraft flying over the world map (and waiting at its destination until ordered to land or attack).</summary>
    public class WorldObject_CrossMapThing_Flying : WorldObject
    {
        public float Fuel;
        public int SafeRange;
        public int NoBackRange;
        public Map TargetMap;
        public Building_Aerocraft_AsBaseThing LinkToAerocraft;
        public int DestinationTile = -1;
        public bool arrived;
        private int InitialTile = -1;
        public float TraveledPct;
        public float TravelSpeed = 0.00025f;
        public List<Building> AllExtraWeapon = new List<Building>();
        public List<Thing> ListThingToSaveCompTransporter = new List<Thing>();

        public bool IsPlayerControlled => Faction == Faction.OfPlayer;

        private Vector3 Start => Find.WorldGrid.GetTileCenter(InitialTile);

        private Vector3 End => Find.WorldGrid.GetTileCenter(DestinationTile);

        public override Vector3 DrawPos => Vector3.Slerp(Start, End, TraveledPct);

        public override bool ExpandingIconFlipHorizontal => GenWorldUI.WorldToUIPosition(Start).x > GenWorldUI.WorldToUIPosition(End).x;

        public override float ExpandingIconRotation
        {
            get
            {
                if (!def.rotateGraphicWhenTraveling)
                {
                    return base.ExpandingIconRotation;
                }
                Vector2 a = GenWorldUI.WorldToUIPosition(Start);
                Vector2 b = GenWorldUI.WorldToUIPosition(End);
                float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                if (angle > 180f)
                {
                    angle -= 180f;
                }
                return angle + 90f;
            }
        }

        private float TraveledPctStepPerTick
        {
            get
            {
                Vector3 start = Start;
                Vector3 end = End;
                if (start == end)
                {
                    return 1f;
                }
                float distance = GenMath.SphericalDistance(start.normalized, end.normalized);
                return distance == 0f ? 1f : Comp_CanCrossMap.EffectiveTravelSpeed(TravelSpeed) / distance;
            }
        }

        private Comp_CanCrossMap CrossMapComp => LinkToAerocraft?.TryGetComp<Comp_CanCrossMap>();

        private CompTransporter Transporter => LinkToAerocraft?.TryGetComp<CompTransporter>();

        public override string Label => LinkToAerocraft != null ? LinkToAerocraft.LabelCap.ToString() : base.Label;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref LinkToAerocraft, "LinkToAerocraft");
            Scribe_Values.Look(ref TravelSpeed, "TravelSpeed", 0f);
            Scribe_Values.Look(ref DestinationTile, "DestinationTile", 0);
            Scribe_Values.Look(ref arrived, "arrived", false);
            Scribe_Values.Look(ref InitialTile, "InitialTile", 0);
            Scribe_Values.Look(ref TraveledPct, "TraveledPct", 0f);
            Scribe_Collections.Look(ref AllExtraWeapon, "AllExtraWeapon", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                AllExtraWeapon = AllExtraWeapon ?? new List<Building>();
                AllExtraWeapon.RemoveAll(b => b == null);
            }
        }

        public override void PostAdd()
        {
            base.PostAdd();
            InitialTile = Tile;
        }

        public override void Tick()
        {
            base.Tick();
            if (arrived)
            {
                return;
            }
            TraveledPct += TraveledPctStepPerTick;
            if (TraveledPct >= 1f)
            {
                TraveledPct = 1f;
                Arrived();
            }
        }

        private void Arrived()
        {
            if (arrived)
            {
                return;
            }
            arrived = true;
            Tile = DestinationTile;
            MapParent mapParent = Find.WorldObjects.MapParentAt(DestinationTile);
            string label = LinkToAerocraft?.LabelCap ?? def.LabelCap;
            string text = label + ": " + "AerocraftFramework_AerocraftArrived".Translate(mapParent?.LabelCap ?? Find.WorldGrid[DestinationTile].biome.LabelCap);
            Find.LetterStack.ReceiveLetter(text, text, LetterDefOf.NeutralEvent, new GlobalTargetInfo(this));
            if (mapParent != null && mapParent.Faction == Faction.OfPlayer)
            {
                DoSomething_Attack();
            }
        }

        public override IEnumerable<Gizmo> GetCaravanGizmos(Caravan caravan)
        {
            foreach (Gizmo gizmo in base.GetCaravanGizmos(caravan))
            {
                yield return gizmo;
            }
            if (Spawned && arrived && LinkToAerocraft != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "AerocraftFramework_Trade_GetOn_Label".Translate(),
                    defaultDesc = "AerocraftFramework_Trade_GetOn_Desc".Translate() + " (" + LinkToAerocraft.Label + ")",
                    icon = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/CaravanUp"),
                    action = () => DoSomething_GetOn(caravan)
                };
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (!arrived || LinkToAerocraft == null)
            {
                yield break;
            }
            Comp_CanCrossMap crossMap = CrossMapComp;
            if (crossMap != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = crossMap.Props.CrossMap_Label,
                    defaultDesc = crossMap.Props.CrossMap_Description,
                    icon = MYDE_TexButton.IconOrDefault(crossMap.Props.CrossMap_IconPath),
                    action = DoSomething_Move
                };
            }
            MapParent mapParent = Find.WorldObjects.MapParentAt(Tile);
            Command_Action attack = new Command_Action
            {
                defaultLabel = "AerocraftFramework_CommandAttackSettlement".Translate(),
                defaultDesc = "CommandAttackSettlementDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/AttackSettlement"),
                action = DoSomething_Attack
            };
            if (mapParent == null)
            {
                attack.Disable("AerocraftFramework_NothingToLandAt".Translate());
            }
            yield return attack;
            if (LinkToAerocraft.ListCompTransporterPawn.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "AerocraftFramework_Trade_Leave_Label".Translate(),
                    defaultDesc = "AerocraftFramework_Trade_Leave_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/CaravanDown"),
                    action = DoSomething_Trade
                };
            }
            if (crossMap != null && crossMap.Props.DefaultThing_ToRefuel != null && LinkToAerocraft.TryGetComp<CompRefuelable>() != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "AerocraftFramework_Refuel_Label".Translate(),
                    defaultDesc = "AerocraftFramework_Refuel_Desc".Translate(),
                    icon = crossMap.Props.DefaultThing_ToRefuel.uiIcon,
                    action = DoSomething_Refuel
                };
            }
            CompRefuelable refuelable = LinkToAerocraft.TryGetComp<CompRefuelable>();
            if (refuelable != null)
            {
                yield return new Gizmo_RefuelableFuelStatus { refuelable = refuelable };
            }
        }

        public void DoSomething_Move()
        {
            Comp_CanCrossMap crossMap = CrossMapComp;
            if (crossMap == null)
            {
                return;
            }
            float fuelConsumeBase = crossMap.Props.FuelConsumeBase;
            Comp_CanCrossMap.ComputeRanges(LinkToAerocraft.TryGetComp<CompRefuelable>(), fuelConsumeBase, out Fuel, out SafeRange, out NoBackRange);
            Find.WorldSelector.ClearSelection();
            Find.WorldTargeter.BeginTargeting(ChoseWorldTarget, canTargetTiles: true, null, closeWorldTabWhenFinished: false, () =>
            {
                GenDraw.DrawWorldRadiusRing(Tile, SafeRange);
                GenDraw.DrawWorldRadiusRing(Tile, NoBackRange);
            }, target => Comp_CanCrossMap.TargetingLabelGetter(target, Tile, SafeRange, NoBackRange, Fuel, fuelConsumeBase, TravelSpeed));
        }

        public static string TargetingLabelGetter(GlobalTargetInfo Target, int OriginTile, int SafeLaunchDistance, int MaxLaunchDistance, float Fuel, float FuelConsumeSpeedBase)
        {
            return Comp_CanCrossMap.TargetingLabelGetter(Target, OriginTile, SafeLaunchDistance, MaxLaunchDistance, Fuel, FuelConsumeSpeedBase);
        }

        public bool ChoseWorldTarget(GlobalTargetInfo Target)
        {
            if (!Target.IsValid || Target.Tile == Tile)
            {
                return false;
            }
            if (Target.WorldObject == null)
            {
                Messages.Message("AerocraftFramework_ChoseWorldTargetError".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            int distance = Find.WorldGrid.TraversalDistanceBetween(Tile, Target.Tile);
            if (distance > NoBackRange)
            {
                Messages.Message("AerocraftFramework_OutOfRange".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            LinkToAerocraft.TryGetComp<CompRefuelable>()?.ConsumeFuel(distance * (CrossMapComp?.Props.FuelConsumeBase ?? 0f));
            InitialTile = Tile;
            DestinationTile = Target.Tile;
            arrived = false;
            TraveledPct = 0f;
            CameraJumper.TryJump(new GlobalTargetInfo(this));
            return true;
        }

        /// <summary>Lands on the destination map (generating it if needed) near a map corner and flies to its centre.</summary>
        public void DoSomething_Attack()
        {
            if (LinkToAerocraft == null)
            {
                Destroy();
                return;
            }
            MapParent mapParent = Find.WorldObjects.MapParentAt(Tile);
            if (mapParent == null)
            {
                Messages.Message("AerocraftFramework_NothingToLandAt".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            TargetMap = mapParent.Map ?? GetOrGenerateMapUtility.GetOrGenerateMap(Tile, mapParent.def);
            if (TargetMap == null)
            {
                return;
            }
            for (int i = 0; i < AllExtraWeapon.Count; i++)
            {
                if (AllExtraWeapon[i] != null && !LinkToAerocraft.AllExtraWeapon.Contains(AllExtraWeapon[i]))
                {
                    LinkToAerocraft.AllExtraWeapon.Add(AllExtraWeapon[i]);
                }
            }
            AllExtraWeapon.Clear();
            CompTransporter transporter = Transporter;
            if (transporter != null)
            {
                for (int i = 0; i < LinkToAerocraft.ListCompTransporterPawn.Count; i++)
                {
                    transporter.innerContainer.TryAdd(LinkToAerocraft.ListCompTransporterPawn[i]);
                }
                LinkToAerocraft.ListCompTransporterPawn.Clear();
            }
            Building_Aerocraft_AsBaseThing aircraft = LinkToAerocraft;
            LinkToAerocraft = null;
            // It comes in over the edge on the side it flew from and heads for the middle of the map.
            float fromHeading = InitialTile >= 0 && InitialTile != Tile ? Find.WorldGrid.GetHeadingFromTo(Tile, InitialTile) : Rand.Range(0f, 360f);
            Building_Aerocraft_AsBaseThing.ArriveOverEdge(aircraft, TargetMap, fromHeading, TargetMap.Center.ToVector3Shifted());
            Current.Game.CurrentMap = TargetMap;
            CameraJumper.TryJump(aircraft.Position, TargetMap);
            Find.Selector.ClearSelection();
            Find.Selector.Select(aircraft);
            Destroy();
        }

        /// <summary>The transporter passengers leave the aircraft as a caravan (trade, peaceful visits).</summary>
        public void DoSomething_Trade()
        {
            if (LinkToAerocraft == null || LinkToAerocraft.ListCompTransporterPawn.Count <= 0)
            {
                Messages.Message("AerocraftFramework_TradeError".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Caravan caravan = CaravanMaker.MakeCaravan(LinkToAerocraft.ListCompTransporterPawn, Faction.OfPlayer, Tile, addToWorldPawnsIfNotAlready: true);
            LinkToAerocraft.ListCompTransporterPawn.Clear();
            CompTransporter transporter = Transporter;
            if (transporter != null)
            {
                List<Thing> cargo = transporter.innerContainer.ToList();
                for (int i = 0; i < cargo.Count; i++)
                {
                    transporter.innerContainer.Remove(cargo[i]);
                    CaravanInventoryUtility.GiveThing(caravan, cargo[i]);
                }
            }
            Find.WorldSelector.Deselect(this);
            Find.WorldSelector.Select(caravan);
        }

        /// <summary>Refuels the aircraft from fuel carried as transporter cargo.</summary>
        public void DoSomething_Refuel()
        {
            CompRefuelable refuelable = LinkToAerocraft?.TryGetComp<CompRefuelable>();
            CompTransporter transporter = Transporter;
            ThingDef fuelDef = CrossMapComp?.Props.DefaultThing_ToRefuel;
            if (refuelable == null || transporter == null || fuelDef == null)
            {
                return;
            }
            if (refuelable.Fuel >= refuelable.Props.fuelCapacity)
            {
                Messages.Message("AerocraftFramework_Refuel_Message_A".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            List<Thing> fuelStacks = transporter.innerContainer.Where(t => t.def == fuelDef).ToList();
            if (fuelStacks.Count == 0)
            {
                Messages.Message("AerocraftFramework_Refuel_Message_B".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            foreach (Thing stack in fuelStacks)
            {
                float missing = (refuelable.Props.fuelCapacity - refuelable.Fuel) / refuelable.Props.FuelMultiplierCurrentDifficulty;
                int take = Mathf.Min(stack.stackCount, Mathf.CeilToInt(missing));
                if (take <= 0)
                {
                    break;
                }
                refuelable.Refuel(take);
                if (take >= stack.stackCount)
                {
                    transporter.innerContainer.Remove(stack);
                    stack.Destroy();
                }
                else
                {
                    stack.stackCount -= take;
                }
            }
        }

        /// <summary>A caravan at this tile boards the aircraft as transporter passengers.</summary>
        public void DoSomething_GetOn(Caravan caravan)
        {
            CompTransporter transporter = Transporter;
            if (transporter == null)
            {
                Messages.Message((LinkToAerocraft?.Label ?? "") + " " + "AerocraftFramework_TradeError_CannotCarry".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            List<Pawn> pawns = caravan.PawnsListForReading.ToList();
            List<Thing> items = new List<Thing>();
            foreach (Pawn pawn in pawns)
            {
                items.AddRange(pawn.inventory.innerContainer);
            }
            foreach (Thing item in items)
            {
                item.holdingOwner?.Remove(item);
            }
            transporter.innerContainer.TryAddRangeOrTransfer(items);
            foreach (Pawn pawn in pawns)
            {
                caravan.RemovePawn(pawn);
                LinkToAerocraft.ListCompTransporterPawn.Add(pawn);
                if (Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }
            }
            caravan.Destroy();
            Find.WorldSelector.Select(this);
        }
    }
}
