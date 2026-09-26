using RimWorld;
using Verse;

namespace MYDE_AerocraftFramework
{
    [DefOf]
    public static class MYDE_JobDefOf
    {
        public static JobDef MYDE_AerocraftFramework_Job_Enter_Building_Aerocraft_AsBaseThing;

        public static JobDef MYDE_AerocraftFramework_Job_ReplaceCurrentWeapon;

        public static JobDef MYDE_AerocraftFramework_Job_LoadShell;

        /// <summary>Reload job of the Combat Extended module (null without CE).</summary>
        [MayRequire("CETeam.CombatExtended")]
        public static JobDef MYDE_AF_CE_ReloadTurret;

        static MYDE_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MYDE_JobDefOf));
        }
    }
}
