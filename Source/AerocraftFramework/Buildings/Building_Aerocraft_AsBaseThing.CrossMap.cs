using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Leaving for and arriving from another world tile, as the aircraft of Vehicle Framework do: the aircraft flies
    /// off the map edge on the side of its destination, climbing (drawn larger) as it goes, and arrives over the
    /// edge on the side it comes from, coming down to its usual size. The original despawned it on the spot and
    /// played a drop pod's shrinking take-off.
    /// </summary>
    public partial class Building_Aerocraft_AsBaseThing
    {
        /// <summary>How far past the map edge the aircraft flies before it leaves (and where it arrives from).</summary>
        private const float OffMapDistance = 12f;

        /// <summary>How much larger the aircraft gets while climbing away (and starts when arriving).</summary>
        private const float ClimbScale = 0.6f;

        private int departureTile = -1;
        private WorldObjectDef departureWorldObject;
        private float departureTravelSpeed;
        private float departureFuel;
        private Vector3 departurePoint;
        private Vector3 departureSentTo;
        private bool departureSent;
        private float departureDistance;

        private bool arrivingOverEdge;
        private Vector3 arrivalTarget;
        private float arrivalDistance;

        public bool IsDeparting => departureTile >= 0;

        public int DepartureTile => departureTile;

        public bool IsArrivingOverEdge => arrivingOverEdge;

        private float FlyingScale => 1f + Draw_ScaleIncreaseFactor_WhenFlying;

        private float FlyingShadowHeight => 1f + Draw_ScaleIncreaseFactor_WhenFlying * Draw_Shadow_Base_HeightFactor;

        /// <summary>The map edge does not turn an aircraft round while it flies off the map or comes in over it.</summary>
        private bool IgnoresMapEdge => IsDeparting || arrivingOverEdge;

        private void ExposeCrossMap()
        {
            Scribe_Values.Look(ref departureTile, "AF_DepartureTile", -1);
            Scribe_Defs.Look(ref departureWorldObject, "AF_DepartureWorldObject");
            Scribe_Values.Look(ref departureTravelSpeed, "AF_DepartureTravelSpeed", 0f);
            Scribe_Values.Look(ref departureFuel, "AF_DepartureFuel", 0f);
            Scribe_Values.Look(ref departurePoint, "AF_DeparturePoint");
            Scribe_Values.Look(ref departureSentTo, "AF_DepartureSentTo");
            Scribe_Values.Look(ref departureSent, "AF_DepartureSent", false);
            Scribe_Values.Look(ref departureDistance, "AF_DepartureDistance", 0f);
            Scribe_Values.Look(ref arrivingOverEdge, "AF_ArrivingOverEdge", false);
            Scribe_Values.Look(ref arrivalTarget, "AF_ArrivalTarget");
            Scribe_Values.Look(ref arrivalDistance, "AF_ArrivalDistance", 0f);
        }

        /// <summary>A point outside the map, straight from <paramref name="from"/> in the given world heading (0 = north).</summary>
        public static Vector3 OffMapPoint(Map map, Vector3 from, float heading)
        {
            Vector3 direction = new Vector3(Mathf.Sin(heading * Mathf.Deg2Rad), 0f, Mathf.Cos(heading * Mathf.Deg2Rad));
            float t = float.MaxValue;
            if (direction.x > 0.001f)
            {
                t = Mathf.Min(t, (map.Size.x + OffMapDistance - from.x) / direction.x);
            }
            else if (direction.x < -0.001f)
            {
                t = Mathf.Min(t, (-OffMapDistance - from.x) / direction.x);
            }
            if (direction.z > 0.001f)
            {
                t = Mathf.Min(t, (map.Size.z + OffMapDistance - from.z) / direction.z);
            }
            else if (direction.z < -0.001f)
            {
                t = Mathf.Min(t, (-OffMapDistance - from.z) / direction.z);
            }
            Vector3 point = from + direction * t;
            point.y = from.y;
            return point;
        }

        private bool IsOffMap(Vector3 point, float beyond)
        {
            return point.x < -beyond || point.z < -beyond || point.x > Map.Size.x + beyond || point.z > Map.Size.z + beyond;
        }

        // ------------------------------------------------------------------ departure

        /// <summary>
        /// Flies off the map towards the world tile, climbing first if landed. The fuel for the trip is paid when the
        /// aircraft leaves the map; another order before that cancels the departure.
        /// </summary>
        public void BeginDeparture(int tile, WorldObjectDef worldObject, float travelSpeed, float fuel)
        {
            ClearStrafeRun();
            CancelTroopDrop();
            GetComp<Comp_CanLoadShell>()?.ClearBombRun();
            departureTile = tile;
            departureWorldObject = worldObject;
            departureTravelSpeed = travelSpeed;
            departureFuel = fuel;
            departureSent = false;
            departurePoint = OffMapPoint(Map, DrawPos, Find.WorldGrid.GetHeadingFromTo(Map.Tile, tile));
            departureDistance = Mathf.Max(1f, (departurePoint - DrawPos).MagnitudeHorizontal());
            if (!Is_Flying && !If_UpOrDown)
            {
                // A vertical climb, the fixed-wing aircraft included: there is no runway to pick for this order.
                Change_Up();
                if (!If_UpOrDown)
                {
                    CancelDeparture();
                }
            }
        }

        public void CancelDeparture()
        {
            if (!IsDeparting)
            {
                return;
            }
            departureTile = -1;
            if (Spawned && Is_Flying)
            {
                Draw_ScaleFactorNow = FlyingScale;
                Draw_Shadow_Base_HeightNow = FlyingShadowHeight;
            }
        }

        private void DepartureTick()
        {
            if (!IsDeparting)
            {
                return;
            }
            if (!Is_Flying)
            {
                if (!If_UpOrDown || If_DropingNow)
                {
                    CancelDeparture();
                }
                return;
            }
            if (!departureSent)
            {
                if (If_NeedGlidingWhenTakeOff && (If_GlidingTakeOffNow || If_GlidingDownNow || If_TuringByGlidingTakeOffOrDownNow))
                {
                    // Still on its take-off run: it ignores new points until airborne.
                    return;
                }
                Set_TargetVPos(departurePoint);
                departureSentTo = TargetVPos;
                departureSent = true;
                departureDistance = Mathf.Max(1f, (departurePoint - DrawPos).MagnitudeHorizontal());
                if (HaveGoToTarget)
                {
                    CancelDeparture();
                }
                return;
            }
            if (TargetVPos != departureSentTo || FollowTargetThing != null || If_GoBackNow || If_DropingNow)
            {
                CancelDeparture();
                return;
            }
            float progress = 1f - Mathf.Clamp01((departurePoint - DrawPos).MagnitudeHorizontal() / departureDistance);
            Draw_ScaleFactorNow = FlyingScale * (1f + ClimbScale * progress);
            Draw_Shadow_Base_HeightNow = FlyingShadowHeight * (1f + ClimbScale * progress);
            if (IsOffMap(DrawPos, OffMapDistance / 2f))
            {
                LeaveForWorld();
            }
        }

        private void LeaveForWorld()
        {
            Map map = Map;
            int tile = departureTile;
            departureTile = -1;
            RefuelableComp?.ConsumeFuel(departureFuel);
            Comp_CanCrossMap.StowTransporterPawns(this, map);
            WorldObject_CrossMapThing_Flying flying = (WorldObject_CrossMapThing_Flying)WorldObjectMaker.MakeWorldObject(departureWorldObject);
            flying.Tile = map.Tile;
            flying.SetFaction(Faction.OfPlayer);
            flying.DestinationTile = tile;
            flying.TravelSpeed = departureTravelSpeed;
            flying.LinkToAerocraft = this;
            flying.AllExtraWeapon.AddRange(AllExtraWeapon);
            Draw_ScaleFactorNow = FlyingScale;
            Draw_Shadow_Base_HeightNow = FlyingShadowHeight;
            bool wasSelected = Find.Selector.IsSelected(this);
            DepartingCrossMap = true;
            DeSpawn();
            Find.WorldObjects.Add(flying);
            if (wasSelected)
            {
                Find.Selector.ClearSelection();
            }
        }

        // ------------------------------------------------------------------ arrival

        /// <summary>
        /// Spawns the aircraft flying just over the map edge on the side it comes from (world heading
        /// <paramref name="fromHeading"/>), heading for <paramref name="target"/>, drawn larger as if still high up.
        /// </summary>
        public static void ArriveOverEdge(Building_Aerocraft_AsBaseThing aircraft, Map map, float fromHeading, Vector3 target)
        {
            Vector3 entry = OffMapPoint(map, target, fromHeading);
            int margin = Mathf.Max(aircraft.def.size.x, aircraft.def.size.z) + 2;
            IntVec3 spawnCell = new IntVec3(
                Mathf.Clamp((int)entry.x, margin, map.Size.x - 1 - margin), 0,
                Mathf.Clamp((int)entry.z, margin, map.Size.z - 1 - margin));
            GenSpawn.Spawn(aircraft, spawnCell, map);
            aircraft.RealCurrentPosition = new Vector2(entry.x, entry.z);
            aircraft.If_UpOrDown = true;
            aircraft.Move_WarmUpTick = aircraft.Move_WarmUpTickMax + 1;
            aircraft.Angle_Fly_Now = (target - entry).AngleFlat();
            aircraft.arrivingOverEdge = true;
            aircraft.arrivalTarget = target;
            aircraft.arrivalDistance = Mathf.Max(1f, (target - entry).MagnitudeHorizontal());
            aircraft.Draw_ScaleFactorNow = aircraft.FlyingScale * (1f + ClimbScale);
            aircraft.Draw_Shadow_Base_HeightNow = aircraft.FlyingShadowHeight * (1f + ClimbScale);
            aircraft.Set_TargetVPos(target);
        }

        private void ArrivalTick()
        {
            if (!arrivingOverEdge)
            {
                return;
            }
            // Down to the usual flying size over the first half of the way in.
            float travelled = 1f - Mathf.Clamp01((arrivalTarget - DrawPos).MagnitudeHorizontal() / arrivalDistance);
            float climb = ClimbScale * (1f - Mathf.Clamp01(travelled * 2f));
            Draw_ScaleFactorNow = FlyingScale * (1f + climb);
            Draw_Shadow_Base_HeightNow = FlyingShadowHeight * (1f + climb);
            bool inside = !IsOffMap(DrawPos, 0f) && !DrawPos.ToIntVec3().InNoBuildEdgeArea(Map);
            if ((inside && climb <= 0f) || HaveGoToTarget || !Is_Flying)
            {
                arrivingOverEdge = false;
                if (Is_Flying)
                {
                    Draw_ScaleFactorNow = FlyingScale;
                    Draw_Shadow_Base_HeightNow = FlyingShadowHeight;
                }
            }
        }
    }
}
