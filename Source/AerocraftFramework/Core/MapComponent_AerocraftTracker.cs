using System.Collections.Generic;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>Keeps the list of spawned aerocraft turrets (bodies and weapon mounts) on a map, for work givers and UI.</summary>
    public class MapComponent_AerocraftTracker : MapComponent
    {
        private readonly List<Building_Aerocraft_Base> turrets = new List<Building_Aerocraft_Base>();

        public MapComponent_AerocraftTracker(Map map) : base(map)
        {
        }

        public List<Building_Aerocraft_Base> Turrets => turrets;

        public static MapComponent_AerocraftTracker For(Map map) => map?.GetComponent<MapComponent_AerocraftTracker>();

        public void Register(Building_Aerocraft_Base turret)
        {
            if (!turrets.Contains(turret))
            {
                turrets.Add(turret);
            }
        }

        public void Deregister(Building_Aerocraft_Base turret)
        {
            turrets.Remove(turret);
        }

        public IEnumerable<Building_Aerocraft_AsBaseThing> Aircraft
        {
            get
            {
                for (int i = 0; i < turrets.Count; i++)
                {
                    if (turrets[i] is Building_Aerocraft_AsBaseThing aircraft)
                    {
                        yield return aircraft;
                    }
                }
            }
        }
    }
}
