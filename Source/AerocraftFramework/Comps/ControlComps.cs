using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
    /// <summary>Movement settings of the aircraft and the hover toggle.</summary>
    [StaticConstructorOnStartup]
    public class Comp_MoveToTargetAndHover : ThingComp
    {
        public string Hover_Label;
        public Texture2D Hover_Icon;
        private Texture2D iconOn;
        private Texture2D iconOff;

        public CompProperties_MoveToTargetAndHover Props => props as CompProperties_MoveToTargetAndHover;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Props.If_ShowHover_Icon && Building_Aerocraft_AsBaseThing != null && AerocraftUtility.IsControllable(parent))
            {
                UpdateLabel();
                yield return new Command_Toggle
                {
                    defaultLabel = Hover_Label,
                    defaultDesc = Props.Hover_Icon_Description,
                    icon = Hover_Icon ?? BaseContent.BadTex,
                    hotKey = KeyBindingDefOf.Misc2,
                    isActive = () => Building_Aerocraft_AsBaseThing.If_CanHover,
                    toggleAction = DoSomething_ChangeHover
                };
            }
        }

        public void DoSomething_ChangeHover()
        {
            Building_Aerocraft_AsBaseThing.If_CanHover = !Building_Aerocraft_AsBaseThing.If_CanHover;
        }

        private void UpdateLabel()
        {
            if (iconOn == null)
            {
                iconOn = MYDE_TexButton.IconOrDefault(Props.Hover_Icon_On_IconPath);
                iconOff = MYDE_TexButton.IconOrDefault(Props.Hover_Icon_Off_IconPath);
            }
            bool on = Building_Aerocraft_AsBaseThing.If_CanHover;
            Hover_Label = on ? Props.Hover_Icon_On_Label : Props.Hover_Icon_Off_Label;
            Hover_Icon = on ? iconOn : iconOff;
        }
    }

    /// <summary>Automatic hunting mode of drones: fly to the nearest enemy, or home when there is none.</summary>
    [StaticConstructorOnStartup]
    public class Comp_AutoFindTarget : ThingComp
    {
        public bool If_CanFindTargetNow;
        public int FindTargetTick;
        public string FindTarget_Label;
        public Texture2D FindTarget_Icon;
        private Texture2D iconOn;
        private Texture2D iconOff;

        public CompProperties_AutoFindTarget Props => props as CompProperties_AutoFindTarget;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref If_CanFindTargetNow, "If_CanFindTargetNow", false);
            Scribe_Values.Look(ref FindTargetTick, "FindTargetTick", 0);
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            If_CanFindTargetNow = Props.Default_FindTargetSet;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Building_Aerocraft_AsBaseThing != null && AerocraftUtility.IsControllable(parent))
            {
                if (iconOn == null)
                {
                    iconOn = MYDE_TexButton.IconOrDefault(Props.FindTarget_Icon_On_IconPath);
                    iconOff = MYDE_TexButton.IconOrDefault(Props.FindTarget_Icon_Off_IconPath);
                }
                FindTarget_Label = If_CanFindTargetNow ? Props.FindTarget_Icon_On_Label : Props.FindTarget_Icon_Off_Label;
                FindTarget_Icon = If_CanFindTargetNow ? iconOn : iconOff;
                yield return new Command_Toggle
                {
                    defaultLabel = FindTarget_Label,
                    defaultDesc = Props.FindTarget_Icon_Description,
                    icon = FindTarget_Icon,
                    isActive = () => If_CanFindTargetNow,
                    toggleAction = DoSomething_ChangeFindTargetNow
                };
            }
        }

        public void DoSomething_ChangeFindTargetNow()
        {
            If_CanFindTargetNow = !If_CanFindTargetNow;
        }

        public override void CompTick()
        {
            base.CompTick();
            FindTarget();
        }

        public void FindTarget()
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (!If_CanFindTargetNow || aircraft == null || !parent.Spawned || aircraft.CurrentTarget.IsValid || aircraft.If_DropingNow)
            {
                return;
            }
            if (aircraft.Gun_Now != null && !AerocraftCompat.Ammo.CanFireNow(aircraft.Gun_Now))
            {
                return;
            }
            FindTargetTick++;
            if (FindTargetTick < Props.FindTargetTickMax)
            {
                return;
            }
            FindTargetTick = 0;
            Map map = parent.Map;
            IntVec3 position = aircraft.Position;
            Pawn best = null;
            float bestDistance = 500f;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.Faction == null || !pawn.Faction.HostileTo(parent.Faction) || pawn.Downed || pawn.IsPrisoner || pawn.IsSlave || !pawn.Position.InBounds(map))
                {
                    continue;
                }
                RoofDef roof = map.roofGrid.RoofAt(pawn.Position);
                if (roof != null && roof.isThickRoof)
                {
                    continue;
                }
                float distance = (position - pawn.Position).LengthHorizontal;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pawn;
                }
            }
            if (best != null)
            {
                if (!aircraft.If_NeedGlidingWhenTakeOff)
                {
                    if (aircraft.Is_Static)
                    {
                        aircraft.Change_Up();
                    }
                    aircraft.Set_TargetVPos(best.DrawPos);
                    return;
                }
                aircraft.Set_TargetVPos(best.DrawPos);
                if (!aircraft.Is_Flying)
                {
                    aircraft.TakeOffVPos_G = best.DrawPos;
                    aircraft.If_TuringByGlidingTakeOffOrDownNow = true;
                    aircraft.Change_Up();
                }
                return;
            }
            if (parent.Faction != Faction.OfPlayer)
            {
                Building target = null;
                float targetDistance = 500f;
                List<Building> buildings = map.listerBuildings.allBuildingsColonist;
                for (int i = 0; i < buildings.Count; i++)
                {
                    if (buildings[i].def.building != null && buildings[i].def.building.ai_combatDangerous)
                    {
                        float distance = (position - buildings[i].Position).LengthHorizontal;
                        if (distance < targetDistance)
                        {
                            targetDistance = distance;
                            target = buildings[i];
                        }
                    }
                }
                if (target != null)
                {
                    aircraft.Change_Up();
                    aircraft.Set_TargetVPos(target.DrawPos);
                }
            }
            else
            {
                aircraft.Set_GoBack();
            }
        }
    }

    /// <summary>Engine sound while the aircraft is not landed.</summary>
    [StaticConstructorOnStartup]
    public class Comp_SpawnSound : ThingComp
    {
        public float Sound_MakeSoundTick;

        public CompProperties_SpawnSound Props => props as CompProperties_SpawnSound;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        public override void CompTick()
        {
            base.CompTick();
            if (Building_Aerocraft_AsBaseThing != null && parent.Spawned && !Building_Aerocraft_AsBaseThing.Is_Static)
            {
                Tick_SpawnSound();
            }
        }

        public void Tick_SpawnSound()
        {
            if (Props.SoundDef == null)
            {
                return;
            }
            Sound_MakeSoundTick++;
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            float warmup = aircraft.Move_WarmUpTickMax > 0 ? (float)aircraft.Move_WarmUpTick / aircraft.Move_WarmUpTickMax : 1f;
            float interval = Props.Sound_MakeSoundkTick_Max - warmup * Props.Sound_MakeSoundkTick_Add;
            if (Sound_MakeSoundTick >= interval)
            {
                Sound_MakeSoundTick = 0f;
                Props.SoundDef.PlayOneShot(new TargetInfo(aircraft.Position, aircraft.Map));
            }
        }
    }

    /// <summary>Smoke, heat glow and other flecks around the aircraft.</summary>
    [StaticConstructorOnStartup]
    public class Comp_SpawnFleck : ThingComp
    {
        public float Fleck_MakeFleck_AddNumTick;
        public int Fleck_MakeFleckTick;
        public int Fleck_MakeFleckNum;

        public CompProperties_SpawnFleck Props => props as CompProperties_SpawnFleck;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref Fleck_MakeFleck_AddNumTick, "Fleck_MakeFleck_AddNumTick", 0f);
            Scribe_Values.Look(ref Fleck_MakeFleckTick, "Fleck_MakeFleckTick", 0);
            Scribe_Values.Look(ref Fleck_MakeFleckNum, "Fleck_MakeFleckNum", 0);
        }

        public override void CompTick()
        {
            base.CompTick();
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (aircraft == null || !parent.Spawned)
            {
                return;
            }
            if (Props.If_SpawnEffectOrFleckOnlyStatic)
            {
                if (aircraft.Is_Static)
                {
                    Tick_SpawnFleck();
                }
            }
            else if (aircraft.Is_Uping)
            {
                Tick_SpawnFleck();
                AddSpawnNum();
            }
            else if (aircraft.Is_Flying)
            {
                Tick_SpawnFleck();
            }
            else if (aircraft.Is_Downing)
            {
                Tick_SpawnFleck();
                ReduceSpawnNum();
            }
        }

        public void Tick_SpawnFleck()
        {
            if (parent.Destroyed || Props.FleckDef == null)
            {
                return;
            }
            Fleck_MakeFleckTick++;
            if (Fleck_MakeFleckTick < Props.Fleck_MakeFleckTickMax)
            {
                return;
            }
            Fleck_MakeFleckTick = 0;
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            Map map = aircraft.Map;
            float range = Props.Fleck_Range_ToSetPosition * aircraft.Draw_ScaleFactorNow;
            float angle = Props.Fleck_Angle_ToSetPosition + aircraft.Angle_Fly_Now;
            Vector3 pos = MYDE_ModFront.GetVector3_By_AngleFlat(aircraft.DrawPos, range, angle);
            if (!pos.ShouldSpawnMotesAt(map))
            {
                return;
            }
            int count = Props.Fleck_MakeFleckNum_Origin + Math.Min(Props.Fleck_MakeFleckNum_Max, Fleck_MakeFleckNum);
            for (int i = 0; i < count; i++)
            {
                float velocityAngle = Props.Fleck_Angle.RandomInRange;
                if (Props.Fleck_If_FollowBaseThingAngle)
                {
                    velocityAngle += aircraft.Angle_Fly_Now - 90f;
                }
                FleckCreationData data = FleckMaker.GetDataStatic(pos, map, Props.FleckDef, Props.Fleck_Scale.RandomInRange);
                data.rotationRate = Props.Fleck_Rotation.RandomInRange;
                data.velocityAngle = velocityAngle;
                data.velocitySpeed = Props.Fleck_Speed.RandomInRange;
                map.flecks.CreateFleck(data);
            }
        }

        public void AddSpawnNum()
        {
            if (!Props.If_Fleck_Addable)
            {
                return;
            }
            Fleck_MakeFleck_AddNumTick++;
            if (Fleck_MakeFleck_AddNumTick >= Props.Fleck_MakeFleck_AddNumTickMax)
            {
                Fleck_MakeFleck_AddNumTick = 0f;
                Fleck_MakeFleckNum = Math.Min(Fleck_MakeFleckNum + 1, Props.Fleck_MakeFleckNum_Max);
            }
        }

        public void ReduceSpawnNum()
        {
            if (!Props.If_Fleck_Addable)
            {
                return;
            }
            Fleck_MakeFleck_AddNumTick++;
            if (Fleck_MakeFleck_AddNumTick >= Props.Fleck_MakeFleck_AddNumTickMax)
            {
                Fleck_MakeFleck_AddNumTick = 0f;
                Fleck_MakeFleckNum = Math.Max(Fleck_MakeFleckNum - 1, 0);
            }
        }
    }

    /// <summary>Smoke trail of a projectile (works for vanilla and Combat Extended projectiles).</summary>
    [StaticConstructorOnStartup]
    public class Comp_SpawnFleck_Projectile : ThingComp
    {
        public int Fleck_MakeFleckTick;
        private Vector3 lastPos;

        public CompProperties_SpawnFleck_Projectile Props => props as CompProperties_SpawnFleck_Projectile;

        public override void CompTick()
        {
            base.CompTick();
            Tick_SpawnFleck();
        }

        public void Tick_SpawnFleck()
        {
            Vector3 drawPos = parent.DrawPos;
            Vector3 previous = lastPos;
            lastPos = drawPos;
            Fleck_MakeFleckTick++;
            if (Fleck_MakeFleckTick < Props.Fleck_MakeFleckTickMax || !parent.Spawned || Props.FleckDef == null)
            {
                return;
            }
            Fleck_MakeFleckTick = 0;
            Map map = parent.Map;
            if (!drawPos.ShouldSpawnMotesAt(map))
            {
                return;
            }
            float heading;
            if (parent is Projectile projectile)
            {
                heading = (drawPos - projectile.intendedTarget.CenterVector3).AngleFlat();
            }
            else
            {
                heading = previous == Vector3.zero ? 0f : (previous - drawPos).AngleFlat();
            }
            int count = Props.Fleck_MakeFleckNum.RandomInRange;
            for (int i = 0; i < count; i++)
            {
                FleckCreationData data = FleckMaker.GetDataStatic(drawPos, map, Props.FleckDef, Props.Fleck_Scale.RandomInRange);
                data.rotationRate = Props.Fleck_Rotation.RandomInRange;
                data.velocityAngle = Props.Fleck_Angle.RandomInRange + heading;
                data.velocitySpeed = Props.Fleck_Speed.RandomInRange;
                map.flecks.CreateFleck(data);
            }
        }
    }

    /// <summary>A light that follows the aircraft (the glower is respawned under it every few ticks).</summary>
    [StaticConstructorOnStartup]
    public class Comp_SpawnLight : ThingComp
    {
        public int SpawnLightTick;
        public Thing Light_A;
        public Thing Light_B;

        public CompProperties_SpawnLight Props => props as CompProperties_SpawnLight;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref Light_A, "Light_A");
            Scribe_References.Look(ref Light_B, "Light_B");
        }

        public override void PostDeSpawn(Map map)
        {
            base.PostDeSpawn(map);
            ClearLights();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            ClearLights();
        }

        private void ClearLights()
        {
            if (Light_A != null && !Light_A.Destroyed)
            {
                Light_A.Destroy();
            }
            if (Light_B != null && !Light_B.Destroyed)
            {
                Light_B.Destroy();
            }
            Light_A = null;
            Light_B = null;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || Props.SpawnLightDef == null)
            {
                return;
            }
            SpawnLightTick++;
            if (SpawnLightTick < Props.SpawnLightTickMax)
            {
                return;
            }
            SpawnLightTick = 0;
            IntVec3 cell = parent.DrawPos.ToIntVec3();
            if (!cell.InBounds(parent.Map))
            {
                return;
            }
            Thing light = GenSpawn.Spawn(ThingMaker.MakeThing(Props.SpawnLightDef), cell, parent.Map);
            Thing old = Light_A ?? Light_B;
            if (Light_A != null)
            {
                Light_B = light;
                Light_A = null;
            }
            else
            {
                Light_A = light;
                Light_B = null;
            }
            if (old != null && !old.Destroyed)
            {
                old.Destroy();
            }
        }
    }

    /// <summary>Shows the shield status gizmo of a CompProjectileInterceptor.</summary>
    [StaticConstructorOnStartup]
    public class Comp_ShowBuildingShieldGizmos : ThingComp
    {
        public CompProperties_ShowBuildingShieldGizmos PropsSpawner => (CompProperties_ShowBuildingShieldGizmos)props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            CompProjectileInterceptor interceptor = parent.TryGetComp<CompProjectileInterceptor>();
            if (interceptor != null && Find.Selector.SingleSelectedThing == parent)
            {
                yield return new Gizmo_ProjectileInterceptorHitPoints { interceptor = interceptor };
            }
        }
    }

    /// <summary>Shows the battery charge of an electric aircraft.</summary>
    [StaticConstructorOnStartup]
    public class Comp_ShowStoredEnergyGizmos : ThingComp
    {
        public CompProperties_ShowStoredEnergyGizmos PropsSpawner => (CompProperties_ShowStoredEnergyGizmos)props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            CompPowerBattery battery = parent.TryGetComp<CompPowerBattery>();
            if (battery != null && Find.Selector.NumSelected <= 1)
            {
                yield return new Gizmo_RefuelablePowerStatus { CompPowerBattery = battery };
            }
        }
    }

    public class Comp_LinkToVerbSpawnr : ThingComp
    {
        public CompProperties_LinkToVerbSpawnr Props => props as CompProperties_LinkToVerbSpawnr;
    }

    /// <summary>Debug helper of the original mod: logs the angle and distance from the thing to a clicked cell.</summary>
    [StaticConstructorOnStartup]
    public class Comp_Test_Get_AngleAndRange : ThingComp
    {
        public CompProperties_Test_Get_AngleAndRange Props => props as CompProperties_Test_Get_AngleAndRange;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Prefs.DevMode)
            {
                yield break;
            }
            yield return new Command_Action
            {
                action = DoSomething,
                defaultLabel = "DEV: Get angle and range",
                hotKey = KeyBindingDefOf.Misc7
            };
        }

        public void DoSomething()
        {
            Map map = parent.Map;
            TargetingParameters parms = new TargetingParameters
            {
                canTargetLocations = true,
                validator = target => target.IsValid && target.Cell.InBounds(map)
            };
            Find.Targeter.BeginTargeting(parms, target =>
            {
                Vector3 offset = parent.DrawPos - UI.MouseMapPosition();
                Log.Message($"[Aerocraft Framework] range {offset.MagnitudeHorizontal()}, angle {offset.AngleFlat()}");
            }, null, null, null, null, null, true, null, null);
        }
    }
}
