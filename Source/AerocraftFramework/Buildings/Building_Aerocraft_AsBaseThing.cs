using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// The aircraft body: flight model, take-off and landing, pilots, extra weapon mounts.
    /// Public field names and save keys are kept from the original mod.
    /// </summary>
    [StaticConstructorOnStartup]
    public partial class Building_Aerocraft_AsBaseThing : Building_Aerocraft_Base, IThingHolder
    {
        public CompPowerBattery CompPowerBattery;

        private static readonly Material MoveToTargetLineMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(0.5f, 0.7f, 0.6f));
        private static readonly Material ForcedExplosionLineMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f));
        public static readonly Graphic NothingTexture = GraphicDatabase.Get<Graphic_Single>("AerocraftFramework/Nothing/Nothing");

        public bool HaveGoToTarget = true;
        public bool GoToTargetAndDestroy;
        public bool If_CheckInMapBoundaryPos;
        public Vector3 TargetVPos;
        public Thing FollowTargetThing;
        public bool If_GoBackNow;
        public Vector3 TakeOffVPos_A;
        public Vector3 TakeOffVPos_G;
        public bool If_NeedTurnWhenMoving = true;
        public bool If_NeedGlidingWhenTakeOff = true;
        public int Gliding_Range = 2;
        public bool If_TuringByGlidingTakeOffOrDownNow;
        public bool If_GlidingTakeOffNow;
        public bool If_GlidingDownNow;
        public int GlidingTakeOffOrDownTick;
        public int GlidingTakeOffOrDownTickMax = 60;
        public float MoveSpeed_Now;
        public float MoveSpeed_Max;
        public float MoveSpeed_Turning;
        public bool If_CanHover;
        public float AngleChangePerTick_Hover;
        public float AngleChangePerTick_Turning;
        public float AngleChangePerTick_Turning_Origin;
        public bool If_UpOrDown;
        public Texture2D Icon_UpOrDown_Now;
        public string String_UpOrDown_Now;
        public string TakeOffAndLanding_Icon_On_Label;
        public string TakeOffAndLanding_Icon_Off_Label;
        public string TakeOffAndLanding_Icon_Description;
        public string TakeOffAndLanding_Icon_On_IconPath;
        public string TakeOffAndLanding_Icon_Off_IconPath;
        public int Move_WarmUpTick;
        public int Move_WarmUpTickMax = 60;
        public int Check_CollideMoveRangeMax = 5;
        public bool If_CanWrap;
        public EffecterDef Wrap_Effecter_Start;
        public EffecterDef Wrap_Effecter_End;
        public float FuelConsumePerTick = 1f;
        public bool If_DoExplosion_WhenDestroy;
        public bool If_Drop_WhenHitpointZero;
        public int Drop_Range;
        public bool If_DropingNow;

        /// <summary>
        /// Pilots and passengers sitting in the aircraft (despawned). This is the list of <see cref="innerPawns"/>:
        /// read it, but board and let out through <see cref="DoSomething_CarryPawn"/> and <see cref="ReleasePawn"/>.
        /// </summary>
        public List<Pawn> ListPawn;

        /// <summary>
        /// Holds the pilots like a cryptosleep casket holds its sleeper. The original kept them in a plain list, so
        /// the game did not know they were on the map: they vanished from the colonist bar, and a map that has
        /// only aircraft crews left (an occupied or destroyed settlement) was closed with the aircraft on it.
        /// </summary>
        private readonly ThingOwner<Pawn> innerPawns;

        /// <summary>Pawns of the transporter cargo, kept aside while flying between maps.</summary>
        public List<Pawn> ListCompTransporterPawn = new List<Pawn>();

        public int CarryPawnNumMax = 1;
        public bool If_ChangeWeaponByPawnWeaponWhenCarry;
        public bool If_NeedPawnToControl;
        public int NeedPawnToControl_Number = 1;

        /// <summary>Extra weapon mounts riding on this aircraft.</summary>
        public List<Building> AllExtraWeapon = new List<Building>();

        /// <summary>Set by the cross-map travel code just before the aircraft is despawned to leave the map.</summary>
        public bool DepartingCrossMap;

        private bool destroying;
        private bool skipLegacyFollowTarget;
        private List<Pawn> pilotWeaponOwners = new List<Pawn>();
        private List<Thing> pilotWeapons = new List<Thing>();

        public Building_Aerocraft_AsBaseThing()
        {
            innerPawns = new ThingOwner<Pawn>(this, oneStackOnly: false, LookMode.Deep);
            ListPawn = innerPawns.InnerListForReading;
        }

        public override Graphic Graphic => NothingTexture;

        public ThingOwner GetDirectlyHeldThings() => innerPawns;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public bool Is_Uping => Move_WarmUpTick > 0 && Move_WarmUpTick <= Move_WarmUpTickMax && If_UpOrDown;

        public bool Is_Downing => Move_WarmUpTick > 0 && Move_WarmUpTick <= Move_WarmUpTickMax && !If_UpOrDown;

        public bool Is_GlidingTakeOff => GlidingTakeOffOrDownTick >= 0 && GlidingTakeOffOrDownTick <= GlidingTakeOffOrDownTickMax && If_UpOrDown;

        public bool Is_GlidingDown => GlidingTakeOffOrDownTick >= 0 && GlidingTakeOffOrDownTick <= GlidingTakeOffOrDownTickMax && !If_UpOrDown;

        public override bool Is_Flying => Move_WarmUpTick > Move_WarmUpTickMax;

        public override bool Is_Static => Move_WarmUpTick <= 0;

        /// <summary>Pilots must be able to fly: a wounded pawn carried aboard is a passenger.</summary>
        public bool HasEnoughPilots => !If_NeedPawnToControl || CrewCapable.Count() >= NeedPawnToControl_Number;

        /// <summary>Pawns on board who can act (neither downed nor dead), in boarding order.</summary>
        public IEnumerable<Pawn> CrewCapable => ListPawn.Where(p => !p.Dead && !p.Downed);

        public IEnumerable<Building_Aerocraft_Base> AllTurrets
        {
            get
            {
                yield return this;
                for (int i = 0; i < AllExtraWeapon.Count; i++)
                {
                    if (AllExtraWeapon[i] is Building_Aerocraft_Base mount && !mount.Destroyed)
                    {
                        yield return mount;
                    }
                }
            }
        }

        public string FlightStatusLabel
        {
            get
            {
                if (If_DropingNow)
                {
                    return "AerocraftFramework_Status_Crashing".Translate();
                }
                if (Is_Static)
                {
                    return "AerocraftFramework_Status_Landed".Translate();
                }
                if (Is_Flying)
                {
                    return "AerocraftFramework_Status_Flying".Translate();
                }
                return If_UpOrDown ? "AerocraftFramework_Status_TakingOff".Translate() : "AerocraftFramework_Status_Landing".Translate();
            }
        }

        // ------------------------------------------------------------------ lifecycle

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder();
            string inspectString = base.GetInspectString();
            if (!inspectString.NullOrEmpty())
            {
                sb.AppendLine(inspectString);
            }
            sb.AppendLine("AerocraftFramework_Status".Translate() + ": " + FlightStatusLabel);
            for (int i = 0; i < AllExtraWeapon.Count; i++)
            {
                if (AllExtraWeapon[i] is Building_Aerocraft_Base mount && mount.Gun_Now != null)
                {
                    sb.AppendLine("AerocraftFramework_ExtraWeaponName".Translate() + ": " + GunStatusLine(mount.Gun_Now) + (mount.If_BreakDown ? " [" + "AerocraftFramework_AsWeapon_BrokenDown".Translate().ToString() + "]" : ""));
                }
            }
            return sb.ToString().TrimEndNewlines();
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            DepartingCrossMap = false;
            destroying = false;
            CompProperties_MoveToTargetAndHover props = GetComp<Comp_MoveToTargetAndHover>()?.Props;
            if (props != null)
            {
                Move_WarmUpTickMax = props.Move_WarmUpTickMax;
                Check_CollideMoveRangeMax = props.Check_CollideMoveRangeMax;
                If_NeedTurnWhenMoving = props.If_NeedTurnWhenMoving;
                If_NeedGlidingWhenTakeOff = props.If_NeedGlidingWhenTakeOff;
                Gliding_Range = props.Gliding_Range;
                GlidingTakeOffOrDownTickMax = props.GlidingTakeOffOrDownTickMax;
                MoveSpeed_Max = props.MoveSpeed_Max;
                MoveSpeed_Turning = props.MoveSpeed_Turning;
                AngleChangePerTick_Hover = props.AngleChangePerTick_Hover;
                AngleChangePerTick_Turning = props.AngleChangePerTick_Turning;
                AngleChangePerTick_Turning_Origin = props.AngleChangePerTick_Turning;
                If_CanWrap = props.If_CanWrap;
                Wrap_Effecter_Start = props.Wrap_Effecter_Start;
                Wrap_Effecter_End = props.Wrap_Effecter_End;
                FuelConsumePerTick = props.FuelConsumePerTick;
                TakeOffAndLanding_Icon_On_Label = props.TakeOffAndLanding_Icon_On_Label;
                TakeOffAndLanding_Icon_Off_Label = props.TakeOffAndLanding_Icon_Off_Label;
                TakeOffAndLanding_Icon_Description = props.TakeOffAndLanding_Icon_Description;
                TakeOffAndLanding_Icon_On_IconPath = props.TakeOffAndLanding_Icon_On_IconPath;
                TakeOffAndLanding_Icon_Off_IconPath = props.TakeOffAndLanding_Icon_Off_IconPath;
                if (!respawningAfterLoad)
                {
                    If_CanHover = props.Default_HoverSet;
                }
            }
            CompProperties_DoExplosion_BySomeWays explosionProps = GetComp<Comp_DoExplosion_BySomeWays>()?.Props;
            if (explosionProps != null)
            {
                If_DoExplosion_WhenDestroy = explosionProps.If_DoExplosion_WhenDestroy;
                If_Drop_WhenHitpointZero = explosionProps.If_Drop_WhenHitpointZero;
                Drop_Range = explosionProps.Drop_Range;
            }
            CompProperties_CarryPawn carryProps = GetComp<Comp_CarryPawn>()?.Props;
            if (carryProps != null)
            {
                CarryPawnNumMax = carryProps.CarryPawnNumMax;
                If_ChangeWeaponByPawnWeaponWhenCarry = carryProps.If_ChangeWeaponByPawnWeaponWhenCarry;
                If_NeedPawnToControl = carryProps.If_NeedPawnToControl;
                NeedPawnToControl_Number = carryProps.NeedPawnToControl_Number;
            }
            CompPowerBattery = this.TryGetComp<CompPowerBattery>();
            if (!respawningAfterLoad)
            {
                // Arriving from another map: the mounts travelled with the aircraft.
                for (int i = 0; i < AllExtraWeapon.Count; i++)
                {
                    Building mount = AllExtraWeapon[i];
                    if (mount != null && !mount.Spawned && !mount.Destroyed)
                    {
                        GenSpawn.Spawn(mount, Position, map);
                    }
                }
            }
            AllExtraWeapon.RemoveAll(m => m == null || m.Destroyed);
            Check_Texture_Icon_UpOrDown();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref HaveGoToTarget, "HaveGoToTarget", false);
            Scribe_Values.Look(ref GoToTargetAndDestroy, "GoToTargetAndDestroy", false);
            Scribe_Values.Look(ref If_CheckInMapBoundaryPos, "If_CheckInMapBoundaryPos", false);
            Scribe_Values.Look(ref TargetVPos, "TargetVPos");
            LookFollowTarget();
            Scribe_Values.Look(ref If_GoBackNow, "If_GoBackNow", false);
            Scribe_Values.Look(ref TakeOffVPos_A, "TakeOffVPos_A");
            Scribe_Values.Look(ref TakeOffVPos_G, "TakeOffVPos_G");
            Scribe_Values.Look(ref If_NeedTurnWhenMoving, "If_NeedTurnWhenMoving", false);
            Scribe_Values.Look(ref If_NeedGlidingWhenTakeOff, "If_NeedGlidingWhenTakeOff", false);
            Scribe_Values.Look(ref Gliding_Range, "Gliding_Range", 0);
            Scribe_Values.Look(ref If_TuringByGlidingTakeOffOrDownNow, "If_TuringByGlidingTakeOffOrDownNow", false);
            Scribe_Values.Look(ref If_GlidingTakeOffNow, "If_GlidingTakeOffNow", false);
            Scribe_Values.Look(ref If_GlidingDownNow, "If_GlidingDownNow", false);
            Scribe_Values.Look(ref GlidingTakeOffOrDownTick, "GlidingTakeOffOrDownTick", 0);
            Scribe_Values.Look(ref GlidingTakeOffOrDownTickMax, "GlidingTakeOffOrDownTickMax", 0);
            Scribe_Values.Look(ref MoveSpeed_Now, "MoveSpeed_Now", 0f);
            Scribe_Values.Look(ref MoveSpeed_Max, "MoveSpeed_Max", 0f);
            Scribe_Values.Look(ref MoveSpeed_Turning, "MoveSpeed_Turning", 0f);
            Scribe_Values.Look(ref If_CanHover, "If_CanHover", false);
            Scribe_Values.Look(ref AngleChangePerTick_Hover, "AngleChangePerTick_Hover", 0f);
            Scribe_Values.Look(ref AngleChangePerTick_Turning, "AngleChangePerTick_Turning", 0f);
            Scribe_Values.Look(ref AngleChangePerTick_Turning_Origin, "AngleChangePerTick_Turning_Origin", 0f);
            Scribe_Values.Look(ref If_UpOrDown, "If_UpOrDown", false);
            Scribe_Values.Look(ref Move_WarmUpTick, "Move_WarmUpTick", 0);
            Scribe_Values.Look(ref Move_WarmUpTickMax, "Move_WarmUpTickMax", 0);
            Scribe_Values.Look(ref Check_CollideMoveRangeMax, "Check_CollideMoveRangeMax", 0);
            Scribe_Values.Look(ref If_CanWrap, "If_CanWrap", false);
            Scribe_Values.Look(ref FuelConsumePerTick, "FuelConsumePerTick", 0f);
            Scribe_Values.Look(ref If_Drop_WhenHitpointZero, "If_Drop_WhenHitpointZero", false);
            Scribe_Values.Look(ref Drop_Range, "Drop_Range", 0);
            Scribe_Values.Look(ref If_DropingNow, "If_DropingNow", false);
            Scribe_Values.Look(ref CarryPawnNumMax, "CarryPawnNumMax", 0);
            Scribe_Values.Look(ref If_ChangeWeaponByPawnWeaponWhenCarry, "If_ChangeWeaponByPawnWeaponWhenCarry", false);
            LookPawns();
            Scribe_Collections.Look(ref ListCompTransporterPawn, "ListCompTransporterPawn", LookMode.Deep);
            Scribe_Collections.Look(ref pilotWeaponOwners, "pilotWeaponOwners", LookMode.Reference);
            Scribe_Collections.Look(ref pilotWeapons, "pilotWeapons", LookMode.Reference);
            ExposeOperations();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ListPawn.RemoveAll(p => p == null);
                ListCompTransporterPawn = ListCompTransporterPawn ?? new List<Pawn>();
                ListCompTransporterPawn.RemoveAll(p => p == null);
                pilotWeaponOwners = pilotWeaponOwners ?? new List<Pawn>();
                pilotWeapons = pilotWeapons ?? new List<Thing>();
                if (pilotWeaponOwners.Count != pilotWeapons.Count)
                {
                    pilotWeaponOwners.Clear();
                    pilotWeapons.Clear();
                }
                if (FollowTargetThing == this)
                {
                    FollowTargetThing = null;
                }
            }
        }

        /// <summary>
        /// The pawns keep the original save format (a deep-saved list under "ListPawn"), so saves stay compatible
        /// both ways; on load they are put back into the container, as <see cref="ThingOwner{T}"/> does itself.
        /// </summary>
        private void LookPawns()
        {
            List<Pawn> pawns = ListPawn;
            Scribe_Collections.Look(ref pawns, "ListPawn", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && pawns != ListPawn)
            {
                ListPawn.Clear();
                if (pawns != null)
                {
                    foreach (Pawn pawn in pawns)
                    {
                        if (pawn != null)
                        {
                            ListPawn.Add(pawn);
                            pawn.holdingOwner = innerPawns;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The followed thing is saved as a reference. The original mod deep-saved it, which nests a full copy of the
        /// followed aircraft (often the aircraft itself) into the save at every save; such legacy nodes are skipped.
        /// </summary>
        private void LookFollowTarget()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                XmlNode node = Scribe.loader.curXmlParent?["FollowTargetThing"];
                skipLegacyFollowTarget = node != null && node.ChildNodes.Cast<XmlNode>().Any(n => n.NodeType == XmlNodeType.Element);
                if (skipLegacyFollowTarget)
                {
                    FollowTargetThing = null;
                    Log.Warning($"[Aerocraft Framework] {ThingID}: dropped a legacy deep-saved follow target (it made saves grow on every save).");
                    return;
                }
            }
            else if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs && skipLegacyFollowTarget)
            {
                return;
            }
            Scribe_References.Look(ref FollowTargetThing, "FollowTargetThing");
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (If_DropingNow)
            {
                // Crashing: destroyed on impact by Check_GoToTarget.
                return;
            }
            destroying = true;
            if (Spawned)
            {
                ReleaseAllPawns(drafted: false);
            }
            for (int i = AllExtraWeapon.Count - 1; i >= 0; i--)
            {
                (AllExtraWeapon[i] as Building_Aerocraft_AsWeapon)?.DestroyWithParent();
            }
            AllExtraWeapon.Clear();
            base.Destroy(mode);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            if (DepartingCrossMap)
            {
                TryDefeatSettlementOnDeparture(map);
                for (int i = 0; i < AllExtraWeapon.Count; i++)
                {
                    if (AllExtraWeapon[i] != null && AllExtraWeapon[i].Spawned)
                    {
                        AllExtraWeapon[i].DeSpawn();
                    }
                }
            }
            else if (!destroying)
            {
                // Minified or otherwise removed: let everyone out and rebuild the mounts when it is placed again.
                ReleaseAllPawns(drafted: false);
                for (int i = AllExtraWeapon.Count - 1; i >= 0; i--)
                {
                    (AllExtraWeapon[i] as Building_Aerocraft_AsWeapon)?.DestroyWithParent();
                }
                AllExtraWeapon.Clear();
                foreach (Comp_GetExtraWeapon comp in GetComps<Comp_GetExtraWeapon>())
                {
                    comp.If_Spawn = false;
                }
            }
            base.DeSpawn(mode);
        }

        /// <summary>Leaving a hostile settlement map with nobody else there counts as defeating it (original behaviour).</summary>
        private void TryDefeatSettlementOnDeparture(Map map)
        {
            if (map == null || this.TryGetComp<Comp_CanCrossMap>() == null || map.ParentFaction == Faction.OfPlayer || !(map.Parent is Settlement settlement))
            {
                return;
            }
            if (settlement.Faction == null || !settlement.Faction.HostileTo(Faction.OfPlayer) || !IsDefeated(map, settlement.Faction))
            {
                return;
            }
            IdeoUtility.Notify_PlayerRaidedSomeone(map.mapPawns.FreeColonistsSpawned);
            DestroyedSettlement destroyedSettlement = (DestroyedSettlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.DestroyedSettlement);
            destroyedSettlement.Tile = settlement.Tile;
            destroyedSettlement.SetFaction(settlement.Faction);
            Find.WorldObjects.Add(destroyedSettlement);
            TimedDetectionRaids timedRaids = destroyedSettlement.GetComponent<TimedDetectionRaids>();
            timedRaids.CopyFrom(settlement.GetComponent<TimedDetectionRaids>());
            timedRaids.SetNotifiedSilently();
            StringBuilder sb = new StringBuilder();
            sb.Append("LetterFactionBaseDefeated".Translate(settlement.Label, timedRaids.DetectionCountdownTimeLeftString));
            if (!HasAnyOtherBase(settlement))
            {
                settlement.Faction.defeated = true;
                sb.AppendLine();
                sb.AppendLine();
                sb.Append("LetterFactionBaseDefeated_FactionDestroyed".Translate(settlement.Faction.Name));
            }
            foreach (Faction faction in Find.FactionManager.AllFactions)
            {
                if (!faction.Hidden && !faction.IsPlayer && faction != settlement.Faction && faction.HostileTo(settlement.Faction))
                {
                    FactionRelationKind previous = faction.PlayerRelationKind;
                    Faction.OfPlayer.TryAffectGoodwillWith(faction, 20, canSendMessage: false, canSendHostilityLetter: false, HistoryEventDefOf.DestroyedEnemyBase);
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.Append("RelationsWith".Translate(faction.Name) + ": " + 20.ToStringWithSign());
                    faction.TryAppendRelationKindChangedInfo(sb, previous, faction.PlayerRelationKind);
                }
            }
            Find.LetterStack.ReceiveLetter("LetterLabelFactionBaseDefeated".Translate(), sb.ToString(), LetterDefOf.PositiveEvent, new GlobalTargetInfo(settlement.Tile), settlement.Faction);
            map.info.parent = destroyedSettlement;
            settlement.Destroy();
            if (map.mapPawns.FreeColonists.Any())
            {
                TaleRecorder.RecordTale(TaleDefOf.CaravanAssaultSuccessful, map.mapPawns.FreeColonists.RandomElement());
            }
        }

        private static bool HasAnyOtherBase(Settlement defeatedFactionBase)
        {
            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                if (settlements[i].Faction == defeatedFactionBase.Faction && settlements[i] != defeatedFactionBase)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsDefeated(Map map, Faction faction)
        {
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is Building_Aerocraft_AsBaseThing && buildings[i] != this)
                {
                    return false;
                }
            }
            if (map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Count > 0)
            {
                return false;
            }
            List<Pawn> pawns = map.mapPawns.SpawnedPawnsInFaction(faction);
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i].RaceProps.Humanlike && GenHostility.IsActiveThreatToPlayer(pawns[i]))
                {
                    return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ tick

        public override void Tick()
        {
            base.Tick();
            if (!Spawned)
            {
                return;
            }
            OperationsTick();
            Hover();
            MoveToTarget();
            Check_GoToTarget();
            if (!Spawned)
            {
                return;
            }
            if (Is_Flying)
            {
                RealCurrentPosition = RealCurrentPosition.Moved(Angle_Fly_Now, MoveSpeed_Now);
            }
            if (Active)
            {
                if (FollowTargetThing != null)
                {
                    if (FollowTargetThing.Spawned && FollowTargetThing.Map == Map && FollowTargetThing != this)
                    {
                        TargetVPos = FollowTargetThing.DrawPos;
                    }
                    else
                    {
                        FollowTargetThing = null;
                    }
                }
                if (!Is_Static && RefuelableComp != null)
                {
                    RefuelableComp.ConsumeFuel(FuelConsumePerTick);
                    if (RefuelableComp.Fuel <= 1f)
                    {
                        ForceLanding();
                    }
                }
                if (!Is_Static && CompPowerBattery != null)
                {
                    float storedEnergyPct = (CompPowerBattery.StoredEnergy - FuelConsumePerTick) / CompPowerBattery.Props.storedEnergyMax;
                    CompPowerBattery.SetStoredEnergyPct(Mathf.Clamp01(storedEnergyPct));
                    if (CompPowerBattery.StoredEnergy <= 1f)
                    {
                        ForceLanding();
                    }
                }
            }
            Change_Position();
            Change_DrawScaleWhenWarmupOrGliding();
            Check_Texture_Icon_UpOrDown();
            Check_MoveWarmUpNow();
            Check_GlidingTakeOffOrDownNow();
        }

        /// <summary>Out of fuel, stunned or badly hit: land right away (helicopters) or glide down ahead (fixed wing).</summary>
        public void ForceLanding()
        {
            if (!If_UpOrDown)
            {
                return;
            }
            if (!If_NeedGlidingWhenTakeOff)
            {
                Change_Down();
            }
            else
            {
                Set_TargetVPos_Down_GlidingTakeOff(AerocraftUtility.FindEmergencyLandingSpot(this, Drop_Range));
            }
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (!HaveGoToTarget)
            {
                Vector3 drawPos = DrawPos;
                Vector3 targetVPos = TargetVPos;
                drawPos.y = targetVPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                GenDraw.DrawLineBetween(drawPos, targetVPos, GoToTargetAndDestroy ? ForcedExplosionLineMat : MoveToTargetLineMat);
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (!IsControllable)
            {
                yield break;
            }
            Check_Texture_Icon_UpOrDown();
            yield return new Command_Action
            {
                defaultLabel = String_UpOrDown_Now,
                defaultDesc = TakeOffAndLanding_Icon_Description,
                icon = Icon_UpOrDown_Now ?? BaseContent.BadTex,
                hotKey = KeyBindingDefOf.Command_ColonistDraft,
                action = Change_UpOrDown
            };
            yield return new Command_Action
            {
                defaultLabel = "AerocraftFramework_GoBack_Label".Translate(),
                defaultDesc = "AerocraftFramework_GoBack_Desc".Translate(),
                icon = MYDE_TexButton.GoBack,
                hotKey = KeyBindingDefOf.Misc5,
                action = Set_GoBack
            };
            if (Is_Static && ListPawn.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "AerocraftFramework_ReleaseAll_Label".Translate(),
                    defaultDesc = "AerocraftFramework_ReleaseAll_Desc".Translate(),
                    icon = MYDE_TexButton.DownByDraft,
                    action = () => ReleaseAllPawns(drafted: true)
                };
            }
            foreach (Gizmo gizmo in GetOperationGizmos())
            {
                yield return gizmo;
            }
            if (!Is_Static)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = Angle_Fly_Now.ToString("0") + "°",
                defaultDesc = "AerocraftFramework_SetAngle_Desc".Translate() + ": " + Angle_Fly_Now.ToString("0"),
                icon = MYDE_TexButton.Right,
                iconAngle = Angle_Fly_Now,
                action = () =>
                {
                    Func<int, string> textGetter = x => "AerocraftFramework_SetAngle_Label".Translate(x);
                    Find.WindowStack.Add(new Dialog_Slider_Aerocraft(textGetter, -180, 180, value =>
                    {
                        foreach (object selected in Find.Selector.SelectedObjects)
                        {
                            if (selected is Building_Aerocraft_AsBaseThing aircraft && aircraft.Is_Static)
                            {
                                aircraft.Angle_Fly_Now = value;
                            }
                        }
                    }, (int)Angle_Fly_Now));
                }
            };
        }

        /// <summary>
        /// A gizmo for each weapon (its own gun and every mount), so the mounts never need to be selected, and orders
        /// for all of them at once.
        /// </summary>
        public override IEnumerable<Gizmo> GetWeaponGizmos()
        {
            List<Building_Aerocraft_Base> turrets = AllTurrets.Where(t => t.Gun_Now != null).ToList();
            bool several = turrets.Count > 1;
            if (several)
            {
                yield return new Command_AerocraftAttackAll(turrets);
            }
            foreach (Building_Aerocraft_Base turret in turrets)
            {
                Command_AerocraftWeapon weapon = new Command_AerocraftWeapon(turret);
                if (!several && turret == this)
                {
                    weapon.hotKey = KeyBindingDefOf.Misc4;
                }
                yield return weapon;
            }
            if (several)
            {
                if (turrets.Any(t => t.ForcedTarget.IsValid))
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "AerocraftFramework_StopAll_Label".Translate(),
                        defaultDesc = "AerocraftFramework_StopAll_Desc".Translate(),
                        icon = AerocraftWeaponOrders.HaltIcon,
                        hotKey = KeyBindingDefOf.Misc5,
                        Order = -95.5f,
                        action = () =>
                        {
                            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                            AerocraftWeaponOrders.StopAttacking(AllTurrets);
                        }
                    };
                }
                yield return new Command_Toggle
                {
                    defaultLabel = "AerocraftFramework_HoldFireAll_Label".Translate(),
                    defaultDesc = "AerocraftFramework_HoldFireAll_Desc".Translate(),
                    icon = AerocraftWeaponOrders.HoldFireIcon,
                    hotKey = KeyBindingDefOf.Misc6,
                    Order = -95f,
                    isActive = () => AllTurrets.All(t => t.HoldFire),
                    toggleAction = () => AerocraftWeaponOrders.SetHoldFire(AllTurrets.ToList(), !AllTurrets.All(t => t.HoldFire))
                };
            }
            if (AerocraftCompat.Ammo.UsesAmmo && turrets.Any(AerocraftWeaponOrders.HasMagazine))
            {
                yield return new Command_Action
                {
                    defaultLabel = "AerocraftFramework_ReloadAll_Label".Translate(),
                    defaultDesc = "AerocraftFramework_ReloadAll_Desc".Translate(),
                    icon = MYDE_TexButton.Reload,
                    Order = -94f,
                    action = () => AerocraftWeaponOrders.OrderReload(AllTurrets, this)
                };
            }
        }

        // ------------------------------------------------------------------ damage

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PreApplyDamage(ref dinfo, out absorbed);
            if (If_DropingNow)
            {
                absorbed = true;
            }
            if (absorbed || !Is_Flying)
            {
                return;
            }
            if (dinfo.Def.isExplosive)
            {
                dinfo.SetAmount(dinfo.Amount * 0.2f);
            }
            else if (dinfo.Def.defName == "Stun" || dinfo.Def.defName == "EMP")
            {
                ForceLanding();
            }
            else if (!dinfo.Def.isRanged)
            {
                absorbed = true;
            }
        }

        public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            if (!Destroyed && HitPoints <= 10 && If_DoExplosion_WhenDestroy)
            {
                if (If_Drop_WhenHitpointZero)
                {
                    if (!Is_Flying)
                    {
                        HitPoints = 0;
                        Destroy(DestroyMode.KillFinalize);
                        return;
                    }
                    if (!If_DropingNow)
                    {
                        Set_TargetVPos_Droping(AerocraftUtility.FindEmergencyLandingSpot(this, Drop_Range));
                    }
                    return;
                }
                If_DropingNow = true;
            }
            if (IsStunned)
            {
                ForceLanding();
            }
            base.PostApplyDamage(dinfo, totalDamageDealt);
        }

        public override void ToggleHoldFire()
        {
            base.ToggleHoldFire();
            for (int i = 0; i < AllExtraWeapon.Count; i++)
            {
                if (AllExtraWeapon[i] is Building_Aerocraft_Base mount)
                {
                    mount.HoldFire = HoldFire;
                    if (mount.HoldFire)
                    {
                        mount.ResetForcedTarget();
                    }
                }
            }
        }

        // ------------------------------------------------------------------ flight model (unchanged from the original)

        public void Set_FlyAngleEnd()
        {
            Angle_Fly_End = (TargetVPos - DrawPos).ToAngleFlat();
            Anti_Angle_Fly_End = Angle_Fly_End - 180f;
            if (Angle_Fly_End < 0f)
            {
                Anti_Angle_Fly_End = Angle_Fly_End + 180f;
            }
        }

        public void MoveToTarget()
        {
            if (HaveGoToTarget || !Is_Flying)
            {
                return;
            }
            if (!If_NeedTurnWhenMoving)
            {
                MoveSpeed_Now = MoveSpeed_Max;
                Angle_Fly_Now = (TargetVPos - DrawPos).ToAngleFlat();
                return;
            }
            Set_FlyAngleEnd();
            float angleDiff = Math.Abs(Angle_Fly_Now - Angle_Fly_End);
            AngleChangePerTick_Turning = If_CheckInMapBoundaryPos ? 2f * AngleChangePerTick_Turning_Origin : AngleChangePerTick_Turning_Origin;
            const float tolerance = 0.5f;
            if (angleDiff <= AngleChangePerTick_Turning + tolerance)
            {
                MoveSpeed_Now = MoveSpeed_Max;
                if (If_NeedGlidingWhenTakeOff && If_TuringByGlidingTakeOffOrDownNow)
                {
                    if (If_UpOrDown)
                    {
                        If_GlidingTakeOffNow = true;
                    }
                    else
                    {
                        If_GlidingDownNow = true;
                    }
                }
                return;
            }
            if (Angle_Fly_End < 0f)
            {
                if (Angle_Fly_Now >= 0f)
                {
                    if (Angle_Fly_Now <= Anti_Angle_Fly_End)
                    {
                        Angle_Fly_Now -= AngleChangePerTick_Turning;
                    }
                    else
                    {
                        Angle_Fly_Now += AngleChangePerTick_Turning;
                        if (Angle_Fly_Now > 180f)
                        {
                            Angle_Fly_Now -= 360f;
                        }
                    }
                }
                else if (Angle_Fly_Now >= Angle_Fly_End)
                {
                    Angle_Fly_Now -= AngleChangePerTick_Turning;
                }
                else
                {
                    Angle_Fly_Now += AngleChangePerTick_Turning;
                }
            }
            else if (Angle_Fly_Now >= 0f)
            {
                if (Angle_Fly_Now <= Angle_Fly_End)
                {
                    Angle_Fly_Now += AngleChangePerTick_Turning;
                }
                else
                {
                    Angle_Fly_Now -= AngleChangePerTick_Turning;
                }
            }
            else if (Angle_Fly_Now >= Anti_Angle_Fly_End)
            {
                Angle_Fly_Now += AngleChangePerTick_Turning;
            }
            else
            {
                Angle_Fly_Now -= AngleChangePerTick_Turning;
                if (Angle_Fly_Now < -180f)
                {
                    Angle_Fly_Now += 360f;
                }
            }
            MoveSpeed_Now = If_CheckInMapBoundaryPos ? MoveSpeed_Turning / 2f : MoveSpeed_Turning;
        }

        public void Check_MoveWarmUpNow()
        {
            if (If_UpOrDown)
            {
                if (Move_WarmUpTick <= Move_WarmUpTickMax)
                {
                    Move_WarmUpTick++;
                }
            }
            else if (!If_TuringByGlidingTakeOffOrDownNow)
            {
                Move_WarmUpTick--;
                if (Move_WarmUpTick <= 0)
                {
                    Move_WarmUpTick = 0;
                }
            }
            if (!Is_Flying)
            {
                MoveSpeed_Now = 0f;
            }
        }

        public void Check_GlidingTakeOffOrDownNow()
        {
            if (!Is_Flying)
            {
                return;
            }
            if (If_GlidingTakeOffNow)
            {
                GlidingTakeOffOrDownTick = Math.Min(GlidingTakeOffOrDownTick + 1, GlidingTakeOffOrDownTickMax);
            }
            else if (If_GlidingDownNow)
            {
                GlidingTakeOffOrDownTick = Math.Max(GlidingTakeOffOrDownTick - 1, 0);
            }
        }

        public void Check_GoToTarget()
        {
            if (HaveGoToTarget || FollowTargetThing != null)
            {
                return;
            }
            float distance = (DrawPos.ToIntVec3() - TargetVPos.ToIntVec3()).LengthHorizontal;
            if (distance > 0.5f)
            {
                return;
            }
            if (GoToTargetAndDestroy)
            {
                // Kamikaze run or crash: the explosion comp goes off in PostDestroy.
                If_DropingNow = false;
                Destroy(DestroyMode.Vanish);
                return;
            }
            HaveGoToTarget = true;
            ChangPosTick = ChangPosTickMax;
            Change_Position();
            if (!If_GoBackNow)
            {
                Check_Collide();
            }
            if (If_GlidingTakeOffNow)
            {
                If_GlidingTakeOffNow = false;
                If_TuringByGlidingTakeOffOrDownNow = false;
            }
            if (If_GlidingDownNow)
            {
                If_GlidingDownNow = false;
                If_TuringByGlidingTakeOffOrDownNow = false;
                Change_Down();
            }
            if (If_GoBackNow)
            {
                if (!If_NeedGlidingWhenTakeOff)
                {
                    Change_Down();
                    If_GoBackNow = false;
                    return;
                }
                Set_TargetVPos(TakeOffVPos_A);
                If_TuringByGlidingTakeOffOrDownNow = true;
                If_UpOrDown = false;
                If_GoBackNow = false;
            }
        }

        public void Check_Collide()
        {
            List<Thing> thingList = Position.GetThingList(Map);
            for (int i = 0; i < thingList.Count; i++)
            {
                if (thingList[i] is Building_Aerocraft_AsBaseThing && thingList[i] != this)
                {
                    List<IntVec3> cells = MYDE_ModFront.GetPos_Square(Position, Check_CollideMoveRangeMax, Check_CollideMoveRangeMax);
                    cells.RemoveAll(c => !c.InBounds(Map));
                    if (cells.Count > 0)
                    {
                        Set_TargetVPos(cells.RandomElement().ToVector3Shifted());
                    }
                    break;
                }
            }
        }

        public void Change_Position()
        {
            if (Is_Static)
            {
                return;
            }
            ChangPosTick++;
            if (ChangPosTick < ChangPosTickMax)
            {
                return;
            }
            ChangPosTick = 0;
            IntVec3 position = new IntVec3((int)RealCurrentPosition.x, 0, (int)RealCurrentPosition.y);
            if (position.x < 1 + def.size.x || position.x > Map.Size.x - def.size.x - 1 || position.z < 1 + def.size.z || position.z > Map.Size.z - def.size.z - 1)
            {
                return;
            }
            if (position != Position)
            {
                Position = position;
            }
            if (MYDE_AerocraftFramework_Setting.If_CheckMapBoundary)
            {
                if (!If_CheckInMapBoundaryPos)
                {
                    if (Position.InNoBuildEdgeArea(Map))
                    {
                        int r = Check_CollideMoveRangeMax;
                        List<IntVec3> candidates = new List<IntVec3>
                        {
                            Position + new IntVec3(r, 0, r),
                            Position + new IntVec3(-r, 0, r),
                            Position + new IntVec3(-r, 0, -r),
                            Position + new IntVec3(r, 0, -r)
                        };
                        IntVec3 turnBack = candidates.RandomElement();
                        foreach (IntVec3 candidate in candidates)
                        {
                            if (candidate.InBounds(Map) && !candidate.InNoBuildEdgeArea(Map))
                            {
                                turnBack = candidate;
                            }
                        }
                        Set_TargetVPos(turnBack.ToVector3Shifted());
                        If_CheckInMapBoundaryPos = true;
                    }
                }
                else if (!Position.InNoBuildEdgeArea(Map))
                {
                    If_CheckInMapBoundaryPos = false;
                }
            }
            if (CompPowerBattery != null)
            {
                if (PowerConnectionMaker.BestTransmitterForConnector(Position, Map) == null)
                {
                    PowerConnectionMaker.DisconnectFromPowerNet(CompPowerBattery);
                }
                Map.powerNetManager.Notify_ConnectorWantsConnect(CompPowerBattery);
            }
        }

        public void Set_TargetVPos(Vector3 VPos)
        {
            if ((If_NeedGlidingWhenTakeOff && (If_GlidingTakeOffNow || If_GlidingDownNow || If_TuringByGlidingTakeOffOrDownNow)) || If_DropingNow)
            {
                return;
            }
            If_GoBackNow = false;
            HaveGoToTarget = false;
            GoToTargetAndDestroy = false;
            TargetVPos = VPos;
            Angle_Fly_Now = MYDE_ModFront.NormalizeAngle(Angle_Fly_Now);
            Move_ByWrap_TakeOff();
            if (Spawned && TargetVPos.ToIntVec3().InBounds(Map))
            {
                FleckMaker.Static(TargetVPos.ToIntVec3(), Map, FleckDefOf.FeedbackGoto);
            }
            FollowTargetThing = null;
        }

        public void Set_TargetVPos_AndDestroy(Vector3 VPos)
        {
            if (If_DropingNow)
            {
                return;
            }
            HaveGoToTarget = false;
            GoToTargetAndDestroy = true;
            TargetVPos = VPos;
            Angle_Fly_Now = MYDE_ModFront.NormalizeAngle(Angle_Fly_Now);
            Move_ByWrap_Down();
            FollowTargetThing = null;
        }

        public void Set_TargetVPos_Droping(Vector3 VPos)
        {
            If_DropingNow = true;
            HaveGoToTarget = false;
            GoToTargetAndDestroy = true;
            TargetVPos = VPos;
            Angle_Fly_Now = MYDE_ModFront.NormalizeAngle(Angle_Fly_Now);
            Move_ByWrap_Down();
            FollowTargetThing = null;
        }

        public void Set_Target_FollowTargetThing(Thing Thing)
        {
            if (Thing == null || Thing == this || (Thing is Building_Aerocraft_AsWeapon mount && mount.Building_Aerocraft_AsBaseThing == this))
            {
                // Following itself makes no sense (and used to corrupt saves): treat it as a move order.
                if (Thing != null)
                {
                    Set_TargetVPos(Thing.DrawPos);
                }
                return;
            }
            FollowTargetThing = Thing;
            TargetVPos = FollowTargetThing.DrawPos;
            HaveGoToTarget = false;
            Move_ByWrap_Follow();
        }

        public void Set_TargetVPos_Down_GlidingTakeOff(Vector3 VPos)
        {
            If_TuringByGlidingTakeOffOrDownNow = true;
            If_GlidingDownNow = true;
            If_UpOrDown = false;
            HaveGoToTarget = false;
            TargetVPos = VPos;
            Angle_Fly_Now = MYDE_ModFront.NormalizeAngle(Angle_Fly_Now);
            Move_ByWrap_Down();
            FollowTargetThing = null;
        }

        public void Set_GoBack()
        {
            if (!Is_Flying || If_GoBackNow || (If_NeedGlidingWhenTakeOff && (If_GlidingTakeOffNow || If_GlidingDownNow || If_TuringByGlidingTakeOffOrDownNow)))
            {
                return;
            }
            Set_TargetVPos(If_NeedGlidingWhenTakeOff ? TakeOffVPos_G : TakeOffVPos_A);
            If_GoBackNow = true;
        }

        private void TriggerWrapEffect(EffecterDef effecterDef)
        {
            if (effecterDef == null || !Spawned)
            {
                return;
            }
            Effecter effecter = new Effecter(effecterDef) { scale = 1f };
            effecter.Trigger(new TargetInfo(Position, Map), TargetInfo.Invalid);
            effecter.Cleanup();
        }

        private void WrapTo(Vector2 destination)
        {
            RealCurrentPosition = destination;
            IntVec3 cell = new IntVec3((int)destination.x, 0, (int)destination.y);
            if (Spawned && cell.InBounds(Map))
            {
                Position = cell;
            }
        }

        public void Move_ByWrap_TakeOff()
        {
            if (!Is_Flying || !If_CanWrap)
            {
                return;
            }
            TriggerWrapEffect(Wrap_Effecter_Start);
            Angle_Fly_Now = (TargetVPos - DrawPos).ToAngleFlat();
            WrapTo(new Vector2(TargetVPos.x, TargetVPos.z));
            Move_WarmUpTick = Move_WarmUpTickMax;
            TriggerWrapEffect(Wrap_Effecter_End);
        }

        public void Move_ByWrap_Down()
        {
            if (!If_CanWrap)
            {
                return;
            }
            TriggerWrapEffect(Wrap_Effecter_Start);
            Angle_Fly_Now = (TargetVPos - DrawPos).ToAngleFlat();
            WrapTo(new Vector2(TargetVPos.x, TargetVPos.z));
            Move_WarmUpTick = 0;
            TriggerWrapEffect(Wrap_Effecter_End);
        }

        public void Move_ByWrap_Follow()
        {
            if (!Is_Flying || !If_CanWrap)
            {
                return;
            }
            TriggerWrapEffect(Wrap_Effecter_Start);
            Angle_Fly_Now = (TargetVPos - DrawPos).ToAngleFlat();
            WrapTo(new Vector2(TargetVPos.x + Rand.Range(-Check_CollideMoveRangeMax, Check_CollideMoveRangeMax), TargetVPos.z + Rand.Range(-Check_CollideMoveRangeMax, Check_CollideMoveRangeMax)));
            Move_WarmUpTick = Move_WarmUpTickMax;
            FollowTargetThing = null;
            TriggerWrapEffect(Wrap_Effecter_End);
        }

        public void Hover()
        {
            if (!HaveGoToTarget)
            {
                return;
            }
            if (If_CanHover && Is_Flying && !TroopDropActive)
            {
                MoveSpeed_Now = MoveSpeed_Max;
                Angle_Fly_Now = MYDE_ModFront.NormalizeAngle(Angle_Fly_Now + AngleChangePerTick_Hover);
            }
            else if (!If_CanHover || TroopDropActive)
            {
                // A troop drop needs the aircraft still, even one that circles when it arrives.
                MoveSpeed_Now = 0f;
            }
        }

        public void Change_UpOrDown()
        {
            if (!If_UpOrDown && !HasEnoughPilots)
            {
                ThrowNeedPilots();
                return;
            }
            if (If_UpOrDown)
            {
                if (If_NeedGlidingWhenTakeOff)
                {
                    Change_Down_GlidingTakeOff();
                }
                else
                {
                    Change_Down();
                }
            }
            else if (If_NeedGlidingWhenTakeOff)
            {
                Change_Up_GlidingTakeOff();
            }
            else
            {
                Change_Up();
            }
        }

        public void ThrowNeedPilots()
        {
            string text = "AerocraftFramework_NeedPawnToControl".Translate();
            if (NeedPawnToControl_Number > 1)
            {
                text += " " + "AerocraftFramework_NeedPawnToControl_PawnNum".Translate() + ": " + NeedPawnToControl_Number;
            }
            AerocraftUtility.ThrowText(this, text);
        }

        /// <summary>Why the aircraft cannot take off now, or Accepted.</summary>
        public AcceptanceReport CanTakeOff()
        {
            if (RefuelableComp != null && RefuelableComp.Fuel <= 1f)
            {
                return "AerocraftFramework_NeedFuel".Translate();
            }
            if (CompPowerBattery != null && CompPowerBattery.StoredEnergy <= 1f)
            {
                return "AerocraftFramework_NeedEnergy".Translate();
            }
            if (!HasEnoughPilots)
            {
                return "AerocraftFramework_NeedPawnToControl".Translate();
            }
            if (IsStunned)
            {
                return "AerocraftFramework_Stunned".Translate();
            }
            return true;
        }

        public void Change_Up()
        {
            if (RefuelableComp != null && RefuelableComp.Fuel <= 1f)
            {
                AerocraftUtility.ThrowText(this, "AerocraftFramework_NeedFuel".Translate());
            }
            else if (CompPowerBattery != null && CompPowerBattery.StoredEnergy <= 1f)
            {
                AerocraftUtility.ThrowText(this, "AerocraftFramework_NeedEnergy".Translate());
            }
            else if (!IsStunned)
            {
                If_UpOrDown = true;
                TakeOffVPos_A = DrawPos;
            }
        }

        public void Change_Down()
        {
            If_UpOrDown = false;
        }

        /// <summary>The runway a fixed-wing aircraft needs to take off or land towards <paramref name="target"/>, and the blocked cells on it.</summary>
        public void GetGlidingRunway(Vector3 target, List<IntVec3> runway, List<IntVec3> blocked)
        {
            runway.Clear();
            blocked.Clear();
            Map map = Map;
            Vector3 drawPos = DrawPos;
            float angle = (drawPos - target).AngleFlat();
            int halfWidth = Math.Max(1, def.size.x / 2);
            HashSet<IntVec3> cells = new HashSet<IntVec3>();
            for (int i = halfWidth; i < Gliding_Range; i += halfWidth)
            {
                Vector3 point = MYDE_ModFront.GetVector3_By_AngleFlat(drawPos, i, angle);
                if (point.x > map.Size.x || point.x < 0f || point.z > map.Size.z || point.z < 0f)
                {
                    break;
                }
                foreach (IntVec3 cell in MYDE_ModFront.GetPos_Square(point.ToIntVec3(), halfWidth, halfWidth))
                {
                    if (cell.InBounds(map) && cells.Add(cell))
                    {
                        runway.Add(cell);
                    }
                }
            }
            if (map.ParentFaction != Faction.OfPlayer)
            {
                return;
            }
            foreach (IntVec3 cell in runway)
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == this || thing is Filth || (thing is Plant && !thing.def.defName.Contains("Tree")) || thing is Building_Aerocraft_AsWeapon || thing.def.defName == "MUR_SubsurfaceConduit")
                    {
                        continue;
                    }
                    blocked.Add(cell);
                    break;
                }
            }
        }

        private void BeginRunwayTargeting(bool takeOff)
        {
            Map map = Map;
            List<IntVec3> runway = new List<IntVec3>();
            List<IntVec3> blocked = new List<IntVec3>();
            TargetingParameters parms = new TargetingParameters
            {
                canTargetLocations = true,
                validator = target => target.IsValid && target.Cell.InBounds(map)
            };
            Find.Targeter.BeginTargeting(parms, target =>
            {
                GetGlidingRunway(target.CenterVector3, runway, blocked);
                if (blocked.Count > 0)
                {
                    Messages.Message("AerocraftFramework_RunwayBlocked".Translate(), this, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                Vector3 end = MYDE_ModFront.GetVector3_By_AngleFlat(DrawPos, Gliding_Range, (DrawPos - target.CenterVector3).AngleFlat());
                if (takeOff)
                {
                    Set_TargetVPos(end);
                    TakeOffVPos_G = end;
                    If_TuringByGlidingTakeOffOrDownNow = true;
                    Change_Up();
                }
                else
                {
                    Set_TargetVPos(end);
                    If_TuringByGlidingTakeOffOrDownNow = true;
                    If_UpOrDown = false;
                }
            }, target =>
            {
                GetGlidingRunway(target.CenterVector3, runway, blocked);
                GenDraw.DrawFieldEdges(runway, Color.white);
                GenDraw.DrawFieldEdges(blocked, Color.red);
            }, null);
        }

        public void Change_Up_GlidingTakeOff()
        {
            AcceptanceReport canTakeOff = CanTakeOff();
            if (!canTakeOff.Accepted)
            {
                AerocraftUtility.ThrowText(this, canTakeOff.Reason);
                return;
            }
            if (!Is_Static || !Active)
            {
                return;
            }
            BeginRunwayTargeting(takeOff: true);
        }

        public void Change_Down_GlidingTakeOff()
        {
            if (If_GlidingTakeOffNow || If_TuringByGlidingTakeOffOrDownNow)
            {
                return;
            }
            BeginRunwayTargeting(takeOff: false);
        }

        public void Change_DrawScaleWhenWarmupOrGliding()
        {
            float progress;
            if ((Is_Uping || Is_Downing) && !If_NeedGlidingWhenTakeOff)
            {
                progress = (float)Move_WarmUpTick / Move_WarmUpTickMax;
            }
            else if ((Is_GlidingTakeOff || Is_GlidingDown) && If_NeedGlidingWhenTakeOff)
            {
                progress = (float)GlidingTakeOffOrDownTick / GlidingTakeOffOrDownTickMax;
            }
            else
            {
                return;
            }
            Draw_ScaleFactorNow = Math.Min(1f + progress * Draw_ScaleIncreaseFactor_WhenFlying, 1f + Draw_ScaleIncreaseFactor_WhenFlying);
            Draw_Shadow_Base_HeightNow = Math.Min(1f + progress * Draw_ScaleIncreaseFactor_WhenFlying * Draw_Shadow_Base_HeightFactor, 1f + Draw_ScaleIncreaseFactor_WhenFlying * Draw_Shadow_Base_HeightFactor);
        }

        public void Check_Texture_Icon_UpOrDown()
        {
            Icon_UpOrDown_Now = MYDE_TexButton.IconOrDefault(If_UpOrDown ? TakeOffAndLanding_Icon_Off_IconPath : TakeOffAndLanding_Icon_On_IconPath);
            String_UpOrDown_Now = If_UpOrDown ? TakeOffAndLanding_Icon_Off_Label : TakeOffAndLanding_Icon_On_Label;
        }

        /// <summary>Takes off instantly (drones launched from a pack or a carrier).</summary>
        public void Set_Flying()
        {
            AcceptanceReport canTakeOff = CanTakeOff();
            if (!canTakeOff.Accepted)
            {
                AerocraftUtility.ThrowText(this, canTakeOff.Reason);
                return;
            }
            If_UpOrDown = true;
            Move_WarmUpTick = Move_WarmUpTickMax;
            Draw_ScaleFactorNow = 1f + Draw_ScaleIncreaseFactor_WhenFlying;
            Draw_Shadow_Base_HeightNow = 1f + Draw_ScaleIncreaseFactor_WhenFlying * Draw_Shadow_Base_HeightFactor;
            TakeOffVPos_A = DrawPos;
        }

        // ------------------------------------------------------------------ pilots and passengers

        public AcceptanceReport CanBoard(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return false;
            }
            if (!Is_Static)
            {
                return "AerocraftFramework_CannotEnter".Translate() + ": " + "AerocraftFramework_AerocraftIsNotStatic".Translate();
            }
            if (ListPawn.Count >= CarryPawnNumMax)
            {
                return "AerocraftFramework_MaxInnerPawn".Translate();
            }
            return true;
        }

        public void DoSomething_CarryPawn(Pawn Pawn)
        {
            DoSomething_CarryPawn(Pawn, takeWeapon: true);
        }

        /// <summary>
        /// Puts a pawn on board. <paramref name="takeWeapon"/>: with <see cref="If_ChangeWeaponByPawnWeaponWhenCarry"/>,
        /// a boarding pilot's ranged weapon becomes the aircraft's; a wounded passenger keeps his.
        /// </summary>
        public void DoSomething_CarryPawn(Pawn Pawn, bool takeWeapon)
        {
            if (Pawn == null || innerPawns.Contains(Pawn))
            {
                return;
            }
            if (ListPawn.Count >= CarryPawnNumMax)
            {
                AerocraftUtility.ThrowText(this, "AerocraftFramework_MaxInnerPawn".Translate());
                return;
            }
            Map previousMap = Pawn.MapHeld;
            IntVec3 previousCell = Pawn.PositionHeld;
            ThingWithComps weapon = null;
            if (takeWeapon && If_ChangeWeaponByPawnWeaponWhenCarry && Pawn.equipment?.Primary != null && Pawn.equipment.Primary.def.IsRangedWeapon)
            {
                weapon = Pawn.equipment.Primary;
                Pawn.equipment.Remove(weapon);
            }
            if (Pawn.Spawned)
            {
                if (Pawn.carryTracker?.CarriedThing != null)
                {
                    Pawn.carryTracker.TryDropCarriedThing(Pawn.Position, ThingPlaceMode.Near, out _);
                }
                Pawn.jobs?.StopAll();
                Pawn.pather?.StopDead();
                Find.Selector.Deselect(Pawn);
                Pawn.DeSpawn(DestroyMode.Vanish);
            }
            // Also takes a pawn out of whatever held it before (a carrier, a transporter).
            if (!innerPawns.TryAddOrTransfer(Pawn, canMergeWithExistingStacks: false))
            {
                Log.Error($"[Aerocraft Framework] Could not put {Pawn} into {this}.");
                if (!Pawn.Spawned && Pawn.holdingOwner == null && previousMap != null)
                {
                    GenSpawn.Spawn(Pawn, previousCell, previousMap);
                }
                if (weapon != null && Pawn.equipment != null)
                {
                    Pawn.equipment.AddEquipment(weapon);
                }
                return;
            }
            Find.ColonistBar?.MarkColonistsDirty();
            if (weapon != null)
            {
                Change_NowWeapon(weapon);
                pilotWeaponOwners.Add(Pawn);
                pilotWeapons.Add(weapon);
            }
        }

        /// <summary>Lets the first pawn out, drafted (original API).</summary>
        public void DoSomething_ReleasePawn()
        {
            if (ListPawn.Count > 0)
            {
                ReleasePawn(ListPawn[0], drafted: true);
            }
        }

        public void ReleaseAllPawns(bool drafted)
        {
            for (int i = ListPawn.Count - 1; i >= 0; i--)
            {
                ReleasePawn(ListPawn[i], drafted);
            }
        }

        /// <summary>
        /// Lets a pawn out next to the aircraft. In flight, only a hovering aircraft that takes off vertically can
        /// let a pawn rope down, onto an unroofed cell below it.
        /// </summary>
        public bool ReleasePawn(Pawn pawn, bool drafted)
        {
            if (pawn == null || !Spawned || !innerPawns.Contains(pawn))
            {
                return false;
            }
            Map map = Map;
            bool fromAir = !Is_Static;
            if (fromAir && (!CanRopeDown.Accepted || pawn.Downed))
            {
                return false;
            }
            IntVec3 cell;
            if (fromAir)
            {
                if (!CellFinder.TryFindRandomCellNear(DrawPos.ToIntVec3(), map, RopeDownRadius, c => c.Standable(map) && !c.Roofed(map), out cell))
                {
                    return false;
                }
            }
            else if (!CellFinder.TryFindRandomCellNear(Position, map, Math.Max(2, def.size.x / 2 + 1), c => c.Standable(map) && !c.Fogged(map), out cell))
            {
                cell = Position;
            }
            innerPawns.Remove(pawn);
            GenSpawn.Spawn(pawn, cell, map);
            ReturnPilotWeapon(pawn);
            if (drafted && pawn.drafter != null && pawn.IsColonistPlayerControlled && !pawn.Downed)
            {
                pawn.drafter.Drafted = true;
            }
            if (fromAir)
            {
                FleckMaker.ThrowDustPuffThick(cell.ToVector3Shifted(), map, 1.5f, new Color(0.8f, 0.8f, 0.75f));
            }
            else
            {
                SoundDefOf.CryptosleepCasket_Eject.PlayOneShot(new TargetInfo(Position, map));
            }
            return true;
        }

        private void ReturnPilotWeapon(Pawn pawn)
        {
            int index = pilotWeaponOwners.IndexOf(pawn);
            if (index < 0)
            {
                return;
            }
            Thing weapon = pilotWeapons[index];
            pilotWeaponOwners.RemoveAt(index);
            pilotWeapons.RemoveAt(index);
            if (weapon == null || weapon.Destroyed || !AllGuns.Contains(weapon) || !TryRemoveWeapon(weapon))
            {
                return;
            }
            if (pawn.equipment != null && pawn.equipment.Primary == null && weapon is ThingWithComps eq)
            {
                pawn.equipment.AddEquipment(eq);
            }
            else
            {
                GenPlace.TryPlaceThing(weapon, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            }
        }
    }
}
