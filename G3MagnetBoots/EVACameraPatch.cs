using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace G3MagnetBoots
{
    /// <summary>
    /// EVA LOCKED camera override for magnet boots.
    ///
    /// This keeps the same broad behavior as the original EVACameraPatch.cs:
    /// - stock FlightCamera owns pivot/orbit/zoom/FOV/terrain/CameraFX/TrackIR
    /// - stock LateUpdate runs first
    /// - this postfix only changes the camera frame of reference after stock updates it
    ///
    /// Important behavior:
    /// - camera UP is forced toward the Kerbal/hull up vector
    /// - hull/vessel rotation delta is inherited
    /// - Kerbal turning in place does not rotate the camera reference frame
    /// - walking around curved hulls uses parallel transport instead of rebuilding forward from a fixed hull-local vector
    ///
    /// Required ModuleG3MagnetBoots additions:
    ///     public Vector3 CameraLockedUp { get; }
    ///     public Transform CameraLockedReferenceTransform { get; }
    ///
    /// CameraLockedReferenceTransform should normally return the current hull part transform.
    /// CameraLockedUp should return the local-reconstructed smoothed hull normal transformed by that hull part.
    /// </summary>
    [HarmonyPatch(typeof(FlightCamera))]
    public static class Patch_FlightCamera_SetMode_EvaLocked
    {
        [HarmonyPrefix]
        [HarmonyPatch("setMode", new[] { typeof(FlightCamera.Modes) })]
        public static bool Prefix(FlightCamera __instance, ref FlightCamera.Modes __0)
        {
            bool isOnHull = TryGetBoots(out ModuleG3MagnetBoots boots) && boots.IsOnHull;

            if (__0 == FlightCamera.Modes.LOCKED && (!G3MagnetBoots.lockedCameraModeEnabled || !isOnHull))
            {
                __0 = FlightCamera.Modes.FREE;
                return true;
            }

            // Stock FlightCamera.LateUpdate force-kicks EVA LOCKED back to FREE.
            // Suppress only that internal transition.
            if (__instance.mode == FlightCamera.Modes.LOCKED &&
                __0 == FlightCamera.Modes.FREE &&
                Patch_FlightCamera_LateUpdate_EvaLocked.InLateUpdate)
            {
                return false;
            }

            // Unblocking LOCKED for EVA makes AUTO reachable in the enum cycle.
            // Preserve the previous stock-alike skip back to FREE.
            if (G3MagnetBoots.lockedCameraModeEnabled && isOnHull && __0 == FlightCamera.Modes.AUTO)
            {
                __0 = FlightCamera.Modes.FREE;
            }

            return true;
        }

        internal static bool TryGetBoots(out ModuleG3MagnetBoots boots)
        {
            boots = null;

            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || !v.isEVA || v.rootPart == null)
                return false;

            boots = v.rootPart.FindModuleImplementing<ModuleG3MagnetBoots>();
            return boots != null;
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
            AccessTools.Method(typeof(FlightCamera), "setMode", new[] { typeof(FlightCamera.Modes) });

        private static readonly FieldInfo FrameOfReference =
            AccessTools.Field(typeof(FlightCamera), "frameOfReference");

        internal static bool InLateUpdate;

        // Smoothed target FoR actually applied to FlightCamera.
        private static Quaternion _smoothedFrame = Quaternion.identity;
        private static bool _smoothedFrameValid = false;

        // Unsmooth transported target frame.
        // This is not rebuilt from Kerbal.forward and not rebuilt from a fixed projected hull vector.
        // It is carried forward frame-to-frame, with hull transform delta and minimal up correction.
        private static Quaternion _transportFrame = Quaternion.identity;
        private static bool _transportFrameValid = false;

        private static Vector3 _lastUp = Vector3.up;
        private static Transform _referenceTransform;
        private static Quaternion _lastReferenceRotation = Quaternion.identity;
        private static bool _lastReferenceRotationValid = false;

        private static float _hullBlend = 0f;
        private static int _framesOffHull = 0;

        // Tuning.
        private const float FRAME_SMOOTH_TAU_BASE = 0.14f;
        private const float FRAME_SMOOTH_TAU_FAST = 0.045f;
        private const float SNAP_ANGLE = 150f;
        private const float BLEND_IN_RATE = 3.5f;
        private const float BLEND_OUT_RATE = 2.5f;
        private const float ADAPTIVE_ANGLE_MIN = 2f;
        private const float ADAPTIVE_ANGLE_MAX = 45f;

        [HarmonyPrefix]
        private static void Prefix()
        {
            InLateUpdate = true;
        }

        [HarmonyPostfix]
        private static void Postfix(FlightCamera __instance)
        {
            try
            {
                if (UpdateFoR == null || FoRlerp == null || SetMode == null || FrameOfReference == null)
                    return;

                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null || !v.isEVA)
                {
                    ResetState();
                    return;
                }

                if (!G3MagnetBoots.lockedCameraModeEnabled)
                {
                    ResetState();
                    return;
                }

                float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.fixedDeltaTime;
                Quaternion curFoR = NormalizeSafe((Quaternion)FrameOfReference.GetValue(__instance));

                bool hasBoots = Patch_FlightCamera_SetMode_EvaLocked.TryGetBoots(out ModuleG3MagnetBoots boots);
                bool isOnHull = hasBoots && boots.IsOnHull;

                if (!isOnHull)
                {
                    _framesOffHull++;
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

                    // Blend out using the last valid frame.
                    if (__instance.mode != FlightCamera.Modes.LOCKED || _hullBlend <= 0f || !_smoothedFrameValid)
                        return;
                }
                else
                {
                    _framesOffHull = 0;

                    if (__instance.mode != FlightCamera.Modes.LOCKED)
                    {
                        ResetFrameCaptureOnly();
                        return;
                    }

                    _hullBlend = Mathf.MoveTowards(_hullBlend, 1f, BLEND_IN_RATE * dt);

                    Vector3 up = GetCameraUp(boots);
                    Transform referenceTransform = boots.CameraLockedReferenceTransform;

                    Quaternion desiredFrame;
                    if (!_transportFrameValid || referenceTransform != _referenceTransform)
                    {
                        desiredFrame = CaptureTransportFrame(__instance, curFoR, up, referenceTransform);
                    }
                    else
                    {
                        desiredFrame = UpdateTransportedFrame(up, referenceTransform);
                    }

                    UpdateSmoothedFrame(desiredFrame, dt);
                }

                if (!_smoothedFrameValid)
                    return;

                Quaternion delta = NormalizeSafe(_smoothedFrame * Quaternion.Inverse(curFoR));
                Quaternion scaledDelta = Quaternion.Slerp(Quaternion.identity, delta, _hullBlend);
                Quaternion newFoR = NormalizeSafe(scaledDelta * curFoR);

                float lerp = (float)FoRlerp.GetValue(__instance);
                UpdateFoR.Invoke(__instance, new object[] { newFoR, lerp });
            }
            finally
            {
                InLateUpdate = false;
            }
        }

        private static Vector3 GetCameraUp(ModuleG3MagnetBoots boots)
        {
            // Prefer CameraLockedUp. It is expected to be the render-current hull-local reconstructed normal.
            // Kerbal.transform.up is only a fallback, because it is physics-stepped and can add visual jitter.
            Vector3 up = boots.CameraLockedUp;

            if (up.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD && boots.Kerbal != null)
                up = boots.Kerbal.transform.up;

            if (up.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD)
                up = Vector3.up;

            return up.normalized;
        }

        private static Quaternion CaptureTransportFrame(FlightCamera cam, Quaternion curFoR, Vector3 up, Transform referenceTransform)
        {
            _referenceTransform = referenceTransform;

            Vector3 fwd = Vector3.ProjectOnPlane(curFoR * Vector3.forward, up);

            if (fwd.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD && cam != null && cam.GetPivot() != null)
                fwd = Vector3.ProjectOnPlane(cam.GetPivot().forward, up);

            if (fwd.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD && cam != null)
                fwd = Vector3.ProjectOnPlane(cam.transform.forward, up);

            if (fwd.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD && referenceTransform != null)
                fwd = Vector3.ProjectOnPlane(referenceTransform.forward, up);

            fwd = SafeTangentForward(fwd, up, referenceTransform);

            _transportFrame = NormalizeSafe(Quaternion.LookRotation(fwd, up));
            _transportFrameValid = true;
            _lastUp = up;

            _lastReferenceRotation = referenceTransform != null ? referenceTransform.rotation : Quaternion.identity;
            _lastReferenceRotationValid = referenceTransform != null;

            return _transportFrame;
        }

        private static Quaternion UpdateTransportedFrame(Vector3 up, Transform referenceTransform)
        {
            Vector3 previousFwd = _transportFrame * Vector3.forward;
            Vector3 previousUp = _lastUp;

            Quaternion refDelta = Quaternion.identity;
            if (referenceTransform != null && referenceTransform == _referenceTransform && _lastReferenceRotationValid)
            {
                Quaternion currentRefRotation = referenceTransform.rotation;
                refDelta = NormalizeSafe(currentRefRotation * Quaternion.Inverse(_lastReferenceRotation));
                _lastReferenceRotation = currentRefRotation;
            }
            else
            {
                _lastReferenceRotation = referenceTransform != null ? referenceTransform.rotation : Quaternion.identity;
                _lastReferenceRotationValid = referenceTransform != null;
            }

            // 1. Carry previous frame with actual hull transform rotation.
            Vector3 fwdAfterHullRotation = refDelta * previousFwd;
            Vector3 upAfterHullRotation = refDelta * previousUp;

            if (upAfterHullRotation.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD)
                upAfterHullRotation = previousUp;
            upAfterHullRotation.Normalize();

            // 2. Apply the minimum swing needed to align previous up to the current Kerbal/hull up.
            // This is the important piece for cylinders/spheres: it parallel-transports the camera frame
            // instead of re-projecting a fixed hull-local forward, which causes the 180-degree side swivel.
            Quaternion upSwing = Quaternion.FromToRotation(upAfterHullRotation, up);
            Vector3 fwd = upSwing * fwdAfterHullRotation;
            fwd = Vector3.ProjectOnPlane(fwd, up);
            fwd = SafeTangentForward(fwd, up, referenceTransform);

            _transportFrame = NormalizeSafe(Quaternion.LookRotation(fwd, up));
            _transportFrameValid = true;
            _lastUp = up;
            _referenceTransform = referenceTransform;

            return _transportFrame;
        }

        private static Vector3 SafeTangentForward(Vector3 fwd, Vector3 up, Transform referenceTransform)
        {
            fwd = Vector3.ProjectOnPlane(fwd, up);

            if (fwd.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD && _transportFrameValid)
                fwd = Vector3.ProjectOnPlane(_transportFrame * Vector3.forward, up);

            if (fwd.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD && referenceTransform != null)
                fwd = Vector3.ProjectOnPlane(referenceTransform.forward, up);

            if (fwd.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD)
                fwd = Vector3.Cross(up, Vector3.right);

            if (fwd.sqrMagnitude < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD)
                fwd = Vector3.Cross(up, Vector3.forward);

            return fwd.normalized;
        }

        private static void UpdateSmoothedFrame(Quaternion desiredFrame, float dt)
        {
            desiredFrame = NormalizeSafe(desiredFrame);

            if (!_smoothedFrameValid)
            {
                _smoothedFrame = desiredFrame;
                _smoothedFrameValid = true;
                return;
            }

            float angleDiff = Quaternion.Angle(_smoothedFrame, desiredFrame);

            if (angleDiff >= SNAP_ANGLE)
            {
                _smoothedFrame = desiredFrame;
                return;
            }

            float t = Mathf.InverseLerp(ADAPTIVE_ANGLE_MIN, ADAPTIVE_ANGLE_MAX, angleDiff);
            float tau = Mathf.Lerp(FRAME_SMOOTH_TAU_BASE, FRAME_SMOOTH_TAU_FAST, t);
            float alpha = 1f - Mathf.Exp(-dt / tau);
            _smoothedFrame = NormalizeSafe(Quaternion.Slerp(_smoothedFrame, desiredFrame, alpha));
        }

        private static Quaternion NormalizeSafe(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < ModuleG3MagnetBoots.VECTOR_ZERO_THRESHOLD)
                return Quaternion.identity;

            float inv = 1f / mag;
            q.x *= inv;
            q.y *= inv;
            q.z *= inv;
            q.w *= inv;
            return q;
        }

        private static void ResetFrameCaptureOnly()
        {
            _smoothedFrame = Quaternion.identity;
            _smoothedFrameValid = false;

            _transportFrame = Quaternion.identity;
            _transportFrameValid = false;

            _lastUp = Vector3.up;
            _referenceTransform = null;
            _lastReferenceRotation = Quaternion.identity;
            _lastReferenceRotationValid = false;
        }

        private static void ResetState()
        {
            ResetFrameCaptureOnly();
            _hullBlend = 0f;
            _framesOffHull = 0;
        }
    }
}
