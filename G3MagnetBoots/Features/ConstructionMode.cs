using System;
using HarmonyLib;
using UnityEngine;

namespace G3MagnetBoots
{
    [HarmonyPatch(typeof(KerbalEVA), "weld_OnEnter")]
    internal static class Patch_KerbalEVA_weld_OnEnter
    {
        static bool Prefix(KerbalEVA __instance)
        {
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            Logger.Debug($"HullTargetIsValid= {magBoots?.HullTargetIsValid}  Started from Hull= {magBoots?._constructionFromHull}  FSM State= {magBoots?.CurrentFSMStateName}");
            if (magBoots?.HullTargetIsValid != true) return true;
            if (!magBoots._constructionFromHull) return true;

            // Mirror stock weld_OnEnter but force grounded animation path
            __instance.Animations.weld.State.time = __instance.Animations.weld.start;
            __instance.Animations.weldSuspended.State.time =
                __instance.Animations.weldSuspended.start;

            // Mimic stock LoS check
            if (!KerbalEVAAccess.HasWeldLineOfSight(__instance))
            {
                __instance.fsm.RunEvent(__instance.On_weldComplete);
                return false;
            }

            // Force grounded animation regardless of SurfaceContact
            KerbalEVAAccess.Animation(__instance).CrossFade(
                __instance.Animations.weld, 0.2f, PlayMode.StopSameLayer);

            // Stock visor & effects
            bool wasVisor = __instance.VisorState == KerbalEVA.VisorStates.Lowered;
            KerbalEVAAccess.WasVisorEnabledBeforeWelding(__instance) = wasVisor;
            __instance.LowerVisor(forceHelmet: true);
            __instance.WeldFX?.Play();

            return false;
        }
    }

    [HarmonyPatch(typeof(KerbalEVA), "weld_acquireHeading_OnFixedUpdate")]
    internal static class Patch_KerbalEVA_weld_acquireHeading_OnFixedUpdate
    {
        static bool Prefix(KerbalEVA __instance)
        {
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            bool onHull = magBoots?.IsOnHull == true;
            return !onHull; // skip stock when on hull
        }
    }

    [HarmonyPatch(typeof(KerbalEVA), "weld_acquireHeading_OnLateUpdate")]
    internal static class Patch_KerbalEVA_weld_acquireHeading_OnLateUpdate
    {
        static bool Prefix(KerbalEVA __instance)
        {
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            bool onHull = magBoots?.IsOnHull == true;
            return !onHull;
        }
    }

    [HarmonyPatch(typeof(KerbalEVA), "weld_OnFixedUpdate")]
    internal static class Patch_KerbalEVA_weld_OnFixedUpdate
    {
        static bool Prefix(KerbalEVA __instance)
        {
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            if (magBoots?.IsOnHull != true) return true;

            // Run only the aim animation blend; skip rotation/movement stock logic
            var constructionTarget = KerbalEVAAccess.ConstructionTarget(__instance);
            if (constructionTarget == null) return false;

            float value = Vector3.SignedAngle(
                constructionTarget.transform.position - __instance.transform.position,
                __instance.transform.forward,
                __instance.transform.right);
            value = Mathf.Clamp(value, -20f, 45f);

            var anim = KerbalEVAAccess.Animation(__instance);
            if (value > 0f)
            {
                value /= 45f;
                anim.Blend(__instance.Animations.weld_aim_up, value, 0.15f);
                anim.Blend(__instance.Animations.weld_aim_down, 0f, 0.15f);
                anim.Blend(__instance.Animations.weld, 1f - value, 0.15f);
            }
            else
            {
                value /= 20f;
                anim.Blend(__instance.Animations.weld_aim_down, -value, 0.15f);
                anim.Blend(__instance.Animations.weld_aim_up, 0f, 0.15f);
                anim.Blend(__instance.Animations.weld, 1f + value, 0.15f);
            }

            __instance.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;
            return false;
        }
    }
}