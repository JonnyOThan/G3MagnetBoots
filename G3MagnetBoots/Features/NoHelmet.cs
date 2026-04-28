using System;
using HarmonyLib;
using UnityEngine;

namespace G3MagnetBoots
{
    [HarmonyPatch(typeof(KerbalEVA), "CheckHelmetOffSafe", new[] { typeof(bool), typeof(bool) })]
    internal static class Patch_KerbalEVA_CheckHelmetOffSafe
    {
        static bool Prefix(bool includeSafetyMargins, bool startEVAChecks, ref bool __result)
        {
            try
            {
                var cfg = G3MagnetBootsSettings.Current;
                if (cfg != null && cfg.allowHelmetOffInSpace)
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex);
            }
            return true;
        }
    }
}