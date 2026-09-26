using System;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Calls into Combat Extended methods whose signatures changed between CE releases.
    /// The original Aerocraft Framework called CompAmmoUser.LoadAmmo(Thing), which CE later replaced by
    /// LoadAmmo(Thing, bool): every reload then died with a MissingMethodException. These calls are bound by
    /// reflection so that any known variant works.
    /// </summary>
    public static class CEApi
    {
        private static readonly MethodInfo loadAmmo2 = AccessTools.Method(typeof(CompAmmoUser), "LoadAmmo", new[] { typeof(Thing), typeof(bool) });
        private static readonly MethodInfo loadAmmo1 = AccessTools.Method(typeof(CompAmmoUser), "LoadAmmo", new[] { typeof(Thing) });
        private static readonly MethodInfo tryUnloadOut = AccessTools.Method(typeof(CompAmmoUser), "TryUnload", new[] { typeof(Thing).MakeByRefType(), typeof(bool) });
        private static readonly MethodInfo tryUnloadBool = AccessTools.Method(typeof(CompAmmoUser), "TryUnload", new[] { typeof(bool) });
        private static readonly MethodInfo tryUnloadNone = AccessTools.Method(typeof(CompAmmoUser), "TryUnload", Type.EmptyTypes);
        private static readonly MethodInfo resetAmmo1 = AccessTools.Method(typeof(CompAmmoUser), "ResetAmmoCount", new[] { typeof(AmmoDef) });
        private static readonly MethodInfo resetAmmo0 = AccessTools.Method(typeof(CompAmmoUser), "ResetAmmoCount", Type.EmptyTypes);

        /// <summary>True when every method this module needs was found (checked by the self test).</summary>
        public static bool AllBound => (loadAmmo2 != null || loadAmmo1 != null) && (tryUnloadOut != null || tryUnloadBool != null || tryUnloadNone != null) && (resetAmmo1 != null || resetAmmo0 != null);

        public static void LoadAmmo(CompAmmoUser comp, Thing ammo)
        {
            if (loadAmmo2 != null)
            {
                loadAmmo2.Invoke(comp, new object[] { ammo, false });
            }
            else if (loadAmmo1 != null)
            {
                loadAmmo1.Invoke(comp, new object[] { ammo });
            }
            else
            {
                Log.ErrorOnce("[Aerocraft Framework] CompAmmoUser.LoadAmmo not found in this Combat Extended version.", 0x4AF_0001);
            }
        }

        public static void TryUnload(CompAmmoUser comp)
        {
            if (tryUnloadOut != null)
            {
                tryUnloadOut.Invoke(comp, new object[] { null, false });
            }
            else if (tryUnloadBool != null)
            {
                tryUnloadBool.Invoke(comp, new object[] { false });
            }
            else
            {
                tryUnloadNone?.Invoke(comp, null);
            }
        }

        public static void ResetAmmoCount(CompAmmoUser comp)
        {
            if (resetAmmo1 != null)
            {
                resetAmmo1.Invoke(comp, new object[] { null });
            }
            else
            {
                resetAmmo0?.Invoke(comp, null);
            }
        }
    }
}
