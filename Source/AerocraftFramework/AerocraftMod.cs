using HarmonyLib;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Mod entry point. The class name is kept from the original mod: RimWorld stores mod settings per Mod class name.
    /// </summary>
    public class MYDE_AerocraftFramework_Setting_Main : Mod
    {
        public const string HarmonyId = "MYDE.AerocraftFramework";

        public static MYDE_AerocraftFramework_Setting Settings;

        public static Harmony HarmonyInstance { get; private set; }

        public MYDE_AerocraftFramework_Setting_Main(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MYDE_AerocraftFramework_Setting>();
            HarmonyInstance = new Harmony(HarmonyId);
            HarmonyInstance.PatchAll(typeof(MYDE_AerocraftFramework_Setting_Main).Assembly);
            Log.Message($"[Aerocraft Framework] patched build {typeof(MYDE_AerocraftFramework_Setting_Main).Assembly.GetName().Version} loaded.");
        }

        public override string SettingsCategory() => "AerocraftFramework_Setting_Label".Translate();

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

        public static void DebugLog(string text)
        {
            if (MYDE_AerocraftFramework_Setting.If_DebugLogging)
            {
                Log.Message("[Aerocraft Framework] " + text);
            }
        }
    }
}
