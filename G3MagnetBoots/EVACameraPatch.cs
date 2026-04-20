using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace G3MagnetBoots
{
    [HarmonyPatch(typeof(FlightCamera))]
    public static class Patch_FlightCamera_SetMode_EvaLocked
    {
        [HarmonyPrefix]
        [HarmonyPatch("setMode", new[] { typeof(FlightCamera.Modes) })]
        public static bool Prefix(FlightCamera __instance, ref FlightCamera.Modes __0)
        {
            bool IsOnHull = FlightGlobals.ActiveVessel?.rootPart?.FindModuleImplementing<ModuleG3MagnetBoots>()?.IsOnHull ?? false;

            if (__0 == FlightCamera.Modes.LOCKED && (!G3MagnetBoots.lockedCameraModeEnabled || !IsOnHull))
            {
                __0 = FlightCamera.Modes.FREE;
                return true;
            }

            if (__instance.mode == FlightCamera.Modes.LOCKED &&
                __0 == FlightCamera.Modes.FREE &&
                Patch_FlightCamera_LateUpdate_EvaLocked.InLateUpdate)
            {
                return false;
            }

            if (G3MagnetBoots.lockedCameraModeEnabled && IsOnHull && __0 == FlightCamera.Modes.AUTO) // unblocking LOCKED mode on EVA means the AUTO mode is also enabled by way of the next in the cycle, so we stock-alike skip it back to FREE
            {
                __0 = FlightCamera.Modes.FREE;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(FlightCamera), "LateUpdate")]
    internal static class Patch_FlightCamera_LateUpdate_EvaLocked
    {
        private static readonly MethodInfo UpdateFoR = AccessTools.Method(typeof(FlightCamera), "updateFoR");
        private static readonly FieldInfo FoRlerp = AccessTools.Field(typeof(FlightCamera), "FoRlerp");
        private static readonly MethodInfo SetMode = AccessTools.Method(typeof(FlightCamera), "setMode", new[] { typeof(FlightCamera.Modes) });
        private static readonly FieldInfo FrameOfReference = AccessTools.Field(typeof(FlightCamera), "frameOfReference");

        internal static bool InLateUpdate;
        private static int framesOffHull = 0;

        [HarmonyPrefix]
        private static void Prefix() => InLateUpdate = true;

        [HarmonyPostfix]
        private static void Postfix(FlightCamera __instance)
        {
            try
            {
                if (UpdateFoR == null || FoRlerp == null || SetMode == null || FrameOfReference == null) return; // guard against reflection failure which will never happen but harmony is scary like that

                var v = FlightGlobals.ActiveVessel;
                if (v == null || !v.isEVA) return;
                if (!G3MagnetBoots.lockedCameraModeEnabled) {
                    framesOffHull = 0;
                    return;
                }
                bool IsOnHull = v.rootPart?.FindModuleImplementing<ModuleG3MagnetBoots>()?.IsOnHull ?? false;

                framesOffHull = IsOnHull ? 0 : framesOffHull + 1;
                if (!IsOnHull && framesOffHull >= ModuleG3MagnetBoots.OFF_HULL_FRAMES_TO_UNLOCK &&
                    __instance.mode == FlightCamera.Modes.LOCKED)
                {
                    InLateUpdate = false;
                    SetMode.Invoke(__instance, new object[] { FlightCamera.Modes.FREE });
                    return;
                }

                if (!IsOnHull) return;
                if (__instance.mode != FlightCamera.Modes.LOCKED) return;

                // ---- UP-ONLY FoR adjustment ----
                var curFoR = (Quaternion)FrameOfReference.GetValue(__instance);

                Vector3 desiredUp = v.transform.up.normalized; // only thing taken from the kerbal
                Vector3 curUp = (curFoR * Vector3.up).normalized;

                float dot = Vector3.Dot(curUp, desiredUp);

                Quaternion swing;
                if (dot > ModuleG3MagnetBoots.QUAT_DOT_NEARLY_SAME)
                {
                    swing = Quaternion.identity;
                }
                else if (dot < ModuleG3MagnetBoots.QUAT_DOT_OPPOSITE)
                {
                    // 180° flip: pick a stable axis using current FoR forward/right, projected onto the desired up plane
                    Vector3 axis = Vector3.ProjectOnPlane(curFoR * Vector3.forward, desiredUp);
                    if (axis.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD)
                        axis = Vector3.ProjectOnPlane(curFoR * Vector3.right, desiredUp);
                    axis.Normalize();
                    swing = Quaternion.AngleAxis(ModuleG3MagnetBoots.QUAT_FLIP_ANGLE, axis);
                }
                else
                {
                    swing = Quaternion.FromToRotation(curUp, desiredUp);
                }

                Quaternion newFoR = swing * curFoR;
                var lerp = (float)FoRlerp.GetValue(__instance);

                UpdateFoR.Invoke( __instance, new object[] { newFoR, lerp });
            }
            finally { InLateUpdate = false; }
        }
    }


}