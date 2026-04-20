using System;
using HarmonyLib;
using UnityEngine;

namespace G3MagnetBoots
{
    // Safety net: block stock Dzhanibekov(Callback) experiment if ever called while on a hull
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

    // Gate hull golf EVASITUATION (priority=0, situationMask=48, PlayGolf) so it only wins the ValidEVASituation selection when the kerbal is on a hull. When not on hull, the situation is temporarily removed from the list before ValidEVASituation runs so stock Dzhanibekov (priority=1, situationMask=48) wins as normal, then restored immediately after.
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

            // Identify hull golf by PlayGolf action restricted to space situations. The stock ground golf also uses PlayGolf but has situationMask = 1 (SrfLanded).
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