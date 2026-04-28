using System;
using HarmonyLib;
using UnityEngine;

namespace G3MagnetBoots
{
    [KSPAddon(KSPAddon.Startup.Instantly, false)]
    internal sealed class HarmonyLoader : MonoBehaviour
    {
        private void Awake()
        {
            try
            {
                Harmony h2 = new("EVAMagBoots");
                h2.PatchAll(typeof(HarmonyLoader).Assembly);
                AccessTools.Method(typeof(KerbalEVA), "SetupFSM");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex);
            }
        }
    }

    // Patch into KerbalEVA.cs SetupFSM method to initialize custom hull states and events
    [HarmonyPatch(typeof(KerbalEVA), "SetupFSM")]
    internal static class Patch_KerbalEVA_SetupFSM
    {
        static void Postfix(KerbalEVA __instance)
        {
            if (__instance == null) return;
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            magBoots?.HookIntoEva(__instance);

            BlockStockConstructionMovementEvents(__instance);
        }

        private static void BlockStockConstructionMovementEvents(KerbalEVA eva)
        {
            WrapMoveEvent(eva, eva.On_MoveLowG_Acd);
            WrapMoveEvent(eva, eva.On_MoveLowG_fps);

            // on st_enteringConstruction and st_exitingConstruction.
            WrapMoveEvent(eva, eva.On_MoveAcd);
            WrapMoveEvent(eva, eva.On_MoveFPS);
        }

        private static void WrapMoveEvent(KerbalEVA eva, KFSMEvent evt)
        {
            if (eva == null || evt == null || evt.OnCheckCondition == null)
                return;

            var original = evt.OnCheckCondition;

            evt.OnCheckCondition = st =>
            {
                try
                {
                    var boots = eva.part?.FindModuleImplementing<ModuleG3MagnetBoots>();

                    if (boots != null && (boots.IsOnHull || boots._constructionFromHull))
                    {
                        // Only suppress while stock construction transition states are active.
                        // This avoids disabling normal EVA movement elsewhere.
                        if (eva.fsm?.CurrentState == eva.st_enteringConstruction ||
                            eva.fsm?.CurrentState == eva.st_exitingConstruction)
                        {
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex);
                }

                return original(st);
            };
        }
    }

    [HarmonyPatch(typeof(KerbalEVA), "HandleMovementInput")]
    internal static class Patch_KerbalEVA_HandleMovementInput
    {
        static void Postfix(KerbalEVA __instance)
        {
            if (__instance == null) return;
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            if (magBoots == null) return;

            // Suppress movement during construction mode and welding.
            if (magBoots._constructionFromHull && (__instance.fsm?.CurrentState == __instance.st_enteringConstruction || __instance.fsm?.CurrentState == __instance.st_exitingConstruction || __instance.fsm?.CurrentState == __instance.st_weld) || EVAConstructionModeController.MovementRestricted)
            {
                KerbalEVAAccess.TgtRpos(__instance) = Vector3.zero;
            }

            // Velocity matching.
            Vector3 playerInput = KerbalEVAAccess.PackTgtRPos(__instance);
            Vector3 contribution = magBoots.GetVelocityMatchContribution(playerInput);
            if (contribution != Vector3.zero)
            {
                KerbalEVAAccess.PackTgtRPos(__instance) += contribution;
            }
        }
    }

    [HarmonyPatch(typeof(KerbalEVA), "SurfaceContact")]
    internal static class Patch_KerbalEVA_SurfaceContact
    {
        static bool Prefix(KerbalEVA __instance, ref bool __result)
        {
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();

            // Suppress stock SurfaceContact when on hull.
            if (magBoots != null && (magBoots.IsOnHull || magBoots._constructionFromHull || magBoots._weldStartedFromHull))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}