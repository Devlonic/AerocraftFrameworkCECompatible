using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>The take-off animation when an aircraft leaves a player map for another world tile.</summary>
    public class Skyfaller_Aerocraft_Leaving : Skyfaller
    {
        public Building_Aerocraft_AsBaseThing LinkToAerocraft;
        public int DestinationTile = -1;
        public WorldObjectDef WorldObject;
        private bool AlreadyLeft;
        public float TravelSpeed = 0.00025f;
        public List<Building> AllExtraWeapon = new List<Building>();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            angle = 45f;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref LinkToAerocraft, "LinkToAerocraft");
            Scribe_Values.Look(ref DestinationTile, "DestinationTile", 0);
            Scribe_Values.Look(ref AlreadyLeft, "AlreadyLeft", false);
            Scribe_Defs.Look(ref WorldObject, "WorldObject");
            Scribe_Values.Look(ref TravelSpeed, "TravelSpeed", 0f);
            Scribe_Collections.Look(ref AllExtraWeapon, "AllExtraWeapon", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                AllExtraWeapon = AllExtraWeapon ?? new List<Building>();
                AllExtraWeapon.RemoveAll(b => b == null);
            }
        }

        protected override void LeaveMap()
        {
            if (!AlreadyLeft && LinkToAerocraft != null && WorldObject != null)
            {
                WorldObject_CrossMapThing_Flying flying = (WorldObject_CrossMapThing_Flying)WorldObjectMaker.MakeWorldObject(WorldObject);
                flying.Tile = Map.Tile;
                flying.SetFaction(Faction.OfPlayer);
                flying.DestinationTile = DestinationTile;
                flying.TravelSpeed = TravelSpeed;
                flying.LinkToAerocraft = LinkToAerocraft;
                flying.AllExtraWeapon.AddRange(AllExtraWeapon);
                LinkToAerocraft = null;
                AllExtraWeapon.Clear();
                Find.WorldObjects.Add(flying);
            }
            AlreadyLeft = true;
            Destroy();
        }
    }
}
