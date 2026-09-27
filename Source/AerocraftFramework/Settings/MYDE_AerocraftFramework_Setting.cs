using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Mod settings. Field and key names are kept from the original mod so existing settings files still load.
    /// </summary>
    public class MYDE_AerocraftFramework_Setting : ModSettings
    {
        public static bool If_CheckMapBoundary = true;
        public static bool If_DrawShadow = true;
        public static float Draw_Shadow_HeighRange_Min = 0.5f;
        public static float Draw_Shadow_HeighRange_Max = 1f;
        public static float Draw_Shadow_Angle = 60f;
        public static bool If_CanFireOnlyFlying = true;

        /// <summary>The original mod landed an aircraft hit by EMP or stun damage; now it stays up with its weapons silent.</summary>
        public static bool If_StunForcesLanding = false;
        public static bool If_CanControlNonPlayer = false;

        // Added by the patched version.
        public static bool If_ServiceOnlyInHomeArea = false;
        public static bool If_AutoReload = true;
        public static bool If_ReloadAllWeapons = true;
        public static bool If_DebugLogging = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref If_CheckMapBoundary, "If_CheckMapBoundary", true);
            Scribe_Values.Look(ref If_DrawShadow, "If_DrawShadow", true);
            Scribe_Values.Look(ref Draw_Shadow_HeighRange_Min, "Draw_Shadow_HeighRange_Min", 0.5f);
            Scribe_Values.Look(ref Draw_Shadow_HeighRange_Max, "Draw_Shadow_HeighRange_Max", 1f);
            Scribe_Values.Look(ref Draw_Shadow_Angle, "Draw_Shadow_Angle", 60f);
            Scribe_Values.Look(ref If_CanFireOnlyFlying, "If_CanFireOnlyFlying", true);
            Scribe_Values.Look(ref If_StunForcesLanding, "If_StunForcesLanding", false);
            Scribe_Values.Look(ref If_CanControlNonPlayer, "If_CanControlNonPlayer", false);
            Scribe_Values.Look(ref If_ServiceOnlyInHomeArea, "If_ServiceOnlyInHomeArea", false);
            Scribe_Values.Look(ref If_AutoReload, "If_AutoReload", true);
            Scribe_Values.Look(ref If_ReloadAllWeapons, "If_ReloadAllWeapons", true);
            Scribe_Values.Look(ref If_DebugLogging, "If_DebugLogging", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Draw_Shadow_HeighRange_Max = Mathf.Max(Draw_Shadow_HeighRange_Max, Draw_Shadow_HeighRange_Min);
            }
        }

        public void Initialization()
        {
            If_CheckMapBoundary = true;
            If_DrawShadow = true;
            Draw_Shadow_HeighRange_Min = 0.5f;
            Draw_Shadow_HeighRange_Max = 1f;
            Draw_Shadow_Angle = 60f;
            If_CanFireOnlyFlying = true;
            If_CanControlNonPlayer = false;
            If_ServiceOnlyInHomeArea = false;
            If_AutoReload = true;
            If_ReloadAllWeapons = true;
            If_DebugLogging = false;
        }

        public void DoWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);
            if (list.ButtonText("AerocraftFramework_Setting_Initialization".Translate()))
            {
                Initialization();
            }
            list.GapLine();

            Header(list, "AerocraftFramework_Setting_HeaderFlight");
            list.CheckboxLabeled("AerocraftFramework_Setting_If_CheckMapBoundary".Translate(), ref If_CheckMapBoundary, "AerocraftFramework_Setting_If_CheckMapBoundary_Tip".Translate());
            list.CheckboxLabeled("AerocraftFramework_Setting_If_CanFireOnlyFlying".Translate(), ref If_CanFireOnlyFlying, "AerocraftFramework_Setting_If_CanFireOnlyFlying_Tip".Translate());
            list.CheckboxLabeled("AerocraftFramework_Setting_If_StunForcesLanding".Translate(), ref If_StunForcesLanding, "AerocraftFramework_Setting_If_StunForcesLanding_Tip".Translate());
            list.Gap();

            Header(list, "AerocraftFramework_Setting_HeaderService");
            list.CheckboxLabeled("AerocraftFramework_Setting_If_AutoReload".Translate(), ref If_AutoReload, "AerocraftFramework_Setting_If_AutoReload_Tip".Translate());
            list.CheckboxLabeled("AerocraftFramework_Setting_If_ReloadAllWeapons".Translate(), ref If_ReloadAllWeapons, "AerocraftFramework_Setting_If_ReloadAllWeapons_Tip".Translate());
            list.CheckboxLabeled("AerocraftFramework_Setting_If_ServiceOnlyInHomeArea".Translate(), ref If_ServiceOnlyInHomeArea, "AerocraftFramework_Setting_If_ServiceOnlyInHomeArea_Tip".Translate());
            list.Gap();

            Header(list, "AerocraftFramework_Setting_HeaderShadow");
            list.CheckboxLabeled("AerocraftFramework_Setting_If_DrawShadow".Translate(), ref If_DrawShadow);
            list.Label("AerocraftFramework_Setting_Draw_Shadow_HeighRange_Min".Translate() + ": " + Draw_Shadow_HeighRange_Min.ToString("0.00"));
            Draw_Shadow_HeighRange_Min = list.Slider(Draw_Shadow_HeighRange_Min, 0f, Draw_Shadow_HeighRange_Max);
            list.Label("AerocraftFramework_Setting_Draw_Shadow_HeighRange_Max".Translate() + ": " + Draw_Shadow_HeighRange_Max.ToString("0.00"));
            Draw_Shadow_HeighRange_Max = list.Slider(Draw_Shadow_HeighRange_Max, Draw_Shadow_HeighRange_Min, 10f);
            list.Label("AerocraftFramework_Setting_Draw_Shadow_Angle".Translate() + ": " + Draw_Shadow_Angle.ToString("0"));
            Draw_Shadow_Angle = Mathf.Round(list.Slider(Draw_Shadow_Angle, 0f, 360f));
            list.Gap();

            Header(list, "AerocraftFramework_Setting_HeaderDebug");
            list.CheckboxLabeled("AerocraftFramework_Setting_If_CanControlNonPlayer".Translate(), ref If_CanControlNonPlayer);
            list.CheckboxLabeled("AerocraftFramework_Setting_If_DebugLogging".Translate(), ref If_DebugLogging);
            list.End();
        }

        private static void Header(Listing_Standard list, string key)
        {
            Text.Font = GameFont.Medium;
            list.Label(key.Translate());
            Text.Font = GameFont.Small;
        }
    }
}
