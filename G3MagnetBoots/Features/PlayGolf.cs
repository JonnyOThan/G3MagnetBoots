using System;
using HarmonyLib;
using UnityEngine;

namespace G3MagnetBoots
{
    // block stock wingnut experiment while on a hull (shouldn't be possible but just in case)
    [HarmonyPatch(typeof(KerbalEVA), "Dzhanibekov", new[] { typeof(Callback) })]
    internal static class Patch_KerbalEVA_Dzhanibekov
    {
        static bool Prefix(KerbalEVA __instance)
        {
            if (__instance == null) return true;
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            if (magBoots?.IsOnHull == true) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(ModuleScienceExperiment), "ValidEVASituation")]
    internal static class Patch_ModuleScienceExperiment_ValidEVASituation
    {
        static ModuleScienceExperiment.EVASituation _removed;

        static void Prefix(ModuleScienceExperiment __instance)
        {
            _removed = null;
            if (__instance.experimentID != "evaScience") return;

            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            if (magBoots?.IsOnHull == true) return;

            var situations = __instance.evaSituations;
            if (situations == null) return;

            // Identify hull golf by PlayGolf action restricted to space. Stock golf uses situationMask = 1.
            _removed = situations.Find(s => s.KerbalAction == "PlayGolf" && (s.situationMask & 48u) != 0);
            if (_removed != null) situations.Remove(_removed);
        }

        static void Postfix(ModuleScienceExperiment __instance)
        {
            if (_removed == null) return;
            __instance.evaSituations?.Add(_removed);
            _removed = null;
        }
    }
}