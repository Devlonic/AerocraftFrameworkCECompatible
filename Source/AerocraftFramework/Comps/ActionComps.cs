using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MYDE_AerocraftFramework
{
    /// <summary>A weapon kit "building" placed on an aircraft: installs its weapon into the aircraft and disappears.</summary>
    [StaticConstructorOnStartup]
    public class Comp_AddableWeapon_Add : ThingComp
    {
        private bool pendingInstall;

        public CompProperties_AddableWeapon_Add Props => props as CompProperties_AddableWeapon_Add;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // Installing right away would destroy the building while it is still being created by the construction job.
            pendingInstall = true;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (pendingInstall && parent.Spawned)
            {
                pendingInstall = false;
                Install();
            }
        }

        private void Install()
        {
            Map map = parent.Map;
            Building_Aerocraft_AsBaseThing aircraft = parent.Position.GetThingList(map).OfType<Building_Aerocraft_AsBaseThing>().FirstOrDefault();
            if (aircraft != null && Props.AddableWeaponDef != null)
            {
                aircraft.Change_NowWeapon(ThingMaker.MakeThing(Props.AddableWeaponDef));
                AerocraftUtility.ThrowText(aircraft, aircraft.LabelShort + ": " + "AerocraftFramework_AddWeapon".Translate() + " " + Props.AddableWeaponDef.label);
            }
            else
            {
                AerocraftUtility.ThrowText(parent, "AerocraftFramework_NoAerocraftInPosition".Translate());
                if (parent.def.costList != null)
                {
                    foreach (ThingDefCountClass cost in parent.def.costList)
                    {
                        Thing refund = ThingMaker.MakeThing(cost.thingDef);
                        refund.stackCount = cost.count;
                        GenPlace.TryPlaceThing(refund, parent.Position, map, ThingPlaceMode.Near);
                    }
                }
            }
            parent.Destroy();
        }
    }

    /// <summary>Carrying pilots and passengers: quick-load gizmo, "enter" order and the inspect line.</summary>
    [StaticConstructorOnStartup]
    public class Comp_CarryPawn : ThingComp
    {
        public CompProperties_CarryPawn Props => props as CompProperties_CarryPawn;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        public override string CompInspectStringExtra()
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (aircraft == null)
            {
                return null;
            }
            string text = "AerocraftFramework_InnerPawnNum".Translate() + ": " + aircraft.ListPawn.Count + " / " + Props.CarryPawnNumMax;
            if (Props.If_NeedPawnToControl)
            {
                text += " (" + "AerocraftFramework_PilotsNeeded".Translate(Props.NeedPawnToControl_Number) + ")";
            }
            return text;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Props.If_ShowFastLordGizmos && Building_Aerocraft_AsBaseThing != null && parent.Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    action = DoSomething_CarryPawn,
                    defaultLabel = Props.Gizmos_CarryPawn_Label,
                    defaultDesc = Props.Gizmos_CarryPawn_Description,
                    icon = MYDE_TexButton.IconOrDefault(Props.Gizmos_CarryPawn_IconPath),
                    onHover = () => GenDraw.DrawFieldEdges(MYDE_ModFront.GetPos_Square(parent.Position, Props.CarryPawn_MaxRange, Props.CarryPawn_MaxRange))
                };
            }
        }

        /// <summary>Loads every player humanlike or mechanoid pawn standing around the aircraft.</summary>
        public void DoSomething_CarryPawn()
        {
            Map map = parent.Map;
            int range = Props.CarryPawn_MaxRange;
            List<Pawn> pawns = new List<Pawn>();
            foreach (IntVec3 cell in MYDE_ModFront.GetPos_Square(parent.Position, range, range))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn pawn && pawn.Faction == Faction.OfPlayer && !pawn.RaceProps.Animal && !pawns.Contains(pawn))
                    {
                        pawns.Add(pawn);
                    }
                }
            }
            foreach (Pawn pawn in pawns)
            {
                if (Building_Aerocraft_AsBaseThing.ListPawn.Count >= Building_Aerocraft_AsBaseThing.CarryPawnNumMax)
                {
                    AerocraftUtility.ThrowText(parent, "AerocraftFramework_MaxInnerPawn".Translate());
                    break;
                }
                Building_Aerocraft_AsBaseThing.DoSomething_CarryPawn(pawn);
            }
        }

        protected virtual string FloatMenuOptionLabel(Pawn pawn)
        {
            return "AerocraftFramework_EnterAerocraft".Translate() + ": " + Building_Aerocraft_AsBaseThing.LabelShort;
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn myPawn)
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (aircraft == null || aircraft.Faction != Faction.OfPlayer || myPawn.RaceProps.Animal)
            {
                yield break;
            }
            AcceptanceReport canBoard = aircraft.CanBoard(myPawn);
            if (!canBoard.Accepted)
            {
                if (!canBoard.Reason.NullOrEmpty())
                {
                    yield return new FloatMenuOption(FloatMenuOptionLabel(myPawn) + " (" + canBoard.Reason + ")", null);
                }
                yield break;
            }
            if (!myPawn.CanReach(aircraft, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(FloatMenuOptionLabel(myPawn) + " (" + "NoPath".Translate() + ")", null);
                yield break;
            }
            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(FloatMenuOptionLabel(myPawn), () =>
            {
                Job job = JobMaker.MakeJob(MYDE_JobDefOf.MYDE_AerocraftFramework_Job_Enter_Building_Aerocraft_AsBaseThing, aircraft);
                myPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }), myPawn, aircraft);
        }
    }

    /// <summary>Lets a colonist install his own ranged weapon into the turret.</summary>
    [StaticConstructorOnStartup]
    public class Comp_ReplaceCurrentWeapon : ThingComp
    {
        public CompProperties_ReplaceCurrentWeapon Props => props as CompProperties_ReplaceCurrentWeapon;

        private Building_Aerocraft_Base Building_Aerocraft_Base => parent as Building_Aerocraft_Base;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Props.If_CanShowGizmosToReplace && AerocraftUtility.IsControllable(parent))
            {
                yield return new Command_Action
                {
                    action = DoSomething_SelectPawnAndLetItReplace,
                    defaultLabel = "AerocraftFramework_SelectPawnAndLetItReplace_Label".Translate(),
                    defaultDesc = "AerocraftFramework_SelectPawnAndLetItReplace_Desc".Translate(),
                    icon = MYDE_TexButton.SelectPawnAndLetItReplace
                };
            }
        }

        private static bool CanInstall(Pawn pawn)
        {
            return pawn != null && pawn.equipment?.Primary != null && pawn.equipment.Primary.def.IsRangedWeapon && !pawn.Downed;
        }

        public void DoSomething_SelectPawnAndLetItReplace()
        {
            TargetingParameters parms = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                validator = target => target.Thing is Pawn pawn && pawn.Faction == parent.Faction && CanInstall(pawn)
            };
            Find.Targeter.BeginTargeting(parms, target =>
            {
                if (target.Pawn != null && CanInstall(target.Pawn))
                {
                    GiveJob(target.Pawn);
                }
            }, null, null, null, null, null, true, null, null);
        }

        private void GiveJob(Pawn pawn)
        {
            Job job = JobMaker.MakeJob(MYDE_JobDefOf.MYDE_AerocraftFramework_Job_ReplaceCurrentWeapon, Building_Aerocraft_Base);
            job.count = 1;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        protected virtual string FloatMenuOptionLabel(Pawn pawn)
        {
            return "AerocraftFramework_ReplaceCurrentWeapon_Replace".Translate() + " " + Building_Aerocraft_Base.LabelShort + " " + "AerocraftFramework_ReplaceCurrentWeapon_CurrentWeapon".Translate() + ": " + (Building_Aerocraft_Base.Gun_Now?.LabelShort ?? "-");
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn myPawn)
        {
            Building_Aerocraft_Base turret = Building_Aerocraft_Base;
            if (turret == null || turret.Faction != Faction.OfPlayer || !CanInstall(myPawn) || !turret.Is_Static)
            {
                yield break;
            }
            if (!myPawn.CanReach(turret, PathEndMode.Touch, Danger.Deadly))
            {
                yield break;
            }
            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(FloatMenuOptionLabel(myPawn), () => GiveJob(myPawn)), myPawn, turret);
        }
    }

    /// <summary>Creates the extra weapon mounts of an aircraft when it is first placed.</summary>
    [StaticConstructorOnStartup]
    public class Comp_GetExtraWeapon : ThingComp
    {
        public bool If_Spawn;

        public CompProperties_GetExtraWeapon Props => props as CompProperties_GetExtraWeapon;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref If_Spawn, "If_Spawn", false);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            SpawnExtraWeapon();
        }

        public void SpawnExtraWeapon()
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (If_Spawn || aircraft == null || Props.ExtraWeaponDef == null)
            {
                return;
            }
            Map map = aircraft.Map;
            float range = Props.ExtraWeapon_Range;
            float angle = aircraft.Angle_Fly_Now + Props.ExtraWeapon_Angle;
            IntVec3 cell = MYDE_ModFront.GetVector3_By_AngleFlat(parent.Position.ToVector3(), range, angle).ToIntVec3().ClampInsideMap(map);
            Thing thing = ThingMaker.MakeThing(Props.ExtraWeaponDef);
            if (thing is Building_Aerocraft_AsWeapon mount)
            {
                mount.Building_Aerocraft_AsBaseThing = aircraft;
                mount.Building_Aerocraft_Base_Range = range;
                mount.Building_Aerocraft_Base_Angle = Props.ExtraWeapon_Angle;
            }
            else
            {
                Log.ErrorOnce($"[Aerocraft Framework] {Props.ExtraWeaponDef.defName} (extra weapon of {parent.def.defName}) must use Building_Aerocraft_AsWeapon.", Props.ExtraWeaponDef.shortHash ^ 0x0E11);
            }
            if (parent.Faction != null)
            {
                thing.SetFactionDirect(parent.Faction);
            }
            GenSpawn.Spawn(thing, cell, map);
            If_Spawn = true;
        }
    }

    /// <summary>Explodes the aircraft when destroyed, forced explosion (kamikaze drones) and self-destruct timers.</summary>
    [StaticConstructorOnStartup]
    public class Comp_DoExplosion_BySomeWays : ThingComp
    {
        public int ExplosionCountDown_Tick;
        public int DrawExplosionChangeTick;
        public bool DrawExplosionTick_UpOrDown;

        public CompProperties_DoExplosion_BySomeWays Props => props as CompProperties_DoExplosion_BySomeWays;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        public override string CompInspectStringExtra()
        {
            if (!Props.If_CountDownToExplosion)
            {
                return null;
            }
            return Props.ShowCountDownToExplosion_Label + ": " + ExplosionCountDown_Tick.ToStringSecondsFromTicks();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ExplosionCountDown_Tick, "ExplosionCountDown_Tick", 0);
            Scribe_Values.Look(ref DrawExplosionChangeTick, "DrawExplosionChangeTick", 0);
            Scribe_Values.Look(ref DrawExplosionTick_UpOrDown, "DrawExplosionTick_UpOrDown", false);
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            ExplosionCountDown_Tick = Props.ExplosionCountDown_TickMax;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Props.If_ForceDoExplosion_Mannable && AerocraftUtility.IsControllable(parent))
            {
                yield return new Command_Action
                {
                    action = DoSomething_ForceDoExplosion,
                    defaultLabel = Props.ForceDoExplosion_Label,
                    defaultDesc = Props.ForceDoExplosion_Description,
                    icon = MYDE_TexButton.IconOrDefault(Props.ForceDoExplosion_IconPath),
                    hotKey = KeyBindingDefOf.Misc7
                };
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (previousMap == null || !Props.If_DoExplosion_WhenDestroy || aircraft == null)
            {
                return;
            }
            if (aircraft.HitPoints <= 0 || aircraft.GoToTargetAndDestroy)
            {
                AerocraftCompat.Ammo.DoExplosion(parent.Position, previousMap, Props, parent);
            }
        }

        public void DoSomething_ForceDoExplosion()
        {
            Map map = parent.Map;
            TargetingParameters parms = new TargetingParameters
            {
                canTargetLocations = true,
                validator = target => target.IsValid && target.Cell.InBounds(map)
            };
            Find.Targeter.BeginTargeting(parms, target =>
            {
                foreach (object selected in Find.Selector.SelectedObjects.ToList())
                {
                    if (selected is Building_Aerocraft_AsBaseThing aircraft && AerocraftUtility.IsControllable(aircraft))
                    {
                        aircraft.Set_TargetVPos_AndDestroy(target.CenterVector3);
                    }
                }
            }, null, null, null, null, null, true, null, null);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!Props.If_CountDownToExplosion || !parent.Spawned)
            {
                return;
            }
            ExplosionCountDown_Tick--;
            if (ExplosionCountDown_Tick <= 0)
            {
                ExplosionCountDown_Tick = Props.ExplosionCountDown_TickMax;
                parent.HitPoints = 0;
                parent.Destroy(DestroyMode.Vanish);
                return;
            }
            if (ExplosionCountDown_Tick > Props.DrawExplosion_BeginTick)
            {
                return;
            }
            if (DrawExplosionTick_UpOrDown)
            {
                DrawExplosionChangeTick++;
                if (DrawExplosionChangeTick >= Props.DrawExplosion_ChangeTickMax)
                {
                    DrawExplosionTick_UpOrDown = false;
                }
            }
            else
            {
                DrawExplosionChangeTick--;
                if (DrawExplosionChangeTick <= 0)
                {
                    DrawExplosionTick_UpOrDown = true;
                }
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (!Props.If_ShowCountDownToExplosionTick || aircraft == null || ExplosionCountDown_Tick > Props.DrawExplosion_BeginTick)
            {
                return;
            }
            Vector3 drawPos = aircraft.DrawPos;
            drawPos.y = aircraft.def.Altitude + aircraft.Draw_Base_ExtraAltitudeLayerNum + 0.1f;
            float scale = Props.DrawExplosion_Scale * aircraft.Draw_ScaleFactorNow;
            Matrix4x4 matrix = default;
            matrix.SetTRS(drawPos, Quaternion.identity, new Vector3(scale, 0f, scale));
            Color color = ExplosionCountDown_Tick <= Props.DrawExplosion_RedTick ? Color.red : Color.yellow;
            color.a = Props.DrawExplosion_ChangeTickMax > 0 ? (float)DrawExplosionChangeTick / Props.DrawExplosion_ChangeTickMax : 1f;
            Graphics.DrawMesh(MeshPool.plane10, matrix, MaterialPool.MatFrom("Things/Mote/BrightFlash", ShaderDatabase.WorldOverlayTransparent, color), 0);
        }
    }

    /// <summary>A manual special shot (firefoam shell of the support aircraft...) with its own reload timer.</summary>
    [StaticConstructorOnStartup]
    public class Comp_ShootSomethingManual : ThingComp
    {
        public string ShootSomething_Label;
        public Texture2D ShootSomething_Icon;
        public int ShootSomething_ReloadTick;

        public CompProperties_ShootSomethingManual Props => props as CompProperties_ShootSomethingManual;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        private bool Ready => ShootSomething_ReloadTick >= Props.ShootSomething_ReloadTickMax;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ShootSomething_ReloadTick, "ShootSomething_ReloadTick", 0);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Building_Aerocraft_AsBaseThing == null || !AerocraftUtility.IsControllable(parent))
            {
                yield break;
            }
            ShootSomething_Label = Ready ? Props.ShootSomething_Label : ShootSomething_ReloadTick + "/" + Props.ShootSomething_ReloadTickMax;
            ShootSomething_Icon = MYDE_TexButton.IconOrDefault(Ready ? Props.ShootSomething_True_IconPath : Props.ShootSomething_False_IconPath);
            Command_Action command = new Command_Action
            {
                action = DoSomething,
                defaultLabel = ShootSomething_Label,
                defaultDesc = Props.ShootSomething_Description,
                icon = ShootSomething_Icon
            };
            if (!Ready)
            {
                command.Disable("AerocraftFramework_Recharging".Translate((Props.ShootSomething_ReloadTickMax - ShootSomething_ReloadTick).ToStringSecondsFromTicks()));
            }
            yield return command;
        }

        public void DoSomething()
        {
            if (!Ready || Props.ShootSomethingDef == null)
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
                if ((aircraft.Position - target.Cell).LengthHorizontal > Props.ShootSomething_Range)
                {
                    Messages.Message("MessageTargetBeyondMaximumRange".Translate(), aircraft, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                aircraft.ChangPosTick = aircraft.ChangPosTickMax;
                aircraft.Change_Position();
                if (AerocraftCompat.Ammo.LaunchProjectile(aircraft, Props.ShootSomethingDef, aircraft.DrawPos, Mathf.Max(1f, parent.def.fillPercent), target, 100f))
                {
                    ShootSomething_ReloadTick = 0;
                }
            }, target =>
            {
                if ((aircraft.Position - target.Cell).LengthHorizontal <= Props.ShootSomething_Range)
                {
                    GenDraw.DrawTargetHighlight(target);
                    float radius = Props.ShootSomethingDef.projectile?.explosionRadius ?? 0f;
                    if (radius > 0f)
                    {
                        GenDraw.DrawRadiusRing(target.Cell, radius);
                    }
                }
                GenDraw.DrawRadiusRing(aircraft.Position, Props.ShootSomething_Range);
            }, null);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (ShootSomething_ReloadTick < Props.ShootSomething_ReloadTickMax)
            {
                ShootSomething_ReloadTick++;
            }
        }
    }
}
