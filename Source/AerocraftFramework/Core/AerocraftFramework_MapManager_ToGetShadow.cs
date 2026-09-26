using System;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>Computes the shadow offset, angle and opacity of flying things from the time of day.</summary>
    public class AerocraftFramework_MapManager_ToGetShadow : MapComponent
    {
        public float Shadow_Hour;
        public static float Shadow_Range;
        public static float Shadow_Angle;
        public static float Shadow_Transparency;

        public AerocraftFramework_MapManager_ToGetShadow(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (!MYDE_AerocraftFramework_Setting.If_DrawShadow || map != Find.CurrentMap)
            {
                return;
            }
            float ageInDays = map.AgeInDays;
            Shadow_Hour = ageInDays - (float)Math.Floor(ageInDays);
            float min = MYDE_AerocraftFramework_Setting.Draw_Shadow_HeighRange_Min;
            float max = MYDE_AerocraftFramework_Setting.Draw_Shadow_HeighRange_Max;
            float baseAngle = MYDE_AerocraftFramework_Setting.Draw_Shadow_Angle;
            float rangePerQuarter = (max - min) / 0.25f;
            float anglePerQuarter = baseAngle / 0.25f;
            if (Shadow_Hour < 0.25f)
            {
                Shadow_Range = max - Shadow_Hour * rangePerQuarter;
                Shadow_Angle = baseAngle - anglePerQuarter * Shadow_Hour;
            }
            else if (Shadow_Hour < 0.5f)
            {
                Shadow_Range = min + (Shadow_Hour - 0.25f) * rangePerQuarter;
                Shadow_Angle = baseAngle - anglePerQuarter * Shadow_Hour;
            }
            else if (Shadow_Hour < 0.75f)
            {
                Shadow_Range = max - (Shadow_Hour - 0.5f) * rangePerQuarter;
                Shadow_Angle = baseAngle - anglePerQuarter * (Shadow_Hour - 0.5f);
            }
            else
            {
                Shadow_Range = min + (Shadow_Hour - 0.75f) * rangePerQuarter;
                Shadow_Angle = baseAngle - anglePerQuarter * (Shadow_Hour - 0.5f);
            }
            if ((Shadow_Hour >= 0.04f && Shadow_Hour < 0.46f) || (Shadow_Hour >= 0.54f && Shadow_Hour < 0.96f))
            {
                Shadow_Transparency = 1f;
            }
            else if (Shadow_Hour < 0.04f)
            {
                Shadow_Transparency = Shadow_Hour * 25f;
            }
            else if (Shadow_Hour >= 0.5f && Shadow_Hour < 0.54f)
            {
                Shadow_Transparency = (Shadow_Hour - 0.5f) * 25f;
            }
            else if (Shadow_Hour >= 0.46f && Shadow_Hour < 0.5f)
            {
                Shadow_Transparency = (0.5f - Shadow_Hour) * 25f;
            }
            else
            {
                Shadow_Transparency = (1f - Shadow_Hour) * 25f;
            }
        }
    }
}
