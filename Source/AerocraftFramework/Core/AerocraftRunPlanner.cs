using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Shared geometry of runs over the map (bombing, strafing): lining up before a line, and telling when the
    /// aircraft is over a point. A fixed-wing aircraft cannot turn onto a point a few cells to its side: it would
    /// circle it for ever, so passing close by counts too.
    /// </summary>
    public static class AerocraftRunPlanner
    {
        /// <summary>Passing this close to a point and moving away again counts as being over it.</summary>
        public const float PassDistance = 4f;

        /// <summary>How far before a line the aircraft lines up with it, when it comes in at an angle.</summary>
        public const float LeadInDistance = 8f;

        public const float LeadInAngle = 30f;

        /// <summary>Over a point: a cell and a half, a little more for fast aircraft.</summary>
        public static float OverDistance(Building_Aerocraft_AsBaseThing aircraft)
        {
            return Mathf.Max(1.5f, aircraft.MoveSpeed_Max * 3f);
        }

        /// <summary>
        /// A point on the line's extension before <paramref name="start"/>, when the aircraft at
        /// <paramref name="from"/> would meet the line at more than <see cref="LeadInAngle"/>; otherwise invalid.
        /// </summary>
        public static IntVec3 LeadIn(Vector3 from, IntVec3 start, IntVec3 end, Map map)
        {
            if (start == end)
            {
                return IntVec3.Invalid;
            }
            Vector3 a = start.ToVector3Shifted();
            Vector3 line = end.ToVector3Shifted() - a;
            Vector3 approach = a - from;
            line.y = approach.y = 0f;
            if (approach.magnitude < 1f || Vector3.Angle(approach, line) <= LeadInAngle)
            {
                return IntVec3.Invalid;
            }
            return ClampFlyable((a - line.normalized * LeadInDistance).ToIntVec3(), map);
        }

        /// <summary>
        /// Keeps a point the run adds itself (lining up, flying on) out of the band along the map edge where an
        /// aircraft turns back, which would end the run.
        /// </summary>
        public static IntVec3 ClampFlyable(IntVec3 cell, Map map)
        {
            const int margin = 11;
            int minX = Mathf.Min(margin, map.Size.x / 2);
            int minZ = Mathf.Min(margin, map.Size.z / 2);
            int maxX = Mathf.Max(map.Size.x - 1 - margin, map.Size.x / 2);
            int maxZ = Mathf.Max(map.Size.z - 1 - margin, map.Size.z / 2);
            return new IntVec3(Mathf.Clamp(cell.x, minX, maxX), 0, Mathf.Clamp(cell.z, minZ, maxZ));
        }

        /// <summary>A line of the given length through the target, along the approach from <paramref name="from"/>.</summary>
        public static void LineThrough(Vector3 from, IntVec3 target, float length, Map map, out IntVec3 start, out IntVec3 end)
        {
            Vector3 center = target.ToVector3Shifted();
            Vector3 direction = center - from;
            direction.y = 0f;
            direction = direction.sqrMagnitude < 0.01f ? Vector3.forward : direction.normalized;
            start = (center - direction * (length / 2f)).ToIntVec3().ClampInsideMap(map);
            end = (center + direction * (length / 2f)).ToIntVec3().ClampInsideMap(map);
        }

        /// <summary>
        /// Whether the aircraft is over the cell: close enough, passing it and moving away, or arrived where it was
        /// sent. <paramref name="lastDistance"/> keeps the distance between calls (start with float.MaxValue).
        /// </summary>
        public static bool IsOver(Building_Aerocraft_AsBaseThing aircraft, IntVec3 cell, ref float lastDistance)
        {
            float distance = (aircraft.DrawPos - cell.ToVector3Shifted()).MagnitudeHorizontal();
            bool passing = distance <= PassDistance && distance > lastDistance;
            lastDistance = distance;
            if (distance <= OverDistance(aircraft) || passing || aircraft.HaveGoToTarget)
            {
                lastDistance = float.MaxValue;
                return true;
            }
            return false;
        }

        /// <summary>Where the aircraft goes after a run: straight on, instead of circling over what it hit.</summary>
        public static IntVec3 ExitPoint(Building_Aerocraft_AsBaseThing aircraft, float distance = 10f)
        {
            return ClampFlyable(MYDE_ModFront.GetVector3_By_AngleFlat(aircraft.DrawPos, distance, aircraft.Angle_Fly_Now).ToIntVec3(), aircraft.Map);
        }
    }
}
