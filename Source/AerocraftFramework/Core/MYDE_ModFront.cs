using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>Geometry and UI helpers shared by the framework (names kept from the original mod).</summary>
    public static class MYDE_ModFront
    {
        /// <summary>Point at <paramref name="Range"/> from <paramref name="Center"/>; angle 0 points to -Z, 90 to -X (the mod's own convention).</summary>
        public static Vector3 GetVector3_By_AngleFlat(Vector3 Center, float Range, float Angle)
        {
            double radians = Angle * Math.PI / 180.0;
            float x = Center.x - Range * (float)Math.Sin(radians);
            float z = Center.z - Range * (float)Math.Cos(radians);
            return new Vector3(x, Center.y, z);
        }

        public static List<IntVec3> GetPos_Square(IntVec3 TargetPos, int CX, int CY)
        {
            List<IntVec3> list = new List<IntVec3>((2 * CX + 1) * (2 * CY + 1));
            for (int i = -CX; i <= CX; i++)
            {
                for (int j = -CY; j <= CY; j++)
                {
                    list.Add(TargetPos + new IntVec3(i, 0, j));
                }
            }
            return list;
        }

        /// <summary>The pawn, or failing that the first damageable building, among the things in a cell.</summary>
        public static Thing Get_TargetThing(List<Thing> ListThing)
        {
            for (int i = 0; i < ListThing.Count; i++)
            {
                Thing thing = ListThing[i];
                if (thing is Pawn || (thing is Building && thing.def.useHitPoints))
                {
                    return thing;
                }
            }
            return null;
        }

        public static void DrawLine(Rect Rect)
        {
            Color color = GUI.color;
            GUI.color = color * new Color(1f, 1f, 1f, 0.4f);
            Widgets.DrawLineHorizontal(Rect.x, Rect.y, Rect.width);
            GUI.color = color;
        }

        /// <summary>Normalizes an angle to (-180, 180].</summary>
        public static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle <= -180f)
            {
                angle += 360f;
            }
            return angle;
        }
    }
}
