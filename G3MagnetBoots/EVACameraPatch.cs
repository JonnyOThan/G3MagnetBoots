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
        private static readonly MethodInfo UpdateFoR =
            AccessTools.Method(typeof(FlightCamera), "updateFoR");
        private static readonly FieldInfo FoRlerp =
            AccessTools.Field(typeof(FlightCamera), "FoRlerp");
        private static readonly MethodInfo SetMode =
            AccessTools.Method(typeof(FlightCamera), "setMode",
                new[] { typeof(FlightCamera.Modes) });
        private static readonly FieldInfo FrameOfReference =
            AccessTools.Field(typeof(FlightCamera), "frameOfReference");

        internal static bool InLateUpdate;

        // Smoothing state — persists across frames
        private static Vector3 _smoothedUp = Vector3.up;
        private static bool _smoothedUpValid = false;
        private static float _hullBlend = 0f;   // 0=fully unblended, 1=fully tracking
        private static int _framesOffHull = 0;

        // Tune these if needed:
        // UP_SMOOTH_TAU_BASE  — time constant for small jitter (seconds). Higher = smoother but more lag.
        // UP_SMOOTH_TAU_FAST  — time constant used at large angle diffs (stays snappy through hull rolls).
        // SNAP_ANGLE          — above this angle, lerp hard toward the raw value (instant snap).
        private const float UP_SMOOTH_TAU_BASE = 0.10f;
        private const float UP_SMOOTH_TAU_FAST = 0.018f;
        private const float SNAP_ANGLE = 120f;   // degrees
        private const float BLEND_IN_RATE = 3.5f;   // units/sec  (~0.29s to fully enter)
        private const float BLEND_OUT_RATE = 2.5f;   // units/sec  (~0.40s to fully exit)

        [HarmonyPrefix]
        private static void Prefix() => InLateUpdate = true;

        [HarmonyPostfix]
        private static void Postfix(FlightCamera __instance)
        {
            try
            {
                if (UpdateFoR == null || FoRlerp == null || SetMode == null || FrameOfReference == null)
                    return;

                var v = FlightGlobals.ActiveVessel;
                if (v == null || !v.isEVA) return;

                if (!G3MagnetBoots.lockedCameraModeEnabled)
                {
                    ResetState();
                    return;
                }

                bool isOnHull = v.rootPart?
                    .FindModuleImplementing<ModuleG3MagnetBoots>()?
                    .IsOnHull ?? false;

                float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.fixedDeltaTime;

                // --- Off-hull logic ---
                if (!isOnHull)
                {
                    _framesOffHull++;

                    // Blend OUT — fade the correction toward zero before killing the mode
                    _hullBlend = Mathf.MoveTowards(_hullBlend, 0f, BLEND_OUT_RATE * dt);

                    if (_hullBlend <= 0f && _framesOffHull >= ModuleG3MagnetBoots.OFF_HULL_FRAMES_TO_UNLOCK)
                    {
                        if (__instance.mode == FlightCamera.Modes.LOCKED)
                        {
                            InLateUpdate = false;
                            SetMode.Invoke(__instance, new object[] { FlightCamera.Modes.FREE });
                        }
                        ResetState();
                        return;
                    }

                    // Still blending out: keep running the FoR correction at reduced strength
                    if (__instance.mode != FlightCamera.Modes.LOCKED || _hullBlend <= 0f)
                        return;

                    // Fall through to the FoR adjustment below with the current _smoothedUp
                    // (don't update _smoothedUp while off-hull — let it coast)
                }
                else
                {
                    // --- On-hull logic ---
                    _framesOffHull = 0;

                    if (__instance.mode != FlightCamera.Modes.LOCKED)
                        return;

                    // Blend IN
                    _hullBlend = Mathf.MoveTowards(_hullBlend, 1f, BLEND_IN_RATE * dt);

                    // --- Angle-adaptive EMA smoothing of desiredUp ---
                    Vector3 rawUp = v.transform.up.normalized;

                    if (!_smoothedUpValid || _smoothedUp.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD)
                    {
                        _smoothedUp = rawUp;
                        _smoothedUpValid = true;
                        // Don't return — still run the FoR update on first frame
                    }
                    else
                    {
                        float angleDiff = Vector3.Angle(_smoothedUp, rawUp);

                        if (angleDiff >= SNAP_ANGLE)
                        {
                            // Large rotation (e.g. walking over a corner): snap immediately
                            _smoothedUp = rawUp;
                        }
                        else
                        {
                            // Adaptive tau: tightens as angle grows so big deliberate rotations
                            // stay snappy while small physics jitter is heavily damped.
                            float t = Mathf.InverseLerp(2f, 45f, angleDiff); // 0 at <2°, 1 at >45°
                            float tau = Mathf.Lerp(UP_SMOOTH_TAU_BASE, UP_SMOOTH_TAU_FAST, t);
                            float alpha = 1f - Mathf.Exp(-dt / tau);
                            _smoothedUp = Vector3.Slerp(_smoothedUp, rawUp, alpha).normalized;
                        }
                    }
                }

                // --- UP-ONLY FoR adjustment (shared by blend-in and blend-out paths) ---
                var curFoR = (Quaternion)FrameOfReference.GetValue(__instance);
                Vector3 curUp = (curFoR * Vector3.up).normalized;
                float dot = Vector3.Dot(curUp, _smoothedUp);

                Quaternion swing;
                if (dot > ModuleG3MagnetBoots.QUAT_DOT_NEARLY_SAME)
                {
                    swing = Quaternion.identity;
                }
                else if (dot < ModuleG3MagnetBoots.QUAT_DOT_OPPOSITE)
                {
                    Vector3 axis = Vector3.ProjectOnPlane(curFoR * Vector3.forward, _smoothedUp);
                    if (axis.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD)
                        axis = Vector3.ProjectOnPlane(curFoR * Vector3.right, _smoothedUp);
                    axis.Normalize();
                    swing = Quaternion.AngleAxis(ModuleG3MagnetBoots.QUAT_FLIP_ANGLE, axis);
                }
                else
                {
                    swing = Quaternion.FromToRotation(curUp, _smoothedUp);
                }

                // Scale the correction by the blend weight — entry/exit fade for free
                Quaternion scaledSwing = Quaternion.Slerp(Quaternion.identity, swing, _hullBlend);
                Quaternion newFoR = scaledSwing * curFoR;

                var lerp = (float)FoRlerp.GetValue(__instance);
                UpdateFoR.Invoke(__instance, new object[] { newFoR, lerp });
            }
            finally { InLateUpdate = false; }
        }

        private static void ResetState()
        {
            _smoothedUp = Vector3.up;
            _smoothedUpValid = false;
            _hullBlend = 0f;
            _framesOffHull = 0;
        }
    }


}