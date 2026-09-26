using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>The minified aircraft shows the aircraft picture instead of the invisible building graphic.</summary>
    public class MinifiedThing_ChangeByAerocraftFramework : MinifiedThing
    {
        public override Graphic Graphic => InnerThing?.DefaultGraphic ?? base.Graphic;
    }

    /// <summary>Draws the aircraft picture as the placement ghost (the building graphic itself is invisible).</summary>
    public class PlaceWorker_DrawAerocraft : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 loc, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            if (def.graphicData == null)
            {
                return;
            }
            Graphic graphic = GraphicDatabase.Get<Graphic_Single>(def.graphicData.texPath, ShaderDatabase.Cutout, def.graphicData.drawSize, Color.white);
            GhostUtility.GhostGraphicFor(graphic, def, ghostCol).DrawFromDef(GenThing.TrueCenter(loc, rot, def.Size, AltitudeLayer.MetaOverlays.AltitudeFor()), rot, def);
        }
    }

    /// <summary>Weapon kits can only be placed on a landed player aircraft.</summary>
    public class PlaceWorker_InBaseThing : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            List<IntVec3> cells = new List<IntVec3>();
            bool allowed = false;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!(buildings[i] is Building_Aerocraft_AsBaseThing aircraft))
                {
                    continue;
                }
                CellRect rect = aircraft.OccupiedRect();
                foreach (IntVec3 cell in rect)
                {
                    cells.Add(cell);
                }
                if (rect.Contains(loc))
                {
                    allowed = true;
                }
            }
            GenDraw.DrawFieldEdges(cells);
            return allowed ? AcceptanceReport.WasAccepted : new AcceptanceReport("AerocraftFramework_NoAerocraftInPosition".Translate());
        }
    }
}
