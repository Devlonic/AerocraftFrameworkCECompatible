using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MYDE_AerocraftFramework
{
    /// <summary>Launches aircraft (drones) of the caster's faction: drone packs worn by pawns and carrier launchers.</summary>
    public class Verb_Spawn_Aerocraft_WithCasterFaction : Verb_CastBase
    {
        protected override bool TryCastShot()
        {
            CompProperties_LinkToVerbSpawnr props = EquipmentSource?.TryGetComp<Comp_LinkToVerbSpawnr>()?.Props;
            if (props == null || props.SpawnDef == null || caster?.Map == null)
            {
                return false;
            }
            CompApparelReloadable reloadable = ReloadableCompSource;
            Map map = caster.Map;
            Vector3 drawPos = Caster.DrawPos;
            Vector3 targetPos = CurrentTarget.CenterVector3;
            IntVec3 origin = Caster.Position;
            if (props.If_SpawnInMapBoundary)
            {
                float angle = (drawPos - targetPos).AngleFlat();
                for (int i = 0; i < 500; i++)
                {
                    IntVec3 cell = MYDE_ModFront.GetVector3_By_AngleFlat(drawPos, i, angle).ToIntVec3();
                    if (!cell.InBounds(map))
                    {
                        break;
                    }
                    origin = cell;
                    if (cell.InNoBuildEdgeArea(map))
                    {
                        break;
                    }
                }
            }
            List<IntVec3> cells = MYDE_ModFront.GetPos_Square(origin, props.SpawnRadius, props.SpawnRadius);
            cells.RemoveAll(c => !c.InBounds(map));
            if (cells.Count == 0)
            {
                cells.Add(origin);
            }
            for (int i = 0; i < props.SpawnNum; i++)
            {
                if (reloadable != null && reloadable.RemainingCharges < props.SpawnConsumePerNum)
                {
                    break;
                }
                DoSomething_Spawn(props, cells, targetPos);
            }
            if (props.If_OneUse && Caster is Pawn pawn)
            {
                EquipmentSource.Destroy();
                if (props.WeaponDefAfterOneUse != null)
                {
                    pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(props.WeaponDefAfterOneUse));
                }
            }
            return true;
        }

        public void DoSomething_Spawn(CompProperties_LinkToVerbSpawnr Comp, List<IntVec3> ListAllPos, Vector3 End)
        {
            Map map = caster.Map;
            Thing thing = ThingMaker.MakeThing(Comp.SpawnDef);
            if (caster.Faction != null)
            {
                thing.SetFactionDirect(caster.Faction);
            }
            GenSpawn.Spawn(thing, ListAllPos.RandomElement(), map);
            if (thing is Building_Aerocraft_AsBaseThing aircraft)
            {
                if (Comp.If_AutoSelectAfterSpawn && caster.Faction == Faction.OfPlayer)
                {
                    Find.Selector.Select(aircraft);
                }
                foreach (Building_Aerocraft_Base turret in aircraft.AllTurrets)
                {
                    AerocraftCompat.Ammo.FillMagazines(turret);
                }
                aircraft.Set_Flying();
                if (Comp.If_DefaultFollow)
                {
                    aircraft.Set_Target_FollowTargetThing(Caster);
                }
                else
                {
                    aircraft.Set_TargetVPos(End);
                }
            }
            CompApparelReloadable reloadable = ReloadableCompSource;
            if (reloadable != null)
            {
                for (int i = 0; i < Comp.SpawnConsumePerNum; i++)
                {
                    reloadable.UsedOnce();
                }
            }
        }
    }
}
