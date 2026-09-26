using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>An extra weapon mount that rides on an aircraft body (rocket pods, missile rails, drone launchers...).</summary>
    [StaticConstructorOnStartup]
    public class Building_Aerocraft_AsWeapon : Building_Aerocraft_Base
    {
        public Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing;
        public float Building_Aerocraft_Base_Range;
        public float Building_Aerocraft_Base_Angle;
        public int CheckBreakDonwTick;
        public int CheckBreakDonwTickMax = 600;

        /// <summary>Set when the parent aircraft is destroyed, so the mount goes with it instead of breaking down.</summary>
        private bool destroyingWithParent;

        public Building_Aerocraft_AsWeapon()
        {
            top = new TurretTop_ChangeDraw(this);
        }

        public override bool Is_Flying => Building_Aerocraft_AsBaseThing != null && Building_Aerocraft_AsBaseThing.Is_Flying;

        public override bool Is_Static => Building_Aerocraft_AsBaseThing == null || Building_Aerocraft_AsBaseThing.Is_Static;

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            if (If_BreakDown)
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }
                sb.Append("AerocraftFramework_AsWeapon_BrokenDown".Translate());
            }
            return sb.ToString().TrimEndNewlines();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref Building_Aerocraft_AsBaseThing, "Building_Aerocraft_AsBaseThing");
            Scribe_Values.Look(ref Building_Aerocraft_Base_Range, "Building_Aerocraft_Base_Range", 0f);
            Scribe_Values.Look(ref Building_Aerocraft_Base_Angle, "Building_Aerocraft_Base_Angle", 0f);
            Scribe_Values.Look(ref CheckBreakDonwTick, "CheckBreakDonwTick", 0);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (Building_Aerocraft_AsBaseThing == null)
            {
                List<Thing> thingList = Position.GetThingList(map);
                for (int i = 0; i < thingList.Count; i++)
                {
                    if (thingList[i] is Building_Aerocraft_AsBaseThing body)
                    {
                        Building_Aerocraft_AsBaseThing = body;
                        Vector3 offset = Position.ToVector3Shifted() - body.DrawPos;
                        Building_Aerocraft_Base_Range = offset.MagnitudeHorizontal();
                        Building_Aerocraft_Base_Angle = offset.ToAngleFlat();
                        break;
                    }
                }
            }
            if (Building_Aerocraft_AsBaseThing != null && !Building_Aerocraft_AsBaseThing.AllExtraWeapon.Contains(this))
            {
                Building_Aerocraft_AsBaseThing.AllExtraWeapon.Add(this);
            }
            FollowParent();
        }

        public override void Tick()
        {
            if (Building_Aerocraft_AsBaseThing != null && Building_Aerocraft_AsBaseThing.Destroyed)
            {
                DestroyWithParent();
                return;
            }
            FollowParent();
            base.Tick();
            if (!Spawned)
            {
                return;
            }
            Change_Position();
            CheckBreakDonwTick++;
            if (CheckBreakDonwTick >= CheckBreakDonwTickMax)
            {
                CheckBreakDonwTick = 0;
                if (HitPoints >= MaxHitPoints)
                {
                    If_BreakDown = false;
                }
            }
        }

        /// <summary>Keeps the mount glued to its place on the parent aircraft.</summary>
        private void FollowParent()
        {
            Building_Aerocraft_AsBaseThing body = Building_Aerocraft_AsBaseThing;
            if (body == null)
            {
                return;
            }
            float range = Building_Aerocraft_Base_Range * body.Draw_ScaleFactorNow;
            float angle = body.Angle_Fly_Now + Building_Aerocraft_Base_Angle;
            Vector3 pos = MYDE_ModFront.GetVector3_By_AngleFlat(body.DrawPos, range, angle);
            RealCurrentPosition = new Vector2(pos.x, pos.z);
            Draw_ScaleFactorNow = body.Draw_ScaleFactorNow;
            Draw_Gun_Scale_Now = Draw_Gun_Scale_Origin * body.Draw_ScaleFactorNow;
            Angle_Fly_Now = body.Angle_Fly_Now;
            Shadow_Pos = MYDE_ModFront.GetVector3_By_AngleFlat(body.Shadow_Pos, range, angle);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (Building_Aerocraft_AsBaseThing != null)
            {
                yield return new Command_Action
                {
                    action = SelectBaseThing,
                    defaultLabel = "AerocraftFramework_SelectBaseThing_Label".Translate(),
                    defaultDesc = "AerocraftFramework_SelectBaseThing_Desc".Translate(),
                    icon = Building_Aerocraft_AsBaseThing.def.uiIcon ?? BaseContent.BadTex
                };
            }
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PreApplyDamage(ref dinfo, out absorbed);
            if (absorbed)
            {
                return;
            }
            if (Is_Flying)
            {
                if (dinfo.Def.isExplosive)
                {
                    dinfo.SetAmount(dinfo.Amount * 0.2f);
                }
                else if (!dinfo.Def.isRanged)
                {
                    absorbed = true;
                    return;
                }
            }
            if (If_BreakDown && Building_Aerocraft_AsBaseThing != null && !Building_Aerocraft_AsBaseThing.Destroyed)
            {
                // A broken mount passes further damage to the aircraft.
                absorbed = true;
                Building_Aerocraft_AsBaseThing.TakeDamage(dinfo);
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (!destroyingWithParent && HitPoints <= 0 && Building_Aerocraft_AsBaseThing != null && !Building_Aerocraft_AsBaseThing.Destroyed)
            {
                // Shot to pieces: the mount stops working until repaired, but stays on the aircraft.
                If_BreakDown = true;
                HitPoints = 1;
                return;
            }
            Building_Aerocraft_AsBaseThing?.AllExtraWeapon.Remove(this);
            base.Destroy(mode);
        }

        /// <summary>Removes the mount together with its aircraft.</summary>
        public void DestroyWithParent()
        {
            if (Destroyed)
            {
                return;
            }
            destroyingWithParent = true;
            Destroy(DestroyMode.Vanish);
        }

        public void SelectBaseThing()
        {
            Find.Selector.Deselect(this);
            Find.Selector.Select(Building_Aerocraft_AsBaseThing);
        }

        public void Change_Position()
        {
            ChangPosTick++;
            if (ChangPosTick < ChangPosTickMax)
            {
                return;
            }
            ChangPosTick = 0;
            IntVec3 position = new IntVec3((int)RealCurrentPosition.x, 0, (int)RealCurrentPosition.y);
            if (position != Position && position.InBounds(Map))
            {
                Position = position;
            }
        }
    }
}
