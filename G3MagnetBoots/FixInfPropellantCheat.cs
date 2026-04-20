using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace G3MagnetBoots
{
    [HarmonyPatch(typeof(KerbalEVA), "UpdatePackFuel")]
    internal static class KerbalEVA_UpdatePackFuel_InfiniteRefillPatch
    {
        private static FieldInfo FiPropellantResource => AccessTools.Field(typeof(KerbalEVA), "propellantResource");
        private static FieldInfo FiInventoryPropellantResources => AccessTools.Field(typeof(KerbalEVA), "inventoryPropellantResources");

        [HarmonyPostfix]
        private static void Postfix(KerbalEVA __instance)
        {
            if (!CheatOptions.InfinitePropellant) return;
            if (__instance?.part?.Resources == null) return;

            object prop = FiPropellantResource?.GetValue(__instance);
            if (prop is PartResourceDefinition def)
            {
                var pr = __instance.part.Resources.Get(def.id) ?? __instance.part.Resources[def.name];
                if (pr != null) pr.amount = pr.maxAmount;
            }
            else if (prop is PartResource pr2)
            {
                pr2.amount = pr2.maxAmount;
            }
            else
            {
                // fallback
                var pr = __instance.part.Resources["EVA Propellant"];
                if (pr != null) pr.amount = pr.maxAmount;
            }

            var invObj = FiInventoryPropellantResources?.GetValue(__instance);
            if (invObj is not IList inv) return;

            for (int i = 0; i < inv.Count; i++)
            {
                var entry = inv[i];
                if (entry == null) continue;

                var snap = AccessTools.Field(entry.GetType(), "pPResourceSnapshot")?.GetValue(entry);
                if (snap == null) continue;

                var amountF = AccessTools.Field(snap.GetType(), "amount");
                var maxF = AccessTools.Field(snap.GetType(), "maxAmount");
                if (amountF != null && maxF != null)
                    amountF.SetValue(snap, maxF.GetValue(snap));

                AccessTools.Method(snap.GetType(), "UpdateConfigNodeAmounts")?.Invoke(snap, null);
            }
        }
    }
}